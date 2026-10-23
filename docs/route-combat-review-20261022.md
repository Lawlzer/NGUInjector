# Route/Combat Review Completion

## Lifecycle Revision Closure

Both independently reviewed P1s are CLOSED, not handed off as known failures.
The older1475cf83 full pass below did not cover nonzero-recall terminal quiescence
or expired-epoch cleanup after NORMAL runtime reset. This section supersedes those
coverage claims with actual red/green regressions and a new matched full run.

### Production Fixes

- `Main.CombatRouting.EndRouteProcess` checks the native epoch BEFORE capacity
  dispatch and ends stale authority with reconcile=false, including a claim the
  normal runtime reset already ended. It performs no route/gear/resource write or
  current-session successor reconciliation against the newer epoch.
- `EndCapacityRouteProcess` has a Quiescing terminal path ONLY after exact current
  ownership, resource-map/context and native Safe Zone retirement proof. It retains
  the semantic delayed-clamp funding limit, ends the hold WITHOUT reconciliation,
  persists semantic evidence, and never installs/funds CURRENT B or restores history.
- Unexported `RouteProcess.ResourceHoldEnded` records that VERIFIED terminal ending
  for repeated service/quiesce/Unload. The repeat path can retry semantic persistence
  but gains no native mutation rights. A merely ended same-epoch claim does NOT get
  that marker and remains fail-closed. Nothing imports this marker as authority.
- The terminal proof additionally rejects preserved same-epoch Gear/Support/Route
  uncertainty even if physical values happen to match. Lane permission/revision loss
  still blocks hold ending; only an exact narrow Safe Zone retirement may execute.
  Foreign/Unknown route and foreign bars cause no native cleanup writes.
- `Main.ManualTitan.CancelManualTitan` retains the old epoch test before clearing
  its producer fields. Expired cleanup skips route semantic publication and ordinary
  capacity-release reconciliation, preserving the newer native epoch AND quarantined
  old semantic bytes. No new death budget, cursor or acquisition is used to clear it.

No production changes in this revision to Main.ExecutionLifecycle, Main.Lifecycle,
Main.Execution, GearOptimizer, LoadoutManager, Main.LoadoutPresets, Main.GlobalPresets,
Support, actions or math. Their existing reset/Unload paths and all core NRE/result/
receipt common-witness changes were preserved. Staged/unstaged work was not reverted
or staged. No live/bridge/UI/injection/debug attach/index/commit/nested-agent/
notification operation was performed.

### Regression Evidence

New owned partial group `CheckCapacityLifecycleRegressions` contains3 behavior
methods /9 native timelines /177 assertions and is hooked into BOTH current route
and finite/action entry groups. Focused selector: `--capacity-lifecycle`.

1. Quiesce/service, actual Unload-first, and forward-manual-disable: actual
   PrepareFundedTitanCapacityWorld -> admitted manual titan -> nonzero E/M Recall3 ->
   real lifecycle retirement/Unload. Assert exact Safe Zone once, actual hold ended,
   repeated retirement/quiesce/Unload, B/A never installed, zero resource/gear writes,
   unchanged delayed over-cap totals/idle buffers/basic12/full E/M/R3 maps and durable
   clamp-safe semantic policy/mask.
2. Native epoch advance -> actual ResetCustomAutomationRuntime ends the old claim ->
   actual ResetManualTitanRuntime clears active/recovering/process/history references.
   Assert ZERO newer-epoch route/gear/resource writes, unchanged exact native maps,
   totals/idle/basic12/R3/route/material and old persisted/quarantined semantic bytes.
   An explicitly admitted real newer-epoch claim/grant is valid and survives repeated
   old-process cleanup. No constant ClaimCurrent or fresh manual budget shortcut.
3. Same-epoch lane permission loss, foreign route, preserved Unknown route, foreign
   bars and externally ended claim remain blocked across repeats. Their evidence is
   neither discarded nor converted into Safe Zone/native return permission.

The existing withdrawal/entry callback epoch matrix also performs actual normal
reset/producer cleanup before a second producer invocation. Its previous generic
retry could start a NEW epoch attempt once expired cleanup worked; that is not old
attempt recovery. Existing same-epoch retry assertions remain unchanged. The matrix
now has194 assertions versus180, with exact no-write/current-map cleanup checks.

