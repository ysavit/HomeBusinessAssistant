[CmdletBinding()]
param(
    [string]$InstallDirectory = "",
    [string]$DataDirectory = "",
    [switch]$RemoveData,
    [string]$ConfirmRemoveData = "",
    [switch]$PreserveConfiguration,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = Join-Path $env:LOCALAPPDATA "HomeBusinessAssistant" }
$install = [System.IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
if ($install -eq [System.IO.Path]::GetPathRoot($install) -or $install.Length -lt 8) { throw "The install root is unsafe." }
if ([string]::IsNullOrWhiteSpace($DataDirectory)) { $DataDirectory = Join-Path $install "data" }
$data = [System.IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
if ($RemoveData -and $ConfirmRemoveData -ne "REMOVE-DATA") { throw "-RemoveData requires -ConfirmRemoveData REMOVE-DATA." }
if ($RemoveData) {
    $approvedDefault = [System.IO.Path]::GetFullPath((Join-Path $install "data")).TrimEnd('\')
    $approvedLocal = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "HomeBusinessAssistant\data")).TrimEnd('\')
    if ($data -ne $approvedDefault -and $data -ne $approvedLocal) { throw "Refusing to delete a data root outside the validated HomeBusinessAssistant locations." }
}

$app = Join-Path $install "app"
$runner = Join-Path $app "HomeBusinessAssistant.Runner.exe"
$hostExecutable = Join-Path $app "HomeBusinessAssistant.Host.exe"
$config = Join-Path $install "config\appsettings.json"
$bootstrap = @("--data-directory", $data, "--agent-directory", (Join-Path $app "agents"), "--manifest-directory", (Join-Path $app "manifests"), "--database-file-name", "assistant.db")
if (Test-Path -LiteralPath $runner) {
    $diagnostic = & $runner diagnose @bootstrap 2>$null
    if ($LASTEXITCODE -eq 0 -and $diagnostic) {
        $state = @($diagnostic)[-1] | ConvertFrom-Json
        if ([int]$state.activeRuns -gt 0 -and -not $Force) { throw "Uninstall is refused while $($state.activeRuns) run(s) are active." }
    }
}
if (Test-Path -LiteralPath $hostExecutable) {
    $runningHost = @(Get-Process -Name "HomeBusinessAssistant.Host" -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $hostExecutable })
    if ($runningHost.Count -gt 0) {
        $signal = Start-Process -FilePath $hostExecutable -ArgumentList @("--bootstrap-config", $config, "--shutdown") -WorkingDirectory $app -WindowStyle Hidden -PassThru
        if (-not $signal.WaitForExit(15000)) {
            Stop-Process -Id $signal.Id -Force -ErrorAction SilentlyContinue
            $signal.WaitForExit(5000)
        }
    }
}
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
do {
    $owned = @(Get-Process -Name "HomeBusinessAssistant.Host", "FounderScout", "WakeRemote" -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($app + '\', [StringComparison]::OrdinalIgnoreCase) })
    if ($owned.Count -eq 0) { break }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)
if ($owned.Count -gt 0 -and -not $Force) { throw "Owned application processes did not stop within 15 seconds; review active work or re-run with -Force." }
if ($owned.Count -gt 0 -and $Force) { $owned | Stop-Process -Force }
if (Test-Path -LiteralPath $runner) {
    & $runner remove-host-startup @bootstrap | Out-Null
    & $runner remove-wake-task @bootstrap | Out-Null
}
if (Test-Path -LiteralPath $app) { Remove-Item -LiteralPath $app -Recurse -Force }
if (-not $PreserveConfiguration -and (Test-Path -LiteralPath (Join-Path $install "config"))) { Remove-Item -LiteralPath (Join-Path $install "config") -Recurse -Force }
foreach ($path in @((Join-Path $install "scripts"), (Join-Path $install "docs"), (Join-Path $install "install-manifest.json"))) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
if ($RemoveData -and (Test-Path -LiteralPath $data)) { Remove-Item -LiteralPath $data -Recurse -Force }
[ordered]@{ status = "uninstalled"; dataPreserved = (-not $RemoveData); configurationPreserved = [bool]$PreserveConfiguration; installDirectory = $install; dataDirectory = $data } | ConvertTo-Json -Compress
