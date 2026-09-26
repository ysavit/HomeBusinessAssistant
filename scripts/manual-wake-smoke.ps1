[CmdletBinding()]
param(
    [ValidateRange(1, 60)]
    [int]$Minutes = 3,

    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA "HomeBusinessAssistant\manual-wake-smoke"),

    [switch]$ConfirmTaskRegistration
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmTaskRegistration) {
    throw "This opt-in smoke registers the managed NextWake task. Re-run with -ConfirmTaskRegistration after reviewing the script."
}

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "The manual wake smoke requires Windows."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runnerDirectory = Join-Path $repositoryRoot "src\HomeBusinessAssistant.Runner\bin\Release\net10.0-windows"
$runner = Join-Path $runnerDirectory "HomeBusinessAssistant.Runner.exe"
$wakeRemoteOutput = Join-Path $repositoryRoot "agents\WakeRemote\WakeRemote.Agent\bin\Release\net10.0-windows"
$agentDirectory = Join-Path $DataDirectory "manual-agent-root"
$wakeRemoteInstall = Join-Path $agentDirectory "wake-remote"
$manifestDirectory = Join-Path $repositoryRoot "manifests"
$taskName = "\HomeBusinessAssistant\NextWake"

if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
    throw "Build the solution in Release before running the manual wake smoke."
}

if (-not (Test-Path -LiteralPath $wakeRemoteOutput -PathType Container)) {
    throw "Build WakeRemote.Agent in Release before running the manual wake smoke."
}

New-Item -ItemType Directory -Path $wakeRemoteInstall -Force | Out-Null
Copy-Item -Path (Join-Path $wakeRemoteOutput "*") -Destination $wakeRemoteInstall -Recurse -Force

$bootstrapArguments = @(
    "--data-directory", $DataDirectory,
    "--agent-directory", $agentDirectory,
    "--manifest-directory", $manifestDirectory
)

try {
    $prepareOutput = @(& $runner "prepare-wake-test" "--minutes" $Minutes @bootstrapArguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Wake-test preparation failed with exit code $LASTEXITCODE."
    }

    $preparation = $prepareOutput[-1] | ConvertFrom-Json
    $occurrenceId = $preparation.OccurrenceId.Value
    if (-not $occurrenceId) {
        $occurrenceId = $preparation.OccurrenceId
    }

    Write-Host "Expected wake (UTC): $($preparation.ExpectedWakeAtUtc)"
    Write-Host "Occurrence: $occurrenceId"
    & "$env:SystemRoot\System32\schtasks.exe" /Query /TN $taskName /XML
    & "$env:SystemRoot\System32\powercfg.exe" /waketimers

    Write-Host "The application will not sleep the workstation. Lock it and choose Sleep manually if you want to test hardware wake."
    Read-Host "Press Enter after the expected wake time and after signing back in"

    & $runner "wake-test-status" "--occurrence-id" $occurrenceId @bootstrapArguments
    Write-Host "Inspect the persisted run start, wake delay, and wake-test-result artifact in $DataDirectory."
}
finally {
    & $runner "remove-wake-task" @bootstrapArguments
    Write-Host "Managed NextWake task removal requested. The diagnostic database and staged agent remain at $DataDirectory for inspection."
}
