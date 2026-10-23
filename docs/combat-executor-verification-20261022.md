# Combat Executor Verification

## Status

Latest follow-up: the native Ownership focus regression is CLOSED on Release MVID
`c5602156-049f-49f6-9b4a-93946b24cb90`. FULL Ownership10,294, CombatPrep11,985,
Preset13,528, BlackBar24,081 and all five requested source suites PASS against
the final source/DLL. Walderp changes are preserved. Exact callback diagnosis,
minimal ordering fix, commands and file manifest:
[Focus Ownership Regression](focus-ownership-regression-20261022.md).

**Public-command combat acceptance PASS. The three concrete combat handoff
blockers are closed; this is offline combat-slice acceptance, NOT the full release/live gate.**

Final Release DLL MVID: `59d239d5-eea5-41b7-a39e-45041fc8b5fe`.
Production build PASS. FULL native CombatPrep (NO selector) PASS **11,985**,
including shared setup1,100 and **428 public focus/combat transition assertions**.
Native inline PASS **279** on the SAME DLL; SDK source Focus PASS **2,638**.
`git diff --check` PASS. The requested15-minute target was exceeded during full
verification; no live access, deployment, commits or parent WIP edits occurred.

### Public Command Acceptance

- Replaced the blanket stop-focus blocker with read-only readiness validation:
  current retained selection/epoch, applied owner, no pending activation, exact
  gear/material/support, pinned support feasibility, verified resource outcome,
  still-owned managed focus lanes and no unresolved shared resource evidence.
  Existing route, lock, permission and shared conflict gates remain intact.
- Preserved the existing idle-only policy for `ApplyCustomFocusGoal` and external
  `focusGoal`/`stopFocus`. Removed the prior pass's internal Stop-during-combat
  special case. NO replacement-focus feature, UI or configuration was added.
- Native acceptance now enters through REAL `ExecuteExternalUiCommand` dispatch:
  `focusGoal` -> `previewCombatPrep` -> `startCombatPrep` -> cancellation/completion
  -> normal `ReconcileCustomResources`. No private acquired run, fabricated preview
  cache or rewritten policy/deadline shortcut is used in this focus handoff test.
- Both MagicNgu and EnergyNgu cover cancel, completion, authorized stop after
  cancellation, existing lifecycle clear, foreign material, changed epoch and
  known-partial gear failure/retry. Actual retained gear/support and native funding
  verify; optimizer counts do not increase from acquired combat through handoff.
  Entry and native W levels/partial progress survive; rejected replacement/stop
  commands preserve choices, run/policy, phase/deadline, allocations and support
  with zero gear/resource writes or searches.
- Rejection tests use actual incomplete native support application, foreign gear,
  legal foreign support levels, changed rebirth, foreign resource allocation and
  clock rollback. Public Start rejects before token consumption/native mutation;
  fresh public Preview reports executable candidates unavailable with a concrete
  current-focus reason. No new concrete safety blocker remains for this scope.

Exact files changed in this public-command pass:

```text
NGUInjector/Main.CombatPrep.cs
NGUInjector/Main.CustomFocus.cs
checks/CombatPrepIntegration/SharedCombatSetupChecks.cs
docs/combat-executor-verification-20261022.md
docs/COMBAT_PREP_BACKEND.md
docs/AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md
docs/execution-writer-inventory.md
```

