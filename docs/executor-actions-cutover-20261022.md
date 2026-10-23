# Action Cutover Handoff

## Current Result

C's precision partial is now integrated and EXECUTED in the existing native
action group. There was no new production failure and no production source edit
in this integration pass. The earlier 00:05 pill/takeover fixes remain intact.
This is NOT full combined release or live acceptance.

The latest run uses the shared fixture owner's corrected real saved-plan
OverrideCustomStage acquisition and durable CURRENT-B checkpoint/entry assertions.
Its intermediate 9-assertion transition failure was fixed in that owner's files,
not by C editing or bypassing the currently owned shared action partial.

| Verification | Actual Result |
| --- | --- |
| Latest native blood selector | PASS 510; MVID `7a919d9b-7089-4d85-9a33-3164d40718dd` |
| Latest native scheduling regressions | PASS 305; MVID `ce795a53-7d98-4a0f-89b9-f26b245c7acb` |
| Latest native action selector, including preparation and precision hooks | PASS 253 = existing 217 + new group 36; MVID `423072f6-3265-461d-8d20-7cf9061f0d6d` |
| Source runner/store/preparation/handoff checks | PASS 103 assertions |
| Owned tracked-file `git diff --check` | PASS |
| Pill scalar classifier group | PASS 36, including 34 numerical assertions and two fixture setup assertions |
| Full BlackBar | Fixture owner subsequently PASS 24,603; prior C failure is historical, not a current blocker |
| Full combined release suites, deployment, restart, soak | Not performed by C |

Commands actually run:

```powershell
& "checks\VerifyExecutorOffline.ps1" -Suite PresetIntegration -Selector --finite-action-cutover -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" -LogPath "C:\Users\Kevin\AppData\Local\Temp\opencode\executor-native-pill-precision-e1587b4cefe446df89e2632a76f3aa45.log"
& "checks\VerifyExecutorOffline.ps1" -Suite BlackBar -Selector blood -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
& "checks\VerifyExecutorOffline.ps1" -Suite BlackBar -Selector scheduling-regressions -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
& "checks\VerifyExecutorOffline.ps1" -Suite BlackBar -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
dotnet run --project "checks/FiniteActions/FiniteActionsChecks.csproj" -c Release
```

The unique GUID log retains production MVID at line 11, new precision group PASS
at line 13 and total native PASS 253 at line 15. The wrapper returned success and
printed terminal completion in the execution result. This pathname was not reused.

The verification wrapper holds `Local\NGUInjector-Executor-Offline-Verification`
across production build, fixture build and run. The production build emitted no
warnings in the latest run. The shared native fixture emits its existing
unassigned `Program.NativeGearSwapped` warning. The source-only fixture emits
three unassigned-field warnings for native-only action options which its explicit
authority boundary does not exercise. Source checks are NOT native authority proof.

The final supervision/fixture handoffs were read. After the shared fixture owner
returned, the user authorized exactly this bounded integration: one Compile entry
in each of PresetIntegration, CombatPrepIntegration and ManualAdventureOverlay,
and one `CheckActionPillPrecisionRegressions()` call in the existing shared action
group. All three linked consumers include the definition; StageTargets inherits
the Preset project. Full Preset and --finite-action-cutover already call the same
group, so no duplicate hook or hidden selector was added. The numerical partial
now rejects BOTH adjacent ULP directions for every field. C did not edit shared
Program.cs, verifier, fixture loaders, production, core/persistence/route code or
settings transport in this pass. No index changes,
staging, commit, live operation, deployment, restart, parent WIP edit, nested agent
or supervising-session task notification occurred.

## Closed Defects

- **Iron Pill binary32 proof:** red reproduced on MVID
  `8ffee38c-8007-4e1d-a795-c4ad238d1a3e`: native exact fields had bits
  `40400000,40400000,41100000,3db851eb` but the classifier returned UNKNOWN in all
  three successful-cast timelines. Installed native IL uses add/stfld float32 for
  attack/defense and mul/add/stfld float32 for HP/regen, with NO intermediate
  product store. The classifier now materializes all four COMPLETE expressions
  into a float array before comparing, matching those storage boundaries and order.
  It does not rely on float locals/casts which may retain desktop CLR extended
  precision. All four exact stat checks AND zero blood remain required; no epsilon
  or blood-only fallback was added. The partial/rejected native fixture still
  returns UNKNOWN. Native blood is now green, including reservation ending, plan
  elapsed target and the actual game cast.
