# Independent Safety Verification

## Status And Ownership

**Latest review: the focused retained-data NRE fix has no concrete failed proof
found in the inspected scope. Its six-timeline/455-assertion and full Preset 17,285 passes are
offline evidence, NOT a new full candidate or live acceptance.** The old 3c01e825
capture passed its recorded offline gate, was actually deployed, and FAILED live
acceptance. Preserve that distinction and its `liveVerified:false` status.

This review only inspected the post-rebind projection/model-commit boundary,
caller receipt publication, actual retained input and owned regressions. Only this
report was edited. No implementation, builds/tests/native runs, live/bridge/UI
actions, diagnostics/injection/attach, index changes, commits or nested agents.
No new diagnostic injection is proposed or permitted. The prior hash operation
described in historical closure evidence was not repeated in this review.

## Retained NRE Fix Review

### Result And Actual Cause

No new concrete defect was found in this narrow fix. The failed deployed payload
contains six legitimate digger decisions WITHOUT names; they were not unavailable
diggers or permission to select replacements. Import at
`Main.ExecutionPersistence.cs:242-260` retains IDs/levels/native parameters and does
not invent display names. `LoadoutPresetCandidateJson` previously fed a null Name
into JSONString; the payload-size ToString then reached Escape's string Length.
That error was after real native effects and before application receipt publication.

At `Main.LoadoutPresets.cs:1306-1310`, the repaired result label comes from the
CURRENT captured snapshot's digger with the SAME retained ID. The snapshot labels
are metadata (`Main.Support.cs:97-103`). The original ID, level, weight, drain
parameters, selected gear/support and acquisition are not replaced. There is no
blank-name substitute, serializer-wide null bypass, historical-production metric
fabrication or optimizer/surrogate selection added by label resolution.

### Common Commit And Publication

| Boundary | Inspected Proof And Assessment |
| --- | --- |
| Native effect versus result failure | `Main.LoadoutPresets.cs:833-840,855-894`: setup, native targets, shared funding and route checks finish BEFORE capture of the commit witness and the separate result-projection try/catch. A result error becomes ModelAppliedRenderFailed ONLY if RequirePreparedPresetCommit still verifies; otherwise the outer uncertain-application/quarantine path remains. The projection itself is read-only. |
| Coupled witness | `Main.LoadoutPresets.cs:921-964`: real common grant covers Gear/Support/Route/NativeSettings plus the actual controlled resource lanes. Its predicate binds coordinator/common revisions/lifecycle/permissions/claims, native epoch/context, exact selected setup/material/support, SAME resource-intent object and authority/map, current/idle pools, native settings/route and original scheduler/generation/entry/cycle. Matching amounts alone do not authorize publication; unresolved evidence in the named resource scope fails VerifyExecutionPrerequisites. |
| Policy-to-receipt handoff | `Main.LoadoutPresets.cs:1106-1129`: continuing authority allows only the exact applying policy OBJECT and owner to become its own published receipt, with the same valid epoch. Same preset ID or numeric equality is insufficient. The outer shared resource grant still checks its original model/permission/entry/material/support and domain/claim context. No fresh grant is synthesized from a projection exception. |
| Caller publication | `Main.GlobalPresets.cs:1038-1049`: caller validates the SAME witness at acquisition entry and again immediately before receipt creation, after dynamic observation. Loss cannot silently turn the prepared model into a published receipt. Resolved execution ControlsEnergy/ControlsMagic, not sparse imported observation defaults, govern subsequent permission checks (`1201-1202`). |
| Credit and replay | Lost witness sets a current-entry review blocker, suspends credit and reaches the existing uncertain application path, not a render-success downgrade. The owned negative cases require no receipt, zero ordinary retry writes/route re-entry and rejection of the spent token. Receipt validity plus current ownership still gates MarkApplied (`Main.ProtectedTiming.cs:410-420`); scoped native-setting yield can retain known accounting while readiness/credit remains withheld. |

The common grant validator at `Main.Execution.cs:346-376,412-416` enforces
coordinator, lifecycle, epoch, permission, named-domain/conflict revisions and
exact claim ownership. Therefore projection-only handling does not waive foreign
callback, permission, epoch or unresolved-resource proof. The new 455 assertions
explicitly inject Route revision, resource-map and native-model loss DURING
projection, plus Route loss at caller publication; permission withdrawal is also
checked after publication. They do not separately inject every possible permission/
epoch/claim/support ordering inside projection. Those safeguards were traced in
the actual common code, not claimed as extra native test cases or universal proof.

### Real Retained Input, Not An All-Nonnull Fixture

Compared the source `post-live/stage-progress-be6762c5194b46c089468f46df8ee220a34749e8f70a69c187181786e4917897.json`
under `temp/checkins/20261023-034637-captured-relaunch` with the checked-in fixture.
Both selected-policy support arrays contain exactly `0:40,3:40,4:18,5:15,6:3,7:3`,
with NO name properties. Acquisition is `3c2748bd93c74f578553dcee8f3b09a2`, entry4,
original horizon49822, and the actual definition/scoring/credit/AT/W history and
zero exclusions are retained. Recorded source checkpoint SHA256:
`A2A14EEED815C2B6E9CF68769ED83AC71A2C61E86E740C0F1EC22418A05ED778`.

