#requires -Version 7.0
<#
.SYNOPSIS
Offline verification, or explicit native-save protection and longest-playtime recovery.
.DESCRIPTION
Offline (default) never reads the live bridge or changes the game. It runs verify-ui
once with BrowserTests/GameFolder (without IncludeInjector), then independently runs
checks/VerifyExecutorOffline.ps1 once as offline.nativeExecutor even if UI checks fail.
Save codec, inspector, bridge, diagnostics and harness checks also run. The full offline
gate passes only when all gates pass; each gate retains its own log and errors.
Prepare first captures a NEW native local save (never quickSave),
then waits at most 120 seconds for a full scan. Loading a longer-playtime save requires
-ConfirmLiveSaveLoad, then runs the read-only runtime diagnostics. No resume, rebind,
stage reset, or settings fallback is sent.

Live performs the same preparation but ALWAYS reports BLOCKED: native autosaves,
Steam Cloud/achievements and automation checkpoints are not isolated. No older fixture
is ever loaded, so original restoration is never claimed. Fixture manifests are v1
objects with a fixtures array of name/path and optional scenario/plan/stage metadata.
Registered files are hashed and safely decoded offline by the compiled SaveInspector
CLI before any game load. Decoded metadata is retained as INSPECTED_NOT_EXECUTED;
it never selects a native fixture load. Tied runtime saves require manual review.
The inspector is built for nonempty manifests and the full offline gate, not for an
empty diagnostic-only run. Runtime capture metadata remains backend-decoded evidence.
Playtime comparisons use saved clocks, never file dates or session age.