- **Explicit manual fight takeover:** red reproduced on MVID
  `75b73499-0b70-430e-8171-01efa60a2aa7`: the running and manual predecessor cases
  both returned `previous identified preparation must retire first`. The new path
  acquires and preflights exact gear/support BEFORE cancelling old intent, retaining
  the original behavior for rejected locks, missing plans and unusable selections.
  A semantic `preparation-handoff` record stores the explicit successor identity,
  original request start/entry and selected material/support, then durably records
  retirement authorization before using the existing exact StopBossPush/cancel
  paths. Only observed native retirement plus a stored result/tombstone permits
  the successor. It never wraps the old complete workflow in RunFiniteAction.
- Running/manual old rebirth requests become durable `superseded:<successor>`
  history, not reusable completed-rebirth instructions. Imported invocation/unknown
  handoff history blocks resuming the old request. Dirty result storage cannot lend
  replacement authority. The same selected successor and original command/start/
  entry survive asynchronous retirement/storage retries. Native setup is withheld
  until retirement storage succeeds; its declared footprint is reserved meanwhile.
  No old gear restoration or covered-baseline detour occurs on this handoff.
- Newer unrelated boss/titan/combat owners are not cancelled through stale request
  references. Explicit cancellation persists a semantic withdrawal without erasing
  unresolved retirement history, and a failed cancellation write cannot later
  resume the stale desired request. Original blood-saving/rebirth state is untouched
  by a rejected selection and retired only after actual handoff admission. Pending
  and retained preparation gear is also protected from destructive inventory use.
- Capacity preflight is skipped only for an ALREADY EXACT native gear map, where
  no gear/capacity transition exists; changed maps still use the real exact capacity
  check. This avoided an unnecessary projection of unrelated absent fixture data,
  not an authority or capacity-proof stub. Both real scheduling takeover cases now
  pass, including disabling auto-rebirth after takeover without stopping the new
  manual boss process, zero rebirths, preserved scheduler scope and rejected-input
  retention of the prior running process/intent.

- **Dirty committed results:** `FiniteActionLedger.HasUnresolved` includes dirty
  commitments. Dependent action admission performs a bounded storage-only flush
  (at most eight results), then checks unresolved epoch/domain evidence. A locked
  result file withholds dependent native effects and rebirth, not unrelated
  resources/actions. Known committed model accounting stays committed; storage
  recovery never re-invokes it or manufactures fresh predecessor authority.
- Invocation records persist native epoch and affected/dependency domains before
  invocation. Imported INVOCATION remains UNKNOWN. A new owner, GUID, process or
  opportunity cannot bypass same-epoch unresolved native footprint. EXP, digger
  and item effects also retain their uncertainty across rebirth because those
  native inputs/progression persist across rebirth; unrelated lanes remain healthy.
- **Preparation acquisition retry:** the SAME original identity/start/entry/window
  is durable before acquisition. A proven pre-model `acquired` state may retry;
  selected/started/committed/unknown history cannot reopen replacement acquisition.
  Selected gear/support survive selection/storage/preflight failures in memory.
  The exact selected policy and ledger `selected` stage are durable before native
  setup application. Native partial application retries the SAME selection/claim.
- Manual opportunities use the actual `CurrentExecutionCommandId`; retries retain
  the original request. No new-GUID fallback exists. Automatic opportunities stay
  stable for the original plan/rebirth.
- **No-scheduler import:** preparation capture/import now use the existing safe
  `ExecutionPersistenceEntry`, not unguarded `AutomaticRecoveryEntry`.
- **Committed import intent:** terminal manual fight-only preparation does not set
  automatic rebirth completion. Original manual-rebirth choice and explicit
  opportunity are retained, including for terminal records. Completed history is
  retained independently of active native claims, without importing cleanup rights.
- **Original fruit steps:** each preparation persists its original per-fruit
  harvest counters. Its effect opportunity is derived from the ORIGINAL process
  identity plus fruit ID, not a later native counter. A later ripe cycle, retry or
  ledger reload cannot consume a second fruit for the same preparation. Recognizing
  the same committed step still requires current exact setup proof before the next
  effect. Missing original identities quarantine instead of acquiring new ones.
- **Manual post-rebirth result storage:** a manual request retains its original
  rebirth number. After a known model-committed engagement with failed result or
  checkpoint storage, continuation retries only those writes, not another rebirth
  in the successor epoch. UNKNOWN is never promoted merely by matching counters.
  Cached history cannot report a new engagement on an unchanged/rolled-back count.
  `CommitExecutionRebirthCheckpoint()` remains before intent/epoch reset on the
  verified manual success path; the automatic caller remains D-owned.
- **Native callback boundary:** actual resource model/context and allocated bars,
  selected instance/material, support/income source and native-settings identity
  are checked around each leaf. Foreign callbacks retain commitment/uncertainty
  without lending dependent authority; actual per-domain unresolved evidence is
  retained. Expected item material changes are exact per-instance values, not
  blanket permission to adopt any material change.