`RetainedFarmingRebindChecks.cs:85-110` reads the actual captured checkpoint,
24hr plan/library, settings and native save; `125-186` loads that data into isolated
offline native objects. The test explicitly asserts imported digger Names are
STILL null at `215-216`, then exercises the real serializer. It is not seeding
all-nonnull policy metadata to avoid the defect. All twelve retained material keys
are asserted; native-save-omitted path/unique fields are supplied from retained
descriptors. Scene slot entitlements/unrelated bonuses and an eligible finite
40-HP/10-defense floor template are declared offline boundaries, not full live-stat
parity or independent recovery of fields the native save did not serialize.

### Exact Regression And Retained Results

`RetainedFarmingRebindChecks.cs:189-306` runs SIX timelines: positive retained
import, projection-only exception, projection+Route/resource/model loss, and caller-
publication Route loss. Actual funding repair/native floor196 entry precedes the
fault; negative cases preserve original intent, withhold receipts and ordinary
replay, and can recover ONLY after another real tokenized review plus explicit
native Safe Zone handover. Positive and render-failed receipts prevent repeated
accounting/route entry. Identity, horizon, definition, frozen scoring, zero exclusions,
AT/W history, entry-fight identity, scheduler credit/budget and no-search assertions
remain; no native success/ownership Boolean replaces the actual proof.

| Retained Evidence Read | Actual Scope And Result |
| --- | --- |
| `core-retained-rebind-isolated-final-b275264dce3e4c169f5f6df26c3257c5.log:11,53-54` | MVID `cd49e5cf-c202-4057-8c8c-4e6981b93219`; PASS 455 retained native assertions. The printed exception stacks are deliberate lost-witness negative cases, not suite failures. |
| `core-retained-rebind-full-final-3036bbb4759f492da20de98c8cb74d6a.log:11,194-205` | MVID `f9427d5a-9bca-450e-968f-47b6860a4521`; FULL Preset PASS 17,285 = prior16,830 +455. Existing manual-resume383 and finite253 remain executed. This is NOT a same-DLL full thirteen-suite release gate. |
| Owner's retained red evidence | Actual null-name serializer failure on `0d086486-b8fd-46ff-8126-8928d61cd8e3`; label-only repair still failed projection-fault publication on `05cc2be3-9fa8-4f2f-b5f1-71908bcf3823`. The guard separation was not inferred from the label fix alone. |

All owner runs used the named-mutex verifier; this reviewer only read their code,
inputs and retained logs. No diagnostic injection, offline fixture execution or
assembly instrumentation was performed here.

### Current Readiness And Prohibition

The successful old capture's offline evidence below remains historical FACT, but
its deployment failed: native setup changed before an unpublished receipt caused
UnknownOutcome, stage credit froze, zero idle alone was not acceptance, Walderp
capacity still rejected, and no new-build kill/fruit7 activation was proven. The
deployed old UnknownOutcome is NOT retrospectively verified by the fix or tests.

The prior diagnostic injection crashed Mono; final six seconds are not certified
lossless. Neither diagnostic binary nor another injection/attach experiment may
be used. Keep the actual game running; no further restart without fresh regression/
full candidate proof and the authorized live owner's save-backed handoff.

NRE fix review is accepted ONLY in the scope above. Fresh complete candidate
source/UI/native gates, frozen artifact and live acceptance are still required.
The separate opt-in manual-titan capacity transition is pending its own owner/
architecture review and is NOT reviewed or implemented here. Preserving-capacity
guards are not removed by this fix. No stage reset, forced Equip/rebirth, reduced
target or fabricated receipt/continuity is authorized to bypass either issue.

## Historical Captured Offline Acceptance

Authoritative OLD offline artifact, not a mixture of earlier MVIDs. It was later
deployed and failed live acceptance; it is not the NRE-fix build or a live-approved
artifact. Preserve its manifest and `liveVerified:false` unchanged:

```text
A:\misc\NGU-Automation\temp\checkins\verified-candidate-3b6016de5360463ea3ce4ac991dbb589
MVID:   3c01e825-035c-4ab7-98b1-3c2d50d1f2c9
SHA256: 5FEFCA5E10572707FC19A3E99AA9F1372A02640551F7E131CFD3774277AB881A
Captured: 2026-10-23T03:09:07.6166296-05:00
liveVerified: false
```

Independently recomputed the frozen `injector/NGUInjector.dll` SHA256: exact match
to manifest and frozen AutoExp log. Directly read all twelve full-suite completion
lines in the capture's `native-verification.log`; its line 1 names all twelve and
has an EMPTY selector list. The relevant assembly-consuming runners print the
SAME MVID, not identities borrowed from older runs. ResourceAggregation is the
source-linked numeric fixture in that gate, not a native gameplay simulation.

