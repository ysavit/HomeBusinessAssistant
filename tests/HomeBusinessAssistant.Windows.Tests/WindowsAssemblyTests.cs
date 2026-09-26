using WindowsAssemblyMarker = HomeBusinessAssistant.Windows.AssemblyMarker;

namespace HomeBusinessAssistant.Windows.Tests;

internal sealed class WindowsAssemblyTests
{
    [Test]
    public void WindowsMarkerLoadsOnSupportedRuntime()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OperatingSystem.IsWindows(), Is.True);
            Assert.That(
                typeof(WindowsAssemblyMarker).Assembly.GetName().Name,
                Is.EqualTo("HomeBusinessAssistant.Windows"));
        });
    }
}
