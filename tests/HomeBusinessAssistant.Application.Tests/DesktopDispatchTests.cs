using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using Moq;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class DesktopDispatchTests
{
    [Test]
    public async Task RunnerLaunchFailureTerminallyMarksAStillReadyOccurrenceAndAudits()
    {
        ScheduleOccurrenceRecord occurrence = CreateOccurrence(OccurrenceStatus.Ready);
        var occurrences = new Mock<IOccurrenceRepository>(MockBehavior.Strict);
        occurrences.Setup(item => item.GetAsync(occurrence.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(occurrence);
        occurrences.Setup(item => item.TryTransitionAsync(
                It.Is<OccurrenceTransitionRequest>(request =>
                    request.OccurrenceId == occurrence.Id
                    && request.TargetStatus == OccurrenceStatus.Failed
                    && request.ReasonCode == "runner.path-missing"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(occurrence with { Status = OccurrenceStatus.Failed });
        var runner = new Mock<IRunnerProcessLauncher>(MockBehavior.Strict);
        runner.Setup(item => item.LaunchAsync(occurrence.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunnerProcessLaunchResult(false, null, "runner.path-missing"));
        var audit = new Mock<IAuditWriter>(MockBehavior.Strict);
        audit.Setup(item => item.WriteAsync(
                It.Is<WriteAuditEventRequest>(request =>
                    request.Action == "runner.launch-failed"
                    && request.Outcome == AuditOutcome.Failed),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAudit());
        var dispatcher = new OccurrenceRunnerDispatcher(
            occurrences.Object,
            runner.Object,
            audit.Object,
            TimeProvider.System);

        OccurrenceDispatchResult result = await dispatcher.DispatchAsync(
            occurrence.Id,
            "tray",
            Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.RunnerStarted, Is.False);
            Assert.That(result.Code, Is.EqualTo("runner.path-missing"));
        });
        occurrences.VerifyAll();
        runner.VerifyAll();
        audit.VerifyAll();
    }

    [Test]
    public async Task PlannedOccurrenceRemainsQueuedAndDoesNotStartRunner()
    {
        ScheduleOccurrenceRecord occurrence = CreateOccurrence(OccurrenceStatus.Planned);
        var occurrences = new Mock<IOccurrenceRepository>(MockBehavior.Strict);
        occurrences.Setup(item => item.GetAsync(occurrence.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(occurrence);
        var dispatcher = new OccurrenceRunnerDispatcher(
            occurrences.Object,
            Mock.Of<IRunnerProcessLauncher>(MockBehavior.Strict),
            Mock.Of<IAuditWriter>(MockBehavior.Strict),
            TimeProvider.System);

        OccurrenceDispatchResult result = await dispatcher.DispatchAsync(
            occurrence.Id,
            "tray",
            Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Queued, Is.True);
            Assert.That(result.RunnerStarted, Is.False);
            Assert.That(result.Code, Is.EqualTo("occurrence.queued"));
        });
        occurrences.VerifyAll();
    }

    private static ScheduleOccurrenceRecord CreateOccurrence(OccurrenceStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(
            OccurrenceId.New(),
            ScheduleId: null,
            AgentId.Parse("wake-remote"),
            "diagnose",
            "{}",
            Guid.NewGuid(),
            now,
            TriggerType.TrayMenu,
            status,
            RequiresWake: false,
            KeepSystemAwake: false,
            KeepDisplayOn: false,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            ClaimedBy: null,
            ClaimedAtUtc: null,
            ClaimExpiresAtUtc: null,
            StartedAtUtc: null,
            CompletedAtUtc: null,
            CancellationRequestedAtUtc: null,
            CancellationReason: null,
            TerminalReasonCode: null,
            TerminalMessage: null,
            RunId: null,
            now,
            now);
    }

    private static AuditEventRecord CreateAudit() => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        AuditActorType.User,
        "tray",
        "runner.launch-failed",
        "occurrence",
        Guid.NewGuid().ToString("D"),
        AuditOutcome.Failed,
        Guid.NewGuid(),
        RunId: null,
        "{}");
}