| Full Captured Gate | Actual Passing Result | Native Log Lines |
| --- | --- | --- |
| PresetIntegration | 16,830 assertions | 153-163: manual-resume 383, preparation 40, pill precision 36, finite group 253, then full PASS |
| CombatPrepIntegration | 12,011 assertions | 168,193-194: same MVID, zero failed shared groups and full PASS |
| ManualAdventureOverlay | 297 assertions, zero failed groups | 200-213 |
| BlackBar | 24,603 assertions | 218-246; exact native pill bits and retained UNKNOWN negative precede full PASS |
| Ownership | 10,321 assertions / 486 scenarios | 251-253 |
| TitanGold | 71 cases / 1,497 assertions, failures=0 | 258-330 |
| PresetEquipment | 94 assertions | 334-335 |
| GearEquip | 20,805 assertions | 340-358 |
| OrdinaryPolicy | 125 plus terminal-hold 42 assertions | 364-375 |
| WinFormsBlood | 174 assertions | 379-386 |
| StageTargetsNative | 48 assertions | 391-393 |
| ResourceAggregation | 4,646 assertions | 397-398 |

The expected failure/UNKNOWN diagnostics inside negative scenarios are not suite
failures. No failed full suite was replaced with a focused pass for this capture.

### Capture Integrity

Inspected `checks/VerifyExecutorOffline.ps1:47-118`: capture requires a retained
log, NO selector, the complete distinct allowed suite set, an existing parent and
a NEW destination. The exact named mutex covers production rebuild, all fixture
builds/runs, aggregate failure rejection, binary checks, distribution copy and
manifest creation; release is in finally AFTER capture. The tested hash is taken
after production build, checked again after all suites, and compared with the
distribution DLL BEFORE copying. Any failed suite or mismatching tested/dist hash
throws before capture. Independent frozen-copy hashing above also verifies the
actual copied DLL, not just the source path or a later mutable bin artifact.

The verifier now additionally includes AutoExp as a thirteenth default entry.
That later gate expansion is not falsely represented as part of the captured
twelve-name log. Its additional native coverage DID run against this EXACT frozen
DLL, without production/dependency rebuild, under the same verification mutex:
`autoexp-migration-frozen-3c01e825.log:1-5,111-112` records matching MVID, 105/105
cases / 4,884 assertions / zero failures, and identical SHA256 before AND after.
All original native cases remain, with new refusal/replay/storage controls.

### Source, UI And Browser

The supervisor's completed fresh `verify-ui.ps1` PASS is recorded at
`agent-supervision-20261022.md:231-236`: all source gates, UI 2,082 tests, Rust 179,
full format/typecheck/build, including AutoExp planner 26 cases / 59,410 assertions.
Reviewed the wrapper's per-command nonzero-exit guards; no wrapper was rerun here.
The native and browser logs above/below were independently read, not merely
inferred from that high-level source-run report.

Directly read `ngu-browser-final-082b9fa79f974cefb96919c077190d70.json:29465-29473`:
errors=[], expected=852, skipped=4, unexpected=0, flaky=0. Project retries are 0
(`26-57`); all 856 discovered cases are accounted for. The four existing desktop
skips are not newly hidden failures. This is actual full browser completion,
not only the earlier twelve focused execution-review cases.

## Prior Reachability Audit (Historical)

These are repository production callers and supported ingresses, not a claim about
arbitrary third-party reflection into internal methods. Source line numbers refer
to that earlier inspected candidate. This new NRE review did not repeat a whole-
codebase writer audit. Prior offline closure does not erase the later failed live
acceptance or certify a newly modified candidate.

