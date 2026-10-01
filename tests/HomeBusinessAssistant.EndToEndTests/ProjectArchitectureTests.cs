using System.Xml.Linq;

namespace HomeBusinessAssistant.EndToEndTests;

internal sealed class ProjectArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["HomeBusinessAssistant.Domain"] = [],
            ["HomeBusinessAssistant.Application"] = ["HomeBusinessAssistant.Domain"],
            ["HomeBusinessAssistant.AgentSdk"] = ["HomeBusinessAssistant.Domain"],
            ["HomeBusinessAssistant.Infrastructure"] =
                ["HomeBusinessAssistant.AgentSdk", "HomeBusinessAssistant.Application"],
            ["HomeBusinessAssistant.Windows"] = ["HomeBusinessAssistant.Application"],
            ["HomeBusinessAssistant.Host"] =
                ["FounderScout.Application", "FounderScout.Infrastructure", "HomeBusinessAssistant.AgentSdk", "HomeBusinessAssistant.Application", "HomeBusinessAssistant.Infrastructure", "HomeBusinessAssistant.Windows", "WakeRemote.Application"],
            ["HomeBusinessAssistant.Runner"] =
                ["FounderScout.Application", "FounderScout.Infrastructure", "HomeBusinessAssistant.AgentSdk", "HomeBusinessAssistant.Application", "HomeBusinessAssistant.Infrastructure", "HomeBusinessAssistant.Windows", "WakeRemote.Application"],
            ["FounderScout.Domain"] = [],
            ["FounderScout.Application"] =
                ["FounderScout.Domain", "HomeBusinessAssistant.Application", "HomeBusinessAssistant.Domain"],
            ["FounderScout.Infrastructure"] = ["FounderScout.Application"],
            ["FounderScout.Agent"] = ["FounderScout.Infrastructure", "HomeBusinessAssistant.AgentSdk"],
            ["FounderScout.SimpleCli"] =
                ["FounderScout.Agent", "HomeBusinessAssistant.Infrastructure", "HomeBusinessAssistant.Windows"],
            ["WakeRemote.Application"] = ["HomeBusinessAssistant.Application", "HomeBusinessAssistant.Domain"],
            ["WakeRemote.Infrastructure"] = ["HomeBusinessAssistant.Windows", "WakeRemote.Application"],
            ["WakeRemote.Agent"] =
                ["HomeBusinessAssistant.AgentSdk", "HomeBusinessAssistant.Application", "HomeBusinessAssistant.Windows", "WakeRemote.Infrastructure"],
            ["SampleBusinessAgent.Application"] =
                ["HomeBusinessAssistant.AgentSdk", "HomeBusinessAssistant.Application"],
            ["SampleBusinessAgent.Infrastructure"] = ["SampleBusinessAgent.Application"],
            ["SampleBusinessAgent.Agent"] =
                ["HomeBusinessAssistant.AgentSdk", "SampleBusinessAgent.Application", "SampleBusinessAgent.Infrastructure"],
        };

    [Test]
    public void ProductionProjectReferencesMatchTheAllowedGraph()
    {
        IReadOnlyDictionary<string, string[]> actual = ReadProductionGraph();

        Assert.That(actual.Keys, Is.EquivalentTo(ExpectedReferences.Keys));
        foreach ((string projectName, string[] expectedDependencies) in ExpectedReferences)
        {
            Assert.That(
                actual[projectName],
                Is.EquivalentTo(expectedDependencies),
                $"Unexpected project reference for {projectName}.");
        }
    }

    [Test]
    public void ProductionProjectReferenceGraphContainsNoCycle()
    {
        IReadOnlyDictionary<string, string[]> graph = ReadProductionGraph();
        HashSet<string> completed = new(StringComparer.Ordinal);

        foreach (string project in graph.Keys)
        {
            Assert.That(
                HasCycle(project, graph, completed, new HashSet<string>(StringComparer.Ordinal)),
                Is.False,
                $"A project-reference cycle starts at {project}.");
        }
    }

    [Test]
    public void DomainProjectsHaveNoForbiddenFrameworkOrPackageReferences()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] domainProjects =
        [
            Path.Combine(repositoryRoot, "src", "HomeBusinessAssistant.Domain", "HomeBusinessAssistant.Domain.csproj"),
            Path.Combine(repositoryRoot, "agents", "FounderScout", "FounderScout.Domain", "FounderScout.Domain.csproj"),
        ];

        string[] forbiddenTerms =
        [
            "Infrastructure",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Playwright",
            "System.Windows.Forms",
            ".Agent",
        ];

        foreach (string projectPath in domainProjects)
        {
            XDocument project = XDocument.Load(projectPath);
            string[] references = project.Descendants()
                .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference" or "FrameworkReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .ToArray();

            foreach (string forbiddenTerm in forbiddenTerms)
            {
                Assert.That(
                    references.Any(reference => reference.Contains(forbiddenTerm, StringComparison.Ordinal)),
                    Is.False,
                    $"{Path.GetFileName(projectPath)} references forbidden dependency {forbiddenTerm}.");
            }
        }
    }

    private static Dictionary<string, string[]> ReadProductionGraph()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] projectFiles =
        [
            .. Directory.GetFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories),
            .. Directory.GetFiles(Path.Combine(repositoryRoot, "agents"), "*.csproj", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal)),
        ];

        return projectFiles.ToDictionary(
            projectPath => Path.GetFileNameWithoutExtension(projectPath)
                ?? throw new InvalidOperationException($"Project path has no file name: {projectPath}"),
            projectPath =>
            {
                XDocument project = XDocument.Load(projectPath);
                return project.Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Select(element => (string?)element.Attribute("Include"))
                    .Where(include => !string.IsNullOrWhiteSpace(include))
                    .Select(include => Path.GetFileNameWithoutExtension(include!))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            },
            StringComparer.Ordinal);
    }

    private static bool HasCycle(
        string project,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> completed,
        ISet<string> visiting)
    {
        if (completed.Contains(project))
        {
            return false;
        }

        if (!visiting.Add(project))
        {
            return true;
        }

        foreach (string dependency in graph[project])
        {
            if (HasCycle(dependency, graph, completed, visiting))
            {
                return true;
            }
        }

        visiting.Remove(project);
        completed.Add(project);
        return false;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
