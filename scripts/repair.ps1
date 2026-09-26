[CmdletBinding()]
param(
    [string]$InstallDirectory = "",
    [string]$DataDirectory = "",
    [string]$SourceDirectory = "",
    [switch]$InstallPlaywrightBrowser,
    [switch]$SkipStartupTask
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = Join-Path $env:LOCALAPPDATA "HomeBusinessAssistant" }
$install = [System.IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
if ($install -eq [System.IO.Path]::GetPathRoot($install) -or $install.Length -lt 8) { throw "The install root is unsafe." }
if ([string]::IsNullOrWhiteSpace($DataDirectory)) { $DataDirectory = Join-Path $install "data" }
$data = [System.IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
$manifestPath = Join-Path $install "install-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "The installed publish manifest is missing." }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$invalid = @()
foreach ($entry in $manifest.files) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $install ([string]$entry.path).Replace('/', '\')))
    if (-not $path.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $invalid += [string]$entry.path
        continue
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry.sha256) { $invalid += [string]$entry.path }
}
if ($invalid.Count -gt 0) {
    if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { throw "Repair found $($invalid.Count) invalid file(s); provide -SourceDirectory to replace them." }
    $installer = Join-Path $SourceDirectory "scripts\install.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -SourceDirectory $SourceDirectory -InstallDirectory $install -DataDirectory $data -Force -SkipStartupTask
    if ($LASTEXITCODE -ne 0) { throw "Repair reinstall failed." }
    $remainingInvalid = @()
    $refreshedManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $refreshedManifest.files) {
        $path = [System.IO.Path]::GetFullPath((Join-Path $install ([string]$entry.path).Replace('/', '\')))
        if (-not $path.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $remainingInvalid += [string]$entry.path
            continue
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry.sha256) { $remainingInvalid += [string]$entry.path }
    }
    if ($remainingInvalid.Count -gt 0) { throw "Repair reinstall left $($remainingInvalid.Count) invalid file(s)." }
}

$probe = Join-Path $data (".repair-write-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $data -Force | Out-Null
Set-Content -LiteralPath $probe -Value "write-test" -Encoding ASCII
Remove-Item -LiteralPath $probe -Force
$app = Join-Path $install "app"
$runner = Join-Path $app "HomeBusinessAssistant.Runner.exe"
$hostExecutable = Join-Path $app "HomeBusinessAssistant.Host.exe"
$config = Join-Path $install "config\appsettings.json"
$bootstrap = @("--data-directory", $data, "--agent-directory", (Join-Path $app "agents"), "--manifest-directory", (Join-Path $app "manifests"), "--database-file-name", "assistant.db")
& $runner migrate @bootstrap
if ($LASTEXITCODE -ne 0) { throw "Database repair/migration failed." }
& $runner scan-agents @bootstrap
if ($LASTEXITCODE -ne 0) { throw "Installed agent registration failed during repair." }
if (-not $SkipStartupTask) {
    & $runner register-host-startup --host-executable $hostExecutable --bootstrap-config $config --delay-seconds 15 @bootstrap
    if ($LASTEXITCODE -ne 0) { throw "HostAtLogon repair failed." }
}
& $runner reconcile-wake @bootstrap
$wakeExit = $LASTEXITCODE
$playwright = Join-Path $app "agents\founder-scout\playwright.ps1"
$playwrightStatus = "missing"
if (Test-Path -LiteralPath $playwright) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $playwright install --list | Out-Null
    $playwrightStatus = if ($LASTEXITCODE -eq 0) { "available" } else { "not-installed" }
    if ($InstallPlaywrightBrowser -and $playwrightStatus -ne "available") {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $playwright install chromium
        if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium repair failed." }
        $playwrightStatus = "installed"
    }
}
& $runner diagnose @bootstrap
if ($LASTEXITCODE -ne 0) { throw "Post-repair diagnosis failed." }
[ordered]@{ status = "repaired"; invalidFilesReplaced = $invalid.Count; playwright = $playwrightStatus; wakeReconciliationExitCode = $wakeExit; hostAtLogon = (-not $SkipStartupTask) } | ConvertTo-Json -Compress
