#requires -Version 7.0
<#
.SYNOPSIS
Read-only runtime diagnostics. Returns one report object; never exits the caller.
.DESCRIPTION
Only runtime.json is read. No commands, settings, saves, or game state are written.
success means every required check passed or was explicitly uncontrolled. Optional
independent NGU distribution remains INCONCLUSIVE: published caps are historical.
Samples=1 cannot establish heartbeat progress. Freshness allows 10 seconds of age
and 5 seconds of clock skew. A new ReportPath may be written outside RuntimeDir;
its parent must already exist. Infrastructure/argument errors throw; observations
(including missing/malformed runtime JSON) are returned as check results.
.EXAMPLE
$report = & ./diagnostics.ps1 -ExpectedPlan 'My Plan'
if (-not $report.success) { throw 'Runtime diagnostics did not pass.' }
#>
[CmdletBinding()]
param(
    [string]$RuntimeDir = (Join-Path $env:USERPROFILE 'Desktop/NGUInjector'),
    [ValidateRange(1, 1000)][int]$Samples = 3,
    [ValidateRange(0, 3600)][double]$IntervalSeconds = 2,
    [string]$ExpectedPlan,
    [string]$ReportPath
)

# Keep helpers/preferences out of a parent that dot-invokes this script.
& {
    $ErrorActionPreference = 'Stop'
    # Optional/additive runtime fields are observations, even under a strict parent.
    Set-StrictMode -Off
    $runtimeDirectory = [IO.Path]::GetFullPath($RuntimeDir)
    if (-not [IO.Directory]::Exists($runtimeDirectory)) { throw "Runtime directory does not exist: $runtimeDirectory" }
    $runtimePath = Join-Path $runtimeDirectory 'runtime.json'
    $outputPath = $null
    if ($ReportPath) {
        $outputPath = [IO.Path]::GetFullPath($ReportPath)
        $parent = [IO.Path]::GetDirectoryName($outputPath)
        if (-not [IO.Directory]::Exists($parent)) { throw "Report parent does not exist: $parent" }
        $prefix = $runtimeDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if ($outputPath.Equals($runtimeDirectory, [StringComparison]::OrdinalIgnoreCase) -or
            $outputPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'ReportPath must be outside RuntimeDir; diagnostics cannot write runtime/game files.'
        }
        if ([IO.File]::Exists($outputPath) -or [IO.Directory]::Exists($outputPath)) { throw "ReportPath already exists: $outputPath" }
    }

    $checks = [Collections.Generic.List[object]]::new()
    $observations = [Collections.Generic.List[object]]::new()
    $sampleIndex = 0
    function Add-Check($Id, $Status, $Message, $Evidence = $null, [bool]$Required = $true) {
        $checks.Add([pscustomobject]@{
            sample = $sampleIndex; id = $Id; status = $Status; required = $Required
            message = $Message; evidence = $Evidence
        })
    }
    function Test-IntegerString($Value) {
        return $Value -is [string] -and $Value -cmatch '\A(?:0|[1-9][0-9]*)\z' -and
            [bigint]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture) -le [long]::MaxValue
    }
    function Test-JsonInteger($Value, [long]$Maximum = [int]::MaxValue) {
        return ($Value -is [int] -or $Value -is [long] -or $Value -is [bigint]) -and $Value -ge 0 -and $Value -le $Maximum
    }
    function Get-Timestamp($Value) {
        if ($Value -is [datetime] -or $Value -is [datetimeoffset]) { return [datetimeoffset]$Value }
        $parsed = [datetimeoffset]::MinValue
        if ($Value -is [string] -and [datetimeoffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) { return $parsed }
        return $null
    }
    function Get-ControlStatus($Domain) {
        if ($Domain.controlled -isnot [bool]) { return 'INCONCLUSIVE' }
        if (-not $Domain.controlled) { return 'UNCONTROLLED' }
        if ($Domain.state -cne 'Observed') { return 'WAITING' }
        return 'PASS'
    }
    function Get-EqualityStatus($Domain, [bool]$Valid, [bool]$Equal, $Verified, $Reason) {
        $control = Get-ControlStatus $Domain
        if ($control -ne 'PASS') { return $control }
        if (-not $Valid) { return 'INCONCLUSIVE' }
        if (-not $Equal) {
            if ($Verified -is [bool] -and $Verified) { return 'FAIL' }
            if ($Reason) { return 'WAITING' }
            return 'FAIL'
        }
        if ($Verified -is [bool] -and $Verified) { return 'PASS' }
        if ($Reason) { return 'WAITING' }
        return 'INCONCLUSIVE'
    }
    function Read-SupportKey($Key) {
        if ($Key -isnot [string]) { return $null }
        $parts = $Key.Split('|')
        if ($parts.Count -ne 2) { return $null }
        $beards = @(); $diggers = @()
        if ($parts[0]) {
            foreach ($id in $parts[0].Split(',')) {
                if ($id -cnotmatch '\A[0-6]\z' -or $beards -contains [int]$id) { return $null }
                $beards += [int]$id
            }
        }
        if ($parts[1]) {
            foreach ($entry in $parts[1].Split(',')) {
                if ($entry -cnotmatch '\A(0|[1-9]|10|11):([1-9][0-9]*)\z') { return $null }
                $id = [int]$Matches[1]; $level = $Matches[2]
                if (-not (Test-IntegerString $level) -or $diggers.id -contains $id) { return $null }
                $diggers += [pscustomobject]@{ id = $id; level = $level }
            }
        }
        return [pscustomobject]@{ beards = $beards; diggers = $diggers }
    }
    function Get-OverallStatus($Items) {
        foreach ($status in @('FAIL', 'WAITING', 'INCONCLUSIVE', 'UNCONTROLLED')) {
            if (@($Items | Where-Object status -EQ $status).Count -gt 0) { return $status }
        }
        return 'PASS'
    }

    $targets = @{
        EnergyNgu = 'Energy'; AdvancedTraining = 'Energy'; Augment = 'Energy'; AugmentUpgrade = 'Energy'
        TimeMachineEnergy = 'Energy'; WandoosEnergy = 'Energy'; WishEnergy = 'Energy'
        BasicAttack = 'Energy'; BasicDefense = 'Energy'
        MagicNgu = 'Magic'; TimeMachineMagic = 'Magic'; WandoosMagic = 'Magic'; BloodRitual = 'Magic'; WishMagic = 'Magic'
        Hack = 'Res3'; WishRes3 = 'Res3'
    }
    for ($sampleIndex = 1; $sampleIndex -le $Samples; $sampleIndex++) {
        if ($sampleIndex -gt 1 -and $IntervalSeconds -gt 0) { Start-Sleep -Seconds $IntervalSeconds }
        $capturedAt = [datetimeoffset]::UtcNow
        $observation = [pscustomobject]@{
            index = $sampleIndex; capturedAt = $capturedAt.ToString('o'); updatedAt = $null
            session = $null; runtimeRevision = $null; readable = $false; fresh = $false; running = $false
        }
        $observations.Add($observation)
        try { $json = [IO.File]::ReadAllText($runtimePath) }
        catch [IO.FileNotFoundException] { Add-Check 'runtime.read' 'WAITING' 'runtime.json is not published yet.'; continue }
        catch [IO.DirectoryNotFoundException] { Add-Check 'runtime.read' 'WAITING' 'Runtime directory disappeared during sampling.'; continue }
        try { $runtime = ConvertFrom-Json -InputObject $json -AsHashtable -Depth 100 }
        catch { Add-Check 'runtime.read' 'FAIL' 'runtime.json is not valid JSON.' $_.Exception.Message; continue }
        if ($runtime -isnot [Collections.IDictionary]) { Add-Check 'runtime.read' 'FAIL' 'Runtime JSON must be an object.'; continue }
        $observation.readable = $true
        Add-Check 'runtime.read' 'PASS' 'Read runtime.json.'
        $updated = Get-Timestamp $runtime.updatedAt
        $started = Get-Timestamp $runtime.startedAt
        if ($null -ne $started) { $observation.session = $started.ToUniversalTime().ToString('o') }
        $observation.runtimeRevision = $runtime.runtimeRevision
        Add-Check 'runtime.revision' $(if (Test-JsonInteger $runtime.runtimeRevision 9007199254740991) { 'PASS' } else { 'INCONCLUSIVE' }) 'runtimeRevision must be a nonnegative JSON safe integer.' @{ revision = $runtime.runtimeRevision }
        if ($null -eq $updated) { Add-Check 'runtime.fresh' 'FAIL' 'updatedAt is missing or invalid.' }
        else {
            $observation.updatedAt = $updated.ToUniversalTime().ToString('o')
            $age = ($capturedAt - $updated).TotalSeconds
            $observation.fresh = $age -ge -5 -and $age -le 10
            Add-Check 'runtime.fresh' $(if ($observation.fresh) { 'PASS' } else { 'FAIL' }) 'Snapshot age must be between -5 and 10 seconds.' @{ ageSeconds = $age }
        }
        $observation.running = $runtime.status -ceq 'running' -and $runtime.execution.lifecycle -ceq 'Running'
        $runningStatus = if ($observation.running) { 'PASS' } elseif ($runtime.status -in @('failed', 'unloaded')) { 'FAIL' } else { 'WAITING' }
        Add-Check 'runtime.running' $runningStatus 'Runtime and execution lifecycle must both be running.' @{
            status = $runtime.status; lifecycle = $runtime.execution.lifecycle; startupError = $runtime.startupError
        }
        $execution = if ($runtime.execution -is [Collections.IDictionary]) { $runtime.execution } else { @{} }
        $domains = if ($execution.domains -is [Collections.IDictionary]) { $execution.domains } else { @{} }
        $plan = $runtime.planProgress
        $selected = $runtime.settings.customPlanFile
        $planEvidence = @{ expected = $ExpectedPlan; selected = $selected; observed = $plan.planName; revision = $plan.planRevision; stage = $plan.activeStageIndex; reason = $plan.reason }
        $planStatus = 'PASS'
        if ($ExpectedPlan -and (-not [string]::Equals($ExpectedPlan, $selected, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals($ExpectedPlan, $plan.planName, [StringComparison]::OrdinalIgnoreCase))) { $planStatus = 'FAIL' }
        elseif ($selected -and $plan.planName -and -not [string]::Equals($selected, $plan.planName, [StringComparison]::OrdinalIgnoreCase)) { $planStatus = 'FAIL' }
        elseif ($runtime.settings.useCustomRebirthPlan -is [bool] -and -not $runtime.settings.useCustomRebirthPlan -and -not $ExpectedPlan) { $planStatus = 'UNCONTROLLED' }
        elseif ($runtime.settings.useCustomRebirthPlan -isnot [bool] -or $plan.available -isnot [bool] -or -not $selected -or -not $plan.planName) { $planStatus = 'INCONCLUSIVE' }
        elseif (-not $runtime.settings.useCustomRebirthPlan -or -not $plan.available -or $plan.status -cne 'running') { $planStatus = 'WAITING' }
        Add-Check 'plan.identity' $planStatus 'Selected and observed plan identity; ExpectedPlan also requires an active plan.' $planEvidence

        foreach ($lane in @('Energy', 'Magic', 'Res3')) {
            $rows = @($execution.resources | Where-Object { $_.target -and $targets[$_.target] -eq $lane })
            $status = Get-ControlStatus $domains.$lane
            if ($status -eq 'PASS' -and $rows.Count -eq 0) { $status = 'INCONCLUSIVE' }
            Add-Check "resources.$lane" $status 'Backend domain control and observed resource rows.' @{ domain = $domains.$lane; rowCount = $rows.Count }
        }
        $seen = @{}
        foreach ($row in $execution.resources) {
            $lane = if ($row.target) { $targets[$row.target] } else { $null }
            $valid = $row.desired -is [array] -and $row.observed -is [array] -and $row.desired.Count -gt 0 -and $row.desired.Count -eq $row.observed.Count
            $equal = $valid
            if ($valid) {
                for ($i = 0; $i -lt $row.desired.Count; $i++) {
                    if (-not (Test-IntegerString $row.desired[$i]) -or -not (Test-IntegerString $row.observed[$i])) { $valid = $false }
                    if ($row.desired[$i] -cne $row.observed[$i]) { $equal = $false }
                }
            }
            $reason = if ($lane) { $domains.$lane.waitingReason } else { $null }
            $status = if ($lane) { Get-EqualityStatus $domains.$lane $valid $equal $row.verified $reason } else { 'INCONCLUSIVE' }
            if (-not $row.target -or $seen.ContainsKey([string]$row.target)) { $status = 'FAIL' }
            $seen[[string]$row.target] = $true
            Add-Check "resource.$($row.target)" $status 'Exact integer-string desired/observed equality AND backend verification are required.' @{
                lane = $lane; desired = $row.desired; observed = $row.observed; verified = $row.verified; validIntegerStrings = $valid; reason = $reason
            }
        }

        $ngu = if ($runtime.nguAllocation -is [Collections.IDictionary]) { $runtime.nguAllocation } else { @{} }
        foreach ($lane in @('energy', 'magic')) {
            $rows = $ngu["${lane}Targets"]
            $weights = $ngu["${lane}Weights"]
            $count = if ($lane -eq 'energy') { 9 } else { 7 }
            $valid = $ngu.available -is [bool] -and $ngu.available -and $rows -is [array] -and $rows.Count -eq $count -and $weights -is [array] -and $weights.Count -eq $count
            $funded = 0; $excluded = @(); $violations = @(); $total = [bigint]0
            if ($valid) {
                for ($i = 0; $i -lt $count; $i++) {
                    $row = $rows[$i]
                    if (-not (Test-JsonInteger $row.index) -or $row.index -ne $i -or -not (Test-JsonInteger $row.weight) -or
                        -not (Test-JsonInteger $weights[$i]) -or $row.weight -ne $weights[$i] -or -not (Test-IntegerString $row.allocated)) { $valid = $false; continue }
                    $amount = [bigint]::Parse($row.allocated, [Globalization.CultureInfo]::InvariantCulture)
                    $total += $amount
                    if ($amount -gt 0) { $funded++ }
                    if ($row.weight -eq 0) { $excluded += $i; if ($amount -ne 0) { $violations += $i } }
                }
            }
            $status = if ($valid) { 'PASS' } else { 'INCONCLUSIVE' }
            Add-Check "ngu.$lane.counts" $status 'Live NGU target counts and allocations, not historical receipts.' @{
                targetCount = @($rows).Count; expectedTargetCount = $count; fundedCount = $funded
                totalAllocated = $total.ToString([Globalization.CultureInfo]::InvariantCulture); targets = $rows
            }
            $status = if (-not $valid) { 'INCONCLUSIVE' } elseif ($violations.Count -gt 0) { 'FAIL' } else { 'PASS' }
            Add-Check "ngu.$lane.exclusions" $status 'Zero-weight NGU targets must have zero actual allocation.' @{ excludedIndexes = $excluded; violatingIndexes = $violations }
            Add-Check "ngu.$lane.distribution" 'INCONCLUSIVE' 'Independent weighted distribution cannot be verified: no authoritative current caps/budget are published. Historical bufferedCap, belowBufferedCap and WeightsApplied are not proof.' @{ capBasis = $ngu.capBasis } $false
        }

        $overlay = if ($runtime.manualAdventureOverlay -is [Collections.IDictionary]) { $runtime.manualAdventureOverlay } else { @{} }
        foreach ($lane in @('Energy', 'Magic', 'Res3')) {
            $key = $lane.ToLowerInvariant()
            $total = $overlay.$key; $idle = $overlay."idle$lane"
            $actual = if ($lane -ne 'Res3') { $overlay["${key}Allocations"] } else { $null }
            $valid = $overlay.available -is [bool] -and $overlay.available -and (Test-IntegerString $total) -and (Test-IntegerString $idle)
            $allocations = @(); $allocated = [bigint]0
            if ($lane -ne 'Res3') {
                if ($actual -isnot [string] -or $actual.Length -eq 0) { $valid = $false }
                else {
                    $allocations = $actual.Split(',')
                    foreach ($amount in $allocations) {
                        if (-not (Test-IntegerString $amount)) { $valid = $false }
                        else { $allocated += [bigint]::Parse($amount, [Globalization.CultureInfo]::InvariantCulture) }
                    }
                }
            }
            $status = if (-not $valid) { 'INCONCLUSIVE' } elseif ([bigint]$idle -gt [bigint]$total) { 'FAIL' }
                elseif ($domains.$lane.controlled -isnot [bool]) { 'INCONCLUSIVE' }
                elseif (-not $domains.$lane.controlled) { 'UNCONTROLLED' }
                elseif ($idle -cne '0') { 'WAITING' } else { 'PASS' }
            Add-Check "idle.$lane" $status 'Actual manualAdventureOverlay observation; nonzero controlled idle is visible, not proof of an allocator defect.' @{
                total = $total; idle = $idle; allocations = $allocations; observedAllocationSum = $allocated.ToString([Globalization.CultureInfo]::InvariantCulture)
                overlayActive = $overlay.active; reason = $domains.$lane.waitingReason
                limitation = 'Energy lane list omits basic training; Res3 lane allocations are not published. No total-conservation or cap inference.'
            }
        }

        $gear = @($execution.gear)
        $status = Get-ControlStatus $domains.Gear
        if ($status -eq 'PASS' -and @(-1, -2, -3, -4, -5 | Where-Object { $_ -notin $gear.slot }).Count -gt 0) { $status = 'INCONCLUSIVE' }
        Add-Check 'gear' $status 'Native equipment observations must include all five base slots.'
        $seen = @{}
        foreach ($row in $gear) {
            if ($null -eq $row) { continue }
            $valid = ($row.slot -is [int] -or $row.slot -is [long]) -and ($row.slot -in @(-1, -2, -3, -4, -5, -6) -or $row.slot -ge 10000) -and
                (Test-JsonInteger $row.desired) -and (Test-JsonInteger $row.observed)
            $status = Get-EqualityStatus $domains.Gear $valid ($row.desired -ceq $row.observed) $row.verified $domains.Gear.waitingReason
            if ($seen.ContainsKey([string]$row.slot)) { $status = 'FAIL' }
            $seen[[string]$row.slot] = $true
            Add-Check "gear.$($row.slot)" $status 'Native gear ID equality AND backend instance/material verification are required.' $row
        }
        $support = $execution.support
        $observedSupport = Read-SupportKey $support.observed
        $desiredSupport = Read-SupportKey $support.desired
        $valid = $null -ne $observedSupport -and $null -ne $desiredSupport
        $reason = $support.reason
        if (-not $reason) { $reason = $domains.Support.waitingReason }
        $status = Get-EqualityStatus $domains.Support $valid ($support.desired -ceq $support.observed) $support.verified $reason
        Add-Check 'support.equality' $status 'Native support stable-key equality AND backend verification are required.' $support
        foreach ($part in @('beards', 'diggers')) {
            $wanted = ConvertTo-Json -InputObject @($desiredSupport.$part) -Compress -Depth 10
            $actual = ConvertTo-Json -InputObject @($observedSupport.$part) -Compress -Depth 10
            $status = Get-EqualityStatus $domains.Support $valid ($wanted -ceq $actual) $support.verified $reason
            Add-Check "support.$part" $status 'Exact active native selection (including digger integer-string levels).' @{ desired = $desiredSupport.$part; observed = $observedSupport.$part; reason = $reason }
        }
        $status = Get-ControlStatus $domains.Support
        if ($status -eq 'PASS') {
            $status = if ($support.desiredFeasible -isnot [bool]) { 'INCONCLUSIVE' } elseif (-not $support.desiredFeasible) { 'WAITING' }
                elseif (-not $valid -or $support.desired -cne $support.observed) { 'INCONCLUSIVE' } else { 'PASS' }
        }
        Add-Check 'support.affordability' $status 'Backend desired support feasibility applies to observed support only when selections match; independent income/drain data are not published.' @{
            desiredFeasible = $support.desiredFeasible; reason = $reason
        }
        $native = $execution.nativeSettings
        foreach ($setting in @(
            @{ name = 'os'; desired = $native.desiredOs; observed = $native.os; valid = (Test-JsonInteger $native.desiredOs) -and (Test-JsonInteger $native.os) },
            @{ name = 'atToughness'; desired = $native.desiredAtToughness; observed = @($native.atTargets)[0]; valid = (Test-IntegerString $native.desiredAtToughness) -and (Test-IntegerString @($native.atTargets)[0]) },
            @{ name = 'atPower'; desired = $native.desiredAtPower; observed = @($native.atTargets)[1]; valid = (Test-IntegerString $native.desiredAtPower) -and (Test-IntegerString @($native.atTargets)[1]) }
        )) {
            $status = Get-ControlStatus $domains.NativeSettings
            if ($status -eq 'PASS') {
                if ($null -eq $setting.desired -or $setting.desired -ceq '') { $status = 'UNCONTROLLED' }
                elseif (-not $setting.valid) { $status = 'INCONCLUSIVE' }
                elseif ($setting.desired -cne $setting.observed) { $status = 'FAIL' }
            }
            Add-Check "native.$($setting.name)" $status 'Exact native setting observation (AT index 0 toughness, index 1 power).' $setting
        }

        $safety = $runtime.saveSafety
        $status = switch -CaseSensitive ($safety.status) {
            'verified' { 'PASS' }; 'behind' { 'FAIL' }; 'unknown' { 'INCONCLUSIVE' }
            'testing' { 'WAITING' }; 'recoveryRequired' { 'FAIL' }; default { 'INCONCLUSIVE' }
        }
        if ($safety.testActive -is [bool] -and $safety.testActive -and $status -eq 'PASS') { $status = 'WAITING' }
        if ($safety.scanComplete -isnot [bool] -or -not $safety.scanComplete) { if ($status -eq 'PASS') { $status = 'WAITING' } }
        if ($safety.errors -is [array] -and $safety.errors.Count -gt 0) { $status = 'FAIL' }
        Add-Check 'saveSafety' $status 'Save-safety status; behind/testing/recovery are visible issues.' $safety
        $valid = $safety -is [Collections.IDictionary] -and (Test-JsonInteger $safety.version) -and $safety.version -eq 1 -and
            $safety.status -cin @('verified', 'behind', 'unknown', 'testing', 'recoveryRequired') -and $safety.message -is [string] -and
            $safety.testActive -is [bool] -and $safety.scanComplete -is [bool] -and $safety.errors -is [array]
        foreach ($marker in @($safety.current, $safety.longest)) {
            if ($null -eq $marker) { $valid = $false; continue }
            if (-not (Test-IntegerString $marker.rebirth)) { $valid = $false }
            foreach ($field in @('totalPlaytimeSeconds', 'rebirthSeconds')) {
                $number = 0.0
                if ($marker.$field -isnot [string] -or -not [double]::TryParse($marker.$field, [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or -not [double]::IsFinite($number) -or $number -lt 0) { $valid = $false }
            }
        }
        if ($valid) {
            foreach ($marker in @($safety.current, $safety.longest)) {
                if ([double]::Parse($marker.rebirthSeconds, [Globalization.CultureInfo]::InvariantCulture) -gt
                    [double]::Parse($marker.totalPlaytimeSeconds, [Globalization.CultureInfo]::InvariantCulture)) { $valid = $false }
            }
        }
        if ($safety.longest.path -isnot [string] -or [string]::IsNullOrWhiteSpace($safety.longest.path) -or
            $safety.longest.sha256 -isnot [string] -or $safety.longest.sha256 -cnotmatch '\A[0-9a-fA-F]{64}\z') { $valid = $false }
        # Unknown/scanning telemetry may legitimately lack a longest-save marker.
        $schemaStatus = if ($valid) { 'PASS' } elseif ($safety.status -ceq 'verified') { 'FAIL' } else { 'INCONCLUSIVE' }
        if ($valid -and $safety.status -ceq 'verified' -and [double]::Parse($safety.current.totalPlaytimeSeconds, [Globalization.CultureInfo]::InvariantCulture) -lt
            [double]::Parse($safety.longest.totalPlaytimeSeconds, [Globalization.CultureInfo]::InvariantCulture)) { $schemaStatus = 'FAIL' }
        Add-Check 'saveSafety.evidence' $schemaStatus 'Version-1 progress strings and longest-save identity; verified current playtime cannot be behind longest. This does not re-read or hash save files.' $safety
    }

    $sampleIndex = 0
    $sessionStatus = 'PASS'
    $sessions = @($observations | Where-Object { $null -ne $_.session } | ForEach-Object session | Sort-Object -Unique)
    if ($sessions.Count -gt 1) { $sessionStatus = 'FAIL' }
    elseif ($sessions.Count -ne 1 -or @($observations | Where-Object { $null -eq $_.session }).Count -gt 0) { $sessionStatus = 'INCONCLUSIVE' }
    foreach ($observation in $observations) {
        if ($observation.session -and (Get-Timestamp $observation.session) -gt (Get-Timestamp $observation.capturedAt).AddSeconds(5)) { $sessionStatus = 'FAIL' }
        if ($observation.session -and $observation.updatedAt -and (Get-Timestamp $observation.session) -gt (Get-Timestamp $observation.updatedAt).AddSeconds(5)) { $sessionStatus = 'FAIL' }
    }
    Add-Check 'runtime.session' $sessionStatus 'All samples must belong to the same startedAt session.' @{ sessions = $sessions }
    $progressStatus = if ($Samples -eq 1) { 'INCONCLUSIVE' } else { 'WAITING' }
    $previous = $null; $advanced = $false
    foreach ($observation in $observations) {
        if (-not (Test-JsonInteger $observation.runtimeRevision 9007199254740991)) { $progressStatus = 'INCONCLUSIVE'; break }
        if ($null -ne $previous) {
            if ($observation.runtimeRevision -lt $previous.runtimeRevision -or
                ($observation.runtimeRevision -eq $previous.runtimeRevision -and $observation.updatedAt -cne $previous.updatedAt)) { $progressStatus = 'FAIL'; break }
            if ($observation.runtimeRevision -gt $previous.runtimeRevision) { $advanced = $true }
        }
        $previous = $observation
    }
    if ($progressStatus -eq 'WAITING' -and $advanced) { $progressStatus = 'PASS' }
    Add-Check 'runtime.progress' $progressStatus 'Multiple samples require a progressing, non-regressing runtimeRevision; a single sample cannot establish progress.' @{ revisions = @($observations | ForEach-Object runtimeRevision) } ($Samples -gt 1)
    foreach ($check in $checks) {
        if ($check.sample -eq 0 -or $check.id.StartsWith('runtime.') -or $check.status -ne 'PASS') { continue }
        $observation = $observations[$check.sample - 1]
        if (-not $observation.fresh -or -not $observation.running -or $sessionStatus -ne 'PASS' -or ($Samples -gt 1 -and $progressStatus -ne 'PASS')) {
            $check.status = 'INCONCLUSIVE'
            $check.message = 'Untrusted observation (fresh/running/same-session/progress prerequisites not met). ' + $check.message
        }
    }
    $counts = [ordered]@{}
    foreach ($status in @('PASS', 'FAIL', 'INCONCLUSIVE', 'WAITING', 'UNCONTROLLED')) { $counts[$status] = @($checks | Where-Object status -EQ $status).Count }
    $requiredIssues = @($checks | Where-Object { $_.required -and $_.status -notin @('PASS', 'UNCONTROLLED') })
    $report = [pscustomobject]@{
        version = 1; generatedAt = [datetimeoffset]::UtcNow.ToString('o'); readOnly = $true
        runtimePath = $runtimePath; expectedPlan = $ExpectedPlan; sampleCount = $Samples; intervalSeconds = $IntervalSeconds
        freshnessSeconds = 10; futureSkewSeconds = 5; success = $requiredIssues.Count -eq 0
        status = Get-OverallStatus $checks; counts = $counts; requiredIssueCount = $requiredIssues.Count
        samples = @($observations | ForEach-Object {
            $items = @($checks | Where-Object sample -EQ $_.index)
            [pscustomobject]@{ observation = $_; status = Get-OverallStatus $items; checks = $items }
        })
        checks = $checks.ToArray()
        issues = @($checks | Where-Object { $_.status -notin @('PASS', 'UNCONTROLLED') })
    }
    if ($outputPath) {
        $content = ConvertTo-Json -InputObject $report -Depth 100
        $stream = [IO.File]::Open($outputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($content)
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
    }
    $report
}
