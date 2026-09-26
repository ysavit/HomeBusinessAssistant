namespace HomeBusinessAssistant.Host;

/// <summary>Bootstrap-only Host paths, local URL, and bounded orchestration intervals.</summary>
public sealed record HostBootstrapSettings(
    string ApplicationRoot,
    string DataDirectory,
    string AgentDirectory,
    string ManifestDirectory,
    string RunnerExecutablePath,
    string RunnerWorkingDirectory,
    string DatabaseFileName,
    string Url,
    TimeSpan ScheduleInterval,
    TimeSpan WakeInterval,
    TimeSpan RecoveryInterval,
    TimeSpan NotificationInterval)
{
    /// <summary>Loads appsettings, HBA-prefixed environment variables, and command-line overrides.</summary>
    public static HostBootstrapSettings Load(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string[] configurationArgs = args.Where(argument => argument != "--shutdown").ToArray();
        string? bootstrapPath = ReadBootstrapConfigurationPath(configurationArgs);
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory);
        if (bootstrapPath is null)
        {
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        }
        else
        {
            string fullPath = Path.GetFullPath(bootstrapPath);
            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException("The bootstrap configuration path is invalid.");
            builder.AddJsonFile(
                new Microsoft.Extensions.FileProviders.PhysicalFileProvider(directory),
                Path.GetFileName(fullPath),
                optional: false,
                reloadOnChange: false);
        }

        IConfigurationRoot configuration = builder
            .AddEnvironmentVariables("HBA_")
            .AddCommandLine(configurationArgs)
            .Build();
        string baseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string? repositoryRoot = FindRepositoryRoot(baseDirectory);
        string applicationRoot = GetPath(configuration["Host:ApplicationRoot"], repositoryRoot ?? baseDirectory);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localRoot = Path.Combine(localAppData, "HomeBusinessAssistant");
        string dataDirectory = GetPath(configuration["Host:DataDirectory"], Path.Combine(localRoot, "data"));
        string agentDirectory = GetPath(configuration["Host:AgentDirectory"], Path.Combine(localRoot, "app", "agents"));
        string manifestDirectory = GetPath(
            configuration["Host:ManifestDirectory"],
            repositoryRoot is null ? Path.Combine(baseDirectory, "manifests") : Path.Combine(repositoryRoot, "manifests"));
        string defaultRunner = FindDevelopmentRunner(repositoryRoot, baseDirectory)
            ?? Path.Combine(baseDirectory, "HomeBusinessAssistant.Runner.exe");
        string runnerPath = GetPath(configuration["Host:RunnerExecutablePath"], defaultRunner);
        string runnerWorking = GetPath(
            configuration["Host:RunnerWorkingDirectory"],
            Path.GetDirectoryName(runnerPath) ?? applicationRoot);
        string url = configuration[WebHostDefaults.ServerUrlsKey]
            ?? configuration["Host:Url"]
            ?? HostApplication.DefaultUrl;
        var result = new HostBootstrapSettings(
            applicationRoot,
            dataDirectory,
            agentDirectory,
            manifestDirectory,
            runnerPath,
            runnerWorking,
            configuration["Host:DatabaseFileName"] ?? "assistant.db",
            LoopbackUrlPolicy.Validate(url),
            ReadInterval(configuration, "Host:ScheduleIntervalSeconds", TimeSpan.FromSeconds(30)),
            ReadInterval(configuration, "Host:WakeIntervalSeconds", TimeSpan.FromMinutes(1)),
            ReadInterval(configuration, "Host:RecoveryIntervalSeconds", TimeSpan.FromMinutes(2)),
            ReadInterval(configuration, "Host:NotificationIntervalSeconds", TimeSpan.FromSeconds(15)));
        result.Validate();
        return result;
    }

    /// <summary>Validates path containment, safe file naming, and loop bounds before startup side effects.</summary>
    public void Validate()
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ApplicationRoot));
        string runner = Path.GetFullPath(RunnerExecutablePath);
        string runnerWorking = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RunnerWorkingDirectory));
        if (!IsContained(root, runner)
            || !IsContained(root, runnerWorking)
            || string.IsNullOrWhiteSpace(DatabaseFileName)
            || DatabaseFileName != Path.GetFileName(DatabaseFileName)
            || DatabaseFileName.Length > 128
            || !Directory.Exists(ManifestDirectory))
        {
            throw new ArgumentException("Host bootstrap paths are invalid.");
        }

        _ = Path.GetFullPath(DataDirectory);
        _ = Path.GetFullPath(AgentDirectory);
        _ = LoopbackUrlPolicy.Validate(Url);
        ValidateInterval(ScheduleInterval, nameof(ScheduleInterval));
        ValidateInterval(WakeInterval, nameof(WakeInterval));
        ValidateInterval(RecoveryInterval, nameof(RecoveryInterval));
        ValidateInterval(NotificationInterval, nameof(NotificationInterval));
    }

    private static TimeSpan ReadInterval(IConfiguration configuration, string key, TimeSpan defaultValue)
    {
        string? value = configuration[key];
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int seconds)
            ? TimeSpan.FromSeconds(seconds)
            : throw new ArgumentException($"Configuration value '{key}' must be an integer number of seconds.");
    }

    private static void ValidateInterval(TimeSpan value, string name)
    {
        if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(name, "Host loop intervals must be between one second and one hour.");
        }
    }

    private static string GetPath(string? configured, string defaultValue) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? defaultValue : configured);

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HomeBusinessAssistant.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? ReadBootstrapConfigurationPath(string[] args)
    {
        string? value = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] != "--bootstrap-config") continue;
            if (value is not null || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException("--bootstrap-config requires one unique path.");
            }

            value = args[++index];
        }

        return value;
    }

    private static string? FindDevelopmentRunner(string? repositoryRoot, string baseDirectory)
    {
        if (repositoryRoot is null)
        {
            return null;
        }

        string configuration = baseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";
        string candidate = Path.Combine(
            repositoryRoot,
            "src",
            "HomeBusinessAssistant.Runner",
            "bin",
            configuration,
            "net10.0-windows",
            "HomeBusinessAssistant.Runner.exe");
        return candidate;
    }

    private static bool IsContained(string root, string candidate) =>
        string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
