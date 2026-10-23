param(
    [ValidateRange(1, 86400)][int]$DurationSeconds = 120,
    [string]$Family = '',
    [ValidateSet('Any', 'Planned')][string]$SupportPolicy = 'Any',
    [ValidateRange(0, 86400)][int]$CancelAfterSeconds = 0,
    [switch]$PreviewOnly,
    [string]$BridgePath = (Join-Path $PSScriptRoot '..\..\..\temp\ngu-bridge.ps1'),
    [string]$ReportParent = (Join-Path $env:LOCALAPPDATA 'Temp\opencode')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ReportParent -PathType Container)) { throw 'Report parent does not exist.' }
if (-not (Test-Path -LiteralPath $BridgePath -PathType Leaf)) { throw 'Live command bridge is unavailable.' }
. $BridgePath

$reportDir = Join-Path $ReportParent ('combat-prep-live-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$null = New-Item -ItemType Directory -Path $reportDir
function Save-Report([string]$Name, $Value) {
    [IO.File]::WriteAllText((Join-Path $reportDir $Name), ($Value | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
}
function Training-Summary($State) {
    [pscustomobject]@{
        status = $State.status; phase = $State.phase; elapsed = $State.elapsedSeconds
        remaining = $State.remainingSeconds; reason = $State.reason
        atEnergy = $State.actual.energyAtLevel; atMagic = $State.actual.magicAtLevel
        wEnergy = $State.actual.wandoosEnergyLevel; wMagic = $State.actual.wandoosMagicLevel
        wMagicAllocation = $State.actual.wandoosMagicAllocation
        actualAugments = $State.actual.augmentMultiplier; actualWandoos = $State.actual.wandoosMultiplier
        actualCombined = $State.actual.combinedMultiplier; projectedCombined = $State.projected.combinedMultiplier
        supportStatus = $State.supportStatus; supportRestoration = $State.supportRestoration
        plannedSupport = $State.plannedSupport; actualSupport = $State.actualSupport
    }
}
function Support-Key($Support) {
    if ($null -eq $Support -or $null -eq $Support.beards -or $null -eq $Support.diggers) { return $null }
    $beards = @($Support.beards | Sort-Object id | ForEach-Object { [string]$_.id }) -join ','
    $diggers = @($Support.diggers | Sort-Object id | ForEach-Object { [string]$_.id + ':' + [string]$_.level }) -join ','
    return $beards + '|' + $diggers
}

$before = Read-NguRuntime
Save-Report 'before.json' $before
$settingsPath = Join-Path $RuntimeDir 'settings.json'
$settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
$previewResult = Invoke-NguCommand 'previewCombatPrep' @{ durationSeconds = $DurationSeconds; backgroundEnergyPercent = 3; backgroundMagicPercent = 3 }
Save-Report 'preview.json' $previewResult
$preview = $previewResult.data.combatPrepPreview
if ($preview.durationSeconds -ne $DurationSeconds -or $preview.schemaVersion -ne 1) { throw 'Unexpected preview contract.' }
$eligible = @($preview.candidates | Where-Object {
    $_.available -and (-not $Family -or $_.label.StartsWith($Family, [StringComparison]::Ordinal)) -and
        ($SupportPolicy -eq 'Any' -or @($_.phases | Where-Object { $_.support.policy -eq 'phase' }).Count -gt 0)
})
$candidate = if ($Family -or $SupportPolicy -eq 'Planned') { $eligible[0] }
else { @($eligible | Where-Object { $_.id -eq $preview.recommendedCandidateId })[0] }
if (-not $candidate) { throw "Requested candidate is unavailable. Report: $reportDir" }
[pscustomobject]@{ report = $reportDir; candidate = $candidate.label; seconds = $DurationSeconds; projectedGain = $candidate.gainFactor; phases = $candidate.phases } | ConvertTo-Json -Depth 7
if ($PreviewOnly) { return }

$runId = $null
$samples = [Collections.Generic.List[object]]::new()
$sawAt = $false
$sawPostAtW = $false
$started = [Diagnostics.Stopwatch]::StartNew()
try {
    $startResult = Invoke-NguCommand 'startCombatPrep' @{ previewId = $preview.previewId; candidateId = $candidate.id }
    Save-Report 'start.json' $startResult
    $runId = $startResult.data.runtime.combatPrep.runId
    if (-not $runId) { throw 'Start did not report its run ID.' }
    do {
        $runtime = Read-NguRuntime
        $state = $runtime.combatPrep
        if ($state.runId -ne $runId) { throw 'Run identity changed during live verification.' }
        $samples.Add($state)
        if ($state.active -and $state.supportStatus -eq 'verified') {
            $wanted = Support-Key $state.plannedSupport
            $observed = Support-Key $state.actualSupport
            if ($null -eq $wanted -or $wanted -ne $observed) { throw 'Observed phase support differs from the pinned verified selection.' }
        }
        if ($candidate.atSeconds -gt 0 -and -not $sawAt -and $state.active -and $state.phase -ne 'AT' -and
            ($state.actual.wandoosMagicAllocation -ne '0' -or $state.actual.wandoosEnergyAllocation -ne '0')) {
            throw 'Wandoos received resources before a later AT preparation phase.'
        }
        if ($state.active -and $state.phase -eq 'AT') {
            $sawAt = $true
            if ($state.actual.wandoosMagicAllocation -ne '0' -or $state.actual.wandoosEnergyAllocation -ne '0') {
                throw 'Wandoos received resources before AT preparation completed.'
            }
        }
        if ($sawAt -and $state.active -and $state.phase -ne 'AT' -and $state.actual.wandoosMagicAllocation -ne '0') {
            if (-not $state.atTargetsReached) { throw 'Wandoos magic started before actual AT targets were reached.' }
            $sawPostAtW = $true
        }
        if (-not $state.active) { break }
        if ($CancelAfterSeconds -gt 0 -and $started.Elapsed.TotalSeconds -ge $CancelAfterSeconds) {
            $cancel = Invoke-NguCommand 'cancelCombatPrep' @{ runId = $runId }
            Save-Report 'cancel.json' $cancel
            $state = $cancel.data.runtime.combatPrep
            $samples.Add($state)
            break
        }
        if ($started.Elapsed.TotalSeconds -gt $DurationSeconds + 60) { throw 'Run did not honor its original deadline.' }
        Start-Sleep -Milliseconds $(if ($DurationSeconds -gt 600) { 5000 } else { 500 })
    } while ($true)
    Save-Report 'samples.json' $samples.ToArray()
    Save-Report 'final.json' (Read-NguRuntime)
    if ($state.status -eq 'failed') { throw ('Live execution failed: ' + $state.reason) }
    if (-not $CancelAfterSeconds -and $state.status -ne 'completed') { throw ('Run did not complete: ' + $state.reason) }
    if ($CancelAfterSeconds -and $state.status -ne 'cancelled') { throw 'Explicit cancellation did not end the run.' }
    if ($candidate.atSeconds -gt 0 -and -not $sawAt) { throw 'AT candidate never entered AT.' }
    $summary = Training-Summary $state
    $summary | Add-Member -NotePropertyName report -NotePropertyValue $reportDir
    $summary | Add-Member -NotePropertyName settingsUnchanged -NotePropertyValue ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -eq $settingsHash)
    $summary | Add-Member -NotePropertyName sawAt -NotePropertyValue $sawAt
    $summary | Add-Member -NotePropertyName sawPostAtMagicWandoos -NotePropertyValue $sawPostAtW
    $summary | Add-Member -NotePropertyName forecastErrorPercent -NotePropertyValue $(if ($state.status -eq 'completed' -and $state.projected.combinedMultiplier -gt 0) {
        100 * ($state.actual.combinedMultiplier / $state.projected.combinedMultiplier - 1)
    } else { $null })
    Save-Report 'summary.json' $summary
    $summary | ConvertTo-Json -Depth 4
    if (-not $summary.settingsUnchanged) { throw "Live verification changed settings.json. Report: $reportDir" }
} finally {
    if ($runId) {
        $current = Read-NguRuntime
        if ($current.combatPrep.active -and $current.combatPrep.runId -eq $runId) {
            $null = Invoke-NguCommand 'cancelCombatPrep' @{ runId = $runId }
        }
    }
    if ($samples.Count) { Save-Report 'samples.json' $samples.ToArray() }
}
