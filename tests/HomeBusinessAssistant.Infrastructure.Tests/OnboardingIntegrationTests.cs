using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class OnboardingIntegrationTests
{
    private static readonly string[] ExpectedCheckKeys = ["required.local", "optional.caveat", "persistence.protected-storage"];

    [Test]
    public async Task FreshInitializationIsUniqueAndDeferredSessionResumesWithStaleRevisionProtection()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);

        Task<EnsureInitialOnboardingResult>[] racing = Enumerable.Range(0, 4)
            .Select(index => repository.EnsureInitialAsync(
                establishedInstallation: false,
                $"local-web-{index}",
                Guid.NewGuid()).AsTask())
            .ToArray();
        EnsureInitialOnboardingResult[] results = await Task.WhenAll(racing);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(item => item.Created), Is.EqualTo(1));
            Assert.That(results.Select(item => item.Session.Id).Distinct().Count(), Is.EqualTo(1));
            Assert.That(results[0].Session.Status, Is.EqualTo(OnboardingSessionStatus.InProgress));
            Assert.That(results[0].Session.CurrentStep, Is.EqualTo(OnboardingSteps.Readiness));
        });

        OnboardingSession current = await repository.GetCurrentAsync()
            ?? throw new InvalidOperationException("Initial session is missing.");
        OnboardingSession deferred = await repository.TransitionAsync(new(
            current.Id,
            current.Revision,
            OnboardingSessionStatus.Deferred,
            current.CurrentStep,
            "local-web",
            Guid.NewGuid(),
            AcknowledgedWarningCount: 0,
            IncrementCompletedStepCount: false));
        Assert.That(deferred.Status, Is.EqualTo(OnboardingSessionStatus.Deferred));
        Assert.That(deferred.DeferredAtUtc, Is.Not.Null);

        Assert.That(async () => await repository.TransitionAsync(new(
            current.Id,
            current.Revision,
            OnboardingSessionStatus.InProgress,
            current.CurrentStep,
            "local-web",
            Guid.NewGuid(),
            AcknowledgedWarningCount: 0,
            IncrementCompletedStepCount: false)),
            Throws.TypeOf<OnboardingConcurrencyException>());
        Assert.That(async () => await repository.TransitionAsync(new(
            deferred.Id,
            deferred.Revision,
            OnboardingSessionStatus.Completed,
            deferred.CurrentStep,
            "local-web",
            Guid.NewGuid(),
            AcknowledgedWarningCount: 0,
            IncrementCompletedStepCount: false)),
            Throws.TypeOf<InvalidOperationException>());

        OnboardingSession resumed = await repository.TransitionAsync(new(
            deferred.Id,
            deferred.Revision,
            OnboardingSessionStatus.InProgress,
            deferred.CurrentStep,
            "local-web",
            Guid.NewGuid(),
            AcknowledgedWarningCount: 0,
            IncrementCompletedStepCount: false));
        Assert.Multiple(() =>
        {
            Assert.That(resumed.Status, Is.EqualTo(OnboardingSessionStatus.InProgress));
            Assert.That(resumed.DeferredAtUtc, Is.Null);
            Assert.That(resumed.Revision, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task PopulatedStage17DatabaseUpgradesWithoutLosingConfigurationAndBackfillsLegacySession()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreatePopulatedStage17Async();
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var service = new OnboardingService(
            repository,
            new OnboardingReadinessRunner([], temporary.TimeProvider),
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);

        OnboardingEntryDecision decision = await service.GetEntryDecisionAsync("local-web");

        await using var context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM AgentConfigurations), (SELECT COUNT(*) FROM OnboardingSessions), (SELECT COUNT(*) FROM OnboardingAgentSelections);";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(applied[^1], Is.EqualTo("20260901204157_AddFounderScoutOnboardingAuthentication"));
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetInt32(1), Is.EqualTo(1));
            Assert.That(reader.GetInt32(2), Is.Zero);
            Assert.That(decision.RedirectToOnboarding, Is.False);
            Assert.That(decision.Session.Status, Is.EqualTo(OnboardingSessionStatus.Completed));
            Assert.That(decision.Session.CurrentStep, Is.EqualTo(OnboardingSteps.LegacyInstallation));
        });
    }

    [Test]
    public async Task AgentChoicesAreIdempotentDurableStaleSafeAndRetainHistoryWhenPackageIsRemoved()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        EnsureInitialOnboardingResult initialized = await repository.EnsureInitialAsync(
            establishedInstallation: false,
            "local-web",
            Guid.NewGuid());
        OnboardingSession selectionStep = await repository.TransitionAsync(new(
            initialized.Session.Id,
            initialized.Session.Revision,
            OnboardingSessionStatus.InProgress,
            OnboardingSteps.AgentSelectionPending,
            "local-web",
            Guid.NewGuid(),
            AcknowledgedWarningCount: 0,
            IncrementCompletedStepCount: true));
        AgentId selectedAgent = AgentId.Parse("wake-remote");
        AgentId founderScout = AgentId.Parse("founder-scout");
        SaveOnboardingAgentChoice[] choices =
        [
            Choice(selectedAgent, OnboardingAgentSelectionStatus.Selected, "wake-remote.pending"),
            Choice(founderScout, OnboardingAgentSelectionStatus.Deferred, "founder-scout.pending"),
        ];

        SaveOnboardingAgentChoicesResult saved = await repository.SaveChoicesAsync(
            selectionStep.Id,
            selectionStep.Revision,
            choices,
            "local-web",
            Guid.NewGuid());
        SaveOnboardingAgentChoicesResult noOp = await repository.SaveChoicesAsync(
            saved.Session.Id,
            saved.Session.Revision,
            choices,
            "local-web",
            Guid.NewGuid());

        Assert.That(async () => await repository.SaveChoicesAsync(
            selectionStep.Id,
            selectionStep.Revision,
            choices,
            "local-web",
            Guid.NewGuid()), Throws.TypeOf<OnboardingConcurrencyException>());

        var restartedRepository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        IReadOnlyList<OnboardingAgentSelection> afterRestart = await restartedRepository.GetSelectionsAsync(saved.Session.Id);
        IReadOnlyList<AgentId> reconciled = await restartedRepository.ReconcileUnavailableAsync(
            saved.Session.Id,
            new Dictionary<AgentId, bool> { [selectedAgent] = true });
        IReadOnlyList<OnboardingAgentSelection> afterRemoval = await restartedRepository.GetSelectionsAsync(saved.Session.Id);
        OnboardingAgentSelection removed = afterRemoval.Single(item => item.AgentId == selectedAgent);

        Assert.Multiple(() =>
        {
            Assert.That(saved.ChangedAgentIds, Is.EquivalentTo(new[] { selectedAgent, founderScout }));
            Assert.That(saved.Session.CurrentStep, Is.EqualTo(OnboardingSteps.AgentConfiguration));
            Assert.That(noOp.ChangedAgentIds, Is.Empty);
            Assert.That(noOp.Session.Revision, Is.EqualTo(saved.Session.Revision));
            Assert.That(afterRestart.Single(item => item.AgentId == selectedAgent).SelectionStatus,
                Is.EqualTo(OnboardingAgentSelectionStatus.Selected));
            Assert.That(afterRestart.Single(item => item.AgentId == founderScout).SelectionStatus,
                Is.EqualTo(OnboardingAgentSelectionStatus.Deferred));
            Assert.That(reconciled, Is.EqualTo(new[] { selectedAgent }));
            Assert.That(removed.SelectionStatus, Is.EqualTo(OnboardingAgentSelectionStatus.Removed));
            Assert.That(removed.ProgressStatus, Is.EqualTo(OnboardingAgentProgressStatus.NeedsAttention));
            Assert.That(removed.CreatedAtUtc, Is.EqualTo(afterRestart.Single(item => item.AgentId == selectedAgent).CreatedAtUtc));
        });
    }

    [Test]
    public async Task ScannerSeededDefaultConfigurationDoesNotSuppressFreshInstallOnboarding()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        _ = await configurations.SaveAsync(new(
            AgentId.Parse("founder-scout"),
            "1.0",
            JsonSerializer.Deserialize<JsonElement>("{\"schemaVersion\":\"1.0\"}"),
            "agent-registry-scanner",
            "Scanner-seeded default",
            Guid.NewGuid()));
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var service = new OnboardingService(
            repository,
            new OnboardingReadinessRunner([], temporary.TimeProvider),
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);

        OnboardingEntryDecision decision = await service.GetEntryDecisionAsync("local-web");

        Assert.Multiple(() =>
        {
            Assert.That(decision.RedirectToOnboarding, Is.True);
            Assert.That(decision.Session.Kind, Is.EqualTo(OnboardingSessionKind.FirstRun));
            Assert.That(decision.Session.CurrentStep, Is.EqualTo(OnboardingSteps.Readiness));
        });
    }

    [Test]
    public async Task EstablishedInstallationIsBackfilledWithoutForcedEntryAndCanStartOptionalReview()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        _ = await configurations.SaveAsync(new(
            AgentId.Parse("founder-scout"),
            "1.0",
            JsonSerializer.Deserialize<JsonElement>("{\"schemaVersion\":\"1.0\"}"),
            "local-web",
            "Existing owner configuration",
            Guid.NewGuid()));
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var service = new OnboardingService(
            repository,
            new OnboardingReadinessRunner([], temporary.TimeProvider),
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);

        OnboardingEntryDecision decision = await service.GetEntryDecisionAsync("local-web");
        OnboardingSession review = await service.StartReadinessReviewAsync("local-web");
        OnboardingSession cancelled = await service.CancelAsync(review.Id, review.Revision, "local-web");

        await using var context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT group_concat(Action, '|') FROM AuditEvents WHERE TargetType = 'onboarding-session';";
        string auditActions = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(decision.RedirectToOnboarding, Is.False);
            Assert.That(decision.ShowReviewInvitation, Is.True);
            Assert.That(decision.Session.Status, Is.EqualTo(OnboardingSessionStatus.Completed));
            Assert.That(decision.Session.CurrentStep, Is.EqualTo(OnboardingSteps.LegacyInstallation));
            Assert.That(review.Kind, Is.EqualTo(OnboardingSessionKind.ReconfigureAgent));
            Assert.That(review.Status, Is.EqualTo(OnboardingSessionStatus.InProgress));
            Assert.That(cancelled.Status, Is.EqualTo(OnboardingSessionStatus.Cancelled));
            Assert.That(auditActions, Does.Contain("onboarding.completed"));
            Assert.That(auditActions, Does.Contain("onboarding.cancelled"));
        });
    }

    [Test]
    public async Task LatestChecksReplaceInPlaceAndReadinessRequiresWarningAcknowledgement()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var runner = new OnboardingReadinessRunner(
        [
            new StaticCheck("required.local", required: true, explicitAction: false, OnboardingCheckStatus.Passed),
            new StaticCheck("optional.caveat", required: false, explicitAction: false, OnboardingCheckStatus.Warning),
            new StaticCheck("persistence.protected-storage", required: true, explicitAction: true, OnboardingCheckStatus.Passed),
        ], temporary.TimeProvider);
        var service = new OnboardingService(repository, runner, audit, temporary.TimeProvider);
        OnboardingEntryDecision entry = await service.GetEntryDecisionAsync("local-web");

        OnboardingOverview ordinary = await service.RunChecksAsync(entry.Session.Id, entry.Session.Revision, "local-web");
        OnboardingOverview explicitResult = await service.RunExplicitProbesAsync(
            entry.Session.Id,
            ordinary.Session.Revision,
            "local-web");
        OnboardingOverview rechecked = await service.RunChecksAsync(
            entry.Session.Id,
            explicitResult.Session.Revision,
            "local-web");
        Assert.Multiple(() =>
        {
            Assert.That(rechecked.Checks, Has.Count.EqualTo(3));
            Assert.That(rechecked.WarningCount, Is.EqualTo(1));
            Assert.That(rechecked.UnknownRequiredCount, Is.Zero);
            Assert.That(rechecked.CanContinue, Is.True);
        });

        Assert.That(async () => await service.ContinueAsync(
            entry.Session.Id,
            rechecked.Session.Revision,
            acknowledgeWarnings: false,
            "local-web"), Throws.InvalidOperationException);
        OnboardingSession continued = await service.ContinueAsync(
            entry.Session.Id,
            rechecked.Session.Revision,
            acknowledgeWarnings: true,
            "local-web");
        Assert.Multiple(() =>
        {
            Assert.That(continued.CurrentStep, Is.EqualTo(OnboardingSteps.AgentSelectionPending));
            Assert.That(continued.Status, Is.EqualTo(OnboardingSessionStatus.InProgress));
            Assert.That(continued.AcknowledgedWarningCount, Is.EqualTo(1));
            Assert.That(continued.CompletedStepCount, Is.EqualTo(1));
        });

        IReadOnlyList<OnboardingCheckRecord> checks = await repository.GetChecksAsync(entry.Session.Id);
        Assert.That(checks.Select(item => item.CheckKey), Is.EquivalentTo(ExpectedCheckKeys));

        await using var context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM AgentSchedules) + (SELECT COUNT(*) FROM AgentRuns);";
        int mutationCount = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = "SELECT group_concat(Action, '|') FROM AuditEvents WHERE TargetType = 'onboarding-session';";
        string auditActions = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Multiple(() =>
        {
            Assert.That(mutationCount, Is.Zero);
            Assert.That(auditActions, Does.Contain("onboarding.readiness-requested"));
            Assert.That(auditActions, Does.Contain("onboarding.protected-storage-probe-succeeded"));
            Assert.That(auditActions, Does.Contain("onboarding.warning-acknowledged"));
        });
    }

    [Test]
    public async Task ExpiredRequiredResultReturnsToUnknownUntilItIsRechecked()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var repository = new OnboardingRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var service = new OnboardingService(
            repository,
            new OnboardingReadinessRunner([new ExpiringCheck(temporary.TimeProvider)], temporary.TimeProvider),
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);
        OnboardingEntryDecision entry = await service.GetEntryDecisionAsync("local-web");
        OnboardingOverview checkedOverview = await service.RunChecksAsync(
            entry.Session.Id,
            entry.Session.Revision,
            "local-web");

        temporary.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        OnboardingOverview expired = await service.GetOverviewAsync(entry.Session.Id, "local-web");

        Assert.Multiple(() =>
        {
            Assert.That(checkedOverview.CanContinue, Is.True);
            Assert.That(expired.CanContinue, Is.False);
            Assert.That(expired.UnknownRequiredCount, Is.EqualTo(1));
            Assert.That(expired.Checks.Single().Status, Is.EqualTo(OnboardingCheckStatus.Unknown));
            Assert.That(expired.Checks.Single().ReasonCode, Is.EqualTo("readiness.result-expired"));
        });
    }

    private sealed class StaticCheck(
        string key,
        bool required,
        bool explicitAction,
        OnboardingCheckStatus status) : IOnboardingReadinessCheck
    {
        public OnboardingReadinessCheckDefinition Definition { get; } = new(
            key,
            OnboardingCheckScope.Platform,
            key,
            $"Description for {key}.",
            required,
            explicitAction,
            TimeSpan.FromSeconds(1));

        public ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new OnboardingReadinessCheckResult(
                Definition,
                status,
                status == OnboardingCheckStatus.Warning ? "test.warning" : "test.passed",
                status == OnboardingCheckStatus.Warning ? "A safe test warning." : "The test check passed.",
                new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                ExpiresAtUtc: null,
                "1.0",
                JsonSerializer.SerializeToElement(new { safe = true })));
        }
    }

    private static SaveOnboardingAgentChoice Choice(
        AgentId agentId,
        OnboardingAgentSelectionStatus status,
        string adapterId) => new(
            agentId,
            status,
            status == OnboardingAgentSelectionStatus.Selected
                ? OnboardingAgentProgressStatus.NotStarted
                : OnboardingAgentProgressStatus.Skipped,
            adapterId,
            status == OnboardingAgentSelectionStatus.Selected ? "agent.selected" : "agent.deferred",
            "1.0",
            "1.0",
            StartingConfigurationRevisionId: null,
            StartingConfigurationHash: null,
            status == OnboardingAgentSelectionStatus.Selected ? "onboarding.agent-selected" : "onboarding.agent-deferred");

    private sealed class ExpiringCheck(TimeProvider timeProvider) : IOnboardingReadinessCheck
    {
        public OnboardingReadinessCheckDefinition Definition { get; } = new(
            "test.expiring",
            OnboardingCheckScope.Platform,
            "Expiring readiness",
            "A required check with a bounded evidence lifetime.",
            Required: true,
            RequiresExplicitAction: false,
            TimeSpan.FromSeconds(1));

        public ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset observedAtUtc = timeProvider.GetUtcNow();
            return ValueTask.FromResult(new OnboardingReadinessCheckResult(
                Definition,
                OnboardingCheckStatus.Passed,
                "test.ready",
                "The bounded check passed.",
                observedAtUtc,
                observedAtUtc.AddMinutes(1),
                "1.0",
                JsonSerializer.SerializeToElement(new { safe = true })));
        }
    }
}
