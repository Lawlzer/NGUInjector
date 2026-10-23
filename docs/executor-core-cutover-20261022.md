# Core Cutover Integration

## Retained Farming Rebind Repair

Confirmed the failed-live NRE OFFLINE from the retained October23 checkpoint,
24hr plan/library and native save. The original captured distribution, game,
bridge, diagnostic binaries and live files were never mutated or invoked.

### Confirmed Cause

The actual imported six-digger semantic decision contains IDs/levels/parameters,
not display names. In-memory result inspection found twelve null JSON strings at
`candidates[0|1].support.diggers[0..5].name`. LoadoutPresetCandidateJson assigned
`d.Name` directly; SimpleJSON created JSONString(null), then the bounded payload
size check called ToString -> JSONNode.Escape -> aText.Length and threw NRE.
This was AFTER real native setup/funding/floor196 entry, but BEFORE the caller
published its receipt. Candidates[0].Copy and missing scoring references were NOT
the null boundary. The old broad native-effect catch then discarded presentation
and withheld publication as UnknownOutcome. Existing sparse/empty-support rebind
checks did not exercise this retained six-digger result shape.

### Focused Changes

- Main.LoadoutPresets.cs derives digger display labels from the current captured
  snapshot by the exact retained ID. No empty-name default, serializer-wide null
  bypass, codec schema change or reconstructed historical metric was added.
- Result projection is separated from native commit classification. A common
  coupled grant fences the actual selected setup/material, resource intent/map,
  native object references/settings/route, current/idle pools, epoch, permissions,
  claims and original scheduler identity. It is checked across projection and
  again at Main.GlobalPresets.AcquireGlobalPreset before receipt publication.
  A result-only exception yields ModelAppliedRenderFailed with its error retained,
  while the still-verified native receipt publishes without repeating effects.
- Lost grant/model/funding proof is NOT promoted. It keeps the application
  uncertain, preserves the original selected policy and uses the existing review
  quarantine. Ordinary callbacks cannot replay it; only a NEW valid tokenized
  review can admit continuation. Scoped AT-setting yield still retains known
  model accounting while withholding readiness/credit; its old assertions pass.
- Resource grant continuity now permits ONLY the exact applying-policy/owner to
  its own published receipt handoff, with matching policy epoch. Same preset ID or
  numeric equality alone is insufficient. Native telemetry now verifies actual
  current-owner resource/support authority, not just matching allocation arrays.
- Permission observation uses the receipt's resolved Execution control flags,
  not uninitialized observational flags on a sparse imported semantic candidate.
  Positive/empty controlled lanes still require their own current permission.
  D's persistence/import implementation and Walderp's capacity guard are unchanged.

### Retained Native Proof

checks/PresetIntegration/Fixtures/CapturedFarmingRebind20261023.json is generated from the real retained
post-live payload (source checkpoint SHA256
`A2A14EEED815C2B6E9CF68769ED83AC71A2C61E86E740C0F1EC22418A05ED778`).
All12 complete native material descriptors match, including the save-omitted
runtime path/unique fields supplied from their retained observations. The fixture
keeps acquisition `3c2748bd93c74f578553dcee8f3b09a2`, entry4, horizon49822,
original definition/scoring references/credit, AT/W history and zero exclusions.
Original AT getters execute; scene slot entitlements/presentation are offline
boundaries. Unrelated bonuses remain neutral and a finite enemy template makes
the unchanged floor196 eligible; this is NOT full live-stat parity or a weakened
production safety predicate. The fixture README states that limit explicitly.

The existing full/core hook and new --retained-farming-rebind selector execute six
timelines /455 assertions: positive imported farming; projection-only failure;
Route/funding/model loss during projection; and Route loss at caller publication.
They assert actual repair writes, native entry196, current grant verification,
no repeated model/route writes, original timing/history, spent-token rejection and
successful same-entry recovery ONLY after another real review plus explicit native
Safe Zone handover. Game/plugin instrumentation changes only in-memory offline
assemblies and test logs. Shared loader hooks/manifests only link this owned partial.
Each retained case has an isolated short runtime path: earlier full attempts caught
shared-library reference contamination and desktop CLR MAX_PATH; those harness
errors were fixed without changing library validation or production guards.

| Gate | Actual result / production MVID |
| --- | --- |
| Exact retained null-name reproduction | RED after18 assertions; `0d086486-b8fd-46ff-8126-8928d61cd8e3`; full Escape/LoadoutPresetJson/prepared-apply stack and exact null paths logged |
| Projection-fault control after label fix alone | RED after67; `05cc2be3-9fa8-4f2f-b5f1-71908bcf3823`; proves label repair alone did not protect publication |
| Final isolated retained selector | PASS455; `cd49e5cf-c202-4057-8c8c-4e6981b93219` |
| FULL Preset, including all new cases/actions | PASS17,285 (baseline16,830 +455), actions253 unchanged; `f9427d5a-9bca-450e-968f-47b6860a4521`; native suite562.96s, exit0 |
| Same-code core/budget diagnostics before final harness isolation | PASS2,262 =1,634 core +628 budget on `59a45bd4-5dbd-4487-ab17-54097be882ab`; that earlier FULL run stayed RED for its fixture-library error, never promoted by diagnostics |
| Source Presets / full SustainedFocus | PASS8,653 /2,328 |
| git diff --check | PASS |

