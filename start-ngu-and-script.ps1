param(
    [string]$GameFolder = "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE",
    [string]$Configuration = "Release",
    [string]$ProbeMode = "safe",
    [string[]]$ProbeCommands = @("status", "optimize Fight"),
    [bool]$LoadCloudSave = $true,
    [switch]$NoBuild,
    [switch]$ExternalUi,
    [switch]$StartUi,
    [switch]$ForceRestart,
    [int]$ShutdownWaitSeconds = 20,
    [int]$LaunchWaitSeconds = 15,
    [int]$InjectWaitSeconds = 10,
    [int]$InjectRetrySeconds = 60
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionPath = Join-Path $RepoRoot "NGUInjector.sln"
$ProjectDir = Join-Path $RepoRoot "NGUInjector"
$GameExe = Join-Path $GameFolder "NGUIdle.exe"
$DesktopInjectorDir = Join-Path $env:USERPROFILE "Desktop\NGUInjector"
$ProbeFile = Join-Path $DesktopInjectorDir "probe.txt"
$AutorunFile = Join-Path $DesktopInjectorDir "probe.autorun"
$CloudLoadAutorunFile = Join-Path $DesktopInjectorDir "cloud-load.autorun"
$MsBuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

if (!(Test-Path -LiteralPath $RepoRoot)) {
    throw "Repo root not found: $RepoRoot"
}

if (!(Test-Path -LiteralPath $GameExe)) {
    throw "NGUIdle.exe not found: $GameExe"
}

if (!(Test-Path -LiteralPath $MsBuild)) {
    throw "MSBuild not found: $MsBuild"
}

if (!(Test-Path -LiteralPath $DesktopInjectorDir)) {
    New-Item -ItemType Directory -Path $DesktopInjectorDir | Out-Null
}

if ($ProbeMode -notin @("safe", "commands")) {
    throw "ProbeMode must be 'safe' or 'commands'."
}

# Close standalone controllers; the legacy WinForms UI closes with the game below.
foreach ($ui in @(Get-Process -Name 'nguinjector-ui' -ErrorAction SilentlyContinue)) {
    $ui | Stop-Process -Force
    if (!$ui.WaitForExit([Math]::Max(1, $ShutdownWaitSeconds) * 1000)) {
        throw "Injector UI process $($ui.Id) did not exit."
    }
}

if (!$NoBuild) {
    $msbuildArgs = @(
        $SolutionPath,
        "/t:Build",
        "/p:Configuration=$Configuration",
        "/p:GameFolder=$GameFolder",
        "/m"
    )
    & $MsBuild @msbuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }
}

$DistDir = Join-Path $ProjectDir "bin\$Configuration\dist"
$InjectorDir = Join-Path $DistDir "injector"
$SmiPath = Join-Path $InjectorDir "smi.exe"
$DllPath = Join-Path $InjectorDir "NGUInjector.dll"

if (!(Test-Path -LiteralPath $SmiPath)) {
    throw "Injector executable not found: $SmiPath"
}

if (!(Test-Path -LiteralPath $DllPath)) {
    throw "NGUInjector.dll not found: $DllPath"
}

$existing = Get-Process -Name "NGUIdle" -ErrorAction SilentlyContinue
if ($existing) {
    foreach ($game in $existing) {
        if (!$game.CloseMainWindow()) {
            if (!$ForceRestart) { throw 'NGU did not accept a graceful close. Save/close the game or explicitly pass -ForceRestart.' }
            $game | Stop-Process -Force
        } elseif (!$game.WaitForExit([Math]::Max(1, $ShutdownWaitSeconds) * 1000)) {
            if (!$ForceRestart) { throw 'NGU has not finished closing. Restart aborted without force-killing it.' }
            $game | Stop-Process -Force
        }
    }
    Start-Sleep -Seconds 2
}

# Publish startup requests only after the previous injector has released the runtime directory.
Set-Content -LiteralPath $ProbeFile -Value $ProbeCommands -Encoding UTF8
Set-Content -LiteralPath $AutorunFile -Value $ProbeMode -Encoding ASCII
if ($LoadCloudSave) {
    Set-Content -LiteralPath $CloudLoadAutorunFile -Value "cloud" -Encoding ASCII
} elseif (Test-Path -LiteralPath $CloudLoadAutorunFile) {
    Remove-Item -LiteralPath $CloudLoadAutorunFile -Force
}
if ($ExternalUi) {
    Set-Content -LiteralPath (Join-Path $DesktopInjectorDir 'external-ui.enabled') -Value 'NGUInjector external UI enabled' -Encoding ASCII
}

