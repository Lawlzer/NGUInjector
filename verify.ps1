#requires -Version 7.0
<#
.SYNOPSIS
Offline verification. Never reads the live bridge or changes the game.
.DESCRIPTION
All runs UI/browser, source, native, save-safety and runner checks. Other scopes
are focused passes, not full acceptance. Shared outputs have one fail-fast owner.
On contention or -DeferReason, append the request and -HandoffNote to
artifacts/agent-handoff.md and return 2. No waiting queue or automatic retry.
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'UI', 'Source', 'Native', 'Harness')][string]$Scope = 'All',
    [string[]]$Suite = @(),
    [string[]]$Selector = @(),
    [string]$GameFolder = $env:NGU_IDLE_DIR,
    [string]$ReportParent = (Join-Path $PSScriptRoot 'artifacts/verification'),
    [string]$ArtifactDirectory = '',
    [ValidateRange(1, 32)][int]$BrowserWorkers = 2,
    [string]$DeferReason = '',
    [string]$HandoffNote = '',
    [switch]$FunctionsOnly
)

$importOnly = [bool]$FunctionsOnly
. (Join-Path $PSScriptRoot 'test.ps1') -FunctionsOnly -ReportParent $ReportParent
$FunctionsOnly = $importOnly
. (Join-Path $PSScriptRoot 'checks/verification/Coordination.ps1')
. (Join-Path $PSScriptRoot 'checks/verification/Native.ps1')
. (Join-Path $PSScriptRoot 'checks/verification/Ui.ps1')

function Invoke-VerificationGate($Context, [string]$Name, [scriptblock]$Action) {
    $count = $Context.Report.errors.Count
    try { $null = & $Action }
    catch {
        if ($Context.Report.errors.Count -eq $count) {
            $Context.Report.errors.Add(@{ phase = $Name; status = 'FAIL'; message = $_.Exception.Message })
        }
    }
}

function Invoke-VerificationHarness($Context, [string]$Repo, [string]$GameFolder) {
    $pwsh = Join-Path $PSHOME 'pwsh.exe'
    $checks = Join-Path $Repo 'checks/SaveSafety'
    $msbuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe'
    if (-not $Context.NativeBuildAttempted) {
        $Context.NativeBuildAttempted = $true
        Invoke-VerificationGate $Context 'harness.production' {
            if (-not $GameFolder) { throw 'Pass -GameFolder or set NGU_IDLE_DIR for production-dependent harness checks.' }
            Invoke-TestAllNative $Context 'harness.productionBuild' $msbuild @((Join-Path $Repo 'NGUInjector/NGUInjector.csproj'), '/restore', '/t:Build', '/p:Configuration=Release', "/p:GameFolder=$GameFolder", '/nologo', '/v:minimal')
            $plugin = Join-Path $Repo 'NGUInjector/bin/Release/NGUInjector.dll'
            $Context.NativeArtifact = @{ path = $plugin; sha256 = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
                mvid = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($plugin)).ManifestModule.ModuleVersionId.ToString(); suites = @() }
        }
    }
    if ($Context.NativeArtifact) {
        Invoke-VerificationGate $Context 'harness.SessionChecks' {
            Invoke-TestAllNative $Context 'harness.buildSessionChecks' $msbuild @((Join-Path $checks 'SessionChecks.csproj'), '/restore', '/t:Build', '/p:Configuration=Release', '/nologo', '/v:minimal')
            Invoke-TestAllNative $Context 'harness.SessionChecks' (Join-Path $checks 'bin/SessionChecks/SaveTestSessionChecks.exe') @($Context.NativeArtifact.path, (Join-Path $GameFolder 'NGUIdle_Data/Managed'))
        }
    } else {
        $Context.Report.errors.Add(@{ phase = 'harness.productionConsumers'; status = 'BLOCKED'; message = 'No successfully built production artifact; session and packaged-codec checks cannot use an old DLL.' })
    }
    Invoke-VerificationGate $Context 'harness.inspector' { $null = Initialize-TestAllInspector $Context $Repo }
    $fixtures = Join-Path $Context.ReportDir 'synthetic-fixtures'
    Invoke-VerificationGate $Context 'harness.fixtures' {
        Invoke-TestAllNative $Context 'harness.buildFixtureGenerator' 'dotnet' @('build', (Join-Path $checks 'Fixtures/FixtureGenerator.csproj'), '--configuration', 'Release', '--nologo')
        Invoke-TestAllNative $Context 'harness.generateFixtures' (Join-Path $checks 'Fixtures/bin/Release/net48/FixtureGenerator.exe') @($fixtures)
        if ($Context.NativeArtifact) {
            Invoke-VerificationGate $Context 'harness.packagedCodec' {
                Invoke-TestAllNative $Context 'harness.buildPackagedCodec' $msbuild @((Join-Path $checks 'PackagedCodecChecks.csproj'), '/t:Build', '/p:Configuration=Release', '/nologo', '/v:minimal')
                Invoke-TestAllNative $Context 'harness.packagedCodec' (Join-Path $checks 'bin/PackagedCodec/PackagedCodecChecks.exe') @($Context.NativeArtifact.path, (Join-Path $fixtures 'valid.ok.txt'))
            }
        }
        foreach ($project in @('SaveSafetyChecks', 'BridgeChecks')) {
            Invoke-VerificationGate $Context "harness.$project" {
                Invoke-TestAllNative $Context "harness.$project" 'dotnet' @('run', '--project', (Join-Path $checks "$project.csproj"), '--configuration', 'Release', '--', $fixtures)
            }
        }
    }
    foreach ($script in @('DiagnosticsChecks', 'TestAllChecks')) {
        Invoke-VerificationGate $Context "harness.$script" {
            Invoke-TestAllNative $Context "harness.$script" $pwsh @('-NoProfile', '-NonInteractive', '-File', (Join-Path $checks "$script.ps1"))
        }
    }
    Invoke-VerificationGate $Context 'harness.RunnerChecks' {
        Invoke-TestAllNative $Context 'harness.RunnerChecks' $pwsh @('-NoProfile', '-NonInteractive', '-File', (Join-Path $Repo 'checks/verification/RunnerChecks.ps1'))
    }
}