All production/native commands used checks/VerifyExecutorOffline.ps1 and unique
GUID logs. Full Preset was fail-fast on the shared mutex (no queued run) and bounded
to600,000ms. No full thirteen-suite/source/browser freeze or live pass is claimed.
Retained logs under `%LOCALAPPDATA%/Temp/opencode/`:

- `core-retained-rebind-red-34f1792f49bf4833b74517c5558d41a3.log`
- `core-retained-rebind-projection-red-cdacd2a213ce455aa164b71a9a3d1d1c.log`
- `core-retained-rebind-isolated-final-b275264dce3e4c169f5f6df26c3257c5.log`
- `core-retained-rebind-full-final-3036bbb4759f492da20de98c8cb74d6a.log`

### Live Owner Repair Limits

The existing 3c01e825 live UnknownOutcome is NOT retrospectively verified by this
patch. Do not repeat its review/handover loop on the currently loaded old code.
After a fresh complete candidate gate and authorized save-backed deployment, the
live owner may obtain ONE NEW PreviewExecutionRebind token and ApplyExecutionRebind
for the original current entry/selection. If inherited unowned native ITOPOD still
blocks normal setup, an explicit Safe Zone handover may be required; ordinary
continuation must then independently verify and publish the same acquisition.
No reset, forced Equip, target reduction, invented receipt or uncertainty clearing.
Changed/missing material, lost provenance, route feasibility or Walderp's retained-
Energy capacity rejection must remain blocked. This work does not fix that separate
capacity finding, prove the crash's final six seconds lossless, or establish live
credit/refill/fruit/Walderp/soak acceptance. No live operations, bridge commands,
diagnostic injection/attach, staging, commit, nested agents or notification tasks.

## Final Manual Resume Fence

Implemented the FinalSafetyHold P1 in Main.GlobalPresets.cs only. The actual
RunCustomBeardAutomation -> RotateGlobalPresetBeards -> resource-render -> manual
support resume path reproduced RED on MVID `ee40ef2c-617d-4f02-b16b-a41592ed9a6a`:
after 39 assertions, Route-only revision invalidation left physical prerequisites
unchanged but the native route re-entered zone 0 once (required zero).

- ManualSupportRouteClaim retains its ORIGINAL common ExecutionGrant from before
  support retirement, across resource rendering and pending retries. It binds
  current intent/preferences, selected material, native route/resource/support
  object identities and epoch; the common grant supplies lifecycle/coordinator,
  domain/conflict revisions, permissions and exact resolved-claim identity fences.
- The manual setter now requires VerifyExecutionPrerequisites with that grant and
  its actual footprint immediately before AND after native changeZone. Matching
  physical fields cannot renew authority. A new Route claim cannot silently finish
  manual resume/consume cadence. Ordinary farming and the coupled CombatPrep route
  proof are unchanged; no new restore workflow or resource grant was introduced.
- The existing ExecutionCoreCutoverChecks full/selector hook runs 12 native timelines
  / 383 assertions: unchanged exact manual resume; renderer-only Route revision,
  Route conflict and foreign Route claim; permission/controller/model/epoch/intent
  loss; and post-native revision/controller/foreign-route callbacks. Pure Route loss
  leaves native resource verification healthy with zero affected resource lanes.
  Negative controls retain committed support/funding/gear, foreign routes/claims,
  entry/budget/history, and do not consume cadence or re-enter on pending retries.
  Native toughness completion (Power still unfinished) reaches the REAL AT renderer;
  no physical predicate or authority was stubbed to manufacture this boundary.

| Post-fix gate | Actual result / production MVID |
| --- | --- |
| Final execution core + commit budget | PASS 1,807 = 1,179 core (including new 383) + 628 budget; `a48af623-c792-436c-9ba5-d592641f58cc` |
| Existing manual-overlay selector | PASS 276; `6f0ad1a6-0303-4e67-8007-e438aa8ad5d6` |
| Bounded full Preset | INCONCLUSIVE at 600,000 ms command limit (including mutex queue); `911894bc-867d-40fb-b710-6e16332317a2`. Last completed group: new manual support authority 383, after resource coordination 1,991 / zero failures, inline 462, foundation 42 and persistence 347. No assertion failure, next core summary or full PASS printed. No Preset/CombatPrep fixture process remained afterward; newer MSBuild PID 39172 (start 02:27:06) was observed and left untouched. |
| Full source SustainedFocus | PASS 2,328 |
| git diff --check | PASS |

All production/native attempts used checks/VerifyExecutorOffline.ps1 and the exact
shared mutex. Retained GUID logs under `%LOCALAPPDATA%/Temp/opencode/`:

- `core-manual-resume-red-37b68321918a4be3b7cdb578410eb28f.log`
- `core-manual-resume-final-core-ca086bfa2281404d9cd17a675ac87f11.log`
- `core-manual-resume-overlay-923dae088228489ba3ab69c71560098a.log`
- `core-manual-resume-full-preset-44681fe712c64679b0c2e3ca8568b798.log`

Only Main.GlobalPresets.cs, the owned core-check partial, and this report were
edited. Shared Program/manifests and all capacity/weighted-phase/C/D work remain
untouched. No staging, commit, live operation, nested agent or notification task.
This closes the reported boundary in scoped native proof, NOT the final same-artifact
twelve-suite/source/browser/freeze/deployment gate. Pre-fix MVIDs are not final proof.

## Supervised Weighted Route Proof

The reported startup defect reproduced on MVID
`c4c6a114-ee14-49aa-a477-089bbdee91b5`: FAIL after 12 native assertions,
`phaseVerified=True; normalBaseline=null`, route blocked by the normal-preset guard.

