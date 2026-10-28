#requires -Version 7.0
<#
Isolated harness checks. Run with pwsh -NoProfile -File checks/SaveSafety/TestAllChecks.ps1.
Uses real temporary runtime/queue/result files and a separate PowerShell bridge process.
Only the external backend is faked: harness functions, hashing, validation, timeouts,
atomic publication and reporting run unchanged. Inspector integration uses the actual
compiled CLI and generated native fixtures. Only standalone check tools are built;
no game, deployment or production injector build is involved. Native process helper
checks retain a real child-process boundary.
#>
[CmdletBinding()]
param([string]$CaseFilter = '*')

& {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off
    $harness = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../test.ps1'))
    . $harness -FunctionsOnly
    $parent = Join-Path $env:LOCALAPPDATA 'Temp/opencode'
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Approved temporary parent does not exist.' }
    $root = Join-Path $parent ('ngu-test-all-checks-' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($root)
    $results = [Collections.Generic.List[object]]::new()
    $jobs = [Collections.Generic.List[object]]::new()
    $toolState = @{}
    $completedSuccessfully = $false

    function Assert-Equal($Actual, $Expected, [string]$Label = 'value') {
        $actualJson = ConvertTo-Json -InputObject $Actual -Depth 100 -Compress
        $expectedJson = ConvertTo-Json -InputObject $Expected -Depth 100 -Compress
        if ($actualJson -cne $expectedJson) { throw "$Label expected $expectedJson, got $actualJson" }
    }
    function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
        $message = $null
        try { $null = & $Action } catch { $message = $_.Exception.Message }
        if ($null -eq $message -or $message -notmatch $Pattern) { throw "Expected exception '$Pattern', got '$message'" }
    }
    function New-Context {
        $directory = Join-Path $root ([guid]::NewGuid().ToString('N'))
        foreach ($child in @('', 'runtime', 'runtime/commands', 'runtime/commands/claimed', 'runtime/command-results', 'reports')) {
            $null = [IO.Directory]::CreateDirectory((Join-Path $directory $child))
        }
        $report = @{ phases = [Collections.Generic.List[object]]::new(); commands = [Collections.Generic.List[object]]::new()
            errors = [Collections.Generic.List[object]]::new(); restoration = @{ originalBackupVerified = $false; required = $false; attempted = $false; restored = $false }
            restorationErrors = [Collections.Generic.List[string]]::new(); baseline = $null; liveFixturesExecuted = 0 }
        return @{ RuntimeDir = (Join-Path $directory 'runtime'); ReportDir = (Join-Path $directory 'reports'); Report = $report
            Session = $null; ConfirmLiveSaveLoad = $false; CommandTimeoutSeconds = 2; ScanTimeoutSeconds = 3; PollMilliseconds = 30
            TransitionTimeoutSeconds = 2; SettleTimeoutSeconds = 2; StableSamples = 3; SettleMilliseconds = 100 }
    }
    function Get-Requests($Context) {
        return ,@([IO.Directory]::GetFiles((Join-Path $Context.RuntimeDir 'commands/claimed'), '*.claimed') | Sort-Object | ForEach-Object { Read-TestAllJson $_ })
    }
    function Get-StableRuntimeFiles($Context) {
        $runtimePath = [regex]::Escape((Join-Path $Context.RuntimeDir 'runtime.json'))
        # Only the fake heartbeat writer's atomic-publication files are transient.
        return ,@([IO.Directory]::GetFiles($Context.RuntimeDir, '*', [IO.SearchOption]::AllDirectories) |
            Where-Object { $_ -notmatch ('^' + $runtimePath + '(?:\.[a-f0-9]{32})?\.tmp$') } | Sort-Object)
    }
    function Stop-Bridge($Job) {
        Stop-Job -Job $Job -ErrorAction SilentlyContinue
        $failure = $Job.ChildJobs[0].JobStateInfo.Reason
        $messages = @(Receive-Job -Job $Job -ErrorAction SilentlyContinue)
        Remove-Job -Job $Job -Force
        $null = $jobs.Remove($Job)
        if ($failure) { throw "Fake bridge failed: $failure $($messages -join ' ')" }
    }
    function Start-Bridge($Context, [string]$Behavior = 'Normal') {
        $job = Start-Job -ArgumentList $Context.RuntimeDir, $Behavior, (Join-Path $PSScriptRoot 'diagnostics-fixtures.json') -ScriptBlock {
            param($Directory, $Behavior, $DiagnosticsFixture)
            $ErrorActionPreference = 'Stop'
            function Copy-Json($Value) {
                $options = @{ AsHashtable = $true }
                if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $options.DateKind = 'String' }
                return ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 100) @options
            }
            function Write-Json($Path, $Value) {
                $temporary = $Path + '.tmp'
                [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $Value -Depth 100))
                for ($attempt = 0; ; $attempt++) {
                    try { [IO.File]::Move($temporary, $Path, $true); break }
                    catch {
                        if ($attempt -ge 20) { throw }
                        Start-Sleep -Milliseconds 10
                    }
                }
            }
            function Get-Marker($Path, [string]$Total, [string]$Rebirth = '7', [string]$Elapsed = '60.5') {
                return @{ path = $Path; sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
                    totalPlaytimeSeconds = $Total; rebirth = $Rebirth; rebirthSeconds = $Elapsed; version = 1260; playerName = 'Synthetic Player' }
            }
            $longestPath = Join-Path $Directory 'longest.txt'
            [IO.File]::WriteAllText($longestPath, 'fake external backend save bytes, not a native fixture')
            # Deliberately OLDER mtime: recovery must use the backend's decoded playtime.
            [IO.File]::SetLastWriteTimeUtc($longestPath, [datetime]::UtcNow.AddYears(-1))
            $behind = $Behavior.StartsWith('Behind') -or $Behavior -eq 'LoadNotVerified'
            $longest = Get-Marker $longestPath $(if ($behind) { '2000.25' } else { '1000.25' })
            $current = Copy-Json $longest
            $current.path = ''; $current.sha256 = ''; $current.totalPlaytimeSeconds = '1000.25'
            $status = if ($behind) { 'behind' } else { 'verified' }
            $runtime = @{ startedAt = [datetimeoffset]::UtcNow.AddDays(-7).ToString('o'); updatedAt = [datetimeoffset]::UtcNow.ToString('o')
                runtimeRevision = 1; status = 'running'; execution = @{ lifecycle = 'Running' }
                supportedCommands = @('captureLocalSave', 'scanSaves', 'loadLongestSave')
                saveSafety = @{ version = 1; status = $status; scanComplete = $true; pendingFiles = 0; errors = @(); testActive = $false
                    current = $current; longest = $longest; ambiguousLongest = $false; liveFixtureTestingSupported = $false
                    liveFixtureBlocker = 'Synthetic isolation blocker: autosave/cloud/achievements/checkpoints are not isolated.'; message = 'Synthetic backend' } }
            $diagnosticRuntime = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($DiagnosticsFixture)) -AsHashtable
            foreach ($key in $diagnosticRuntime.Keys) {
                if (-not $runtime.ContainsKey($key)) { $runtime[$key] = $diagnosticRuntime[$key] }
            }
            $runtime.execution = $diagnosticRuntime.execution
            if ($Behavior -in @('BehindAmbiguous', 'CurrentAmbiguous')) {
                $runtime.saveSafety.ambiguousLongest = $true
                $runtime.saveSafety.status = $status = 'unknown'
            }
            if ($Behavior -eq 'BehindForeign') { $longest.playerName = 'Other Player' }
            function Publish {
                $runtime.runtimeRevision++
                $runtime.updatedAt = [datetimeoffset]::UtcNow.ToString('o')
                Write-Json (Join-Path $Directory 'runtime.json') $runtime
            }
            Publish
            [IO.File]::WriteAllText((Join-Path $Directory 'ready'), 'ready')
            $scanning = $false
            $clock = [Diagnostics.Stopwatch]::StartNew()
            while ($clock.Elapsed.TotalSeconds -lt 45) {
                if ($scanning -and $Behavior -ne 'ScanTimeout') {
                    $runtime.saveSafety.pendingFiles = [Math]::Max(0, $runtime.saveSafety.pendingFiles - 25)
                    if ($runtime.saveSafety.pendingFiles -eq 0) { $runtime.saveSafety.scanComplete = $true; $runtime.saveSafety.status = $status; $scanning = $false }
                }
                Publish
                foreach ($path in [IO.Directory]::GetFiles((Join-Path $Directory 'commands'), '*.json')) {
                    $options = @{ AsHashtable = $true }
                    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $options.DateKind = 'String' }
                    $request = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($path)) @options
                    $claim = Join-Path $Directory "commands/claimed/$($request.id).claimed"
                    [IO.File]::Move($path, $claim)
                    if ($Behavior -eq 'NoAck') { continue }
                    $data = @{}
                    switch ($request.command) {
                        'captureLocalSave' {
                            $savePath = Join-Path $Directory ('backup-' + $request.id + '.txt')
                            [IO.File]::WriteAllText($savePath, 'fake native export from external backend')
                            $save = Copy-Json $current
                            $save.path = $savePath; $save.sha256 = (Get-FileHash -LiteralPath $savePath -Algorithm SHA256).Hash.ToLowerInvariant()
                            switch ($Behavior) {
                                'CaptureMissing' { [IO.File]::Delete($savePath) }
                                'CaptureHash' { $save.sha256 = '0' * 64 }
                                'CaptureClock' { $save.totalPlaytimeSeconds = '11000.25' }
                                'CaptureRebirth' { $save.rebirth = '8' }
                            }
                            $data.save = $save
                        }
                        'scanSaves' {
                            $scanning = $true
                            $runtime.saveSafety.scanComplete = $false
                            $runtime.saveSafety.pendingFiles = 400
                            if ($Behavior -eq 'ScanError') { $runtime.saveSafety.errors = @(@{ path = 'broken'; message = 'bad save' }) }
                            $data.saveSafety = Copy-Json $runtime.saveSafety
                        }
                        'loadLongestSave' {
                            if ($request.args.sha256 -cne $longest.sha256 -or $request.args.expectedStartedAt -cne $runtime.startedAt) { throw 'Harness sent an unguarded load.' }
                            $backupPath = Join-Path $Directory 'protective-before-load.txt'
                            [IO.File]::WriteAllText($backupPath, 'backend protective backup')
                            $backup = Copy-Json $current
                            $backup.path = $backupPath; $backup.sha256 = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
                            $data.backup = $backup; $data.selected = Copy-Json $longest; $data.loaded = $true
                            if ($Behavior -ne 'LoadNotVerified') {
                                $runtime.saveSafety.current = Copy-Json $longest
                                $runtime.saveSafety.current.path = ''; $runtime.saveSafety.current.sha256 = ''
                                $runtime.saveSafety.status = 'verified'; $status = 'verified'
                            }
                        }
                        default { throw "Harness sent forbidden command: $($request.command)" }
                    }
                    Publish
                    $data.runtime = Copy-Json $runtime
                    $result = @{ id = $request.id; command = $request.command; success = $true; data = $data; message = 'synthetic result'; completedAt = [datetimeoffset]::UtcNow.ToString('o') }
                    switch ($Behavior) {
                        'BadId' { $result.id = 'wrong-id' }
                        'BadCommand' { $result.command = 'quickSave' }
                        'BadSuccess' { $result.success = 'true' }
                        'BadSession' { $result.data.runtime.startedAt = [datetimeoffset]::UtcNow.ToString('o') }
                        'BadCompletedAt' { $result.completedAt = [datetimeoffset]::UtcNow.AddMinutes(1).ToString('o') }
                        'StaleResult' { $result.completedAt = [datetimeoffset]::UtcNow.AddMinutes(-1).ToString('o') }
                        'BackendFailure' { $result.success = $false; $result.message = 'synthetic backend rejection' }
                    }
                    Write-Json (Join-Path $Directory "command-results/$($request.id).json") $result
                }
                Start-Sleep -Milliseconds 40
            }
        }
        $jobs.Add($job)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not [IO.File]::Exists((Join-Path $Context.RuntimeDir 'ready'))) {
            if ($job.State -in @('Failed', 'Completed') -or $clock.Elapsed.TotalSeconds -gt 20) { throw 'External test bridge did not start.' }
            Start-Sleep -Milliseconds 30
        }
        return $job
    }
    function Start-LiveBridge($Context, [string]$Behavior = 'Normal', [double]$AutomationTimeScale = 0.1) {
        $job = Start-Job -ArgumentList (Join-Path $PSScriptRoot 'TestAllFakeBackend.ps1'), $Context.RuntimeDir, $Behavior, $harness, $Context.ReportDir, $AutomationTimeScale -ScriptBlock {
            param($Backend, $Directory, $Behavior, $Harness, $ReportDir, $AutomationTimeScale)
            & $Backend -Directory $Directory -Behavior $Behavior -Harness $Harness -ReportDir $ReportDir -AutomationTimeScale $AutomationTimeScale
        }
        $jobs.Add($job)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not [IO.File]::Exists((Join-Path $Context.RuntimeDir 'ready'))) {
            if ($job.State -in @('Failed', 'Completed') -or $clock.Elapsed.TotalSeconds -gt 20) { Stop-Bridge $job; throw 'External live-test fake did not start.' }
            Start-Sleep -Milliseconds 30
        }
        return $job
    }
    function Assert-GameReport($Report, [string]$Mode) {
        Assert-Equal $Report.schema 'ngu.test.game'
        Assert-Equal $Report.version 2
        Assert-Equal $Report.scope 'game'
        Assert-Equal $Report.mode $Mode
        Assert-Equal @($Report.phases | Where-Object name -Like 'offline.*').Count 0
    }
    function Test-Case([string]$Name, [scriptblock]$Action, [switch]$NativeTools) {
        if ($Name -notlike $CaseFilter) { return }
        try {
            if ($NativeTools) {
                Initialize-CheckTools
                $tools = $toolState.context; $inspectorDll = $toolState.inspectorDll; $nativeFixtures = $toolState.nativeFixtures
            }
            $null = & $Action
            $results.Add(@{ name = $Name; success = $true; error = $null })
        } catch {
            $results.Add(@{ name = $Name; success = $false; error = $_.ToString() + ' at ' + $_.ScriptStackTrace })
        }
    }
    try {
        Test-Case 'FunctionsOnly imports without invoking checks or live commands' {
            Assert-Equal (Get-Command Invoke-TestAllPrepare).CommandType ([Management.Automation.CommandTypes]::Function)
            Assert-Equal (Get-Command Invoke-TestAllOffline -ErrorAction SilentlyContinue) $null
            Assert-Equal @([IO.Directory]::GetFiles($root, '*', [IO.SearchOption]::AllDirectories)).Count 0
        }
        Test-Case 'fresh local capture uses atomic queue envelope with session-preserving timestamps' {
            $context = New-Context; $job = Start-Bridge $context
            try {
                $runtime = Read-TestAllRuntime $context; $context.Session = $runtime.startedAt
                $result = Invoke-TestAllCommand $context 'captureLocalSave'
                $requests = Get-Requests $context
                Assert-Equal $requests.Count 1
                Assert-Equal $requests[0].command 'captureLocalSave'
                Assert-Equal $requests[0].args.Count 0
                Assert-Equal $result.id $requests[0].id
                Assert-Equal $context.Report.commands[0].outcome 'SUCCEEDED'
                $lifetime = ((Get-TestAllTimestamp $requests[0].expiresAt) - (Get-TestAllTimestamp $requests[0].createdAt)).TotalSeconds
                Assert-Equal $lifetime ([double]2)
                Assert-Equal @([IO.Directory]::GetFiles((Join-Path $context.RuntimeDir 'commands'), '*.tmp')).Count 0
                Assert-Equal (Read-TestAllRuntime $context).startedAt $context.Session
            } finally { Stop-Bridge $job }
        }
        Test-Case 'preparation captures original before a 400-file asynchronous scan' {
            $context = New-Context; $job = Start-Bridge $context
            try {
                $prepared = Invoke-TestAllPrepare $context
                Assert-Equal $prepared.saveSafety.status 'verified'
                Assert-Equal $prepared.saveSafety.pendingFiles 0
                Assert-Equal $context.Report.preparationPassed $true
                Assert-Equal $context.Report.restoration.originalBackupVerified $true
                Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves')
                Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.id } | Sort-Object -Unique).Count 2
                Assert-Equal $context.Report.phases[1].evidence.independentlyDecoded $false
                Assert-Equal (Read-TestAllJson (Join-Path $context.ReportDir 'report.json')).baseline.sha256 $context.Report.baseline.sha256
            } finally { Stop-Bridge $job }
        }
        Test-Case 'behind without confirmation retains baseline and never loads' {
            $context = New-Context; $job = Start-Bridge $context 'Behind'
            try {
                Assert-Throws { Invoke-TestAllPrepare $context } 'ConfirmLiveSaveLoad'
                Assert-Equal $context.Report.phases[-1].status 'BLOCKED'
                Assert-Equal $context.Report.restoration.originalBackupVerified $true
                Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves')
                Assert-Equal (Test-Path -LiteralPath $context.Report.baseline.path) $true
            } finally { Stop-Bridge $job }
        }
        Test-Case 'confirmed recovery pins longest PLAYTIME hash even though its file date is older' {
            $context = New-Context; $context.ConfirmLiveSaveLoad = $true; $job = Start-Bridge $context 'Behind'
            try {
                $prepared = Invoke-TestAllPrepare $context
                Assert-Equal $prepared.saveSafety.current.totalPlaytimeSeconds '2000.25'
                Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves', 'loadLongestSave')
                $load = $context.Report.commands[2].request
                Assert-Equal $load.args.sha256 $prepared.saveSafety.longest.sha256
                Assert-Equal $load.args.expectedStartedAt $context.Session
                Assert-Equal $load.args.Count 2
                Assert-Equal ([IO.File]::GetLastWriteTimeUtc($prepared.saveSafety.longest.path) -lt [IO.File]::GetLastWriteTimeUtc($context.Report.baseline.path)) $true
                Assert-Equal $context.Report.baseline.totalPlaytimeSeconds '1000.25'
                Assert-Equal $context.Report.originalBaselineAfterRecovery.totalPlaytimeSeconds '2000.25'
                Assert-Throws { Assert-TestAllProgress $context.Report.originalBaselineAfterRecovery $context.Report.baseline } 'behind restoreTarget'
                Assert-Equal $context.Report.recovery.loaded $true
            } finally { Stop-Bridge $job }
        }
        foreach ($behavior in @('BehindAmbiguous', 'CurrentAmbiguous', 'BehindForeign')) {
            Test-Case "manual review blocks unsafe recovery: $behavior" {
                $context = New-Context; $context.ConfirmLiveSaveLoad = $true; $job = Start-Bridge $context $behavior
                try {
                    Assert-Throws { Invoke-TestAllPrepare $context } 'manual review'
                    Assert-Equal $context.Report.commands.Count 2
                    Assert-Equal $context.Report.phases[-1].status 'BLOCKED'
                    if ($behavior -ne 'BehindForeign') {
                        Assert-Equal $context.Report.phases[-1].name 'prepare.fullScan'
                        Assert-Equal $context.Report.lastScan.saveSafety.status 'unknown'
                        Assert-Equal $context.Report.lastScan.saveSafety.ambiguousLongest $true
                    }
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($behavior in @('CaptureMissing', 'CaptureHash', 'CaptureClock', 'CaptureRebirth')) {
            Test-Case "invalid baseline prevents scanning and loading: $behavior" {
                $context = New-Context; $job = Start-Bridge $context $behavior
                try {
                    Assert-Throws { Invoke-TestAllPrepare $context } 'does not exist|SHA-256|tolerance|rebirth'
                    Assert-Equal $context.Report.commands.Count 1
                    Assert-Equal $context.Report.restoration.originalBackupVerified $false
                    Assert-Equal $context.Report.phases[-1].status 'FAIL'
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($behavior in @('BadId', 'BadCommand', 'BadSuccess', 'BadSession', 'BadCompletedAt', 'StaleResult')) {
            Test-Case "untrusted result remains UNKNOWN and is not replayed: $behavior" {
                $context = New-Context; $job = Start-Bridge $context $behavior
                try {
                    $context.Session = (Read-TestAllRuntime $context).startedAt
                    Assert-Throws { Invoke-TestAllCommand $context 'captureLocalSave' } 'mismatch|session changed|timestamp.*lifetime'
                    Assert-Equal $context.Report.commands.Count 1
                    Assert-Equal $context.Report.commands[0].outcome 'UNKNOWN'
                    Assert-Equal (Get-Requests $context).Count 1
                } finally { Stop-Bridge $job }
            }
        }
        Test-Case 'backend rejection is FAILED, not successful or replayed' {
            $context = New-Context; $job = Start-Bridge $context 'BackendFailure'
            try {
                Assert-Throws { Invoke-TestAllCommand $context 'captureLocalSave' } 'synthetic backend rejection'
                Assert-Equal $context.Report.commands[0].outcome 'FAILED'
                Assert-Equal (Get-Requests $context).Count 1
            } finally { Stop-Bridge $job }
        }
        Test-Case 'missing result times out UNKNOWN while persistent claim and unrelated files survive' {
            $context = New-Context; $context.CommandTimeoutSeconds = 1; $job = Start-Bridge $context 'NoAck'
            try {
                $sentinel = Join-Path $context.RuntimeDir 'commands/claimed/existing.claimed'
                [IO.File]::WriteAllText($sentinel, 'persistent sentinel')
                Assert-Throws { Invoke-TestAllCommand $context 'captureLocalSave' } 'Timed out.*UNKNOWN.*Do not replay'
                Assert-Equal $context.Report.commands[0].outcome 'UNKNOWN'
                Assert-Equal ([IO.File]::ReadAllText($sentinel)) 'persistent sentinel'
                Assert-Equal ([IO.File]::Exists($context.Report.commands[0].claimPath)) $true
                Assert-Equal $context.Report.commands.Count 1
            } finally { Stop-Bridge $job }
        }
        foreach ($behavior in @('ScanError', 'ScanTimeout', 'LoadNotVerified')) {
            Test-Case "incomplete or contradicted safety cannot pass preparation: $behavior" {
                $context = New-Context; $context.ScanTimeoutSeconds = 1; $context.ConfirmLiveSaveLoad = $true
                $job = Start-Bridge $context $behavior
                try {
                    Assert-Throws { Invoke-TestAllPrepare $context } 'scan/recovery error|scan timed out|recovery was not freshly verified'
                    Assert-Equal $context.Report.phases[-1].status 'FAIL'
                    Assert-Equal $context.Report.preparationPassed $null
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($change in @('stale', 'future', 'quiesced', 'unsupported', 'newSession')) {
            Test-Case "prepublication runtime gate refuses $change without writing a command" {
                $context = New-Context; $job = Start-Bridge $context
                Stop-Bridge $job
                $runtime = Read-TestAllJson (Join-Path $context.RuntimeDir 'runtime.json')
                $context.Session = $runtime.startedAt
                switch ($change) {
                    'stale' { $runtime.updatedAt = [datetimeoffset]::UtcNow.AddSeconds(-6).ToString('o') }
                    'future' { $runtime.updatedAt = [datetimeoffset]::UtcNow.AddSeconds(10).ToString('o') }
                    'quiesced' { $runtime.execution.lifecycle = 'Quiesced' }
                    'unsupported' { $runtime.supportedCommands = @('scanSaves') }
                    'newSession' { $runtime.startedAt = [datetimeoffset]::UtcNow.ToString('o') }
                }
                Write-TestAllJson (Join-Path $context.RuntimeDir 'runtime.json') $runtime
                Assert-Throws { Invoke-TestAllCommand $context 'captureLocalSave' } 'heartbeat|Running|advertise|session changed'
                Assert-Equal $context.Report.commands.Count 0
                Assert-Equal @([IO.Directory]::GetFiles((Join-Path $context.RuntimeDir 'commands'), '*.json')).Count 0
            }
        }
        Test-Case 'fixture manifest retains multiple scenarios before read-only inspection' {
            $context = New-Context
            $one = Join-Path $context.ReportDir 'one.txt'; $two = Join-Path $context.ReportDir 'two.txt'
            [IO.File]::WriteAllText($one, 'first'); [IO.File]::WriteAllText($two, 'second')
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'one'; path = 'one.txt'; scenario = 'early'; plan = '24hr'; stage = 0 }, @{ name = 'two'; path = 'two.txt'; scenario = 'Human allocation label'; stage = 2 }) }
            $fixtures = @(Read-TestAllManifest $manifest)
            Assert-Equal $fixtures.Count 2
            Assert-Equal $fixtures[0].scenario 'early'
            Assert-Equal $fixtures[0].stageIndex 0
            Assert-Equal $fixtures[1].stageIndex 2
            Assert-Equal $fixtures[1].scenario 'allocation'
            Assert-Equal $fixtures[1].description 'Human allocation label'
            Assert-Equal $fixtures[1].plan '24hr'
            Assert-Equal $fixtures[0].sha256 (Get-FileHash -LiteralPath $one -Algorithm SHA256).Hash.ToLowerInvariant()
            Assert-Equal @($fixtures | ForEach-Object status) @('CONFIGURED_NOT_EXECUTED', 'CONFIGURED_NOT_EXECUTED')
            Assert-Equal @($fixtures | ForEach-Object decoded) @($false, $false)
            Assert-Equal @($fixtures | ForEach-Object executed) @($false, $false)
        }
        Test-Case 'manifest rejects duplicate names' {
            $context = New-Context; $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'same'; path = 'manifest.json' }, @{ name = 'same'; path = 'manifest.json' }) }
            Assert-Throws { Read-TestAllManifest $manifest } 'unique name'
        }
        Test-Case 'empty manifest does not require or build the inspector' {
            $context = New-Context; $context.Report.fixtures = @()
            Invoke-TestAllFixtureInspection $context (Join-Path $root 'no-project-required')
            Assert-Equal $context.InspectorDll $null
            Assert-Equal $context.Report.phases.Count 0
            Assert-Equal $context.Report.commands.Count 0
        }

        $repo = [IO.Path]::GetDirectoryName($harness)
        function Initialize-CheckTools {
            if ($toolState.initialized) { return }
            # Build only standalone tools, lazily and entirely under this run's report.
            $toolState.context = New-Context
            $toolState.inspectorDll = Initialize-TestAllInspector $toolState.context $repo
            $generatorOutput = Join-Path $toolState.context.ReportDir 'generator'
            $intermediate = (Join-Path $toolState.context.ReportDir 'generator-obj').Replace('\', '/') + '/'
            $toolState.nativeFixtures = Join-Path $toolState.context.ReportDir 'native fixtures'
            Invoke-TestAllNative $toolState.context 'tools.buildGenerator' 'dotnet' @('build', (Join-Path $PSScriptRoot 'Fixtures/FixtureGenerator.csproj'), '--configuration', 'Release', '--nologo',
                "-p:BaseIntermediateOutputPath=$intermediate", "-p:MSBuildProjectExtensionsPath=$intermediate", "-p:IntermediateOutputPath=${intermediate}Release/", '--output', $generatorOutput)
            Invoke-TestAllNative $toolState.context 'tools.generateFixtures' (Join-Path $generatorOutput 'FixtureGenerator.exe') @($toolState.nativeFixtures)
            $toolState.initialized = $true
        }

        function New-LiveContext([string[]]$Scenarios = @('allocation')) {
            Initialize-CheckTools
            $context = New-Context
            $context.InspectorDll = $toolState.inspectorDll; $context.ConfirmLiveSaveLoad = $true
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            $index = 0
            $entries = @($Scenarios | ForEach-Object {
                @{ name = "fixture-$index"; path = (Join-Path $toolState.nativeFixtures 'valid.ok.txt'); scenario = $_; stage = $(if ($_ -eq 'early') { 0 } else { 3 }) }
                $index++
            })
            Write-TestAllJson $manifest @{ version = 1; fixtures = $entries }
            $context.Report.fixtures = @(Read-TestAllManifest $manifest)
            Invoke-TestAllFixtureInspection $context $repo
            return $context
        }
        Test-Case 'live sequential fixtures use restart acknowledgements, real stage guards and backend-only restoration' {
            $context = New-LiveContext @('allocation', 'fallback', 'early'); $job = Start-LiveBridge $context
            try {
                $null = Assert-TestAllLiveCapability $context
                $null = Invoke-TestAllPrepare $context
                Invoke-TestAllLive $context
                Assert-Equal @($context.Report.fixtures | ForEach-Object status) @('PASS', 'PASS', 'INCONCLUSIVE')
                Assert-Equal $context.Report.liveFixturesExecuted 3
                Assert-Equal $context.Report.restoration.restored $true
                Assert-Equal $context.Report.restorationErrors.Count 0
                Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves', 'startSaveTest', 'setSaveTestStage', 'loadSaveTestFixture', 'setSaveTestStage', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')
                Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'loadSaveTestFixture' } | ForEach-Object { $_.request.args.expectedFixtureGeneration }) @('1', '2')
                Assert-Equal $context.Report.commands[2].result.data.pendingRestart $true
                Assert-Equal $context.Report.commands[2].result.data.runtime.status 'starting'
                Assert-Equal $context.Report.fixtures[0].samples.Count 3
                Assert-Equal $context.Report.fixtures[0].native.offlineProgress.totalGainSeconds '10'
                Assert-Equal $context.Report.fixtures[0].reset.verified $true
                Assert-Equal @($context.Report.fixtures[1].assertions | Where-Object id -Like 'fallback.*' | ForEach-Object status) @('PASS', 'PASS')
                Assert-Equal @($context.Report.fixtures[2].assertions | Where-Object id -Like 'fundedNgu.*').Count 0
                Assert-Equal @($context.Report.fixtures[2].assertions | Where-Object id -EQ 'support.nonemptyFeasibleVerified')[0].status 'INCONCLUSIVE'
                Assert-Equal ([IO.File]::ReadAllText((Join-Path $context.RuntimeDir 'native-local.txt'))) 'original native bytes'
                Assert-Equal ([IO.File]::ReadAllText((Join-Path $context.RuntimeDir 'settings.json'))) 'original domain settings'
                Assert-Equal (Get-Requests $context).Count 9
                Assert-Equal $context.Report.restoration.backend.observed.totalPlaytimeSeconds '2020.25'
                Assert-Equal $context.Report.restoreTarget.totalPlaytimeSeconds '2000.25'
            } finally { Stop-Bridge $job }
        }
        Test-Case 'settled assertion failures do not prevent subsequent sequential fixtures or final restoration' {
            $context = New-LiveContext @('allocation', 'allocation'); $job = Start-LiveBridge $context 'ZeroNgu'
            try {
                $null = Invoke-TestAllPrepare $context; Invoke-TestAllLive $context
                Assert-Equal @($context.Report.fixtures | ForEach-Object status) @('FAIL', 'FAIL')
                Assert-Equal @($context.Report.fixtures | ForEach-Object { $_.samples.Count -gt 3 }) @($true, $true)
                Assert-Equal @($context.Report.fixtures | ForEach-Object { $_.samples[-1].timedOut }) @($true, $true)
                Assert-Equal $context.Report.errors.Count 2
                Assert-Equal $context.Report.restorationErrors.Count 0
                Assert-Equal $context.Report.restoration.restored $true
                Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves', 'startSaveTest', 'setSaveTestStage', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')
            } finally { Stop-Bridge $job }
        }
        Test-Case 'settling waits past four seconds of failures for the ten-second automation tick before three progressing passes' {
            $context = New-LiveContext; $scale = 0.15
            $context.SettleMilliseconds = [int](2000 * $scale); $context.SettleTimeoutSeconds = 30 * $scale
            $job = Start-LiveBridge $context 'LateApply' $scale
            try {
                $null = Invoke-TestAllPrepare $context; Invoke-TestAllLive $context
                $fixture = $context.Report.fixtures[0]
                $failed = @($fixture.samples | Where-Object status -EQ 'FAIL')
                $passed = @($fixture.samples | Where-Object status -EQ 'PASS')
                Assert-Equal ($failed.Count -ge 3) $true
                Assert-Equal (($failed[2].elapsedSeconds - $failed[0].elapsedSeconds) / $scale -ge 4) $true
                Assert-Equal @($failed | ForEach-Object { $_.runtime.fakeAutomation.applied } | Sort-Object -Unique) @($false)
                Assert-Equal $fixture.status 'PASS'
                Assert-Equal $passed.Count 3
                Assert-Equal @($passed | ForEach-Object stableSamples) @(1, 2, 3)
                Assert-Equal @($passed | ForEach-Object { $_.runtime.runtimeRevision } | Sort-Object -Unique).Count 3
                Assert-Equal $passed[-1].runtime.fakeAutomation.applied $true
                Assert-Equal ($passed[0].runtime.fakeAutomation.elapsedSeconds -ge 10) $true
                Assert-Equal $passed[-1].timedOut $false
                Assert-Equal $context.Report.restoration.restored $true
                Assert-Equal $context.Report.errors.Count 0
            } finally { Stop-Bridge $job }
        }
        Test-Case 'settling timeout retains the actual stable failed checks and still restores' {
            $context = New-LiveContext; $context.SettleTimeoutSeconds = 0.7; $job = Start-LiveBridge $context 'ZeroNgu'
            try {
                $null = Invoke-TestAllPrepare $context; Invoke-TestAllLive $context
                $fixture = $context.Report.fixtures[0]; $last = $fixture.samples[-1]
                Assert-Equal $fixture.status 'FAIL'
                Assert-Equal $last.timedOut $true
                Assert-Equal ($last.elapsedSeconds -ge $context.SettleTimeoutSeconds) $true
                Assert-Equal ($last.stableSamples -ge 3) $true
                Assert-Equal @($fixture.assertions | Where-Object id -EQ 'fundedNgu.Energy')[0].status 'FAIL'
                Assert-Equal @($context.Report.phases | Where-Object name -EQ 'live.fixture-0.observe')[0].status 'PASS'
                Assert-Equal @($context.Report.errors | ForEach-Object phase) @('live.fixture-0.assertions')
                $saved = Read-TestAllJson (Join-Path $context.ReportDir 'report.json')
                Assert-Equal @($saved.fixtures[0].assertions | Where-Object id -EQ 'fundedNgu.Energy')[0].status 'FAIL'
                Assert-Equal $saved.restoration.restored $true
            } finally { Stop-Bridge $job }
        }
        foreach ($behavior in @('NoStartAck', 'NoLoadAck', 'StageFailure', 'FailedStartup', 'BadGeneration', 'ResetMissing', 'MissingResultSession')) {
            Test-Case "live interruption restores without replay: $behavior" {
                $scenarios = if ($behavior -eq 'NoLoadAck') { @('allocation', 'allocation') } else { @('allocation') }
                $context = New-LiveContext $scenarios
                $context.CommandTimeoutSeconds = 1; $context.TransitionTimeoutSeconds = 0.5
                $job = Start-LiveBridge $context $behavior
                try {
                    $null = Invoke-TestAllPrepare $context
                    Assert-Throws { Invoke-TestAllLive $context } 'UNKNOWN|rejected|recovery required|generation advanced|startup/reset timed out|saveTestSession contract'
                    Assert-Equal $context.Report.restoration.restored $true
                    Assert-Equal $context.Report.restoration.attempted $true
                    Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'startSaveTest' }).Count 1
                    Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'restoreSaveTest' }).Count 1
                    Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'loadSaveTestFixture' }).Count $(if ($behavior -eq 'NoLoadAck') { 1 } else { 0 })
                    if ($behavior -in @('NoStartAck', 'MissingResultSession', 'BadGeneration')) {
                        Assert-Equal $context.Report.commands[2].outcome 'UNKNOWN'
                        Assert-Equal $context.Report.commands[2].request.args.sessionId $context.TestSessionId
                    }
                    Assert-Equal ([IO.File]::ReadAllText((Join-Path $context.RuntimeDir 'native-local.txt'))) 'original native bytes'
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($case in @(
            @{ behavior = 'ZeroNgu'; scenario = 'allocation'; id = 'fundedNgu.Energy'; status = 'FAIL' },
            @{ behavior = 'ExcludedFunded'; scenario = 'allocation'; id = 'diagnostics.ngu.energy.exclusions'; status = 'FAIL' },
            @{ behavior = 'UnverifiedGear'; scenario = 'allocation'; id = 'loadout.nonemptyVerified'; status = 'FAIL' },
            @{ behavior = 'UnverifiedResource'; scenario = 'allocation'; id = 'actualEnergy'; status = 'FAIL' },
            @{ behavior = 'IdleUnexplained'; scenario = 'allocation'; id = 'idle.Energy'; status = 'FAIL' },
            @{ behavior = 'EmptySupport'; scenario = 'allocation'; id = 'support.nonemptyFeasibleVerified'; status = 'FAIL' },
            @{ behavior = 'FallbackNoSpend'; scenario = 'fallback'; id = 'fallback.Energy'; status = 'FAIL' },
            @{ behavior = 'Idle'; scenario = 'allocation'; id = 'idle.Energy'; status = 'INCONCLUSIVE' },
            @{ behavior = 'IdleZero'; scenario = 'early'; id = 'idle.Energy'; status = 'INCONCLUSIVE' }
        )) {
            Test-Case "actual fixture assertion is honest and still restores: $($case.behavior)" {
                $context = New-LiveContext @($case.scenario); $job = Start-LiveBridge $context $case.behavior
                try {
                    $null = Invoke-TestAllPrepare $context
                    Invoke-TestAllLive $context
                    $fixture = $context.Report.fixtures[0]
                    Assert-Equal @($fixture.assertions | Where-Object id -EQ $case.id)[0].status $case.status
                    Assert-Equal ($fixture.samples.Count -gt 3) $true
                    Assert-Equal $fixture.samples[-1].timedOut $true
                    Assert-Equal $context.Report.restoration.restored $true
                    if ($case.status -eq 'FAIL') { Assert-Equal $fixture.status 'FAIL' }
                    Assert-Equal @($fixture.assertions | Where-Object id -EQ 'independentCurrentCaps')[0].required $false
                    Assert-Equal @($fixture.assertions | Where-Object id -EQ 'independentCurrentCaps')[0].status 'INCONCLUSIVE'
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($behavior in @('RestoreFailure', 'RestoreMissingFlag', 'RestoreMissingNative', 'RestoreMissingVerified', 'RestoreClock', 'RestoreRuntimeClock')) {
            Test-Case "restoration never passes contradictory backend evidence: $behavior" {
                $context = New-LiveContext; $job = Start-LiveBridge $context $behavior
                try {
                    $null = Invoke-TestAllPrepare $context
                    Assert-Throws { Invoke-TestAllLive $context } 'restore recovery required|Verified|verified|behind restoreTarget'
                    Assert-Equal $context.Report.fixtures[0].status 'PASS'
                    Assert-Equal $context.Report.restoration.restored $false
                    Assert-Equal $context.Report.restoration.status 'RECOVERY_REQUIRED'
                    Assert-Equal $context.Report.restorationErrors.Count 1
                    Assert-Equal @($context.Report.errors | Where-Object phase -EQ 'live.restore').Count 1
                } finally { Stop-Bridge $job }
            }
        }
        Test-Case 'lost restore acknowledgement can only pass on fresh backend restoration evidence' {
            $context = New-LiveContext; $context.CommandTimeoutSeconds = 1; $job = Start-LiveBridge $context 'NoRestoreAck'
            try {
                $null = Invoke-TestAllPrepare $context
                Invoke-TestAllLive $context
                Assert-Equal $context.Report.restoration.restored $true
                Assert-Equal ($context.Report.restoration.acknowledgementError -match 'UNKNOWN') $true
                Assert-Equal $context.Report.commands[-1].outcome 'UNKNOWN'
                Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'restoreSaveTest' }).Count 1
            } finally { Stop-Bridge $job }
        }
        foreach ($behavior in @('NoProgress', 'PriorityChange')) {
            Test-Case "bounded observations reject broken reconciliation and restore: $behavior" {
                $context = New-LiveContext; $context.SettleTimeoutSeconds = 0.6; $job = Start-LiveBridge $context $behavior
                try {
                    $null = Invoke-TestAllPrepare $context
                    if ($behavior -eq 'NoProgress') {
                        Invoke-TestAllLive $context
                        $fixture = $context.Report.fixtures[0]
                        Assert-Equal $fixture.status 'INCONCLUSIVE'
                        Assert-Equal @($fixture.assertions | Where-Object id -EQ 'settlement.progress')[0].status 'INCONCLUSIVE'
                        Assert-Equal $fixture.samples[-1].timedOut $true
                        Assert-Equal $fixture.samples[-1].stableSamples 0
                    } else {
                        Assert-Throws { Invoke-TestAllLive $context } 'source priority order changed'
                        Assert-Equal $context.Report.fixtures[0].status 'FAIL'
                    }
                    Assert-Equal $context.Report.restoration.restored $true
                    Assert-Equal $context.Report.commands[-1].request.command 'restoreSaveTest'
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($behavior in @('CancelledRejected', 'CancelledQueued')) {
            Test-Case "cancelled start verifies the current epoch without claiming fixture rollback: $behavior" {
                $context = New-LiveContext; $context.CommandTimeoutSeconds = 1; $job = Start-LiveBridge $context $behavior
                try {
                    $null = Invoke-TestAllPrepare $context
                    Assert-Throws { Invoke-TestAllLive $context } 'rejected|UNKNOWN'
                    Assert-Equal $context.Report.originalBaselineAfterRecovery.rebirth '7'
                    Assert-Equal $context.Report.restoration.backend.restoreTarget.rebirth '8'
                    Assert-Equal $context.Report.restoration.restored $true
                    Assert-Equal $context.Report.restoration.cancelledBeforeStart $true
                    Assert-Equal $context.Report.restoration.reason 'start cancelled; no fixture applied. Backend verified current native progression and files; no rollback was performed.'
                    Assert-Equal $context.Report.liveFixturesExecuted 0
                    Assert-Equal $context.Report.fixtures[0].executed $false
                    Assert-Equal $context.Report.commands[2].outcome $(if ($behavior -eq 'CancelledQueued') { 'UNKNOWN' } else { 'FAILED' })
                    Assert-Equal @($context.Report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves', 'startSaveTest', 'restoreSaveTest')
                    Assert-Equal $context.Report.restorationErrors.Count 0
                    Assert-Equal ([IO.File]::ReadAllText((Join-Path $context.RuntimeDir 'native-local.txt'))) 'original native bytes'
                    Assert-Equal ([IO.File]::Exists((Join-Path $context.RuntimeDir 'native.snapshot'))) $false
                } finally { Stop-Bridge $job }
            }
        }
        foreach ($behavior in @('CancelledMissingNative', 'CancelledMissingFiles', 'CancelledMissingVerified', 'CancelledObservedClock', 'CancelledRuntimeClock')) {
            Test-Case "cancelled start still requires native/files verification and actual target clocks: $behavior" {
                $context = New-LiveContext; $job = Start-LiveBridge $context $behavior
                try {
                    $null = Invoke-TestAllPrepare $context
                    Assert-Throws { Invoke-TestAllLive $context } 'Verified|verified|behind restoreTarget'
                    Assert-Equal $context.Report.restoration.restored $false
                    Assert-Equal $context.Report.restoration.status 'RECOVERY_REQUIRED'
                    Assert-Equal $context.Report.restorationErrors.Count 1
                } finally { Stop-Bridge $job }
            }
        }
        Test-Case 'explicit Restore recovers a known failed-startup session without starting a new test' {
            $context = New-LiveContext; $job = Start-LiveBridge $context 'FailedStartup'
            try {
                $null = Invoke-TestAllPrepare $context
                $context.TestSessionId = [guid]::NewGuid().ToString('N'); $context.Report.testSessionId = $context.TestSessionId
                $context.Report.restoration.required = $true
                Save-TestAllReport $context
                $payload = @{}; foreach ($key in @('name', 'path', 'sha256', 'plan', 'stageIndex', 'scenario')) { $payload[$key] = $context.Report.fixtures[0][$key] }
                $null = Invoke-TestAllCommand $context 'startSaveTest' @{ expectedStartedAt = $context.Session; sessionId = $context.TestSessionId; acceptNativeSideEffects = $true; fixture = $payload }
                Start-Sleep -Milliseconds 250
                Assert-Equal (Read-TestAllRuntime $context -AllowTransition).status 'failed'
                $report = Invoke-TestAllHarness -Mode Restore -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir
                Assert-GameReport $report 'Restore'
                Assert-Equal $report.restoration.restored $true
                Assert-Equal $report.testSessionId $context.TestSessionId
                Assert-Equal $report.exitCode 0
                Assert-Equal $report.status 'PASS'
                Assert-Equal @($report.commands | ForEach-Object { $_.request.command }) @('restoreSaveTest')
                Assert-Equal $report.fixtures.Count 0
            } finally { Stop-Bridge $job }
        }
        Test-Case 'natural stage remains -1 in the payload and invokes the guarded backend stage command once' {
            $context = New-LiveContext; $context.Report.fixtures[0].stageIndex = -1; $job = Start-LiveBridge $context
            try {
                $null = Invoke-TestAllPrepare $context; Invoke-TestAllLive $context
                Assert-Equal $context.Report.fixtures[0].status 'PASS'
                Assert-Equal $context.Report.commands[2].request.args.fixture.stageIndex (-1)
                Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'setSaveTestStage' }).Count 1
                Assert-Equal $context.Report.restoration.restored $true
            } finally { Stop-Bridge $job }
        }
        Test-Case 'a locked report cannot prevent a guarded idempotent backend restore' {
            $context = New-LiveContext; $job = Start-LiveBridge $context
            try {
                $null = Invoke-TestAllPrepare $context; Invoke-TestAllLive $context
                $context.Report.restoration.attempted = $false; $context.Report.restoration.restored = $false
                $lock = [IO.File]::Open((Join-Path $context.ReportDir 'report.json'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                try { $null = Invoke-TestAllRestore $context } finally { $lock.Dispose() }
                Save-TestAllReport $context
                Assert-Equal $context.Report.restoration.restored $true
                Assert-Equal ($context.Report.restoration.reportingError -match 'denied|used by another process') $true
                Assert-Equal @($context.Report.commands | Where-Object { $_.request.command -eq 'restoreSaveTest' }).Count 2
                Assert-Equal (Read-TestAllJson (Join-Path $context.ReportDir 'report.json')).restoration.status 'RESTORED'
            } finally { Stop-Bridge $job }
        }
        Test-Case 'fixture generation and hash fence reject stale observations despite unchanged startedAt' {
            $context = New-LiveContext; $job = Start-LiveBridge $context
            try {
                $null = Invoke-TestAllPrepare $context; Invoke-TestAllLive $context
                $runtime = $context.Report.fixtures[0].samples[0].runtime
                $runtime.saveTestSession.fixtureGeneration = '2'
                Assert-Throws { Assert-TestAllFixtureFence $context $runtime $context.ActiveFixture '1' } 'generation/reset fence'
                $runtime.saveTestSession.fixtureGeneration = '1'; $runtime.saveTestSession.fixture.sha256 = '0' * 64
                Assert-Throws { Assert-TestAllFixtureFence $context $runtime $context.ActiveFixture '1' } 'sha256 changed'
                $runtime.saveTestSession.sessionId = 'f' * 32
                Assert-Throws { Assert-TestAllFixtureFence $context $runtime $context.ActiveFixture '1' } 'session changed'
            } finally { Stop-Bridge $job }
        }
        Test-Case 'native Live subprocess passes the selected game mode after verified restoration' {
            $context = New-LiveContext; $job = Start-LiveBridge $context
            try {
                $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Live -RuntimeDir $context.RuntimeDir -FixtureManifestPath (Join-Path $context.ReportDir 'manifest.json') -ReportParent $context.ReportDir -ConfirmLiveSaveLoad)
                Assert-Equal $LASTEXITCODE 0
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories) | Where-Object { $_ -ne (Join-Path $context.ReportDir 'report.json') })[0]
                $report = Read-TestAllJson $path
                Assert-GameReport $report 'Live'
                Assert-Equal $report.success $true
                Assert-Equal $report.fixtures[0].status 'PASS'
                Assert-Equal $report.restoration.restored $true
                Assert-Equal $report.phases[-1].name 'live.restore'
                Assert-Equal ($output -join "`n" -match 'test: PASS') $true
                Assert-Equal ($output -join "`n" -match 'RESTORED') $true
            } finally { Stop-Bridge $job }
        }

        foreach ($invalid in @(@{ stage = 0.5 }, @{ stage = '0' }, @{ stage = -2 }, @{ stage = 1024 }, @{ scenario = 'typo' }, @{ plan = '../unsafe' }, @{ plan = '' })) {
            Test-Case "manifest rejects backend-incompatible metadata $(ConvertTo-Json -Compress $invalid)" -NativeTools {
                $context = New-Context; $manifest = Join-Path $context.ReportDir 'manifest.json'
                $entry = @{ name = 'invalid'; path = (Join-Path $toolState.nativeFixtures 'valid.ok.txt') }
                foreach ($key in $invalid.Keys) { $entry[$key] = $invalid[$key] }
                Write-TestAllJson $manifest @{ version = 1; fixtures = @($entry) }
                Assert-Throws { Read-TestAllManifest $manifest } 'stage must|scenario must|plan must'
                Assert-Equal $context.Report.commands.Count 0
            }
        }

        Test-Case 'successful inspector build is isolated and reused instead of rebuilt' -NativeTools {
            Assert-Equal (Initialize-TestAllInspector $tools $repo) $inspectorDll
            Assert-Equal @($tools.Report.phases | Where-Object name -EQ 'inspector.build').Count 1
            Assert-Equal (Test-Path -LiteralPath $inspectorDll -PathType Leaf) $true
            $build = @($tools.Report.phases | Where-Object name -EQ 'inspector.build')[0].evidence
            $intermediate = (Join-Path $tools.ReportDir 'save-inspector-obj').Replace('\', '/') + '/'
            Assert-Equal $build.arguments @('build', (Join-Path $repo 'checks/SaveSafety/SaveInspector.csproj'), '--configuration', 'Release', '--nologo',
                "-p:BaseIntermediateOutputPath=$intermediate", "-p:MSBuildProjectExtensionsPath=$intermediate", "-p:IntermediateOutputPath=${intermediate}Release/", '--output', (Join-Path $tools.ReportDir 'save-inspector'))
            Assert-Equal ([IO.File]::Exists((Join-Path $intermediate 'project.assets.json'))) $true
            Assert-Equal ([IO.File]::Exists((Join-Path $intermediate 'Release/SaveInspector.dll'))) $true
        }
        Test-Case 'real inspector decodes registered native paths and retains scenario metadata without executing' -NativeTools {
            $context = New-Context; $context.InspectorDll = $inspectorDll
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            $one = Join-Path $nativeFixtures 'valid.ok.txt'; $two = Join-Path $nativeFixtures 'bridge/earlier.ok.txt'
            $hashes = @((Get-FileHash -LiteralPath $one).Hash, (Get-FileHash -LiteralPath $two).Hash)
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'one'; path = $one; scenario = 'allocation'; plan = '24hr'; stage = 0 }, @{ name = 'two'; path = $two; scenario = 'early'; stage = 2 }) }
            $context.Report.fixtures = @(Read-TestAllManifest $manifest)
            Invoke-TestAllFixtureInspection $context $repo
            $fixtures = $context.Report.fixtures
            Assert-Equal @($fixtures | ForEach-Object status) @('INSPECTED_NOT_EXECUTED', 'INSPECTED_NOT_EXECUTED')
            Assert-Equal @($fixtures | ForEach-Object inspected) @($true, $true)
            Assert-Equal @($fixtures | ForEach-Object decoded) @($true, $true)
            Assert-Equal @($fixtures | ForEach-Object executed) @($false, $false)
            Assert-Equal @($fixtures | ForEach-Object { $_.metadata.totalPlaytimeSeconds }) @('1000.25', '100')
            Assert-Equal @($fixtures | ForEach-Object { $_.metadata.rebirth }) @('7', '6')
            Assert-Equal $fixtures[0].metadata.rebirthSeconds '60.5'
            Assert-Equal $fixtures[0].metadata.playerName 'Synthetic Player'
            Assert-Equal $fixtures[0].metadata.version 1260
            Assert-Equal $fixtures[0].metadata.lastTime 1720000000
            Assert-Equal $fixtures[0].metadata.sha256 $fixtures[0].sha256
            Assert-Equal $fixtures[1].metadata.sha256 $fixtures[1].sha256
            Assert-Equal $fixtures[0].scenario 'allocation'
            Assert-Equal $fixtures[0].plan '24hr'
            Assert-Equal $fixtures[1].stageIndex 2
            Assert-Equal $context.Report.commands.Count 0
            $phase = @($context.Report.phases | Where-Object name -EQ 'fixtures.inspector.1')[0]
            Assert-Equal $phase.evidence.executable 'dotnet'
            Assert-Equal $phase.evidence.arguments @($inspectorDll, $one)
            Assert-Equal $phase.evidence.output.success $true
            Assert-Equal (Read-TestAllJson $phase.evidence.log).files[0].sha256 $fixtures[0].sha256
            Assert-Equal ([IO.File]::ReadAllText($phase.evidence.stderrLog)) ''
            $saved = Read-TestAllJson (Join-Path $context.ReportDir 'report.json')
            Assert-Equal $saved.fixtures[0].metadata.totalPlaytimeSeconds '1000.25'
            Assert-Equal $saved.fixtures[0].status 'INSPECTED_NOT_EXECUTED'
            Assert-Equal @((Get-FileHash -LiteralPath $one).Hash, (Get-FileHash -LiteralPath $two).Hash) $hashes
        }
        Test-Case 'real inspector rejects a corrupt native fixture and preserves its JSON errors' -NativeTools {
            $context = New-Context; $context.InspectorDll = $inspectorDll
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'broken'; path = (Join-Path $nativeFixtures 'wrong-checksum.bad.txt') }) }
            $context.Report.fixtures = @(Read-TestAllManifest $manifest)
            Assert-Throws { Invoke-TestAllFixtureInspection $context $repo } 'fixtures.inspector.1 exited 1'
            Assert-Equal $context.Report.fixtures[0].decoded $false
            Assert-Equal $context.Report.fixtures[0].metadata $null
            Assert-Equal $context.Report.fixtures[0].executed $false
            $phase = @($context.Report.phases | Where-Object name -EQ 'fixtures.inspector.1')[0]
            Assert-Equal $phase.status 'FAIL'
            Assert-Equal $phase.evidence.exitCode 1
            Assert-Equal $phase.evidence.output.success $false
            Assert-Equal $phase.evidence.output.errors.Count 1
            Assert-Equal $context.Report.commands.Count 0
        }
        Test-Case 'changed registered bytes fail the hash cross-check even when the real inspector decodes them' -NativeTools {
            $context = New-Context; $context.InspectorDll = $inspectorDll
            $registered = Join-Path $context.ReportDir 'registered.txt'
            [IO.File]::Copy((Join-Path $nativeFixtures 'valid.ok.txt'), $registered)
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'changed'; path = 'registered.txt' }) }
            $context.Report.fixtures = @(Read-TestAllManifest $manifest)
            [IO.File]::Copy((Join-Path $nativeFixtures 'bridge/earlier.ok.txt'), $registered, $true)
            Assert-Throws { Invoke-TestAllFixtureInspection $context $repo } 'SHA-256 changed since registration'
            Assert-Equal $context.Report.fixtures[0].status 'CONFIGURED_NOT_EXECUTED'
            Assert-Equal $context.Report.fixtures[0].decoded $false
            Assert-Equal $context.Report.fixtures[0].metadata $null
            $phase = @($context.Report.phases | Where-Object name -EQ 'fixtures.inspector.1')[0]
            Assert-Equal $phase.evidence.output.success $true
            Assert-Equal $phase.evidence.output.files[0].totalPlaytimeSeconds '100'
            Assert-Equal ($phase.evidence.output.files[0].sha256 -cne $context.Report.fixtures[0].sha256) $true
            Assert-Equal $context.Report.commands.Count 0
        }
        Test-Case 'tied native fixture branches are each inspected without choosing or loading a branch' -NativeTools {
            $context = New-Context; $context.InspectorDll = $inspectorDll
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'branch-a'; path = (Join-Path $nativeFixtures 'valid.ok.txt') }, @{ name = 'branch-b'; path = (Join-Path $nativeFixtures 'bridge/tied-branch.ok.txt') }) }
            $context.Report.fixtures = @(Read-TestAllManifest $manifest)
            Invoke-TestAllFixtureInspection $context $repo
            Assert-Equal @($context.Report.fixtures | ForEach-Object { $_.metadata.totalPlaytimeSeconds }) @('1000.25', '1000.25')
            Assert-Equal @($context.Report.fixtures | ForEach-Object { $_.metadata.rebirth }) @('7', '8')
            Assert-Equal ($context.Report.fixtures[0].sha256 -cne $context.Report.fixtures[1].sha256) $true
            Assert-Equal @($context.Report.fixtures | ForEach-Object status) @('INSPECTED_NOT_EXECUTED', 'INSPECTED_NOT_EXECUTED')
            Assert-Equal $context.Report.commands.Count 0
        }
        Test-Case 'Inspect decodes native files without requiring or creating a runtime' -NativeTools {
            $context = New-Context
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'inspect-only'; path = (Join-Path $nativeFixtures 'valid.ok.txt') }) }
            $runtime = Join-Path $context.ReportDir 'no-runtime'
            $report = Invoke-TestAllHarness -Mode Inspect -RuntimeDir $runtime -FixtureManifestPath $manifest -ReportParent $context.ReportDir
            Assert-GameReport $report 'Inspect'
            Assert-Equal $report.exitCode 0
            Assert-Equal $report.status 'PASS'
            Assert-Equal $report.fixtures[0].metadata.totalPlaytimeSeconds '1000.25'
            Assert-Equal $report.fixtures[0].status 'INSPECTED_NOT_EXECUTED'
            Assert-Equal $report.fixtures[0].executed $false
            Assert-Equal $report.commands.Count 0
            Assert-Equal $report.originalRuntime $null
            Assert-Equal $report.preparationPassed $false
            Assert-Equal @($report.phases | Where-Object name -Like 'prepare.*').Count 0
            Assert-Equal (Test-Path -LiteralPath $runtime) $false
        }
        Test-Case 'confirmed recovery uses the runtime catalog rather than inspected fixture playtime or hash' -NativeTools {
            $context = New-Context; $context.InspectorDll = $inspectorDll; $context.ConfirmLiveSaveLoad = $true
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'not-a-recovery-source'; path = (Join-Path $nativeFixtures 'large-finite-clocks.ok.txt') }) }
            $context.Report.fixtures = @(Read-TestAllManifest $manifest)
            Invoke-TestAllFixtureInspection $context $repo
            $job = Start-Bridge $context 'Behind'
            try {
                $prepared = Invoke-TestAllPrepare $context
                $load = $context.Report.commands[2].request
                Assert-Equal $load.command 'loadLongestSave'
                Assert-Equal $load.args.sha256 $prepared.saveSafety.longest.sha256
                Assert-Equal ($load.args.sha256 -cne $context.Report.fixtures[0].sha256) $true
                Assert-Equal $prepared.saveSafety.current.totalPlaytimeSeconds '2000.25'
                Assert-Equal $context.Report.fixtures[0].executed $false
            } finally { Stop-Bridge $job }
        }
        foreach ($mode in @('Inspect', 'Prepare', 'Live')) {
            Test-Case "corrupt manifest fixture fails native $mode before any game command or recovery" -NativeTools {
                $context = New-Context; $job = Start-Bridge $context 'Behind'
                try {
                    $manifest = Join-Path $context.ReportDir 'manifest.json'
                    Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'broken'; path = (Join-Path $nativeFixtures 'wrong-checksum.bad.txt') }) }
                    $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode $mode -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir -FixtureManifestPath $manifest -ConfirmLiveSaveLoad
                    Assert-Equal $LASTEXITCODE 1
                    $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                    $report = Read-TestAllJson $path
                    Assert-GameReport $report $mode
                    Assert-Equal $report.status 'FAIL'
                    Assert-Equal $report.commands.Count 0
                    Assert-Equal $report.baseline $null
                    Assert-Equal $report.fixtures[0].decoded $false
                    Assert-Equal $report.fixtures[0].executed $false
                    Assert-Equal (Get-Requests $context).Count 0
                } finally { Stop-Bridge $job }
            }
        }
        Test-Case 'unsupported backend blocks Live after inspecting every fixture and before preparation or mutation' -NativeTools {
            $context = New-Context; $job = Start-Bridge $context
            try {
                $manifest = Join-Path $context.ReportDir 'manifest.json'
                Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'huge-playtime'; path = (Join-Path $nativeFixtures 'large-finite-clocks.ok.txt'); scenario = 'fixture only' },
                    @{ name = 'second'; path = (Join-Path $nativeFixtures 'valid.ok.txt') }) }
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Live -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir -FixtureManifestPath $manifest -ConfirmLiveSaveLoad
                Assert-Equal $LASTEXITCODE 2
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                $report = Read-TestAllJson $path
                Assert-GameReport $report 'Live'
                Assert-Equal $report.status 'BLOCKED'
                Assert-Equal ($report.liveFixtureBlocker -match 'capability unsupported.*runtime.saveTestSession') $true
                Assert-Equal $report.fixtures[0].status 'INSPECTED_NOT_EXECUTED'
                Assert-Equal $report.fixtures[0].metadata.rebirth '9223372036854775807'
                Assert-Equal $report.preparedRuntime $null
                Assert-Equal $report.fixtures[0].executed $false
                Assert-Equal $report.liveFixturesExecuted 0
                Assert-Equal $report.commands.Count 0
                Assert-Equal @($report.phases | Where-Object name -EQ 'inspector.build').Count 1
                Assert-Equal @($report.fixtures | ForEach-Object decoded) @($true, $true)
                $inspectionIndex = @($report.phases.name).IndexOf('fixtures.inspector.2')
                $preflightIndex = @($report.phases.name).IndexOf('live.capability')
                Assert-Equal ($inspectionIndex -ge 0 -and $inspectionIndex -lt $preflightIndex) $true
                Assert-Equal $report.restoration.restored $false
            } finally { Stop-Bridge $job }
        }
        Test-Case 'command lifetime cannot exceed the backend 120-second bound' {
            $context = New-Context; $context.CommandTimeoutSeconds = 121
            Assert-Throws { Invoke-TestAllCommand $context 'captureLocalSave' } 'between 1 and 120'
            Assert-Equal $context.Report.commands.Count 0
        }
        Test-Case 'report parent cannot redirect harness writes into the runtime' {
            $context = New-Context
            Assert-Throws { Invoke-TestAllHarness -Mode Diagnostics -RuntimeDir $context.RuntimeDir -ReportParent $context.RuntimeDir } 'outside RuntimeDir'
            Assert-Throws { Invoke-TestAllHarness -Mode Inspect -RuntimeDir ($context.RuntimeDir + '\') -ReportParent $context.RuntimeDir } 'outside RuntimeDir'
            Assert-Equal @([IO.Directory]::GetFiles($context.RuntimeDir, '*', [IO.SearchOption]::AllDirectories)).Count 0
        }
        Test-Case 'native child failure retains its exit code and fails the phase' {
            $context = New-Context
            Assert-Throws { Invoke-TestAllNative $context 'child.failure' (Join-Path $PSHOME 'pwsh.exe') @('-NoProfile', '-NonInteractive', '-Command', '"synthetic child failure"; exit 7') } 'exited 7'
            Assert-Equal $context.Report.phases[0].status 'FAIL'
            Assert-Equal $context.Report.phases[0].evidence.exitCode 7
            Assert-Equal ([IO.File]::ReadAllText($context.Report.phases[0].evidence.log).Trim()) 'synthetic child failure'
        }
        Test-Case 'native child success retains its log without output contaminating report evidence' {
            $context = New-Context
            Invoke-TestAllNative $context 'child.success' (Join-Path $PSHOME 'pwsh.exe') @('-NoProfile', '-NonInteractive', '-Command', '"synthetic child success"; exit 0')
            Assert-Equal $context.Report.phases[0].status 'PASS'
            Assert-Equal $context.Report.phases[0].evidence.exitCode 0
            Assert-Equal ([IO.File]::ReadAllText($context.Report.phases[0].evidence.log).Trim()) 'synthetic child success'
        }
        Test-Case 'NativeProcessBoundary retains real subprocess arguments JSON logs and exit failures' {
            $context = New-Context
            $context.NativeProcessBoundary = Join-Path $context.ReportDir 'native-process-boundary.ps1'
            [IO.File]::WriteAllText($context.NativeProcessBoundary, @'
param([string]$Executable, [string]$ArgumentsBase64)
$arguments = @(ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ArgumentsBase64))))
ConvertTo-Json -InputObject @{ executable = $Executable; arguments = @($arguments); pid = $PID } -Compress
exit ([int]$arguments[-1])
'@)
            $arguments = @('path with spaces', '--flag', '0')
            $output = Invoke-TestAllNative $context 'boundary.success' 'synthetic-tool' $arguments -JsonOutput
            Assert-Equal $output.executable 'synthetic-tool'
            Assert-Equal $output.arguments $arguments
            Assert-Equal ($output.pid -ne $PID) $true
            Assert-Equal $context.Report.phases[0].evidence.processBoundary $context.NativeProcessBoundary
            Assert-Throws { Invoke-TestAllNative $context 'boundary.failure' 'synthetic-tool' @('7') -JsonOutput } 'exited 7'
            Assert-Equal $context.Report.phases[1].status 'FAIL'
            Assert-Equal $context.Report.phases[1].evidence.exitCode 7
            Assert-Equal $context.Report.phases[1].evidence.output.arguments @('7')
        }
        Test-Case 'capture metadata tolerance is based on elapsed seconds, not a seven-day session' {
            $context = New-Context; $job = Start-Bridge $context
            try {
                $runtime = Read-TestAllRuntime $context
                $result = Invoke-TestAllCommand $context 'captureLocalSave'
                $result.data.save.totalPlaytimeSeconds = '11000.25'
                Assert-Throws { Assert-TestAllCapture $result.data.save $runtime.saveSafety.current $runtime.saveSafety.current 1 } 'tolerance'
            } finally { Stop-Bridge $job }
        }
        Test-Case 'runtime file snapshots ignore only heartbeat publication temporaries' {
            $context = New-Context
            $originals = @('native-local.txt', 'settings.json', 'unrelated.tmp', 'commands/request.tmp', 'commands/runtime.json.tmp')
            foreach ($name in @('runtime.json') + $originals) { [IO.File]::WriteAllText((Join-Path $context.RuntimeDir $name), 'protected or unrelated bytes') }
            $before = Get-StableRuntimeFiles $context
            foreach ($name in @('runtime.json.tmp', ('runtime.json.' + [guid]::NewGuid().ToString('N') + '.tmp'))) {
                $temporary = Join-Path $context.RuntimeDir $name
                [IO.File]::WriteAllText($temporary, 'heartbeat publication')
                Assert-Equal (Get-StableRuntimeFiles $context) $before
                [IO.File]::Delete($temporary)
                Assert-Equal (Get-StableRuntimeFiles $context) $before
            }
            foreach ($name in $originals) {
                $path = Join-Path $context.RuntimeDir $name
                Assert-Equal ($before -contains $path) $true
                [IO.File]::Delete($path)
                Assert-Equal ((Get-StableRuntimeFiles $context) -contains $path) $false
                [IO.File]::WriteAllText($path, 'protected or unrelated bytes')
                Assert-Equal (Get-StableRuntimeFiles $context) $before
            }
        }
        foreach ($mode in @('Diagnostics', 'Inspect', 'Prepare', 'Live', 'Restore')) {
            Test-Case "native script exit/report is honest for selected game mode $mode" {
                $context = New-Context; $job = $null
                $arguments = @('-NoProfile', '-NonInteractive', '-File', $harness, '-RuntimeDir', $context.RuntimeDir, '-ReportParent', $context.ReportDir)
                # Omit Mode entirely to prove the safe public default.
                if ($mode -ne 'Diagnostics') { $arguments += @('-Mode', $mode) }
                if ($mode -eq 'Inspect') {
                    $manifest = Join-Path $context.ReportDir 'manifest.json'
                    Write-TestAllJson $manifest @{ version = 1; fixtures = @() }
                    $arguments += @('-FixtureManifestPath', $manifest)
                } elseif ($mode -eq 'Restore') { $job = Start-LiveBridge $context }
                else { $job = Start-Bridge $context }
                try {
                    $before = Get-StableRuntimeFiles $context
                    $output = @(& (Join-Path $PSHOME 'pwsh.exe') @arguments 2>&1)
                    $childExitCode = $LASTEXITCODE
                    [IO.File]::WriteAllText((Join-Path $context.ReportDir 'child.output.log'), ($output -join [Environment]::NewLine))
                    [IO.File]::WriteAllText((Join-Path $context.ReportDir 'child.exit-code.txt'), [string]$childExitCode)
                    $blocked = $mode -in @('Live', 'Restore')
                    Assert-Equal $childExitCode $(if ($blocked) { 2 } else { 0 }) 'native exit code'
                    $reports = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))
                    Assert-Equal $reports.Count 1
                    $report = Read-TestAllJson $reports[0]
                    Assert-GameReport $report $mode
                    Assert-Equal $report.status $(if ($blocked) { 'BLOCKED' } else { 'PASS' })
                    Assert-Equal $report.success (-not $blocked)
                    Assert-Equal ($output -join "`n" -match ('test: ' + $report.status)) $true
                    Assert-Equal $report.liveFixturesExecuted 0
                    Assert-Equal $report.restoration.restored $false
                    Assert-Equal $report.restoration.attempted $false
                    Assert-Equal $report.fixtures.Count 0
                    Assert-Equal @($report.phases | Where-Object name -EQ 'inspector.build').Count 0
                    if ($mode -ne 'Prepare') {
                        Assert-Equal $report.commands.Count 0
                        Assert-Equal (Get-Requests $context).Count 0
                        Assert-Equal (Get-StableRuntimeFiles $context) $before
                        Assert-Equal $report.preparationPassed $false
                        Assert-Equal @($report.phases | Where-Object name -Like 'prepare.*').Count 0
                    } elseif ($mode -eq 'Prepare') {
                        Assert-Equal $report.preparationPassed $true
                        Assert-Equal @($report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves')
                    }
                    if ($mode -in @('Diagnostics', 'Prepare')) { Assert-Equal $report.runtimeDiagnostics.success $true }
                    if ($mode -eq 'Live') {
                        Assert-Equal $report.preparationPassed $false
                        Assert-Equal $report.commands.Count 0
                        Assert-Equal ($output -join "`n" -match 'ConfirmLiveSaveLoad') $true
                    }
                } catch {
                    [Console]::Error.WriteLine("Selected $mode child output:`n$($output -join [Environment]::NewLine)`nChild reports: $($context.ReportDir)")
                    throw
                } finally { if ($job) { Stop-Bridge $job } }
            }
        }
        Test-Case 'default Diagnostics failure is read-only and never falls through to preparation or verification' {
            $context = New-Context; $job = Start-Bridge $context 'Behind'
            try {
                $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir)
                Assert-Equal $LASTEXITCODE 1
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                $report = Read-TestAllJson $path
                Assert-GameReport $report 'Diagnostics'
                Assert-Equal $report.status 'FAIL'
                Assert-Equal $report.runtimeDiagnostics.success $false
                Assert-Equal $report.commands.Count 0
                Assert-Equal (Get-Requests $context).Count 0
                Assert-Equal @($report.phases | ForEach-Object name) @('diagnostics.runtimeDiagnostics')
                Assert-Equal $report.restoration.attempted $false
            } finally { Stop-Bridge $job }
        }
        Test-Case 'native Live recovery failure stays failed without invoking offline verification' {
            $context = New-LiveContext; $job = Start-LiveBridge $context 'RestoreMissingFlag'
            try {
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Live -RuntimeDir $context.RuntimeDir -FixtureManifestPath (Join-Path $context.ReportDir 'manifest.json') -ReportParent $context.ReportDir -ConfirmLiveSaveLoad
                Assert-Equal $LASTEXITCODE 1
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories) | Where-Object { $_ -ne (Join-Path $context.ReportDir 'report.json') })[0]
                $report = Read-TestAllJson $path
                Assert-GameReport $report 'Live'
                Assert-Equal $report.fixtures[0].status 'PASS'
                Assert-Equal $report.restoration.status 'RECOVERY_REQUIRED'
                Assert-Equal @($report.commands | Where-Object { $_.request.command -eq 'restoreSaveTest' }).Count 1
                Assert-Equal $report.restoration.restored $false
            } finally { Stop-Bridge $job }
        }
        Test-Case 'Restore failure reports unknown session without publishing commands or preparing' {
            $context = New-Context; $job = Start-LiveBridge $context
            try {
                $report = Invoke-TestAllHarness -Mode Restore -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir
                Assert-GameReport $report 'Restore'
                Assert-Equal $report.exitCode 2
                Assert-Equal $report.status 'BLOCKED'
                Assert-Equal $report.commands.Count 0
                Assert-Equal (Get-Requests $context).Count 0
                Assert-Equal @($report.phases | ForEach-Object name) @('restore.existingSession')
            } finally { Stop-Bridge $job }
        }
        Test-Case 'native Restore fails contradictory recovery evidence without preparing or running verification' {
            $context = New-LiveContext; $job = Start-LiveBridge $context 'RestoreMissingFlag'
            try {
                $null = Invoke-TestAllPrepare $context
                Assert-Throws { Invoke-TestAllLive $context } 'filesVerified=true'
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Restore -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir
                Assert-Equal $LASTEXITCODE 1
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories) | Where-Object { $_ -ne (Join-Path $context.ReportDir 'report.json') })[0]
                $report = Read-TestAllJson $path
                Assert-GameReport $report 'Restore'
                Assert-Equal $report.status 'FAIL'
                Assert-Equal $report.restoration.status 'RECOVERY_REQUIRED'
                Assert-Equal $report.restoration.restored $false
                Assert-Equal @($report.commands | ForEach-Object { $_.request.command }) @('restoreSaveTest')
                Assert-Equal @($report.phases | ForEach-Object name) @('restore.existingSession', 'finally.restore')
                Assert-Equal $report.preparationPassed $false
                Assert-Equal $report.testSessionId $context.TestSessionId
            } finally { Stop-Bridge $job }
        }
        Test-Case 'CaseFilter matching zero cases fails instead of reporting green or building tools' {
            $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $PSCommandPath -CaseFilter ('no-such-case-' + [guid]::NewGuid().ToString('N')) 2>&1)
            Assert-Equal $LASTEXITCODE 1
            Assert-Equal ($output -join "`n" -match 'CaseFilter matched zero cases') $true
            Assert-Equal ($output -join "`n" -match 'Determining projects to restore|Build succeeded') $false
        }
        Test-Case 'finally writes a failure report when native capture evidence is invalid' {
            $context = New-Context; $job = Start-Bridge $context 'CaptureHash'
            try {
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Prepare -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir
                Assert-Equal $LASTEXITCODE 1
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                $report = Read-TestAllJson $path
                Assert-GameReport $report 'Prepare'
                Assert-Equal $report.status 'FAIL'
                Assert-Equal $report.commands.Count 1
                Assert-Equal $report.restoration.originalBackupVerified $false
                Assert-Equal $report.phases[2].status 'FAIL'
                Assert-Equal ($null -ne $report.completedAt) $true
            } finally { Stop-Bridge $job }
        }
        if ($results.Count -eq 0) { throw "CaseFilter matched zero cases: $CaseFilter" }
        $failures = @($results | Where-Object success -EQ $false)
        if ($failures.Count) {
            foreach ($failure in $failures) { [Console]::Error.WriteLine($failure.name + ': ' + $failure.error) }
            throw "$($failures.Count)/$($results.Count) harness checks failed."
        }
        $completedSuccessfully = $true
        [pscustomobject]@{ success = $true; passed = $results.Count; failed = 0; liveActions = 0; cases = $results.ToArray() }
    } finally {
        foreach ($job in $jobs.ToArray()) { Stop-Job -Job $job -ErrorAction SilentlyContinue; Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
        if ($completedSuccessfully) { [IO.Directory]::Delete($root, $true) }
        else {
            Write-TestAllJson (Join-Path $root 'checks-failure.json') @{ caseFilter = $CaseFilter; cases = $results.ToArray(); success = $false }
            [Console]::Error.WriteLine("Harness failure evidence retained: $root")
        }
    }
}
