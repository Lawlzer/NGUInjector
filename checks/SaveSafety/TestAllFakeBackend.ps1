#requires -Version 7.0
# External game boundary only. All requests/results/claims and protected bytes are real files.
param([string]$Directory, [string]$Behavior, [string]$Harness, [string]$ReportDir, [double]$AutomationTimeScale = 0.1)
$ErrorActionPreference = 'Stop'
. $Harness -FunctionsOnly
function Copy-Json($Value) {
    $options = @{ AsHashtable = $true; Depth = 100 }
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $options.DateKind = 'String' }
    ConvertFrom-Json -InputObject (ConvertTo-Json -InputObject $Value -Depth 100) @options
}
function Write-BackendJson($Path, $Value) {
    for ($attempt = 0; ; $attempt++) {
        try { Write-TestAllJson $Path $Value; return }
        catch { if ($attempt -ge 20) { throw }; Start-Sleep -Milliseconds 10 }
    }
}
function Publish {
    if (-not ($Behavior -eq 'NoProgress' -and $runtime.saveTestSession.phase -eq 'running' -and $runtime.planProgress.activeStageIndex -ge 0)) { $runtime.runtimeRevision++ }
    $runtime.updatedAt = [datetimeoffset]::UtcNow.ToString('o')
    Write-BackendJson (Join-Path $Directory 'runtime.json') $runtime
}
function Marker($Path, [string]$Total = '2000.25') {
    @{ path = $Path; sha256 = (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant(); playerName = 'Synthetic Player'; version = 1260
        rebirth = '7'; totalPlaytimeSeconds = $Total; rebirthSeconds = '60.5' }
}
$runtime = Read-TestAllJson (Join-Path $PSScriptRoot 'diagnostics-fixtures.json')
$runtime.startedAt = [datetimeoffset]::UtcNow.AddDays(-1).ToString('o')
$runtime.supportedCommands = @('captureLocalSave', 'scanSaves', 'loadLongestSave', 'startSaveTest', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')
$runtime.saveTestSession = @{ version = 1; supported = $true; sessionId = ''; fixtureGeneration = '0'; phase = 'idle' }
$native = Join-Path $Directory 'native-local.txt'; $settings = Join-Path $Directory 'settings.json'
[IO.File]::WriteAllText($native, 'original native bytes'); [IO.File]::WriteAllText($settings, 'original domain settings')
$original = Marker $native
$runtime.saveSafety = @{ version = 1; status = 'verified'; message = 'synthetic'; testActive = $false; scanComplete = $true; errors = @(); pendingFiles = 0
    ambiguousLongest = $false; current = Copy-Json $original; longest = Copy-Json $original }
$readyAt = $null; $restoreAt = $null; $counter = 0; $changedOrder = $false; $allocationReadyAt = $null
Publish
[IO.File]::WriteAllText((Join-Path $Directory 'ready'), 'ready')
$clock = [Diagnostics.Stopwatch]::StartNew()
while ($clock.Elapsed.TotalSeconds -lt 90) {
    $session = $runtime.saveTestSession
    if ($null -ne $allocationReadyAt) {
        $runtime.fakeAutomation.elapsedSeconds = ($clock.Elapsed.TotalSeconds - $allocationStartedAt) / $AutomationTimeScale
        if ($clock.Elapsed.TotalSeconds -ge $allocationReadyAt) {
            $runtime.execution = $readyExecution; $runtime.nguAllocation = $readyNgu
            $runtime.fakeAutomation.applied = $true
            $allocationReadyAt = $null
        }
    }
    if ($readyAt -and [datetimeoffset]::UtcNow -ge $readyAt) {
        $readyAt = $null
        if ($Behavior -eq 'FailedStartup') {
            $runtime.status = 'failed'; $runtime.execution.lifecycle = 'LoadingSave'; $session.phase = 'recoveryRequired'; $session.error = 'synthetic failed fresh startup'
        } else {
            $session.phase = 'running'; $runtime.status = 'running'; $runtime.execution.lifecycle = 'Running'
        }
    }
    if ($restoreAt -and [datetimeoffset]::UtcNow -ge $restoreAt) {
        $restoreAt = $null
        [IO.File]::Copy((Join-Path $Directory 'native.snapshot'), $native, $true)
        [IO.File]::Copy((Join-Path $Directory 'settings.snapshot'), $settings, $true)
        $session.phase = 'restored'; $session.restoration = @{ verified = $true; nativeVerified = $true; filesVerified = $true }
        $session.observed = Copy-Json $original
        $session.observed.totalPlaytimeSeconds = '2020.25'; $session.observed.rebirthSeconds = '80.5'
        if ($Behavior -eq 'RestoreClock') { $session.observed.totalPlaytimeSeconds = '1999.25' }
        if ($Behavior -eq 'RestoreMissingFlag') { $session.restoration.filesVerified = $false }
        if ($Behavior -eq 'RestoreMissingNative') { $session.restoration.nativeVerified = $false }
        if ($Behavior -eq 'RestoreMissingVerified') { $session.restoration.verified = $false }
        $runtime.saveSafety.current = Copy-Json $session.observed
        if ($Behavior -eq 'RestoreRuntimeClock') { $runtime.saveSafety.current.totalPlaytimeSeconds = '1999.25' }
        $runtime.saveSafety.testActive = $false; $runtime.saveSafety.status = 'verified'
        $runtime.status = 'running'; $runtime.execution.lifecycle = 'Running'
    }
    if ($session.phase -eq 'running' -and $runtime.planProgress.activeStageIndex -ge 0) {
        $counter++
        if ($Behavior -eq 'PriorityChange' -and $counter -gt 6 -and -not $changedOrder) {
            $runtime.planProgress.stages[0].configuredEnergyPriorities = @('TM', 'NGU')
            $changedOrder = $true
        }
        if ($Behavior -eq 'Unsettled') { $runtime.execution.gear[0].verified = -not $runtime.execution.gear[0].verified }
    }
    Publish
    foreach ($path in [IO.Directory]::GetFiles((Join-Path $Directory 'commands'), '*.json')) {
        $request = Read-TestAllJson $path
        [IO.File]::Move($path, (Join-Path $Directory "commands/claimed/$($request.id).claimed"))
        $args = $request.args; $data = @{}; $success = $true
        if ($request.command -in @('startSaveTest', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')) {
            if ($args.expectedStartedAt -cne $runtime.startedAt -or $args.sessionId -cnotmatch '\A[0-9a-f]{32}\z') { throw 'Ungarded transport/session request.' }
            $cancelRestore = $request.command -eq 'restoreSaveTest' -and $Behavior -like 'Cancelled*' -and $session.phase -eq 'idle'
            if ($request.command -ne 'startSaveTest' -and -not $cancelRestore -and $args.sessionId -cne $session.sessionId) { throw 'Wrong client session.' }
        }
        switch ($request.command) {
            'captureLocalSave' {
                $capture = Join-Path $Directory ($request.id + '.txt')
                [IO.File]::Copy($native, $capture)
                $data.save = Marker $capture
            }
            'scanSaves' { }
            { $_ -in @('startSaveTest', 'loadSaveTestFixture') } {
                if ($request.command -eq 'startSaveTest') {
                    $clientReport = @([IO.Directory]::GetFiles($ReportDir, 'report.json', [IO.SearchOption]::AllDirectories) | Where-Object {
                        $candidate = Read-TestAllJson $_
                        $request.id -cin @($candidate.commands | ForEach-Object { $_.request.id })
                    })[0]
                    $persisted = Read-TestAllJson $clientReport
                    if ($persisted.testSessionId -cne $args.sessionId -or -not $persisted.restoration.required -or $persisted.commands[-1].outcome -cne 'UNKNOWN') { throw 'Client identity/restore obligation was not durable before dispatch.' }
                    if ($args.acceptNativeSideEffects -isnot [bool] -or -not $args.acceptNativeSideEffects -or $args.Count -ne 4) { throw 'Missing explicit native acceptance.' }
                    if ($Behavior -like 'Cancelled*') {
                        # No fixture or snapshot is applied. The original game legitimately
                        # rebirths while the start is rejected or remains unacknowledged.
                        $runtime.saveSafety.current.rebirth = '8'
                        $runtime.saveSafety.current.totalPlaytimeSeconds = '2010.25'
                        $runtime.saveSafety.current.rebirthSeconds = '5.5'
                        $success = $false
                        break
                    }
                    [IO.File]::Copy($native, (Join-Path $Directory 'native.snapshot'))
                    [IO.File]::Copy($settings, (Join-Path $Directory 'settings.snapshot'))
                    $session = $runtime.saveTestSession = @{ version = 1; supported = $true; sessionId = $args.sessionId; fixtureGeneration = '0'
                        phase = 'starting'; restoreTarget = Copy-Json $original; restoration = @{ verified = $false }; error = '' }
                } elseif ($args.expectedFixtureGeneration -cne $session.fixtureGeneration -or $args.Count -ne 4) { throw 'Unfenced successor load.' }
                if ($args.fixture.Count -ne 6 -or $args.fixture.sha256 -cne (Get-FileHash -LiteralPath $args.fixture.path).Hash.ToLowerInvariant()) { throw 'Wrong fixture payload/hash.' }
                $session.fixture = Copy-Json $args.fixture
                $session.fixtureGeneration = ([long]$session.fixtureGeneration + 1).ToString()
                $session.phase = 'loadingFixture'
                $registered = @((Read-TestAllJson $clientReport).fixtures | Where-Object name -CEQ $args.fixture.name)[0]
                $session.observed = Copy-Json $registered.metadata
                $session.observed.totalPlaytimeSeconds = ((Get-TestAllClock $session.observed.totalPlaytimeSeconds) + 10).ToString([Globalization.CultureInfo]::InvariantCulture)
                $session.observed.rebirthSeconds = ((Get-TestAllClock $session.observed.rebirthSeconds) + 10).ToString([Globalization.CultureInfo]::InvariantCulture)
                $session.offlineProgress = @{ mode = 'native'; totalGainSeconds = '10' }
                $session.resetEvidence = @{ verified = $true; fallbackTargetsCompleted = $args.fixture.scenario -eq 'fallback'; nonbasicEnergyZero = $true
                    nonbasicMagicZero = $true; beardsInactive = $true; diggersInactive = $true; purchasedDiggerLevelsPreserved = $true }
                if ($Behavior -eq 'ResetMissing') { $session.resetEvidence.verified = $false }
                [IO.File]::WriteAllText($native, 'fixture native output'); [IO.File]::WriteAllText($settings, 'fixture domain settings')
                $runtime.saveSafety.current = Copy-Json $session.observed; $runtime.saveSafety.status = 'testing'; $runtime.saveSafety.testActive = $true
                $runtime.settings.customPlanFile = $args.fixture.plan
                $runtime.planProgress.planName = $args.fixture.plan; $runtime.planProgress.activeStageIndex = -1
                $runtime.planProgress.stages = @(@{ index = $(if ($args.fixture.stageIndex -lt 0) { 0 } else { $args.fixture.stageIndex }); energyPriorities = @('NGU', 'TM'); magicPriorities = @('NGU', 'WAN'); configuredEnergyPriorities = @('NGU', 'TM'); configuredMagicPriorities = @('NGU', 'WAN') })
                $runtime.status = 'starting'; $runtime.execution.lifecycle = 'LoadingSave'
                $readyAt = [datetimeoffset]::UtcNow.AddMilliseconds(150)
                $data.pendingRestart = $true
                if ($Behavior -eq 'BadGeneration') { $session.fixtureGeneration = '99' }
                if ($Behavior -eq 'BadSession') { $session.sessionId = 'f' * 32 }
            }
            'setSaveTestStage' {
                if ($args.Count -ne 4 -or $args.expectedFixtureGeneration -cne $session.fixtureGeneration -or $args.fixtureSha256 -cne $session.fixture.sha256 -or $session.phase -cne 'running') { throw 'Unfenced stage command.' }
                $runtime.planProgress.activeStageIndex = if ($session.fixture.stageIndex -lt 0) { 0 } else { $session.fixture.stageIndex }
                $runtime.planProgress.stages[0].index = $runtime.planProgress.activeStageIndex
                if ($Behavior -eq 'StageFailure') { $success = $false; $runtime.execution.lifecycle = 'Quiesced' }
                if ($Behavior -in @('ZeroNgu', 'FallbackNoSpend') -or $session.fixture.scenario -in @('fallback', 'early')) {
                    foreach ($row in $runtime.execution.resources | Where-Object target -In @('EnergyNgu', 'MagicNgu')) { $row.desired = @($row.desired | ForEach-Object { '0' }); $row.observed = $row.desired }
                    foreach ($lane in @('energy', 'magic')) { foreach ($row in $runtime.nguAllocation["${lane}Targets"]) { $row.allocated = '0' } }
                }
                if ($session.fixture.scenario -eq 'fallback' -and $Behavior -ne 'FallbackNoSpend') {
                    $runtime.execution.resources += @(@{ target = 'TimeMachineEnergy'; desired = @('12'); observed = @('12'); verified = $true }, @{ target = 'WandoosMagic'; desired = @('15'); observed = @('15'); verified = $true })
                }
                if ($session.fixture.scenario -eq 'early') {
                    $runtime.planProgress.stages[0].energyPriorities = @('TM'); $runtime.planProgress.stages[0].magicPriorities = @('WAN')
                    $runtime.execution.support = @{ desired = '|'; observed = '|'; verified = $true; desiredFeasible = $true; reason = 'Support locked' }
                }
                if ($Behavior -eq 'ExcludedFunded') { $runtime.nguAllocation.energyTargets[2].allocated = '1' }
                if ($Behavior -eq 'UnverifiedGear') { $runtime.execution.gear[0].verified = $false }
                if ($Behavior -eq 'UnverifiedResource') { $runtime.execution.resources[0].verified = $false }
                if ($Behavior -eq 'IdleUnexplained') { $runtime.manualAdventureOverlay.idleEnergy = '3'; $runtime.execution.resources[0].observed[0] = '0' }
                if ($Behavior -eq 'EmptySupport') { $runtime.execution.support.desired = '|'; $runtime.execution.support.observed = '|' }
                if ($Behavior -in @('Idle', 'IdleZero')) { $runtime.manualAdventureOverlay.idleEnergy = '3' }
                if ($Behavior -eq 'IdleZero') {
                    foreach ($row in $runtime.execution.resources | Where-Object target -EQ 'EnergyNgu') { $row.desired = @($row.desired | ForEach-Object { '0' }); $row.observed = $row.desired }
                    foreach ($row in $runtime.nguAllocation.energyTargets) { $row.allocated = '0' }
                    $runtime.planProgress.stages[0].energyPriorities = @('TM')
                }
                if ($Behavior -eq 'LateApply') {
                    $readyExecution = Copy-Json $runtime.execution; $readyNgu = Copy-Json $runtime.nguAllocation
                    $allocationStartedAt = $clock.Elapsed.TotalSeconds
                    $allocationReadyAt = $allocationStartedAt + 10 * $AutomationTimeScale
                    $runtime.fakeAutomation = @{ elapsedSeconds = 0; applyAfterSeconds = 10; applied = $false }
                    $runtime.execution.domains.Energy.waitingReason = 'FightOnEntry pending before the automation tick.'
                    $runtime.execution.resources[0].verified = $false
                    $runtime.execution.resources[0].observed = @($runtime.execution.resources[0].observed | ForEach-Object { '0' })
                    foreach ($row in $runtime.nguAllocation.energyTargets) { $row.allocated = '0' }
                    $runtime.execution.gear[0].verified = $false
                    $runtime.execution.support.verified = $false
                    $runtime.execution.support.reason = 'FightOnEntry pending before the automation tick.'
                }
            }
            'restoreSaveTest' {
                if ($args.Count -ne 2) { throw 'Wrong restore schema.' }
                $readyAt = $null
                if ($Behavior -like 'Cancelled*') {
                    $session = $runtime.saveTestSession = @{ version = 1; supported = $true; sessionId = $args.sessionId; fixtureGeneration = '0'; phase = 'restored'
                        restoreTarget = Copy-Json $runtime.saveSafety.current; observed = Copy-Json $runtime.saveSafety.current; fixture = @{}; error = ''
                        restoration = @{ verified = $true; nativeVerified = $true; filesVerified = $true; cancelledBeforeStart = $true } }
                    if ($Behavior -eq 'CancelledMissingNative') { $session.restoration.nativeVerified = $false }
                    if ($Behavior -eq 'CancelledMissingFiles') { $session.restoration.filesVerified = $false }
                    if ($Behavior -eq 'CancelledMissingVerified') { $session.restoration.verified = $false }
                    if ($Behavior -eq 'CancelledObservedClock') { $session.observed.totalPlaytimeSeconds = '2009.25' }
                    if ($Behavior -eq 'CancelledRuntimeClock') { $runtime.saveSafety.current.totalPlaytimeSeconds = '2009.25' }
                    break
                }
                if ($Behavior -eq 'RestoreFailure') { $session.phase = 'recoveryRequired'; $session.error = 'synthetic file restore failure'; $success = $false }
                else {
                    $session.phase = 'restoring'; $runtime.status = 'starting'; $runtime.execution.lifecycle = 'LoadingSave'
                    $restoreAt = [datetimeoffset]::UtcNow.AddMilliseconds(100); $data.pendingRestart = $true
                }
            }
            default { throw "Unexpected command: $($request.command)" }
        }
        Publish
        if ($request.command -in @('startSaveTest', 'loadSaveTestFixture', 'setSaveTestStage', 'restoreSaveTest')) { $data.saveTestSession = Copy-Json $session }
        $data.runtime = Copy-Json $runtime
        if ($Behavior -eq 'MissingResultSession' -and $request.command -eq 'startSaveTest') { $data.Remove('saveTestSession') }
        if (($Behavior -in @('NoStartAck', 'CancelledQueued') -and $request.command -eq 'startSaveTest') -or
            ($Behavior -eq 'NoLoadAck' -and $request.command -eq 'loadSaveTestFixture') -or
            ($Behavior -eq 'NoRestoreAck' -and $request.command -eq 'restoreSaveTest')) { continue }
        Write-BackendJson (Join-Path $Directory "command-results/$($request.id).json") @{ id = $request.id; command = $request.command; success = $success
            completedAt = [datetimeoffset]::UtcNow.ToString('o'); message = 'synthetic command result'; data = $data }
    }
    Start-Sleep -Milliseconds 25
}
