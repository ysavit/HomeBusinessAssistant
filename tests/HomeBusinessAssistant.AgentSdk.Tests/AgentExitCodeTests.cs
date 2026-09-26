using HomeBusinessAssistant.AgentSdk.Execution;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AgentExitCodeTests
{
    [Test]
    public void ExitCodeMappingRemainsStable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AgentExitCode.Success, Is.Zero);
            Assert.That(AgentExitCode.InvalidArguments, Is.EqualTo(2));
            Assert.That(AgentExitCode.InvalidConfiguration, Is.EqualTo(3));
            Assert.That(AgentExitCode.AuthenticationRequired, Is.EqualTo(10));
            Assert.That(AgentExitCode.Throttled, Is.EqualTo(11));
            Assert.That(AgentExitCode.ChallengeDetected, Is.EqualTo(12));
            Assert.That(AgentExitCode.Cancelled, Is.EqualTo(20));
            Assert.That(AgentExitCode.TransientFailure, Is.EqualTo(30));
            Assert.That(AgentExitCode.PermanentFailure, Is.EqualTo(40));
            Assert.That(AgentExitCode.UnhandledFailure, Is.EqualTo(70));
            Assert.That(AgentExitCode.IsDefined(99), Is.False);
        });
    }
}
