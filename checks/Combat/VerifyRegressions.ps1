param(
    [string] $ScratchRoot = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'opencode')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ScratchRoot -PathType Container)) {
    throw "Scratch parent does not exist: $ScratchRoot"
}
$projectPath = Join-Path $PSScriptRoot 'CombatChecks.csproj'
$sourcePath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../NGUInjector/Main.CustomAdventure.cs'))
$source = [System.IO.File]::ReadAllText($sourcePath)
$mutations = @(
    @{
        Name = 'boss90-cutoff'
        Original = 'var rebirthSeconds = Character.rebirthTime.totalseconds;'
        Replacement = 'if (ZoneHelpers.CurrentRebirthEffectiveBossId() >= 90) { reason = "regression: boss 90 cutoff"; return false; }' +
            [Environment]::NewLine + '            var rebirthSeconds = Character.rebirthTime.totalseconds;'
        ExpectedFailure = 'FAIL: gold eligibility mode=0, boss=90, seconds=1017;'
    },
    @{
        Name = 'disabled-death-guard'
        Original = 'if (_customAdventureGoldPushEnteredZone && (Character.adventure.zone == -1 || Character.adventure.curHP <= 0))'
        Replacement = 'if (_customAdventureGoldPushEnteredZone && Character.bossID == int.MinValue)'
        ExpectedFailure = 'FAIL: death mode=0, observedEnemy=False, safeZone=False: cleanup before farm;'
    }
)

$scratch = Join-Path $ScratchRoot ('combat-regressions-' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($scratch)
try {
    foreach ($mutation in $mutations) {
        $first = $source.IndexOf($mutation.Original, [StringComparison]::Ordinal)
        if ($first -lt 0 -or $first -ne $source.LastIndexOf($mutation.Original, [StringComparison]::Ordinal)) {
            throw "Mutation anchor changed or is ambiguous: $($mutation.Name)"
        }
        $directory = Join-Path $scratch $mutation.Name
        [void][System.IO.Directory]::CreateDirectory($directory)
        $mutatedSource = Join-Path $directory 'Main.CustomAdventure.cs'
        [System.IO.File]::WriteAllText($mutatedSource, $source.Replace($mutation.Original, $mutation.Replacement))

        # Clone project links into scratch; production and test files remain untouched.
        $project = [xml][System.IO.File]::ReadAllText($projectPath)
        foreach ($compile in $project.Project.ItemGroup.Compile) {
            $absolute = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $compile.Include))
            if ($absolute -eq $sourcePath) { $absolute = $mutatedSource }
            $compile.SetAttribute('Include', $absolute)
        }
        $defaultItems = $project.CreateElement('EnableDefaultCompileItems')
        $defaultItems.InnerText = 'false'
        [void]$project.Project.PropertyGroup.AppendChild($defaultItems)
        foreach ($testSource in [System.IO.Directory]::GetFiles($PSScriptRoot, '*.cs')) {
            $compile = $project.CreateElement('Compile')
            $compile.SetAttribute('Include', $testSource)
            [void]$project.Project.ItemGroup.AppendChild($compile)
        }
        $temporaryProject = Join-Path $directory 'CombatChecks.csproj'
        $project.Save($temporaryProject)
        $output = & dotnet run --project $temporaryProject 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        $evidence = [regex]::Match($output, '(?m)^' + [regex]::Escape($mutation.ExpectedFailure) + '[^\r\n]*').Value
        if ($exitCode -ne 1 -or -not $evidence) {
            throw "Expected assertion failure missing for $($mutation.Name) (exit $exitCode):`n$output"
        }
        "PASS: $($mutation.Name) rejected by executed production-route checks (exit $exitCode)."
        $evidence
    }
}
finally {
    [System.IO.Directory]::Delete($scratch, $true)
}