function Invoke-Verification(
    [string]$Repo, [string]$Scope = 'All', [string[]]$Suite = @(), [string[]]$Selector = @(),
    [string]$GameFolder = '', [string]$ReportParent, [string]$ArtifactDirectory = '',
    [int]$BrowserWorkers = 2, [string]$DeferReason = '', [string]$HandoffNote = '',
    [string]$MutexName = 'Local\NGUInjector-Executor-Offline-Verification',
    [string]$NativeProcessBoundary = ''
) {
    $ErrorActionPreference = 'Stop'
    $Repo = [IO.Path]::GetFullPath($Repo)
    if ($Scope -notin @('All', 'UI', 'Source', 'Native', 'Harness')) { throw 'Unknown verification scope.' }
    if (($Suite.Count -or $Selector.Count) -and $Scope -ne 'Native') { throw 'Suite/Selector require -Scope Native; a focused pass is not All.' }
    $allSuites = @(Get-NativeVerificationSuites)
    if ($ArtifactDirectory -and ($Scope -notin @('All', 'Native') -or $Selector.Count -or ($Suite.Count -and (@($Suite | Sort-Object -Unique).Count -ne $allSuites.Count -or @($Suite | Where-Object { $_ -notin $allSuites }).Count)))) {
        throw 'Artifact capture requires all full native suites, without selectors.'
    }
    if (-not (Test-Path -LiteralPath $Repo -PathType Container)) { throw 'Repository is unavailable.' }
    if (-not $ReportParent) { $ReportParent = Join-Path $Repo 'artifacts/verification' }
    $ReportParent = [IO.Path]::GetFullPath($ReportParent)
    $null = [IO.Directory]::CreateDirectory($ReportParent)
    $id = [guid]::NewGuid().ToString('N')
    $directory = Join-Path $ReportParent $id
    $null = [IO.Directory]::CreateDirectory($directory)
    $quote = { param($s) "'" + $s.Replace("'", "''") + "'" }
    $command = '& ' + (& $quote (Join-Path $Repo 'verify.ps1')) + ' -Scope ' + $Scope
    if ($GameFolder) { $command += ' -GameFolder ' + (& $quote $GameFolder) }
    if ($Suite.Count) { $command += ' -Suite ' + (($Suite | ForEach-Object { & $quote $_ }) -join ',') }
    if ($Selector.Count) { $command += ' -Selector ' + (($Selector | ForEach-Object { & $quote $_ }) -join ',') }
    if ($ArtifactDirectory) { $command += ' -ArtifactDirectory ' + (& $quote $ArtifactDirectory) }
    if ($BrowserWorkers -ne 2) { $command += " -BrowserWorkers $BrowserWorkers" }
    $report = [ordered]@{
        schema = 'ngu.verify.offline'; version = 1; id = $id; scope = $Scope
        startedAt = [datetimeoffset]::UtcNow.ToString('o'); completedAt = $null
        status = 'RUNNING'; success = $false; exitCode = 1; fullOfflineGatePassed = $false
        reportDirectory = $directory; owner = $null; sourceBefore = $null; sourceAfter = $null
        request = @{ command = $command; suites = $Suite; selectors = $Selector; gameFolder = $GameFolder }
        phases = [Collections.Generic.List[object]]::new(); errors = [Collections.Generic.List[object]]::new()
        commands = @(); testedArtifact = $null; artifact = $null; handoff = $null
    }
    $context = @{ Repo = $Repo; ReportDir = $directory; Report = $report; BrowserWorkers = $BrowserWorkers
        NativeArtifact = $null; NativeBuildAttempted = $false; NativeProcessBoundary = $NativeProcessBoundary }
    Save-TestAllReport $context
    $mutex = [Threading.Mutex]::new($false, $MutexName)
    $held = $false
    $ownerPath = Join-Path $Repo 'artifacts/verification-owner.json'
    try {
        if ($DeferReason) {
            $report.errors.Add(@{ phase = 'coordination'; status = 'BLOCKED'; message = $DeferReason })
            Write-VerificationHandoff $context $DeferReason $HandoffNote
        } else {
            try { $held = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
            if (-not $held) {
                if ([IO.File]::Exists($ownerPath)) { try { $report.owner = Read-TestAllJson $ownerPath } catch { $report.owner = @{ reason = 'Owner metadata unavailable' } } }
                $reason = 'Another verification/build owns the shared outputs; no build or test was started.'
                $report.errors.Add(@{ phase = 'coordination'; status = 'BLOCKED'; message = $reason })
                Write-VerificationHandoff $context $reason $HandoffNote
            } else {
                $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $ownerPath))
                $report.owner = @{ id = $id; pid = $PID; command = $command; report = (Join-Path $directory 'report.json') }
                Write-TestAllJson $ownerPath $report.owner
                $identityGame = if ($Scope -in @('All', 'Native', 'Harness')) { $GameFolder } else { '' }
                try { $report.sourceBefore = Get-VerificationSourceStamp $Repo $identityGame }
                catch { $_.Exception.Data['VerificationBlocked'] = $true; throw }
                if ($Scope -in @('All', 'Native')) {
                    $context.NativeBuildAttempted = $true
                    Invoke-VerificationGate $context 'native' { Invoke-NativeVerification $context $Repo $GameFolder $Suite $Selector }
                }
                if ($Scope -in @('All', 'UI')) { Invoke-VerificationGate $context 'ui' { Invoke-UiVerification $context $Repo } }
                if ($Scope -in @('All', 'Source')) { Invoke-VerificationGate $context 'source' { Invoke-SourceVerification $context $Repo } }
                if ($Scope -in @('All', 'Harness')) { Invoke-VerificationGate $context 'harness' { Invoke-VerificationHarness $context $Repo $GameFolder } }
                try { $report.sourceAfter = Get-VerificationSourceStamp $Repo $identityGame }
                catch { $_.Exception.Data['VerificationBlocked'] = $true; throw }
                if ($report.sourceBefore -cne $report.sourceAfter) {
                    $reason = 'Build/test inputs changed during verification; no current-tree acceptance or artifact capture is claimed.'
                    $report.errors.Add(@{ phase = 'source.identity'; status = 'BLOCKED'; message = $reason })
                    Write-VerificationHandoff $context $reason $HandoffNote
                }
                if ($context.NativeArtifact -and (Get-FileHash -LiteralPath $context.NativeArtifact.path -Algorithm SHA256).Hash -ne $context.NativeArtifact.sha256) {
                    $exception = [InvalidOperationException]::new('Production artifact changed after native verification.')
                    $exception.Data['VerificationBlocked'] = $true
                    throw $exception
                }
                if ($ArtifactDirectory -and -not $report.errors.Count) {
                    $destination = [IO.Path]::GetFullPath($ArtifactDirectory)
                    if (-not (Test-Path -LiteralPath (Split-Path -Parent $destination) -PathType Container) -or (Test-Path -LiteralPath $destination)) { throw 'Artifact capture needs an existing parent and a new destination.' }
                    $dist = Join-Path $Repo 'NGUInjector/bin/Release/dist/injector'
                    if ((Get-FileHash -LiteralPath (Join-Path $dist 'NGUInjector.dll') -Algorithm SHA256).Hash -ne $context.NativeArtifact.sha256) { throw 'Distribution DLL differs from the tested artifact.' }
                    $null = [IO.Directory]::CreateDirectory($destination)
                    Copy-Item -LiteralPath $dist -Destination (Join-Path $destination 'injector') -Recurse
                    Copy-Item -LiteralPath $directory -Destination (Join-Path $destination 'verification') -Recurse
                    $manifest = @{ verifiedAt = [datetimeoffset]::UtcNow.ToString('o'); sha256 = $context.NativeArtifact.sha256
                        mvid = $context.NativeArtifact.mvid; suites = $context.NativeArtifact.suites; scope = $Scope
                        sourceStamp = $report.sourceAfter; fullOfflineVerified = $Scope -eq 'All'; liveVerified = $false }
                    Write-TestAllJson (Join-Path $destination 'manifest.json') $manifest
                    $report.artifact = $destination
                }
            }
        }
    } catch {
        $status = if ($_.Exception.Data['VerificationBlocked']) { 'BLOCKED' } else { 'FAIL' }
        $report.errors.Add(@{ phase = 'verification'; status = $status; message = $_.Exception.Message })
        if ($status -eq 'BLOCKED') { Write-VerificationHandoff $context $_.Exception.Message $HandoffNote }
    } finally {
        $report.testedArtifact = $context.NativeArtifact
        $failed = @($report.errors | Where-Object status -EQ 'FAIL').Count -gt 0
        $blocked = @($report.errors | Where-Object status -EQ 'BLOCKED').Count -gt 0
        $report.exitCode = if ($failed) { 1 } elseif ($blocked) { 2 } else { 0 }
        $report.status = if ($failed) { 'FAIL' } elseif ($blocked) { 'BLOCKED' } else { 'PASS' }
        $report.success = $report.exitCode -eq 0
        $report.fullOfflineGatePassed = $report.success -and $Scope -eq 'All'
        $report.completedAt = [datetimeoffset]::UtcNow.ToString('o')
        try {
            Save-TestAllReport $context
            if ($report.artifact) { Copy-Item -LiteralPath (Join-Path $directory 'report.json') -Destination (Join-Path $report.artifact 'verification/report.json') }
        }
        finally {
            if ($held) {
                try { if ([IO.File]::Exists($ownerPath) -and (Read-TestAllJson $ownerPath).id -eq $id) { [IO.File]::Delete($ownerPath) } }
                finally { $mutex.ReleaseMutex() }
            }
            $mutex.Dispose()
        }
    }
    return $report
}

if ($FunctionsOnly) { return }
try {
    $result = Invoke-Verification -Repo $PSScriptRoot -Scope $Scope -Suite $Suite -Selector $Selector -GameFolder $GameFolder -ReportParent $ReportParent -ArtifactDirectory $ArtifactDirectory -BrowserWorkers $BrowserWorkers -DeferReason $DeferReason -HandoffNote $HandoffNote
    "verify: $($result.status) (scope $Scope; full offline gate: $($result.fullOfflineGatePassed))"
    foreach ($issue in $result.errors) { "$($issue.status): $($issue.phase): $($issue.message)" }
    "Report: $(Join-Path $result.reportDirectory 'report.json')"
    if ($result.handoff) {
        if ($result.handoff.written) { "Have the next agent continue from $($result.handoff.path)" }
        else { "Handoff append failed: $($result.handoff.error). Complete entry retained in the report above." }
    }
    exit $result.exitCode
} catch {
    [Console]::Error.WriteLine('verify: FAIL: ' + $_.Exception.Message)
    exit 1
}
