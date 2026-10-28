#requires -Version 7.0
<#
.SYNOPSIS
Bounded game diagnostics, fixture inspection, and guarded native-save testing/recovery.
.DESCRIPTION
Diagnostics (default) reads runtime diagnostics and writes a game-scoped report. It
never prepares the game or publishes commands. Inspect only decodes registered native
fixtures, without reading the bridge/runtime. Neither mode can enter preparation.
Prepare first captures a NEW native local save (never quickSave),
then waits at most 120 seconds for a full scan. Loading a longer-playtime save requires
-ConfirmLiveSaveLoad, then runs the read-only runtime diagnostics. No resume, rebind,
stage reset, or settings fallback is sent.

Live requires -ConfirmLiveSaveLoad, accepting native offline progress, Steam Cloud and
achievements. All fixtures are inspected before preparation. A supported backend owns
native loading, fresh-owner reset, local-file/settings snapshots and verified restore.
The client never restores files itself or replays an unknown start/load. Fixtures run
sequentially in one client-generated, durably reported session, restored in finally.
Restore mode explicitly recovers the runtime's existing session.
Manifest v1 entries use scenario allocation/early/fallback, plan (default 24hr), and
stage (-1 natural, or 0..1023). Legacy prose scenarios become allocation descriptions.
Inspect and Live default to test-fixtures.example.json. Tied runtime saves require
manual review. The standalone inspector is built only for nonempty manifests, with
restore/intermediate/output isolated in the report directory. Runtime capture metadata
remains backend-decoded evidence. This runner never invokes offline verification.
Playtime comparisons use saved clocks, never file dates or session age.