Actual final verification commands, from the repository root:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "NGUInjector.sln" /restore /t:Rebuild /p:Configuration=Release /p:GameFolder="C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\CombatPrepIntegration\CombatPrepIntegrationChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& "checks\PresetIntegration\bin\PresetIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed" --inline-stage-setup
dotnet run --project "checks\SustainedFocus\SustainedFocusChecks.csproj" -c Release
git diff --check
```

Production was rebuilt before Framework fixture verification. The existing rebuilt
Preset runner loaded the same final production DLL. Native full timeout1200000ms.
Initial diagnostics caught a C# catch-variable collision and a test's illegal
foreign digger level above its purchased maximum; both were corrected before the
final complete PASS. FullPreset/Ownership/BlackBar/UI release matrix and all live
checks remain outside this pass. Parent owns live zero-idle and later Walderp checks.

## Prior Retained Focus Evidence

**Retained focus producer implemented and native internal handoff verified.
Public focus-origin combat admission remains guarded; the combat slice is NOT complete.**

Latest Release DLL MVID: `df2754cf-fec8-4fc7-9bd5-bc4122242e56`.
Production Release build and Framework fixture builds PASS. Final SAME-DLL complete
CombatPrep runner PASS **11,457**, including **66** new retained-focus assertions
and shared setup572. Native inline selector PASS **279**. SDK source Focus PASS
**2,638** (game/controller boundaries stubbed). `git diff --check` PASS.

### Retained Focus Pass

- Focus now retains its acquired exact gear/support, acquisition identity, material,
  coordinator/settings/plan/native epoch and bounded observations independently of
  mutable `_selectedSetup`. Recurring recovery reconciles these requirements through
  shared actuation, rather than rerunning the full focus workflow or searching again.
- Shared verified/known-partial transitions update focus observations; causally owned
  auto-boost updates the same selected material. Stop during combat clears focus intent
  without recalling the combat owner's current allocations. Periodic support refresh
  rejects externally changed setup before selecting/applying support.
- Native acceptance covers actual focus -> acquired combat gear -> current focus,
  partial native gear failure/retry, stop, foreign material/support-refresh rejection
  and rebirth epoch invalidation. It proves actual gear/support/resource verification,
  unchanged search counters and preserved combat entry/deadline.
- IMPORTANT: this new internal test constructs an acquired run and calls the REAL
  `BeginCombatPrepRun`; it does NOT prove public admission. The existing public guard
  test remains unchanged. Read-only acquisition/publication of a replacement focus
  during combat is still unimplemented (`ApplyCustomFocusGoal` requires idle combat).
  That successor/supersession contract and public command acceptance must be completed
  before removing the guard. No global conflict check was waived.
- Source Focus boundaries now provide an explicitly acquired setup and real numeric
  support preflight. Its indefinite-time scenario advances both native clocks together,
  rather than simulating a different save origin while claiming passive elapsed time.

Exact manual file changes for this retained-focus pass:

```text
NGUInjector/Main.CustomFocus.cs
NGUInjector/Main.GlobalPresets.cs
checks/CombatPrepIntegration/SharedCombatSetupChecks.cs
checks/SustainedFocus/Stubs.cs
checks/SustainedFocus/GameAllocationBoundary.cs
checks/SustainedFocus/EquipmentSetupChecks.cs
docs/combat-executor-verification-20261022.md
```

Commands: same production MSBuild command below, followed by Framework rebuilds of
`checks\CombatPrepIntegration\CombatPrepIntegrationChecks.csproj` and
`checks\PresetIntegration\PresetIntegrationChecks.csproj` with
`/restore /t:Rebuild /p:Configuration=Release /verbosity:minimal`.
Final complete CombatPrep EXE used the same DLL/Managed arguments below, NO selector,
timeout1200000ms. Native Preset EXE used those arguments plus `--inline-stage-setup`.
Source command: `dotnet run --project "checks\SustainedFocus\SustainedFocusChecks.csproj" -c Release`.
The new native selector was initially run with `--check CheckRetainedFocusCombatTransition`
on intermediate MVID`685c5a12-4f7e-444b-b53d-283b37256d8d` (PASS67 including setup);
the final complete run includes its strengthened66-case-assertion group.
No broad FullPreset/Ownership/BlackBar/UI matrix was rerun in this bounded pass.
No parent WIP edit, live bridge/game/settings access, deployment, restart, injection
or commit occurred. Parent retains live Energy/Walderp responsibility.

## Prior Resume Evidence

**Two native handoff failures CLOSED in the bounded resume pass. Focus producer
dependency remains guarded; this is not full combat/release completion.**

Current built Release DLL MVID: `91d968ea-13eb-402c-91ae-3dadb3322432`.
Production build, native fixture rebuild and COMPLETE CombatPrep runner PASS
**11,391** assertions. Original acceptance cases are unchanged: identical TM
Energy10/Magic20 handoff keeps accounting writes2 at2, and paused borrowed
AT-3/AT-4/nuke fields return5000/6000/17. Additional native pause checks preserve
foreign field edits, replacement target/timer models and changed epochs.

The parent owns live Energy observations and Walderp work. This bounded pass did
not access any live bridge/game/settings/plans, operational scripts, deployment
folder or parent WIP. No commit, restart or injection was performed.

### Bounded Resume Changes

- `EndCombatPrep` delegates resource reconciliation to the CURRENT selected dashboard or scheduled resource intent instead of submitting controlled-empty first. Scheduled successor existence is resolved on a detached pure scheduler copy with no entry/credit mutation. Standalone/no-successor and unload retain authorized resource release; no NGU fallback is added.
- Borrowed setting return authorization now binds current coordinator/run, native epoch, model/field identities, original selected file and applicable entry independently of enabled forward-process admission. Target leases additionally bind settings instance, focus and preset publication. Foreign values are preserved.
- Before-model nuke return failures retain the still-owned lease and retry to verified completion. Committed/render-failed returns are consumed once; foreign/epoch/model changes do not authorize a return.
- Exit-fault tests distinguish an actual no-successor release from the current successor's own rejected resource preflight. They require zero old-combat empty-map writes and zero fabricated successor credit. Resource-release fault scenarios explicitly use a terminal row or unload, rather than injecting an unused failure into a nonexistent old release.
- Focus setup publication is NOT implemented in this bound. The existing active-focus admission guard remains. Do not remove it or claim focus-origin combat handoff complete.

Exact files changed in this bounded pass:

```text
NGUInjector/Main.CombatPrep.cs
checks/CombatPrepIntegration/AutomaticReconciliationRegressions.cs
checks/CombatPrepIntegration/SharedCombatSetupChecks.cs
checks/CombatPrepIntegration/ReviewRegressions.cs
checks/CombatPrepIntegration/CleanupRecoveryRegressions.cs
checks/CombatPrepIntegration/ActivityTimingRegressions.cs
checks/CombatPrepIntegration/AutoBoostRegressions.cs
checks/CombatPrepIntegration/PresetInterruptionRegressions.cs
docs/combat-executor-verification-20261022.md
```

Actual commands used, from `A:\misc\NGU-Automation\NGUInjector`:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "NGUInjector.sln" /restore /t:Rebuild /p:Configuration=Release /p:GameFolder="C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\CombatPrepIntegration\CombatPrepIntegrationChecks.csproj" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed" --check CheckCombatCurrentResourceHandoff
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed" --check CheckCombatPausedBorrowedFields
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed" --check CheckCombatPausedForeignBorrowedFields
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed" --check CheckCombatBorrowedTimerOutcomes
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
```