Only commands/*.json (atomically published) and a new report directory are written by
the harness itself; offline tools create their usual test/build artifacts. Persistent
backend claims/results are never removed or replayed. An
unacknowledged command has UNKNOWN outcome, even if a timeout occurred. Reports retain
every phase, request, result and error. Exit codes: 0 selected mode passed, 1 failure,
2 blocked/incomplete. SkipOfflineChecks is diagnostics-only and can NEVER pass the gate.
FunctionsOnly exports helpers for isolated tests without running checks or live actions.
.EXAMPLE
pwsh -NoProfile -File ./test-all.ps1 -GameFolder 'C:\Games\NGU IDLE'
.EXAMPLE
pwsh -NoProfile -File ./test-all.ps1 -Mode Prepare -ConfirmLiveSaveLoad -GameFolder 'C:\Games\NGU IDLE'
#>
[CmdletBinding()]
param(
    [ValidateSet('Offline', 'Prepare', 'Live')][string]$Mode = 'Offline',
    [string]$RuntimeDir = (Join-Path $env:USERPROFILE 'Desktop/NGUInjector'),
    [string]$GameFolder = $env:NGU_IDLE_DIR,
    [string]$FixtureManifestPath,
    [string]$ReportParent = (Join-Path $env:LOCALAPPDATA 'Temp/opencode'),
    [switch]$ConfirmLiveSaveLoad,
    [switch]$SkipOfflineChecks,
    [switch]$FunctionsOnly
)

function Read-TestAllJson([string]$Path) {
    $options = @{ AsHashtable = $true; Depth = 100 }
    # Preserve startedAt EXACTLY for the backend's session guard, including its offset.
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $options.DateKind = 'String' }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream)
    try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $value = ConvertFrom-Json -InputObject $json @options
    if ($value -isnot [Collections.IDictionary]) { throw "JSON must be an object: $Path" }
    return $value
}

function Write-TestAllJson([string]$Path, $Value) {
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $Value -Depth 100), [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $Path, $true)
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Save-TestAllReport($Context) {
    Write-TestAllJson (Join-Path $Context.ReportDir 'report.json') $Context.Report
}

function Stop-TestAllBlocked([string]$Message) {
    $exception = [InvalidOperationException]::new($Message)
    $exception.Data['TestAllBlocked'] = $true
    throw $exception
}

function Invoke-TestAllPhase($Context, [string]$Name, [scriptblock]$Action) {
    $phase = [ordered]@{ name = $Name; status = 'RUNNING'; startedAt = [datetimeoffset]::UtcNow.ToString('o'); completedAt = $null; evidence = $null; error = $null }
    $Context.Report.phases.Add($phase)
    Save-TestAllReport $Context
    try {
        $phase.evidence = & $Action
        $phase.status = 'PASS'
        if ($null -ne $phase.evidence) { return $phase.evidence }
    } catch {
        $phase.status = if ($_.Exception.Data['TestAllBlocked']) { 'BLOCKED' } else { 'FAIL' }
        $phase.error = $_.Exception.Message
        $Context.Report.errors.Add(@{ phase = $Name; status = $phase.status; message = $_.Exception.Message })
        throw
    } finally {
        $phase.completedAt = [datetimeoffset]::UtcNow.ToString('o')
        Save-TestAllReport $Context
    }
}

function Get-TestAllTimestamp($Value) {
    $parsed = [datetimeoffset]::MinValue
    if ($Value -isnot [string] -or -not [datetimeoffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) { throw 'Missing or invalid timestamp.' }
    return $parsed
}

function Assert-TestAllRuntime($Runtime, [string]$Session) {
    if ($Runtime -isnot [Collections.IDictionary]) { throw 'Runtime must be an object.' }
    $updated = Get-TestAllTimestamp $Runtime.updatedAt
    $started = Get-TestAllTimestamp $Runtime.startedAt
    $age = ([datetimeoffset]::UtcNow - $updated).TotalSeconds
    if ($age -lt -1 -or $age -gt 5) { throw "Runtime heartbeat is not fresh (age $age seconds; limit 5)." }
    if ($started -gt $updated.AddSeconds(1)) { throw 'Runtime session is in the future.' }
    if ($Session -and $Runtime.startedAt -cne $Session) { throw 'Runtime session changed; no command may be replayed.' }
    if ($Runtime.status -cne 'running' -or $Runtime.execution.lifecycle -cne 'Running') { throw 'Runtime and execution must both be Running; no automatic resume is permitted.' }
    if (($Runtime.runtimeRevision -isnot [long] -and $Runtime.runtimeRevision -isnot [int]) -or
        $Runtime.runtimeRevision -lt 0 -or $Runtime.runtimeRevision -gt 9007199254740991L) { throw 'Invalid runtime revision.' }
}

function Read-TestAllRuntime($Context) {
    $runtime = Read-TestAllJson (Join-Path $Context.RuntimeDir 'runtime.json')
    Assert-TestAllRuntime $runtime $Context.Session
    if ($null -ne $Context.LastRuntimeRevision -and $runtime.runtimeRevision -lt $Context.LastRuntimeRevision) { throw 'Runtime revision regressed within the pinned session.' }
    $Context.LastRuntimeRevision = $runtime.runtimeRevision
    return $runtime
}

function Get-TestAllClock($Value) {
    $number = 0.0
    if ($Value -isnot [string] -or -not [double]::TryParse($Value, [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or -not [double]::IsFinite($number) -or $number -lt 0) {
        throw 'Save clocks must be finite, nonnegative invariant numeric strings.'
    }
    return $number
}

function Assert-TestAllSave($Save, [switch]$FileIdentity) {
    if ($Save -isnot [Collections.IDictionary]) { throw 'Missing save metadata.' }
    $total = Get-TestAllClock $Save.totalPlaytimeSeconds
    $elapsed = Get-TestAllClock $Save.rebirthSeconds
    if ($elapsed -gt $total -or $Save.rebirth -isnot [string] -or $Save.rebirth -cnotmatch '\A(?:0|[1-9][0-9]*)\z' -or
        [bigint]$Save.rebirth -gt [long]::MaxValue -or $Save.playerName -isnot [string] -or
        ($Save.version -isnot [int] -and $Save.version -isnot [long]) -or $Save.version -lt 1) { throw 'Invalid decoded save identity.' }
    if ($FileIdentity -and ($Save.path -isnot [string] -or -not [IO.Path]::IsPathFullyQualified($Save.path) -or
        $Save.sha256 -isnot [string] -or $Save.sha256 -cnotmatch '\A[0-9a-fA-F]{64}\z')) { throw 'Missing save path or SHA-256 identity.' }
}

function Assert-TestAllSaveFile($Save) {
    Assert-TestAllSave $Save -FileIdentity
    if (-not (Test-Path -LiteralPath $Save.path -PathType Leaf)) { throw "Native backup/selected save does not exist: $($Save.path)" }
    $actual = (Get-FileHash -LiteralPath $Save.path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $Save.sha256.ToLowerInvariant()) { throw 'Save file SHA-256 does not match the decoded identity.' }
    return @{ path = $Save.path; sha256 = $actual; bytesVerified = $true; decodedBy = 'backend SaveSafetyCatalog'; independentlyDecoded = $false }
}

function Assert-TestAllCapture($Save, $Before, $After, [double]$ElapsedSeconds) {
    $file = Assert-TestAllSaveFile $Save
    Assert-TestAllSave $Before
    Assert-TestAllSave $After
    foreach ($field in @('rebirth', 'version', 'playerName')) {
        if ($Save[$field] -cne $Before[$field] -or $Save[$field] -cne $After[$field]) { throw "Captured $field does not match runtime; baseline cannot be verified." }
    }
    foreach ($field in @('totalPlaytimeSeconds', 'rebirthSeconds')) {
        $captured = Get-TestAllClock $Save[$field]
        $low = Get-TestAllClock $Before[$field]
        $high = Get-TestAllClock $After[$field]
        # Bound by wall-clock elapsed since the fresh observation, NOT injector uptime.
        $tolerance = $ElapsedSeconds + 6
        if ($captured -lt $low - 0.01 -or $captured -gt $high + 0.01 -or
            $captured - $low -gt $tolerance -or $high - $captured -gt $tolerance) { throw "Captured $field disagrees with runtime beyond elapsed-time tolerance." }
    }
    return $file
}

function Invoke-TestAllCommand($Context, [ValidateSet('captureLocalSave', 'scanSaves', 'loadLongestSave')][string]$Command, [hashtable]$Arguments = @{}) {
    if ($Context.CommandTimeoutSeconds -lt 1 -or $Context.CommandTimeoutSeconds -gt 120) { throw 'Command lifetime must be between 1 and 120 seconds.' }
    $runtime = Read-TestAllRuntime $Context
    if (-not $Context.Session) { $Context.Session = $runtime.startedAt }
    if ($runtime.supportedCommands -isnot [array] -or $Command -cnotin $runtime.supportedCommands) { throw "Backend does not advertise $Command." }
    if ($Command -eq 'loadLongestSave') {
        if (-not $Context.ConfirmLiveSaveLoad) { Stop-TestAllBlocked 'Longest-playtime loading requires -ConfirmLiveSaveLoad.' }
        if ($runtime.saveSafety.ambiguousLongest -isnot [bool] -or $runtime.saveSafety.ambiguousLongest) { Stop-TestAllBlocked 'Longest-playtime save is ambiguous or ambiguity is unknown; manual review required. No branch was selected.' }
        if ($Arguments.Count -ne 2 -or $Arguments.expectedStartedAt -cne $Context.Session -or
            $Arguments.sha256 -cne $runtime.saveSafety.longest.sha256) { throw 'Longest-save request must match the current catalog and session.' }
    } elseif ($Arguments.Count -ne 0) { throw "$Command accepts no arguments." }
    foreach ($directory in @('commands', 'commands/claimed', 'command-results')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Context.RuntimeDir $directory) -PathType Container)) { throw "Bridge directory missing: $directory" }
    }
    $id = 'test-all-' + [guid]::NewGuid().ToString('N')
    $path = Join-Path $Context.RuntimeDir "commands/$id.json"
    $resultPath = Join-Path $Context.RuntimeDir "command-results/$id.json"
    $created = [datetimeoffset]::UtcNow
    $request = @{ id = $id; command = $Command; args = $Arguments; createdAt = $created.ToString('o'); expiresAt = $created.AddSeconds($Context.CommandTimeoutSeconds).ToString('o') }
    $entry = [ordered]@{ request = $request; outcome = 'NOT_PUBLISHED'; result = $null; error = $null; claimPath = (Join-Path $Context.RuntimeDir "commands/claimed/$id.claimed") }
    $Context.Report.commands.Add($entry)
    Save-TestAllReport $Context
    $temporary = $path + '.tmp'
    try {
        if ([IO.File]::Exists($path) -or [IO.File]::Exists($resultPath) -or [IO.File]::Exists($entry.claimPath)) { throw 'Unique command ID collision; no command published.' }
        [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $request -Depth 10), [Text.UTF8Encoding]::new($false))
        # Persist uncertainty BEFORE publication; a crash must never imply permission to retry.
        $entry.outcome = 'UNKNOWN'
        Save-TestAllReport $Context
        [IO.File]::Move($temporary, $path)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        do {
            $null = Read-TestAllRuntime $Context
            if ([IO.File]::Exists($resultPath)) {
                $result = Read-TestAllJson $resultPath
                $entry.result = $result
                if ($result.id -cne $id -or $result.command -cne $Command -or $result.success -isnot [bool]) { throw 'Result ID/command/success contract mismatch; outcome UNKNOWN.' }
                $completed = Get-TestAllTimestamp $result.completedAt
                if ($completed -lt $created.AddSeconds(-1) -or $completed -gt [datetimeoffset]::UtcNow.AddSeconds(1) -or
                    $completed -gt $created.AddSeconds($Context.CommandTimeoutSeconds)) { throw 'Result completion timestamp is outside this request lifetime; outcome UNKNOWN.' }
                Assert-TestAllRuntime $result.data.runtime $Context.Session
                $entry.outcome = if ($result.success) { 'SUCCEEDED' } else { 'FAILED' }
                if (-not $result.success) { throw "Backend rejected ${Command}: $($result.message)" }
                return $result
            }
            if ($clock.Elapsed.TotalSeconds -ge $Context.CommandTimeoutSeconds) { throw "Timed out waiting for $Command; outcome UNKNOWN. Do not replay; inspect persistent claims/results." }
            Start-Sleep -Milliseconds $Context.PollMilliseconds
        } while ($true)
    } catch {
        $entry.error = $_.Exception.Message
        throw
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        Save-TestAllReport $Context
    }
}

function Wait-TestAllScan($Context, [long]$AfterRevision, [Diagnostics.Stopwatch]$Clock = [Diagnostics.Stopwatch]::StartNew()) {
    if ($Context.ScanTimeoutSeconds -le 0 -or $Context.ScanTimeoutSeconds -gt 120) { throw 'Scan timeout must be positive and at most 120 seconds.' }
    do {
        $runtime = Read-TestAllRuntime $Context
        $safety = $runtime.saveSafety
        if (($safety.version -isnot [int] -and $safety.version -isnot [long]) -or $safety.version -ne 1 -or
            $safety.scanComplete -isnot [bool] -or $safety.errors -isnot [array] -or
            ($safety.pendingFiles -isnot [int] -and $safety.pendingFiles -isnot [long]) -or $safety.pendingFiles -lt 0) { throw 'Invalid save-safety scan contract.' }
        $Context.Report.lastScan = @{ runtimeRevision = $runtime.runtimeRevision; saveSafety = $safety }
        Save-TestAllReport $Context
        if ($safety.status -ceq 'recoveryRequired' -or $safety.errors.Count -gt 0) { throw "Save scan/recovery error: $($safety.message) $($safety.errors | ConvertTo-Json -Compress -Depth 10)" }
        if ($runtime.runtimeRevision -ge $AfterRevision -and $safety.scanComplete -and $safety.pendingFiles -eq 0) {
            if ($safety.ambiguousLongest -isnot [bool] -or $safety.ambiguousLongest) { Stop-TestAllBlocked 'Longest-playtime save is ambiguous or ambiguity is unknown; manual review required. No branch was selected.' }
            if ($safety.status -cnotin @('verified', 'behind')) { throw "Full scan did not verify save safety: $($safety.status)" }
            if ($safety.testActive -isnot [bool] -or $safety.testActive) { throw 'A fixture test is already active or its status is unknown.' }
            Assert-TestAllSave $safety.current
            Assert-TestAllSave $safety.longest -FileIdentity
            $behind = (Get-TestAllClock $safety.current.totalPlaytimeSeconds) + 0.001 -lt (Get-TestAllClock $safety.longest.totalPlaytimeSeconds)
            if (($safety.status -ceq 'behind') -ne $behind) { throw 'Save-safety status contradicts decoded total playtime.' }
            return $runtime
        }
        if ($clock.Elapsed.TotalSeconds -ge $Context.ScanTimeoutSeconds) { throw "Full save scan timed out; pending files: $($safety.pendingFiles). No load attempted." }
        Start-Sleep -Milliseconds $Context.PollMilliseconds
    } while ($true)
}

function Invoke-TestAllPrepare($Context) {
    $before = Invoke-TestAllPhase $Context 'prepare.preflight' {
        $runtime = Read-TestAllRuntime $Context
        foreach ($command in @('captureLocalSave', 'scanSaves', 'loadLongestSave')) {
            if ($command -cnotin $runtime.supportedCommands) { throw "Backend lacks required command $command." }
        }
        $Context.Session = $runtime.startedAt
        $Context.Report.originalRuntime = $runtime
        return $runtime
    }
    $null = Invoke-TestAllPhase $Context 'prepare.captureOriginal' {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-TestAllCommand $Context 'captureLocalSave'
        $Context.Report.baseline = $result.data.save
        $file = Assert-TestAllCapture $result.data.save $before.saveSafety.current $result.data.runtime.saveSafety.current $clock.Elapsed.TotalSeconds
        $Context.Report.restoration.originalBackupVerified = $true
        return $file
    }
    $runtime = Invoke-TestAllPhase $Context 'prepare.fullScan' {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-TestAllCommand $Context 'scanSaves'
        return Wait-TestAllScan $Context $result.data.runtime.runtimeRevision $clock
    }
    $Context.Report.preparedRuntime = $runtime
    if ($runtime.saveSafety.status -ceq 'behind') {
        $runtime = Invoke-TestAllPhase $Context 'prepare.recoverLongest' {
            if (-not $Context.ConfirmLiveSaveLoad) { Stop-TestAllBlocked 'Current game is behind the longest PLAYTIME. Retained original backup; pass -ConfirmLiveSaveLoad to permit recovery. No load attempted.' }
            if ($runtime.saveSafety.ambiguousLongest -isnot [bool] -or $runtime.saveSafety.ambiguousLongest) { Stop-TestAllBlocked 'Longest-playtime save is ambiguous or ambiguity is unknown; manual review required.' }
            $selected = $runtime.saveSafety.longest
            $null = Assert-TestAllSaveFile $selected
            if ($selected.playerName -cne $Context.Report.baseline.playerName) { Stop-TestAllBlocked 'Longest save belongs to a different player; manual review required.' }
            $result = Invoke-TestAllCommand $Context 'loadLongestSave' @{ sha256 = $selected.sha256; expectedStartedAt = $Context.Session }
            $Context.Report.recovery = $result.data
            if ($result.data.loaded -isnot [bool]) { throw 'Load result does not say whether native loading occurred.' }
            $null = Assert-TestAllSaveFile $result.data.backup
            if ($result.data.selected.sha256 -cne $selected.sha256) { throw 'Load result selected a different save.' }
            $fresh = Wait-TestAllScan $Context $result.data.runtime.runtimeRevision
            if ($fresh.saveSafety.status -cne 'verified' -or
                (Get-TestAllClock $fresh.saveSafety.current.totalPlaytimeSeconds) + 0.001 -lt (Get-TestAllClock $selected.totalPlaytimeSeconds)) { throw 'Longest-playtime recovery was not freshly verified.' }
            if ($result.data.loaded) {
                foreach ($field in @('playerName', 'rebirth', 'version')) {
                    if ($fresh.saveSafety.current[$field] -cne $selected[$field]) { throw "Recovered $field does not match the selected save." }
                }
            }
            $Context.Report.restoration.reason = 'No older fixture loaded. Original backup retained; longest-playtime recovery was requested. No automatic restore/resume/rebind was attempted.'
            return $fresh
        }
        $Context.Report.preparedRuntime = $runtime
    }
    $Context.Report.preparationPassed = $true
    Save-TestAllReport $Context
    return $runtime
}

function Read-TestAllManifest([string]$Path) {
    if (-not $Path) { return @() }
    $full = [IO.Path]::GetFullPath($Path)
    $manifest = Read-TestAllJson $full
    if (($manifest.version -isnot [int] -and $manifest.version -isnot [long]) -or $manifest.version -ne 1 -or
        $manifest.fixtures -isnot [array]) { throw 'Fixture manifest requires version 1 and a fixtures array.' }
    $names = @{}
    $fixtures = foreach ($fixture in $manifest.fixtures) {
        if ($fixture -isnot [Collections.IDictionary] -or $fixture.name -isnot [string] -or [string]::IsNullOrWhiteSpace($fixture.name) -or
            $names.ContainsKey($fixture.name) -or $fixture.path -isnot [string] -or [string]::IsNullOrWhiteSpace($fixture.path)) { throw 'Each fixture requires a unique name and a local path.' }
        $names[$fixture.name] = $true
        $file = [IO.Path]::GetFullPath($fixture.path, [IO.Path]::GetDirectoryName($full))
        if ($file.StartsWith('\\') -or $file.StartsWith('//')) { throw 'Fixture paths must be local, not network shares.' }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Configured fixture file does not exist: $file" }
        @{ name = $fixture.name; path = $file; scenario = $fixture.scenario; plan = $fixture.plan; stage = $fixture.stage
            sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
            status = 'CONFIGURED_NOT_EXECUTED'; inspected = $false; decoded = $false; executed = $false; metadata = $null
            limitation = 'Awaiting read-only SaveInspector decode. No live fixture execution is permitted.' }
    }
    return @($fixtures)
}

function Invoke-TestAllNative($Context, [string]$Name, [string]$Executable, [string[]]$Arguments, [switch]$JsonOutput) {
    $PSNativeCommandUseErrorActionPreference = $false
    $evidence = Invoke-TestAllPhase $Context $Name {
        $log = Join-Path $Context.ReportDir ($Name + '.log')
        $evidence = @{ executable = $Executable; arguments = $Arguments; log = $log; exitCode = $null }
        $Context.Report.phases[$Context.Report.phases.Count - 1].evidence = $evidence
        Save-TestAllReport $Context
        $program = $Executable; $programArguments = $Arguments
        # FunctionsOnly tests may substitute a child-process boundary, never gate internals.
        if ($Context.NativeProcessBoundary) {
            $program = Join-Path $PSHOME 'pwsh.exe'
            $encodedArguments = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($Arguments) -Compress)))
            $programArguments = @('-NoProfile', '-NonInteractive', '-File', $Context.NativeProcessBoundary, '-Executable', $Executable, '-ArgumentsBase64', $encodedArguments)
            $evidence.processBoundary = $Context.NativeProcessBoundary
        }
        if ($JsonOutput) {
            $evidence.stderrLog = Join-Path $Context.ReportDir ($Name + '.stderr.log')
            $stdout = @(& $program @programArguments 2> $evidence.stderrLog)
            [IO.File]::WriteAllText($log, ($stdout -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
        } else { & $program @programArguments 2>&1 | Tee-Object -FilePath $log | Out-Host }
        $evidence.exitCode = $LASTEXITCODE
        if ($JsonOutput) { $evidence.output = Read-TestAllJson $log }
        if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE. See $log" }
        return $evidence
    }
    if ($JsonOutput) { return $evidence.output }
}

function Initialize-TestAllInspector($Context, [string]$Repo) {
    if ($Context.InspectorDll) {
        if (-not (Test-Path -LiteralPath $Context.InspectorDll -PathType Leaf)) { throw 'Previously built SaveInspector DLL is missing.' }
        return $Context.InspectorDll
    }
    $output = Join-Path $Context.ReportDir 'save-inspector'
    Invoke-TestAllNative $Context 'inspector.build' 'dotnet' @('build', (Join-Path $Repo 'checks/SaveSafety/SaveInspector.csproj'), '--configuration', 'Release', '--nologo', '--output', $output)
    $dll = Join-Path $output 'SaveInspector.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "SaveInspector build did not produce $dll" }
    $Context.InspectorDll = $dll
    return $dll
}

function Invoke-TestAllFixtureInspection($Context, [string]$Repo) {
    if ($Context.Report.fixtures.Count -eq 0) { return }
    $null = Invoke-TestAllPhase $Context 'fixtures.inspect' {
        $dll = Initialize-TestAllInspector $Context $Repo
        $index = 0
        foreach ($fixture in $Context.Report.fixtures) {
            $index++
            # Decode each registered path, never ask the fixture set to select a branch.
            $inspection = Invoke-TestAllNative $Context "fixtures.inspector.$index" 'dotnet' @($dll, $fixture.path) -JsonOutput
            if (($inspection.version -isnot [int] -and $inspection.version -isnot [long]) -or $inspection.version -ne 1 -or
                $inspection.success -isnot [bool] -or -not $inspection.success -or $inspection.files -isnot [array] -or
                $inspection.files.Count -ne 1 -or $inspection.errors -isnot [array] -or $inspection.errors.Count -ne 0 -or
                $inspection.ambiguousLongest -isnot [bool] -or $inspection.ambiguousLongest) { throw 'SaveInspector did not return one successful, unambiguous file inspection.' }
            $metadata = $inspection.files[0]
            Assert-TestAllSave $metadata -FileIdentity
            if (-not [string]::Equals($metadata.path, $fixture.path, [StringComparison]::OrdinalIgnoreCase) -or
                $metadata.sha256.ToLowerInvariant() -cne $fixture.sha256) { throw "Fixture path/SHA-256 changed since registration: $($fixture.name)" }
            $null = Assert-TestAllSaveFile $metadata
            $fixture.metadata = $metadata
            $fixture.inspected = $true; $fixture.decoded = $true
            $fixture.status = 'INSPECTED_NOT_EXECUTED'
            $fixture.limitation = 'Read-only inspection only. Native autosave, cloud/achievement and checkpoint isolation is unavailable; no fixture execution or restoration was attempted.'
            Save-TestAllReport $Context
        }
        return $Context.Report.fixtures
    }
}

function Invoke-TestAllOffline($Context, [string]$Repo, [string]$GameFolder) {
    $pwsh = Join-Path $PSHOME 'pwsh.exe'
    $failures = [Collections.Generic.List[string]]::new()
    # Independent gates: browser failures must not hide native executor regressions.
    try { Invoke-TestAllNative $Context 'offline.verifyUi' $pwsh @('-NoProfile', '-NonInteractive', '-File', (Join-Path $Repo 'verify-ui.ps1'), '-BrowserTests', '-GameFolder', $GameFolder) }
    catch { $failures.Add($_.Exception.Message) }
    try { Invoke-TestAllNative $Context 'offline.nativeExecutor' $pwsh @('-NoProfile', '-NonInteractive', '-File', (Join-Path $Repo 'checks/VerifyExecutorOffline.ps1'), '-GameFolder', $GameFolder) }
    catch { $failures.Add($_.Exception.Message) }
    try { $null = Initialize-TestAllInspector $Context $Repo }
    catch { $failures.Add($_.Exception.Message) }
    $checks = Join-Path $Repo 'checks/SaveSafety'
    $fixtures = Join-Path $Context.ReportDir 'synthetic-fixtures'
    try {
        Invoke-TestAllNative $Context 'offline.buildSyntheticGenerator' 'dotnet' @('build', (Join-Path $checks 'Fixtures/FixtureGenerator.csproj'), '--configuration', 'Release', '--nologo')
        Invoke-TestAllNative $Context 'offline.generateSyntheticFixtures' (Join-Path $checks 'Fixtures/bin/Release/net48/FixtureGenerator.exe') @($fixtures)
        try {
            $msbuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe'
            Invoke-TestAllNative $Context 'offline.buildPackagedCodec' $msbuild @((Join-Path $checks 'PackagedCodecChecks.csproj'), '/t:Build', '/p:Configuration=Release', '/nologo', '/v:minimal')
            Invoke-TestAllNative $Context 'offline.packagedCodec' (Join-Path $checks 'bin/PackagedCodec/PackagedCodecChecks.exe') @((Join-Path $Repo 'NGUInjector/bin/Release/NGUInjector.dll'), (Join-Path $fixtures 'valid.ok.txt'))
        } catch { $failures.Add($_.Exception.Message) }
        foreach ($project in @('SaveSafetyChecks', 'BridgeChecks')) {
            try { Invoke-TestAllNative $Context "offline.$project" 'dotnet' @('run', '--project', (Join-Path $checks "$project.csproj"), '--configuration', 'Release', '--', $fixtures) }
            catch { $failures.Add($_.Exception.Message) }
        }
    } catch { $failures.Add($_.Exception.Message) }
    foreach ($script in @('DiagnosticsChecks', 'TestAllChecks')) {
        try { Invoke-TestAllNative $Context "offline.$script" $pwsh @('-NoProfile', '-NonInteractive', '-File', (Join-Path $checks "$script.ps1")) }
        catch { $failures.Add($_.Exception.Message) }
    }
    if ($failures.Count) { throw "Offline gate failed: $($failures -join ' | ')" }
    $Context.Report.fullOfflineGatePassed = $true
}

function Invoke-TestAllHarness(
    [ValidateSet('Offline', 'Prepare', 'Live')][string]$Mode,
    [string]$RuntimeDir, [string]$GameFolder, [string]$FixtureManifestPath, [string]$ReportParent,
    [bool]$ConfirmLiveSaveLoad, [bool]$SkipOfflineChecks
) {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off
    if (-not (Test-Path -LiteralPath $ReportParent -PathType Container)) { throw "Report parent must already exist: $ReportParent" }
    $parent = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ReportParent))
    $runtimeDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RuntimeDir))
    $prefix = $runtimeDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($parent.Equals($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -or $parent.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'ReportParent must be outside RuntimeDir.' }
    $directory = Join-Path $parent ('ngu-test-all-' + [datetimeoffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($directory)
    $report = [ordered]@{
        version = 1; mode = $Mode; startedAt = [datetimeoffset]::UtcNow.ToString('o'); completedAt = $null
        status = 'RUNNING'; success = $false; exitCode = 1; reportDirectory = $directory
        fullOfflineGatePassed = $false; offlineChecksSkipped = $SkipOfflineChecks; preparationPassed = $false
        liveFixturesExecuted = 0; liveFixtureBlocker = $null; baseline = $null; originalRuntime = $null; preparedRuntime = $null; recovery = $null; lastScan = $null; runtimeDiagnostics = $null
        fixtures = @(); phases = [Collections.Generic.List[object]]::new(); commands = [Collections.Generic.List[object]]::new(); errors = [Collections.Generic.List[object]]::new()
        restoration = @{ required = $false; attempted = $false; restored = $false; originalBackupVerified = $false; status = 'NOT_REQUIRED'; reason = 'No older fixture was loaded. No original restoration is claimed.' }
    }
    $context = @{ RuntimeDir = $runtimeDirectory; ReportDir = $directory; Report = $report; Session = $null
        ConfirmLiveSaveLoad = $ConfirmLiveSaveLoad; CommandTimeoutSeconds = 120; ScanTimeoutSeconds = 120; PollMilliseconds = 200 }
    try {
        $report.fixtures = @(Invoke-TestAllPhase $context 'fixtures.configure' { Read-TestAllManifest $FixtureManifestPath })
        Invoke-TestAllFixtureInspection $context $PSScriptRoot
        if ($Mode -ne 'Offline') {
            $prepared = Invoke-TestAllPrepare $context
            if ($Mode -eq 'Live') {
                $null = Invoke-TestAllPhase $context 'live.fixtureIsolationGate' {
                    $blocker = $prepared.saveSafety.liveFixtureBlocker
                    if ([string]::IsNullOrWhiteSpace($blocker)) { $blocker = 'Native autosave, cloud/achievement and checkpoint isolation is not implemented. Destructive fixture loads are disabled.' }
                    $report.liveFixtureBlocker = $blocker
                    Stop-TestAllBlocked $blocker
                }
            }
            if ($Mode -eq 'Prepare') {
                $null = Invoke-TestAllPhase $context 'prepare.runtimeDiagnostics' {
                    $null = Read-TestAllRuntime $context
                    $diagnostics = & (Join-Path $PSScriptRoot 'diagnostics.ps1') -RuntimeDir $runtimeDirectory
                    $report.runtimeDiagnostics = $diagnostics
                    $null = Read-TestAllRuntime $context
                    if ($diagnostics.success -isnot [bool] -or -not $diagnostics.success) { throw 'Read-only runtime diagnostics did not pass; no automatic runtime changes were attempted.' }
                    return $diagnostics
                }
            }
        }
    } catch {
        if ($report.errors.Count -eq 0) { $report.errors.Add(@{ phase = 'preparation'; status = 'FAIL'; message = $_.Exception.Message }) }
    } finally {
        try {
            if ($SkipOfflineChecks) {
                $null = Invoke-TestAllPhase $context 'offline.fullGate' { Stop-TestAllBlocked 'Offline checks skipped for diagnostics only; no full gate pass is claimed.' }
            } else { Invoke-TestAllOffline $context $PSScriptRoot $GameFolder }
        } catch {
            if (-not $SkipOfflineChecks) { $report.errors.Add(@{ phase = 'offline.fullGate'; status = 'FAIL'; message = $_.Exception.Message }) }
        } finally {
            $hasFailures = @($report.errors | Where-Object status -EQ 'FAIL').Count -gt 0
            $blocked = @($report.errors | Where-Object status -EQ 'BLOCKED').Count -gt 0
            $report.exitCode = if ($hasFailures) { 1 } elseif ($blocked) { 2 } else { 0 }
            $report.status = if ($hasFailures) { 'FAIL' } elseif ($blocked) { 'BLOCKED' } else { 'PASS' }
            $report.success = $report.exitCode -eq 0
            $report.completedAt = [datetimeoffset]::UtcNow.ToString('o')
            Save-TestAllReport $context
        }
    }
    return $report
}

if ($FunctionsOnly) { return }
try {
    $result = Invoke-TestAllHarness $Mode $RuntimeDir $GameFolder $FixtureManifestPath $ReportParent ([bool]$ConfirmLiveSaveLoad) ([bool]$SkipOfflineChecks)
    "test-all: $($result.status) (mode $Mode; offline gate passed: $($result.fullOfflineGatePassed); live fixtures executed: $($result.liveFixturesExecuted))"
    foreach ($issue in $result.errors) { "$($issue.status): $($issue.phase): $($issue.message)" }
    "Original restoration: $($result.restoration.status). $($result.restoration.reason)"
    "Report: $(Join-Path $result.reportDirectory 'report.json')"
    exit $result.exitCode
} catch {
    [Console]::Error.WriteLine('test-all: FAIL: ' + $_.Exception.Message)
    exit 1
}
