using AgentSdkAssemblyMarker = HomeBusinessAssistant.AgentSdk.AssemblyMarker;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AssemblyMarkerTests
{
    [Test]
    public void MarkerIdentifiesAgentSdkAssembly()
    {
        string? assemblyName = typeof(AgentSdkAssemblyMarker).Assembly.GetName().Name;

        Assert.That(assemblyName, Is.EqualTo("HomeBusinessAssistant.AgentSdk"));
    }
}