The native fixture isolates ONLY the two unavailable Unity callback-scheduler
shutdown leaves (CancelInvoke/StopAllCoroutines), as the existing combat fixture
already does. Actual injector QuiesceExecution, ServiceLifecycleRetirement, Unload,
claim retirement and native Safe Zone entry remain production code. This is not a
native combat cancellation model. A startup assertion verifies exactly2 such leaves.
No shared manifest entry or verification-wrapper change was needed.

### Red Then Green

- RED before production fixes: MVID `e02a64f5-5c15-40d9-b845-d2b39b5438d2`.
  Healthy nonzero capacity quiesce expected true/actual false; normal epoch cleanup
  expected inactive/actual active. Guard case also caught Unknown-route16 -> -1.
  All failed groups were recorded;48 executed assertions are NOT a pass count.
- GREEN focused closure:177 assertions (+1 startup) on
  `ff78fdfb-4be2-4244-a10c-be0a2933a83a`.
- GREEN complete route:1572 assertions (+1 startup) on
  `e5269451-0b98-4f0c-8934-a3fed06235fd`.
- GREEN finite/action entry:430 assertions (+1 startup) on
  `13fc2f0e-b898-451b-9d8b-8fec0c38ced5`.
- GREEN unchanged normal persistence/quiesce group:347 assertions (+1 startup) on
  `e297ff18-15a8-4cf2-b41a-7765f71434f6`.

The first wider route rerun caught the obsolete epoch-retry expectation described
above; it was changed to the required NORMAL reset/cleanup sequence, not weakened
to permit stale recovery. That failed run is not counted as passing.

### Matched Full Gate

**ONE production DLL / MVID `e324a924-da10-4980-8ac7-2781592376e8`:**

| Required gate | Actual result |
| --- | --- |
| Full PresetIntegration | PASS18,316; includes route1572, finite430, normal persistence347, original NRE/retained-data455 |
| Full CombatPrepIntegration | PASS12,011 |
| Full Ownership | PASS10,321 assertions /486 scenarios |
| Relevant source SustainedFocus | PASS2,331 |
| Whole-worktree `git diff --check` | PASS |

Full native run started08:21:59; durations553.36s Preset,138.4s CombatPrep and8.76s
Ownership. No observed mutex queue/blocker. All production/native commands held the
exact shared named mutex through build/run using checks/VerifyExecutorOffline.ps1
with unique GUID logs. Only this one full matched3-suite command was run after
focused green, not duplicate ALL13/full-source twenty-minute checks. The existing
08:00 full-source gate was not repeated; relevant source Focus and diff were rerun.
The fixture emits its existing unused NativeGearSwapped warning; no ignored errors.

```powershell
& "checks\VerifyExecutorOffline.ps1" -Suite @('PresetIntegration','CombatPrepIntegration','Ownership') -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" -LogPath <unique-GUID-log>
dotnet run --project "checks/SustainedFocus/SustainedFocusChecks.csproj" -c Release
git diff --check
```

Logs under `C:/Users/Kevin/AppData/Local/Temp/opencode/`:

- `capacity-lifecycle-red-3360e4a2b443437fb91a90ea583c8bff.log`
- `capacity-lifecycle-green-c164fff16fac4d45bebcdc9fa245aa70.log`
- `capacity-lifecycle-route-green-a52f8862f33c473b9d2ea08ad07e7836.log`
- `capacity-lifecycle-finite-ff20019f0cda46148bdc66402ef6c073.log`
- `capacity-lifecycle-normal-quiesce-129b2bb89fcf48f688c53c3f5a0a87f4.log`
- `capacity-lifecycle-full-native-028b4e80ddf847fe933a75fc41739f56.log`

There is no known remaining P1 from this review or external blocker in the requested
gates. Supervisor owns final ALL13-native artifact capture (wrapper includes AutoExp)
and separate safe redeployment/live Walderp-kill acceptance. Neither was performed
or falsely claimed here; the live scoped UnknownOutcome remains untouched.

## October 23 Capacity Continuation

Implemented the focused response to the captured3c01e825 LIVE Walderp admission
failure. No live repair/rebind/restart, bridge/UI action, injection, diagnostic DLL,
debug attach, staging, commit, nested agent or task notification was performed.
The existing captured artifact and running game's scoped UnknownOutcome were not
touched. Core's newer digger-label/result-projection/receipt common-witness fix and
GlobalPresets publication changes were preserved. This continuation did NOT edit
Main.LoadoutPresets.cs, Main.GlobalPresets.cs, Support, actions, math, Ownership or
LoadoutManager.cs. Existing staged/unstaged contributions were not reverted.

