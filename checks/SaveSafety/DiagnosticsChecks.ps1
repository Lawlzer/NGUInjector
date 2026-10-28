#requires -Version 7.0
<# Offline checks: pwsh -NoProfile -File checks/SaveSafety/DiagnosticsChecks.ps1
   Real temporary runtime.json files, no Pester, game process, build, or deployment.
   Only Start-Sleep is replaced in sequence tests, to publish the next real JSON
   snapshot deterministically at the sampling boundary instead of racing a timer.
#>
& {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off
    $diagnostics = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../diagnostics.ps1'))
    $fixturePath = Join-Path $PSScriptRoot 'diagnostics-fixtures.json'
    $tempParent = Join-Path ([IO.Path]::GetTempPath()) 'opencode'
    if (-not [IO.Directory]::Exists($tempParent)) { $tempParent = [IO.Path]::GetTempPath() }
    if (-not (Test-Path -LiteralPath $tempParent -PathType Container)) { throw 'Temporary parent does not exist.' }
    $root = Join-Path $tempParent ('ngu-diagnostics-' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($root)
    $results = [Collections.Generic.List[object]]::new()
    $caseNumber = 0
    $expectedTestSessionId = '0123456789abcdef0123456789abcdef'
    function Assert-Equal($Actual, $Expected, $Label = 'value') {
        $a = ConvertTo-Json -InputObject $Actual -Depth 100 -Compress
        $e = ConvertTo-Json -InputObject $Expected -Depth 100 -Compress
        if ($a -cne $e) { throw "$Label expected $e, got $a" }
    }
    function Assert-Check($Report, $Id, $Status, [int]$Sample = 1) {
        $matches = @($Report.checks | Where-Object { $_.id -ceq $Id -and $_.sample -eq $Sample })
        Assert-Equal $matches.Count 1 "check count for $Id at sample $Sample"
        Assert-Equal $matches[0].status $Status "status for $Id at sample $Sample"
        return $matches[0]
    }
    function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
        $message = $null
        try { $null = & $Action } catch { $message = $_.Exception.Message }
        if ($null -eq $message -or $message -notmatch $Pattern) { throw "Expected exception matching '$Pattern', got '$message'" }
    }
    function New-Fixture {
        $data = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($fixturePath)) -AsHashtable -Depth 100
        $data.startedAt = [datetimeoffset]::UtcNow.AddMinutes(-1).ToString('o')
        $data.updatedAt = [datetimeoffset]::UtcNow.ToString('o')
        return $data
    }
    function New-TestFixture([string]$Status = 'behind') {
        $data = New-Fixture
        $restore = $data.saveSafety.longest.Clone()
        $restore.path = Join-Path $caseDirectory 'protected-original.txt'
        $restore.version = 123; $restore.playerName = 'Fixture Player'
        $data.saveTestSession = @{
            version = 1; supported = $true; sessionId = $expectedTestSessionId; phase = 'running'
            fixtureGeneration = '9007199254740993'
            fixture = @{ name = 'Older fixture'; sha256 = ('a' * 64) }
            resetEvidence = @{ verified = $true }; restoreTarget = $restore
        }
        $data.saveSafety.status = $Status; $data.saveSafety.testActive = $true
        $data.saveSafety.scanComplete = $false; $data.saveSafety.pendingFiles = 0; $data.saveSafety.knownFiles = 1
        $data.saveSafety.message = "Fixture $Status warning: do not overwrite newer progress."
        $data.saveSafety.current.totalPlaytimeSeconds = if ($Status -ceq 'behind') { '500.5' } else { '1000.5' }
        return $data
    }
    function Write-Fixture($Data, $Directory) {
        [IO.File]::WriteAllText((Join-Path $Directory 'runtime.json'), (ConvertTo-Json -InputObject $Data -Depth 100), [Text.UTF8Encoding]::new($false))
    }
    function Invoke-Fixture($Data, [hashtable]$Arguments = @{}) {
        Write-Fixture $Data $caseDirectory
        return & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0 @Arguments
    }
    function Invoke-Sequence([object[]]$Frames, [hashtable]$Arguments = @{}) {
        $state = @{ index = 0; calls = 0 }
        Write-Fixture $Frames[0] $caseDirectory
        function Start-Sleep([double]$Seconds) {
            $state.index++; $state.calls++
            Write-Fixture $Frames[$state.index] $caseDirectory
        }
        $report = & $diagnostics -RuntimeDir $caseDirectory -Samples $Frames.Count -IntervalSeconds 0.01 @Arguments
        Assert-Equal $state.calls ($Frames.Count - 1) 'sampling interval calls'
        return $report
    }
    function Test-Case($Name, [scriptblock]$Action) {
        $caseNumber = $results.Count + 1
        $caseDirectory = Join-Path $root $caseNumber
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Test root disappeared.' }
        $null = [IO.Directory]::CreateDirectory($caseDirectory)
        try {
            $null = & $Action
            $results.Add([pscustomobject]@{ name = $Name; success = $true; error = $null })
        } catch {
            $results.Add([pscustomobject]@{ name = $Name; success = $false; error = $_.ToString() + ' at ' + $_.ScriptStackTrace })
        }
    }
    try {
        Test-Case 'runtime reader permits the publisher handle without retries or lost validation' {
            Write-Fixture (New-Fixture) $caseDirectory
            $path = Join-Path $caseDirectory 'runtime.json'
            $publisher = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try {
                $report = & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0
                $null = Assert-Check $report 'runtime.read' 'PASS'
                Assert-Equal $report.requiredIssueCount 0
            } finally { $publisher.Dispose() }
        }
        Test-Case 'fresh single snapshot returns one structured report with scoped success' {
            $report = @(Invoke-Fixture (New-Fixture))
            Assert-Equal $report.Count 1 'pipeline object count'
            $report = $report[0]
            Assert-Equal $report.version 1
            Assert-Equal $report.success $true
            Assert-Equal $report.readOnly $true
            Assert-Equal $report.status 'INCONCLUSIVE'
            Assert-Equal $report.requiredIssueCount 0
            Assert-Equal $report.sampleCount 1
            Assert-Equal $report.samples.Count 1
            Assert-Equal $report.samples[0].checks.Count @($report.checks | Where-Object sample -EQ 1).Count
            $null = Assert-Check $report 'runtime.fresh' 'PASS'
            $null = Assert-Check $report 'automationErrors' 'PASS'
            $null = Assert-Check $report 'runtime.session' 'PASS' 0
            $check = Assert-Check $report 'runtime.progress' 'INCONCLUSIVE' 0
            Assert-Equal $check.required $false
            $null = Assert-Check $report 'saveSafety' 'PASS'
            $null = Assert-Check $report 'saveSafety.evidence' 'PASS'
        }
        Test-Case 'three progressing samples in one session pass required checks' {
            $one = New-Fixture; $two = New-Fixture; $three = New-Fixture
            $two.startedAt = $three.startedAt = $one.startedAt
            $two.runtimeRevision = 2; $three.runtimeRevision = 3
            $report = Invoke-Sequence @($one, $two, $three)
            Assert-Equal $report.success $true
            Assert-Equal $report.sampleCount 3
            Assert-Equal $report.samples.Count 3
            Assert-Equal @($report.samples | ForEach-Object { $_.checks.Count }) @(36, 36, 36)
            $check = Assert-Check $report 'runtime.progress' 'PASS' 0
            Assert-Equal $check.evidence.revisions @(1, 2, 3)
            $null = Assert-Check $report 'resource.EnergyNgu' 'PASS' 3
        }
        Test-Case 'a repeated revision between advancing snapshots is allowed' {
            $one = New-Fixture; $two = New-Fixture; $three = New-Fixture
            $two.startedAt = $three.startedAt = $one.startedAt
            $two.updatedAt = $one.updatedAt; $three.runtimeRevision = 2
            $report = Invoke-Sequence @($one, $two, $three)
            Assert-Equal $report.success $true
            $null = Assert-Check $report 'runtime.progress' 'PASS' 0
        }
        Test-Case 'frozen runtime revision is waiting even if timestamp is fresh' {
            $one = New-Fixture
            $report = Invoke-Sequence @($one, $one, $one)
            Assert-Equal $report.success $false
            $null = Assert-Check $report 'runtime.progress' 'WAITING' 0
            $null = Assert-Check $report 'resource.EnergyNgu' 'INCONCLUSIVE'
        }
        Test-Case 'cross-session samples fail and cannot certify equality' {
            $one = New-Fixture; $two = New-Fixture
            $two.startedAt = [datetimeoffset]::UtcNow.ToString('o'); $two.runtimeRevision = 2
            $report = Invoke-Sequence @($one, $two)
            Assert-Equal $report.success $false
            $null = Assert-Check $report 'runtime.session' 'FAIL' 0
            $null = Assert-Check $report 'gear.-1' 'INCONCLUSIVE'
        }
        Test-Case 'future session start cannot certify equality' {
            $data = New-Fixture; $data.startedAt = [datetimeoffset]::UtcNow.AddMinutes(1).ToString('o')
            $report = Invoke-Fixture $data
            $null = Assert-Check $report 'runtime.session' 'FAIL' 0
            Assert-Equal $report.success $false
        }
        Test-Case 'default sampling reads three snapshots at two-second boundaries' {
            $data = New-Fixture
            $state = @{ calls = 0 }
            Write-Fixture $data $caseDirectory
            function Start-Sleep([double]$Seconds) {
                Assert-Equal $Seconds ([double]2) 'default interval'
                $state.calls++; $data.runtimeRevision++
                Write-Fixture $data $caseDirectory
            }
            $report = & $diagnostics -RuntimeDir $caseDirectory
            Assert-Equal $state.calls 2
            Assert-Equal $report.sampleCount 3
            Assert-Equal $report.intervalSeconds ([double]2)
            Assert-Equal $report.success $true
        }
        Test-Case 'regressing revision fails' {
            $one = New-Fixture; $two = New-Fixture; $two.startedAt = $one.startedAt
            $one.runtimeRevision = 5; $two.runtimeRevision = 4
            $report = Invoke-Sequence @($one, $two)
            $null = Assert-Check $report 'runtime.progress' 'FAIL' 0
        }
        Test-Case 'changed timestamp without revision fails' {
            $one = New-Fixture; $two = New-Fixture; $two.startedAt = $one.startedAt
            $two.updatedAt = [datetimeoffset]::UtcNow.AddSeconds(1).ToString('o')
            $report = Invoke-Sequence @($one, $two)
            $null = Assert-Check $report 'runtime.progress' 'FAIL' 0
        }
        foreach ($field in @('updatedAt', 'startedAt', 'runtimeRevision')) {
            Test-Case "missing runtime $field cannot succeed" {
                $data = New-Fixture; $data.Remove($field)
                $report = Invoke-Fixture $data
                Assert-Equal $report.success $false
                if ($field -eq 'runtimeRevision') { $null = Assert-Check $report 'runtime.revision' 'INCONCLUSIVE' }
            }
        }
        foreach ($offset in @(-30, 30)) {
            Test-Case "timestamp outside freshness/skew window ($offset seconds) fails" {
                $data = New-Fixture; $data.updatedAt = [datetimeoffset]::UtcNow.AddSeconds($offset).ToString('o')
                $report = Invoke-Fixture $data
                Assert-Equal $report.success $false
                $null = Assert-Check $report 'runtime.fresh' 'FAIL'
                $null = Assert-Check $report 'saveSafety' 'INCONCLUSIVE'
            }
        }
        foreach ($status in @('starting', 'unloaded', 'failed')) {
            Test-Case "non-running runtime $status cannot certify equality" {
                $data = New-Fixture; $data.status = $status
                $report = Invoke-Fixture $data
                $expected = if ($status -eq 'starting') { 'WAITING' } else { 'FAIL' }
                $null = Assert-Check $report 'runtime.running' $expected
                $null = Assert-Check $report 'resource.EnergyNgu' 'INCONCLUSIVE'
                Assert-Equal $report.success $false
            }
        }
        Test-Case 'quiesced execution is waiting' {
            $data = New-Fixture; $data.execution.lifecycle = 'Quiesced'
            $report = Invoke-Fixture $data
            $null = Assert-Check $report 'runtime.running' 'WAITING'
            Assert-Equal $report.success $false
        }
        Test-Case 'injector exceptions fail even when every allocation is verified' {
            $data = New-Fixture
            $data.automationErrors = @(@{ context = 'CustomAdventureTick'; message = 'Optimal tower settings authority changed.'; count = 75045; details = 'Original stack trace' })
            $report = Invoke-Fixture $data
            $check = Assert-Check $report 'automationErrors' 'FAIL'
            Assert-Equal $check.required $true
            Assert-Equal $check.evidence $data.automationErrors 'unmodified error evidence'
            Assert-Equal $report.success $false
            $null = Assert-Check $report 'resource.EnergyNgu' 'PASS'
        }
        foreach ($value in @($null, @{}, '', $false)) {
            Test-Case "invalid injector error telemetry cannot certify zero errors: $value" {
                $data = New-Fixture; $data.automationErrors = $value
                $report = Invoke-Fixture $data
                $null = Assert-Check $report 'automationErrors' 'INCONCLUSIVE'
                Assert-Equal $report.success $false
            }
        }
        Test-Case 'missing injector error telemetry cannot certify zero errors' {
            $data = New-Fixture; $data.Remove('automationErrors')
            $report = Invoke-Fixture $data
            $null = Assert-Check $report 'automationErrors' 'INCONCLUSIVE'
            Assert-Equal $report.success $false
        }
        Test-Case 'missing runtime file returns waiting instead of throwing' {
            $report = & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0
            $null = Assert-Check $report 'runtime.read' 'WAITING'
            Assert-Equal $report.success $false
        }
        foreach ($json in @('{bad', '[]', 'null', '{}')) {
            Test-Case "malformed or incomplete runtime returns issues: $json" {
                [IO.File]::WriteAllText((Join-Path $caseDirectory 'runtime.json'), $json)
                $report = & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0
                Assert-Equal $report.success $false
                if ($json -ne '{}') { $null = Assert-Check $report 'runtime.read' 'FAIL' }
                else { $null = Assert-Check $report 'ngu.energy.counts' 'INCONCLUSIVE' }
            }
        }
        foreach ($field in @('execution', 'nguAllocation', 'manualAdventureOverlay', 'saveSafety')) {
            Test-Case "missing additive section $field produces a report, not an exception" {
                $data = New-Fixture; $data.Remove($field)
                $report = Invoke-Fixture $data
                Assert-Equal $report.success $false
                Assert-Equal $report.sampleCount 1
            }
        }
        Test-Case 'strict parent receives incomplete-schema issues without helper leaks' {
            [IO.File]::WriteAllText((Join-Path $caseDirectory 'runtime.json'), '{}')
            Set-StrictMode -Version Latest
            $report = . $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0
            Assert-Equal $report.success $false
            $null = Assert-Check $report 'ngu.energy.counts' 'INCONCLUSIVE'
            Assert-Throws { $null = $undefinedDiagnosticsStrictModeProbe } 'has not been set'
        }
        Test-Case 'ExpectedPlan identity is optional and case-insensitive' {
            $report = Invoke-Fixture (New-Fixture) @{ ExpectedPlan = 'fixture plan' }
            $check = Assert-Check $report 'plan.identity' 'PASS'
            Assert-Equal $check.evidence.revision 'fixture-revision'
            Assert-Equal $report.success $true
        }
        Test-Case 'wrong expected plan fails' {
            $report = Invoke-Fixture (New-Fixture) @{ ExpectedPlan = 'Wrong Plan' }
            $null = Assert-Check $report 'plan.identity' 'FAIL'
            Assert-Equal $report.success $false
        }
        Test-Case 'selected and observed plan disagreement fails without ExpectedPlan' {
            $data = New-Fixture; $data.planProgress.planName = 'Wrong Plan'
            $null = Assert-Check (Invoke-Fixture $data) 'plan.identity' 'FAIL'
        }
        Test-Case 'paused plan is uncontrolled unless ExpectedPlan is requested' {
            $data = New-Fixture; $data.settings.useCustomRebirthPlan = $false; $data.planProgress.status = 'paused'
            $null = Assert-Check (Invoke-Fixture $data) 'plan.identity' 'UNCONTROLLED'
            $report = Invoke-Fixture $data @{ ExpectedPlan = 'Fixture Plan' }
            $null = Assert-Check $report 'plan.identity' 'WAITING'
            Assert-Equal $report.success $false
        }
        Test-Case 'resource amounts preserve integers above JavaScript precision' {
            $report = Invoke-Fixture (New-Fixture)
            $check = Assert-Check $report 'resource.EnergyNgu' 'PASS'
            Assert-Equal $check.evidence.observed[0] '9007199254740993'
        }
        Test-Case 'one-unit resource mismatch above JavaScript precision fails' {
            $data = New-Fixture; $data.execution.resources[0].observed[0] = '9007199254740992'; $data.execution.resources[0].verified = $false
            $report = Invoke-Fixture $data
            $null = Assert-Check $report 'resource.EnergyNgu' 'FAIL'
            Assert-Equal $report.success $false
        }
        foreach ($value in @(9007199254740993L, '01', '1e3', '-1', '9223372036854775808', '1.0')) {
            Test-Case "noncanonical resource integer is not certified: $value ($($value.GetType().Name))" {
                $data = New-Fixture; $data.execution.resources[0].desired[0] = $value; $data.execution.resources[0].observed[0] = $value
                $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'INCONCLUSIVE'
            }
        }
        Test-Case 'long max resource strings remain exact' {
            $data = New-Fixture; $data.execution.resources[0].desired[0] = $data.execution.resources[0].observed[0] = '9223372036854775807'
            $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'PASS'
        }
        Test-Case 'backend verification false cannot pass equal resources' {
            $data = New-Fixture; $data.execution.resources[0].verified = $false
            $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'INCONCLUSIVE'
        }
        Test-Case 'controlled resource wait remains visible' {
            $data = New-Fixture; $data.execution.resources[0].verified = $false; $data.execution.domains.Energy.waitingReason = 'Resource boundary is busy.'
            $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'WAITING'
        }
        Test-Case 'uncontrolled resources are not certified by matching arrays' {
            $data = New-Fixture; $data.execution.domains.Energy.controlled = $false
            $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'UNCONTROLLED'
        }
        Test-Case 'string controlled flag is not interpreted as boolean' {
            $data = New-Fixture; $data.execution.domains.Energy.controlled = 'true'
            $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'INCONCLUSIVE'
        }
        Test-Case 'missing resource arrays cannot pass a controlled lane' {
            $data = New-Fixture; $data.execution.resources = @()
            $null = Assert-Check (Invoke-Fixture $data) 'resources.Energy' 'INCONCLUSIVE'
        }
        Test-Case 'unknown resource target is an inconclusive observation' {
            $data = New-Fixture; $data.execution.resources[0].target = 'FutureTarget'
            $null = Assert-Check (Invoke-Fixture $data) 'resource.FutureTarget' 'INCONCLUSIVE'
        }
        Test-Case 'resource array length mismatch cannot pass' {
            $data = New-Fixture; $data.execution.resources[0].observed = @('1')
            $null = Assert-Check (Invoke-Fixture $data) 'resource.EnergyNgu' 'INCONCLUSIVE'
        }
        Test-Case 'live NGU counts and totals come from nguAllocation' {
            $data = New-Fixture; $data.nguAllocation.energyWeightsApplied = $false
            $report = Invoke-Fixture $data
            $check = Assert-Check $report 'ngu.energy.counts' 'PASS'
            Assert-Equal $check.evidence.targetCount 9
            Assert-Equal $check.evidence.fundedCount 2
            Assert-Equal $check.evidence.totalAllocated '9007199254740994'
            $check = Assert-Check $report 'ngu.magic.counts' 'PASS'
            Assert-Equal $check.evidence.targetCount 7
            Assert-Equal $check.evidence.totalAllocated '18'
        }
        Test-Case 'funding a zero-weight actual NGU target fails independently of resources' {
            $data = New-Fixture; $data.nguAllocation.energyTargets[2].allocated = '1'
            $report = Invoke-Fixture $data
            $check = Assert-Check $report 'ngu.energy.exclusions' 'FAIL'
            Assert-Equal $check.evidence.violatingIndexes @(2)
            $null = Assert-Check $report 'resource.EnergyNgu' 'PASS'
            Assert-Equal $report.success $false
        }
        Test-Case 'stale caps and applied flags never prove weighted ratios' {
            $report = Invoke-Fixture (New-Fixture)
            foreach ($lane in @('energy', 'magic')) {
                $check = Assert-Check $report "ngu.$lane.distribution" 'INCONCLUSIVE'
                Assert-Equal $check.required $false
                Assert-Equal $check.evidence.capBasis 'last-allocation-pass'
            }
            Assert-Equal $report.success $true
        }
        Test-Case 'incomplete NGU rows are inconclusive, not a zero count pass' {
            $data = New-Fixture; $data.nguAllocation.energyTargets = @()
            $null = Assert-Check (Invoke-Fixture $data) 'ngu.energy.counts' 'INCONCLUSIVE'
        }
        Test-Case 'idle uses actual overlay lanes even when overlay is inactive' {
            $report = Invoke-Fixture (New-Fixture)
            $check = Assert-Check $report 'idle.Energy' 'PASS'
            Assert-Equal $check.evidence.overlayActive $false
            Assert-Equal $check.evidence.allocations[0] '9007199254740993'
            Assert-Equal $check.evidence.observedAllocationSum '9007199254740994'
        }
        Test-Case 'controlled nonzero actual idle is waiting regardless of resource verification' {
            $data = New-Fixture; $data.manualAdventureOverlay.idleEnergy = '6'
            $report = Invoke-Fixture $data
            $check = Assert-Check $report 'idle.Energy' 'WAITING'
            Assert-Equal $check.evidence.idle '6'
            Assert-Equal $report.success $false
        }
        Test-Case 'uncontrolled actual idle remains uncontrolled' {
            $data = New-Fixture; $data.manualAdventureOverlay.idleMagic = '1'; $data.execution.domains.Magic.controlled = $false
            $null = Assert-Check (Invoke-Fixture $data) 'idle.Magic' 'UNCONTROLLED'
        }
        Test-Case 'unavailable overlay does not imply zero idle' {
            $data = New-Fixture; $data.manualAdventureOverlay.available = $false
            $null = Assert-Check (Invoke-Fixture $data) 'idle.Energy' 'INCONCLUSIVE'
        }
        Test-Case 'equal gear IDs require backend instance verification' {
            $data = New-Fixture; $data.execution.gear[0].verified = $false
            $null = Assert-Check (Invoke-Fixture $data) 'gear.-1' 'INCONCLUSIVE'
        }
        Test-Case 'gear ID mismatch contradicting verified backend flag fails' {
            $data = New-Fixture; $data.execution.gear[0].observed = 999
            $null = Assert-Check (Invoke-Fixture $data) 'gear.-1' 'FAIL'
        }
        Test-Case 'missing native base gear slots cannot succeed' {
            $data = New-Fixture; $data.execution.gear = @($data.execution.gear[0])
            $null = Assert-Check (Invoke-Fixture $data) 'gear' 'INCONCLUSIVE'
        }
        Test-Case 'native support exposes exact beard IDs and digger levels' {
            $report = Invoke-Fixture (New-Fixture)
            $check = Assert-Check $report 'support.beards' 'PASS'
            Assert-Equal $check.evidence.observed @(3, 6)
            $check = Assert-Check $report 'support.diggers' 'PASS'
            Assert-Equal @($check.evidence.observed | ForEach-Object level) @('2', '3')
            $null = Assert-Check $report 'support.affordability' 'PASS'
        }
        Test-Case 'changed native digger level fails equality' {
            $data = New-Fixture; $data.execution.support.observed = '3,6|4:2,5:4'; $data.execution.support.verified = $false
            $null = Assert-Check (Invoke-Fixture $data) 'support.diggers' 'FAIL'
        }
        Test-Case 'unaffordable support is visibly waiting' {
            $data = New-Fixture; $data.execution.support.desiredFeasible = $false; $data.execution.support.reason = 'Digger drain exceeds gross GPS.'
            $report = Invoke-Fixture $data
            $check = Assert-Check $report 'support.affordability' 'WAITING'
            Assert-Equal $check.evidence.reason 'Digger drain exceeds gross GPS.'
            Assert-Equal $report.success $false
        }
        Test-Case 'missing affordability cannot pass based on support equality alone' {
            $data = New-Fixture; $data.execution.support.Remove('desiredFeasible')
            $null = Assert-Check (Invoke-Fixture $data) 'support.affordability' 'INCONCLUSIVE'
        }
        foreach ($key in @('3,3|4:2', '8|4:2', '3|4:0', '3|4:2,4:3', '3|4:1e3')) {
            Test-Case "malformed native support cannot pass: $key" {
                $data = New-Fixture; $data.execution.support.desired = $data.execution.support.observed = $key
                $null = Assert-Check (Invoke-Fixture $data) 'support.equality' 'INCONCLUSIVE'
            }
        }
        Test-Case 'controlled empty support is different from uncontrolled' {
            $data = New-Fixture; $data.execution.support.desired = $data.execution.support.observed = '|'
            $report = Invoke-Fixture $data
            $check = Assert-Check $report 'support.beards' 'PASS'
            Assert-Equal $check.evidence.observed @()
            $data.execution.domains.Support.controlled = $false; $data.execution.support.desired = 'uncontrolled'
            $null = Assert-Check (Invoke-Fixture $data) 'support.equality' 'UNCONTROLLED'
        }
        Test-Case 'native AT power and toughness indexes are not reversed' {
            $report = Invoke-Fixture (New-Fixture)
            $check = Assert-Check $report 'native.atPower' 'PASS'; Assert-Equal $check.evidence.observed '200'
            $check = Assert-Check $report 'native.atToughness' 'PASS'; Assert-Equal $check.evidence.observed '100'
        }
        Test-Case 'native OS mismatch fails' {
            $data = New-Fixture; $data.execution.nativeSettings.os = 2
            $null = Assert-Check (Invoke-Fixture $data) 'native.os' 'FAIL'
        }
        foreach ($state in @('behind', 'unknown', 'testing', 'recoveryRequired')) {
            Test-Case "save safety $state is visible and prevents success" {
                $data = New-Fixture; $data.saveSafety.status = $state; $data.saveSafety.message = "Fixture $state"
                $expected = switch ($state) { 'unknown' { 'INCONCLUSIVE' }; 'testing' { 'WAITING' }; default { 'FAIL' } }
                $report = Invoke-Fixture $data
                $check = Assert-Check $report 'saveSafety' $expected
                Assert-Equal $check.evidence.message "Fixture $state"
                Assert-Equal $report.success $false
            }
        }
        Test-Case 'save testActive overrides a nominally verified status' {
            $data = New-Fixture; $data.saveSafety.testActive = $true
            $null = Assert-Check (Invoke-Fixture $data) 'saveSafety' 'WAITING'
        }
        Test-Case 'incomplete save scan is waiting' {
            $data = New-Fixture; $data.saveSafety.scanComplete = $false
            $null = Assert-Check (Invoke-Fixture $data) 'saveSafety' 'WAITING'
        }
        Test-Case 'save scan errors fail even if status says verified' {
            $data = New-Fixture; $data.saveSafety.errors = @('Unreadable save.')
            $null = Assert-Check (Invoke-Fixture $data) 'saveSafety' 'FAIL'
        }
        Test-Case 'verified save progress behind longest is contradictory' {
            $data = New-Fixture; $data.saveSafety.current.totalPlaytimeSeconds = '999.5'
            $null = Assert-Check (Invoke-Fixture $data) 'saveSafety.evidence' 'FAIL'
        }
        Test-Case 'save rebirth clock cannot exceed total playtime' {
            $data = New-Fixture; $data.saveSafety.current.rebirthSeconds = '2000'
            $null = Assert-Check (Invoke-Fixture $data) 'saveSafety.evidence' 'FAIL'
        }
        foreach ($field in @('sha256', 'path', 'rebirthSeconds', 'totalPlaytimeSeconds')) {
            Test-Case "verified save evidence missing $field cannot pass" {
                $data = New-Fixture; $data.saveSafety.longest.Remove($field)
                $null = Assert-Check (Invoke-Fixture $data) 'saveSafety.evidence' 'FAIL'
            }
        }
        Test-Case 'save progress numbers must be strings' {
            $data = New-Fixture; $data.saveSafety.current.totalPlaytimeSeconds = 1001
            $null = Assert-Check (Invoke-Fixture $data) 'saveSafety.evidence' 'FAIL'
        }
        Test-Case 'missing save safety is inconclusive, not silently verified' {
            $data = New-Fixture; $data.Remove('saveSafety')
            $report = Invoke-Fixture $data
            $null = Assert-Check $report 'saveSafety' 'INCONCLUSIVE'
            Assert-Equal $report.success $false
        }
        foreach ($state in @('behind', 'testing')) {
            Test-Case "exact expected test session accepts backend paused-scan $state while retaining raw warning evidence" {
                $data = New-TestFixture $state
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $true
                Assert-Equal $report.expectedTestSessionId $expectedTestSessionId
                Assert-Equal $report.requiredIssueCount 0
                Assert-Equal $report.counts.EXPECTED 1
                $check = Assert-Check $report 'saveTestSession' 'PASS'
                Assert-Equal $check.required $true
                Assert-Equal $check.evidence.session $data.saveTestSession
                Assert-Equal $check.evidence.fixtureFence.generation '9007199254740993'
                Assert-Equal $check.evidence.productionScanStatus 'PAUSED'
                $check = Assert-Check $report 'saveSafety' 'EXPECTED'
                Assert-Equal $check.required $true
                Assert-Equal $check.evidence $data.saveSafety
                Assert-Equal $check.evidence.scanComplete $false
                Assert-Equal $check.evidence.pendingFiles 0
                Assert-Equal $check.evidence.knownFiles 1
                Assert-Equal ($check.message -match 'scan is PAUSED.*not complete') $true 'paused scan is not reported as complete'
                Assert-Equal @($report.issues | Where-Object id -CEQ 'saveSafety').Count 1 'raw warning remains visible'
                $null = Assert-Check $report 'saveSafety.evidence' 'PASS'
                foreach ($id in @('resource.EnergyNgu', 'gear.-1', 'support.equality', 'native.os')) {
                    $null = Assert-Check $report $id 'PASS'
                }
            }
            Test-Case "exact expected test session also accepts $state with a completed scan" {
                $data = New-TestFixture $state; $data.saveSafety.scanComplete = $true
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $true
                $null = Assert-Check $report 'saveTestSession' 'PASS'
                $check = Assert-Check $report 'saveSafety' 'EXPECTED'
                Assert-Equal $check.evidence.scanComplete $true
                Assert-Equal ($check.message -match 'PAUSED') $false
            }
            Test-Case "paused production scan cannot waive $state scan errors" {
                $data = New-TestFixture $state; $data.saveSafety.errors = @('Unreadable protected save.')
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $check = Assert-Check $report 'saveTestSession' 'FAIL'
                Assert-Equal ($check.message -match 'scan errors cannot be waived') $true
                $check = Assert-Check $report 'saveSafety' 'FAIL'
                Assert-Equal $check.evidence.scanComplete $false
                Assert-Equal $check.evidence.errors @('Unreadable protected save.')
                Assert-Equal @($report.checks | Where-Object status -CEQ 'EXPECTED').Count 0
            }
            Test-Case "test context without ExpectedTestSessionId preserves ordinary $state alarm" {
                $report = Invoke-Fixture (New-TestFixture $state)
                Assert-Equal $report.success $false
                $expected = if ($state -eq 'behind') { 'FAIL' } else { 'WAITING' }
                $null = Assert-Check $report 'saveSafety' $expected
                Assert-Equal @($report.checks | Where-Object id -CEQ 'saveTestSession').Count 0
                Assert-Equal $report.counts.Contains('EXPECTED') $false
            }
        }
        foreach ($state in @('verified', 'unknown')) {
            Test-Case "expected session cannot waive an incomplete scan for $state status" {
                $report = Invoke-Fixture (New-TestFixture $state) @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $check = Assert-Check $report 'saveTestSession' 'FAIL'
                Assert-Equal $check.message 'Save-safety scan must be complete outside an expected behind/testing fixture warning.'
                Assert-Equal @($report.checks | Where-Object status -CEQ 'EXPECTED').Count 0
            }
        }
        foreach ($generation in @('0', '9223372036854775807')) {
            Test-Case "expected fixture generation accepts nonnegative Int64 boundary $generation" {
                $data = New-TestFixture; $data.saveTestSession.fixtureGeneration = $generation
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $true
                $check = Assert-Check $report 'saveTestSession' 'PASS'
                Assert-Equal $check.evidence.fixtureFence.generation $generation
            }
        }
        foreach ($id in @('', 'not-hex', ('a' * 31), ('a' * 33), '01234567-89ab-cdef-0123-456789abcdef', ($expectedTestSessionId + "`n"))) {
            Test-Case "ExpectedTestSessionId rejects invalid format [$id]" {
                Assert-Throws { Invoke-Fixture (New-TestFixture) @{ ExpectedTestSessionId = $id } } 'ExpectedTestSessionId'
            }
        }
        Test-Case 'matching uppercase hexadecimal session ID is accepted without normalization' {
            $data = New-TestFixture; $data.saveTestSession.sessionId = $expectedTestSessionId.ToUpperInvariant()
            $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $data.saveTestSession.sessionId }
            Assert-Equal $report.success $true
        }
        Test-Case 'stable expected fixture across three advancing samples passes' {
            $one = New-TestFixture; $two = New-TestFixture; $three = New-TestFixture
            $two.startedAt = $three.startedAt = $one.startedAt
            $two.runtimeRevision = 2; $three.runtimeRevision = 3
            $report = Invoke-Sequence @($one, $two, $three) @{ ExpectedTestSessionId = $expectedTestSessionId }
            Assert-Equal $report.success $true
            Assert-Equal $report.counts.EXPECTED 3
            foreach ($sample in 1..3) { $null = Assert-Check $report 'saveTestSession' 'PASS' $sample }
        }
        foreach ($field in @('fixtureGeneration', 'sha256', 'sessionId', 'saveTestSession')) {
            Test-Case "changing $field in a middle sample fails even if the original fixture returns" {
                $one = New-TestFixture; $two = New-TestFixture; $three = New-TestFixture
                $two.startedAt = $three.startedAt = $one.startedAt
                $two.runtimeRevision = 2; $three.runtimeRevision = 3
                switch ($field) {
                    'fixtureGeneration' { $two.saveTestSession.fixtureGeneration = '9007199254740994' }
                    'sha256' { $two.saveTestSession.fixture.sha256 = 'b' * 64 }
                    'sessionId' { $two.saveTestSession.sessionId = 'f' * 32 }
                    'saveTestSession' { $two.Remove('saveTestSession') }
                }
                $report = Invoke-Sequence @($one, $two, $three) @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $check = Assert-Check $report 'saveTestSession' 'FAIL' 2
                Assert-Equal $check.required $true
                $null = Assert-Check $report 'saveSafety' 'FAIL' 2
                if ($field -in @('fixtureGeneration', 'sha256')) {
                    Assert-Equal $check.message 'Fixture generation or SHA-256 changed across diagnostics samples.'
                    Assert-Equal $check.evidence.fixtureFence.generation $one.saveTestSession.fixtureGeneration
                    Assert-Equal $check.evidence.fixtureFence.sha256 $one.saveTestSession.fixture.sha256
                }
            }
        }
        $invalidContexts = @(
            @{ name = 'missing context'; change = { $data.Remove('saveTestSession') }; reason = 'missing' },
            @{ name = 'wrong ID'; change = { $data.saveTestSession.sessionId = 'f' * 32 }; reason = 'exactly match' },
            @{ name = 'differently cased ID'; change = { $data.saveTestSession.sessionId = $expectedTestSessionId.ToUpperInvariant() }; reason = 'exactly match' },
            @{ name = 'unsupported version'; change = { $data.saveTestSession.version = 2 }; reason = 'version-1' },
            @{ name = 'string version'; change = { $data.saveTestSession.version = '1' }; reason = 'version-1' },
            @{ name = 'unsupported capability'; change = { $data.saveTestSession.supported = $false }; reason = 'supported' },
            @{ name = 'string capability'; change = { $data.saveTestSession.supported = 'true' }; reason = 'supported' },
            @{ name = 'numeric generation'; change = { $data.saveTestSession.fixtureGeneration = 1 }; reason = 'Int64' },
            @{ name = 'noncanonical generation'; change = { $data.saveTestSession.fixtureGeneration = '01' }; reason = 'Int64' },
            @{ name = 'overflow generation'; change = { $data.saveTestSession.fixtureGeneration = '9223372036854775808' }; reason = 'Int64' },
            @{ name = 'missing fixture hash'; change = { $data.saveTestSession.fixture.Remove('sha256') }; reason = 'SHA-256' },
            @{ name = 'invalid fixture hash'; change = { $data.saveTestSession.fixture.sha256 = 'not-a-hash' }; reason = 'SHA-256' },
            @{ name = 'missing fixture name'; change = { $data.saveTestSession.fixture.Remove('name') }; reason = 'name' },
            @{ name = 'unverified reset'; change = { $data.saveTestSession.resetEvidence.verified = $false }; reason = 'resetEvidence' },
            @{ name = 'string reset verification'; change = { $data.saveTestSession.resetEvidence.verified = 'true' }; reason = 'resetEvidence' },
            @{ name = 'missing reset'; change = { $data.saveTestSession.Remove('resetEvidence') }; reason = 'resetEvidence' },
            @{ name = 'missing restore target'; change = { $data.saveTestSession.Remove('restoreTarget') }; reason = 'restoreTarget' },
            @{ name = 'invalid restore hash'; change = { $data.saveTestSession.restoreTarget.sha256 = 'bad' }; reason = 'restoreTarget' },
            @{ name = 'invalid decoded restore identity'; change = { $data.saveTestSession.restoreTarget.Remove('playerName') }; reason = 'restoreTarget' },
            @{ name = 'invalid restore clock'; change = { $data.saveTestSession.restoreTarget.totalPlaytimeSeconds = 'NaN' }; reason = 'restoreTarget' },
            @{ name = 'contradictory restore clocks'; change = { $data.saveTestSession.restoreTarget.rebirthSeconds = '2000' }; reason = 'restoreTarget' },
            @{ name = 'inactive test'; change = { $data.saveSafety.testActive = $false }; reason = 'testActive' },
            @{ name = 'save recovery required'; change = { $data.saveSafety.status = 'recoveryRequired' }; reason = 'recovery' },
            @{ name = 'save scan errors'; change = { $data.saveSafety.errors = @('Unreadable protected save.') }; reason = 'scan errors' },
            @{ name = 'nonboolean scan completion'; change = { $data.saveSafety.scanComplete = 'false' }; reason = 'metadata' },
            @{ name = 'missing scan completion'; change = { $data.saveSafety.Remove('scanComplete') }; reason = 'metadata' }
        )
        foreach ($context in $invalidContexts) {
            Test-Case "expected session fails closed for $($context.name)" {
                $data = New-TestFixture; & $context.change
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $check = Assert-Check $report 'saveTestSession' 'FAIL'
                Assert-Equal ($check.message -match $context.reason) $true 'specific fence failure reason'
                Assert-Equal $check.required $true
                Assert-Equal @($report.checks | Where-Object status -CEQ 'EXPECTED').Count 0
            }
        }
        foreach ($phase in @('idle', 'starting', 'loadingFixture', 'restoring', 'restored', 'recoveryRequired', 'Running')) {
            Test-Case "expected session rejects non-running phase $phase" {
                $data = New-TestFixture; $data.saveTestSession.phase = $phase
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $check = Assert-Check $report 'saveTestSession' 'FAIL'
                Assert-Equal ($check.message -match 'phase must be running') $true
            }
        }
        foreach ($field in @('saveSafety', 'current', 'sha256', 'rebirthSeconds', 'totalPlaytimeSeconds')) {
            Test-Case "expected session cannot waive missing save metadata $field" {
                $data = New-TestFixture 'testing'
                switch ($field) {
                    'saveSafety' { $data.Remove('saveSafety') }
                    'current' { $data.saveSafety.Remove('current') }
                    default { $data.saveSafety.longest.Remove($field) }
                }
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $null = Assert-Check $report 'saveTestSession' 'FAIL'
                $null = Assert-Check $report 'saveSafety.evidence' 'FAIL'
            }
        }
        foreach ($json in @($null, '{bad', '[]')) {
            Test-Case "expected session requires readable object JSON in every sample [$json]" {
                if ($null -ne $json) { [IO.File]::WriteAllText((Join-Path $caseDirectory 'runtime.json'), $json) }
                $report = & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -ExpectedTestSessionId $expectedTestSessionId
                Assert-Equal $report.success $false
                $check = Assert-Check $report 'saveTestSession' 'FAIL'
                Assert-Equal ($check.message -match 'not read as an object') $true
            }
        }
        Test-Case 'expected warning cannot certify a stale runtime' {
            $data = New-TestFixture; $data.updatedAt = [datetimeoffset]::UtcNow.AddMinutes(-1).ToString('o')
            $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
            Assert-Equal $report.success $false
            $null = Assert-Check $report 'runtime.fresh' 'FAIL'
            $null = Assert-Check $report 'saveTestSession' 'INCONCLUSIVE'
            $null = Assert-Check $report 'saveSafety' 'INCONCLUSIVE'
        }
        foreach ($domain in @('resources', 'gear', 'support')) {
            Test-Case "expected save warning does not waive $domain failures" {
                $data = New-TestFixture
                $id = switch ($domain) {
                    'resources' { $data.execution.resources[0].observed[0] = '1'; 'resource.EnergyNgu' }
                    'gear' { $data.execution.gear[0].observed = 999; 'gear.-1' }
                    'support' { $data.execution.support.observed = '3,6|4:2,5:4'; 'support.equality' }
                }
                $report = Invoke-Fixture $data @{ ExpectedTestSessionId = $expectedTestSessionId }
                Assert-Equal $report.success $false
                $null = Assert-Check $report 'saveSafety' 'EXPECTED'
                $null = Assert-Check $report $id 'FAIL'
            }
        }
        Test-Case 'dot invocation returns without exiting or leaking helper preferences' {
            Write-Fixture (New-Fixture) $caseDirectory
            $ErrorActionPreference = 'Continue'
            $report = . $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0
            $reached = $true
            Assert-Equal $reached $true
            Assert-Equal $ErrorActionPreference 'Continue'
            Assert-Equal $report.success $true
            Assert-Equal (Get-Command Add-Check -ErrorAction SilentlyContinue) $null
        }
        Test-Case 'runtime directory and game-file sentinels remain byte-for-byte unchanged' {
            Write-Fixture (New-Fixture) $caseDirectory
            $null = [IO.Directory]::CreateDirectory((Join-Path $caseDirectory 'commands'))
            [IO.File]::WriteAllText((Join-Path $caseDirectory 'commands/existing.json'), '{"command":"do-not-touch"}')
            [IO.File]::WriteAllText((Join-Path $caseDirectory 'settings.json'), '{"sentinel":true}')
            [IO.File]::WriteAllText((Join-Path $caseDirectory 'save.txt'), 'do not touch')
            function Capture-Files {
                @([IO.Directory]::GetFiles($caseDirectory, '*', [IO.SearchOption]::AllDirectories) | Sort-Object | ForEach-Object {
                    [pscustomobject]@{ path = $_; bytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($_)); written = [IO.File]::GetLastWriteTimeUtc($_).ToString('o') }
                })
            }
            $before = Capture-Files
            $report = & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0
            Assert-Equal $report.success $true
            Assert-Equal (Capture-Files) $before
            Write-Fixture (New-TestFixture) $caseDirectory
            $before = Capture-Files
            $report = & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -IntervalSeconds 0 -ExpectedTestSessionId $expectedTestSessionId
            Assert-Equal $report.success $true
            Assert-Equal $report.readOnly $true
            Assert-Equal (Capture-Files) $before
        }
        Test-Case 'optional report serializes the same report outside runtime directory' {
            $path = Join-Path $root 'report.json'
            $report = Invoke-Fixture (New-Fixture) @{ ReportPath = $path }
            $saved = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($path))
            Assert-Equal $saved.success $report.success
            Assert-Equal ([datetimeoffset]$saved.generatedAt).ToUniversalTime().ToString('o') ([datetimeoffset]$report.generatedAt).ToUniversalTime().ToString('o')
            Assert-Equal $saved.checks.Count $report.checks.Count
            Assert-Equal $saved.samples[0].checks.Count $report.samples[0].checks.Count
        }
        Test-Case 'ReportPath cannot overwrite game queue or runtime files' {
            Write-Fixture (New-Fixture) $caseDirectory
            foreach ($path in @((Join-Path $caseDirectory 'runtime.json'), (Join-Path $caseDirectory 'settings.json'))) {
                Assert-Throws { & $diagnostics -RuntimeDir $caseDirectory -Samples 1 -ReportPath $path } 'outside RuntimeDir'
            }
        }
        Test-Case 'report refuses to overwrite an existing external file' {
            $path = Join-Path $root 'existing.json'; [IO.File]::WriteAllText($path, 'unchanged')
            Assert-Throws { Invoke-Fixture (New-Fixture) @{ ReportPath = $path } } 'already exists'
            Assert-Equal ([IO.File]::ReadAllText($path)) 'unchanged'
        }
        Test-Case 'missing report parent is an infrastructure error' {
            $path = Join-Path $root 'missing/report.json'
            Assert-Throws { Invoke-Fixture (New-Fixture) @{ ReportPath = $path } } 'Report parent does not exist'
        }
        Test-Case 'missing runtime directory is an infrastructure error' {
            Assert-Throws { & $diagnostics -RuntimeDir (Join-Path $root 'missing') -Samples 1 } 'Runtime directory does not exist'
        }
        $failures = @($results | Where-Object success -EQ $false)
        if ($failures.Count -gt 0) {
            throw "$($failures.Count)/$($results.Count) diagnostics checks failed:`n$((@($failures | ForEach-Object { $_.name + ': ' + $_.error })) -join "`n")"
        }
        [pscustomobject]@{ success = $true; passed = $results.Count; failed = 0; cases = $results.ToArray(); liveActions = 0 }
    } finally {
        if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root, $true) }
    }
}