Builds PASS. Final SAME-DLL targeted cases PASS17, PASS15, PASS33 and PASS34.
`git diff --check` PASS. Paused return retries retain the half-second cadence,
rather than retrying a rejected native setter every frame. Final rebuilt NO-selector suite
PASS11,391 (shared setup506, activity timing1,459, preset interruption175,
adventure685; real compiled native models/accounting). Full native timeout was
1200000ms. Earlier diagnostic full runs identified old assertions demanding a
combat clear before a successor; these were updated to assert the actual current
successor's typed failure/direct recovery, not weakened to accept extra writes.
Broader Preset/Ownership/BlackBar/source/UI release matrix was not rerun in this
bounded pass; historical results below do not validate this new DLL.

## Prior Slice Evidence

The following records describe the prior blocked MVID and are superseded for the
two handoff failures by the bounded resume results above.

**Prior status: BLOCKED, not a completed combat vertical slice or release gate.**

Built Release DLL MVID: `4ee188e9-abc2-40db-92b8-7a9e9e8e9dec`.
The complete rebuilt CombatPrep runner fails two new native acceptance gates.
Active legacy focus is additionally guarded before combat admission because its
producer does not retain independently publishable current setup intent.

No commit, injection, deployment, game restart, live library write or live
preference change was performed. The requested final `24hour-auto-rebirth` state,
enabled AutoRebirth/Energy/Magic, actual funding and four 15-minute checkpoints
remain behind the unchanged FULL release gate in `../../wip.md`.

## Blocking Findings

1. **Resource exit still competes with current intent.**
   `Main.CombatPrep.cs:546,998,1064`: `EndCombatPrep` submits controlled-empty
   through `RecallCombatPrepResources` before the current baseline resolves.
   `CheckCombatCurrentResourceHandoff` uses actual native TM allocations Energy10
   and Magic20. Baseline, combat and final successor allocations are identical;
   gear does not change, but the native accounting-write counter goes **2 -> 6**.
   The four extra writes are emptying and refilling, not duplicated resource
   points. The current shared executor's most recent combat map is not an
   independently retained current-baseline requirement. Publish/resolve the
   CURRENT successor resource intent before release; do not restore old amounts.