Start-Process -FilePath $GameExe -WorkingDirectory $GameFolder | Out-Null

$deadline = (Get-Date).AddSeconds($LaunchWaitSeconds)
do {
    Start-Sleep -Milliseconds 500
    $process = Get-Process -Name "NGUIdle" -ErrorAction SilentlyContinue
} while (!$process -and (Get-Date) -lt $deadline)

if (!$process) {
    throw "NGUIdle process did not start within $LaunchWaitSeconds seconds."
}

Start-Sleep -Seconds $InjectWaitSeconds

$injectDeadline = (Get-Date).AddSeconds($InjectRetrySeconds)
$injected = $false
$injectedProcessId = 0
$lastInjectionText = ""
do {
    $targets = @(Get-Process -Name 'NGUIdle' -ErrorAction SilentlyContinue)
    if ($targets.Count -ne 1) {
        Start-Sleep -Seconds 2
        continue
    }
    $target = $targets[0]
    $injectOutput = & $SmiPath inject -p NGUIdle -a $DllPath -n NGUInjector -c Loader -m Init 2>&1
    $injectExitCode = $LASTEXITCODE
    $lastInjectionText = ($injectOutput | Out-String).Trim()

    if ($lastInjectionText.Length -gt 0) {
        $lastInjectionText
    }

    $failedByText = $lastInjectionText -match "Failed to inject|InjectorException|Exception"
    if ($injectExitCode -eq 0 -and !$failedByText) {
        $injected = $true
        $injectedProcessId = $target.Id
        break
    }

    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $injectDeadline)

if (!$injected) {
    throw "Injection failed after $InjectRetrySeconds seconds. Last injector output: $lastInjectionText"
}

"Started NGU, injected NGUInjector, requested cloud-load=$LoadCloudSave, and requested '$ProbeMode' startup probe."
"Probe file: $ProbeFile"
"Probe reports will appear under: $(Join-Path $DesktopInjectorDir 'logs')"

if ($ExternalUi) {
    $runtimePath = Join-Path $DesktopInjectorDir 'runtime.json'
    $runtimeDeadline = (Get-Date).AddSeconds(30)
    $connected = $false
    do {
        Start-Sleep -Milliseconds 500
        $targets = @(Get-Process -Name 'NGUIdle' -ErrorAction SilentlyContinue)
        if ($targets.Count -ne 1) { continue }
        $target = $targets[0]
        # Steam can replace the initial process after Mono injection succeeds.
        if ($target.Id -ne $injectedProcessId) {
            if (((Get-Date) - $target.StartTime).TotalSeconds -lt [Math]::Max(1, $InjectWaitSeconds)) { continue }
            $injectOutput = & $SmiPath inject -p NGUIdle -a $DllPath -n NGUInjector -c Loader -m Init 2>&1
            $injectExitCode = $LASTEXITCODE
            $lastInjectionText = ($injectOutput | Out-String).Trim()
            if ($injectExitCode -ne 0 -or $lastInjectionText -match 'Failed to inject|InjectorException|Exception') {
                Start-Sleep -Seconds 2
                continue
            }
            $injectedProcessId = $target.Id
        }
        if (Test-Path -LiteralPath $runtimePath) {
            try {
                $runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
                $fresh = [DateTimeOffset]::Now - [DateTimeOffset]::Parse($runtime.updatedAt)
                if ([DateTimeOffset]::Parse($runtime.startedAt) -lt [DateTimeOffset]$target.StartTime) { continue }
                if ($runtime.status -eq 'failed' -and $fresh.TotalSeconds -lt 5) { throw "Injector startup failed: $($runtime.startupError)" }
                $connected = $runtime.status -eq 'running' -and $runtime.externalUiEnabled -and $runtime.protocolVersion -ge 3 -and $fresh.TotalSeconds -ge 0 -and $fresh.TotalSeconds -lt 5
            } catch {
                if ($_.Exception.Message -like 'Injector startup failed:*') { throw }
            }
        }
    } while (!$connected -and (Get-Date) -lt $runtimeDeadline)
    if (!$connected) { throw 'Injection was reported successful, but the external UI heartbeat did not become ready.' }
    "External UI connected: protocol $($runtime.protocolVersion), $($runtime.status)."
}

if ($StartUi) {
    & (Join-Path $RepoRoot 'start-ui.ps1')
}
