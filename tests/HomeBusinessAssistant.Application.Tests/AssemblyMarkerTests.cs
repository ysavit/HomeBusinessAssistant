using ApplicationAssemblyMarker = HomeBusinessAssistant.Application.AssemblyMarker;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class AssemblyMarkerTests
{
    [Test]
    public void MarkerIdentifiesApplicationAssembly()
    {
        string? assemblyName = typeof(ApplicationAssemblyMarker).Assembly.GetName().Name;

        Assert.That(assemblyName, Is.EqualTo("HomeBusinessAssistant.Application"));
    }
}