2. **Pause discards still-owned borrowed fields.**
   `Main.CombatPrep.cs:889,998,1125,1146`: narrow return closures use process
   admission (`CombatPrepEntryAuthority`), and retirement clears the records when
   the selected plan is disabled. A real unfinished AT run borrows targets
   5000/6000 and timer17, holding them at 0/0/0. Pause keeps original entry/deadline
   but leaves actual native values **0/0/0**, not **5000/6000/17**.
   Separate per-field borrowed-setting return authority from active process
   authority while retaining exact epoch, entry, model and foreign-owner guards.
   Do not fix this by allowing generic old-owner cleanup after handoff.

3. **Legacy focus does not publish retained desired setup.**
   `Main.CustomFocus.cs:470-472,545,671-733`: pending activation reuses mutable
   `_selectedSetup`; successful activation discards its context, and sustained
   execution resumes resources only. Combat replaces `_selectedSetup` with its
   own requirements. There is no independent producer-owned focus selection and
   domain-control contract for a correct focus successor. Calling the complete
   focus workflow would search/reset decisions; recording physical entry gear
   would recreate a historical return ladder. `CombatPrepStartBlocker` therefore
   rejects an active focus before consuming the token or mutating anything.
   Native `CheckCombatFocusAdmission` passes35 assertions using actual applied
   focus gear/support/funding, not a writable compatibility-success flag.

The first two failures are included in `CheckSharedCombatSetup`, which is included
in the NO-selector COMPLETE CombatPrep runner. Its aggregate reports both. They
are deliberate release gates, not assertions to relax or hide behind selectors.

## Implemented Behavior

- `AdvanceCombatPrep` retains process selection/timing; coordinator service applies concrete phase requirements. `ResolveCombatPrepResources` is read-only native observation/math; the shared `ResourceSetupExecutor` performs exact differences.
- Gear/support use the existing `ApplySelectedSetup` and bounded support continuation. Known partial progress continues toward the same selected instances/support without rollback, replacement search or renewed deadlines.
- Original gear/support remain reporting and explicitly acquired Current-profile inputs. No implicit standalone historical return target remains. Current gear/support successor B applies directly; unclaimed setup is preserved.
- Shared native-model-scoped gear/support/route conflicts survive ordinary resets. Reviewed Equip/Preview+Apply and entry-bound durably accepted stage actions bind authority to current context/conflict revision; later foreign changes invalidate it.
- Explicit combat recall footprint is E/M, protecting basic training and unmanaged/yielded R3/E/M through native intermediate-cap checks.
- Allocated bar/model changes during support or native route callbacks enter shared scoped resource evidence. Healthy Energy continues after actual foreign Magic conflict without whole-phase credit or replayed accounting.
- Safe-zone callbacks cannot change gear/allocations and then have gear actuation adopt those changes. Adventure leases bind actual controller/model objects, zone, floors and idle state. Foreign routes are not reacquired on retry.
- Current verified baseline farming can retire through the same shared actuator before combat setup; the retirement witness is consumed, not retained as historical restore permission.
- Actual unload attempts owned native retirement before invalidating authority and refuses an unretired owned route. Foreign routes retain prerequisites.
- OS pin v2 records stable run/entry/action identity and invocation authorization before native reset; v1 remains readable. Unknown resets are not replayed. Verified model commit/render failure is distinguished; later native W progression is not reset.
- Borrowed timer fault checks retain actual native setter effects and distinguish rejected/committed/foreign results. No automatic-nuke preference or native W/AT progression is restored from snapshots. The separate pause defect above remains open.
- Recurring combat refresh does not enumerate full inventory/material/profile forecasts. Owned boosting emits causal same-instance material revisions, including a suspended baseline, without granting application authority or replacement-search permission under AutoUpdate=false.
- The UI removes obsolete restoration receipt rows/note and displays actual setup domain/reason alongside planned versus observed support. No visual redesign was performed.

## Deleted Paths

No source file was deleted. Removed production/C# fixture references:

```text
CombatPrepGearApplicationException
ApplyCombatPrepPhaseGear
EquipCombatPrepGear
ApplyCombatPrepPhaseSupport
RestoreCombatPrepSupport
AllocateCombatPrepPhase
_combatDirectResources
CombatPrepRun.Resources (private ResourceApplication)
CombatPrepRun.ResourceRelease (private ResourceApplication)
SupportReceipt / TouchedSupportLevels historical-return union
GearApplicationInProgress / GearRecoveryRequired / RestoreGear
CleanupRestoreGear / historical cleanup retry budget
SupportRestorationStatus / SupportRestorationReason / supportRestoration runtime UI
AutomaticCombatPrepRestorationAuthority (replaced by current entry authority)
```