- Audited all three ApplyLoadoutPresetRoute callers. Main.LoadoutPresets.cs now
  consumes CombatPrep's actual coupled owner grant at every native callback.
  Ordinary preset routes still require the same CURRENT baseline intent/reference,
  exact map and resolved temporary claims. Missing baseline is never success.
- Main.CombatPrep.cs binds the funding intent's authority to its original native
  resource model references. Main.CombatPrepAdventure.cs verifies the executor's
  exact CURRENT detached phase intent/map, both verified resource lanes, selection,
  support, adventure settings, phase/entry/deadline, epoch and coordinator. No normal
  baseline is fabricated and no previous success Boolean substitutes for observation.
- The owned ExecutionCoreCutoverChecks.cs adds a Program partial method exposed by
  the EXISTING CombatPrep `--check CheckWeightedRouteOwnerProof` hook. No shared
  Program, manifest or fixture file was edited. Fourteen timelines / 473 assertions
  verify actual funded maps/native ITOPOD entry plus pre-route stale funding/model/
  intent and native callback loss of funding, foreign Magic, model, permission,
  claim, epoch, coordinator, support, selection or settings. Failed proof performs
  zero farming/resource/gear writes, preserves foreign state and cannot renew entry
  or budget. The runner's initial setup adds three assertions (476 total).

| Current gate | Actual result / production MVID |
| --- | --- |
| Original startup regression | PASS 40; `70617d84-6f76-4054-a9ae-96ae25da72e6` |
| All 16 weighted-adventure groups | PASS 689 runner assertions / 685 group assertions; `34da1947-6557-4ad5-98e9-04d2dee72044` |
| Owned route-owner proof matrix | PASS 476; `fa8995ae-a301-4a45-896a-1ca86db7c067` |
| FULL CombatPrepIntegration | PASS 12,011, including weighted groups and shared combat 1,126 / zero failed groups; `6633121b-b2d3-4dfc-8b29-b1276403ed0f` |
| Normal Preset execution core + budget | PASS 1,424 = 796 + 628; `da1c57dd-72a0-4aec-81d3-9eec1fd65c4c` |
| Full source SustainedFocus | PASS 2,328, including all 19 hard-support timelines / 104 assertions; CustomBeards unchanged |
| Optional FULL PresetIntegration | INCONCLUSIVE: 1,200,000 ms tool timeout; `809e61e6-3422-4680-864c-e8c83d96862e`. Resource coordination 1,991 / zero failures, inline 462 and persistence 347 passed before the next core-group summary; no assertion failure or full PASS printed. Offline process tree exited. |
| `git diff --check` | PASS |

Every production/native build and run used `checks/VerifyExecutorOffline.ps1` and
the shared named mutex. Combat selectors were `--check CheckAdventureWeightStartupRoute`,
`--adventure-weight`, and `--check CheckWeightedRouteOwnerProof`; full CombatPrep and
optional full Preset used no selector. Logs: `%LOCALAPPDATA%/Temp/opencode/core-weighted-route-*.log`.
Full combined release remains unclaimed. Prior route work and unrelated/index changes
were preserved. No staging, commit, live operation, nested agent or parent notification.

## Supervised Capacity Admission

Fully-unmanaged capacity-only ordinary service now verifies **Applied**, not safe
Waiting. The new native regression first failed after 5 assertions with
`Preset apply requires a managed resource lane.` on production MVID
`f67c3501-3d88-4b7e-bd7a-4ec12d4bd7fd`.

- Audited all three LoadoutPresetApplyBlocker callers before adding optional exact
  candidate intent. Selection-free calls retain lifecycle/combat/setup admission;
  Preview/Apply and Equip require each selected controlled lane's own permission
  before mutation. Receipt verification uses the acquired candidate's control mask,
  not the global Manage switches or replacement library definition.
- Main.LoadoutPresets.cs and Main.GlobalPresets.cs are the only production edits.
  Existing preserving swap, native-context, foreign-callback and grant checks remain.
- ExecutionCoreCutoverChecks adds 9 timelines / 167 assertions: ordinary/dashboard,
  real larger-capacity Preview/Apply swap, Equip, scheduled acquisition, unsafe pool
  capacity, native context replacement, foreign callback, and positive/empty lane
  permission loss. Native allocations/current/idle, Basic Training and managed R3
  remain unchanged; selected setup and resource intent acquire no E/M authority.
  Ordinary replay remains Applied with the same acquisition after a library edit.

| Gate after this change | Actual result / production MVID |
| --- | --- |
| `--execution-core` | PASS 1,424 = 796 core + 628 budget; `bf73fa3b-c29d-4628-a5cd-ba50fb9c25a6` |
| `--manual-overlay` | PASS 276; `e8e7e556-1157-48f0-9862-482ca7e6673a` |
| `--inline-stage-setup` | PASS 462; `67f33bb6-ba2d-45d6-9152-6d452745709a` |
| D's unchanged `--execution-persistence` | PASS 347; now prints capacity ordinary service Applied; `ce376464-7f84-4dd3-99ac-bf07d62c74f2` |
| Source Presets | PASS 8,653 |
| Source SustainedFocus `--resource-compatibility` | PASS 297 + 19 |
| Full source SustainedFocus | FAIL 16/19 separately owned hard-support prerequisite cases in SupportPrerequisiteChecks |
| `git diff --check` | PASS |

