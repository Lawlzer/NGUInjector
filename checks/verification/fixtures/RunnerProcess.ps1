#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Tool', 'Import', 'Mutex')][string]$Mode = 'Tool',
    [string]$Executable, [string]$ArgumentsBase64,
    [string]$Repo, [string]$Directory, [string]$MutexName
)
$ErrorActionPreference = 'Stop'

if ($Mode -eq 'Mutex') {
    $mutex = [Threading.Mutex]::new($false, $MutexName)
    $held = $false
    try {
        $held = $mutex.WaitOne(0)
        if (-not $held) { throw 'Synthetic mutex unexpectedly owned.' }
        [IO.File]::WriteAllText((Join-Path $Directory 'ready'), "$PID")
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not [IO.File]::Exists((Join-Path $Directory 'release')) -and $clock.Elapsed.TotalSeconds -lt 30) {
            Start-Sleep -Milliseconds 20
        }
    } finally {
        if ($held) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
    exit 0
}

if ($Mode -eq 'Import') {
    function git { throw 'Import invoked git.' }
    function npm { throw 'Import invoked npm.' }
    function dotnet { throw 'Import invoked dotnet.' }
    function cargo { throw 'Import invoked cargo.' }
    function Start-Process { throw 'Import started a process.' }
    function Invoke-RestMethod { throw 'Import accessed the bridge.' }
    function Invoke-WebRequest { throw 'Import accessed the bridge.' }
    $importRoot = $Directory
    $before = @([IO.Directory]::EnumerateFileSystemEntries($importRoot, '*', [IO.SearchOption]::AllDirectories))
    $output = @(. (Join-Path $Repo 'test.ps1') -FunctionsOnly -RuntimeDir (Join-Path $importRoot 'bridge') -ReportParent (Join-Path $importRoot 'reports'))
    $output += @(. (Join-Path $Repo 'verify.ps1') -FunctionsOnly -ReportParent (Join-Path $importRoot 'reports'))
    foreach ($helper in @('Coordination', 'Native', 'Ui')) {
        $path = Join-Path $Repo "checks/verification/$helper.ps1"
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
        if ($errors.Count -or @($ast.EndBlock.Statements | Where-Object { $_ -isnot [Management.Automation.Language.FunctionDefinitionAst] }).Count) {
            throw "$helper must contain side-effect-free function definitions only."
        }
        $output += @(. $path)
    }
    foreach ($name in @('Invoke-Verification', 'Invoke-TestAllNative', 'Get-VerificationSourceStamp', 'Invoke-NativeVerification', 'Invoke-UiVerification', 'Invoke-SourceVerification')) {
        if (-not (Get-Command $name -CommandType Function -ErrorAction SilentlyContinue)) { throw "Missing import: $name" }
    }
    $after = @([IO.Directory]::EnumerateFileSystemEntries($importRoot, '*', [IO.SearchOption]::AllDirectories))
    if ($output.Count -or ($before -join "`n") -cne ($after -join "`n")) { throw 'FunctionsOnly import emitted output or wrote files.' }
    '{"imported":true,"actions":0,"filesCreated":0}'
    exit 0
}

# This is the only external-tool substitute. Never execute the requested program,
# including RunnerChecks itself when the fake harness reaches its child check.
if (-not $env:NGU_RUNNER_CHECK_STATE) { throw 'Missing isolated process-boundary state.' }
$state = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($env:NGU_RUNNER_CHECK_STATE)) -AsHashtable
$arguments = @(ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ArgumentsBase64))))
$entry = @{ executable = $Executable; arguments = $arguments; cwd = $PWD.Path; pid = $PID }
[IO.File]::AppendAllText($state.trace, (ConvertTo-Json -InputObject $entry -Compress -Depth 20) + "`n")
$command = $Executable + ' ' + ($arguments -join ' ')
if ($state.failPattern -and $command -match $state.failPattern) {
    [Console]::Error.WriteLine('Synthetic tool failure: ' + $command)
    exit 23
}
function Write-OutputFile([string]$Path, [switch]$Assembly) {
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    if ($Assembly) { [IO.File]::Copy([Uri].Assembly.Location, $Path, $true) }
    else { [IO.File]::WriteAllText($Path, 'Synthetic output; never executable.') }
}
$plugin = Join-Path $state.repo 'NGUInjector/bin/Release/NGUInjector.dll'
if ($command -match 'MSBuild\.exe.*(?:NativeVerification\.proj|NGUInjector\.csproj)') {
    Write-OutputFile $plugin -Assembly
    Write-OutputFile (Join-Path $state.repo 'NGUInjector/bin/Release/dist/injector/NGUInjector.dll') -Assembly
    foreach ($relative in @(
        'PresetIntegration/bin/PresetIntegrationChecks.exe', 'CombatPrepIntegration/bin/CombatPrepIntegrationChecks.exe',
        'ManualAdventureOverlay/bin/ManualAdventureOverlayChecks.exe', 'bin/BlackBarChecks.exe', 'Ownership/bin/OwnershipChecks.exe',
        'TitanGold/bin/TitanGoldChecks.exe', 'Presets/bin/Release/equipment/PresetEquipmentChecks.exe', 'GearEquip/bin/GearEquipChecks.exe',
        'OrdinaryPolicy/bin/OrdinaryPolicyChecks.exe', 'WinFormsBlood/bin/WinFormsBloodChecks.exe',
        'PresetIntegration/bin/stage-targets/PresetIntegrationChecks.exe', 'Presets/bin/resource-aggregation/ResourceAggregationChecks.exe',
        'AutoExp/bin/native/AutoExpNativeChecks.exe'
    )) { Write-OutputFile (Join-Path $state.repo "checks/$relative") }
}
if ($command -match 'SessionChecks\.csproj') { Write-OutputFile (Join-Path $state.repo 'checks/SaveSafety/bin/SessionChecks/SaveTestSessionChecks.exe') }
if ($command -match 'PackagedCodecChecks\.csproj') { Write-OutputFile (Join-Path $state.repo 'checks/SaveSafety/bin/PackagedCodec/PackagedCodecChecks.exe') }
if ($command -match 'FixtureGenerator\.csproj') { Write-OutputFile (Join-Path $state.repo 'checks/SaveSafety/Fixtures/bin/Release/net48/FixtureGenerator.exe') }
if ($Executable -like '*FixtureGenerator.exe') { Write-OutputFile (Join-Path $arguments[0] 'valid.ok.txt') }
if ($command -match 'SaveInspector\.csproj') {
    $index = [Array]::IndexOf($arguments, '--output')
    if ($index -lt 0) { throw 'Inspector output argument missing.' }
    Write-OutputFile (Join-Path $arguments[$index + 1] 'SaveInspector.dll') -Assembly
}
if ($state.mutatePattern -and $command -match $state.mutatePattern) {
    if ($state.deleteInput) { [IO.File]::Delete((Join-Path $state.repo 'input.cs')) }
    else { [IO.File]::AppendAllText((Join-Path $state.repo 'input.cs'), '// changed during verification') }
}
'Synthetic tool boundary completed.'
exit 0