The controlled-empty resource release policy and coupled paused return guards
remain, as exposed above. Therefore removal of private application instances is
NOT reported as removal of the complete competing release pipeline.

## Exact File Manifest

Paths are relative to this repository except `../wip.md`. This lists session edits,
not the extensive unrelated pre-existing worktree changes.
Generated browser artifacts are retained separately under
`NGUInjector.UI/test-results-combat-executor/`; ordinary build outputs are not
listed as manual source edits.

```text
NGUInjector/Main.CombatPrep.cs
NGUInjector/Main.AutoCombatPrep.cs
NGUInjector/Main.CombatPrepAdventure.cs
NGUInjector/Main.CombatPrepCapture.cs
NGUInjector/Main.ResourceCoordination.cs
NGUInjector/Main.CustomFocus.cs
NGUInjector/Main.AutomationLoop.cs
NGUInjector/Main.GlobalPresets.cs
NGUInjector/Main.CustomAutomation.cs
NGUInjector/Main.Lifecycle.cs
NGUInjector/Main.ManualAdventureOverlay.cs
NGUInjector/Main.ProtectedTiming.cs
NGUInjector/Main.LoadoutPresets.cs
NGUInjector/Managers/ResourceWriter.cs
checks/CombatPrepIntegration/Program.cs
checks/CombatPrepIntegration/AutomaticReconciliationRegressions.cs
checks/CombatPrepIntegration/ActivityTimingRegressions.cs
checks/CombatPrepIntegration/CombatPrepIntegrationChecks.csproj
checks/CombatPrepIntegration/AdventureWeightRegressions.cs
checks/CombatPrepIntegration/PhaseSupportIntegration.cs
checks/CombatPrepIntegration/CleanupRecoveryRegressions.cs
checks/CombatPrepIntegration/LiveResourceRegressions.cs
checks/CombatPrepIntegration/ReviewRegressions.cs
checks/CombatPrepIntegration/AutoBoostRegressions.cs
checks/CombatPrepIntegration/PresetInterruptionRegressions.cs
checks/CombatPrepIntegration/EquipmentReplanRegressions.cs
checks/CombatPrepIntegration/NativeProgressionIntegration.cs
checks/CombatPrepIntegration/SharedCombatSetupChecks.cs (new)
checks/CombatPrepIntegration/ScheduledIntegration.cs
checks/PresetIntegration/Program.cs
checks/Ownership/YggdrasilChecks.cs
checks/ExternalUi/Stubs.cs
checks/ExternalUi/Program.cs
NGUInjector.UI/src/lib/CombatPrep.svelte
NGUInjector.UI/tests/combat-prep.test.ts
NGUInjector.UI/e2e/combat-prep.spec.ts
docs/COMBAT_PREP_BACKEND.md
docs/AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md
docs/frontend-global-presets.md
docs/execution-writer-inventory.md
docs/combat-executor-verification-20261022.md (new)
../wip.md
```

## Commands And Results

Working directory for C# commands: `A:\misc\NGU-Automation\NGUInjector`.
Full output/errors were captured by the terminal tools; native full suites used
timeout1200000ms. No `run_and_extract_errors` tool was available.