### Actual Verification

All FIVE full native suites below used one production rebuild, one MVID and the
exact shared named mutex held continuously across builds/runs:

**MVID `1475cf83-2ded-40ce-9886-63a87cef273d`**

| Gate | Actual result |
| --- | --- |
| Full PresetIntegration | PASS17,947; includes original retained-farming/NRE455 and route1381 |
| Full CombatPrepIntegration | PASS12,011 |
| Full TitanGold | PASS71 cases /1,497 |
| Full Ownership | PASS486 scenarios /10,321 |
| Full GearEquip | PASS20,805 |
| Source SustainedFocus | PASS2,331; real Main.Execution/Common selected/resource code |
| Source Combat | PASS4,536 |
| Source ResourceWriter | PASS1,553 |
| Whole-worktree diff whitespace | PASS |

Full log:
`C:/Users/Kevin/AppData/Local/Temp/opencode/manual-titan-full-native-7ea8b31fed5142f8ba2206079356906a.log`.
Native durations were598.85s Preset,157.33s CombatPrep,7.05s Titan,9.79s Ownership,
18.29s GearEquip. This was ONE queued command, not duplicate twenty-minute runs.
There was no observed mutex wait before its07:40:40 start. Native fixture emits its
existing unused NativeGearSwapped warning; source Focus emits existing boundary
unused-field/lowercase-name warnings. No errors or ignored failed full groups.

Exact full command shape (the actual LogPath used the GUID filename above):

```powershell
& "checks\VerifyExecutorOffline.ps1" -Suite @('PresetIntegration','CombatPrepIntegration','TitanGold','Ownership','GearEquip') -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" -LogPath <unique-GUID-log>
```

Focused selector is `--manual-titan-capacity` in PresetIntegration. The last full
route selector passed1381 assertions on `e8eae9cd-482e-482a-b836-8355369ebc8b`; the
SAME1381 then passed in the final full Preset artifact above. This retains all719
existing route checks and adds662 capacity assertions. The initial decisive proof
ran BEFORE production edits, PASS37 on
`98fae404-8bef-4bd4-8b65-54405507eb63`, log
`C:/Users/Kevin/AppData/Local/Temp/opencode/manual-titan-proof-19d19cb3bc5749efa2070130928fd2bb.log`.

### Implemented APIs

- `GearOptimizer.CapacityTransitionPlan` / `PreflightCapacityTransition(wanted,
  permitted)`: pure typed result with Recall, minimum E/M capacities and Failure.
  Uses the real PlanEquipGear order and CheckSwapCapacity/native resource projection,
  not lower fabricated current totals. R3 and basic floors remain protected. Existing
  CheckExactSwapCapacity and other consumers retain their preserving defaults.
- `BeginRouteProcess` has explicit optional capacityTransition/resourceRecall/
  acquisition parameters. Only manual-titan owners may opt in. It declares full
  Gear/Support/Route/NativeSettings plus affected permitted E/M domains before effects,
  retains exact selected material and original acquisition, and captures resource,
  support, route model contexts, entry/configuration and protected maps.
- Manual titan explicitly acquires that semantic policy; selection is retained before
  preflight rejection/retry. No retry re-equips to discover feasibility, selects
  replacement gear or acquires a fresh GUID/death budget/deadline. Setup/partial wait
  is charged to the original bounded owned time. AT readiness/native death cursor/
  disarm/rearm behavior is retained.
- `ApplyRouteProcess` executes the exact captured ordinary predecessor retirement
  witness, checks actual Safe Zone BEFORE controlled-empty resource reconciliation,
  even for gear/support no-ops, then uses the real common resource executor and gear
  actuator with matching explicit recall/depletion footprint. Receiptless/foreign/
  Unknown routes cannot be adopted by admission or forced Safe Zone.
- A retained TransitionGrant checks the FULL coupled footprint across setup callbacks.
  `RouteProcessDependenciesReady` plus the retained CombatGrant prove actual empty
  maps, unblocked held lanes, original revisions/permissions/entry/model/material/
  support independently of legitimate native route/idle/death observations. Lane,
  Route or setting revocation cannot acquire a fresh combat grant each tick.
