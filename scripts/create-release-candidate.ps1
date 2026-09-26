[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [string]$Version = "1.0.0-rc.1",
    [string]$GitCommit = "unavailable",
    [string]$BuildUtc = "",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\stage16-release"
}
$output = [System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts")).TrimEnd('\')
if (-not $output.StartsWith($artifactsRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $output -eq $artifactsRoot) {
    throw "Release output must be a child of the repository artifacts directory."
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version must be a bounded semantic version."
}
if ([string]::IsNullOrWhiteSpace($BuildUtc)) { $BuildUtc = [DateTimeOffset]::UtcNow.ToString("O") }

$artifactName = "HomeBusinessAssistant-$Version-win-x64"
$bundle = Join-Path $output $artifactName
$archive = Join-Path $output "$artifactName.zip"
$checksums = Join-Path $output "SHA256SUMS.txt"
$releaseManifest = Join-Path $output "release-manifest.json"
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($path in @($bundle, $archive, $checksums, $releaseManifest)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$publishArguments = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "publish.ps1"),
    "-OutputDirectory", $bundle,
    "-Version", $Version,
    "-GitCommit", $GitCommit,
    "-BuildUtc", $BuildUtc
)
if ($NoRestore) { $publishArguments += "-NoRestore" }
& powershell @publishArguments
if ($LASTEXITCODE -ne 0) { throw "Release publish failed." }

$manifestPath = Join-Path $bundle "publish-manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = Join-Path $bundle ([string]$entry.path).Replace('/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Manifest file is missing: $($entry.path)" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne [string]$entry.sha256 -or (Get-Item -LiteralPath $path).Length -ne [long]$entry.sizeBytes) {
        throw "Manifest verification failed: $($entry.path)"
    }
}

Compress-Archive -Path (Join-Path $bundle "*") -DestinationPath $archive -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksums -Encoding ASCII -Value "$archiveHash  $artifactName.zip"
$metadata = [ordered]@{
    schemaVersion = "1.0"
    productVersion = $Version
    buildUtc = $BuildUtc
    runtimeTarget = "win-x64"
    archive = [ordered]@{
        fileName = "$artifactName.zip"
        sizeBytes = (Get-Item -LiteralPath $archive).Length
        sha256 = $archiveHash
    }
    bundleDirectory = $artifactName
    publishManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
Set-Content -LiteralPath $releaseManifest -Encoding UTF8 -Value ($metadata | ConvertTo-Json -Depth 6)
$metadata | ConvertTo-Json -Compress