Production command, PASS:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "NGUInjector.sln" /restore /t:Rebuild /p:Configuration=Release /p:GameFolder="C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /verbosity:minimal
```

Each Framework fixture below was rebuilt with that SAME MSBuild executable and
the following exact suffix, PASS (fixture-only unused observer warnings retained):
`"<project>" /restore /t:Rebuild /p:Configuration=Release /verbosity:minimal`.

```text
checks\CombatPrepIntegration\CombatPrepIntegrationChecks.csproj
checks\PresetIntegration\PresetIntegrationChecks.csproj
checks\Ownership\OwnershipChecks.csproj
checks\BlackBarChecks.csproj
```

All native EXE rows below used these first two arguments verbatim:

```text
"NGUInjector\bin\Release\NGUInjector.dll"
"C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
```

Thus each row is `& "<EXE>" <the two arguments above> <selector>`; empty selector
means NO selector. All rows in this table use MVID4ee188e9.

| EXE | Selector | Actual Result |
| --- | --- | --- |
| `checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe` | none | **FAIL**, both handoff gates; legacy groups pass and shared native474 execute before acceptance failures |
| same Combat EXE | `--check CheckCombatCurrentResourceHandoff` | **FAIL**, write counter expected2, actual6 with unchanged actual allocations |
| same Combat EXE | `--check CheckCombatPausedBorrowedFields` | **FAIL**, expected5000/6000/17, actual0/0/0 |
| same Combat EXE | `--check CheckCombatFocusAdmission` | PASS35 |
| same Combat EXE | `--automatic-reconciliation` | PASS1,219 |
| same Combat EXE | `--resource-domains` | PASS220 |
| same Combat EXE | `--check CheckActivityTimingRegressions` | PASS1,462 |
| same Combat EXE | `--check CheckPhaseSupport` | PASS316 |
| same Combat EXE | `--check CheckScheduledCombatPrep` | PASS183 |
| same Combat EXE | `--check CheckCleanupRecoveryRegressions` | PASS155 |
| `checks\PresetIntegration\bin\PresetIntegrationChecks.exe` | none | PASS13,500, including inline279 |
| same Preset EXE | `--inline-stage-setup` | PASS279 |
| `checks\Ownership\bin\OwnershipChecks.exe` | none | PASS10,233 |
| same Ownership EXE | `yggdrasil` | PASS671; native fruit spending/max predicates/dispatch, reward/UI leaves substituted |
| `checks\bin\BlackBarChecks.exe` | none | PASS24,081, including protected timing2,393 |

The earlier combat baseline PASS10,825 was established before conversion on
MVID02eb7178. Before adding the blocking acceptance cases, the expanded runner
passed11,317 on MVID69a64e72. Neither is a current complete combat pass. An old
fixture executable also printed11,349 on MVID4ee188e9 before its final rebuild;
that is NOT evidence for the current acceptance suite.

Fresh SDK source-linked commands (game/action boundaries stubbed as declared by
their fixtures, not substitutes for the compiled native tests):

| Actual Command | Result |
| --- | --- |
| `dotnet run --project "checks\SustainedFocus\SustainedFocusChecks.csproj" -c Release` | PASS2,638 |
| `dotnet run --project "checks\Combat\CombatChecks.csproj" -c Release` | PASS10,416 |
| `dotnet run --project "checks\Support\SupportChecks.csproj" -c Release` | PASS31,089 |
| `dotnet run --project "checks\ResourceWriter\ResourceWriterChecks.csproj" -c Release` | PASS1,494 |
| `dotnet run --project "checks\ExternalUi\ExternalUiChecks.csproj" -c Release` | PASS9,646, including real lifecycle refusal/disposal ordering at the explicit combat boundary |
| `dotnet run --project "checks\CombatPrep\CombatPrepChecks.csproj" -c Release` | PASS6,689, pure planner mathematics unchanged |

Frontend working directory: `A:\misc\NGU-Automation\NGUInjector\NGUInjector.UI`.

| Actual Command | Result |
| --- | --- |
| `npm run check` | PASS, zero errors/warnings |
| `npm test` | PASS29 files /1,991 tests, including combat200 |
| `npm run build` | PASS |
| `Set-Item -Path Env:NGUI_TEST_PORT -Value 1438 && npx playwright test "e2e/combat-prep.spec.ts" --workers=2 --output="test-results-combat-executor"` | PASS42 desktop/mobile cases; real app/controller, mocked native IPC, no live actions |
| `git diff --check` (repository root) | PASS |

No separate C# linter is configured in this verification path. Native Tauri
packaging, all remaining finite-workflow/rebind/migration gates and live operation
were not run or claimed complete.

## Review And Next Work

Self-review only; no nested agents/sessions were used. Review found and native
regressions cover retirement callback adoption, foreign route/controller retries,
unverified idle-setting activation, suspended-baseline causal growth, explicit
restart review binding and old-epoch resource evidence attribution. Pure planner
mathematics, original AT/AUG/OS decisions, native progression and deadline rules
were not replaced by synthetic receipts or compatibility booleans.

The remaining blockers are implementation/producer-contract dependencies, not an
environment/tool permission problem. Next work must expose retained current
focus/setup and baseline resource requirements to shared resolution, then move
narrow borrowed-setting release authority out of active-process admission.
Remove the competing controlled-empty release only after actual successor intent
is available. Keep both failing native gates, preserve current entry/deadline,
and rerun the complete SAME-DLL matrix. Do not substitute physical entry snapshots,
old complete focus application, optimizer retry or historical allocation replay.