Every production/native build and run used `checks/VerifyExecutorOffline.ps1` with
the shared named mutex. Logs: `%LOCALAPPDATA%/Temp/opencode/core-capacity-*.log`.
Source gates used `dotnet run --project checks/Presets/PresetsChecks.csproj -c Release`
and `dotnet run --project checks/SustainedFocus/SustainedFocusChecks.csproj -c Release`,
with `-- --resource-compatibility` for the focused run. Full combined native
acceptance was not rerun or claimed. D's tests, shared Program/manifests, BlackBar,
native action fixtures, other owners' implementation files and index state were
untouched. No staging, commit, live operation, nested agent or parent notification.

## Resumed Stop Cleanup

Implemented the supervisor's narrowly owned uncontrolled-lane stop finding after
reading agent-supervision-20261022.md, ../wip.md and current production/fixture code.
This section supersedes earlier acceptance claims below. No parent WIP edit,
staging/unstaging, commit, live access, restart or deployment. No settings, route,
combat, Ownership, shared Program/manifest, or parent/D-owned execution/lifecycle
file was edited. The finite-action hooks were inspected at native loader lines
344 and 4490; they already exist and were not changed.

### Changes

- Main.GlobalPresets.cs: StopManualGlobalPreset requires the retained execution's
  declared ControlsEnergy/ControlsMagic before testing still-owned allocation
  observations. Matching observed strings and global Manage flags cannot acquire
  an otherwise uncontrolled lane. The existing explicit temporary-Magic policy may
  also retire, but only with its matching allocation stamp and exact native context
  references. No compatibility flag or new persisted schema was introduced.
- Main.CustomFocus.cs: audited the lane-plan asymmetry. Cleanup permissions are
  captured only for actual plans, so a missing Magic plan does not authorize Magic
  even when ManageMagic is enabled. Release snapshots and retires the OUTGOING
  acquisition's cleanup callbacks before native work; both admission and grant bind
  the original selection identity. StopCustomFocusGoal snapshots intent before
  dashboard release, and cannot clear or consume a newly installed focus afterward.
  This also protects a same-goal/new-acquisition successor, not merely a new goal name.
- checks/PresetIntegration/ExecutionCoreCutoverChecks.cs: added three native groups
  and owned fixture helpers, using the existing --execution-core entry point.
  Sixteen stop/handoff timelines add 159 assertions beyond the previous 150 core
  assertions. Assertions inspect actual lane destinations, current/idle pools,
  basic training and managed R3, not just control flags. No old assertion was weakened.
- This report is the fourth and final edited file in this resumed cleanup scope.

Native coverage includes Energy-only/Magic-only/capacity-only stop, foreign edits,
duplicate stop, explicit controlled-empty, current normal-plan handoff, committed AT
render failure without repeated accounting, stopping an active explicit temporary
Magic policy, Magic-only focus, missing-Magic-plan focus, same-goal/new-goal focus
successors during AT release, and dashboard-to-focus succession inside Quick stop.
No historical allocation or pool is restored. Basic training/R3 stay unchanged.

### Verification

| Gate | Current resumed result |
| --- | --- |
| Protected production + PresetIntegration --execution-core | PASS 937 = 309 core + 628 budget; MVID `5e3d151f-385c-4e13-acee-094eee028310` |
| Source SustainedFocus --resource-compatibility | PASS 43 focus cases / 297 assertions and 4 resource-caller cases / 19 assertions |
| Full source SustainedFocus | NOT GREEN: cached optional Fu must not report a verified hard prerequisite when Fu is absent; ExecutionCoreChecks.cs:133 via BeardAutomationChecks.cs:44 |
| git diff --check | PASS |
| Full native Preset | Not rerun in this scope; parent/review coordinator owns the combined full gate |

Commands used:

```powershell
& "A:\misc\NGU-Automation\temp\verify-executor-offline.ps1" -Suite PresetIntegration -Selector --execution-core
dotnet run --project "checks/SustainedFocus/SustainedFocusChecks.csproj" -c Release -- --resource-compatibility
dotnet run --project "checks/SustainedFocus/SustainedFocusChecks.csproj" -c Release
git diff --check
```

Every production/native attempt used the named-mutex verifier. A transient route
fixture compile error (LastInlineException at RouteExecutorCutoverChecks.cs:590)
was left to its active owner; subsequent native builds and runs passed. The full
source failure is a separate required-beard/setup-verification finding, not a stop
cleanup failure; its assertion remains intact. Parent must coordinate that owner
and the full Preset gate. No cross-owner interface change is required by this fix.
Earlier route failures/pass totals below are historical, not claims about the
current combined tree or permission to deploy.

Parent WIP evidence: core uncontrolled-lane STOP cleanup and successor-safe focus
release implemented; native core/budget 937 PASS on the MVID above; focused source
316 PASS; full source blocked at the exact cached-Fu prerequisite assertion; full
combined Preset remains parent-owned. No broad release/deployment completion claim.

## Earlier Acceptance Continuation

Current results supersede the earlier foundation-only status below. No live access,
deployment, restart, commit or parent WIP edit. Parent retirement-grant restrictions
were preserved: Route/NativeSettings only, exact still-owned setup, two-second bound.
Every production/native run used the parent's named-mutex verifier, never a standalone
production build or unlocked fixture EXE.