| Item | Classification | Actual Call Path And Evidence |
| --- | --- | --- |
| 1 | **Previous P1 CLOSED: real common grant fence** | `Main.GlobalPresets.cs:108-113,1595-1626,1659-1685` retains the ORIGINAL ExecutionGrant from before support retirement. Immediately before AND after native route re-entry, VerifyExecutionPrerequisites uses that grant and complete declared footprint. It binds common lifecycle/coordinator/domain/conflict/claim authority plus original intent/material/context, rather than reissuing authority from matching physical fields. The real twelve-timeline/383-assertion group executes in full captured PresetIntegration. |
| 2 | **Dead historical gear/digger families, not active overlap** | Production caller search finds no current entry to LoadoutManager snapshot/restore/lease/Try* swaps or DiggerManager save/restore/equip/recap families. Main startup only resets lock metadata (`Main.cs:124-125`). `ChangeGear`/`EquipDiggers` external callers are exclusively `CustomAllocation.Equipment.cs:25,47`, reached through the disabled allocation adapter. Keep active CaptureExactLoadout/ComposeExactLoadout/ApplyExactLoadoutObserved/SnapshotEpoch helpers separate from dead restore machinery. |
| 3 | **Legacy allocation and snipe ingress are provably blocked** | `Main.AutomationLoop.cs:10` is the literal getter `LegacyAutomationEnabled => false`, not a saved setting. ReloadAllocation may call DoAllocations (`CustomAllocation.Loading.cs:94`), but `Extensions.cs:216-219` returns before allocation/gear/diggers/OS. Resource allocation methods additionally throw before writes (`CustomAllocation.ResourceAllocation.cs:12-13,73-74,126-127`); breakpoint Allocate methods have the same constant guard. Main.Start does not schedule SnipeZone (`Main.cs:358-359`), and SnipeZone/MoveToITOPOD/SetResnipe also return at `Main.CombatRouting.cs:700-701,791-792,813-814`. GlobalEnabled cannot turn that getter on. |
| 4 | **Dead secondary resource engines; deletion work remains** | `BlackBarAllocator.cs:84-96,141-151` retains three private legacy ResourceApplications. AllocateCombatPrepFallbacks has no production caller; AllocatePriorities is called only by NGUBP.Allocate, which rejects disabled legacy allocation at `NGUBP.cs:52-56`. Current focus/stage/combat use CalculatePriorities/CalculateCombatPrepFallbacks, then Main's shared ResourceSetupExecutor. Instantiating unused objects is not evidence their Apply bodies run. |
| 5 | **Reachable identified finite/configuration leaves, not old workflows** | AutomationRoutine -> Ygg ManageYggHarvest -> HarvestFruitAction/ActivateFruitAction; BaseRebirth.PreRebirth -> the same harvest/purchase/threshold-spell actions; auto boost -> InventoryManager MergeNativeItem/BoostNativeItem -> RunInventoryLeaf; external/probe/WinForms allocate/equip -> RequestAugmentAllocation/ApplyManualGearSelection. Native fruit/spell/purchase/pit/item effects are in FiniteAction Invoke bodies admitted by RunFiniteAction, not in old manager loops. `InventoryManager.cs:404-440,454-523,605-703,731-741` forwards to those leaves or explicit grant-checked desired settings. Dry-run clone mutations are not live inventory writes. |
| 6 | **Application evidence and narrow returns are not historical restoration authority** | `CustomStageAllocator.cs:208-221,347-425` clears stale receipts and issues application evidence only from a verified shared outcome. Credit also requires actual current setup/map/epoch/permissions through `Main.ProtectedTiming.cs:410-420`; receipt non-null alone is insufficient. Selected gear/support native calls have the shared grant at `Main.CustomFocus.cs:297-305,383-488` and per-write support fences. Last-owned AT/nuke/input returns remain narrow granted setting returns. F8's explicitly acquired fallback is allowed by WIP: EndExecutionClaim chooses CURRENT normal/focus/manual intent before ExplicitReturnBaseline (`Main.Execution.cs:527-555`), not an automatic old-snapshot ladder. |
| 7 | **Bounded dead-code cleanup required by final WIP, not additional active bugs** | Remaining dead mutator families include LoadoutManager GearRestoreLease/saved-temp/Try* changes, DiggerManager RestoreSnapshot/affordable restoration/legacy equip+recap/cheapest cache, GearOptimizer OptimizeAndEquip/OptimizeAndEquipForAutokill old orchestration, ZoneHelpers.OptimizeITOPOD and unused ResourceWriter.ActivateFruit/full-recall helpers. Some contain raw native writes, but current production roots do not reach them. Remove converted dead families and reconcile the writer inventory as the explicitly requested final deletion task; do not delete shared pure selectors/typed actuators or demand unrelated cleanup. |
| 8 | **Offline evidence gate satisfied; WIP/live completion separate** | The captured SAME-MVID full suite set above supersedes older split-artifact/inconclusive results. Hard support, source/UI/Rust, full browser and frozen native AutoExp proof are complete for their stated offline scopes. ActionPillPrecisionChecks is now compiled/hooked/executed: 36 assertions inside captured finite 253. Dead-family deletion and actual candidate restart/+2h/+4h/+6h/daily rebirth are NOT certified by offline green. |

### Independent P1 Closure

Read the actual fix and
`checks/PresetIntegration/ExecutionCoreCutoverChecks.cs:27-143`, not only its pass
count. Positive control requires real `-1,0` retirement/
resume, current receipt and consumed cadence. The native AT renderer then injects
Route-only revision/conflict/foreign claim, permission/controller/model/epoch/
intent loss and post-native faults. Route-only controls explicitly preserve
physical allocation/support AND leave the real resource outcome Verified with
AffectedLanes=0. Negative cases require zero pre-resume re-entry, unchanged cadence,
retained committed support/funding/gear/entry/history, preserved foreign claim/route,
and no authority renewal or resource replay on pending retries. Post-native faults
cannot claim completed resume after their one already-performed native entry.

The code keeps the original grant across those callbacks/retries and checks it
before and after changeZone. It does not introduce another physical-check success
Boolean, historical restore, fake grant, or bypass of existing permissions. All
383 assertions completed on the captured DLL (`native-verification.log:153`)
before full Preset PASS. The earlier native red was independently documented on
`ee40ef2c-617d-4f02-b16b-a41592ed9a6a`; it is historical, not a current blocker.
No specific surviving counterexample was found within this closure scope.

