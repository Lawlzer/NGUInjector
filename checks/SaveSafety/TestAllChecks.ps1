#requires -Version 7.0
<#
Isolated harness checks. Run with pwsh -NoProfile -File checks/SaveSafety/TestAllChecks.ps1.
Uses real temporary runtime/queue/result files and a separate PowerShell bridge process.
Only the external backend is faked: harness functions, hashing, validation, timeouts,
atomic publication and reporting run unchanged. Inspector integration uses the actual
compiled CLI and generated native fixtures. Only standalone check tools are built;
no game, deployment or production injector build is involved. Offline orchestration
checks substitute only native child processes, not the gate/phase implementation.
#>
[CmdletBinding()]
param()

& {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off
    $harness = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../test-all.ps1'))
    . $harness -FunctionsOnly
    $parent = Join-Path $env:LOCALAPPDATA 'Temp/opencode'
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'Approved temporary parent does not exist.' }
    $root = Join-Path $parent ('ngu-test-all-checks-' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($root)
    $results = [Collections.Generic.List[object]]::new()
    $jobs = [Collections.Generic.List[object]]::new()

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
            errors = [Collections.Generic.List[object]]::new(); restoration = @{ originalBackupVerified = $false }; baseline = $null; fullOfflineGatePassed = $false }
        return @{ RuntimeDir = (Join-Path $directory 'runtime'); ReportDir = (Join-Path $directory 'reports'); Report = $report
            Session = $null; ConfirmLiveSaveLoad = $false; CommandTimeoutSeconds = 2; ScanTimeoutSeconds = 3; PollMilliseconds = 30 }
    }
    function Get-Requests($Context) {
        return ,@([IO.Directory]::GetFiles((Join-Path $Context.RuntimeDir 'commands/claimed'), '*.claimed') | Sort-Object | ForEach-Object { Read-TestAllJson $_ })
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
    function Test-Case([string]$Name, [scriptblock]$Action) {
        try {
            $null = & $Action
            $results.Add(@{ name = $Name; success = $true; error = $null })
        } catch {
            $results.Add(@{ name = $Name; success = $false; error = $_.ToString() + ' at ' + $_.ScriptStackTrace })
        }
    }
    try {
        Test-Case 'FunctionsOnly imports without invoking checks or live commands' {
            Assert-Equal (Get-Command Invoke-TestAllPrepare).CommandType ([Management.Automation.CommandTypes]::Function)
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
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'one'; path = 'one.txt'; scenario = 'early'; plan = 'Plan A'; stage = 0 }, @{ name = 'two'; path = 'two.txt'; scenario = 'late'; stage = @{ index = 2 } }) }
            $fixtures = @(Read-TestAllManifest $manifest)
            Assert-Equal $fixtures.Count 2
            Assert-Equal $fixtures[0].scenario 'early'
            Assert-Equal $fixtures[0].stage 0
            Assert-Equal $fixtures[1].stage.index 2
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

        # Build only the real read-only CLI and trusted offline fixture generator.
        $repo = [IO.Path]::GetDirectoryName($harness)
        $tools = New-Context
        $inspectorDll = Initialize-TestAllInspector $tools $repo
        $generatorOutput = Join-Path $tools.ReportDir 'generator'
        $nativeFixtures = Join-Path $tools.ReportDir 'native fixtures'
        Invoke-TestAllNative $tools 'tools.buildGenerator' 'dotnet' @('build', (Join-Path $PSScriptRoot 'Fixtures/FixtureGenerator.csproj'), '--configuration', 'Release', '--nologo', '--output', $generatorOutput)
        Invoke-TestAllNative $tools 'tools.generateFixtures' (Join-Path $generatorOutput 'FixtureGenerator.exe') @($nativeFixtures)

        foreach ($outcomes in @(
            @{ label = 'both gates pass'; ui = 0; native = 0; codec = 0 },
            @{ label = 'UI fails but native still runs'; ui = 1; native = 0; codec = 0 },
            @{ label = 'native fails independently'; ui = 0; native = 7; codec = 0 },
            @{ label = 'both independent failures are retained'; ui = 1; native = 7; codec = 0 },
            @{ label = 'other checks still gate success'; ui = 0; native = 0; codec = 9 }
        )) {
            Test-Case "offline command subprocess boundary: $($outcomes.label)" {
                $context = New-Context; $context.InspectorDll = $inspectorDll
                $uiScript = Join-Path $repo 'verify-ui.ps1'
                $nativeScript = Join-Path $repo 'checks/VerifyExecutorOffline.ps1'
                $codecProject = Join-Path $repo 'checks/SaveSafety/SaveSafetyChecks.csproj'
                Write-TestAllJson (Join-Path $context.ReportDir 'boundary-config.json') @{
                    uiScript = $uiScript; nativeScript = $nativeScript; codecProject = $codecProject; outcomes = $outcomes
                }
                $context.NativeProcessBoundary = Join-Path $context.ReportDir 'native-process-boundary.ps1'
                [IO.File]::WriteAllText($context.NativeProcessBoundary, @'
param([string]$Executable, [string]$ArgumentsBase64)
$ErrorActionPreference = 'Stop'
$config = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'boundary-config.json'))) -AsHashtable
$arguments = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ArgumentsBase64)))
$request = ConvertTo-Json -InputObject @{ executable = $Executable; arguments = @($arguments) } -Compress
[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'invocations.jsonl'), $request + [Environment]::NewLine)
$code = if ($arguments -contains $config.uiScript) { $config.outcomes.ui }
    elseif ($arguments -contains $config.nativeScript) { $config.outcomes.native }
    elseif ($arguments -contains $config.codecProject) { $config.outcomes.codec }
    else { 0 }
