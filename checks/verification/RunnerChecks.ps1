#requires -Version 7.0
<#
Build-free orchestration regressions. Real git inputs, reports, mutexes and child
processes; only the external tool boundary is faked. Never calls the production
mutex, real build tools, game commands, or a recursive full verification run.
Evidence is retained under the printed temporary directory, including failures.
#>
[CmdletBinding()]
param([string]$CaseFilter = '*', [string]$EvidenceParent = (Join-Path $env:LOCALAPPDATA 'Temp/opencode'))

& {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Off
    $filter = $CaseFilter
    if (-not (Test-Path -LiteralPath $EvidenceParent -PathType Container)) { throw 'Evidence parent must already exist.' }
    $root = Join-Path $EvidenceParent ('ngu-runner-checks-' + [guid]::NewGuid().ToString('N'))
    $null = [IO.Directory]::CreateDirectory($root)
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $fixture = Join-Path $PSScriptRoot 'fixtures/RunnerProcess.ps1'
    . (Join-Path $repo 'verify.ps1') -FunctionsOnly -ReportParent (Join-Path $root 'unused-import-reports')
    $results = [Collections.Generic.List[object]]::new()
    $counts = @{ assertions = 0 }
    $oldState = $env:NGU_RUNNER_CHECK_STATE
    $oldProgramFiles = ${env:ProgramFiles(x86)}
    # Native validates tool existence before reaching the fake process boundary.
    ${env:ProgramFiles(x86)} = Join-Path $root 'synthetic-program-files'
    $fakeMSBuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe'
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $fakeMSBuild))
    [IO.File]::WriteAllText($fakeMSBuild, 'Not executable; process boundary is mandatory.')

    function Assert($Condition, [string]$Label) {
        $counts.assertions++
        if (-not $Condition) { throw $Label }
    }
    function Equal($Actual, $Expected, [string]$Label) {
        $a = ConvertTo-Json -InputObject $Actual -Depth 50 -Compress
        $e = ConvertTo-Json -InputObject $Expected -Depth 50 -Compress
        Assert ($a -ceq $e) "$Label expected $e; got $a"
    }
    function Throws([scriptblock]$Action, [string]$Pattern) {
        $message = $null
        try { $null = & $Action } catch { $message = $_.Exception.Message }
        Assert ($message -and $message -match $Pattern) "Expected exception '$Pattern'; got '$message'"
    }
    function Case([string]$Name, [scriptblock]$Action) {
        if ($Name -notlike $filter) { return }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        try {
            $null = & $Action
            $results.Add(@{ name = $Name; status = 'PASS'; seconds = $timer.Elapsed.TotalSeconds })
            "PASS: $Name"
        } catch {
            $results.Add(@{ name = $Name; status = 'FAIL'; seconds = $timer.Elapsed.TotalSeconds; error = $_.Exception.Message; location = $_.ScriptStackTrace })
            "FAIL: ${Name}: $($_.Exception.Message)"
        }
    }
    function New-Sandbox([string]$Name, [switch]$NoGit, [switch]$Empty) {
        $directory = Join-Path $root $Name
        $sandboxRepo = Join-Path $directory "repo with ' quote"
        foreach ($relative in @('', 'NGUInjector', 'NGUInjector.UI/node_modules', 'checks', 'game/NGUIdle_Data/Managed')) {
            $null = [IO.Directory]::CreateDirectory((Join-Path $sandboxRepo $relative))
        }
        if (-not $Empty) { [IO.File]::WriteAllText((Join-Path $sandboxRepo 'input.cs'), '// synthetic verification input') }
        if (-not $NoGit) {
            $null = & git -C $sandboxRepo init --quiet 2>&1
            if ($LASTEXITCODE -ne 0) { throw 'Synthetic git initialization failed.' }
        }
        $state = @{ repo = $sandboxRepo; trace = (Join-Path $directory 'tool-children.jsonl'); failPattern = ''; mutatePattern = ''; deleteInput = $false }
        $statePath = Join-Path $directory 'boundary-state.json'
        Write-TestAllJson $statePath $state
        $env:NGU_RUNNER_CHECK_STATE = $statePath
        return @{ directory = $directory; repo = $sandboxRepo; state = $state; statePath = $statePath
            options = @{ Repo = $sandboxRepo; GameFolder = (Join-Path $sandboxRepo 'game'); ReportParent = (Join-Path $directory 'reports')
                MutexName = ('Local\NGU-RunnerChecks-' + [guid]::NewGuid().ToString('N')); NativeProcessBoundary = $fixture } }
    }
    function Configure($Sandbox, [string]$Fail = '', [string]$Mutate = '', [switch]$DeleteInput) {
        $Sandbox.state.failPattern = $Fail; $Sandbox.state.mutatePattern = $Mutate; $Sandbox.state.deleteInput = [bool]$DeleteInput
        Write-TestAllJson $Sandbox.statePath $Sandbox.state
    }
    function Calls($Sandbox) {
        if ([IO.File]::Exists($Sandbox.state.trace)) {
            foreach ($line in [IO.File]::ReadAllLines($Sandbox.state.trace)) { ConvertFrom-Json -InputObject $line -AsHashtable }
        }
    }
    function Check-Report($Report, [string]$Status, [int]$ExitCode, [string]$ProcessBoundary = $fixture) {
        Equal $Report.schema 'ngu.verify.offline' 'report schema'
        Equal $Report.status $Status 'report status'
        Equal $Report.exitCode $ExitCode 'report exitCode'
        Equal $Report.success ($ExitCode -eq 0) 'report success'
        Assert ([bool]$Report.completedAt) 'report completed timestamp missing'
        Equal @($Report.commands).Count 0 'offline run published game commands'
        $saved = Read-TestAllJson (Join-Path $Report.reportDirectory 'report.json')
        Equal $saved.status $Status 'durable report status'
        Equal $saved.exitCode $ExitCode 'durable report exitCode'
        Equal @($saved.commands).Count 0 'durable report game commands'
        foreach ($phase in $Report.phases) {
            Assert ($phase.status -in @('PASS', 'FAIL', 'BLOCKED') -and $phase.completedAt) "Incomplete phase $($phase.name)"
            Assert ([IO.File]::Exists($phase.evidence.log)) "Missing phase log $($phase.name)"
            Equal $phase.evidence.processBoundary $ProcessBoundary 'tool escaped fake process boundary'
        }
    }
    function Start-Fixture([string[]]$Arguments) {
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
        $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $fixture) + $Arguments) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($start)
        return $process
    }

    try {
        Case 'FunctionsOnly imports perform no actions or file writes' {
            $directory = Join-Path $root 'import-check'
            $null = [IO.Directory]::CreateDirectory($directory)
            $process = Start-Fixture @('-Mode', 'Import', '-Repo', $repo, '-Directory', $directory)
            try {
                Assert ($process.WaitForExit(15000)) 'Import child timed out'
                $stdout = $process.StandardOutput.ReadToEnd(); $stderr = $process.StandardError.ReadToEnd()
                [IO.File]::WriteAllText((Join-Path $root 'import.stdout.log'), $stdout)
                [IO.File]::WriteAllText((Join-Path $root 'import.stderr.log'), $stderr)
                Equal $process.ExitCode 0 "Import child failed: $stderr"
                Assert ((ConvertFrom-Json $stdout).imported) 'Import child did not verify exports'
                Equal @([IO.Directory]::EnumerateFileSystemEntries($directory)).Count 0 'Import created files'
            } finally { if (-not $process.HasExited) { $process.Kill($true) }; $process.Dispose() }
        }

        Case 'Launcher acceptance locks normalized shared paths but permits independent captures' {
            $s = New-Sandbox 'launcher-paths'; $options = $s.options
            $launcher = Join-Path $s.repo 'start-ngu-and-script.ps1'
            $source = [IO.File]::ReadAllText((Join-Path $repo 'start-ngu-and-script.ps1'))
            Assert ($source.Contains('Local\NGUInjector-Executor-Offline-Verification')) 'Launcher mutex isolation anchor missing'
            [IO.File]::WriteAllText($launcher, $source.Replace('Local\NGUInjector-Executor-Offline-Verification', $options.MutexName))
            [IO.File]::WriteAllText((Join-Path $s.repo 'verify.ps1'), @'
param($Scope, $GameFolder, $DeferReason, $HandoffNote)
[IO.File]::AppendAllText((Join-Path $PSScriptRoot 'deferred.txt'), $DeferReason + "`n")
'@)
            [IO.File]::WriteAllText((Join-Path $options.GameFolder 'NGUIdle.exe'), 'Not executable.')
            foreach ($relative in @('NGUInjector/bin/Release/dist/injector', 'NGUInjector/bin/Debug/dist/injector', 'NGUInjector/bin-capture/injector', 'captured/injector')) {
                $path = Join-Path $s.repo $relative
                $null = [IO.Directory]::CreateDirectory($path)
                foreach ($name in @('smi.exe', 'NGUInjector.dll')) { [IO.File]::WriteAllText((Join-Path $path $name), 'Not executable.') }
            }
            $profile = Join-Path $s.directory 'profile'
            $null = [IO.Directory]::CreateDirectory((Join-Path $profile 'Desktop/NGUInjector'))
            $process = Start-Fixture @('-Mode', 'Mutex', '-MutexName', $options.MutexName, '-Directory', $s.directory)
            try {
                $clock = [Diagnostics.Stopwatch]::StartNew()
                while (-not [IO.File]::Exists((Join-Path $s.directory 'ready')) -and -not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 20 }
                Assert ([IO.File]::Exists((Join-Path $s.directory 'ready'))) 'Launcher mutex holder did not signal readiness'
                $rows = @(
                    @{ path = ''; shared = $true },
                    @{ path = (Join-Path $s.repo 'NGUInjector/bin/Release/dist/../dist/INJECTOR/'); shared = $true },
                    @{ path = 'NGUInjector/bin/Release/dist/../dist/injector/'; shared = $true },
                    @{ path = (Join-Path $s.repo 'NGUInjector/bin/Debug/dist/injector'); shared = $true },
                    @{ path = (Join-Path $s.repo 'captured/injector'); shared = $false },
                    @{ path = (Join-Path $s.repo 'NGUInjector/bin-capture/injector'); shared = $false }
                )
                foreach ($row in $rows) {
                    $observed = @{ processQueries = 0; starts = 0; stops = 0; message = '' }
                    & {
                        function Test-Path {
                            param([string]$LiteralPath)
                            if ($LiteralPath -like '*Microsoft Visual Studio*MSBuild.exe') { return $true }
                            Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
                        }
                        function Get-Process { $observed.processQueries++; throw 'Synthetic no-game boundary reached.' }
                        function Start-Process { $observed.starts++; throw 'Launcher escaped the no-game boundary.' }
                        function Stop-Process { $observed.stops++; throw 'Launcher escaped the no-game boundary.' }
                        $previousProfile = $env:USERPROFILE
                        $previousDirectory = [Environment]::CurrentDirectory
                        $env:USERPROFILE = $profile
                        [Environment]::CurrentDirectory = $s.repo
                        try { $null = & $launcher -GameFolder $options.GameFolder -NoBuild -InjectorDirectory $row.path }
                        catch { $observed.message = $_.Exception.Message }
                        finally { $env:USERPROFILE = $previousProfile; [Environment]::CurrentDirectory = $previousDirectory }
                    }
                    if ($row.shared) {
                        Assert ($observed.message -match 'Launch blocked before game/UI mutation') "Shared path bypassed lock: $($row.path); $($observed.message)"
                        Equal $observed.processQueries 0 'Busy launcher queried game/UI processes'
                    } else {
                        Equal $observed.message 'Synthetic no-game boundary reached.' 'Independent capture was blocked by shared output ownership'
                        Equal $observed.processQueries 1 'Capture did not reach the no-game boundary'
                    }
                    Equal $observed.starts 0 'Launcher started a game/UI process'
                    Equal $observed.stops 0 'Launcher stopped a game/UI process'
                    Equal @([IO.Directory]::GetFiles((Join-Path $profile 'Desktop/NGUInjector'))).Count 0 'Launcher published startup requests'
                }
                Equal @([IO.File]::ReadAllLines((Join-Path $s.repo 'deferred.txt'))).Count 4 'Shared launcher attempts did not all defer exactly once'
            } finally {
                [IO.File]::WriteAllText((Join-Path $s.directory 'release'), 'release')
                if (-not $process.WaitForExit(5000)) { $process.Kill($true) }
                [IO.File]::WriteAllText((Join-Path $s.directory 'holder.stderr.log'), $process.StandardError.ReadToEnd())
                $process.Dispose()
            }
        }

        Case 'Session reflection acceptance selects overloads and preserves ref args and exceptions' {
            $source = [IO.File]::ReadAllText((Join-Path $repo 'checks/SaveSafety/SessionChecks.Program.cs'))
            $method = [regex]::Match($source, '(?s)    private static object Call\(.*?\r?\n    \}').Value
            Assert ([bool]$method) 'Actual SessionChecks.Call helper was not found'
            Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
public static class RunnerSessionReflection {
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
$method
    public static object Invoke(object target, string name, object[] args) { return Call(target, name, args); }
}
public class RunnerSessionReflectionTarget {
    private static string Restore(string path) { return path; }
    private static string Restore(string path, bool allowArchiveFailure) { return path + allowArchiveFailure; }
    private static string Typed(string value) { return "string"; }
    private static string Typed(int value) { return "int"; }
    private string WithResult(string command, object input, out string result) { result = command; return input == null ? "null" : "value"; }
    private static void Fail() { throw new IOException("Synthetic reflected restoration failure."); }
}
"@
            $target = [RunnerSessionReflectionTarget]
            Equal ([RunnerSessionReflection]::Invoke($target, 'Restore', @('bundle', $true))) 'bundleTrue' 'Actual overloaded Restore(string, bool)'
            Equal ([RunnerSessionReflection]::Invoke($target, 'Restore', @('bundle'))) 'bundle' 'Restore(string)'
            Equal ([RunnerSessionReflection]::Invoke($target, 'Typed', @('value'))) 'string' 'Same-arity string overload'
            Equal ([RunnerSessionReflection]::Invoke($target, 'Typed', @([int]7))) 'int' 'Same-arity int overload'
            $arguments = [object[]]@('restoreSaveTest', $null, $null)
            Equal ([RunnerSessionReflection]::Invoke([RunnerSessionReflectionTarget]::new(), 'WithResult', $arguments)) 'null' 'Null and instance arguments'
            Equal $arguments[2] 'restoreSaveTest' 'Reflected out argument was not propagated'
            $failure = $null
            try { [RunnerSessionReflection]::Invoke($target, 'Fail', @()) } catch { $failure = $_.Exception.GetBaseException() }
            Assert ($failure -is [IO.IOException] -and $failure.Message -eq 'Synthetic reflected restoration failure.') 'Reflected restoration exception was swallowed or replaced'
            Throws { [RunnerSessionReflection]::Invoke($target, 'Restore', @('bundle', 'invalid boolean')) } 'not found|MissingMethod'
        }

        Case 'Relative UI report acceptance keeps one absolute report and log root' {
            $s = New-Sandbox 'relative-ui-report'; $options = $s.options
            $options.ReportParent = 'reports'
            $previousDirectory = [Environment]::CurrentDirectory
            [Environment]::CurrentDirectory = $s.directory
            Push-Location -LiteralPath $s.directory
            try {
                Assert (-not [IO.Path]::IsPathRooted($options.ReportParent)) 'Regression did not supply a relative report parent'
                $before = $PWD.Path
                $r = Invoke-Verification @options -Scope UI
                Check-Report $r PASS 0
                Assert ([IO.Path]::IsPathFullyQualified($r.reportDirectory)) 'Report directory remained relative'
                Equal (Split-Path -Parent $r.reportDirectory) (Join-Path $s.directory 'reports') 'Normalized report parent'
                Equal $PWD.Path $before 'UI did not restore caller cwd'
                Equal @($r.phases).Count 7 'Relative report lost UI phases'
                foreach ($phase in $r.phases) {
                    Equal (Split-Path -Parent $phase.evidence.log) $r.reportDirectory 'UI phase log left the single absolute report root'
                }
                Equal @([IO.Directory]::GetFiles($s.directory, 'report.json', [IO.SearchOption]::AllDirectories)).Count 1 'UI wrote reports under multiple cwd roots'
            } finally {
                Pop-Location
                [Environment]::CurrentDirectory = $previousDirectory
            }
        }

        Case 'Non-capture artifact acceptance persists tested hash and MVID after success and suite failure' {
            foreach ($fail in @($false, $true)) {
                $s = New-Sandbox ('tested-artifact-' + $fail); $options = $s.options
                if ($fail) { Configure $s -Fail 'BlackBarChecks\.exe' }
                $r = Invoke-Verification @options -Scope Native -Suite BlackBar
                Check-Report $r $(if ($fail) { 'FAIL' } else { 'PASS' }) $(if ($fail) { 1 } else { 0 })
                $saved = Read-TestAllJson (Join-Path $r.reportDirectory 'report.json')
                foreach ($report in @($r, $saved)) {
                    Equal $report.artifact $null 'Non-capture run claimed a captured distribution'
                    Equal $report.testedArtifact.path (Join-Path $s.repo 'NGUInjector/bin/Release/NGUInjector.dll') 'Tested artifact path'
                    Equal $report.testedArtifact.sha256 (Get-FileHash -LiteralPath $report.testedArtifact.path).Hash 'Tested artifact SHA256'
                    Equal $report.testedArtifact.mvid ([Uri].Assembly.ManifestModule.ModuleVersionId.ToString()) 'Tested artifact MVID'
                    Equal @($report.testedArtifact.suites) @('BlackBar') 'Tested suites'
                }
            }
        }

        Case 'Rust-only acceptance drift blocks artifact capture' {
            $s = New-Sandbox 'rust-only-drift'; $options = $s.options
            [IO.File]::WriteAllText((Join-Path $s.repo 'input.rs'), '// synthetic Rust input')
            $rustBoundary = Join-Path $s.directory 'rust-process.ps1'
            [IO.File]::WriteAllText($rustBoundary, [IO.File]::ReadAllText($fixture).Replace("'input.cs'", "'input.rs'"))
            $options.NativeProcessBoundary = $rustBoundary
            Configure $s -Mutate 'BlackBarChecks\.exe'
            $unchanged = (Get-FileHash -LiteralPath (Join-Path $s.repo 'input.cs')).Hash
            $capture = Join-Path $s.directory 'must-not-capture'
            $r = Invoke-Verification @options -Scope Native -ArtifactDirectory $capture
            Check-Report $r BLOCKED 2 $rustBoundary
            Equal (Get-FileHash -LiteralPath (Join-Path $s.repo 'input.cs')).Hash $unchanged 'Regression changed non-Rust source'
            Assert ($r.sourceBefore -and $r.sourceAfter -and $r.sourceBefore -cne $r.sourceAfter) 'Rust-only source drift was not detected'
            Assert (-not [IO.Directory]::Exists($capture) -and -not $r.artifact) 'Rust drift captured an artifact'
            Assert (@($r.errors | Where-Object { $_.phase -eq 'source.identity' -and $_.status -eq 'BLOCKED' }).Count -eq 1) 'Rust-only source blocker missing'
            Assert ($r.handoff.written) 'Rust source drift handoff missing'
        }

        Case 'All runs UI once after failures and continues independent source and harness gates' {
            $s = New-Sandbox 'all-independent'
            Configure $s -Fail '(npm run check|cargo fmt|ExternalUiChecks\.csproj)'
            $options = $s.options
            $r = Invoke-Verification @options -Scope All -BrowserWorkers 3
            Check-Report $r FAIL 1
            $names = @($r.phases.name)
            Equal @($names | Where-Object { $_ -like 'ui.*' }).Count 7 'UI gates duplicated or skipped'
            foreach ($name in @('ui.check', 'ui.test', 'ui.build', 'ui.cargo.fmt', 'ui.cargo.test', 'ui.cargo.check', 'ui.browser')) {
                Equal @($names | Where-Object { $_ -eq $name }).Count 1 "$name must execute once"
            }
            Equal @($names | Where-Object { $_ -like 'source.*' }).Count 22 'independent source coverage'
            Equal @($r.errors | Where-Object status -EQ FAIL).Count 3 'independent failures retained'
            Assert ($names -contains 'harness.RunnerChecks') 'Harness child not scheduled'
            Assert (-not $r.fullOfflineGatePassed) 'Failed All claimed full acceptance'
            $calls = @(Calls $s)
            Equal @($calls | Where-Object { ($_.arguments -join ' ') -like '*BundleChecks.csproj*' }).Count 1 'Bundle must run once'
            $browser = @($r.phases | Where-Object name -EQ ui.browser)[0]
            Assert ($browser.evidence.arguments -contains '--workers=3') 'Browser worker option lost'
            $buildIndex = [Array]::IndexOf($names, 'harness.buildSessionChecks')
            $runIndex = [Array]::IndexOf($names, 'harness.SessionChecks')
            Assert ($buildIndex -ge 0 -and $runIndex -gt $buildIndex) 'Session compiled build must precede run'
            $session = @($r.phases | Where-Object name -EQ harness.SessionChecks)[0]
            Assert ($session.evidence.executable -like '*bin*SessionChecks*SaveTestSessionChecks.exe') 'Session run is not compiled EXE'
            Equal @($names | Where-Object { $_ -eq 'harness.productionBuild' }).Count 0 'All rebuilt production in harness'
            Equal @($names | Where-Object { $_ -eq 'native.build' }).Count 1 'All aggregate count'
            Equal $r.sourceBefore $r.sourceAfter 'Stable synthetic git source identity'
        }

        Case 'Native full gate invokes one incremental aggregate then each fixture with tested identity' {
            $s = New-Sandbox 'native-full'; $options = $s.options
            $capture = Join-Path $s.directory 'captured-native'
            $r = Invoke-Verification @options -Scope Native -ArtifactDirectory $capture
            Check-Report $r PASS 0
            Equal $r.fullOfflineGatePassed $false 'Focused native claimed full acceptance'
            $calls = @(Calls $s)
            Equal $calls.Count 14 'One aggregate plus thirteen native suites'
            Equal @($calls | Where-Object executable -Like '*MSBuild.exe').Count 1 'Native MSBuild invocation count'
            $build = $calls[0]
            Assert ($build.arguments -contains '/restore' -and $build.arguments -contains '/t:Build' -and $build.arguments -contains '/m:1') 'Native aggregate incremental arguments'
            Assert (-not (($build.arguments -join ' ') -match '(?i)Rebuild')) 'Native invoked Rebuild'
            Assert ($build.arguments[0] -like '*checks*NativeVerification.proj') 'Native bypassed aggregate'
            Equal @($r.phases | Where-Object name -Like 'native.*').Count 14 'Native phases'
            $plugin = Join-Path $s.repo 'NGUInjector/bin/Release/NGUInjector.dll'
            $managed = Join-Path $s.repo 'game/NGUIdle_Data/Managed'
            Equal @($calls | Where-Object executable -Like '*ResourceAggregationChecks.exe')[0].arguments.Count 0 'ResourceAggregation arguments'
            Equal @($calls | Where-Object executable -Like '*OrdinaryPolicyChecks.exe')[0].arguments @($managed, $plugin, (Join-Path $s.repo 'checks/bin/BlackBarChecks.exe')) 'OrdinaryPolicy dependency arguments'
            foreach ($call in @($calls | Where-Object { $_.executable -notlike '*MSBuild.exe' -and $_.executable -notlike '*OrdinaryPolicyChecks.exe' -and $_.executable -notlike '*ResourceAggregationChecks.exe' })) {
                Equal $call.arguments @($plugin, $managed) 'Native fixture production arguments'
                Assert ([IO.File]::Exists($call.executable)) 'Boundary did not create expected fixture output'
            }
            Equal $r.artifact $capture 'Successful full native artifact capture'
            $manifest = Read-TestAllJson (Join-Path $capture 'manifest.json')
            Equal $r.testedArtifact.sha256 $manifest.sha256 'Tested identity distinct from capture path'
            Equal $r.testedArtifact.mvid $manifest.mvid 'Captured tested MVID'
            Equal $manifest.sha256 (Get-FileHash -LiteralPath $plugin).Hash 'Manifest tested artifact SHA256'
            Equal $manifest.mvid ([Uri].Assembly.ManifestModule.ModuleVersionId.ToString()) 'Manifest tested assembly MVID'
            Equal @($manifest.suites) @(Get-NativeVerificationSuites) 'Manifest full native suites'
            Equal $manifest.sourceStamp $r.sourceAfter 'Manifest source identity'
            Equal $manifest.fullOfflineVerified $false 'Native capture claimed All acceptance'
            Equal $manifest.liveVerified $false 'Offline capture claimed live verification'
            Equal (Get-FileHash -LiteralPath (Join-Path $capture 'injector/NGUInjector.dll')).Hash $manifest.sha256 'Captured DLL identity'
            Assert ([IO.File]::Exists((Join-Path $capture 'verification/report.json'))) 'Captured verification report missing'
        }

        Case 'UI dependency failure blocks npm consumers but retains independent Rust gates' {
            $s = New-Sandbox 'ui-dependency-failure'; $options = $s.options
            [IO.Directory]::Delete((Join-Path $s.repo 'NGUInjector.UI/node_modules'))
            Configure $s -Fail 'npm ci'
            $before = $PWD.Path
            $r = Invoke-Verification @options -Scope UI
            Check-Report $r FAIL 1
            Equal @($r.phases.name) @('ui.dependencies', 'ui.cargo.fmt', 'ui.cargo.test', 'ui.cargo.check') 'UI npm dependency stop and independent Rust gates'
            Equal $PWD.Path $before 'UI failure did not restore working directory'
            foreach ($call in @(Calls $s)) { Equal $call.cwd (Join-Path $s.repo 'NGUInjector.UI') 'UI tool working directory' }
        }

        Case 'Native fixture failures preserve later independent suites without repeat builds' {
            $s = New-Sandbox 'native-suite-failures'; $options = $s.options
            Configure $s -Fail '(BlackBarChecks\.exe|OwnershipChecks\.exe)'
            $r = Invoke-Verification @options -Scope Native -Suite @('BlackBar', 'Ownership', 'AutoExp')
            Check-Report $r FAIL 1
            Equal @($r.phases.name) @('native.build', 'native.BlackBar', 'native.Ownership', 'native.AutoExp') 'Native failure aggregation or repeat build'
            Equal @($r.errors.phase) @('native.BlackBar', 'native.Ownership') 'Native per-suite failures lost'
            Equal @($r.phases.evidence.exitCode) @(0, 23, 23, 0) 'Actual child exit codes not retained'
            Equal @(Calls $s).Count 4 'Native suite failure repeated workers'
        }

        Case 'Native selection controls suite output paths and exact selector arguments' {
            $matrix = @(
                @{ suite = 'CombatPrepIntegration'; selector = @('--check', 'CheckExample'); output = 'CombatPrepIntegration/bin/CombatPrepIntegrationChecks.exe' },
                @{ suite = 'StageTargetsNative'; selector = @('--stage-targets'); output = 'PresetIntegration/bin/stage-targets/PresetIntegrationChecks.exe' },
                @{ suite = 'AutoExp'; selector = @('--selected'); output = 'AutoExp/bin/native/AutoExpNativeChecks.exe' },
                @{ suite = 'PresetEquipment'; selector = @(); output = 'Presets/bin/Release/equipment/PresetEquipmentChecks.exe' }
            )
            foreach ($row in $matrix) {
                $s = New-Sandbox ('selected-' + $row.suite); $options = $s.options
                $r = Invoke-Verification @options -Scope Native -Suite @($row.suite, $row.suite) -Selector $row.selector
                Check-Report $r PASS 0
                $calls = @(Calls $s)
                Equal $calls.Count 2 'Selection must build once and run only selected suite'
                Assert ($calls[0].arguments -contains "/p:NativeSuites=$($row.suite)") 'Selected aggregate suite property'
                Equal $calls[1].executable (Join-Path $s.repo ('checks/' + $row.output)) 'Selected fixture output'
                Equal $calls[1].arguments (@((Join-Path $s.repo 'NGUInjector/bin/Release/NGUInjector.dll'), (Join-Path $s.repo 'game/NGUIdle_Data/Managed')) + $row.selector) 'Selector arguments/order'
                Equal $r.fullOfflineGatePassed $false 'Selected native claimed All acceptance'
            }
        }

        Case 'Invalid suite selector and capture combinations start no tools' {
            $s = New-Sandbox 'invalid'; $options = $s.options
            Throws { Invoke-Verification @options -Scope UI -Suite BlackBar } 'Suite/Selector require'
            Throws { Invoke-Verification @options -Scope Native -Suite BlackBar -ArtifactDirectory (Join-Path $s.directory 'capture') } 'Artifact capture requires'
            foreach ($request in @(
                @{ Suite = @('unknown') }, @{ Suite = @('BlackBar', 'AutoExp'); Selector = @('--x') },
                @{ Suite = @('BlackBar'); Selector = @('--x', '--y') }
            )) {
                $r = Invoke-Verification @options -Scope Native @request
                Check-Report $r FAIL 1
            }
            Equal @(Calls $s).Count 0 'Invalid request started tool children'
        }

        Case 'Aggregate failure blocks stale production and native consumers across All' {
            $s = New-Sandbox 'failed-aggregate'; $options = $s.options
            $plugin = Join-Path $s.repo 'NGUInjector/bin/Release/NGUInjector.dll'
            $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $plugin))
            [IO.File]::Copy([Uri].Assembly.Location, $plugin)
            Configure $s -Fail 'NativeVerification\.proj'
            $r = Invoke-Verification @options -Scope All
            Check-Report $r FAIL 1
            Equal @($r.phases | Where-Object name -Like 'native.*').Count 1 'Failed aggregate ran stale native consumers'
            Assert ($r.phases.name -notcontains 'harness.SessionChecks' -and $r.phases.name -notcontains 'harness.packagedCodec') 'Harness consumed stale DLL after aggregate failure'
            Assert ($r.phases.name -notcontains 'harness.productionBuild') 'Harness retried failed aggregate production build'
            Assert (@($r.errors | Where-Object { $_.phase -eq 'harness.productionConsumers' -and $_.status -eq 'BLOCKED' }).Count -eq 1) 'Missing stale-consumer blocker'
            Equal $r.testedArtifact $null 'Failed aggregate claimed stale tested identity'
            Assert ($r.phases.name -contains 'ui.browser' -and $r.phases.name -contains 'source.SaveSafety.BundleChecks') 'Aggregate failure suppressed independent gates'
        }

        Case 'Harness production failure never executes stale DLL consumers' {
            $s = New-Sandbox 'failed-production'; $options = $s.options
            $plugin = Join-Path $s.repo 'NGUInjector/bin/Release/NGUInjector.dll'
            $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $plugin))
            [IO.File]::Copy([Uri].Assembly.Location, $plugin)
            Configure $s -Fail 'NGUInjector\.csproj'
            $r = Invoke-Verification @options -Scope Harness
            Check-Report $r FAIL 1
            Assert ($r.phases.name -notcontains 'harness.SessionChecks' -and $r.phases.name -notcontains 'harness.packagedCodec') 'Harness consumed stale production DLL'
            Equal $r.testedArtifact $null 'Failed production build claimed stale tested identity'
            Assert ($r.phases.name -contains 'harness.RunnerChecks') 'Harness did not schedule checks as a child'
            $child = @($r.phases | Where-Object name -EQ harness.RunnerChecks)[0]
            Assert ($child.evidence.arguments -contains (Join-Path $s.repo 'checks/verification/RunnerChecks.ps1')) 'RunnerChecks child path wrong'
        }

        Case 'Harness SessionChecks run depends on its successful compiled build' {
            $s = New-Sandbox 'failed-session-build'; $options = $s.options
            Configure $s -Fail 'SessionChecks\.csproj'
            $r = Invoke-Verification @options -Scope Harness
            Check-Report $r FAIL 1
            Assert ($r.phases.name -contains 'harness.buildSessionChecks') 'Session build missing'
            Assert ($r.phases.name -notcontains 'harness.SessionChecks') 'Session stale executable ran after failed build'
            Assert ($r.phases.name -contains 'harness.packagedCodec') 'Session build failure suppressed independent packaged codec'
            $saved = Read-TestAllJson (Join-Path $r.reportDirectory 'report.json')
            Equal $saved.testedArtifact.sha256 (Get-FileHash -LiteralPath $saved.testedArtifact.path).Hash 'Harness post-build failure tested SHA256'
            Equal $saved.testedArtifact.mvid ([Uri].Assembly.ManifestModule.ModuleVersionId.ToString()) 'Harness post-build failure tested MVID'
        }

        Case 'Explicit defer appends one shared handoff preserving notes and quoted requests' {
            $s = New-Sandbox 'explicit-defer'; $options = $s.options
            $capture = Join-Path $s.directory "artifact with ' quote"
            $first = Invoke-Verification @options -Scope Native -DeferReason 'owner checkpoint one' -HandoffNote "changed RunnerChecks.ps1; retain 'quoted' context" -ArtifactDirectory $capture
            $second = Invoke-Verification @options -Scope Native -Suite CombatPrepIntegration -Selector @('--check', 'CheckExample') -DeferReason 'owner checkpoint two' -HandoffNote 'next owner must read both entries'
            foreach ($r in @($first, $second)) { Check-Report $r BLOCKED 2; Assert ($r.handoff.written) 'Deferred handoff not durable' }
            Equal $first.handoff.path $second.handoff.path 'Deferrals scattered handoff files'
            $text = [IO.File]::ReadAllText($first.handoff.path)
            Equal ([regex]::Matches($text, '\| verification handoff')).Count 2 'Expected exactly two append entries'
            foreach ($value in @($first.id, $second.id, 'owner checkpoint one', 'owner checkpoint two', "retain 'quoted' context", 'next owner must read both entries', "-Selector '--check','CheckExample'", "repo with '' quote", "artifact with '' quote")) {
                Assert ($text.Contains($value)) "Handoff lost context/quoting: $value"
            }
            Equal @(Calls $s).Count 0 'Defer started tool children'
            Equal @($first.phases).Count 0 'Defer started phases'
            Assert (-not [IO.File]::Exists((Join-Path $s.repo 'artifacts/verification-owner.json'))) 'Defer claimed production ownership'
        }

        Case 'Handoff storage failure retains complete entry in durable report' {
            $s = New-Sandbox 'handoff-storage-failure'; $options = $s.options
            [IO.File]::WriteAllText((Join-Path $s.repo 'artifacts'), 'Blocks directory creation without permissions tricks.')
            $r = Invoke-Verification @options -Scope Source -DeferReason 'storage unavailable' -HandoffNote 'complete continuation context'
            Check-Report $r BLOCKED 2
            Assert (-not $r.handoff.written -and $r.handoff.error) 'Handoff storage failure not exposed'
            $saved = Read-TestAllJson (Join-Path $r.reportDirectory 'report.json')
            foreach ($value in @('storage unavailable', 'complete continuation context', $r.request.command, $r.id, 'Safety:', 'Pending checks/next steps:')) {
                Assert ($saved.handoff.entry.Contains($value)) "Durable fallback entry lost $value"
            }
            Equal @(Calls $s).Count 0 'Handoff failure started tools'
        }

        Case 'Real mutex contention fails fast with zero tool children and durable handoff' {
            $s = New-Sandbox 'mutex-contention'; $options = $s.options
            $process = Start-Fixture @('-Mode', 'Mutex', '-MutexName', $options.MutexName, '-Directory', $s.directory)
            try {
                $ready = Join-Path $s.directory 'ready'; $clock = [Diagnostics.Stopwatch]::StartNew()
                while (-not [IO.File]::Exists($ready) -and -not $process.HasExited -and $clock.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 20 }
                Assert ([IO.File]::Exists($ready)) 'Mutex holder did not signal readiness'
                $ownerPath = Join-Path $s.repo 'artifacts/verification-owner.json'
                $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $ownerPath))
                Write-TestAllJson $ownerPath @{ id = 'synthetic-holder'; pid = $process.Id; command = 'no build; mutex holder only' }
                $clock.Restart()
                $r = Invoke-Verification @options -Scope All -HandoffNote 'retry only after owner checkpoint'
                Assert ($clock.Elapsed.TotalSeconds -lt 3) 'Contention waited instead of failing fast'
                Check-Report $r BLOCKED 2
                Equal @(Calls $s).Count 0 'Contended run launched tools'
                Equal @($r.phases).Count 0 'Contended run started gates'
                Equal $r.owner.id 'synthetic-holder' 'Holder evidence lost'
                Equal (Read-TestAllJson $ownerPath).id 'synthetic-holder' 'Contender removed owner metadata'
                Assert ($r.handoff.written -and [IO.File]::ReadAllText($r.handoff.path).Contains('retry only after owner checkpoint')) 'Contention handoff not durable'
            } finally {
                [IO.File]::WriteAllText((Join-Path $s.directory 'release'), 'release')
                if (-not $process.WaitForExit(5000)) { $process.Kill($true) }
                [IO.File]::WriteAllText((Join-Path $s.directory 'holder.stderr.log'), $process.StandardError.ReadToEnd())
                $process.Dispose()
            }
        }

        Case 'Source drift blocks acceptance and artifact capture using real git identity' {
            $s = New-Sandbox 'source-drift'; $options = $s.options
            Configure $s -Mutate 'BlackBarChecks\.exe'
            $capture = Join-Path $s.directory 'must-not-capture'
            $r = Invoke-Verification @options -Scope Native -ArtifactDirectory $capture -HandoffNote 'source changed; verify fresh candidate'
            Check-Report $r BLOCKED 2
            Assert ($r.sourceBefore -and $r.sourceAfter -and $r.sourceBefore -cne $r.sourceAfter) 'Real git source drift not detected'
            Assert (-not [IO.Directory]::Exists($capture) -and -not $r.artifact) 'Drifted source captured artifact'
            Assert (@($r.errors | Where-Object { $_.phase -eq 'source.identity' -and $_.status -eq 'BLOCKED' }).Count -eq 1) 'Source drift blocker missing'
            Assert ($r.handoff.written) 'Source drift handoff missing'
        }

        foreach ($identityMode in @('no-git', 'empty-git')) {
            Case "Source stamp unavailable before gates ($identityMode) is BLOCKED with zero tools" {
                $s = New-Sandbox $identityMode -NoGit:($identityMode -eq 'no-git') -Empty:($identityMode -eq 'empty-git'); $options = $s.options
                $r = Invoke-Verification @options -Scope Source
                Equal @(Calls $s).Count 0 'Unavailable source identity started tools'
                Assert (-not $r.fullOfflineGatePassed) 'Unavailable source identity claimed acceptance'
                Check-Report $r BLOCKED 2
            }
        }

        Case 'Source stamp unavailable after gates is BLOCKED and cannot capture' {
            $s = New-Sandbox 'missing-source-after'; $options = $s.options
            Configure $s -Mutate 'BlackBarChecks\.exe' -DeleteInput
            $capture = Join-Path $s.directory 'must-not-capture'
            $r = Invoke-Verification @options -Scope Native -ArtifactDirectory $capture
            Assert ($r.sourceBefore -and -not $r.sourceAfter) 'Endpoint unavailability not retained'
            Assert (-not [IO.Directory]::Exists($capture)) 'Missing endpoint identity captured artifact'
            Check-Report $r BLOCKED 2
        }

        Case 'Focused source PASS retains Bundle once without full acceptance' {
            $s = New-Sandbox 'source-focused'; $options = $s.options
            $r = Invoke-Verification @options -Scope Source
            Check-Report $r PASS 0
            Equal $r.fullOfflineGatePassed $false 'Focused source claimed full acceptance'
            Equal @($r.phases).Count 22 'Source gate count'
            Equal @($r.phases | Where-Object name -EQ source.SaveSafety.BundleChecks).Count 1 'Bundle once'
            Assert ($r.phases.name -contains 'source.SustainedFocus.execution-core' -and $r.phases.name -contains 'source.RouteExecutorSyntax') 'Independent source selectors missing'
        }

        Case 'Aggregate XML selection matrix preserves dependencies and variant isolation' {
            $xml = [xml]::new(); $xml.Load((Join-Path $repo 'checks/NativeVerification.proj'))
            $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable); $ns.AddNamespace('m', $xml.DocumentElement.NamespaceURI)
            $items = @($xml.SelectNodes('//m:NativeFixture', $ns))
            $matrix = @{
                PresetIntegration = @('PresetIntegration/PresetIntegrationChecks.csproj'); CombatPrepIntegration = @('CombatPrepIntegration/CombatPrepIntegrationChecks.csproj')
                ManualAdventureOverlay = @('ManualAdventureOverlay/ManualAdventureOverlayChecks.csproj'); BlackBar = @('BlackBarChecks.csproj')
                Ownership = @('Ownership/OwnershipChecks.csproj'); TitanGold = @('TitanGold/TitanGoldChecks.csproj'); PresetEquipment = @('Presets/EquipmentChecks.csproj')
                GearEquip = @('GearEquip/GearEquipChecks.csproj'); OrdinaryPolicy = @('BlackBarChecks.csproj', 'OrdinaryPolicy/OrdinaryPolicyChecks.csproj')
                WinFormsBlood = @('WinFormsBlood/WinFormsBloodChecks.csproj'); StageTargetsNative = @('PresetIntegration/PresetIntegrationChecks.csproj')
                ResourceAggregation = @('Presets/ResourceAggregationChecks.csproj'); AutoExp = @('PresetIntegration/PresetIntegrationChecks.csproj', 'AutoExp/NativeChecks.csproj')
            }
            Equal @(Get-NativeVerificationSuites).Count $matrix.Count 'Dependency matrix missing canonical suite'
            foreach ($suite in @(Get-NativeVerificationSuites)) {
                $selected = @($items | Where-Object { $_.Condition.Contains("'|$suite|'") })
                Equal @($selected | ForEach-Object { $_.Include.Replace('$(MSBuildThisFileDirectory)', '').Replace('\', '/') }) $matrix[$suite] "$suite aggregate dependency/order"
                if ($suite -eq 'AutoExp') { Assert (-not $selected[0].AdditionalProperties) 'AutoExp dependency contaminated by stage variant' }
            }
            $stage = @($items | Where-Object { $_.Condition.Contains("'|StageTargetsNative|'") })[0]
            foreach ($property in @('StartupObject=PresetStageTargetsNativeChecks', 'OutputPath=bin\stage-targets\', 'BaseIntermediateOutputPath=obj\stage-targets\', 'IntermediateOutputPath=obj\stage-targets\', 'MSBuildProjectExtensionsPath=$(MSBuildThisFileDirectory)PresetIntegration\obj\stage-targets\')) {
                Assert ($stage.AdditionalProperties.Contains($property)) "Stage variant isolation missing $property"
            }
            Assert ($xml.Project.PropertyGroup.FixtureProperties.Contains('BuildProjectReferences=false')) 'Aggregate fixture references rebuild production'
            foreach ($targetName in @('Restore', 'Build')) {
                $tasks = @($xml.SelectNodes("//m:Target[@Name='$targetName']/m:MSBuild", $ns))
                Equal $tasks.Count 2 "$targetName sequential production/fixture tasks"
                Equal $tasks[0].Projects '$(ProductionProject)' "$targetName production first"
                Equal $tasks[1].Projects '@(NativeFixture)' "$targetName fixtures second"
                foreach ($task in $tasks) {
                    Equal $task.Targets $targetName 'No implicit Rebuild target'
                    Equal $task.BuildInParallel 'false' 'Aggregate parallel build disabled'
                    Equal $task.StopOnFirstFailure 'true' 'Aggregate stops before stale dependents'
                }
            }
        }
    } finally {
        $env:NGU_RUNNER_CHECK_STATE = $oldState
        ${env:ProgramFiles(x86)} = $oldProgramFiles
    }
    $failed = @($results | Where-Object status -EQ FAIL)
    $success = $results.Count -gt 0 -and $failed.Count -eq 0
    $summary = @{ schema = 'ngu.runner-checks'; cases = @($results); assertions = $counts.assertions; evidenceDirectory = $root
        success = $success; exitCode = $(if ($success) { 0 } else { 1 }) }
    Write-TestAllJson (Join-Path $root 'runner-checks.json') $summary
    "RunnerChecks: $($results.Count) cases, $($counts.assertions) assertions, $($failed.Count) failures; exit $($summary.exitCode). Evidence: $root"
    if (-not $results.Count) { [Console]::Error.WriteLine('RunnerChecks matched zero cases.'); exit 1 }
    if ($failed.Count) { exit 1 }
    exit 0
}
