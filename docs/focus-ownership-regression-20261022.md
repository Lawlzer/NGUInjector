# Focus Ownership Regression

## Outcome

Ownership blocker CLOSED. Final Release MVID:
`c5602156-049f-49f6-9b4a-93946b24cb90`.
The existing Walderp source changes were preserved; no live access, deployment,
restart, injection, commit, nested agent or parent WIP edit occurred.

## Proven Cause

The unchanged valid Magic NGU scenario failed on prior production MVID
`62a299de-9571-4a38-9ae3-9627f67fe666`, with Energy management disabled, an enabled
scheduled plan and exact preexisting Energy/R3 allocations including idle.

Native fixture observation of the REAL compiled `FocusSetupCurrent` return showed:

```text
requested=; acquired=MagicNgu; selection=True; epoch=True; settings=True; file=True
```

`ApplyCustomFocusGoal` published the new requested goal BEFORE pausing the old
plan. Setting `AutoRebirth=false` calls `NotifyCustomPlanRunChanged` while
`UseCustomRebirthPlan` is still true. `InvalidateCustomRuntimePlanCache` therefore
calls `ClearCustomSustainedFocus`, removing the newly published request. The new
retained selection then fails its legitimate requested-owner equality predicate
at shared setup admission. This was a production ordering regression, NOT an
obsolete fixture or an unmanaged-resource permission failure.

## Minimal Fix

Moved `PauseCustomRebirthForManualFocus(messages)` ahead of new-request publication,
after existing read-only preflights and handoff admission. All settings callbacks
retire the old owner before the new focus is published. No authority predicate,
resource/native model/epoch/foreign check or capacity protection was relaxed.

The Ownership fixture now has a `magic-focus` selector and an observation-only
Cecil hook on `FocusSetupCurrent` returns. It preserves the actual returned bool
and does not replace any policy. Native tests cover both Energy management modes,
both auto-rebirth states and affordable/infeasible swaps. Valid focus must apply,
have zero failed current-focus observations and zero Energy recalls, pause the
old plan, and retain the complete typed Energy/R3 allocation/idle/current map.
Existing infeasible Energy/R3 and idle-capacity cases remain unchanged.

## Verification

All native rows used the SAME final DLL. Existing Framework runners other than
Ownership were reused; Ownership was rebuilt after production. No historical
result is substituted for a final same-DLL run.

| Check | Result |
| --- | --- |
| Production Release rebuild | PASS |
| Ownership Framework rebuild | PASS; existing linked-fixture CS0649 warning |
| Native Ownership `magic-focus` | PASS 129 |
| FULL Ownership, no selector | PASS 10,294 |
| FULL CombatPrep, no selector | PASS 11,985; public focus/combat 428 |
| FULL Preset, no selector | PASS 13,528; inline279 and Walderp AT28 |
| FULL BlackBar | PASS 24,081 |
| Source Focus | PASS 2,638 |
| Source Combat | PASS 10,428 |
| Source Support | PASS 31,089 |
| Source ResourceWriter | PASS 1,494 |
| Source ExternalUi | PASS 9,646 |
| `git diff --check` | PASS |

## Exact Commands

Working directory: `A:\misc\NGU-Automation\NGUInjector`. Full native/source runs
used timeout1200000ms. The five source suites ran in parallel, followed by parallel
isolated native runners with unique fixture directories.

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "NGUInjector.sln" /restore /t:Rebuild /p:Configuration=Release /p:GameFolder="C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\Ownership\OwnershipChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "checks\Ownership\bin\OwnershipChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed" magic-focus
& "checks\Ownership\bin\OwnershipChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\PresetIntegration\bin\PresetIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\bin\BlackBarChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
dotnet run --project "checks\SustainedFocus\SustainedFocusChecks.csproj" -c Release
dotnet run --project "checks\Combat\CombatChecks.csproj" -c Release
dotnet run --project "checks\Support\SupportChecks.csproj" -c Release
dotnet run --project "checks\ResourceWriter\ResourceWriterChecks.csproj" -c Release
dotnet run --project "checks\ExternalUi\ExternalUiChecks.csproj" -c Release
git diff --check
```

Before the fix, full Ownership and the new focused selector both reproduced the
failure against62a299de. The focused diagnostic proved the exact failed predicate.

## Changed Files

- `NGUInjector/Main.CustomFocus.cs`: ordering fix and causal comment only.
- `checks/Ownership/Program.cs`: focused selector, read-only authority observation,
  expanded native callback/permission matrix and stronger preservation assertions.
- `docs/focus-ownership-regression-20261022.md`: this report.
- `docs/combat-executor-verification-20261022.md`: pointer to latest evidence.

The parent retains live zero-idle/Walderp observations and remaining release work.
