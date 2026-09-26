[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [string]$Version = "1.0.0",
    [string]$GitCommit = "",
    [string]$BuildUtc = "",
    [switch]$NoRestore,
    [switch]$IncludeSampleAgent
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-SafeOutputDirectory([string]$PathValue, [string]$RepositoryRoot) {
    $full = [System.IO.Path]::GetFullPath($PathValue)
    $root = [System.IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrWhiteSpace($full) -or $full -eq $root -or $full -eq $RepositoryRoot -or $full.Length -lt 8) {
        throw "Refusing to use an unsafe publish output directory."
    }
    return $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
}

function Invoke-DotNetPublish([string]$Project, [string]$Destination, [string]$ProductVersion, [string]$InformationVersion) {
    $arguments = @(
        "publish", $Project,
        "-m:1",
        "-nodeReuse:false",
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "--no-restore",
        "-o", $Destination,
        "-p:Version=$ProductVersion",
        "-p:InformationalVersion=$InformationVersion",
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false",
        "-p:BuildInParallel=false",
        "-p:UseSharedCompilation=false",
        "-p:ContinuousIntegrationBuild=true"
    )
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Project." }
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$agentCatalogPath = Join-Path $repositoryRoot "packaging\agents.json"
$agentCatalog = Get-Content -LiteralPath $agentCatalogPath -Raw | ConvertFrom-Json
if ($agentCatalog.schemaVersion -ne "1.0" -or @($agentCatalog.agents).Count -lt 1) { throw "The explicit package agent catalog is invalid." }
$selectedAgents = @($agentCatalog.agents | Where-Object { [bool]$_.includeByDefault -or ($IncludeSampleAgent -and $_.id -eq "sample-business-agent") })
foreach ($agent in $selectedAgents) {
    if ([string]::IsNullOrWhiteSpace([string]$agent.id) -or [string]$agent.id -notmatch '^[a-z][a-z0-9-]{1,63}$') { throw "A package agent identifier is invalid." }
    foreach ($property in @("project", "manifest", "configurationSchema")) {
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot ([string]$agent.$property).Replace('/', '\')))
        if (-not $candidate.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "The package catalog path '$property' for '$($agent.id)' is missing or escapes the repository."
        }
    }
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64"
}
$output = Assert-SafeOutputDirectory $OutputDirectory $repositoryRoot
try { $parsedVersion = [System.Version](($Version -split '[-+]')[0]) } catch { throw "Version must begin with a numeric semantic version." }
if ($null -eq $parsedVersion) { throw "Version must begin with a numeric semantic version." }
if ([string]::IsNullOrWhiteSpace($BuildUtc)) { $BuildUtc = [DateTimeOffset]::UtcNow.ToString("O") }
if ([string]::IsNullOrWhiteSpace($GitCommit)) {
    $GitCommit = "unavailable"
    try {
        $candidate = (& git -C $repositoryRoot rev-parse --verify HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidate)) { $GitCommit = $candidate.Trim() }
    } catch { $GitCommit = "unavailable" }
}
if ($GitCommit.Length -gt 128 -or $GitCommit -match '[\r\n]') { throw "Git commit metadata is invalid." }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("hba-publish-" + [Guid]::NewGuid().ToString("N"))
$published = Join-Path $work "published"
$bundle = Join-Path $work "bundle"
New-Item -ItemType Directory -Path $published -Force | Out-Null
New-Item -ItemType Directory -Path $bundle -Force | Out-Null

try {
    if (-not $NoRestore) {
        & dotnet restore (Join-Path $repositoryRoot "HomeBusinessAssistant.sln") -r win-x64 -m:1 -nodeReuse:false -p:RestoreUseStaticGraphEvaluation=false
        if ($LASTEXITCODE -ne 0) { throw "The win-x64 publish restore failed." }
    }
    $informationalVersion = if ($GitCommit -eq "unavailable") { $Version } else { "$Version+$GitCommit" }
    Invoke-DotNetPublish (Join-Path $repositoryRoot "src\HomeBusinessAssistant.Host\HomeBusinessAssistant.Host.csproj") (Join-Path $published "host") $Version $informationalVersion
    Invoke-DotNetPublish (Join-Path $repositoryRoot "src\HomeBusinessAssistant.Runner\HomeBusinessAssistant.Runner.csproj") (Join-Path $published "runner") $Version $informationalVersion
    foreach ($agent in $selectedAgents) {
        Invoke-DotNetPublish (Join-Path $repositoryRoot ([string]$agent.project).Replace('/', '\')) (Join-Path $published ([string]$agent.id)) $Version $informationalVersion
    }

    $app = Join-Path $bundle "app"
    Copy-DirectoryContents (Join-Path $published "host") $app
    Copy-DirectoryContents (Join-Path $published "runner") $app

    New-Item -ItemType Directory -Path (Join-Path $app "manifests") -Force | Out-Null
    $agentDestinations = @()
    foreach ($agent in $selectedAgents) {
        $destination = Join-Path $app ("agents\" + [string]$agent.id)
        Copy-DirectoryContents (Join-Path $published ([string]$agent.id)) $destination
        $manifestSource = Join-Path $repositoryRoot ([string]$agent.manifest).Replace('/', '\')
        $schemaSource = Join-Path $repositoryRoot ([string]$agent.configurationSchema).Replace('/', '\')
        Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $destination "manifest.json") -Force
        Copy-Item -LiteralPath $schemaSource -Destination (Join-Path $destination "configuration.schema.json") -Force
        Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $app ("manifests\" + [string]$agent.id + ".agent-manifest.json")) -Force
        $agentDestinations += $destination
    }

    $config = Join-Path $bundle "config"
    New-Item -ItemType Directory -Path $config -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "packaging\appsettings.template.json") -Destination (Join-Path $config "appsettings.template.json") -Force
    $bundleScripts = Join-Path $bundle "scripts"
    New-Item -ItemType Directory -Path $bundleScripts -Force | Out-Null
    foreach ($scriptName in @("install.ps1", "repair.ps1", "uninstall.ps1", "smoke-test.ps1")) {
        $scriptPath = Join-Path $repositoryRoot "scripts\$scriptName"
        if (Test-Path -LiteralPath $scriptPath) { Copy-Item -LiteralPath $scriptPath -Destination $bundleScripts -Force }
    }
    $bundleDocs = Join-Path $bundle "docs"
    New-Item -ItemType Directory -Path $bundleDocs -Force | Out-Null
    foreach ($documentName in @("installation.md", "security-privacy.md", "v1-release-checklist.md", "v1-release-notes.md", "dependency-inventory.md", "adding-an-agent.md")) {
        $documentPath = Join-Path $repositoryRoot "docs\$documentName"
        if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) { throw "Required release document is missing: $documentPath" }
        Copy-Item -LiteralPath $documentPath -Destination $bundleDocs -Force
    }

    $buildInfo = [ordered]@{
        productVersion = $Version
        gitCommit = $GitCommit
        buildUtc = $BuildUtc
        runtimeTarget = "win-x64"
        framework = ".NETCoreApp,Version=v10.0"
    }
    $buildJson = $buildInfo | ConvertTo-Json -Depth 4
    foreach ($directory in @($app) + $agentDestinations) {
        Set-Content -LiteralPath (Join-Path $directory "build-info.json") -Value $buildJson -Encoding UTF8
    }

    $requiredFiles = @(
        (Join-Path $app "HomeBusinessAssistant.Host.exe"),
        (Join-Path $app "HomeBusinessAssistant.Runner.exe")
    )
    foreach ($agent in $selectedAgents) {
        $destination = Join-Path $app ("agents\" + [string]$agent.id)
        $manifestDocument = Get-Content -LiteralPath (Join-Path $destination "manifest.json") -Raw | ConvertFrom-Json
        $requiredFiles += @(
            (Join-Path $destination ([string]$manifestDocument.executable)),
            (Join-Path $destination "manifest.json"),
            (Join-Path $destination "configuration.schema.json")
        )
    }
    foreach ($required in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required publish output is missing: $required" }
    }

    $entries = @()
    Get-ChildItem -LiteralPath $bundle -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($bundle.Length + 1).Replace('\', '/')
        $entries += [ordered]@{
            path = $relative
            sizeBytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schemaVersion = "1.0"
        productVersion = $Version
        gitCommit = $GitCommit
        buildUtc = $BuildUtc
        runtimeTarget = "win-x64"
        selfContained = $true
        singleFile = $false
        trimmed = $false
        readyToRun = $false
        files = $entries
        exclusions = @("databases", "SQLite sidecars", "secrets", "browser profiles", "logs", "captured profiles", "developer settings")
    }
    Set-Content -LiteralPath (Join-Path $bundle "publish-manifest.json") -Value ($manifest | ConvertTo-Json -Depth 8) -Encoding UTF8

    $parent = Split-Path -Parent $output
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    Move-Item -LiteralPath $bundle -Destination $output
    $bytes = (Get-ChildItem -LiteralPath $output -File -Recurse | Measure-Object -Property Length -Sum).Sum
    [ordered]@{ status = "published"; outputDirectory = $output; productVersion = $Version; files = $entries.Count + 1; sizeBytes = $bytes } | ConvertTo-Json -Compress
} finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
