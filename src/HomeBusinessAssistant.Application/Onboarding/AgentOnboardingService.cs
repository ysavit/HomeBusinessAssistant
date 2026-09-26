using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>Stage 19 pending-agent, selection, generic configuration, secret, and re-entry orchestration.</summary>
public sealed class AgentOnboardingService(
    IOnboardingRepository onboarding,
    IOnboardingAgentSelectionRepository selections,
    IAgentDefinitionRepository definitions,
    IAgentConfigurationService configurations,
    IInstalledAgentPackageInspector packages,
    IAgentOnboardingAdapterResolver adapters,
    ISecretStore secrets,
    IAuditWriter audit,
    TimeProvider timeProvider) : IAgentOnboardingService
{
    /// <inheritdoc />
    public async ValueTask<AgentOnboardingDashboardStatus> GetDashboardStatusAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AgentOnboardingCard> cards = await BuildCardsAsync(
            sessionId: null,
            reconcileUnavailable: false,
            cancellationToken).ConfigureAwait(false);
        AgentId[] pending = cards
            .Where(item => item.Package.IsAvailable
                && item.IsPending
                && item.Selection?.SelectionStatus != OnboardingAgentSelectionStatus.Deferred)
            .Select(item => item.Definition.Id)
            .ToArray();
        return new(pending.Length, pending);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> StartAddAgentsAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        Guid correlationId = Guid.NewGuid();
        OnboardingSession session = await selections.StartAgentSelectionSessionAsync(
            OnboardingSessionKind.AddAgents,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            "onboarding.add-agents-opened",
            session.Id,
            actorId,
            correlationId,
            new { session.Kind, session.CurrentStep },
            AuditOutcome.Succeeded,
            cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> StartReconfigureAgentAsync(
        AgentId agentId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        AgentDefinitionRecord definition = await definitions.GetAsync(agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The installed agent was not found.");
        InstalledAgentPackageInspection package = await packages.InspectAsync(definition, cancellationToken).ConfigureAwait(false);
        IAgentOnboardingAdapter? adapter = adapters.Resolve(agentId, package.ConfigurationSchema);
        if (!package.IsAvailable || adapter is null)
        {
            throw new InvalidOperationException("This agent cannot start a safe onboarding workflow.");
        }

        Guid correlationId = Guid.NewGuid();
        OnboardingSession session = await selections.StartAgentSelectionSessionAsync(
            OnboardingSessionKind.ReconfigureAgent,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (session.CurrentStep == OnboardingSteps.Readiness)
        {
            throw new InvalidOperationException("Complete the active platform readiness step before configuring an agent.");
        }

        AgentConfigurationRecord? configuration = await adapter.LoadConfigurationAsync(agentId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<OnboardingAgentSelection> current = await selections.GetSelectionsAsync(session.Id, cancellationToken).ConfigureAwait(false);
        OnboardingAgentSelection? existing = current.SingleOrDefault(item => item.AgentId == agentId);
        var choice = new SaveOnboardingAgentChoice(
            agentId,
            OnboardingAgentSelectionStatus.Selected,
            existing?.ProgressStatus == OnboardingAgentProgressStatus.ReadyForValidation
                ? OnboardingAgentProgressStatus.Configuring
                : existing?.ProgressStatus ?? OnboardingAgentProgressStatus.Configuring,
            adapter.Descriptor.AdapterId,
            adapter.Descriptor.Steps[0].Key,
            package.InstalledVersion,
            package.ConfigurationSchemaVersion,
            configuration?.CurrentRevisionId,
            configuration?.CurrentRevision.ConfigurationHash,
            "onboarding.reconfiguration-requested");
        SaveOnboardingAgentChoicesResult saved = await selections.SaveChoicesAsync(
            session.Id,
            session.Revision,
            [choice],
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            "onboarding.reconfiguration-opened",
            session.Id,
            actorId,
            correlationId,
            new { agentId = agentId.Value, adapter = adapter.Descriptor.AdapterId, preservesExistingState = true },
            AuditOutcome.Succeeded,
            cancellationToken).ConfigureAwait(false);
        return saved.Session;
    }

    /// <inheritdoc />
    public async ValueTask<AgentSelectionOverview> GetSelectionOverviewAsync(
        Guid sessionId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        OnboardingSession session = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AgentOnboardingCard> cards = await BuildCardsAsync(
            sessionId,
            reconcileUnavailable: true,
            cancellationToken).ConfigureAwait(false);
        session = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return CreateOverview(session, cards);
    }

    /// <inheritdoc />
    public async ValueTask<AgentSelectionOverview> SaveChoicesAsync(
        Guid sessionId,
        long expectedSessionRevision,
        IReadOnlySet<AgentId> selectedAgentIds,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedAgentIds);
        ValidateActor(actorId);
        OnboardingSession session = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session.Status != OnboardingSessionStatus.InProgress)
        {
            throw new InvalidOperationException("Resume the onboarding session before changing agent choices.");
        }

        IReadOnlyList<AgentOnboardingCard> cards = await BuildCardsAsync(
            sessionId,
            reconcileUnavailable: true,
            cancellationToken).ConfigureAwait(false);
        HashSet<AgentId> availableIds = cards
            .Where(item => item.Package.IsAvailable && item.Support != AgentOnboardingSupport.TypedAdapterRequired)
            .Select(item => item.Definition.Id)
            .ToHashSet();
        if (!selectedAgentIds.IsSubsetOf(availableIds))
        {
            throw new InvalidOperationException("One or more selected agents are unavailable or require a typed adapter.");
        }

        SaveOnboardingAgentChoice[] choices = cards
            .Where(item => item.Package.IsAvailable && item.Support != AgentOnboardingSupport.TypedAdapterRequired)
            .Select(item =>
            {
                bool selected = selectedAgentIds.Contains(item.Definition.Id);
                IAgentOnboardingAdapter adapter = adapters.Resolve(item.Definition.Id, item.Package.ConfigurationSchema)!;
                return new SaveOnboardingAgentChoice(
                    item.Definition.Id,
                    selected ? OnboardingAgentSelectionStatus.Selected : OnboardingAgentSelectionStatus.Deferred,
                    selected
                        ? item.Selection?.ProgressStatus is OnboardingAgentProgressStatus.ReadyForValidation
                            ? OnboardingAgentProgressStatus.ReadyForValidation
                            : OnboardingAgentProgressStatus.NotStarted
                        : OnboardingAgentProgressStatus.Skipped,
                    adapter.Descriptor.AdapterId,
                    selected ? adapter.Descriptor.Steps[0].Key : "agent.deferred",
                    item.Package.InstalledVersion,
                    item.Package.ConfigurationSchemaVersion,
                    null,
                    item.Selection?.StartingConfigurationHash,
                    selected ? "onboarding.agent-selected" : "onboarding.agent-deferred");
            })
            .ToArray();

        // Starting identities come from the current authoritative configuration, not from posted form data.
        for (int index = 0; index < choices.Length; index++)
        {
            AgentOnboardingCard card = cards.Single(item => item.Definition.Id == choices[index].AgentId);
            IAgentOnboardingAdapter adapter = adapters.Resolve(card.Definition.Id, card.Package.ConfigurationSchema)!;
            AgentConfigurationRecord? current = await adapter.LoadConfigurationAsync(card.Definition.Id, cancellationToken).ConfigureAwait(false);
            choices[index] = choices[index] with
            {
                StartingConfigurationRevisionId = card.Selection?.StartingConfigurationRevisionId ?? current?.CurrentRevisionId,
                StartingConfigurationHash = card.Selection?.StartingConfigurationHash ?? current?.CurrentRevision.ConfigurationHash,
            };
        }

        Guid correlationId = Guid.NewGuid();
        SaveOnboardingAgentChoicesResult result = await selections.SaveChoicesAsync(
            sessionId,
            expectedSessionRevision,
            choices,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        foreach (AgentId changed in result.ChangedAgentIds)
        {
            SaveOnboardingAgentChoice choice = choices.Single(item => item.AgentId == changed);
            await WriteAuditAsync(
                choice.SelectionStatus == OnboardingAgentSelectionStatus.Selected
                    ? "onboarding.agent-selected"
                    : "onboarding.agent-deferred",
                sessionId,
                actorId,
                correlationId,
                new
                {
                    agentId = changed.Value,
                    selection = choice.SelectionStatus.ToString(),
                    adapter = choice.AdapterId,
                    reason = choice.ReasonCode,
                },
                AuditOutcome.Succeeded,
                cancellationToken).ConfigureAwait(false);
        }

        if (selectedAgentIds.Count == 0)
        {
            result = result with
            {
                Session = await onboarding.TransitionAsync(new(
                    sessionId,
                    result.Session.Revision,
                    OnboardingSessionStatus.Deferred,
                    result.Session.CurrentStep,
                    actorId,
                    correlationId,
                    result.Session.AcknowledgedWarningCount,
                    IncrementCompletedStepCount: true), cancellationToken).ConfigureAwait(false),
            };
            await WriteAuditAsync(
                "onboarding.all-agents-deferred",
                sessionId,
                actorId,
                correlationId,
                new { availableAgents = choices.Length },
                AuditOutcome.Succeeded,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<AgentOnboardingCard> refreshed = await BuildCardsAsync(
            sessionId,
            reconcileUnavailable: false,
            cancellationToken).ConfigureAwait(false);
        return CreateOverview(result.Session, refreshed);
    }

    /// <inheritdoc />
    public async ValueTask<GenericAgentOnboardingEditor> GetGenericEditorAsync(
        Guid sessionId,
        AgentId agentId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        AgentSelectionOverview overview = await GetSelectionOverviewAsync(sessionId, actorId, cancellationToken).ConfigureAwait(false);
        if (overview.Session.Status != OnboardingSessionStatus.InProgress)
        {
            throw new InvalidOperationException("Resume the onboarding session before configuring an agent.");
        }

        AgentOnboardingCard card = overview.Agents.SingleOrDefault(item => item.Definition.Id == agentId)
            ?? throw new InvalidOperationException("The agent is not part of this installed registry.");
        OnboardingAgentSelection selection = card.Selection
            ?? throw new InvalidOperationException("Select this agent before opening its configuration step.");
        if (selection.SelectionStatus != OnboardingAgentSelectionStatus.Selected
            || card.Support != AgentOnboardingSupport.SafeGeneric
            || card.Package.ConfigurationSchema is null)
        {
            throw new InvalidOperationException("This agent does not have an active safe generic configuration step.");
        }

        IAgentOnboardingAdapter adapter = adapters.Resolve(agentId, card.Package.ConfigurationSchema)!;
        AgentConfigurationRecord? configuration = await adapter.LoadConfigurationAsync(agentId, cancellationToken).ConfigureAwait(false);
        JsonElement document = configuration is null
            ? card.Package.ConfigurationSchema.CreateDefaultDocument()
            : ParseConfiguration(configuration.CurrentRevision.CanonicalConfigurationJson);
        return new(
            overview,
            card,
            selection,
            card.Package.ConfigurationSchema,
            configuration,
            GenericConfigurationFormCodec.ReadValues(card.Package.ConfigurationSchema, document),
            card.Assessment?.Secrets ?? []);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> SaveGenericConfigurationAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        JsonElement configuration,
        long? expectedConfigurationRevision,
        string? expectedConfigurationHash,
        string changeSummary,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        GenericAgentOnboardingEditor editor = await GetGenericEditorAsync(sessionId, agentId, actorId, cancellationToken).ConfigureAwait(false);
        if (editor.Selection.Revision != expectedSelectionRevision)
        {
            throw new OnboardingConcurrencyException();
        }

        IAgentOnboardingAdapter adapter = adapters.Resolve(agentId, editor.Schema)!;
        Guid correlationId = Guid.NewGuid();
        SaveConfigurationResult saved;
        try
        {
            saved = await adapter.SaveConfigurationAsync(new(
                agentId,
                editor.Schema.SchemaVersion,
                configuration,
                actorId,
                string.IsNullOrWhiteSpace(changeSummary) ? "Reviewed during onboarding." : changeSummary,
                correlationId,
                expectedConfigurationRevision,
                expectedConfigurationHash), cancellationToken).ConfigureAwait(false);
        }
        catch (ConfigurationConcurrencyException)
        {
            await WriteAuditAsync(
                "onboarding.generic-configuration-stale-rejected",
                sessionId,
                actorId,
                correlationId,
                new { agentId = agentId.Value, reason = "configuration.stale" },
                AuditOutcome.Failed,
                cancellationToken).ConfigureAwait(false);
            throw;
        }

        AgentOnboardingAssessment assessment = await adapter.AssessAsync(new(
            editor.Agent.Definition,
            editor.Agent.Package,
            await adapter.LoadConfigurationAsync(agentId, cancellationToken).ConfigureAwait(false),
            editor.Selection), cancellationToken).ConfigureAwait(false);
        OnboardingAgentSelection updated = await UpdateAfterAssessmentAsync(
            editor.Selection,
            editor.Agent.Package,
            adapter,
            assessment,
            saved.Revision,
            expectedSelectionRevision,
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            saved.CurrentChanged
                ? "onboarding.generic-configuration-saved"
                : "onboarding.generic-configuration-reviewed-noop",
            sessionId,
            actorId,
            correlationId,
            new
            {
                agentId = agentId.Value,
                revisionId = saved.Revision.Id,
                saved.Revision.RevisionNumber,
                configurationHash = saved.Revision.ConfigurationHash,
                saved.Created,
                saved.CurrentChanged,
                progress = updated.ProgressStatus.ToString(),
            },
            AuditOutcome.Succeeded,
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> SetGenericSecretAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string fieldKey,
        string secretValue,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretValue) || secretValue.Length > 65_536)
        {
            throw new ArgumentException("A bounded non-empty protected value is required.", nameof(secretValue));
        }

        return await MutateSecretAsync(
            sessionId,
            agentId,
            expectedSelectionRevision,
            fieldKey,
            actorId,
            setValue: secretValue,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<OnboardingAgentSelection> DeleteGenericSecretAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string fieldKey,
        string actorId,
        CancellationToken cancellationToken = default) => MutateSecretAsync(
            sessionId,
            agentId,
            expectedSelectionRevision,
            fieldKey,
            actorId,
            setValue: null,
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> ConfigureLaterAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        GenericAgentOnboardingEditor editor = await GetGenericEditorAsync(sessionId, agentId, actorId, cancellationToken).ConfigureAwait(false);
        Guid correlationId = Guid.NewGuid();
        OnboardingAgentSelection updated = await selections.UpdateProgressAsync(new(
            sessionId,
            agentId,
            expectedSelectionRevision,
            OnboardingAgentSelectionStatus.Deferred,
            OnboardingAgentProgressStatus.Skipped,
            editor.Selection.AdapterId,
            "agent.deferred",
            editor.Selection.ReviewedManifestVersion,
            editor.Selection.ReviewedConfigurationSchemaVersion,
            editor.Selection.SavedConfigurationRevisionId,
            editor.Selection.SavedConfigurationHash,
            "onboarding.agent-deferred",
            timeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            "onboarding.agent-deferred",
            sessionId,
            actorId,
            correlationId,
            new { agentId = agentId.Value, fromConfiguration = true },
            AuditOutcome.Succeeded,
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async ValueTask<OnboardingAgentSelection> MutateSecretAsync(
        Guid sessionId,
        AgentId agentId,
        long expectedSelectionRevision,
        string fieldKey,
        string actorId,
        string? setValue,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorId);
        GenericAgentOnboardingEditor editor = await GetGenericEditorAsync(sessionId, agentId, actorId, cancellationToken).ConfigureAwait(false);
        if (editor.Selection.Revision != expectedSelectionRevision)
        {
            throw new OnboardingConcurrencyException();
        }

        AgentOnboardingSecretStatus secret = editor.Secrets.SingleOrDefault(item => item.FieldKey == fieldKey)
            ?? throw new InvalidOperationException("The requested protected field is not declared by the installed schema.");
        if (setValue is null)
        {
            _ = await secrets.DeleteAsync(secret.Reference, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await secrets.SetAsync(secret.Reference, setValue, cancellationToken).ConfigureAwait(false);
        }

        IAgentOnboardingAdapter adapter = adapters.Resolve(agentId, editor.Schema)!;
        AgentConfigurationRecord? current = await adapter.LoadConfigurationAsync(agentId, cancellationToken).ConfigureAwait(false);
        AgentOnboardingAssessment assessment = await adapter.AssessAsync(new(
            editor.Agent.Definition,
            editor.Agent.Package,
            current,
            editor.Selection), cancellationToken).ConfigureAwait(false);
        OnboardingAgentSelection updated = await UpdateAfterAssessmentAsync(
            editor.Selection,
            editor.Agent.Package,
            adapter,
            assessment,
            current?.CurrentRevision,
            expectedSelectionRevision,
            cancellationToken).ConfigureAwait(false);
        Guid correlationId = Guid.NewGuid();
        await WriteAuditAsync(
            setValue is null ? "onboarding.protected-secret-deleted" : "onboarding.protected-secret-set",
            sessionId,
            actorId,
            correlationId,
            new
            {
                agentId = agentId.Value,
                fieldKey,
                status = assessment.Secrets.Single(item => item.FieldKey == fieldKey).ReasonCode,
                progress = updated.ProgressStatus.ToString(),
            },
            AuditOutcome.Succeeded,
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async ValueTask<OnboardingAgentSelection> UpdateAfterAssessmentAsync(
        OnboardingAgentSelection selection,
        InstalledAgentPackageInspection package,
        IAgentOnboardingAdapter adapter,
        AgentOnboardingAssessment assessment,
        ConfigurationRevisionRecord? revision,
        long expectedSelectionRevision,
        CancellationToken cancellationToken)
    {
        OnboardingAgentProgressStatus progress = assessment.ReadyForValidation
            ? OnboardingAgentProgressStatus.ReadyForValidation
            : OnboardingAgentProgressStatus.NeedsAttention;
        return await selections.UpdateProgressAsync(new(
            selection.SessionId,
            selection.AgentId,
            expectedSelectionRevision,
            OnboardingAgentSelectionStatus.Selected,
            progress,
            adapter.Descriptor.AdapterId,
            assessment.ReadyForValidation ? "generic.review" : "generic.secrets",
            package.InstalledVersion,
            package.ConfigurationSchemaVersion,
            revision?.Id,
            revision?.ConfigurationHash,
            assessment.ReasonCode,
            CompletedAtUtc: null), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<AgentOnboardingCard>> BuildCardsAsync(
        Guid? sessionId,
        bool reconcileUnavailable,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentDefinitionRecord> installed = await definitions.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<AgentId, OnboardingAgentSelection> latest = await selections.GetLatestSelectionsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<OnboardingAgentSelection> currentSelections = sessionId.HasValue
            ? await selections.GetSelectionsAsync(sessionId.Value, cancellationToken).ConfigureAwait(false)
            : [];
        var currentById = currentSelections.ToDictionary(item => item.AgentId);
        var cards = new List<AgentOnboardingCard>(installed.Count);
        var unavailable = new Dictionary<AgentId, bool>();
        foreach (AgentDefinitionRecord definition in installed.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            InstalledAgentPackageInspection package = await packages.InspectAsync(definition, cancellationToken).ConfigureAwait(false);
            OnboardingAgentSelection? currentSelection = currentById.GetValueOrDefault(definition.Id);
            OnboardingAgentSelection? reviewSelection = currentSelection
                ?? latest.GetValueOrDefault(definition.Id);
            if (sessionId.HasValue && currentById.ContainsKey(definition.Id) && !package.IsAvailable)
            {
                unavailable[definition.Id] = package.IsRemoved;
            }

            IAgentOnboardingAdapter? adapter = adapters.Resolve(definition.Id, package.ConfigurationSchema);
            AgentOnboardingSupport support = adapter?.Descriptor.Support
                ?? AgentOnboardingSupport.TypedAdapterRequired;
            AgentConfigurationRecord? configuration = adapter is null
                ? await configurations.GetCurrentAsync(definition.Id, cancellationToken).ConfigureAwait(false)
                : await adapter.LoadConfigurationAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            AgentOnboardingAssessment? assessment = package.IsAvailable && adapter is not null
                ? await adapter.AssessAsync(new(definition, package, configuration, reviewSelection), cancellationToken).ConfigureAwait(false)
                : null;
            (bool pending, string reason, string message) = DerivePending(package, support, configuration, reviewSelection, assessment);
            OnboardingAgentSelection? displayedSelection = sessionId.HasValue
                ? currentSelection
                : reviewSelection;
            cards.Add(new(
                definition,
                package,
                support,
                pending,
                assessment?.IsConfigured ?? false,
                assessment?.ReadyForValidation ?? false,
                reason,
                message,
                displayedSelection,
                assessment));
        }

        if (sessionId.HasValue && reconcileUnavailable && unavailable.Count > 0)
        {
            IReadOnlyList<AgentId> changed = await selections.ReconcileUnavailableAsync(
                sessionId.Value,
                unavailable,
                cancellationToken).ConfigureAwait(false);
            foreach (AgentId agentId in changed)
            {
                await WriteAuditAsync(
                    "onboarding.agent-became-unavailable",
                    sessionId.Value,
                    "system",
                    Guid.NewGuid(),
                    new
                    {
                        agentId = agentId.Value,
                        reason = unavailable[agentId] ? "agent.package-removed" : "agent.package-unavailable",
                        historyRetained = true,
                    },
                    AuditOutcome.Succeeded,
                    cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<OnboardingAgentSelection> reconciled = await selections.GetSelectionsAsync(sessionId.Value, cancellationToken).ConfigureAwait(false);
            Dictionary<AgentId, OnboardingAgentSelection> reconciledById = reconciled.ToDictionary(item => item.AgentId);
            cards = cards.Select(card => reconciledById.TryGetValue(card.Definition.Id, out OnboardingAgentSelection? value)
                ? card with { Selection = value }
                : card).ToList();
        }

        return cards;
    }

    private static (bool Pending, string Reason, string Message) DerivePending(
        InstalledAgentPackageInspection package,
        AgentOnboardingSupport support,
        AgentConfigurationRecord? configuration,
        OnboardingAgentSelection? selection,
        AgentOnboardingAssessment? assessment)
    {
        if (!package.IsAvailable)
        {
            return (true, package.ReasonCode, package.Message);
        }

        if (support == AgentOnboardingSupport.TypedAdapterRequired)
        {
            return (true, "onboarding.typed-adapter-required", "Custom setup adapter required. The agent remains disabled.");
        }

        if (selection?.SelectionStatus == OnboardingAgentSelectionStatus.Deferred)
        {
            return (false, "onboarding.agent-deferred", "The owner chose to configure this agent later.");
        }

        if (configuration is null || assessment?.IsConfigured != true)
        {
            return (true, assessment?.ReasonCode ?? "onboarding.configuration-missing", assessment?.Message ?? "A valid current configuration is required.");
        }

        if (assessment.RequiresReview)
        {
            return (true, assessment.ReasonCode, assessment.Message);
        }

        if (!string.Equals(selection?.ReviewedConfigurationSchemaVersion, package.ConfigurationSchemaVersion, StringComparison.Ordinal))
        {
            return (true, "onboarding.configuration-schema-review-required", "The installed configuration schema generation has not been reviewed.");
        }

        return assessment.ReadyForValidation
            ? (false, "onboarding.agent-reviewed", "The current configuration was reviewed and is ready for later validation.")
            : (true, assessment.ReasonCode, assessment.Message);
    }

    private static AgentSelectionOverview CreateOverview(
        OnboardingSession session,
        IReadOnlyList<AgentOnboardingCard> cards) => new(
        session,
        cards,
        cards.Count(item => item.Package.IsAvailable),
        cards.Count(item => item.Package.IsAvailable && item.IsPending),
        cards.Count(item => item.Selection?.SelectionStatus == OnboardingAgentSelectionStatus.Selected),
        cards.Count(item => item.Selection?.SelectionStatus == OnboardingAgentSelectionStatus.Deferred));

    private async ValueTask<OnboardingSession> RequireSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await onboarding.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The onboarding session was not found.");

    private async ValueTask WriteAuditAsync(
        string action,
        Guid sessionId,
        string actorId,
        Guid correlationId,
        object data,
        AuditOutcome outcome,
        CancellationToken cancellationToken)
    {
        _ = await audit.WriteAsync(new(
            actorId == "system" ? AuditActorType.System : AuditActorType.User,
            actorId,
            action,
            "onboarding-session",
            sessionId.ToString("D"),
            outcome,
            correlationId,
            null,
            JsonSerializer.SerializeToElement(data)), cancellationToken).ConfigureAwait(false);
    }

    private static JsonElement ParseConfiguration(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void ValidateActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128)
        {
            throw new ArgumentException("A bounded onboarding actor is required.", nameof(actorId));
        }
    }
}
