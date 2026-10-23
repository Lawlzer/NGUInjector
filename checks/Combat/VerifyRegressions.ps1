param([string]$ScratchRoot = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $ScratchRoot -PathType Container)) { throw 'Source mutation scratch parent is unavailable.' }
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../NGUInjector'))
$project = Join-Path $PSScriptRoot 'CombatChecks.csproj'
$mutations = @(
    @{ Name = 'prompt-inversion'; File = 'Managers/CombatManager.cs'; From = 'if (waldoSays)'; To = 'if (!waldoSays)'; Failure = 'FAIL: true prompt' },
    @{ Name = 'attack-reservation'; File = 'Managers/CombatManager.cs'; From = 'var budget = WalderpSecondsUntilPrompt() - .25f;'; To = 'var budget = WalderpSecondsUntilPrompt() + 100f;'; Failure = 'FAIL: attack 3 cooldown' },
    @{ Name = 'inclusive-rule'; File = 'AdventureRule.cs'; From = 'power >= MinPower'; To = 'power > MinPower'; Failure = 'FAIL: inclusive AND thresholds none' }
)
$scratch = Join-Path $ScratchRoot ('combat-decisions-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
try {
    foreach ($mutation in $mutations) {
        $root = Join-Path $scratch $mutation.Name
        [void][IO.Directory]::CreateDirectory((Join-Path $root 'Managers'))
        foreach ($relative in @('AdventureRule.cs', 'Main.CustomAdventure.cs', 'Managers/CombatManager.cs', 'Managers/CombatHelpers.cs', 'Managers/ZoneHelpers.cs')) {
            $text = [IO.File]::ReadAllText((Join-Path $sourceRoot $relative))
            if ($relative -eq $mutation.File) {
                $first = $text.IndexOf($mutation.From, [StringComparison]::Ordinal)
                if ($first -lt 0 -or $first -ne $text.LastIndexOf($mutation.From, [StringComparison]::Ordinal)) { throw "Ambiguous mutation anchor: $($mutation.Name)" }
                $text = $text.Replace($mutation.From, $mutation.To)
            }
            [IO.File]::WriteAllText((Join-Path $root $relative), $text)
        }
        $output = & dotnet run --project $project -c Release "-p:CombatSourceRoot=$root" "-p:BaseIntermediateOutputPath=$root/obj/" "-p:BaseOutputPath=$root/bin/" 2>&1 | Out-String
        if ($LASTEXITCODE -ne 1 -or !$output.Contains($mutation.Failure) -or $output.Contains('error CS')) { throw "Missing behavioral rejection for $($mutation.Name):`n$output" }
        "PASS: source decision mutant $($mutation.Name) rejected by executed assertions."
    }
} finally { [IO.Directory]::Delete($scratch, $true) }