| Current gate | Actual result / production MVID |
| --- | --- |
| `--execution-core` (new core timelines + all commit-budget cases) | PASS 778 = 150 + 628; `a2227a36-daec-468a-acb8-5fb4082ce190` |
| `--preset-final-review` | PASS 1,056; `280bc5f1-e7b1-44b8-bcae-e04c5b6308fc` |
| `--inline-stage-setup` | PASS 462; `1c7f8925-4848-4ced-9e6b-7802824f50eb` |
| `--reviewed-executor` | PASS 127; `95af6e44-474f-4159-9112-51f0831aa8db` |
| Full source SustainedFocus | PASS 2,153; real Main.Execution linked by parent manifest |
| Pure Presets math/oracles | PASS 3,017 |
| Source ResourceWriter | PASS 1,553 |
| `git diff --check` | PASS |
| Full Preset, no selector | NOT GREEN; latest `88b822ec-1bd5-4dd0-90b4-b8a9946b8c89`, blocked at CheckAdventureDynamicManualHandoff delegated-route removal |

Full Preset reaches/passes readiness, frozen horizons, actual AT lifecycle, all 33
native titan requirement versions, scheduled Iron Pill, 410 auto-boost assertions,
and the dynamic preview/lifecycle/feedback groups before the route integration failure.
B's concurrent route changes now pass the earlier admission boundary and all 206
preset manual-route acquisition assertions. The remaining assertion is "routing reset
clears delegated manual ownership when farming returns": expected false, actual true.
The exact same delegated claim remains, native zone stays 0, and native route entries
increase from 97 to 98 (captures 393 and transfers 131 remain unchanged). Do not bypass
the route-ownership predicate or weaken the handoff assertion. B's files were not edited.

### Bugs Fixed

- Reservation release was recalculated but clipped a real 1,037 native Magic pool
  to its 1,000 cap. Native observations now distinguish current unchanged-equipment
  pools from prospective swap budgets. The original zero-idle/1,037-funded native
  assertions remain unchanged and pass. Projection arithmetic/cap observations are
  not falsified; alternative equipment remains capacity bounded, including empty slots.
- An active-ITOPOD A -> temporary support -> CURRENT B test found refresh reapplying
  temporary support from receipt.Execution AFTER correctly applying B on release.
  Ordinary replay now uses independently retained BaselineSupport, never the covered
  temporary support. The native known-route and unknown-route cases pass.
- No-baseline idle reservations no longer clear the whole lane: they release only
  the deficit from CURRENT amounts, preserving other actual work and both unowned
  Energy current/idle. Positive temporary policies still mean explicit whole-lane
  control; a no-successor exit releases only a still-owned policy and caches its
  typed verified release outcome so repeated End cannot repeat accounting.
- Foreign material cannot become CurrentGearContinuation or dependent credit merely
  because resource observation succeeded. Gear/support foreign evidence is scoped,
  survives ordinary invalidation, and deduplicates identical observations without
  renewing a review version each service tick. A changed native observation revokes
  the old review. Known shared transitions have bounded receipt observations, not
  historical return targets, so legitimate temporary gear is not mislabeled foreign.
- Native AT configuration conflicts no longer masquerade as uncertain resource
  accounting in BOTH lanes. They yield Energy/credit through native-setting evidence
  while healthy Magic can reconcile. Callback/render outcomes retain committed model
  accounting; no original-target restore ladder is inserted while the current
  mandatory goal still needs its prerequisites.
- Resource presentation rechecks authority, model identities, allocations and idle
  at each per-target native callback boundary, not just after the entire renderer.
- Preset candidates now declare ControlsEnergy/ControlsMagic from resolved production
  objectives, independently of allocation amounts. Application, observation and gear
  recall respect those flags. Energy-only preserves manual Magic, Magic-only preserves
  manual Energy, and capacity-only performs a real gear swap without recalling either
  lane. Explicit controlled-empty still clears its own lane. The native regression
  failed before the fix: an Energy-only preset replaced manual Magic 237 with 1,000.
- Temporary blood demand may explicitly claim otherwise uncontrolled Magic. Its
  receipt retains bounded current model/whole-lane observations, never a manual return
  snapshot. Demand exit clears only that still-owned temporary policy, then relinquishes
  Magic; later manual work remains untouched. A foreign allocation yields Magic only,
  performs no release writes, and does not overwrite stronger existing lane uncertainty.
  The added exit regression first caught 101 Magic left in a ritual after demand ended.
  These preset/no-goal cases add 50 native assertions to the previous 100 core assertions.

### Persistence Seams Connected

Reviewed _globalPresetApplyingPolicy now wins even when an application receipt
exists. Replay consumes its ORIGINAL Preset, resolved ExecutionPreset, Stage,
Horizon, Plan/normalization and acquisition, without reading a replacement library
definition or calling CreateGlobalPresetPolicy. Ordinary same-acquisition application
invalidation also reuses its decision; an approved owned material event is a separate
coalesced AutoUpdate acquisition event. Acquired legacy AUG/OS decisions are retained
when that authorized gear reoptimization occurs.

Focus and manual gear persist exact selections before native setup mutation.
Focus additionally persists acquired AUG/ritual/OS/AT/ratio commitments before dependent
resource funding, and validates approved selected material before writing it. Stop
and explicit supersession call D's durable ClearPersistedExecutionSelection hook.

Preset and focus causal material revisions call the existing persistence hooks.
On storage failure the SAME approved revision remains pending, dependent validity
and credit are withheld, and RunCustomAutoBoost retries storage BEFORE invoking any
new inventory effect. A native injected bad-directory case proves one effect,
repeated failed saves without another effect, storage recovery without another effect,
unchanged acquisition/entry, then successful current-policy verification.
The test also models C approving an exact target's material before the outer observer
returns: the outer observer captures approved-before values independently, so that
in-memory update cannot hide the durable-write obligation.