No supported current LoadoutManager/DiggerManager historical-restore overlap was
found in the prior caller audit. Their dead families remain bounded WIP cleanup,
not new active bypasses and not permission for reactivation.

## Earlier Scoped Closures

Paths and line numbers are relative to the repository root at inspection.
P1/P2 indicates the original finding's severity, not an assertion it remains open.
This earlier provenance is historical. Current offline acceptance is established
by the single captured artifact above; no split-MVID combination is needed.

| Original Finding | Current Assessment | Implementation And Regression Evidence |
| --- | --- | --- |
| P1: preset STOP clears uncontrolled lanes | CLOSED for the original finding | `NGUInjector/Main.GlobalPresets.cs:1103-1145` requires acquired ControlsEnergy/ControlsMagic or exact still-owned temporary Magic context. `checks/PresetIntegration/ExecutionCoreCutoverChecks.cs:602-773` retains uncontrolled/foreign/empty/duplicate stop and reentrant successor assertions. Original owner-run core 937 PASS, MVID `5e3d151f-385c-4e13-acee-094eee028310`, included 159 new assertions; later core/capacity 1,424 PASS is separately identified below. |
| P1: dirty committed results do not block successors/rebirth | CLOSED for the original finding | `NGUInjector/FiniteActionLedger.cs:67-95` retains dirty commits and bounded dependency-scoped storage flush; `Main.FiniteActions.cs:58-83` admits no dependent effect until storage resolves. `Main.RebirthActions.cs:18-27` applies the barrier to rebirth. Native `FiniteActionNativeChecks.cs:196-235,403-425` holds an actual result file after native commitment, withholds successor spell and actual rebirth entry, funds healthy Energy, and proves storage-only recovery without predecessor replay/fresh historical authority. Action selector PASS 177, MVID `63b42029-d7c8-4f43-aeeb-a8c65a51330f`; source checks PASS 86. |
| P1: pending ITOPOD process leaks before Routing | CLOSED for the original finding | `NGUInjector/Main.MaxItopodPush.cs:199-207` reuses the admitted pending process; `235-243,310-328` retires it even before Routing and withholds cancellation completion while retirement remains pending. Native `RouteExecutorCutoverChecks.cs:604-666` proves gear/support callback failure, real pending native nuke, retained prerequisite claim, zero renewed death budget and completion only after native retirement. Owner-run route PASS 719, MVID `fd46bb01-3f32-4ce8-adc1-95fb78f45f3f`. |
| P1: fight-preparation record wedges after pre-setup acquisition failure | CLOSED | Same prepared acquisition and exact selection survive selection/file-lock failure without renewed time or premature mutation. The captured finite 253 now includes the original retry coverage, preparation identity 40 AND integrated pill precision 36; neither partial remains unhooked/unrun. |
| P1: no production domain-reauthorization recovery path | CLOSED for the original finding | `NGUInjector/Main.ExecutionPersistence.cs:118-138` calls real ReauthorizeExecutionDomains only after token/entry/permission/conflict/native-map validation and rejects another native-settings owner's evidence. `checks/PresetIntegration/ExecutionPersistenceChecks.cs:202-289,377-387` tests bad token, permission/domain/model/native edits, stale grants, actual repair writes and same-entry convergence, healthy unreviewed Magic and retained UNKNOWN finite history. Recovery selector PASS 347, MVID `2ad8d8a8-d40b-4865-ade7-c4538d97ba27`. |
| P2: single-lane review unconditionally requests both lanes | CLOSED for the original finding | `NGUInjector/Main.ExecutionPersistence.cs:69-81` derives scope from acquired ControlsEnergy/ControlsMagic and actual setup/route requirements, without relaxing grant permissions. `ExecutionPersistenceChecks.cs:202-307` tests Energy-only/Magic-only with the other lane unmanaged and capacity-only with neither resource grant. Included in D's 347 native PASS. |
| P2: route review hides the commitments being approved | CLOSED | `NGUInjector.UI/src/lib/ExecutionReview.svelte:24-31,93-127` renders actual commitments: process zone/slot/item/support, manual deaths/limit/owned time/outcome, tower history and boss window. `e2e/execution-review.spec.ts:101-135` verifies real-shaped route responses, exact visible values, read-only Preview, token-only approval and mobile fit. Latest owner-run review browser suite PASS 12 desktop/mobile cases. |
| P2: rebind/restart apply lacks unknown-outcome guidance | CLOSED | `NGUInjector.UI/src/lib/controller.ts:843-887` distinguishes an IPC attempt from pre-submission failure and matching explicit rejection. `tests/controller.test.ts:1431-1529` covers enqueue/read/mismatch/timeout, known rejection, read-only preview failures/timeouts and zero replay. Browser `execution-review.spec.ts:138-159` proves spent token stays spent through Refresh and another approval needs explicit fresh review. All 2,082 Vitest tests and 12 focused browser cases PASS. |
| Missing finite-native loader hooks | CLOSED for the original finding | Both real PresetIntegration loaders install their hooks. The subsequent action selector 177 PASS on `63b42029-d7c8-4f43-aeeb-a8c65a51330f` executes real NUMBER committed/render-failed/no-repeat and real pit/CURRENT-B assertions; the old missing-hook and synthetic-B failures are historical. |
| CombatPrep positive support bypasses grant boundary | CLOSED for the original fixture finding | `PhaseSupportIntegration.cs:220-231,289` retains raw no-authority rejection plus a real scoped granted positive control; the shared helper resolves/validates production authority, not a success Boolean. Retained FULL CombatPrep PASS 12,011 on `9c8b9350-23de-46a8-b202-04a92ef0a829` supersedes the old unsupported direct-call failure, without establishing the final twelve-suite release. |
| Titan/Ygg fixtures require removed snapshots/historical restore | CLOSED for the migration finding | Full TitanGold PASS 71 cases/1,497 assertions on `87edec2b-ebe7-43e5-a2be-0223022492e6`. Full Ownership PASS 10,321 assertions/486 scenarios on `f7e7b788-b882-423a-a312-30a23fd32660`, including Ygg rewards/no replay/current-successor failure cases (`YggdrasilChecks.cs:87-179,211-235`). No historical restoration target was reintroduced. |
| Fully-unmanaged capacity-only ordinary admission/status | CLOSED | `NGUInjector/Main.LoadoutPresets.cs:629-646,671-674` checks the selected candidate's own controlled lanes; `Main.GlobalPresets.cs:1190-1205` uses acquired controls. `ExecutionCoreCutoverChecks.cs:458-599` asserts actual Applied/verified gear with BOTH management switches off, zero resource grant/writes/recalls, retained acquisition across library edits, plus capacity-loss/context/foreign and positive/controlled-empty permission-loss rejection. Core/budget PASS 1,424 on `bf73fa3b-c29d-4628-a5cd-ba50fb9c25a6`. |
| Explicit-overlay fresh entry and per-entry fight opportunity | CLOSED, including captured full suites | Original assertions require the actual restarted stage to verify under the explicit overlay, preserve native AT/gear/credit, and receive exactly one new entry fight. Captured full Preset 16,830 / Combat 12,011 / Overlay 297 supersede the earlier focused-only closure and old full-gate failures. |

