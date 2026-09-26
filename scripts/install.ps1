[CmdletBinding()]
param(
    [string]$SourceDirectory = "",
    [string]$InstallDirectory = "",
    [string]$DataDirectory = "",
    [ValidateRange(1024, 65535)][int]$Port = 5180,
    [switch]$InstallPlaywrightBrowser,
    [switch]$StartAfterInstall,
    [switch]$Force,
    [switch]$SkipStartupTask,
    [ValidateRange(5, 120)][int]$HealthTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-NormalizedPath([string]$Value) {
    return [System.IO.Path]::GetFullPath($Value).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
}

function Assert-SafeRoot([string]$PathValue, [string]$Name) {
    $full = Get-NormalizedPath $PathValue
    if ($full -eq [System.IO.Path]::GetPathRoot($full) -or $full.Length -lt 8) { throw "$Name is not a safe application root." }
    return $full
}

function Test-IsContained([string]$Root, [string]$Candidate) {
    $prefix = $Root + [System.IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-Checked([string]$Executable, [string[]]$Arguments, [string]$Description) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $Executable @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    if ($exitCode -ne 0) { throw "$Description failed with exit code $exitCode. $($output -join ' ')" }
    Write-Output -NoEnumerate @($output)
}

function Get-BootstrapArguments([string]$DataRoot, [string]$AppRoot) {
    return @(
        "--data-directory", $DataRoot,
        "--agent-directory", (Join-Path $AppRoot "agents"),
        "--manifest-directory", (Join-Path $AppRoot "manifests"),
        "--database-file-name", "assistant.db"
    )
}

function Confirm-Manifest([string]$Root) {
    $manifestPath = Join-Path $Root "publish-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "The publish manifest is missing." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne "1.0" -or $manifest.runtimeTarget -ne "win-x64" -or -not $manifest.selfContained) {
        throw "The publish manifest is incompatible."
    }
    foreach ($entry in $manifest.files) {
        $candidate = Get-NormalizedPath (Join-Path $Root ([string]$entry.path).Replace('/', '\'))
        if (-not (Test-IsContained $Root $candidate) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "A publish-manifest path is missing or escapes the source root."
        }
        $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne [string]$entry.sha256 -or (Get-Item -LiteralPath $candidate).Length -ne [long]$entry.sizeBytes) {
            throw "Publish hash validation failed for $($entry.path)."
        }
    }
    return $manifest
}

function Get-CoreVersion([string]$Value) {
    try { return [System.Version](($Value -split '[-+]')[0]) } catch { throw "Product version '$Value' is invalid." }
}

function Stop-InstalledHost([string]$HostPath, [string]$ConfigPath, [bool]$AllowForce) {
    if (Test-Path -LiteralPath $HostPath -PathType Leaf) {
        $running = @(Get-Process -Name "HomeBusinessAssistant.Host" -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $HostPath })
        if ($running.Count -eq 0) { return }
        $signal = Start-Process -FilePath $HostPath -ArgumentList @("--bootstrap-config", $ConfigPath, "--shutdown") -WorkingDirectory (Split-Path -Parent $HostPath) -WindowStyle Hidden -PassThru
        if (-not $signal.WaitForExit(15000)) {
            Stop-Process -Id $signal.Id -Force -ErrorAction SilentlyContinue
            $signal.WaitForExit(5000)
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        do {
            $running = @(Get-Process -Name "HomeBusinessAssistant.Host" -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $HostPath })
            if ($running.Count -eq 0) { return }
            Start-Sleep -Milliseconds 250
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (-not $AllowForce) { throw "The existing Host did not stop within 15 seconds. Re-run with -Force only after reviewing active work." }
        $running | Stop-Process -Force
    }
}

function Restore-DatabaseFiles([string]$DataRoot, [string]$BackupSetId) {
    if ([string]::IsNullOrWhiteSpace($BackupSetId)) { return }
    $setRoot = Get-NormalizedPath (Join-Path $DataRoot "backups\$BackupSetId")
    if (-not (Test-IsContained (Get-NormalizedPath (Join-Path $DataRoot "backups")) $setRoot)) { throw "The rollback backup path is invalid." }
    $manifest = Get-Content -LiteralPath (Join-Path $setRoot "backup-manifest.json") -Raw | ConvertFrom-Json
    foreach ($name in @("assistant", "founder-scout")) {
        $entry = @($manifest.databases | Where-Object { $_.name -eq $name })
        if ($entry.Count -ne 1) { throw "The rollback backup manifest is incomplete." }
        $source = Join-Path $setRoot ([string]$entry[0].relativePath)
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$entry[0].sha256) {
            throw "The rollback database hash is invalid."
        }
        $destination = if ($name -eq "assistant") { Join-Path $DataRoot "assistant.db" } else { Join-Path $DataRoot "agents\founder-scout\founders.db" }
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        $temporary = "$destination.install-rollback"
        Copy-Item -LiteralPath $source -Destination $temporary -Force
        if (Test-Path -LiteralPath $destination) { [System.IO.File]::Replace($temporary, $destination, $null, $true) } else { Move-Item -LiteralPath $temporary -Destination $destination }
        foreach ($sidecar in @("$destination-wal", "$destination-shm")) { if (Test-Path -LiteralPath $sidecar) { Remove-Item -LiteralPath $sidecar -Force } }
    }
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) { $SourceDirectory = Split-Path -Parent $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = Join-Path $env:LOCALAPPDATA "HomeBusinessAssistant" }
$source = Assert-SafeRoot $SourceDirectory "SourceDirectory"
$install = Assert-SafeRoot $InstallDirectory "InstallDirectory"
if ([string]::IsNullOrWhiteSpace($DataDirectory)) { $DataDirectory = Join-Path $install "data" }
$data = Assert-SafeRoot $DataDirectory "DataDirectory"
if ($source -eq $install -or (Test-IsContained $source $install)) { throw "The install root cannot be the publish source or one of its descendants." }
$publishManifest = Confirm-Manifest $source

$app = Join-Path $install "app"
$configDirectory = Join-Path $install "config"
$configPath = Join-Path $configDirectory "appsettings.json"
$runner = Join-Path $app "HomeBusinessAssistant.Runner.exe"
$hostExecutable = Join-Path $app "HomeBusinessAssistant.Host.exe"
$newVersion = Get-CoreVersion ([string]$publishManifest.productVersion)
$existingBuild = Join-Path $app "build-info.json"
if (Test-Path -LiteralPath $existingBuild) {
    $existing = Get-Content -LiteralPath $existingBuild -Raw | ConvertFrom-Json
    $existingVersion = Get-CoreVersion ([string]$existing.productVersion)
    if ($newVersion -lt $existingVersion -and -not $Force) { throw "Downgrade from $existingVersion to $newVersion is refused by default." }
}

New-Item -ItemType Directory -Path $install -Force | Out-Null
New-Item -ItemType Directory -Path $data -Force | Out-Null
$bootstrap = Get-BootstrapArguments $data $app
$preInstallBackupSetId = ""
if ((Test-Path -LiteralPath $runner) -and (Test-Path -LiteralPath (Join-Path $data "assistant.db"))) {
    $diagnosticLines = Invoke-Checked $runner (@("diagnose") + $bootstrap) "Active-run diagnosis"
    $diagnostic = $diagnosticLines[-1] | ConvertFrom-Json
    if ([int]$diagnostic.activeRuns -gt 0 -and -not $Force) { throw "Upgrade is refused while $($diagnostic.activeRuns) run(s) are active." }
    if ([int]$diagnostic.activeRuns -gt 0 -and $Force) {
        Get-Process -Name "FounderScout", "WakeRemote" -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    $backupLines = Invoke-Checked $runner (@("create-backup") + $bootstrap) "Pre-install database backup"
    $backup = $backupLines[-1] | ConvertFrom-Json
    $preInstallBackupSetId = [string]$backup.backupSet.backupSetId
}

Stop-InstalledHost $hostExecutable $configPath ([bool]$Force)
$staging = Join-Path $install (".install-staging-" + [Guid]::NewGuid().ToString("N"))
$rollback = Join-Path $install (".app-rollback-" + [Guid]::NewGuid().ToString("N"))
$appReplaced = $false
try {
    Copy-Item -LiteralPath (Join-Path $source "app") -Destination $staging -Recurse -Force
    foreach ($required in @("HomeBusinessAssistant.Host.exe", "HomeBusinessAssistant.Runner.exe", "agents\founder-scout\FounderScout.exe", "agents\wake-remote\WakeRemote.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $required) -PathType Leaf)) { throw "The staged app is missing $required." }
    }
    if (Test-Path -LiteralPath $app) { Move-Item -LiteralPath $app -Destination $rollback }
    Move-Item -LiteralPath $staging -Destination $app
    $appReplaced = $true

    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
    $settings = [ordered]@{
        Host = [ordered]@{
            ApplicationRoot = $app
            DataDirectory = $data
            AgentDirectory = (Join-Path $app "agents")
            ManifestDirectory = (Join-Path $app "manifests")
            RunnerExecutablePath = (Join-Path $app "HomeBusinessAssistant.Runner.exe")
            RunnerWorkingDirectory = $app
            DatabaseFileName = "assistant.db"
            Url = "http://127.0.0.1:$Port"
            ScheduleIntervalSeconds = 30
            WakeIntervalSeconds = 60
            RecoveryIntervalSeconds = 120
            NotificationIntervalSeconds = 15
        }
    }
    $configTemporary = "$configPath.tmp"
    Set-Content -LiteralPath $configTemporary -Value ($settings | ConvertTo-Json -Depth 6) -Encoding UTF8
    Move-Item -LiteralPath $configTemporary -Destination $configPath -Force
    Copy-Item -LiteralPath (Join-Path $source "config\appsettings.template.json") -Destination $configDirectory -Force
    $installedScripts = Join-Path $install "scripts"
    New-Item -ItemType Directory -Path $installedScripts -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $source "scripts") -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $installedScripts -Recurse -Force
    }
    $installedDocs = Join-Path $install "docs"
    if (Test-Path -LiteralPath $installedDocs) { Remove-Item -LiteralPath $installedDocs -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $source "docs") -Destination $installedDocs -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $source "publish-manifest.json") -Destination (Join-Path $install "install-manifest.json") -Force

    $runner = Join-Path $app "HomeBusinessAssistant.Runner.exe"
    $hostExecutable = Join-Path $app "HomeBusinessAssistant.Host.exe"
    $bootstrap = Get-BootstrapArguments $data $app
    Invoke-Checked $runner (@("migrate") + $bootstrap) "Database migration and manifest seed" | Out-Null
    Invoke-Checked $runner (@("scan-agents") + $bootstrap) "Installed agent registration" | Out-Null

    if ($InstallPlaywrightBrowser) {
        $playwright = Join-Path $app "agents\founder-scout\playwright.ps1"
        if (-not (Test-Path -LiteralPath $playwright)) { throw "The published Playwright bootstrap script is missing." }
        & powershell -NoProfile -ExecutionPolicy Bypass -File $playwright install chromium
        if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed." }
    }

    if (-not $SkipStartupTask) {
        Invoke-Checked $runner (@("register-host-startup", "--host-executable", $hostExecutable, "--bootstrap-config", $configPath, "--delay-seconds", "15") + $bootstrap) "HostAtLogon registration" | Out-Null
    }

    if ($StartAfterInstall) {
        Start-Process -FilePath $hostExecutable -ArgumentList @("--bootstrap-config", $configPath) -WorkingDirectory $app -WindowStyle Hidden | Out-Null
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($HealthTimeoutSeconds)
        $healthy = $false
        do {
            try {
                $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health/ready" -UseBasicParsing -TimeoutSec 2
                if ($response.StatusCode -eq 200) { $healthy = $true; break }
            } catch { Start-Sleep -Milliseconds 500 }
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if (-not $healthy) { throw "The installed Host did not become ready within the health timeout." }
    } else {
        Invoke-Checked $runner (@("diagnose") + $bootstrap) "Installed runtime diagnosis" | Out-Null
    }

    if (Test-Path -LiteralPath $rollback) { Remove-Item -LiteralPath $rollback -Recurse -Force }
    $logDirectory = Join-Path $data "logs"
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $result = [ordered]@{
        status = "installed"
        productVersion = [string]$publishManifest.productVersion
        installDirectory = $install
        dataDirectory = $data
        port = $Port
        hostAtLogon = (-not $SkipStartupTask)
        playwrightRequested = [bool]$InstallPlaywrightBrowser
        preInstallBackupSetId = $preInstallBackupSetId
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    $resultJson = $result | ConvertTo-Json -Depth 4
    Set-Content -LiteralPath (Join-Path $logDirectory ("install-" + [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss") + ".json")) -Value $resultJson -Encoding UTF8
    $resultJson
} catch {
    if ($appReplaced) {
        Stop-InstalledHost $hostExecutable $configPath $true
        if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Recurse -Force }
        if (Test-Path -LiteralPath $rollback) { Move-Item -LiteralPath $rollback -Destination $app }
        Restore-DatabaseFiles $data $preInstallBackupSetId
    }
    throw
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