An additional native same-ID takeover test now passes: an explicit dashboard
Preview+Apply may change the recipe cache, but the paused stage retains its own
original policy object/acquisition/definition/horizon/plan. Resume restores THAT
current normal intent without search or entry renewal and durably tombstones the
superseded dashboard intent. This also exposed/fixed missing durable policy storage
in the Preview+Apply path itself; persistence now precedes token consumption and
native mutation.

Retained preset selections are indexed by execution owner separately from the
per-ID recipe cache. C's Main partial can use RetainedExecutionPresetPolicies()
when protecting paused selections and ApproveExecutionPolicyMaterialRevision(policy)
after a verified exact target revision. policy.Owner supplies the durable owner;
unsupported different-paused-plan storage remains pending rather than falsely
durable. D's existing persistence hook needs its owner-aware extension for that case.

Stable additive hooks are documented in executor-foundation-20261022.md:

- CaptureExecutionBaselineRouteRetirement(claim): exact current regular/ITOPOD preset
  witness, including current gear/material/support/funding/settings/model identities;
  unknown route returns a rejecting predicate. C has begun using this hook in Main.ActionSetup.
- SetExecutionReturnBaseline(claim, baseline): only quick:F8 may submit an EXPLICIT
  acquired return policy. CURRENT normal baseline always wins. D has wired import use.
- ExecutionClaimReservesEquipment(item): protects active claim setup AND F8 explicit
  return selections. C's IsRetainedActionEquipment still needs this predicate included;
  its current direct scan of claim.Setup misses return selections.
- GetExecutionDomainReviewReason(owner): D may offer same-entry review for live native
  yield evidence without creating a global restoration latch or stopping healthy lanes.
  ReauthorizeExecutionDomains is the scoped reviewed boundary; do not clear irreversible
  action uncertainty as part of setup review.
- Normal-plan resume safely retires a still-owned applied dashboard route before
  withdrawing manual intent, using the same exact witness with endingAppliedDashboard.
  An unrelated active route claim keeps its prerequisites; unknown native routes reject.

### Fixture Migration

Native positive support primitives now admit/validate a REAL scoped grant. New
negative no-authority checks should use raw Invoke, not the positive fixture helper.
AT rendering instrumentation observes the actual per-target renderer. Budget funding
tests displace a real AT bar to reach that renderer, not an unrelated NGU renderer;
unchanged owned-boost maps must not manufacture writes merely to satisfy counters.
Readiness/preparation deadlines, committed accounting and foreign-field assertions
remain enforced. Tests use real explicit stage restart commands rather than reset
as a new-budget shortcut. Structurally different fixtures select an explicit new run.
The reviewed-executor foreign-callback fixture now displaces an actually funded AT
bar to reach its AT renderer, retaining all foreign-Magic/healthy-Energy assertions.
The farming-only dynamic fixture now asserts entire uncontrolled lane preservation;
mixed fixed-NGU goals still assert exact positive execution funding. Hand-built
positive allocation fixtures explicitly declare their controlled lanes.

Source equipment/beard checks now assert exact active sets/levels, retained selection,
independent minute events with no redundant model writes, hard prerequisite rejection,
foreign yield, epoch review, persistence-before-mutation and read-only scheduler queries.
The old uncalled manager-call-counter scenarios were removed instead of adding writers
back. The full source executable is green, not only its focused selector.

### Deletion And Remaining Gates

Removed uncalled BloodMagicOptimizer OptimizeAndAllocate overloads,
OptimizeAndAllocateTarget, OptimizeAndAllocateIdleMagic and AllocateResult. Pure
selection/cap mathematics remain. Removed BeardManager EnableFuManchu/RotateBeards/
EquipBeards/SetDiggerLevelOne mutation bodies; SelectBeards and availability policy
remain. DiggerManager.UpgradeCheapestDigger is now the requested thin adapter to
Main.reference?.PurchaseActionDiggers(ignoreSetting, reserveGold), not another purchase loop.

Last real compile callers blocking the remaining snapshot/legacy manager deletion
are Main.CombatRouting.TryGoldDropSwap (B), Extensions.RecapDiggers, and disabled
CustomAllocation.Equipment.ChangeGear/EquipDiggers (parent final unreachable-branch
cleanup). Re-run the caller audit after those edits, then delete the old snapshot,
restore, direct mutation and feature-lock families listed below; reflection in old
checks is not a reason to preserve them.

Acceptance is NOT complete: full Preset is blocked in B's integration, the remaining
full native/source gates must run after all streams merge, and C must protect explicit
F8 return items. The preset whole-lane empty-map and gear-recall gap now has explicit
control masks and native regression coverage, including temporary demand exit and
foreign release refusal. This is not a completed whole-application no-goal audit of
every other producer and lifecycle transition. Do not deploy based on focused passes alone.

Commands: `& A:\misc\NGU-Automation\temp\verify-executor-offline.ps1 -Suite PresetIntegration`
with selectors `--execution-core`, `--preset-final-review`, `--inline-stage-setup`,
`--reviewed-executor`, or NO selector for full Preset. Verification calls used timeout
1,200,000 ms. Source:
`dotnet run --project checks/SustainedFocus/SustainedFocusChecks.csproj -c Release`,
and the analogous Presets/ResourceWriter projects. No external include target is
needed now that the parent linked Main.Execution into the source manifest.

