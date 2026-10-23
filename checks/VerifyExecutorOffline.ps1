param(
    [ValidateSet('PresetIntegration', 'CombatPrepIntegration', 'ManualAdventureOverlay', 'BlackBar', 'Ownership', 'TitanGold', 'PresetEquipment', 'GearEquip', 'OrdinaryPolicy', 'WinFormsBlood', 'StageTargetsNative', 'ResourceAggregation', 'AutoExp')]
    [string[]]$Suite = @('PresetIntegration', 'CombatPrepIntegration', 'ManualAdventureOverlay', 'BlackBar', 'Ownership', 'TitanGold', 'PresetEquipment', 'GearEquip', 'OrdinaryPolicy', 'WinFormsBlood', 'StageTargetsNative', 'ResourceAggregation', 'AutoExp'),
    [string[]]$Selector = @(),
    [string]$GameFolder = $env:NGU_IDLE_DIR,
    [string]$LogPath = '',
    [string]$ArtifactDirectory = ''
)

$ErrorActionPreference = 'Stop'
function Invoke-VerificationCommand([string]$Executable, [string[]]$Arguments) {
    if ($LogPath) { & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath -Append }
    else { & $Executable @Arguments }
    $script:VerificationExitCode = $LASTEXITCODE
}
$repo = Split-Path -Parent $PSScriptRoot
$msbuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
if ([string]::IsNullOrWhiteSpace($GameFolder)) { throw 'Pass -GameFolder or set NGU_IDLE_DIR.' }
$managed = Join-Path $GameFolder 'NGUIdle_Data\Managed'
foreach ($parent in @((Join-Path $repo 'NGUInjector'), $managed)) {
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw "Offline verification parent unavailable: $parent" }
}
if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) { throw "MSBuild not found: $msbuild" }
if ($LogPath -and -not (Test-Path -LiteralPath (Split-Path -Parent ([IO.Path]::GetFullPath($LogPath))) -PathType Container)) {
    throw "Verification log parent unavailable: $LogPath"
}
if ($Selector.Count -and $Suite.Count -ne 1) { throw 'Selectors require exactly one suite; the default gate always runs full suites.' }
if ($Selector.Count -gt 1 -and -not ($Suite[0] -eq 'CombatPrepIntegration' -and $Selector.Count -eq 2 -and $Selector[0] -eq '--check' -and $Selector[1].StartsWith('Check', [StringComparison]::Ordinal))) {
    throw 'Pass one selector, or CombatPrepIntegration --check plus one method. Multiple selectors in one EXE would run only the first match.'
}

