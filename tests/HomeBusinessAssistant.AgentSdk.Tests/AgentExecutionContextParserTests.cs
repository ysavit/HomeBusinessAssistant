using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AgentExecutionContextParserTests
{
    [Test]
    public void ParseCreatesTypedContextAndNormalizesPaths()
    {
        string[] arguments = CreateValidArguments();

        AgentExecutionContextParseResult result = AgentExecutionContextParser.Parse(arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(AgentExitCode.Success));
            Assert.That(result.Context?.CommandName, Is.EqualTo("run"));
            Assert.That(result.Context?.AgentId, Is.EqualTo(AgentId.Parse("founder-scout")));
            Assert.That(result.Context?.ProtocolVersion, Is.EqualTo(AgentProtocolVersion.Current));
            Assert.That(Path.IsPathFullyQualified(result.Context!.ConfigurationFilePath), Is.True);
            Assert.That(Path.IsPathFullyQualified(result.Context.DataDirectory), Is.True);
            Assert.That(Path.IsPathFullyQualified(result.Context.ArtifactDirectory), Is.True);
        });
    }

    [Test]
    public void ParseRejectsMissingAndDuplicateRequiredOptions()
    {
        string[] arguments = [.. CreateValidArguments(), "--run-id", Guid.NewGuid().ToString("D")];
        arguments = arguments.Where(argument => argument != "--artifact-directory" && argument != "artifacts").ToArray();

        AgentExecutionContextParseResult result = AgentExecutionContextParser.Parse(arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(AgentExitCode.InvalidArguments));
            Assert.That(result.Errors.Select(error => error.Code), Does.Contain("arguments.duplicateOption"));
            Assert.That(result.Errors.Select(error => error.Argument), Does.Contain("--artifact-directory"));
        });
    }

    [Test]
    public void ParseRejectsUnknownProtocolMajor()
    {
        string[] arguments = CreateValidArguments();
        arguments[14] = "2.0";

        AgentExecutionContextParseResult result = AgentExecutionContextParser.Parse(arguments);

        Assert.That(result.Errors.Select(error => error.Code), Does.Contain("arguments.unsupportedProtocolMajor"));
    }

    [Test]
    public void ParseRejectsBadGuidAndMissingOptionValue()
    {
        string[] badGuidArguments = CreateValidArguments();
        badGuidArguments[2] = "not-a-guid";
        string[] missingValueArguments = CreateValidArguments()[..^1];

        AgentExecutionContextParseResult badGuidResult = AgentExecutionContextParser.Parse(badGuidArguments);
        AgentExecutionContextParseResult missingValueResult = AgentExecutionContextParser.Parse(missingValueArguments);

        Assert.Multiple(() =>
        {
            Assert.That(badGuidResult.Errors.Select(error => error.Code), Does.Contain("arguments.invalidRunId"));
            Assert.That(missingValueResult.Errors.Select(error => error.Code), Does.Contain("arguments.missingValue"));
        });
    }

    [Test]
    public void ParseDoesNotEchoInvalidPathValue()
    {
        const string sensitiveMarker = "secret-config-value";
        string[] arguments = CreateValidArguments();
        arguments[8] = $"bad\0{sensitiveMarker}";

        AgentExecutionContextParseResult result = AgentExecutionContextParser.Parse(arguments);

        Assert.That(string.Join('|', result.Errors.Select(error => error.Message)), Does.Not.Contain(sensitiveMarker));
    }

    private static string[] CreateValidArguments() =>
    [
        "run",
        "--run-id", Guid.NewGuid().ToString("D"),
        "--occurrence-id", Guid.NewGuid().ToString("D"),
        "--agent-id", "founder-scout",
        "--config-file", "configuration.json",
        "--data-directory", "data",
        "--artifact-directory", "artifacts",
        "--protocol-version", "1.0",
    ];
}
