using InfrastructureAssemblyMarker = HomeBusinessAssistant.Infrastructure.AssemblyMarker;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class AssemblyMarkerTests
{
    [Test]
    public void MarkerIdentifiesPlatformInfrastructureAssembly()
    {
        string? assemblyName = typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name;

        Assert.That(assemblyName, Is.EqualTo("HomeBusinessAssistant.Infrastructure"));
    }
}