The old Combat 31-diagnostic and Support 5-diagnostic compilation reports are
**historical, not current blockers**. Combat now deliberately tests production
decision slices; Support tests allocation/projection/policy. Owner records show
Combat PASS 4,536 plus three behavioral mutants and Support PASS 31,048. Those
narrowed source boundaries are explicit and do not prove native route/executor
acceptance. The replacement route/Titan/current-successor timelines above matter.

ActivityPolicy integration's old compile failure and hard-support failure are
superseded. It references the real SustainedFocus source executor and passes 264
assertions: 160 migrated metadata/executor assertions plus all 104 hard-prerequisite
assertions. Full SustainedFocus passes 2,328; explicit core passes 159. The original
19-case support matrix remains enabled and passes in all three runners. This is
source-linked proof, not a new compiled-game MVID or final release acceptance.

## Adversarial Review

The old preparation fruit lookup mismatch is CLOSED IN SOURCE, not still open:
C defines the real `PreparationFruitOpportunity` at
`NGUInjector/Main.FiniteActions.cs:153`; both production invocation
(`Main.CustomPrep.cs:298-300`) and D lookup (`Main.ExecutionStore.cs:248-267`)
use it with the retained original fruit count. D rejects dirty/INVOCATION/UNKNOWN
history and binds its latest durable observations; no new opportunity is inferred
from a later ripe cycle. `ExecutionPersistenceChecks.cs:431-466` proves an UNKNOWN
original fruit still blocks approval/review after newer native counts, with zero
native writes/searches/entry fight. This is injected uncertainty, not proof of
actual partial native fruit continuation.

The positive and negative cases were inspected, not just pass totals:

- C's locked-result tests retain known native commitment, block dependent spell and
  actual rebirth, and still allocate healthy Energy. Storage recovery never repeats
  the predecessor or treats cached completion as fresh native authority.
- D's recovery tests require actual repair writes and funded native convergence,
  not an Applied label alone. Invalid token/permission/domain/model/native-map
  evidence clears nothing. Unreviewed Magic and UNKNOWN finite effects remain
  unresolved. Metadata-only completed-preparation review gets no mutation grant.
- Capacity-only Preview/Apply, Equip, scheduled and ordinary service preserve both
  unmanaged lanes, Basic Training and R3. Negative capacity/context/foreign cases
  remain; positive and controlled-empty resource intent still requires its own
  management permission. D's later diagnostic says Applied; the core regression
  additionally ASSERTS Applied and the absence of resource authority/writes.
