$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $root 'NGUInjector'
$assertions = 0
function Assert-Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:assertions++
}
function Read-Source([string]$name) { [IO.File]::ReadAllText((Join-Path $source $name)) }
function Method-Source([string]$text, [string]$name) {
    $match = [regex]::Match($text, '(?ms)^        private (?:static )?bool ' + $name + '\([^\r\n]*\)\r?\n        \{.*?^        \}')
    Assert-Check $match.Success "Production method not found: $name"
    $match.Value
}

$owned = @('Main.ExecutionStore.cs', 'Main.ExecutionPersistence.cs', 'Main.ExecutionIngress.cs',
    'Main.ExecutionLifecycle.cs', 'Main.ProtectedTiming.cs', 'Main.ExecutionTelemetry.cs', 'Main.SaveTests.cs')
foreach ($name in $owned) {
    $text = Read-Source $name
    Assert-Check ($text -notmatch 'FiniteAction(Ledger|Record|State)|\bFiniteActions\b|_finiteActionLedger') "$name still depends on retired action history"
    Assert-Check ($text -notmatch 'finite-actions-v1|lifecycle-route-commitments|route-execution-v1|ImportRouteExecutionCommitments|ImportReviewedFightPreparation|CurrentFightPreparationCommitment') "$name still reads or imports a retired process"
    Assert-Check ($text -notmatch 'Directory\.EnumerateFiles\(|stage-rebirth-|stage-recovery-') "$name still scans or accumulates per-attempt files"
}
$store = Read-Source 'Main.ExecutionStore.cs'
$ingress = Read-Source 'Main.ExecutionIngress.cs'
$lifecycle = Read-Source 'Main.ExecutionLifecycle.cs'
$timing = Read-Source 'Main.ProtectedTiming.cs'
Assert-Check ($store -notmatch 'File\.ReadAllText|WriteAtomic|StandaloneSelectionPath|EnsureExecutionExactLoaded') 'Transient selections still cross restart'
Assert-Check ($ingress -notmatch '\bFile\.|WriteAtomic|ExecutionSetupPath|fight-prep') 'Transient ingress still uses a process file'
Assert-Check ($lifecycle -notmatch '\bFile\.|WriteAtomic|PersistLifecycleExecutionEvidence|EnsureStandaloneSelectionLoaded') 'Lifecycle still imports or archives process evidence'
Assert-Check ($timing.Contains('ImportExecutionSelection(selection, out _)')) 'Current frozen stage intent is not restored'
Assert-Check (!$timing.Contains('Review the acquired selection before resuming this entry.')) 'Normal stage reload still requires approval'
Assert-Check ($timing.Contains('_protectedScheduler.AutoCleanupRecoveryRequired = false;')) 'Restart still imports cleanup authority'
Assert-Check (!$timing.Contains('if (setup["cancelled"].AsBool) _automaticCancelledEntry')) 'Restart turns cancellation into completion'
foreach ($field in @('fightEntry', 'scheduler', 'automaticSetup', 'automaticAtContinuation', 'selectedPolicy')) {
    Assert-Check ($timing.Contains('saved["' + $field + '"]')) "Required stage checkpoint field removed: $field"
}

# Compile only the actual pure rollover predicate and JSON codecs in memory.
# This does not build the injector, reference game assemblies, or touch a runtime.
$json = Read-Source 'JsonFiles.cs'
$simple = Read-Source 'SimpleJson.cs'
$globalPresets = Read-Source 'Main.GlobalPresets.cs'
$methods = (Method-Source $timing 'ProtectedCheckpointForwardRebirth') + "`n" +
    (Method-Source $timing 'FiniteCheckpointNumber') + "`n" +
    (Method-Source $globalPresets 'EquipmentDescriptorInteger')
$finite = [regex]::Match((Read-Source 'Managers/LoadoutPresetPlanner.cs'), '(?m)^        internal static bool Finite\(double x\) \{[^\r\n]+\}').Value
Assert-Check ($finite.Length -gt 0) 'Production planner finite-number predicate not found'
$usings = @([regex]::Matches($json + "`n" + $simple, '(?m)^\s*using [^\r\n]+;') | ForEach-Object { $_.Value.Trim() })
$usings += 'using System.Linq;'
$preamble = ($usings | Sort-Object -Unique) -join "`n"
$json = [regex]::Replace($json, '(?m)^\s*using [^\r\n]+;\r?\n', '')
$simple = [regex]::Replace($simple, '(?m)^\s*using [^\r\n]+;\r?\n', '')
$probe = @"
namespace NGUInjector {
    internal static class LoadoutPresetPlanner {
$finite
    }
    public sealed class ExecutionPersistenceRolloverProbe {
        private string _protectedPlanFile = "fixture";
$methods
        public bool Accept(string json, long rebirth, double origin, double total) {
            return ProtectedCheckpointForwardRebirth(JsonFiles.ParseObject(json), rebirth, origin, total);
        }
    }
}
"@
Add-Type -TypeDefinition ($preamble + "`n" + $json + "`n" + $simple + "`n" + $probe)
foreach ($path in @($owned | ForEach-Object { Join-Path $source $_ }) + @(
    (Join-Path $PSScriptRoot 'ExecutionPersistenceChecks.cs'), (Join-Path $PSScriptRoot 'RetainedFarmingRebindChecks.cs'))) {
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($path))
    $errors = @($tree.GetDiagnostics() | Where-Object { $_.Severity.ToString() -eq 'Error' })
    Assert-Check ($errors.Count -eq 0) "C# syntax errors in ${path}: $($errors -join '; ')"
}
$checker = [NGUInjector.ExecutionPersistenceRolloverProbe]::new()
$original = '{"version":2,"plan":"fixture","generation":"0123456789abcdef0123456789abcdef","rebirth":"7","origin":100,"elapsed":50,"total":150}'
Assert-Check ($checker.Accept($original, 8, 200, 210)) 'Native +1 rollover was rejected'
Assert-Check (!$checker.Accept($original, 7, 100, 160)) 'Same run was mistaken for rollover'
Assert-Check (!$checker.Accept($original, 6, 200, 210)) 'Backward rebirth was accepted'
Assert-Check (!$checker.Accept($original, 9, 200, 210)) 'Multiple unverified rebirths were accepted'
Assert-Check (!$checker.Accept($original, 8, 149, 210)) 'Rollover accepted a current run originating before saved total progression'
Assert-Check (!$checker.Accept($original, 8, 200, 149)) 'Rollover accepted lost total progression'
foreach ($replacement in @(@('"version":2', '"version":999'), @('"plan":"fixture"', '"plan":"other"'),
    @('"generation":"0123456789abcdef0123456789abcdef"', '"generation":"invalid"'), @('"rebirth":"7"', '"rebirth":7'),
    @('"elapsed":50', '"elapsed":-1'), @('"origin":100', '"origin":90'), @('"total":150', '"total":40'))) {
    Assert-Check (!$checker.Accept($original.Replace($replacement[0], $replacement[1]), 8, 200, 210)) "Rollover accepted corrupt envelope: $($replacement[1])"
}
"PASS: $assertions persistence source/pure rollover assertions. Native integration is a separate gate."