- `ExecutionClaim.CapacityTransition` scopes the direct temporary-policy ingress;
  existing non-capacity action/reservation callers retain their previous behavior.
  `SetupReleased` removes only the fight setup override while keeping original
  resource authority through CURRENT B's complete native gear/support sequence.
- `EndCapacityRouteProcess` retires actual combat before B, retains empty authority
  through successor actuation, and ends it only afterward. CURRENT B may be changed
  by real Preview+Apply during the fight, may have still-lower capacity and changed
  support. No goal preserves current native gear and leaves the affected lane idle.
- `LimitExecutionCapacityFunding`, `ExecutionCapacityBudget`, and
  `ExecutionCapacityFundingSafe` preserve a delayed native clamp's idle debit buffer
  after the hold exits. Only actual current resource amounts can fund current policy;
  restored capacity cannot invent refill. The scoped limit retires after actual
  native depletion; actual native generation under genuine later cap growth becomes
  fundable. No general unchanged-current-pool semantics were removed.
- `BeginExecutionCapacityPresetTransition` supplies selected-actuator VERIFIED
  progress evidence for the current receipt/owner/acquisition while a declared
  capacity claim is releasing. It does not publish a receipt or weaken the core
  prepared-commit witness. Full native support-state keys, including inactive digger
  observations, are retained instead of confusing them with active selection keys.

### Five Review Requirements

| Requirement | Production and native proof |
| --- | --- |
| Continuous full coupled combat proof, separate route observation | Retained transition/combat grants and exact empty policy/context checks; native withdrawal/entry callbacks revoke lane/Route/settings/entry/epoch/model/support/bars; existing real Walderp attack/AI/nuke/readiness/death history remain green |
| Actual predecessor retirement before resource effects, including no-op | Real dashboard receipt/ITOPOD actor retirement precedes native resource presentation; persisted full123 footprint checked inside callback; receiptless and preserved Unknown routes cause zero movement/withdrawal |
| Keep empty authority through B without old fight setup override | SetupReleased and same claim; real public B acquisition during fight, lower cap/changed support, direct B/no-A-flash, clamp-safe real funding, actual no-goal stop, genuine native growth after exit |
| Typed full sequence, no fresh permission/budget on failure | Independent E/M/unmanaged R3/basic12, no/E/M/both/delayed native clamps, accessory staging dip with high final cap but real intermediate native loss, blocked lanes and callbacks, real KnownPartial prefix with same selection/acquisition, no-op still proves resource policy |
| Persist/validate semantic policy before rebind effects | On-disk policy/123-footprint read from the real withdrawal callback; exact supported explicit reviewed Safe Zone import, missing/malformed/mismatched policy/owner/foreign bars reject before native effects; imported unfinished death history remains disarmed |

### Persistence Contract

Route-v1 deliberately extends with `resourcePolicy=manual-titan-controlled-empty-v1`,
resourceRecall mask, resourceApplied/releasing and scoped issue metadata. Manual
spawn rows retain the semantic policy, recall mask and ORIGINAL acquisition. Missing
policy means OLD preserving semantics, never new recall authority. Resource-bearing
rows without the exact supported policy/domain/owner/spawn relationship reject
before claim/native mutation. Reviewed import requires exact selected gear/support,
actual controlled-empty maps, current lane permission and no unresolved lane evidence.
It accepts no imported grants, native references, allocations or cleanup rights.
Imported unfinished manual death history is disarmed, not assigned a fabricated cursor.

Post-hold delayed-depletion budgeting persists separately as
`capacityFundingPolicy=manual-titan-clamp-safe-v1` / capacityFundingLanes. Reviewed
import validates actual allocations and native idle debit protection before restoring
that scoped BUDGET limit; it is not resource recall/cleanup authority. Absent policy
cannot grant a capacity limit's associated recall rights or reinterpret old records.
Foreign/unknown or unmatched partial setup/release import stays quarantined rather
than inventing continuity, replacement gear or a new attempt.

### Failures Found And Fixed

- Initial consumer test exposed SetExecutionResourcePolicy being called after a
  new process marked itself admitted but before its producer field was assigned.
  Policy admission now finishes inside the existing constructor admission window,
  before any native effect. No blanket current/grant Boolean was introduced.