- Ownership's current Pit path instruments the REAL RequestMoneyPit and native
  engage for observations, rather than replacing them with success. Real saved-plan
  OverrideCustomStage acquires B while the pit claim covers it. Tests assert B's
  durable scheduler entry before invocation, exact native reward/currency totals,
  current/durable outcome, original timing/credit and no A-restore flash or replay
  (`checks/Ownership/MoneyPitRecoveryChecks.cs:59-149`). Ygg keeps reward/no-repeat,
  unknown/foreign/permission and CURRENT-successor assertions in its full runner.
- UI assertions cover exact actual route-response fields and spent-token behavior,
  including refresh after failure. Known rejection and read-only Preview timeout
  do not become uncertain mutations. No automatic approval or fresh review occurs.

The earlier C/D/capacity/UI closures remain valid within their proof. The additional
route-admission gap found by the writer audit is now CLOSED through the original
common grant and captured twelve-timeline/383-assertion proof, not a physical-success flag.
Source action grant substitutes remain explicit unit-test boundaries, not native
proof. No historical-return compatibility or weakened native assertion was accepted.
Offline fixtures and narrower source decision/math suites are not live-game proof.

## Remaining Gates

The source hard-Fu defect is CLOSED in its tested scope: normalized hard IDs are
part of the retained key, frozen/cache success checks actual availability/flags,
post-application proof is required, and stronger pending promotion preserves its
original minute. All 19 timelines/104 assertions and full/core/ActivityPolicy
results above are green. No failure or matrix was removed for that closure.

Preparation identity and pill precision are BOTH compiled/hooked/run in captured
finite 253, including 40 preparation and 36 precision assertions. Independently
read `ActionPillPrecisionChecks.cs:6-44`: exact native stored-bit positive and
render-failed controls, BOTH adjacent ULP directions for all four fields, and
matching stats with nonzero blood remaining UNKNOWN. The shared manifests include
the partial and FiniteActionNativeChecks invokes it. Native cast behavior is
separately proved by the blood suite/full BlackBar; the precision group's stated
classification-only boundary was not misrepresented as another native cast.
The production classifier materializes the four complete binary32 expressions
and still requires exact stats plus zero blood (`Main.BloodActions.cs:33-44`);
no epsilon or blood-only fallback was accepted. Manual-prep takeover is likewise
fixed through preselection, durable retirement authorization/tombstone, exact
native retirement and SAME selected successor (`Main.CustomPrep.cs:98-230`),
with scheduling selector 305 proof. No old gear-restore ladder was wrapped.

The final native/source/browser gate and exact frozen artifact are now supplied
and accepted for OFFLINE readiness as recorded above. The original item-1 negative
boundary is included, not pending. Final dead-family deletion/inventory completion
remains a WIP task, distinct from an active bypass or failure of this offline gate.

The optional GearActuatorSource boundary's missing old cleanup dependencies are
separate from full GearEquip and should be reconciled with the bounded dead-family
deletion. No optional build was run or called passing by this verifier.

The captured manifest names twelve full fixture suites; the verifier subsequently
added native AutoExp to its default set. Its same-frozen-hash 105-case pass is
recorded separately above, not falsely inserted into the captured native log.
All production/native builds and runs retain the exact named mutex across build
AND execution. This verifier ran none, and performed no source or live check.

## Historical Scoped Artifact Ledger

These older retained executions explain the investigation history only. They are
NOT used to assemble the current acceptance: the authoritative same-MVID capture
and additional same-SHA AutoExp execution are above. Assertion groups overlap and
must not be summed as unique coverage.

