function Get-NativeVerificationSuites {
    'PresetIntegration', 'CombatPrepIntegration', 'ManualAdventureOverlay', 'BlackBar', 'Ownership',
    'TitanGold', 'PresetEquipment', 'GearEquip', 'OrdinaryPolicy', 'WinFormsBlood',
    'StageTargetsNative', 'ResourceAggregation', 'AutoExp'
}

function Invoke-NativeVerification($Context, [string]$Repo, [string]$GameFolder, [string[]]$Suite = @(), [string[]]$Selector = @()) {
    $ErrorActionPreference = 'Stop'
    $Context.NativeArtifact = $null
    $canonical = @(Get-NativeVerificationSuites)
    if (-not $Suite.Count) { $Suite = $canonical }
    foreach ($name in $Suite) {
        if ($name -notin $canonical) { throw "Unknown native verification suite: $name" }
    }
    $Suite = @($canonical | Where-Object { $_ -in $Suite })
    if ($Selector.Count -and $Suite.Count -ne 1) { throw 'Selectors require exactly one suite; the default gate always runs full suites.' }
    if ($Selector.Count -gt 1 -and -not ($Suite[0] -eq 'CombatPrepIntegration' -and $Selector.Count -eq 2 -and $Selector[0] -eq '--check' -and $Selector[1].StartsWith('Check', [StringComparison]::Ordinal))) {
        throw 'Pass one selector, or CombatPrepIntegration --check plus one method. Multiple selectors in one EXE would run only the first match.'
    }

    $Repo = [IO.Path]::GetFullPath($Repo)
    $msbuild = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    if ([string]::IsNullOrWhiteSpace($GameFolder)) { throw 'Pass -GameFolder or set NGU_IDLE_DIR.' }
    $GameFolder = [IO.Path]::GetFullPath($GameFolder)
    $managed = Join-Path $GameFolder 'NGUIdle_Data\Managed'
    foreach ($parent in @((Join-Path $Repo 'NGUInjector'), $managed)) {
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw "Offline verification parent unavailable: $parent" }
    }
    if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) { throw "MSBuild not found: $msbuild" }

    $checks = Join-Path $Repo 'checks'
    $fixtures = @{
        PresetIntegration = 'PresetIntegration\bin\PresetIntegrationChecks.exe'
        CombatPrepIntegration = 'CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe'
        ManualAdventureOverlay = 'ManualAdventureOverlay\bin\ManualAdventureOverlayChecks.exe'
        BlackBar = 'bin\BlackBarChecks.exe'
        Ownership = 'Ownership\bin\OwnershipChecks.exe'
        TitanGold = 'TitanGold\bin\TitanGoldChecks.exe'
        PresetEquipment = 'Presets\bin\Release\equipment\PresetEquipmentChecks.exe'
        GearEquip = 'GearEquip\bin\GearEquipChecks.exe'
        OrdinaryPolicy = 'OrdinaryPolicy\bin\OrdinaryPolicyChecks.exe'
        WinFormsBlood = 'WinFormsBlood\bin\WinFormsBloodChecks.exe'
        StageTargetsNative = 'PresetIntegration\bin\stage-targets\PresetIntegrationChecks.exe'
        ResourceAggregation = 'Presets\bin\resource-aggregation\ResourceAggregationChecks.exe'
        AutoExp = 'AutoExp\bin\native\AutoExpNativeChecks.exe'
    }
    Invoke-TestAllNative $Context 'native.build' $msbuild @(
        (Join-Path $checks 'NativeVerification.proj'), '/restore', '/t:Build', '/m:1',
        '/p:Configuration=Release', "/p:GameFolder=$GameFolder", "/p:ManagedPath=$managed",
        "/p:NativeSuites=$($Suite -join '|')", '/v:minimal', '/nologo'
    )

    $plugin = Join-Path $Repo 'NGUInjector\bin\Release\NGUInjector.dll'
    try {
        $testedHash = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
        $Context.NativeArtifact = [ordered]@{
            path = $plugin
            sha256 = $testedHash
            mvid = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($plugin)).ManifestModule.ModuleVersionId.ToString()
            suites = $Suite
        }
    } catch {
        $Context.Report.errors.Add(@{ phase = 'native.artifact'; status = 'FAIL'; message = $_.Exception.Message })
        throw
    }

    $failures = [Collections.Generic.List[string]]::new()
    foreach ($name in $Suite) {
        $arguments = @($plugin, $managed) + $Selector
        if ($name -eq 'ResourceAggregation') { $arguments = @() }
        if ($name -eq 'OrdinaryPolicy') { $arguments = @($managed, $plugin, (Join-Path $checks 'bin\BlackBarChecks.exe')) + $Selector }
        try { Invoke-TestAllNative $Context "native.$name" (Join-Path $checks $fixtures[$name]) $arguments }
        catch { $failures.Add($name) }
    }
    try {
        if ((Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash -ne $testedHash) {
            throw 'Production binary changed during native verification; no native pass is claimed.'
        }
    } catch {
        $Context.Report.errors.Add(@{ phase = 'native.artifact'; status = 'FAIL'; message = $_.Exception.Message })
        $failures.Add('artifact integrity')
    }
    if ($failures.Count) { throw "Native verification failed: $($failures -join ', '). No full native pass is claimed." }
}