Only commands/*.json (atomically published) and a new report directory are written by
the harness itself, apart from standalone inspector build artifacts in that report.
Persistent backend claims/results are never removed or replayed. An
unacknowledged command has UNKNOWN outcome, even if a timeout occurred. Reports retain
every phase, request, result and error. Exit codes: 0 selected mode passed, 1 failure,
2 blocked/incomplete. PASS applies only to the selected game mode, not offline verification.
FunctionsOnly exports helpers for isolated tests without running checks or live actions.
.EXAMPLE
pwsh -NoProfile -File ./test.ps1
.EXAMPLE
pwsh -NoProfile -File ./test.ps1 -Mode Inspect -FixtureManifestPath ./fixtures.json
.EXAMPLE
pwsh -NoProfile -File ./test.ps1 -Mode Prepare -ConfirmLiveSaveLoad
#>
[CmdletBinding()]
param(
    [ValidateSet('Diagnostics', 'Inspect', 'Prepare', 'Live', 'Restore')][string]$Mode = 'Diagnostics',
    [string]$RuntimeDir = (Join-Path $env:USERPROFILE 'Desktop/NGUInjector'),
    [string]$FixtureManifestPath,
    [string]$ReportParent = (Join-Path $env:LOCALAPPDATA 'Temp/opencode'),
    [switch]$ConfirmLiveSaveLoad,
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
        # Windows readers/AV can briefly deny rename even with an atomic writer.
        for ($attempt = 0; ; $attempt++) {
            try { [IO.File]::Move($temporary, $Path, $true); break }
            catch [IO.IOException] { if ($attempt -ge 20) { throw }; Start-Sleep -Milliseconds 10 }
            catch [UnauthorizedAccessException] { if ($attempt -ge 20) { throw }; Start-Sleep -Milliseconds 10 }
        }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Save-TestAllReport($Context, [switch]$Recovery) {
    try { Write-TestAllJson (Join-Path $Context.ReportDir 'report.json') $Context.Report }
    catch {
        if (-not $Recovery) { throw }
        # Reporting failure must not prevent the dedicated, idempotent restore.
        $Context.Report.restoration.reportingError = $_.Exception.Message
    }
}

function Stop-TestAllBlocked([string]$Message) {
    $exception = [InvalidOperationException]::new($Message)
    $exception.Data['TestAllBlocked'] = $true
    throw $exception
}

function Invoke-TestAllPhase($Context, [string]$Name, [scriptblock]$Action, [switch]$Recovery) {
    $phase = [ordered]@{ name = $Name; status = 'RUNNING'; startedAt = [datetimeoffset]::UtcNow.ToString('o'); completedAt = $null; evidence = $null; error = $null }
    $Context.Report.phases.Add($phase)
    Save-TestAllReport $Context -Recovery:$Recovery
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
        Save-TestAllReport $Context -Recovery:$Recovery
    }
}

function Get-TestAllTimestamp($Value) {
    $parsed = [datetimeoffset]::MinValue
    if ($Value -isnot [string] -or -not [datetimeoffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) { throw 'Missing or invalid timestamp.' }
    return $parsed
}

function Assert-TestAllRuntime($Runtime, [string]$Session, [switch]$AllowTransition) {
    if ($Runtime -isnot [Collections.IDictionary]) { throw 'Runtime must be an object.' }
    $updated = Get-TestAllTimestamp $Runtime.updatedAt
    $started = Get-TestAllTimestamp $Runtime.startedAt
    $age = ([datetimeoffset]::UtcNow - $updated).TotalSeconds
    if ($age -lt -1 -or $age -gt 5) { throw "Runtime heartbeat is not fresh (age $age seconds; limit 5)." }
    if ($started -gt $updated.AddSeconds(1)) { throw 'Runtime session is in the future.' }
    if ($Session -and $Runtime.startedAt -cne $Session) { throw 'Runtime session changed; no command may be replayed.' }
    if (-not $AllowTransition -and ($Runtime.status -cne 'running' -or $Runtime.execution.lifecycle -cne 'Running')) { throw 'Runtime and execution must both be Running; no automatic resume is permitted.' }
    if (($Runtime.runtimeRevision -isnot [long] -and $Runtime.runtimeRevision -isnot [int]) -or
        $Runtime.runtimeRevision -lt 0 -or $Runtime.runtimeRevision -gt 9007199254740991L) { throw 'Invalid runtime revision.' }
}

function Read-TestAllRuntime($Context, [switch]$AllowTransition) {
    $runtime = Read-TestAllJson (Join-Path $Context.RuntimeDir 'runtime.json')
    Assert-TestAllRuntime $runtime $Context.Session -AllowTransition:$AllowTransition
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

function Invoke-TestAllCommand($Context, [ValidateSet('captureLocalSave', 'scanSaves', 'loadLongestSave', 'startSaveTest', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')][string]$Command, [hashtable]$Arguments = @{}) {
    if ($Context.CommandTimeoutSeconds -lt 1 -or $Context.CommandTimeoutSeconds -gt 120) { throw 'Command lifetime must be between 1 and 120 seconds.' }
    $saveTest = $Command -in @('startSaveTest', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')
    $recovering = $Command -eq 'restoreSaveTest'
    if ($Command -eq 'restoreSaveTest') {
        # A stale/quiesced recovery adapter must not prevent a guarded restore attempt.
        $runtime = Read-TestAllJson (Join-Path $Context.RuntimeDir 'runtime.json')
        if (-not $Context.Session -or $runtime.startedAt -cne $Context.Session) { throw 'Runtime session changed; refusing restore in a different transport session.' }
    } else { $runtime = Read-TestAllRuntime $Context }
    if (-not $Context.Session) { $Context.Session = $runtime.startedAt }
    if ($runtime.supportedCommands -isnot [array] -or $Command -cnotin $runtime.supportedCommands) { throw "Backend does not advertise $Command." }
    if ($Command -eq 'loadLongestSave') {
        if (-not $Context.ConfirmLiveSaveLoad) { Stop-TestAllBlocked 'Longest-playtime loading requires -ConfirmLiveSaveLoad.' }
        if ($runtime.saveSafety.ambiguousLongest -isnot [bool] -or $runtime.saveSafety.ambiguousLongest) { Stop-TestAllBlocked 'Longest-playtime save is ambiguous or ambiguity is unknown; manual review required. No branch was selected.' }
        if ($Arguments.Count -ne 2 -or $Arguments.expectedStartedAt -cne $Context.Session -or
            $Arguments.sha256 -cne $runtime.saveSafety.longest.sha256) { throw 'Longest-save request must match the current catalog and session.' }
    } elseif ($saveTest) {
        if ($Arguments.expectedStartedAt -cne $Context.Session -or $Arguments.sessionId -cne $Context.TestSessionId -or
            $Arguments.sessionId -cnotmatch '\A[0-9a-f]{32}\z') { throw 'Save-test command requires the pinned transport and client session ID.' }
        $keys = switch ($Command) {
            'startSaveTest' { @('expectedStartedAt', 'sessionId', 'acceptNativeSideEffects', 'fixture') }
            'loadSaveTestFixture' { @('expectedStartedAt', 'sessionId', 'expectedFixtureGeneration', 'fixture') }
            'setSaveTestStage' { @('expectedStartedAt', 'sessionId', 'expectedFixtureGeneration', 'fixtureSha256') }
            'restoreSaveTest' { @('expectedStartedAt', 'sessionId') }
        }
        if ($Arguments.Count -ne $keys.Count -or @($keys | Where-Object { -not $Arguments.ContainsKey($_) }).Count) { throw 'Unexpected save-test arguments.' }
        if ($Command -eq 'startSaveTest' -and (-not $Context.ConfirmLiveSaveLoad -or $Arguments.acceptNativeSideEffects -isnot [bool] -or -not $Arguments.acceptNativeSideEffects)) { Stop-TestAllBlocked 'Live testing requires -ConfirmLiveSaveLoad and native side-effect acceptance.' }
        if ($Command -in @('loadSaveTestFixture', 'setSaveTestStage')) {
            Assert-TestAllFixtureFence $Context $runtime $Context.ActiveFixture $Context.FixtureGeneration
            if ($Arguments.expectedFixtureGeneration -isnot [string] -or $Arguments.expectedFixtureGeneration -cne $Context.FixtureGeneration) { throw 'Fixture generation changed before dispatch.' }
            if ($Command -eq 'setSaveTestStage' -and $Arguments.fixtureSha256 -cne $Context.ActiveFixture.sha256) { throw 'Fixture hash changed before stage dispatch.' }
        }
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
    Save-TestAllReport $Context -Recovery:$recovering
    $temporary = $path + '.tmp'
    try {
        if ([IO.File]::Exists($path) -or [IO.File]::Exists($resultPath) -or [IO.File]::Exists($entry.claimPath)) { throw 'Unique command ID collision; no command published.' }
        [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $request -Depth 10), [Text.UTF8Encoding]::new($false))
        # Persist uncertainty BEFORE publication; a crash must never imply permission to retry.
        $entry.outcome = 'UNKNOWN'
        Save-TestAllReport $Context -Recovery:$recovering
        [IO.File]::Move($temporary, $path)
        $clock = [Diagnostics.Stopwatch]::StartNew()
        do {
            # Acknowledgements may describe pendingRestart/starting, or failed startup.
            # Read the durable result BEFORE consulting owner health.
            if ([IO.File]::Exists($resultPath)) {
                $result = Read-TestAllJson $resultPath
                $entry.result = $result
                if ($result.id -cne $id -or $result.command -cne $Command -or $result.success -isnot [bool]) { throw 'Result ID/command/success contract mismatch; outcome UNKNOWN.' }
                $completed = Get-TestAllTimestamp $result.completedAt
                if ($completed -lt $created.AddSeconds(-1) -or $completed -gt [datetimeoffset]::UtcNow.AddSeconds(1) -or
                    $completed -gt $created.AddSeconds($Context.CommandTimeoutSeconds)) { throw 'Result completion timestamp is outside this request lifetime; outcome UNKNOWN.' }
                Assert-TestAllRuntime $result.data.runtime $Context.Session -AllowTransition
                if (-not $result.success) { $entry.outcome = 'FAILED'; throw "Backend rejected ${Command}: $($result.message)" }
                if ($saveTest) {
                    Assert-TestAllSession $result.data.saveTestSession $Context.TestSessionId
                    Assert-TestAllSession $result.data.runtime.saveTestSession $Context.TestSessionId
                    if ($Command -ne 'restoreSaveTest') {
                        $expectedGeneration = if ($Command -eq 'startSaveTest') { '1' } elseif ($Command -eq 'loadSaveTestFixture') {
                            ([long]$Arguments.expectedFixtureGeneration + 1).ToString([Globalization.CultureInfo]::InvariantCulture)
                        } else { $Arguments.expectedFixtureGeneration }
                        $hash = if ($Command -eq 'setSaveTestStage') { $Arguments.fixtureSha256 } else { $Arguments.fixture.sha256 }
                        foreach ($session in @($result.data.saveTestSession, $result.data.runtime.saveTestSession)) {
                            if ($session.fixtureGeneration -cne $expectedGeneration -or $session.fixture.sha256 -cne $hash) { throw 'Result fixture generation/hash mismatch; outcome UNKNOWN.' }
                        }
                    }
                }
                $entry.outcome = 'SUCCEEDED'
                return $result
            }
            $null = Read-TestAllRuntime $Context -AllowTransition
            if ($clock.Elapsed.TotalSeconds -ge $Context.CommandTimeoutSeconds) { throw "Timed out waiting for $Command; outcome UNKNOWN. Do not replay; inspect persistent claims/results." }
            Start-Sleep -Milliseconds $Context.PollMilliseconds
        } while ($true)
    } catch {
        $entry.error = $_.Exception.Message
        throw
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        Save-TestAllReport $Context -Recovery:$recovering
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
    $Context.Report.originalBaselineAfterRecovery = $runtime.saveSafety.current
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
        if ($fixture.name.Length -gt 128) { throw 'Fixture name exceeds 128 characters.' }
        $scenario = $fixture.scenario; $description = $fixture.description
        if (-not $scenario) { $scenario = 'allocation' }
        if ($scenario -cnotin @('allocation', 'early', 'fallback')) {
            if ($scenario -isnot [string] -or $scenario -notmatch '\s') { throw 'Fixture scenario must be allocation, early, or fallback.' }
            if (-not $description) { $description = $scenario }
            $scenario = 'allocation'
        }
        $plan = if ($fixture.Contains('plan')) { $fixture.plan } else { '24hr' }
        if ($plan -isnot [string] -or $plan.Length -gt 128 -or $plan -cnotmatch '\A[a-zA-Z0-9_.-]+\z' -or $plan -in @('.', '..')) { throw 'Fixture plan must be a nonempty safe backend plan name.' }
        $stage = -1
        if ($fixture.Contains('stage')) { $stage = $fixture.stage }
        if ($fixture.Contains('stageIndex')) {
            if ($fixture.Contains('stage') -and $fixture.stage -cne $fixture.stageIndex) { throw 'Fixture stage and stageIndex disagree.' }
            $stage = $fixture.stageIndex
        }
        if (($stage -isnot [int] -and $stage -isnot [long]) -or $stage -lt -1 -or $stage -gt 1023) { throw 'Fixture stage must be an integer -1 or 0..1023.' }
        @{ name = $fixture.name; path = $file; scenario = $scenario; description = $description; plan = $plan; stageIndex = [int]$stage
            sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
            status = 'CONFIGURED_NOT_EXECUTED'; inspected = $false; decoded = $false; executed = $false; metadata = $null
            limitation = 'Awaiting read-only SaveInspector decode.'; samples = [Collections.Generic.List[object]]::new(); assertions = @(); native = $null; reset = $null; restoration = $null }
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
    $intermediate = (Join-Path $Context.ReportDir 'save-inspector-obj').Replace('\', '/') + '/'
    Invoke-TestAllNative $Context 'inspector.build' 'dotnet' @('build', (Join-Path $Repo 'checks/SaveSafety/SaveInspector.csproj'), '--configuration', 'Release', '--nologo',
        "-p:BaseIntermediateOutputPath=$intermediate", "-p:MSBuildProjectExtensionsPath=$intermediate", "-p:IntermediateOutputPath=${intermediate}Release/", '--output', $output)
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
            $fixture.limitation = 'Inspected only; live execution requires explicit opt-in and the backend save-test capability.'
            Save-TestAllReport $Context
        }
        return $Context.Report.fixtures
    }
}

function Assert-TestAllSession($Session, [string]$Id) {
    if ($Session -isnot [Collections.IDictionary] -or ($Session.version -isnot [int] -and $Session.version -isnot [long]) -or $Session.version -ne 1 -or $Session.supported -isnot [bool] -or -not $Session.supported -or
        $Session.phase -cnotin @('idle', 'starting', 'loadingFixture', 'running', 'restoring', 'restored', 'recoveryRequired') -or
        $Session.fixtureGeneration -isnot [string] -or $Session.fixtureGeneration -cnotmatch '\A(?:0|[1-9][0-9]*)\z' -or
        [bigint]$Session.fixtureGeneration -gt [long]::MaxValue) { throw 'Missing or invalid version-1 saveTestSession contract.' }
    if ($Id -and $Session.sessionId -cne $Id) { throw 'Save-test session changed; no fixture operation may be replayed.' }
}

function Assert-TestAllLiveCapability($Context) {
    $runtime = Read-TestAllRuntime $Context -AllowTransition
    $missing = @('startSaveTest', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest' | Where-Object { $_ -cnotin $runtime.supportedCommands })
    if ($missing.Count -or $runtime.saveTestSession.version -ne 1 -or $runtime.saveTestSession.supported -isnot [bool] -or -not $runtime.saveTestSession.supported) {
        $message = "Backend save-test capability unsupported: require runtime.saveTestSession version=1 supported=true and commands $($missing -join ', ')."
        $Context.Report.liveFixtureBlocker = $message
        Stop-TestAllBlocked $message
    }
    try { Assert-TestAllSession $runtime.saveTestSession }
    catch { Stop-TestAllBlocked ('Backend save-test capability unsupported: ' + $_.Exception.Message) }
    if ($runtime.saveTestSession.phase -cnotin @('idle', 'restored')) { Stop-TestAllBlocked 'A save-test session is already active; use -Mode Restore before starting another.' }
    return $runtime
}

function Assert-TestAllProgress($Target, $Observed) {
    Assert-TestAllSave $Target
    Assert-TestAllSave $Observed
    if ($Target.playerName -cne $Observed.playerName -or $Target.rebirth -cne $Observed.rebirth -or $Observed.version -lt $Target.version) { throw 'Native observed identity does not match the protected player/rebirth/version.' }
    foreach ($clock in @('totalPlaytimeSeconds', 'rebirthSeconds')) {
        if ((Get-TestAllClock $Observed[$clock]) + 0.001 -lt (Get-TestAllClock $Target[$clock])) { throw "Native observed $clock is behind restoreTarget/fixture; restoration is not verified." }
    }
    $totalGain = (Get-TestAllClock $Observed.totalPlaytimeSeconds) - (Get-TestAllClock $Target.totalPlaytimeSeconds)
    $rebirthGain = (Get-TestAllClock $Observed.rebirthSeconds) - (Get-TestAllClock $Target.rebirthSeconds)
    if ([Math]::Abs($totalGain - $rebirthGain) -gt 5) { throw 'Native observed clocks do not describe the same progression epoch.' }
}

function Assert-TestAllFixtureFence($Context, $Runtime, $Fixture, [string]$Generation) {
    Assert-TestAllRuntime $Runtime $Context.Session
    $session = $Runtime.saveTestSession
    Assert-TestAllSession $session $Context.TestSessionId
    if ($session.phase -cne 'running' -or $session.fixtureGeneration -cne $Generation -or $session.resetEvidence.verified -isnot [bool] -or -not $session.resetEvidence.verified) { throw 'Fixture phase/generation/reset fence is not verified running.' }
    foreach ($key in @('name', 'path', 'sha256', 'plan', 'stageIndex', 'scenario')) {
        if ($session.fixture[$key] -cne $Fixture[$key]) { throw "Fixture $key changed inside the pinned generation." }
    }
}

function Wait-TestAllFixture($Context, $Fixture, [string]$Generation) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $runtime = Read-TestAllRuntime $Context -AllowTransition
        $session = $runtime.saveTestSession
        Assert-TestAllSession $session
        if ($session.sessionId -ceq $Context.TestSessionId) {
            if ($session.phase -ceq 'recoveryRequired') { throw "Backend fixture recovery required: $($session.error)" }
            if ([bigint]$session.fixtureGeneration -gt [bigint]$Generation) { throw 'Fixture generation advanced unexpectedly.' }
            if ($session.fixtureGeneration -ceq $Generation -and $session.phase -ceq 'running' -and $runtime.status -ceq 'running' -and
                $runtime.execution.lifecycle -ceq 'Running' -and $session.resetEvidence.verified -is [bool] -and $session.resetEvidence.verified) {
                Assert-TestAllFixtureFence $Context $runtime $Fixture $Generation
                Assert-TestAllProgress $Fixture.metadata $session.observed
                Assert-TestAllProgress $Context.Report.originalBaselineAfterRecovery $session.restoreTarget
                if ($Context.Report.restoreTarget -and $Context.Report.restoreTarget.sha256 -cne $session.restoreTarget.sha256) { throw 'Protected restoreTarget changed between fixtures.' }
                $Context.Report.restoreTarget = $session.restoreTarget
                if ($session.offlineProgress.mode -cne 'native') { throw 'Backend did not retain native offline progress.' }
                $null = Get-TestAllClock $session.offlineProgress.totalGainSeconds
                $Fixture.native = @{ observed = $session.observed; offlineProgress = $session.offlineProgress; restoreTarget = $session.restoreTarget }
                $Fixture.reset = $session.resetEvidence
                $Fixture.executed = $true
                $Fixture.limitation = 'Native offline progress, Steam Cloud and achievements are accepted, not rolled back. Independent current-cap ratio math remains inconclusive.'
                $Context.Report.liveFixturesExecuted++
                return $runtime
            }
        } elseif ($session.phase -cnotin @('idle', 'restored')) { throw 'Save-test session changed during fixture transition.' }
        if ($clock.Elapsed.TotalSeconds -ge $Context.TransitionTimeoutSeconds) { throw "Fixture startup/reset timed out (phase $($session.phase), generation $($session.fixtureGeneration)). No load will be replayed." }
        Start-Sleep -Milliseconds $Context.PollMilliseconds
    } while ($true)
}

function Get-TestAllFixtureAssertions($Runtime, $Fixture, $Diagnostics) {
    $checks = [Collections.Generic.List[object]]::new()
    function Add-LiveCheck($Id, $Status, $Evidence, [bool]$Required = $true) {
        $checks.Add(@{ id = $Id; status = $Status; required = $Required; evidence = $Evidence })
    }
    # saveSafety is intentionally alarmed for older fixtures. The caller fences BOTH
    # ends of diagnostics to this exact verified session/generation before excluding it.
    $earlySupportLimited = $Fixture.scenario -ceq 'early' -and ($Runtime.execution.domains.Support.controlled -ceq $false -or
        $Runtime.execution.support.desired -in @('|', 'uncontrolled') -or $Runtime.execution.support.reason -match 'lock|unavailable|unlock')
    foreach ($check in $Diagnostics.checks) {
        if ($check.id -in @('saveSafety', 'saveSafety.evidence', 'idle.Energy', 'idle.Magic')) { continue }
        $status = $check.status
        if ($earlySupportLimited -and $check.id -like 'support*' -and $status -in @('UNCONTROLLED', 'WAITING')) { $status = 'INCONCLUSIVE' }
        Add-LiveCheck ('diagnostics.' + $check.id) $status $check.evidence ([bool]$check.required)
    }
    $execution = $Runtime.execution; $domains = $execution.domains
    $activeStage = @($Runtime.planProgress.stages | Where-Object index -EQ $Runtime.planProgress.activeStageIndex)
    $stageOk = $Runtime.planProgress.available -is [bool] -and $Runtime.planProgress.available -and $Runtime.planProgress.status -ceq 'running' -and
        $Runtime.planProgress.planName -ceq $Fixture.plan -and $Runtime.settings.customPlanFile -ceq $Fixture.plan -and
        ($Fixture.stageIndex -eq -1 -or $Runtime.planProgress.activeStageIndex -eq $Fixture.stageIndex)
    Add-LiveCheck 'stage' $(if ($stageOk) { 'PASS' } else { 'FAIL' }) $Runtime.planProgress
    $laneTargets = @{
        Energy = @('EnergyNgu', 'AdvancedTraining', 'Augment', 'AugmentUpgrade', 'TimeMachineEnergy', 'WandoosEnergy', 'WishEnergy', 'BasicAttack', 'BasicDefense')
        Magic = @('MagicNgu', 'TimeMachineMagic', 'WandoosMagic', 'BloodRitual', 'WishMagic')
    }
    foreach ($lane in @('Energy', 'Magic')) {
        $rows = @($execution.resources | Where-Object target -CIn $laneTargets[$lane])
        $valid = $rows.Count -gt 0 -and $domains[$lane].controlled -is [bool] -and $domains[$lane].controlled -and $domains[$lane].state -ceq 'Observed'
        $spent = [bigint]0; $nguSpent = [bigint]0; $fallbackSpent = [bigint]0; $nguWanted = [bigint]0
        foreach ($row in $rows) {
            if ($row.verified -isnot [bool] -or -not $row.verified -or $row.desired -isnot [array] -or $row.observed -isnot [array] -or
                $row.observed.Count -eq 0 -or $row.desired.Count -ne $row.observed.Count) { $valid = $false; continue }
            for ($i = 0; $i -lt $row.observed.Count; $i++) {
                if ($row.desired[$i] -isnot [string] -or $row.observed[$i] -isnot [string] -or $row.observed[$i] -cnotmatch '\A(?:0|[1-9][0-9]*)\z' -or
                    [bigint]$row.observed[$i] -gt [long]::MaxValue -or $row.desired[$i] -cne $row.observed[$i]) { $valid = $false; continue }
                $amount = [bigint]$row.observed[$i]; $spent += $amount
                if ($row.target -ceq "${lane}Ngu") { $nguSpent += $amount; $nguWanted += [bigint]$row.desired[$i] }
                elseif ($row.target -cnotin @('BasicAttack', 'BasicDefense')) { $fallbackSpent += $amount }
            }
        }
        $overlay = $Runtime.manualAdventureOverlay; $key = $lane.ToLowerInvariant()
        $total = $overlay[$key]; $idle = $overlay["idle$lane"]
        $actualValid = $overlay.available -is [bool] -and $overlay.available -and $total -is [string] -and $total -cmatch '\A(?:0|[1-9][0-9]*)\z' -and
            $idle -is [string] -and $idle -cmatch '\A(?:0|[1-9][0-9]*)\z' -and [bigint]$idle -le [bigint]$total -and [bigint]$total -le [long]::MaxValue
        Add-LiveCheck "actual$lane" $(if ($valid -and $actualValid) { 'PASS' } else { 'FAIL' }) @{ total = $total; idle = $idle; resources = $rows; actualSpending = "$spent" }
        $priorities = @($activeStage | ForEach-Object { $_["$($key)Priorities"] })
        $focus = @($activeStage | ForEach-Object { $_["$($key)Focus"] })
        $nguExpected = $nguWanted -gt 0 -or @($priorities | Where-Object { $_ -match '^NGU(?:-|$)' }).Count -gt 0 -or
            ($lane -eq 'Energy' -and 5 -in $focus) -or ($lane -eq 'Magic' -and 6 -in $focus)
        $nguRows = $Runtime.nguAllocation["${key}Targets"]
        $nguActual = [bigint]0
        foreach ($row in $nguRows) {
            if ($row.allocated -is [string] -and $row.allocated -cmatch '\A(?:0|[1-9][0-9]*)\z') { $nguActual += [bigint]$row.allocated }
        }
        if ($Fixture.scenario -ceq 'fallback') {
            Add-LiveCheck "fallback.$lane" $(if ($Fixture.reset.fallbackTargetsCompleted -ceq $true -and $nguActual -eq 0 -and $nguSpent -eq 0 -and $fallbackSpent -gt 0) { 'PASS' } else { 'FAIL' }) @{ actualNgu = "$nguActual"; nonbasicNonNgu = "$fallbackSpent"; resources = $rows; priorities = $priorities }
        } elseif ($Fixture.scenario -ceq 'allocation' -and $nguExpected) {
            Add-LiveCheck "fundedNgu.$lane" $(if ($nguActual -gt 0 -and $nguSpent -gt 0) { 'PASS' } else { 'FAIL' }) @{ actualNgu = "$nguActual"; desiredNgu = "$nguWanted" }
        }
        $idleStatus = 'PASS'; $reason = 'No idle resource.'
        if ($actualValid -and [bigint]$idle -gt 0) {
            $idleStatus = 'INCONCLUSIVE'
            $reason = 'Positive idle is reported, not silently treated as zero. Verified desired rows are satisfied; independent current caps/targets are not published.'
            if (-not $valid) { $idleStatus = 'FAIL'; $reason = 'Unexplained managed idle with unverified desired/observed rows after stable observations.' }
            elseif ($spent -eq 0) { $idleStatus = 'INCONCLUSIVE'; $reason = 'All desired rows are zero. Current cap/target exhaustion cannot be independently established.' }
        }
        Add-LiveCheck "idle.$lane" $idleStatus @{ idle = $idle; reason = $reason; waitingReason = $domains[$lane].waitingReason }
    }
    $gear = @($execution.gear)
    $gearOk = $domains.Gear.controlled -ceq $true -and @($gear | Where-Object { $_.desired -is [int] -or $_.desired -is [long] } | Where-Object desired -GT 0).Count -gt 0 -and
        @($gear | Where-Object { $_.verified -isnot [bool] -or -not $_.verified -or $null -eq $_.desired -or $_.desired -cne $_.observed }).Count -eq 0
    Add-LiveCheck 'loadout.nonemptyVerified' $(if ($gearOk) { 'PASS' } else { 'FAIL' }) $gear
    $support = $execution.support
    $supportOk = $domains.Support.controlled -ceq $true -and $support.verified -is [bool] -and $support.verified -and
        $support.desiredFeasible -is [bool] -and $support.desiredFeasible -and $support.desired -ceq $support.observed -and
        $support.desired -is [string] -and $support.desired -cmatch '\A[0-6](?:,[0-6])*\|(?:[0-9]|10|11):[1-9][0-9]*(?:,(?:[0-9]|10|11):[1-9][0-9]*)*\z'
    $supportStatus = if ($supportOk) { 'PASS' } elseif ($Fixture.scenario -ceq 'early' -and ($domains.Support.controlled -ceq $false -or $support.desired -in @('|', 'uncontrolled') -or $support.reason -match 'lock|unavailable|unlock')) { 'INCONCLUSIVE' } else { 'FAIL' }
    Add-LiveCheck 'support.nonemptyFeasibleVerified' $supportStatus @{ support = $support; reason = 'Both beards and diggers are required; locked early support is inconclusive, never a fake pass.' }
    Add-LiveCheck 'independentCurrentCaps' 'INCONCLUSIVE' 'No authoritative current per-target caps/budget: exact full-ratio math is not proven by historical receipts.' $false
    return $checks.ToArray()
}

function Get-TestAllSourceOrder($Runtime) {
    return ConvertTo-Json -InputObject @($Runtime.planProgress.stages | ForEach-Object {
        [ordered]@{ index = $_.index; energy = $_.configuredEnergyPriorities; magic = $_.configuredMagicPriorities }
    }) -Compress -Depth 20
}

function Wait-TestAllFixtureSettle($Context, $Fixture) {
    $clock = [Diagnostics.Stopwatch]::StartNew(); $stable = 0; $previous = $null; $revision = $null
    do {
        $runtime = Read-TestAllRuntime $Context
        Assert-TestAllFixtureFence $Context $runtime $Fixture $Context.FixtureGeneration
        $arguments = @{ RuntimeDir = $Context.RuntimeDir; Samples = 1; IntervalSeconds = 0; ExpectedPlan = $Fixture.plan }
        $script = Join-Path $PSScriptRoot 'diagnostics.ps1'
        if ((Get-Command $script).Parameters.ContainsKey('ExpectedTestSessionId')) { $arguments.ExpectedTestSessionId = $Context.TestSessionId }
        for ($attempt = 0; ; $attempt++) {
            try { $diagnostics = & $script @arguments; break }
            catch {
                # Retry only a read racing Windows atomic publication, never commands.
                if ($_.Exception.GetBaseException() -isnot [IO.IOException] -or $attempt -ge 5) { throw }
                Start-Sleep -Milliseconds 20
            }
        }
        $after = Read-TestAllRuntime $Context
        Assert-TestAllFixtureFence $Context $after $Fixture $Context.FixtureGeneration
        if (@($diagnostics.samples | Where-Object { (Get-TestAllTimestamp $_.observation.session) -ne (Get-TestAllTimestamp $Context.Session) }).Count) { throw 'Diagnostics crossed the pinned transport session.' }
        $sourceOrder = Get-TestAllSourceOrder $runtime
        if ($sourceOrder -cne $Fixture.sourceOrder) { throw 'Configured source priority order changed during fixture reconciliation.' }
        $checks = @(Get-TestAllFixtureAssertions $runtime $Fixture $diagnostics)
        $signature = ConvertTo-Json -InputObject @($checks | Where-Object required | ForEach-Object { "$($_.id):$($_.status)" }) -Compress
        if ($null -ne $revision -and $runtime.runtimeRevision -le $revision) { $stable = 0 }
        elseif ($signature -ceq $previous) { $stable++ } else { $stable = 1 }
        $previous = $signature; $revision = $runtime.runtimeRevision
        $issues = @($checks | Where-Object { $_.required -and $_.status -cnotin @('PASS', 'UNCONTROLLED') })
        $status = if (@($issues | Where-Object status -In @('FAIL', 'WAITING')).Count) { 'FAIL' } elseif ($issues.Count) { 'INCONCLUSIVE' } else { 'PASS' }
        $timedOut = $clock.Elapsed.TotalSeconds -ge $Context.SettleTimeoutSeconds
        if ($timedOut -and $status -eq 'PASS' -and $stable -lt $Context.StableSamples) {
            $checks += @{ id = 'settlement.progress'; status = 'INCONCLUSIVE'; required = $true
                evidence = "Timeout without $($Context.StableSamples) stable, progressing native observations; observed $stable." }
            $status = 'INCONCLUSIVE'
        }
        $sample = @{ capturedAt = [datetimeoffset]::UtcNow.ToString('o'); runtime = $runtime; diagnostics = $diagnostics; assertions = $checks; sourceOrder = $sourceOrder
            elapsedSeconds = $clock.Elapsed.TotalSeconds; stableSamples = $stable; status = $status; timedOut = $timedOut }
        $Fixture.samples.Add($sample)
        $Fixture.assertions = $checks
        $Fixture.status = $status
        Save-TestAllReport $Context
        # FightOnEntry and the allocation tick may take longer than three samples.
        # Only verified success can finish early; at timeout retain the actual issues.
        if (($status -eq 'PASS' -and $stable -ge $Context.StableSamples) -or $timedOut) { return $sample }
        Start-Sleep -Milliseconds $Context.SettleMilliseconds
    } while ($true)
}

function Invoke-TestAllRestore($Context) {
    if (-not $Context.Report.restoration.required -or $Context.Report.restoration.attempted) { return }
    $restore = $Context.Report.restoration
    $restore.attempted = $true; $restore.status = 'RESTORING'
    Save-TestAllReport $Context -Recovery
    try {
        try {
            $before = Read-TestAllRuntime $Context -AllowTransition
            $restore.before = $before.saveTestSession
            if ($before.saveTestSession.sessionId -ceq $Context.TestSessionId) {
                foreach ($fixture in $Context.Report.fixtures) {
                    if ($fixture.name -ceq $before.saveTestSession.fixture.name -and $fixture.sha256 -ceq $before.saveTestSession.fixture.sha256) {
                        $fixture.native = @{ observed = $before.saveTestSession.observed; offlineProgress = $before.saveTestSession.offlineProgress; restoreTarget = $before.saveTestSession.restoreTarget }
                        $fixture.reset = $before.saveTestSession.resetEvidence
                    }
                }
            }
        } catch { $restore.beforeReadError = $_.Exception.Message }
        try { $null = Invoke-TestAllCommand $Context 'restoreSaveTest' @{ expectedStartedAt = $Context.Session; sessionId = $Context.TestSessionId } }
        catch { $restore.acknowledgementError = $_.Exception.Message; Save-TestAllReport $Context -Recovery }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        do {
            $runtime = Read-TestAllRuntime $Context -AllowTransition
            $session = $runtime.saveTestSession
            Assert-TestAllSession $session $Context.TestSessionId
            $restore.backend = $session
            if ($session.phase -ceq 'restored') {
                foreach ($key in @('verified', 'nativeVerified', 'filesVerified')) {
                    if ($session.restoration[$key] -isnot [bool] -or -not $session.restoration[$key]) { throw "Backend restored phase lacks restoration.$key=true." }
                }
                if ($Context.Report.restoreTarget -and $Context.Report.restoreTarget.sha256 -cne $session.restoreTarget.sha256) { throw 'Restoration target changed.' }
                $cancelled = $session.restoration.cancelledBeforeStart -is [bool] -and $session.restoration.cancelledBeforeStart
                if (-not $cancelled -and $Context.Report.originalBaselineAfterRecovery) { Assert-TestAllProgress $Context.Report.originalBaselineAfterRecovery $session.restoreTarget }
                Assert-TestAllProgress $session.restoreTarget $session.observed
                Assert-TestAllProgress $session.restoreTarget $runtime.saveSafety.current
                $restore.restored = $true; $restore.status = 'RESTORED'
                $restore.cancelledBeforeStart = $cancelled
                $restore.reason = if ($cancelled) { 'start cancelled; no fixture applied. Backend verified current native progression and files; no rollback was performed.' }
                    else { 'Backend verified native progression and original local files/domain settings. Native offline gains, Steam Cloud/achievements and command claims/results are not rolled back.' }
                foreach ($fixture in $Context.Report.fixtures) { if ($fixture.executed) { $fixture.restoration = $session.restoration } }
                return $session
            }
            if ($session.phase -ceq 'recoveryRequired') { throw "Backend restore recovery required: $($session.error)" }
            if ($clock.Elapsed.TotalSeconds -ge $Context.TransitionTimeoutSeconds) { throw "Backend restoration timed out in phase $($session.phase)." }
            Start-Sleep -Milliseconds $Context.PollMilliseconds
        } while ($true)
    } catch {
        $restore.status = 'RECOVERY_REQUIRED'; $restore.error = $_.Exception.Message
        $restore.reason = 'Backend restoration could not be verified. Protected recovery remains required; see restorationErrors.'
        $Context.Report.restorationErrors.Add($_.Exception.Message)
        throw
    } finally { Save-TestAllReport $Context -Recovery }
}

function Invoke-TestAllLive($Context) {
    $Context.TestSessionId = [guid]::NewGuid().ToString('N')
    $Context.Report.testSessionId = $Context.TestSessionId
    # Durable client identity and restore obligation precede the FIRST native request.
    $Context.Report.restoration.required = $true
    $Context.Report.restoration.status = 'REQUIRED'
    $Context.Report.restoration.reason = 'The backend journal may become active. Restore is required even if the first acknowledgement is lost.'
    Save-TestAllReport $Context
    try {
        $generation = '0'; $first = $true
        foreach ($fixture in $Context.Report.fixtures) {
            try {
                $payload = @{}; foreach ($key in @('name', 'path', 'sha256', 'plan', 'stageIndex', 'scenario')) { $payload[$key] = $fixture[$key] }
                $command = if ($first) { 'startSaveTest' } else { 'loadSaveTestFixture' }
                $arguments = @{ expectedStartedAt = $Context.Session; sessionId = $Context.TestSessionId; fixture = $payload }
                if ($first) { $arguments.acceptNativeSideEffects = $true } else { $arguments.expectedFixtureGeneration = $generation }
                $null = Invoke-TestAllPhase $Context ("live.$($fixture.name).load") { Invoke-TestAllCommand $Context $command $arguments }
                $Context.ActiveFixture = $fixture
                $generation = ([long]$generation + 1).ToString([Globalization.CultureInfo]::InvariantCulture)
                $Context.FixtureGeneration = $generation
                $ready = Invoke-TestAllPhase $Context ("live.$($fixture.name).ready") { Wait-TestAllFixture $Context $fixture $generation }
                $fixture.sourceOrder = Get-TestAllSourceOrder $ready
                $null = Invoke-TestAllPhase $Context ("live.$($fixture.name).stage") {
                    Invoke-TestAllCommand $Context 'setSaveTestStage' @{ expectedStartedAt = $Context.Session; sessionId = $Context.TestSessionId; expectedFixtureGeneration = $generation; fixtureSha256 = $fixture.sha256 }
                }
                $null = Invoke-TestAllPhase $Context ("live.$($fixture.name).observe") { Wait-TestAllFixtureSettle $Context $fixture }
                if ($fixture.status -ne 'PASS') { $Context.Report.errors.Add(@{ phase = "live.$($fixture.name).assertions"; status = $fixture.status; message = 'Required fixture assertions did not all pass; see samples/assertions.' }) }
                $first = $false
            } catch {
                $fixture.status = 'FAIL'; $fixture.error = $_.Exception.Message
                # An uncertain mutation or broken fence ends the sequence, never replayed.
                throw
            }
        }
    } finally { $null = Invoke-TestAllPhase $Context 'live.restore' { Invoke-TestAllRestore $Context } -Recovery }
}

function Invoke-TestAllHarness(
    [ValidateSet('Diagnostics', 'Inspect', 'Prepare', 'Live', 'Restore')][string]$Mode = 'Diagnostics',
    [string]$RuntimeDir, [string]$FixtureManifestPath, [string]$ReportParent,
    [bool]$ConfirmLiveSaveLoad = $false
) {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off
    if (-not (Test-Path -LiteralPath $ReportParent -PathType Container)) { throw "Report parent must already exist: $ReportParent" }
    $parent = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($ReportParent))
    $runtimeDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RuntimeDir))
    $prefix = $runtimeDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($parent.Equals($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -or $parent.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'ReportParent must be outside RuntimeDir.' }
    $directory = Join-Path $parent ('ngu-test-' + [datetimeoffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($directory)
    $report = [ordered]@{
        schema = 'ngu.test.game'; version = 2; scope = 'game'; mode = $Mode; startedAt = [datetimeoffset]::UtcNow.ToString('o'); completedAt = $null
        status = 'RUNNING'; success = $false; exitCode = 1; reportDirectory = $directory
        preparationPassed = $false
        liveFixturesExecuted = 0; liveFixtureBlocker = $null; baseline = $null; originalBaselineAfterRecovery = $null; restoreTarget = $null; testSessionId = $null
        originalRuntime = $null; preparedRuntime = $null; recovery = $null; lastScan = $null; runtimeDiagnostics = $null
        fixtures = @(); phases = [Collections.Generic.List[object]]::new(); commands = [Collections.Generic.List[object]]::new(); errors = [Collections.Generic.List[object]]::new()
        restorationErrors = [Collections.Generic.List[string]]::new()
        restoration = @{ required = $false; attempted = $false; restored = $false; originalBackupVerified = $false; status = 'NOT_REQUIRED'; reason = 'No older fixture was loaded. No original restoration is claimed.' }
    }
    $context = @{ RuntimeDir = $runtimeDirectory; ReportDir = $directory; Report = $report; Session = $null
        ConfirmLiveSaveLoad = $ConfirmLiveSaveLoad; CommandTimeoutSeconds = 120; ScanTimeoutSeconds = 120; PollMilliseconds = 200
        TransitionTimeoutSeconds = 120; SettleTimeoutSeconds = 120; StableSamples = 3; SettleMilliseconds = 2000 }
    $runtimeDiagnostics = {
        $runtime = Read-TestAllRuntime $context
        if (-not $context.Session) { $context.Session = $runtime.startedAt; $report.originalRuntime = $runtime }
        $diagnostics = & (Join-Path $PSScriptRoot 'diagnostics.ps1') -RuntimeDir $runtimeDirectory
        $report.runtimeDiagnostics = $diagnostics
        $null = Read-TestAllRuntime $context
        if ($diagnostics.success -isnot [bool] -or -not $diagnostics.success) { throw 'Read-only runtime diagnostics did not pass; no automatic runtime changes were attempted.' }
        return $diagnostics
    }
    try {
        switch ($Mode) {
            'Diagnostics' {
                $null = Invoke-TestAllPhase $context 'diagnostics.runtimeDiagnostics' $runtimeDiagnostics
            }
            'Inspect' {
                if (-not $FixtureManifestPath) { $FixtureManifestPath = Join-Path $PSScriptRoot 'test-fixtures.example.json' }
                $report.fixtures = @(Invoke-TestAllPhase $context 'fixtures.configure' { Read-TestAllManifest $FixtureManifestPath })
                Invoke-TestAllFixtureInspection $context $PSScriptRoot
            }
            'Prepare' {
                $report.fixtures = @(Invoke-TestAllPhase $context 'fixtures.configure' { Read-TestAllManifest $FixtureManifestPath })
                Invoke-TestAllFixtureInspection $context $PSScriptRoot
                $null = Invoke-TestAllPrepare $context
                $null = Invoke-TestAllPhase $context 'prepare.runtimeDiagnostics' $runtimeDiagnostics
            }
            'Live' {
                if (-not $ConfirmLiveSaveLoad) { Stop-TestAllBlocked 'Live testing requires -ConfirmLiveSaveLoad (native offline progress, Steam Cloud and achievements are accepted).' }
                if (-not $FixtureManifestPath) { $FixtureManifestPath = Join-Path $PSScriptRoot 'test-fixtures.example.json' }
                $report.fixtures = @(Invoke-TestAllPhase $context 'fixtures.configure' { Read-TestAllManifest $FixtureManifestPath })
                Invoke-TestAllFixtureInspection $context $PSScriptRoot
                $null = Invoke-TestAllPhase $context 'live.capability' { Assert-TestAllLiveCapability $context }
                if ($report.fixtures.Count -eq 0) { Stop-TestAllBlocked 'Live requires at least one inspected fixture.' }
                $null = Invoke-TestAllPrepare $context
                Invoke-TestAllLive $context
            }
            'Restore' {
                $null = Invoke-TestAllPhase $context 'restore.existingSession' {
                    $runtime = Read-TestAllRuntime $context -AllowTransition
                    Assert-TestAllSession $runtime.saveTestSession
                    $id = $runtime.saveTestSession.sessionId
                    if ($id -isnot [string] -or $id -cnotmatch '\A[0-9a-f]{32}\z') { Stop-TestAllBlocked 'No known backend save-test session is available to restore.' }
                    $context.Session = $runtime.startedAt; $context.TestSessionId = $id; $report.testSessionId = $id
                    $report.restoration.required = $true
                    Save-TestAllReport $context
                }
            }
        }
    } catch {
        if ($report.errors.Count -eq 0) { $report.errors.Add(@{ phase = $Mode; status = $(if ($_.Exception.Data['TestAllBlocked']) { 'BLOCKED' } else { 'FAIL' }); message = $_.Exception.Message }) }
    } finally {
        # Also covers a lost FIRST acknowledgement or an interrupted live sequence.
        try { if ($report.restoration.required -and -not $report.restoration.attempted) { $null = Invoke-TestAllPhase $context 'finally.restore' { Invoke-TestAllRestore $context } -Recovery } }
        catch { }
        $hasFailures = @($report.errors | Where-Object status -EQ 'FAIL').Count -gt 0
        $blocked = @($report.errors | Where-Object status -In @('BLOCKED', 'INCONCLUSIVE')).Count -gt 0
        $report.exitCode = if ($hasFailures) { 1 } elseif ($blocked) { 2 } else { 0 }
        $report.status = if ($hasFailures) { 'FAIL' } elseif ($blocked) { 'BLOCKED' } else { 'PASS' }
        $report.success = $report.exitCode -eq 0
        $report.completedAt = [datetimeoffset]::UtcNow.ToString('o')
        Save-TestAllReport $context
    }
    return $report
}

if ($FunctionsOnly) { return }
try {
    $result = Invoke-TestAllHarness -Mode $Mode -RuntimeDir $RuntimeDir -FixtureManifestPath $FixtureManifestPath -ReportParent $ReportParent -ConfirmLiveSaveLoad ([bool]$ConfirmLiveSaveLoad)
    "test: $($result.status) (scope game; mode $Mode; live fixtures executed: $($result.liveFixturesExecuted))"
    foreach ($issue in $result.errors) { "$($issue.status): $($issue.phase): $($issue.message)" }
    "Original restoration: $($result.restoration.status). $($result.restoration.reason)"
    "Report: $(Join-Path $result.reportDirectory 'report.json')"
    exit $result.exitCode
} catch {
    [Console]::Error.WriteLine('test: FAIL: ' + $_.Exception.Message)
    exit 1
}