- **Real pit:** exact native level-only rewards causally revise the SAME retained
  preset/inline/focus/combat instances, including suspended B. Approved preset
  material uses the current core persistence seam. No original gear/digger
  restoration target exists. The nested MAX_PATH admission failure was fixed by
  using a short independent temporary filename in the same ledger directory.

## Native Evidence

The passing 253-assertion selector exercises actual compiled production authority
and native model operations, with unavailable UI/RNG inputs isolated:

- Unverified selected setup produces zero native NUMBER writes.
- Real retained scheduler A -> exact setup claim -> independently acquired CURRENT
  B; native NUMBER commits then rendering throws, with exact blood/NUMBER totals,
  durable result, no second cast and current B release.
- Actual dashboard ITOPOD farming A -> exact pit selection/known-route retirement
  -> actual dashboard B underneath the claim -> native `PitController.engage`,
  reward dispatch, `allLevelUp`, totalGold/gold/toss accounting -> renderer throws
  -> direct CURRENT B, no historical A flash and no second feed.
- Actual native NUMBER result-file lock: successor spell and native rebirth entry
  are withheld while healthy Energy really allocates another 37 units of capacity.
  Storage recovery flushes the predecessor result and invokes only the successor;
  reload of the unfinished invocation remains UNKNOWN.
- Foreign allocated bar, gear material, income source, blood model and epoch
  callbacks retain committed/unknown evidence, reject fresh dependent authority,
  never repeat the native effect and leave independent Energy authority healthy.
- Native fruit activation uses the real reservation/shared allocation seam, exact
  currency decrement, permanent-cost activation perk and one identified harvest
  with the native seed/perk formula. Committed render failures do not repeat it.
- Native EXP and digger cost/base-or-level/currency proofs, refresh failure,
  same-operation deduplication, still-owned EXP input return and preservation of
  a newer native input edit.
- Same-process preparation selection-input failure, selected-policy storage
  failure before native setup, then storage recovery using the SAME selected setup
  and original identity/time/window without replacement search.
- The now-executed precision group directly invokes the BUILT PillBloodAction
  classifier with native binary32 field storage boundaries for zero, nonzero and
  large base values. It accepts exact stored four-stat/zero-blood proof and
  committed render failures, rejects positive AND negative adjacent ULP edits of
  each field, and rejects matching stats without exact blood consumption. It uses
  no epsilon and no blood-only success path. These are classifier-layer native
  assembly checks; the actual game cast remains covered by the separate blood suite.

The 103 source checks additionally cover bounded dirty-result dependency recovery,
cross-owner/acquisition/opportunity and cross-rebirth persistent-effect uncertainty,
prepared-only versus selected/unknown/terminal preparation semantics, fight-only
versus rebirth completion intent, stable per-prep fruit identities, and original
manual rebirth result completion without replay. They use an explicitly named
source authority boundary and are not substitutes for native execution.

New `PreparationHandoffChecks.cs` uses real ledger writes, a locked result file and
reload to verify selected != authorized != retired/durable, interrupted retirement
remains UNKNOWN, old intent cannot resurrect, storage recovery is required before
replacement and unrelated predecessor scopes remain untouched.

## Full Gate History

One complete BlackBar attempt was made after the initial focused groups were green,
MVID `4b59aca3-4637-44a9-b41d-f307364a953d`. Scheduling 305, protected timing 2853,
Iron Pill 459 and the later optimizer/equip/auto-boost groups completed successfully.
The whole suite FAILED in `StageSupportChecks.CheckPrepLocks` because
`NativeFightFixture.Request` could not read
`command-results/stage-support-locked.json` under its temporary `ngu-stage-support-*`
directory (`NativeFightFixture.cs:121`, `StageSupportChecks.cs:363/574`). Its
24,194 completed-group assertions are NOT a full pass count. This historical
failure was subsequently fixed by the fixture owner: its static automation status
now uses the production default rather than null, and the native preemption fixture
supplies its actual Energy cap/current. Full BlackBar PASS 24,603 on MVID
`031156cb-2b38-47d4-84e4-3115b9e65aa1` is retained in
`executor-native-blackbar-current-full.log`; see
`executor-native-fixture-cutover-20261022.md`. The old missing command-result issue
is NOT a current blocker. C did not repeat the full suite in this precision-only
integration or combine that different artifact with the new action253 as one full
release pass. The parent's final same-artifact gate after the manual-route P1 fix
is still required by the supervision safety hold.

## APIs And Hooks