## Previous Continuation

Core implementation is connected, not another API-only foundation. Production and
compiled-native validation are deliberately PENDING the parent's combined build;
no production build, native fixture build, deployment, live access, commit, WIP edit,
or other stream's production file/manifest edit was performed in this continuation.
The earlier foundation MVID/pass totals do NOT validate these changes.

Implemented source checks: real production Main.Execution, CustomFocus,
CustomSetup/Beards, ResourceWriter, priority allocator and SupportProjection, with
explicit data-only native boundaries. No writable grant/readiness success stub.

| Check | Actual result |
| --- | --- |
| SustainedFocus `--execution-core` | PASS 19 assertions |
| SustainedFocus `--resource-compatibility` | PASS 297 focus-failure + 16 resource-caller assertions |
| Source ResourceWriter | PASS 1,553 assertions |
| `git diff --check` | PASS |
| Full source SustainedFocus | NOT GREEN; an earlier full attempt reaches the legacy EquipmentSetup assertions, which still equate old request priority/order and manager-call counts with exact applied support. No full-source pass claimed. |
| Production/native suites | NOT RUN in this parallel continuation, per parent-owned build policy |

The full source attempt passed ongoing ownership 133, Iron Pill allocation/cleanup
246, protected application 120 and blood-priority 109 before reaching obsolete
equipment-boundary assertions. That is scoped evidence, not a final full pass.

## Connected Behavior

- Preset replay uses actual equipped gear and exact composed support under any
  temporary setup claim, not only manual adventure. It keeps the independent
  acquired baseline support instead of pinning temporary support into B.
- Inline application stores the resolved effective setup, retaining B independently
  underneath an exact temporary claim. Focus reconciles the same composition and
  continues resource observations without replacing retained gear selections.
- Route activation yields to a Route claim. The baseline route leaf now requires a
  grant over route/settings/prerequisites and rechecks exact equipped material,
  support, funding, boss state, route, autoattack, lazy preference and tower input
  boundaries. Unknown activity still prevents withdrawing prerequisites.
- Whole-feature locks no longer authorize the shared selected actuator. Producers
  supply current identities; explicit claims supply admitted process identities.
  No Authorized predicate/claim means no selected mutation. Legacy locks remain
  only in still-unconverted producer admission/manager APIs, not as actuator rights.
- Baseline resource budgets subtract exact current idle reservations BEFORE strict
  priority/planner calculations. Temporary exact whole-lane policies replace only
  declared lanes. Missing authorized temporary targets are explicit zero; basic
  training is forbidden in that policy seam. No old allocation is a return target.
- Unmanaged lanes are filtered before actuation and verification. Yielded lanes
  retain the resource executor's evidence; other lanes continue. Invalid temporary
  lane authority leaves that lane uncontrolled without rejecting unrelated lanes.
- Baseline credit/readiness additionally verifies its own requested resource map,
  not merely a successful composed temporary allocation. Mandatory AT still uses
  actual native levels and original acquired targets, not a new timer or entry.
- Gear/support/material observations are cheap equipped/support observations and
  are checked across resource callbacks. No inventory-wide search was added to a
  recurring allocation pass. Dirty resource/setup events bypass resource cadence,
  not selection policy, and do not renew acquired budgets/deadlines.
- EndExecutionClaim resolves the CURRENT retained successor using a preserving
  gear transition. Controlled pinned/current baseline support is explicit through
  ControlsSupport; observational keep-only support does not become an exit snapshot.
  No authorized successor means preserve native state, not clear/fallback NGU.

## Support And Settings

`ApplyCustomEquipmentSupport` and `ApplyCustomBeards` no longer invoke independent
native beard/digger manager workflows. They retain selected support/acquisition,
pending attempts, exact bounded observations and independent minute events, and
submit the existing exact selected actuator. Application invalidation no longer
discards those acquired support choices. Foreign native support is yielded; epoch
changes require review rather than implicit new authority from matching values.

`RequiredBeards` is explicit on selected requirements. Beard selection accepts an
optional hard-required set, places it before optional policy/core utility, and
rejects unavailable/over-cap hard prerequisites before mutation. Optional utility
retains existing policy and selection calculations. A pinned/current digger policy
is not silently resized by a beard-only operation.

Preset AT writes/returns use `ApplyExecutionSetting` with a current grant. Narrow
AT resource effects use the SAME ResourceSetupExecutor, including model-committed
render failures and per-boundary ownership checks. `ReleaseGlobalPresetNativeTargets`
is now a request, not a hidden native mutation from invalidation/observation.
The ordinary coordinator services it against current successor intent. A mandatory
current AT successor keeps its acquired gate instead of restoring then reapplying.
Quiescing service is explicit through `ServiceExecutionRetirement()`.

Native AT conflicts yield Energy/dependent credit, not healthy Magic. The old
PresetExecutionBlock type and its admission latch are removed. `_presetExecutionBlock`
temporarily remains ONLY a last-result diagnostic for the lifecycle stream's
existing clearing assignment; actual authority resides in native domain evidence.
Removing that clearing assignment lets the parent rename/remove the projection.

## Parallel Interfaces

Stable Begin/Apply/EndExecutionClaim signatures are unchanged. Full semantics and
all additive APIs were published in `executor-foundation-20261022.md` as implemented.

