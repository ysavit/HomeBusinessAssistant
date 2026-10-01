using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Host.Pages.FounderScout;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class FounderScoutStartPageTests
{
    [Test]
    public async Task StartDuringActiveAnalysisShowsConflictInsteadOfThrowing()
    {
        var commands = new Mock<IManagementCommandService>();
        commands.Setup(item => item.RunNowAsync(It.IsAny<ManagementManualRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<OccurrenceDispatchResult>(Task.FromException<OccurrenceDispatchResult>(
                new InvalidOperationException("The agent already has active work and its concurrency policy forbids another occurrence."))));
        var model = new IndexModel(new(Commands: commands.Object))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };
        model.TempData = new TempDataDictionary(model.HttpContext, new Mock<ITempDataProvider>().Object);

        _ = await model.OnPostStartAsync(CancellationToken.None);

        Assert.That(model.TempData["FlashMessage"], Does.Contain("already running"));
    }
}
