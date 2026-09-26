[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [string]$DataDirectory = "",
    [ValidateRange(1024, 65535)][int]$Port = 5280,
    [switch]$ExpectStartupTask,
    [switch]$AllowManagedWakeTask,
    [switch]$KeepSyntheticData
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked([string]$Executable, [string[]]$Arguments, [string]$Name) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $lines = & $Executable @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    if ($exitCode -ne 0) { throw "$Name failed with exit code $exitCode. $($lines -join ' ')" }
    Write-Output -NoEnumerate @($lines)
}

function ConvertTo-NativeJsonArgument([string]$Json) {
    if ($PSVersionTable.PSEdition -eq "Desktop") {
        return $Json.Replace('"', '\"')
    }
    return $Json
}

$install = [System.IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$app = Join-Path $install "app"
if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    $DataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("hba-smoke-" + [Guid]::NewGuid().ToString("N"))
}
$data = [System.IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
if (-not $data.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or -not (Split-Path -Leaf $data).StartsWith("hba-smoke-", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Smoke data must be a uniquely named hba-smoke-* directory beneath the system temp root."
}

$runner = Join-Path $app "HomeBusinessAssistant.Runner.exe"
$hostExecutable = Join-Path $app "HomeBusinessAssistant.Host.exe"
$founder = Join-Path $app "agents\founder-scout\FounderScout.exe"
$wake = Join-Path $app "agents\wake-remote\WakeRemote.exe"
foreach ($path in @($runner, $hostExecutable, $founder, $wake, (Join-Path $app "manifests\founder-scout.agent-manifest.json"), (Join-Path $app "manifests\wake-remote.agent-manifest.json"))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required installed file is missing: $path" }
}

New-Item -ItemType Directory -Path $data -Force | Out-Null
$configDirectory = Join-Path $data "smoke-config"
New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
$configPath = Join-Path $configDirectory "appsettings.json"
$settings = [ordered]@{ Host = [ordered]@{
    ApplicationRoot = $app
    DataDirectory = $data
    AgentDirectory = (Join-Path $app "agents")
    ManifestDirectory = (Join-Path $app "manifests")
    RunnerExecutablePath = $runner
    RunnerWorkingDirectory = $app
    DatabaseFileName = "assistant.db"
    Url = "http://127.0.0.1:$Port"
    ScheduleIntervalSeconds = 30
    WakeIntervalSeconds = 60
    RecoveryIntervalSeconds = 120
    NotificationIntervalSeconds = 15
} }
Set-Content -LiteralPath $configPath -Value ($settings | ConvertTo-Json -Depth 6) -Encoding UTF8
$bootstrap = @("--data-directory", $data, "--agent-directory", (Join-Path $app "agents"), "--manifest-directory", (Join-Path $app "manifests"), "--database-file-name", "assistant.db")

$hostStarted = $false
$primaryHost = $null
$wakeOccurrence = ""
try {
    $versions = @()
    $versions += ((Invoke-Checked $runner @("version") "Runner version")[-1] | ConvertFrom-Json)
    $versions += ((Invoke-Checked $founder @("version") "Founder Scout version")[-1] | ConvertFrom-Json)
    $versions += ((Invoke-Checked $wake @("version") "Wake Remote version")[-1] | ConvertFrom-Json)
    if (@($versions | Select-Object -ExpandProperty productVersion -Unique).Count -ne 1) { throw "Published process versions are inconsistent." }
    Invoke-Checked $runner (@("migrate") + $bootstrap) "Database migration" | Out-Null
    Invoke-Checked $runner (@("scan-agents") + $bootstrap) "Installed agent registration" | Out-Null
    $diagnose = (Invoke-Checked $runner (@("diagnose") + $bootstrap) "Runner diagnosis")[-1] | ConvertFrom-Json
    if ($diagnose.status -ne "healthy" -or [int]$diagnose.activeRuns -ne 0) { throw "Runner diagnosis is not healthy." }

    if ($ExpectStartupTask) {
        $startup = (Invoke-Checked $runner (@("host-startup-status") + $bootstrap) "HostAtLogon status")[-1] | ConvertFrom-Json
        if (-not $startup.exists -or -not $startup.isManaged) { throw "The expected HostAtLogon task is absent or unmanaged." }
    }

    $primaryHost = Start-Process -FilePath $hostExecutable -ArgumentList @("--bootstrap-config", $configPath) -WorkingDirectory $app -WindowStyle Hidden -PassThru
    $hostStarted = $true
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    $ready = $false
    do {
        try {
            $health = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health/ready" -UseBasicParsing -TimeoutSec 2
            if ($health.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $ready) { throw "The synthetic Host did not become ready." }
    $second = Start-Process -FilePath $hostExecutable -ArgumentList @("--bootstrap-config", $configPath) -WorkingDirectory $app -WindowStyle Hidden -PassThru -Wait
    if ($second.ExitCode -ne 0) { throw "The second-instance activation check failed." }

    Invoke-Checked $runner (@("run-agent", "--agent-id", "wake-remote", "--command", "diagnose", "--arguments-json", "{}") + $bootstrap) "Wake Remote diagnostic" | Out-Null

    $imports = Join-Path $data "agents\founder-scout\imports"
    New-Item -ItemType Directory -Path $imports -Force | Out-Null
    $fixturePath = Join-Path $imports "smoke-profile.json"
    $capture = @([ordered]@{
        captureSchemaVersion = "1.0"
        source = "fixture"
        sourceAccountId = "smoke-account"
        sourceSegmentId = "smoke-segment"
        sourceProfileKey = "smoke-founder-001"
        profileUrl = "https://example.invalid/profile/smoke-founder-001"
        capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        displayName = "Synthetic Founder"
        rawText = "Synthetic founder leads customer operations for local retailers."
        structuredFields = [ordered]@{
            location = "Chicago, IL"; technical = "non-technical"; commitment = "full-time"; roles = @("operations", "sales")
            introduction = "Builds practical tools for local retailers."; background = "Led customer operations."; problem = "Supplier coordination takes too long."
            customer = "Independent retailers"; solution = "A supplier workflow."; traction = @("Completed 12 synthetic interviews.")
            cofounderRole = "Technical co-founder and CTO"; cofounderCommitment = "full-time"; equity = "founder-level partnership"; industries = @("local commerce")
        }
        sourceAdapterVersion = "fixture-1.0"
    })
    Set-Content -LiteralPath $fixturePath -Value ($capture | ConvertTo-Json -Depth 8) -Encoding UTF8
    $fixtureArgument = ConvertTo-NativeJsonArgument (@{ inputPath = $fixturePath } | ConvertTo-Json -Compress)
    Invoke-Checked $runner (@("run-agent", "--agent-id", "founder-scout", "--command", "import", "--arguments-json", $fixtureArgument) + $bootstrap) "Founder Scout fixture import" | Out-Null
    Invoke-Checked $runner (@("run-agent", "--agent-id", "founder-scout", "--command", "analyze", "--arguments-json", (ConvertTo-NativeJsonArgument '{"phase":"screen","max":20}')) + $bootstrap) "Founder Scout deterministic screen" | Out-Null
    Invoke-Checked $runner (@("run-agent", "--agent-id", "founder-scout", "--command", "report", "--arguments-json", (ConvertTo-NativeJsonArgument '{"type":"all","top":30}')) + $bootstrap) "Founder Scout report" | Out-Null

    $backup = (Invoke-Checked $runner (@("create-backup") + $bootstrap) "Database backup")[-1] | ConvertFrom-Json
    $backupSetId = [string]$backup.backupSet.backupSetId
    $validation = (Invoke-Checked $runner (@("validate-backup", "--backup-set", $backupSetId) + $bootstrap) "Backup validation")[-1] | ConvertFrom-Json
    if (-not $validation.isValid) { throw "The synthetic backup did not validate." }

    $diagnosticPath = Join-Path $data "smoke-diagnostics.zip"
    Invoke-WebRequest -Uri "http://127.0.0.1:$Port/Settings?handler=Diagnostics" -UseBasicParsing -UseDefaultCredentials -OutFile $diagnosticPath -TimeoutSec 15
    if (-not (Test-Path -LiteralPath $diagnosticPath) -or (Get-Item -LiteralPath $diagnosticPath).Length -le 0) { throw "Diagnostics export failed." }

    if ($AllowManagedWakeTask) {
        $prepared = (Invoke-Checked $runner (@("prepare-wake-test", "--minutes", "3") + $bootstrap) "Managed wake XML/task check")[-1] | ConvertFrom-Json
        $wakeOccurrence = [string]$prepared.occurrenceId
        Invoke-Checked $runner (@("remove-wake-task") + $bootstrap) "Managed wake cleanup" | Out-Null
    }

    $shutdown = Start-Process -FilePath $hostExecutable -ArgumentList @("--bootstrap-config", $configPath, "--shutdown") -WorkingDirectory $app -WindowStyle Hidden -PassThru -Wait
    if ($shutdown.ExitCode -ne 0) { throw "The synthetic Host rejected graceful shutdown." }
    if (-not $primaryHost.WaitForExit(20000)) { throw "The synthetic Host did not stop gracefully within 20 seconds." }
    $hostStarted = $false
    if (@(Get-Process -Name "HomeBusinessAssistant.Host", "FounderScout", "WakeRemote" -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($app + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        throw "Smoke validation left an application process running."
    }
    [ordered]@{
        status = "passed"
        productVersion = [string]$versions[0].productVersion
        hostReady = $true
        secondInstance = "passed"
        wakeRemoteDiagnostic = "passed"
        founderFixturePipeline = "import-screen-report"
        backupSetId = $backupSetId
        backupValidated = $true
        diagnosticsBytes = (Get-Item -LiteralPath $diagnosticPath).Length
        managedWakeTaskChanged = [bool]$AllowManagedWakeTask
    } | ConvertTo-Json -Compress
} finally {
    if ($hostStarted) {
        Start-Process -FilePath $hostExecutable -ArgumentList @("--bootstrap-config", $configPath, "--shutdown") -WorkingDirectory $app -WindowStyle Hidden -Wait | Out-Null
        if ($null -ne $primaryHost -and -not $primaryHost.WaitForExit(20000)) {
            Stop-Process -Id $primaryHost.Id -Force -ErrorAction SilentlyContinue
        }
    }
    if ($AllowManagedWakeTask) { & $runner remove-wake-task @bootstrap | Out-Null }
    if (-not $KeepSyntheticData -and (Test-Path -LiteralPath $data)) {
        Start-Sleep -Milliseconds 250
        Remove-Item -LiteralPath $data -Recurse -Force
    }
}
