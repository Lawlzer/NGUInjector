# Walderp AT Readiness

## Result

The Ownership blocker reported below is now CLOSED on final MVID
`c5602156-049f-49f6-9b4a-93946b24cb90`; all same-DLL broad suites pass.
See `focus-ownership-regression-20261022.md`. Live kill and resumed zero-idle
evidence is separately recorded in `live-energy-walderp-20261022.md`.
The following MVID62a299de evidence is historical, not current blocked status.

Narrow readiness fix implemented and compiled-native regression PASS. Broad
same-DLL verification is **BLOCKED by Ownership**, not accepted as a full release.
Production Release MVID: `62a299de-9571-4a38-9ae3-9627f67fe666`.
SHA256: `C353C306CEA6BD9FE3FD4DAF0B458FF7E090F09A9431D1E31561A4C2344B4C6E`.
The production DLL was rebuilt once; every native runner below used that DLL.

## Cause And Fix

The preset AT producer temporarily borrows positive configured native menu targets,
trains to those levels, then correctly returns the original menu fields. The manual
titan gate previously treated returned `0/0` fields as missing training even after
genuine completion. Parent supplied the live 3000/3000-level, 0/0-target diagnosis;
this pass did not access the live game to repeat it.

Positive native targets retain precedence: both must be positive and actually met.
Only exactly `0/0` may use the fallback. That fallback requires the current enabled
scheduled plan, matching selected file/structure/save epoch, durable completed
protected work without automatic cleanup recovery, and a genuinely completed,
non-skipped standard mandatory AT row with positive configured Power/Toughness
targets and actual native levels meeting both. Partial, negative, higher unmet
native targets, paused/switched plans, changed epochs, zero configured targets,
missing/non-durable receipts, restart and explicit skip all reject.

This intentionally uses the conservative existing whole-protected-work completion
gate, not a new per-row completion API. No targets are lowered or written, no
completion is granted, and no acquisition, scheduler advancement or budget renewal
is performed by readiness queries. Existing public focus replacement/stop semantics
and other actors are unchanged.

## Verification

| Check | Actual Result |
| --- | --- |
| Production Release rebuild | PASS |
| Four Framework fixture rebuilds | PASS; existing CS0649 warning in linked fixture |
| Full CombatPrep, no selector | PASS 11,985; public focus/combat 428, shared setup 1,100 |
| FullPreset, NO selector | PASS 13,528; includes inline 279 and new compiled-native AT 28 |
| Full Ownership | FAIL at Magic NGU focus application; see blocker below |
| Full BlackBar | PASS 24,081; protected timing 2,393 |
| Source Focus | PASS 2,638 |
| Source Combat | PASS 10,428; ManualTitan 482 |
| Source Support | PASS 31,089 |
| Source ResourceWriter | PASS 1,494 |
| Source ExternalUi | PASS 9,646 on retry with longer timeout |
| `git diff --check` | PASS |

The AT fixture retains compiled production ownership, scheduler, completion and
native target-lease code. Actual native `updateAdvancedTraining` ticks reach
configured Toughness20/Power10; normal scheduler completion durably records the
work and returns original native targets to 0/0. The real compiled manual-titan
gate then accepts. Negative controls exercise both native axes, missing/durable
receipts, plan pause/switch/disabled scheduler/zero targets, rebirth epoch,
restart and explicit skip. Repeated queries preserve scheduler state, native
targets and gear/resource/search counters. Source Combat adds 12 target/proof
dispatch cases; its scheduler boundary is explicitly a stub, not native evidence.

Ownership was run twice against the same production DLL. The second run only
added the returned focus error to the existing assertion diagnostic:

```text
Magic NGU focus receipt applied: ERROR in Apply custom focus MagicNgu:
Selected setup authority changed; native transition stopped.
expected=applied, actual=failed
OwnershipChecks.CheckMagicFocus / checks/Ownership/Program.cs:740
```

The preceding manual-rule/preset group passes 38 assertions, but Ownership stops
at this failure and emits no complete-suite assertion total. The failure is in
the existing focus application path; its deeper cause was not established in
this bounded pass. No focus authority gate or expected result was weakened.
ExternalUi's first run exceeded 120000ms after its adventure-rule group; the
1200000ms rerun completed successfully. No UI work was run because UI is unchanged.

## Commands

Executed from `A:\misc\NGU-Automation\NGUInjector`. Framework full runs used
tool timeout **1200000ms**. Setup `Test-Path` checks preceded output-producing
build/source commands. Commands below retain the actual flags and argument order.

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "NGUInjector.sln" /restore /t:Rebuild /p:Configuration=Release /p:GameFolder="C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\PresetIntegration\PresetIntegrationChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\CombatPrepIntegration\CombatPrepIntegrationChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\Ownership\OwnershipChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\BlackBarChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "checks\PresetIntegration\bin\PresetIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\Ownership\bin\OwnershipChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\bin\BlackBarChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
dotnet run --project "checks\Combat\CombatChecks.csproj" -c Release
dotnet run --project "checks\SustainedFocus\SustainedFocusChecks.csproj" -c Release
dotnet run --project "checks\Support\SupportChecks.csproj" -c Release
dotnet run --project "checks\ResourceWriter\ResourceWriterChecks.csproj" -c Release
dotnet run --project "checks\ExternalUi\ExternalUiChecks.csproj" -c Release
git diff --check
[System.Reflection.Assembly]::LoadFile("A:\misc\NGU-Automation\NGUInjector\NGUInjector\bin\Release\NGUInjector.dll").ManifestModule.ModuleVersionId
Get-FileHash -LiteralPath "NGUInjector\bin\Release\NGUInjector.dll" -Algorithm SHA256
```

The Ownership rebuild/EXE command was repeated after its diagnostic-only change;
the ExternalUi source command was repeated after timeout. After the fullPreset
PASS, only one assertion's description was corrected from original menu -1 to
original 0/0; executable assertions and production bytes were unchanged.

## Changed Files

- `NGUInjector/Main.ManualTitan.cs`: preserve native precedence, narrow 0/0 fallback.
- `NGUInjector/Main.ProtectedTiming.cs`: read-only current completed-plan proof.
- `checks/PresetIntegration/Program.cs`: real native zero-target return and 28 proof regressions.
- `checks/Combat/ManualTitanChecks.cs`: target/proof dispatch negatives and acceptance.
- `checks/Combat/Stubs.cs`: explicit compiled-native-covered scheduler boundary.
- `checks/Ownership/Program.cs`: assertion diagnostic only; exposes actual focus error.
- `docs/walderp-at-readiness-20261022.md`: this report.

## Limits

Full broad acceptance remains blocked by Ownership. Native AT uses small unequal
20/10 targets rather than replaying the live 3000/3000 duration; live spawn/fight,
scene rendering, resumed zero-idle and release/deployment acceptance are not claimed.
Existing offline fixture substitutions still apply to scene/controller leaves.
The parent retains those live checks and all broader executor/release tasks.

**No live game access, live settings/plan changes, bridge calls, injection,
deployment, restart or commits occurred. Parent WIP was read but not edited.
All pre-existing dirty tracked/untracked changes were preserved.**