$fixtures = @{
    PresetIntegration = @('PresetIntegration\PresetIntegrationChecks.csproj', 'PresetIntegration\bin\PresetIntegrationChecks.exe')
    CombatPrepIntegration = @('CombatPrepIntegration\CombatPrepIntegrationChecks.csproj', 'CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe')
    ManualAdventureOverlay = @('ManualAdventureOverlay\ManualAdventureOverlayChecks.csproj', 'ManualAdventureOverlay\bin\ManualAdventureOverlayChecks.exe')
    BlackBar = @('BlackBarChecks.csproj', 'bin\BlackBarChecks.exe')
    Ownership = @('Ownership\OwnershipChecks.csproj', 'Ownership\bin\OwnershipChecks.exe')
    TitanGold = @('TitanGold\TitanGoldChecks.csproj', 'TitanGold\bin\TitanGoldChecks.exe')
    PresetEquipment = @('Presets\EquipmentChecks.csproj', 'Presets\bin\Release\equipment\PresetEquipmentChecks.exe')
    GearEquip = @('GearEquip\GearEquipChecks.csproj', 'GearEquip\bin\GearEquipChecks.exe')
    OrdinaryPolicy = @('OrdinaryPolicy\OrdinaryPolicyChecks.csproj', 'OrdinaryPolicy\bin\OrdinaryPolicyChecks.exe')
    WinFormsBlood = @('WinFormsBlood\WinFormsBloodChecks.csproj', 'WinFormsBlood\bin\WinFormsBloodChecks.exe')
    StageTargetsNative = @('PresetIntegration\PresetIntegrationChecks.csproj', 'PresetIntegration\bin\stage-targets\PresetIntegrationChecks.exe')
    ResourceAggregation = @('Presets\ResourceAggregationChecks.csproj', 'Presets\bin\resource-aggregation\ResourceAggregationChecks.exe')
    AutoExp = @('AutoExp\NativeChecks.csproj', 'AutoExp\bin\native\AutoExpNativeChecks.exe')
}
if ($ArtifactDirectory) {
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    if (-not $LogPath -or $Selector.Count -or $Suite.Count -ne $fixtures.Count -or @($Suite | Sort-Object -Unique).Count -ne $fixtures.Count) {
        throw 'Artifact capture requires a retained log and all full native suites.'
    }
    if (-not (Test-Path -LiteralPath (Split-Path -Parent $ArtifactDirectory) -PathType Container) -or (Test-Path -LiteralPath $ArtifactDirectory)) {
        throw 'Artifact capture requires an existing parent and a new destination.'
    }
}
$mutex = [Threading.Mutex]::new($false, 'Local\NGUInjector-Executor-Offline-Verification')
$held = $false
$failures = @()
try {
    try { $held = $mutex.WaitOne([TimeSpan]::FromMinutes(30)) }
    catch [Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { throw 'Another offline verification is still active; no build or test was started.' }
    if ($LogPath) { "Offline verification started $(Get-Date -Format o); suites=$($Suite -join ','); selectors=$($Selector -join ',')" | Tee-Object -FilePath $LogPath }
    Invoke-VerificationCommand $msbuild @((Join-Path $repo 'NGUInjector.sln'), '/restore', '/t:Rebuild', '/p:Configuration=Release', "/p:GameFolder=$GameFolder", '/v:minimal', '/nologo')
    if ($VerificationExitCode -ne 0) { throw 'Combined production build failed; no native pass is claimed.' }
    $plugin = Join-Path $repo 'NGUInjector\bin\Release\NGUInjector.dll'
    $testedHash = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
    foreach ($name in $Suite) {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $fixture = $fixtures[$name]
        $properties = @("/p:ManagedPath=$managed")
        if ($name -eq 'StageTargetsNative') {
            $properties += @("/p:CustomBeforeMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'Presets\StageTargetsNative.targets')",
                '/p:OutputPath=bin\stage-targets\', '/p:IntermediateOutputPath=obj\stage-targets\')
        }
        Invoke-VerificationCommand $msbuild (@((Join-Path $PSScriptRoot $fixture[0]), '/restore', '/t:Rebuild', '/p:Configuration=Release', '/p:DebugSymbols=true', '/p:DebugType=full') + $properties + @('/v:minimal', '/nologo'))
        if ($VerificationExitCode -ne 0) { $failures += "$name build"; continue }
        $arguments = @($plugin, $managed) + $Selector
        if ($name -eq 'ResourceAggregation') { $arguments = @() }
        if ($name -eq 'OrdinaryPolicy') { $arguments = @($managed, $plugin, (Join-Path $PSScriptRoot 'bin\BlackBarChecks.exe')) + $Selector }
        Invoke-VerificationCommand (Join-Path $PSScriptRoot $fixture[1]) $arguments
        $exit = $VerificationExitCode
        "Native suite $name finished in $([Math]::Round($clock.Elapsed.TotalSeconds, 2))s; exit=$exit"
        if ($exit -ne 0) { $failures += $name }
        # A legacy failure must not hide cutover regressions. These diagnostics
        # never replace the failed full suite or turn the gate green.
        if ($name -eq 'PresetIntegration' -and -not $Selector.Count -and $exit -ne 0) {
            foreach ($cutover in @('--execution-foundation', '--execution-core', '--route-executor-cutover', '--finite-action-cutover', '--execution-persistence')) {
                $clock.Restart()
                Invoke-VerificationCommand (Join-Path $PSScriptRoot $fixture[1]) @($plugin, $managed, $cutover)
                $exit = $VerificationExitCode
                "Native suite $name $cutover finished in $([Math]::Round($clock.Elapsed.TotalSeconds, 2))s; exit=$exit"
                if ($exit -ne 0) { $failures += "$name $cutover" }
            }
        }
    }
    if ($failures.Count) { throw "Offline verification failed: $($failures -join ', '). No full native pass is claimed." }
    if ($ArtifactDirectory) {
        if ((Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash -ne $testedHash) {
            throw 'Production binary changed during verification; no artifact was captured.'
        }
        $injector = Join-Path $repo 'NGUInjector\bin\Release\dist\injector'
        if ((Get-FileHash -LiteralPath (Join-Path $injector 'NGUInjector.dll') -Algorithm SHA256).Hash -ne $testedHash) {
            throw 'Distribution binary does not match the tested binary; no artifact was captured.'
        }
        New-Item -ItemType Directory -Path $ArtifactDirectory -ErrorAction Stop > $null
        Copy-Item -LiteralPath $injector -Destination (Join-Path $ArtifactDirectory 'injector') -Recurse -ErrorAction Stop
        Copy-Item -LiteralPath $LogPath -Destination (Join-Path $ArtifactDirectory 'native-verification.log') -ErrorAction Stop
        $manifest = [ordered]@{ verifiedAt = [DateTimeOffset]::Now.ToString('o'); sha256 = $testedHash; suites = $Suite
            mvid = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($plugin)).ManifestModule.ModuleVersionId.ToString()
            gameFolder = [IO.Path]::GetFullPath($GameFolder); liveVerified = $false }
        [IO.File]::WriteAllText((Join-Path $ArtifactDirectory 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        "Captured tested native artifact: $ArtifactDirectory"
    }
    'Offline native verification passed. No live game actions were performed.'
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