- Changed planned-support B exposed a legitimate transition being mislabeled foreign
  after the empty hold ended. The scoped common progress seam above keeps the core
  full-state observation contract. Source/new native success does NOT clear deployed
  UnknownOutcome retrospectively.
- The first large-pool baseline fixture hit real native NGU caps and was mostly idle.
  It was corrected with explicit native NGU level inputs and real four-E/two-M
  global weights so zero-idle fully funded admission is ASSERTED, not assumed.
  Growth asserts the acquired policy's total funding rather than assuming a single
  NGU receives every added unit.
- Source Focus lacked native-capacity and receipt progress DATA members needed by
  the real Main.Execution link. Added explicit capacity inputs/receipt fields, not
  fake grants or no-op production methods, plus3 real core delayed-budget/growth
  assertions. The original source core/action-policy behavior remains green.
- Those failed focused runs were corrected and are not reported as passing gates.

### Remaining Live Gate

No proven external offline blocker remains in the requested full suites. This does
NOT certify a new-build Walderp kill. The sole live owner must safely freeze/redeploy
the fully checked candidate, retain the actual game's original acquisition/entry/
AT3000/3000/death limit, handle existing scoped UnknownOutcome through reviewed
authority, and observe actual native fight/kill/death outcomes. No diagnostic
injection or metadata attach is needed or authorized. Temporary idle in the explicit
fight hold is legitimate bounded preemption, NOT all-Energy-funded combat.

Unsafe/foreign/permission-lost retirement stays scoped blocked. It does not force a
return, expand an unacquired recall footprint, silently acknowledge resource evidence,
import a native death cursor, or use historical allocation/gear restoration. This
work does not certify every unsupported imported partial-release history, every
other route's lower-cap transition, native reward exactly-once behavior, or live soak.

## Outcome And Verification

Route-owned review implementation and the requested source/native gates are green.
This is offline evidence, not whole-application release acceptance. Existing staged
content was not staged/unstaged/reverted; no commit, deployment, injection, restart,
bridge or live save operation was performed. Shared core/fixture edits were limited
to the coordination seams authorized during this continuation.

| Gate | Final result | Production MVID |
| --- | --- | --- |
| Source Combat | PASS 4,536 assertions, including 2,174 titan eligibility assertions | Source decisions only |
| Source mutation sensitivity | PASS all 3 behavioral mutants | Source decisions only |
| Full TitanGold | PASS 71 cases / 1,497 compiled-native assertions | `87edec2b-ebe7-43e5-a2be-0223022492e6` |
| Full route selector | PASS 719 native assertions | `fd46bb01-3f32-4ce8-adc1-95fb78f45f3f` |
| Full Preset | NOT GREEN; stopped after 9,140 executed assertions, not a pass count | `e09c7212-cca4-4a55-88c7-4b69edc72631` |
| Whole-worktree `git diff --check` | PASS | N/A |

Each native row is its own serialized production/fixture rebuild, not a claim that
different MVIDs are one immutable binary. No unlocked production/native build/run
was used. Exact commands, from the repository root:

```powershell
dotnet run --project "checks/Combat/CombatChecks.csproj" -c Release
& "checks\Combat\VerifyRegressions.ps1"
& "checks\TitanGold\VerifyOffline.ps1"
& "A:\misc\NGU-Automation\temp\verify-executor-offline.ps1" -Suite PresetIntegration -Selector --route-executor-cutover
& "A:\misc\NGU-Automation\temp\verify-executor-offline.ps1" -Suite PresetIntegration
git diff --check
```

TitanGold is not a supported selector in the approved parent verifier. Its owned
VerifyOffline.ps1 takes the SAME named mutex for production rebuild, native fixture
rebuild AND execution. The approved parent script was only read, never edited.

## Exact Changes

- Main.CombatRouting: clear removed manual activity intent independently of pending
  native retirement; retain exact delegated evidence and prerequisites on failure;
  end the producer's real claim only after verified delegated retirement. Manual
  tower disable uses the existing bounded retirement grant for its one idle return.
  Removed the disabled SnipeZone TryGoldDropSwap branch, the core deletion audit's
  last route-owned reference to that legacy workflow.
- Main.GlobalPresets: one coordination call after the exact delegated Safe Zone
  check and before consuming its witness. The original ownership predicate and
  failure/handoff assertions remain unchanged.