All are nested `Main` members unless otherwise stated:

```csharp
internal FiniteActionResult RunFiniteAction(FiniteAction action, ExecutionGrant grant);
internal JSONObject ExportFightPreparationCommitment();
internal void ImportReviewedFightPreparation(JSONObject commitment, SelectedSetupRequirements setup);
internal static string PreparationFruitOpportunity(FiniteActionIdentity preparation, int fruitId);
internal void PurchaseActionDiggers(bool ignoreSetting = false, double reserveGold = -1d);
internal ExecutionClaim FightPreparationClaim { get; }
internal SelectedSetupRequirements FightPreparationSetup { get; }
internal string FightPreparationIdentity { get; }
internal bool FightPreparationVerified { get; }
internal void CompleteFightPreparation();
```

`FiniteActionIdentity(kind, owner, acquisition, entry, opportunity)` stores semantic
identity only. A leaf invokes ONE native operation, not an old complete manager
workflow. PREPARED and INVOCATION precede the irreversible effect; action-specific
classification and committed/unknown result precede publication. A cached result
may report Committed/Durable but is NOT fresh Verified authority. No native grant,
reference, delegate, old allocation map or cleanup right is serialized.

D already writes exact `fight-prep` selections and consumes export/import hooks.
Original `fruitHarvests` is an object keyed by fruit IDs `1` and `6`, containing
the native counters acquired for the original preparation. The exact selection
snapshot may precede a later durable ledger stage; the current ledger stage is
authoritative and cannot be replaced by the snapshot's older stage.

Temporary setup uses `CaptureExecutionBaselineRouteRetirement(claim)` and explicit
RetireRoute. Known actual farming may retire under its exact witness; unknown
routes retain prerequisites. Reservation/policy APIs are the implemented core
Set/Reconcile/Ready seams, never Recall or saved maps. Setup exit resolves CURRENT
intent. Required final NUMBER/Fu Manchu prerequisites use the selected coupled
actuator and hard required-beard checks.

Inventory now invokes one source-item merge/boost or one cube source operation,
then reobserves before the next leaf. Original native merge/boost math is evaluated
on a detached copy for exact target material proof; native mutation remains the
real swap/boost method. Cube prediction preserves the caller RNG and original
boost state, using the native read-only recycle inputs/loot factory. Retained and
preview instances are protected; borrowed native item inputs have still-owned
returns. The old page-wide harvest, snapshot restores, augment Recall/Transfer
writers, duplicate threshold-spell bodies and catch-and-continue prep were removed.

## External Integration

1. `ActionPreparationCommitmentChecks.cs` IS now compiled/hooked by the fixture
   owner; its 40 native assertions pass within current action253. The precision
   partial is ALSO now compiled/hooked and its 36-assertion group actually passed.
   Neither has an outstanding Compile/call integration request. Only the Preset
   native project was rebuilt/executed in this bounded pass; linked CombatPrep and
   Overlay inclusion was checked from their current manifests. Their next full
   builds/runs belong to the parent's final gate, not an invented additional pass.
2. **Started preparation route rebind:** C still refuses `fight-requested` import
   until the actual original B/D route process/history can be rebound. The current
   B `RebindRouteExecutionCommitments()` rejects a preparation-owned boss row's
   different claimOwner and Inventory footprint. Do not remove those checks without
   implementing composite original-owner rebind, original boss identity/ordinal/
   remaining seconds and exact still-owned route/settings proof. C must NOT call
   StartBossPush to fabricate that history. This is a remaining external release
   gate, not completed positive started-process import.
3. The fixture owner completed real native boost dispatch/refresh migration and
   reports auto-boost203 and full BlackBar24,603. Do not repeat that old migration
   as a current blocker. The new precision checks add no native cube/consume/filter
   attribution/fault coverage; remaining broader operation-specific acceptance
   still belongs to the combined release gate described in the fixture report.
4. **One obsolete sentinel still has a caller:**
   `Managers/LoadoutManager.cs:136` calls `YggdrasilManager.CleanupStaleHarvest()`.
   The Loadout owner must remove that call before C can delete the empty sentinel.
   RestorePitGear is already deleted with no remaining callers. No new compatibility
   state or historical restore body was added.
5. Full same-DLL suites and remaining native blood-window/deadline/history,
   successful normal/challenge rebirth post-epoch, outer-checkpoint and inventory
   attribution/fault acceptance remain combined release gates. Native UNKNOWN is
   never reset by a new GUID or a generic Retry button. No exactly-once guarantee
   across arbitrary game-save/sidecar branches is made. Only the live owner may
   deploy/restart/observe the installed game; no live acceptance is claimed here.