| Artifact / Gate | Accepted Scope And Limit |
| --- | --- |
| `63b42029-d7c8-4f43-aeeb-a8c65a51330f` | Earlier C action selector PASS 177: commitment/render failure, locked result successor/rebirth barrier, healthy Energy, storage-only recovery, same-preparation selection/IO retry. Superseded in breadth by the 217-assertion selector below. |
| `cdf5046f-9175-4d8e-bd0b-29d22bd3fc1f` | Historical action PASS 217 = 177 + 40 preparation assertions; C also recorded 217 on `db0cb54e-0fc3-482c-b7a3-f1f46cf7e7ae`. Precision was not included THEN; it is integrated and passed NOW in the captured finite 253. |
| `7a919d9b-7089-4d85-9a33-3164d40718dd` | Native blood selector PASS 510 after exact binary32 Iron Pill classifier fix, with real native casts and UNKNOWN negative retained. C source action/preparation/handoff checks PASS 103 are separate source-boundary proof. |
| `ce795a53-7d98-4a0f-89b9-f26b245c7acb` | Native scheduling selector PASS 305, including the original explicit manual-prep takeover/retirement regressions. |
| `2ad8d8a8-d40b-4865-ade7-c4538d97ba27` | D recovery PASS 347: actual scoped native repair, single-lane permission separation, negative freshness cases, retained process/entry/history and uncertainty. Not full native acceptance. |
| `bf73fa3b-c29d-4628-a5cd-ba50fb9c25a6` | Core/budget PASS 1,424 = 796 core + 628 budget; nine unmanaged capacity admission/permission/failure timelines add 167 assertions. Earlier STOP-specific 937 PASS used `5e3d151f-385c-4e13-acee-094eee028310`. |
| `ce376464-7f84-4dd3-99ac-bf07d62c74f2` | Separate rerun of D's unchanged recovery selector PASS 347 after core capacity fix; ordinary fully-unmanaged capacity diagnostic is now Applied. |
| `e8e7e556-1157-48f0-9862-482ca7e6673a` | Focused explicit overlay PASS 276; old fresh-entry failure is not a current open finding. |
| `67f33bb6-ba2d-45d6-9152-6d452745709a` | Focused inline-stage setup PASS 462. Automatic-entry PASS 177 is also recorded by supervisor at 22:35; no artifact MVID supplied for that result, so none is inferred. |
| `f7e7b788-b882-423a-a312-30a23fd32660` | Full Ownership PASS 10,321 assertions/486 scenarios, including Ygg and real Pit request/durable CURRENT B. Not the earlier failed synthetic-B fixture. |
| `fd46bb01-3f32-4ce8-adc1-95fb78f45f3f` | Route selector PASS 719, including pending tower/native-retirement regressions. |
| `87edec2b-ebe7-43e5-a2be-0223022492e6` | Full TitanGold PASS 71 cases/1,497 assertions. |
| D UI/transport follow-up, no native artifact MVID | PASS 2,082 Vitest tests/30 files; 12 desktop/mobile review cases; typecheck/build; FULL Rust formatting and 179 Rust tests. Browser evidence: port 14942, `%LOCALAPPDATA%/Temp/opencode/execution-review-ui-handoff-20261022`. The old formatting failure and two UI defects are superseded, not current blockers. |
| `9c8b9350-23de-46a8-b202-04a92ef0a829` | Retained FULL PresetIntegration PASS 16,411, FULL CombatPrepIntegration PASS 12,011 and FULL Overlay PASS 297 / zero failed groups in `executor-native-acceptance-final.log`, lines 161/192/211. Its later BlackBar failure is not silently promoted to green. |
| `031156cb-2b38-47d4-84e4-3115b9e65aa1` | Separate retained FULL BlackBar PASS 24,603 in `executor-native-blackbar-current-full.log`, line 38; includes StageSupport 409, scheduling 305, protected timing 2,853 and Iron Pill 459. Not the same MVID as the three full-suite passes above. |
| Source support follow-up, no native MVID | Hard prerequisites 19/19 timelines / 104 assertions; full SustainedFocus 2,328; explicit core 159; ActivityPolicy integration 264. `checks/Support/README.md:63-136` records red/green and unchanged authority/foreign/cadence guards. |

The per-suite table is supported by
`executor-native-fixture-cutover-20261022.md:11-41`.
The earlier reported four-suite all-green MVID
`e928aa81-8ece-4dfe-b950-724613cabc50` lost its log to an overwritten pathname;
the replacement log identifies `797b7640-54a6-41a8-965a-9caf9dfe2dbf` and lacks full
completion/exit evidence. That OLD combined-log result remains INCONCLUSIVE, but
is superseded for current readiness by the retained captured twelve-suite run.
No old overwritten log or stale MVID is used for current acceptance. Earlier failure MVID
`28eb4976-3d94-4949-999f-958d2c124b6c` is historical, not evidence its now-fixed
bugs persist.

## Latest Readiness

**Scoped NRE repair review accepted; NEW full candidate and live acceptance NOT
established.** The earlier frozen 3c01e825 offline gate was real, but the actual
deployment FAILED and remains `liveVerified:false`. Known native effects followed
by the old unproved publication cannot be retrospectively certified by this patch.

Read retained evidence directly: isolated 455 PASS on
`cd49e5cf-c202-4057-8c8c-4e6981b93219` and FULL Preset 17,285 PASS on
`f9427d5a-9bca-450e-968f-47b6860a4521`. These are separate artifacts and not the
old captured distribution or a fresh complete thirteen-suite/source/UI freeze.
No concrete new failed proof was found in the requested model/projection/receipt
boundary. The declared native fixture limits and exact six negative/positive
timelines remain as described in the current review above.

The separate Walderp/manual-titan capacity transition remains its assigned
owner's pending work; this review did not evaluate its design or implementation.
Do not weaken preserving guards, reset stages or force effects to conceal it.
Bounded dead-family deletion, actual candidate credit/refill/fruit/Walderp results,
daily rebirth and +2h/+4h/+6h soak still are not complete.

The failed diagnostic injection/crash is not a graceful restart and final six
seconds are not certified lossless. NO repeat diagnostic injection or invasive
attach. No further restart is authorized by this report: first obtain fresh full
candidate proof and the authorized live owner's save-backed handoff. This review
performed only reads and this owned report update, with no native/test execution,
game/bridge/UI mutation, instrumentation/injection, index action or commit.