```csharp
void SetExecutionResourceReservation(ExecutionClaim claim, long energy, long magic, long res3 = 0);
void SetExecutionResourcePolicy(ExecutionClaim claim, ResourceSetupIntent policy);
ResourceMutationResult ReconcileExecutionResourceClaim(ExecutionClaim claim);
bool ExecutionResourceReservationReady(ExecutionClaim claim);
bool ExecutionOwnsDomain(ExecutionDomain domains);
bool ExecutionSetupVerified();
bool ExecutionBaselineResourceMapMatches(ResourceSetupIntent intent);
ResourceMutationResult ApplyExecutionSetting(ExecutionGrant grant, Action write, Func<bool> verify);
bool ReauthorizeExecutionDomains(ExecutionGrant grant, ExecutionDomain domains, Func<bool> reviewed);
ExecutionGrant ResolveExecutionRetirementGrant(ExecutionClaim claim, ExecutionDomain domains, Func<bool> stillOwned);
bool ServiceExecutionRetirement();
```

Finite actions must check typed application outcome plus actual reservation/setup
observations, then validate their action grant at every native boundary. A current
grant alone is NOT credit or finite-action invocation proof. Reentrant reservation
service returns RejectedUnchanged and queues the request, never a stale success.
Action-specific uncertainty/durability remains the finite-action owner's work.

Retirement grants are restricted to an existing current claim's declared non-action
footprint and exact still-owned predicate; Running/Quiescing only. Lifecycle may
service existing preset AT returns through ServiceExecutionRetirement after entering
Quiescing. LoadingSave/Unloaded never gain ordinary/retirement write authority.
Reviewed reauthorization clears only named evidence, revokes the review grant and
does not fabricate selection continuity, action history or scheduler credit.

## Parent Integration

1. Add `../../NGUInjector/Main.Execution.cs` to the SustainedFocus source manifest.
   For isolated checking without editing the parent-owned manifest I used
   `-p:CustomAfterMicrosoftCommonTargets=C:\Users\Kevin\AppData\Local\Temp\opencode\ngu-core-source-authority.targets`.
   That external target includes only the real production authority partial.
2. Add `ExecutionCoreCutoverChecks.cs` to PresetIntegration and linked partial-fixture
   consumers CombatPrepIntegration/ManualAdventureOverlay. Hook
   `CheckExecutionCoreCutover()` into the full Preset runner and optionally a focused
   selector. No shared native Program.cs or manifest was edited here.
3. New native cases cover actual inline A -> temporary gear/support -> real mandatory
   AT B -> direct B release, no covered-B flash/search, entry/deadline/history/basic/R3
   preservation; and acquired AutoUpdate-false preset reservation/current pool growth
   and release. These cases are WRITTEN, not compiled or passed yet.
4. Migrate legacy source equipment/beard assertions to exact selected support and
   model-write semantics. Do not restore removed native manager calls to satisfy
   request-call counters. Exact native tests directly invoking support/selected
   mutators without a grant should now reject; admit a real owner/grant for positive
   actuator tests. Update synchronous target-release checks to service the requested
   coordinator transition before asserting native fields.
5. Run the combined production and native gates, including callback/failure/foreign,
   claim removal with smaller-cap successor, support overspend/pinned modes and
   lifecycle/review timelines. A preserving successor rejects unsafe capacity loss;
   never replace that rejection with a whole-lane historical recall/restore.

## Final Deletion List

Kept these until the parent's last caller audit, rather than breaking parallel code:

- LoadoutManager: GearRestoreLease and saved/temp fields/tokens/epochs;
  SaveCurrentLoadout, SaveTempLoadout, RestoreTempLoadout, RestoreGear,
  DiscardSavedLoadout, DiscardStaleSnapshots, TryTitanSwap, TryYggdrasilSwap,
  TryMoneyPitSwap, TryGoldDropSwap, ChangeGear, ChangeGearObserved,
  RemoveResourcesForGearSwap, CurrentLock/CanSwap/AcquireLock/ReleaseLock and their
  private acquisition/item-location helpers. Preserve SnapshotEpoch, exact capture,
  compose, validation, observation and ApplyExactLoadoutObserved as native primitives.
- DiggerManager: saved/temp snapshots/tokens, DiscardSavedDiggers, DiscardSnapshots,
  SaveDiggers, SaveTempDiggers, RestoreTempDiggers, RestoreDiggers,
  RestoreDiggersOwned, RestoreSnapshot, TryTitanSwap, TryYggSwap, EquipDiggers,
  ClearDiggers, EquipWeightedDiggers, RecapDiggers, CurrentLock/CanSwap/ReleaseLock
  and private restore/lock/set-level workflow helpers. Keep request/calculation types;
  purchase leaves belong to the finite stream and are NOT collateral deletion.
- BeardManager: EnableFuManchu, RotateBeards, EquipBeards, SetDiggerLevelOne old
  mutation bodies once their remaining internal/dead callers are removed. Keep
  SelectBeards, availability/unlock policy and the exact Main.Support actuator.
- Last text audit still found legacy calls in Extensions (RecapDiggers),
  Main.CombatRouting (TryGoldDropSwap) and disabled CustomAllocation.Equipment
  (ChangeGear/EquipDiggers). Other streams may remove these before final integration;
  re-run the audit rather than assuming the inventory is a fixed deletion license.
- Do not delete narrow native-setting/draft returns, bounded partial evidence,
  resource conflicts, selected material descriptors, AT/W/OS history or scheduler
  commitments merely because old snapshot APIs disappear.

No disabled legacy allocation path was enabled. Full release and live soak remain
parent-owned; there is no claim that current combined source is release-validated.
