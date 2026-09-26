using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Operations;

namespace HomeBusinessAssistant.Host.Orchestration;

/// <summary>Projects only current durable onboarding blockers into the existing attention detector.</summary>
public sealed class OnboardingOperationalSignalSource(
    IOnboardingRepository repository,
    TimeProvider timeProvider) : IOperationalSignalSource
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OperationalSignal>> ReadSignalsAsync(
        CancellationToken cancellationToken = default)
    {
        OnboardingSession? session = await repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null || session.Status != OnboardingSessionStatus.InProgress)
        {
            return [];
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        IReadOnlyList<OnboardingCheckRecord> checks = await repository.GetChecksAsync(session.Id, cancellationToken).ConfigureAwait(false);
        return checks
            .Where(item => item.Status == OnboardingCheckStatus.Blocked
                && (item.ExpiresAtUtc is null || item.ExpiresAtUtc > nowUtc))
            .Take(50)
            .Select(item => new OperationalSignal(
                "onboarding",
                AttentionSeverity.Error,
                item.Title,
                item.Message,
                item.AgentId?.Value,
                RunId: null,
                ScheduleId: null,
                $"onboarding:{item.Scope}:{item.CheckKey}:{item.AgentId?.Value ?? "platform"}",
                $"/Onboarding?sessionId={session.Id:D}",
                new
                {
                    item.CheckKey,
                    scope = item.Scope.ToString(),
                    item.ReasonCode,
                    sessionId = session.Id,
                }))
            .ToArray();
    }
}

/// <summary>Combines bounded operational sources so one detector owns dedupe and resolution.</summary>
public sealed class CompositeOperationalSignalSource(
    IReadOnlyList<IOperationalSignalSource> sources) : IOperationalSignalSource
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OperationalSignal>> ReadSignalsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<OperationalSignal>();
        foreach (IOperationalSignalSource source in sources)
        {
            IReadOnlyList<OperationalSignal> items = await source.ReadSignalsAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(items.Take(Math.Max(0, 200 - results.Count)));
            if (results.Count >= 200)
            {
                break;
            }
        }

        return results;
    }
}

