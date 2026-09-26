using DomainAssemblyMarker = HomeBusinessAssistant.Domain.AssemblyMarker;

namespace HomeBusinessAssistant.Domain.Tests;

internal sealed class AssemblyMarkerTests
{
    [Test]
    public void MarkerIdentifiesDomainAssembly()
    {
        string? assemblyName = typeof(DomainAssemblyMarker).Assembly.GetName().Name;

        Assert.That(assemblyName, Is.EqualTo("HomeBusinessAssistant.Domain"));
    }
}