- Main.MaxItopodPush: retain a single pending selection/process across setup retries;
  retire pre-routing acquisitions on failure; preserve pending native retirement
  even before Routing becomes true; prevent a fresh death budget while that claim
  remains. No field-return authority is invented for an unapplied process.
- Main.CustomAdventure: recognize only the entered attempt's zero-HP/Safe-Zone death
  under unchanged actual setup/model/settings observations; retain that claim through
  healing/lower non-titan fallback. Persist the lower target before dependent entry.
- Main.TitanAutomation: unsupported pure capture returns a bounded failure and its
  original one-hour cooldown, rather than throwing/researching every combat tick;
  revoked configuration/native/stat prerequisites cannot earn reset credit;
  unavailable optional Gold-beard utility preserves current support instead of
  preventing an otherwise native-achievable titan.
- No CombatManager/CombatHelpers, Main.ManualTitan, Main.BossPush, ownership suite,
  persistence implementation, support implementation, math or shared manifests were
  edited in this continuation.
- Source Combat now compiles exact decision/read-only slices, never route producers
  against fake Main authority. Removed the obsolete coupled source checks and their
  snapshot stubs. Pure prompts/timing/rules/eligibility remain executable. Duplicate
  idle/preserve routing labels are no longer counted as pure decision coverage.
- TitanGold replaces original-restore/token assertions with real CURRENT-baseline
  and no-successor claims/observations. Keeps independent native IL table checks,
  all 33 versions, spawn/reset/batches/progress/cooldown/priority/bottleneck/parity,
  retained stronger obligation and exact KnownPartial/render/unknown timelines.
- RouteExecutorCutoverChecks adds 11 focused timeline methods plus the unchanged
  existing dynamic-manual handoff test to its full selector. Preset Program has only
  two optional fixture hook calls for the native game/UI boundaries implemented in
  that owned route file. Concurrent core-owned Program changes were preserved.

## Safety Mapping Before Migration

The source Combat fixture predates the shared executor. Its string gear snapshots,
restore leases, overlay-success switches and producer stubs are not authority and
must not be extended with fake grants. The replacement boundary is:

| Original requirement | Retained/replacement evidence |
| --- | --- |
| Walderp true/false/malformed prompts, cooldown reservation, survival, skill unlocks, ordinary non-fast priorities | Source decision-only extraction from the actual CombatManager/CombatHelpers methods; no route/setup/grant code is compiled or claimed tested there |
| Ordered inclusive adventure rules, disabled/locked matches, finite stat telemetry | Source actual AdventureRule and read-only production rule methods |
| Signed spawn windows, non-stat prerequisites, exact version dispatch, numeric thresholds | Source actual ZoneHelpers; native TitanGold plus native table checks for all 33 versions |
| Manual titan positive AT, manual unlock, death-budget continuity, lost cursor, kill/reset/disarm/rearm | RouteExecutorCutoverChecks actual producer with real executor claims/grants; native outcome observation is explicit |
| Manual max-ITOPOD support gate, entry healing, disable cleanup | Native route producer timelines, including actual support callback revocation, current claim verification and one native idle return |
| Gold death/lower-zone fallback, one-hour cutoff, disable, partial setup, foreign/epoch failures | Native adventure-gold producer timelines; CURRENT successor or native preservation with no successor, never original gear restoration |
| Tower bounded deaths, pause/resume, borrowed fields, foreign/model replacement, native zero | Native tower producer timelines with exact field/identity checks, continuous event cursor, real HP gate, native input-callback model replacement and no historical equipment return |
| Titan co-due/staggered reset credit, joining, progress timeout, cooldown/candidate scan, Fight priority | TitanGold actual compiled producer, pure selection before application, exact native checks |
| Stronger titan obligations, impossible selection, partial/render failure, deadline | TitanGold retained pure replacement selection and native upgrade timeline, no speculative trial/rollback |
| Owner release A/temporary/CURRENT B, no successor, foreign setup, callback revocation | Native route claims and actual producer release; exact references and resource-write counts |
| Boss preparation handoff, native invocation, pending stop | Native preparation claim plus actual finite-action runner/native startNuke/beginFight/nukeBosses; no clearing native flags as cancellation |
| Walderp attack/AI leaf acceptance | Real native RegularAttack.doMove, PlayerController damage/cooldown and EnemyAI.takeDamage/didCorrectWaldoMove for true and false prompts; native AI itself consumes the prompt |