"Synthetic subprocess boundary exit=$code"
exit $code
'@)
                $game = Join-Path $context.ReportDir 'game folder with spaces'
                $success = $outcomes.ui -eq 0 -and $outcomes.native -eq 0 -and $outcomes.codec -eq 0
                if ($success) { Invoke-TestAllOffline $context $repo $game }
                else { Assert-Throws { Invoke-TestAllOffline $context $repo $game } 'Offline gate failed' }
                $requests = @([IO.File]::ReadAllLines((Join-Path $context.ReportDir 'invocations.jsonl')) | ForEach-Object { ConvertFrom-Json -InputObject $_ -AsHashtable })
                $ui = @($requests | Where-Object { $_.arguments -contains $uiScript })
                $native = @($requests | Where-Object { $_.arguments -contains $nativeScript })
                Assert-Equal $ui.Count 1 'UI invocation count'
                Assert-Equal $native.Count 1 'native invocation count'
                Assert-Equal $ui[0].executable (Join-Path $PSHOME 'pwsh.exe')
                Assert-Equal $ui[0].arguments @('-NoProfile', '-NonInteractive', '-File', $uiScript, '-BrowserTests', '-GameFolder', $game)
                Assert-Equal $native[0].arguments @('-NoProfile', '-NonInteractive', '-File', $nativeScript, '-GameFolder', $game)
                Assert-Equal ($ui[0].arguments -contains '-IncludeInjector') $false
                Assert-Equal $requests[0].arguments $ui[0].arguments
                Assert-Equal $requests[1].arguments $native[0].arguments
                Assert-Equal @($context.Report.phases | Where-Object name -EQ 'offline.verifyUi').Count 1
                Assert-Equal @($context.Report.phases | Where-Object name -EQ 'offline.nativeExecutor').Count 1
                $uiPhase = @($context.Report.phases | Where-Object name -EQ 'offline.verifyUi')[0]
                $nativePhase = @($context.Report.phases | Where-Object name -EQ 'offline.nativeExecutor')[0]
                Assert-Equal $uiPhase.evidence.exitCode $outcomes.ui
                Assert-Equal $nativePhase.evidence.exitCode $outcomes.native
                Assert-Equal $uiPhase.status $(if ($outcomes.ui) { 'FAIL' } else { 'PASS' })
                Assert-Equal $nativePhase.status $(if ($outcomes.native) { 'FAIL' } else { 'PASS' })
                Assert-Equal $context.Report.fullOfflineGatePassed $success
                $failedPhases = @(
                    if ($outcomes.ui) { 'offline.verifyUi' }
                    if ($outcomes.native) { 'offline.nativeExecutor' }
                    if ($outcomes.codec) { 'offline.SaveSafetyChecks' }
                )
                Assert-Equal @($context.Report.errors | ForEach-Object phase) $failedPhases
                Assert-Equal $context.Report.commands.Count 0
            }
        }

        Test-Case 'successful inspector build is reused instead of rebuilt' {
            Assert-Equal (Initialize-TestAllInspector $tools $repo) $inspectorDll
            Assert-Equal @($tools.Report.phases | Where-Object name -EQ 'inspector.build').Count 1
            Assert-Equal (Test-Path -LiteralPath $inspectorDll -PathType Leaf) $true
        }
        Test-Case 'real inspector decodes registered native paths and retains scenario metadata without executing' {
            $context = New-Context; $context.InspectorDll = $inspectorDll
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            $one = Join-Path $nativeFixtures 'valid.ok.txt'; $two = Join-Path $nativeFixtures 'bridge/earlier.ok.txt'
            $hashes = @((Get-FileHash -LiteralPath $one).Hash, (Get-FileHash -LiteralPath $two).Hash)
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'one'; path = $one; scenario = 'late'; plan = 'Plan A'; stage = 0 }, @{ name = 'two'; path = $two; scenario = 'early'; stage = @{ index = 2 } }) }
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
            Assert-Equal $fixtures[0].scenario 'late'
            Assert-Equal $fixtures[0].plan 'Plan A'
            Assert-Equal $fixtures[1].stage.index 2
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
        Test-Case 'real inspector rejects a corrupt native fixture and preserves its JSON errors' {
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
        Test-Case 'changed registered bytes fail the hash cross-check even when the real inspector decodes them' {
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
        Test-Case 'tied native fixture branches are each inspected without choosing or loading a branch' {
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
        Test-Case 'offline manifest preparation decodes native files without requiring a runtime' {
            $context = New-Context
            $manifest = Join-Path $context.ReportDir 'manifest.json'
            Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'offline'; path = (Join-Path $nativeFixtures 'valid.ok.txt') }) }
            $report = Invoke-TestAllHarness 'Offline' (Join-Path $root 'no-runtime') '' $manifest $context.ReportDir $false $true
            Assert-Equal $report.exitCode 2
            Assert-Equal $report.status 'BLOCKED'
            Assert-Equal $report.fixtures[0].metadata.totalPlaytimeSeconds '1000.25'
            Assert-Equal $report.fixtures[0].status 'INSPECTED_NOT_EXECUTED'
            Assert-Equal $report.fixtures[0].executed $false
            Assert-Equal $report.commands.Count 0
            Assert-Equal $report.originalRuntime $null
            Assert-Equal $report.fullOfflineGatePassed $false
        }
        Test-Case 'confirmed recovery uses the runtime catalog rather than inspected fixture playtime or hash' {
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
        Test-Case 'corrupt manifest fixture fails the native harness before any game command or recovery' {
            $context = New-Context; $job = Start-Bridge $context 'Behind'
            try {
                $manifest = Join-Path $context.ReportDir 'manifest.json'
                Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'broken'; path = (Join-Path $nativeFixtures 'wrong-checksum.bad.txt') }) }
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Prepare -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir -FixtureManifestPath $manifest -ConfirmLiveSaveLoad -SkipOfflineChecks
                Assert-Equal $LASTEXITCODE 1
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                $report = Read-TestAllJson $path
                Assert-Equal $report.status 'FAIL'
                Assert-Equal $report.commands.Count 0
                Assert-Equal $report.baseline $null
                Assert-Equal $report.fixtures[0].decoded $false
                Assert-Equal $report.fixtures[0].executed $false
                Assert-Equal (Get-Requests $context).Count 0
            } finally { Stop-Bridge $job }
        }
        Test-Case 'inspected fixture playtime never drives a native load and Live remains blocked' {
            $context = New-Context; $job = Start-Bridge $context
            try {
                $manifest = Join-Path $context.ReportDir 'manifest.json'
                Write-TestAllJson $manifest @{ version = 1; fixtures = @(@{ name = 'huge-playtime'; path = (Join-Path $nativeFixtures 'large-finite-clocks.ok.txt'); scenario = 'fixture only' }) }
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Live -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir -FixtureManifestPath $manifest -ConfirmLiveSaveLoad -SkipOfflineChecks
                Assert-Equal $LASTEXITCODE 2
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                $report = Read-TestAllJson $path
                Assert-Equal $report.status 'BLOCKED'
                Assert-Equal $report.liveFixtureBlocker 'Synthetic isolation blocker: autosave/cloud/achievements/checkpoints are not isolated.'
                Assert-Equal $report.fixtures[0].status 'INSPECTED_NOT_EXECUTED'
                Assert-Equal $report.fixtures[0].metadata.rebirth '9223372036854775807'
                Assert-Equal ((Get-TestAllClock $report.fixtures[0].metadata.totalPlaytimeSeconds) -gt (Get-TestAllClock $report.preparedRuntime.saveSafety.longest.totalPlaytimeSeconds)) $true
                Assert-Equal $report.fixtures[0].executed $false
                Assert-Equal $report.liveFixturesExecuted 0
                Assert-Equal @($report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves')
                Assert-Equal @($report.phases | Where-Object name -EQ 'inspector.build').Count 1
                $inspectionIndex = @($report.phases.name).IndexOf('fixtures.inspector.1')
                $preflightIndex = @($report.phases.name).IndexOf('prepare.preflight')
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
            Assert-Throws { Invoke-TestAllHarness 'Offline' $context.RuntimeDir '' '' $context.RuntimeDir $false $true } 'outside RuntimeDir'
            Assert-Throws { Invoke-TestAllHarness 'Offline' ($context.RuntimeDir + '\') '' '' $context.RuntimeDir $false $true } 'outside RuntimeDir'
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
        Test-Case 'capture metadata tolerance is based on elapsed seconds, not a seven-day session' {
            $context = New-Context; $job = Start-Bridge $context
            try {
                $runtime = Read-TestAllRuntime $context
                $result = Invoke-TestAllCommand $context 'captureLocalSave'
                $result.data.save.totalPlaytimeSeconds = '11000.25'
                Assert-Throws { Assert-TestAllCapture $result.data.save $runtime.saveSafety.current $runtime.saveSafety.current 1 } 'tolerance'
            } finally { Stop-Bridge $job }
        }
        foreach ($mode in @('Offline', 'Prepare', 'Live')) {
            Test-Case "native script exit/report is honest for diagnostic-only $mode" {
                $context = New-Context; $job = $null
                if ($mode -ne 'Offline') { $job = Start-Bridge $context }
                try {
                    $before = @([IO.Directory]::GetFiles($context.RuntimeDir, '*', [IO.SearchOption]::AllDirectories))
                    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode $mode -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir -SkipOfflineChecks 2>&1)
                    Assert-Equal $LASTEXITCODE 2 'native exit code'
                    $reports = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))
                    Assert-Equal $reports.Count 1
                    $report = Read-TestAllJson $reports[0]
                    Assert-Equal $report.status 'BLOCKED'
                    Assert-Equal $report.success $false
                    Assert-Equal $report.fullOfflineGatePassed $false
                    Assert-Equal $report.liveFixturesExecuted 0
                    Assert-Equal $report.restoration.restored $false
                    Assert-Equal $report.restoration.attempted $false
                    Assert-Equal $report.fixtures.Count 0
                    Assert-Equal @($report.phases | Where-Object name -EQ 'inspector.build').Count 0
                    if ($mode -eq 'Offline') {
                        Assert-Equal $report.commands.Count 0
                        Assert-Equal @([IO.Directory]::GetFiles($context.RuntimeDir, '*', [IO.SearchOption]::AllDirectories)) $before
                    } else {
                        Assert-Equal $report.preparationPassed $true
                        Assert-Equal @($report.commands | ForEach-Object { $_.request.command }) @('captureLocalSave', 'scanSaves')
                    }
                    if ($mode -eq 'Prepare') { Assert-Equal $report.runtimeDiagnostics.success $true }
                    if ($mode -eq 'Live') {
                        Assert-Equal $report.liveFixtureBlocker 'Synthetic isolation blocker: autosave/cloud/achievements/checkpoints are not isolated.'
                        Assert-Equal @($report.phases | Where-Object name -EQ 'live.fixtureIsolationGate')[0].status 'BLOCKED'
                        Assert-Equal ($output -join "`n" -match 'Synthetic isolation blocker') $true
                    }
                } finally { if ($job) { Stop-Bridge $job } }
            }
        }
        Test-Case 'finally writes a failure report when native capture evidence is invalid' {
            $context = New-Context; $job = Start-Bridge $context 'CaptureHash'
            try {
                $null = & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -NonInteractive -File $harness -Mode Prepare -RuntimeDir $context.RuntimeDir -ReportParent $context.ReportDir -SkipOfflineChecks
                Assert-Equal $LASTEXITCODE 1
                $path = @([IO.Directory]::GetFiles($context.ReportDir, 'report.json', [IO.SearchOption]::AllDirectories))[0]
                $report = Read-TestAllJson $path
                Assert-Equal $report.status 'FAIL'
                Assert-Equal $report.commands.Count 1
                Assert-Equal $report.restoration.originalBackupVerified $false
                Assert-Equal $report.phases[2].status 'FAIL'
                Assert-Equal ($null -ne $report.completedAt) $true
            } finally { Stop-Bridge $job }
        }
        $failures = @($results | Where-Object success -EQ $false)
        if ($failures.Count) { throw "$($failures.Count)/$($results.Count) harness checks failed:`n$((@($failures | ForEach-Object { $_.name + ': ' + $_.error })) -join "`n")" }
        [pscustomobject]@{ success = $true; passed = $results.Count; failed = 0; liveActions = 0; cases = $results.ToArray() }
    } finally {
        foreach ($job in $jobs.ToArray()) { Stop-Job -Job $job -ErrorAction SilentlyContinue; Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
        if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
    }
}