Historical tests are replaced only where the contract itself changed: original gear
is not a return target; pure infeasibility performs zero speculative swaps; render
failure with verified model state is not failed application; known partial state
retains its acquired selection. Source decision tests do not accept a synthetic
ExecutionGrant or HasSuccessor Boolean.

## Reproduced Handoff

Full Preset on MVID `e41a6957-f9ee-4c9f-85a6-93bcbb294ebd` reproduced the original
delegated-removal assertion: same claim, zone 0, entries 97 -> 98, captures 393,
transfers 131. Clearing activity intent before attempted native retirement fixes
that assertion without changing its predicate or losing retained evidence.

The next full run, MVID `d3a01762-9b3b-4fa6-a04b-9c36666773bb`, reached the next
assertion: native release observes only `-1`, not `-1,1000`. Core's
RetireDelegatedManualRoute consumed its evidence without ending the route producer's
still-live executor claim. The implemented CompleteDelegatedManualRouteRetirement
hook requires old exact evidence, native Safe Zone, unchanged model/settings/setup,
and inactive manual intent. With the user's expanded coordination authorization,
core now invokes it after native retirement and before consuming evidence. This is
not authority imported from an observed Safe Zone alone.

The final full Preset run passes the ORIGINAL dynamic-manual checks unchanged:
zone entries 97 -> 98 on the failed retirement, then `-1,1000`, entries 101,
captures 396, transfers STILL 131, and claim consumed. The route selector separately
executes this entire timeline with all foreign/model/epoch/entry/preference guards.

## Native Boundaries

The source projection never emits ExecutionGrant/ExecutionDomain and supplies no
successor switch. Its action boundary records decision requests only. Three isolated
mutants prove prompt, cooldown-reservation and inclusive-rule assertions can fail.

Native route tests use the real Main.Execution/selected-setup/finite-action code.
New native leaf hooks supply deterministic floating RNG, replace damage-log text,
discard only IdleAttack border rendering, and keep the actual Selectable
m_Interactable write without Unity scene-transition notification. Player/enemy HP,
native prompt decisions, button readiness, shared cooldown, native boss flags and
all production grants remain real. No attack, AI damage handler, boss invocation or
cancellation is replaced with a model-success delegate.

Native support/game construction now includes actual controller tables, unlocks,
input objects and empty R3. An early TitanGold migration run exposed the unavailable
Unity JSON serializer; its existing shared field-codec boundary was linked rather
than replacing settings validation/publication. Another exposed a fixture-created
uninitialized Main pending index of 0: new-object setup now initializes actual route
indices to -1 instead of accidentally replaying a nonexistent pending upgrade.
Those failed runs are not included in the pass counts above.

## Remaining Gates

1. Full Preset is still blocked in core/explicit-overlay integration, NOT the fixed
   delegated-manual route. Latest failure is CheckManualAdventureOverlay's
   `manual-overlay-True fresh stage acquires base policy under explicit overlay`.
   The concurrent core fixture now uses explicit stage restart: entry 2 is current,
   old receipt entry 1 still occupies native ITOPOD, and application rejects with
   `Selected setup is waiting for the current adventure owner` / DifferentOwner.
   Its readiness/ownership assertion was not weakened. Core owns the old-baseline
   retirement/reacquisition seam; this report does not claim whole Preset acceptance.
2. Walderp TRUE/FALSE native attack/damage/AI and boss native invocation/failed-nuke
   retirement now execute. Complete native Walderp kill/death/log/reward scheduling,
   all four attack leaves, native boss wins across the entire bounded fight window,
   and live scheduling remain unverified. Supplied spawn, kill/phase and PlayerLog
   observations elsewhere are still explicitly model observations, not those leaves.
3. The removed source fixtures' fake ten-kill tower model is not claimed native
   full-climb acceptance. Exact bounded deaths, healing, disable, input/foreign/model
   return and owner-retirement are tested natively; a complete native twenty-kill
   climb/permutation matrix and all funded CURRENT-B AT/W/deadline combinations are
   still broader integration gates.
4. Persistence/rebind continuity, full CombatPrep/public OS/W acceptance, ownership,
   final global legacy-manager deletion and live restart/soak remain their owners'
   work. This continuation did not change those contracts or claim their passes.
