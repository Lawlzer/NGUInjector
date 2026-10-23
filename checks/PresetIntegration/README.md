# Preset Integration Checks

These checks exercise the compiled production preset adapters and an in-memory copy of the installed game assembly. They never inject, attach to, launch or mutate a running game, installed DLL or save.

```powershell
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$game = "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
& $msbuild "NGUInjector.sln" /restore /t:Rebuild /p:Configuration=Release "/p:GameFolder=$game" /v:minimal /nologo
dotnet run --project checks/PresetIntegration/NumericIntegrationChecks.csproj -c Release
& $msbuild checks/PresetIntegration/PresetIntegrationChecks.csproj /restore /t:Build /p:Configuration=Release /v:minimal /nologo
& checks/PresetIntegration/bin/PresetIntegrationChecks.exe NGUInjector/bin/Release/NGUInjector.dll "$game\NGUIdle_Data\Managed"
```

Build the production backend first using its normal project manifest, without temporary build imports. Missing shared source entries or the embedded activity policy are production integration blockers, not reasons to run these checks against a stale DLL.

The fixture links the established Combat Prep integration object's construction helpers rather than duplicating global stubs. Mono.Cecil replaces scene/UI rendering, InputField text storage, recalls, slot-count inputs and selected unrelated recurring input getters in memory. Native beard temporary/permanent/effective getters (including W and DOUBLE Stat), digger effect getters (including W and cubic DOUBLE Stat), purchased-level bonus, Gold income, beard activation/deactivation, digger activation/drain/clear/refresh, native exact-input parsing/bounds, ritual `addMagic(long)`, inventory formulas and production preset capture/planner/preflight/application are not replaced. `GoldDiggerUIController.updateUI` executes its original menu/ID guard and InputField level-assignment IL slice while unrelated rendering is omitted. Controlled fault callbacks execute at the isolated UI boundary. Native clear/refresh are NOT no-ops: menu-44 controls prove that their text overwrite runs on every pod. No full native rendering coverage is claimed. Changed selection comparisons declare the shared relative `1e-6` tolerance for desktop CLR Single-return precision differences; unchanged activation retains its exact captured native getter. The native NGU Single return is materialized before current-state parity checking rather than comparing a rounded Single projection against a desktop CLR evaluation-stack artifact.

The preset runner also links `NativeProgressionIntegration.cs` because the shared Combat Prep fixture's entry point references it. Those unrelated checks are not invoked by this runner. ITOPOD capture checks retain native `progressGained` and `totalPPBonus` with isolated unrelated NGU/hack PP input getters; the production capture and planner are not patched. The fixture uses neutral adventure-beard activation for full-capture parity, leaving the production exact current-getter parity guard intact.

Coverage:

Preparation ingress/entry regressions (written, not run in this change): `--preparation-ingress-entry`
also runs under `--finite-action-cutover`. Actual WinForms click and probe handlers acquire
explicit request identities while raw preparation methods still reject missing IDs. Nested
external identities, repeated/completed opportunities, exception-scope restoration and
quiesced admission remain guarded. Standard/automatic entry cases exercise real selection
and checkpoint faults, native capacity rejection and recovery of the same selected setup.
Original identity/start/deadline/fight receipt persist; started, completed, reloaded,
stale-entry, changed-epoch and uncertain records cannot gain another entry fight.

Iteration 13 evidence capture (UNRUN): initial scheduled failure now includes the
actual caught exception/stack from a private diagnostic field, never UI status.
`--preset-final-review` directly exports a real dashboard-acquired policy under
initialized scheduler provenance before scheduled acquisition tests, printing null
optional-string/reference and support/activity-array evidence. Its failure retains
the original exporter/serializer stack. No speculative exporter fix is included:
this SimpleJSON implementation wraps null strings in `JSONString`, and `Escape`
dereferences their length; confirm the actual caught path before changing encoding.
The fixed-time checkpoint fixture explicitly owns no mandatory AT target and sets
only its initially unowned native menu targets to zero, matching manual budget
setup. Mandatory-AT cases, production gates, quarantine and real filesystem-failure
checks remain intact. Iteration-12 builds passed independently but all scheduled
acquisitions failed; these additions have no executable result yet.

Iteration 12 regression isolation/diagnostics (UNRUN): fresh resource-coordination
setups now use unique fixture-owned checkpoint directories rather than restoring
another case's `fixture` checkpoint from shared Temp. Deliberate reloads retain the
same directory and still exercise real write/read/rewrite and quarantine. Initial
scheduled acquisition must produce both a receipt and its matching on-disk selected
acquisition. Failure reports the first waiting/persistence/recovery/quarantine and
execution reason, checkpoint path/JSON and scheduler state before the AutoUpdate-off
replay can mask it with `No reusable policy`. Atomic persistence remains real; a
failed write reports its exception type/message and still refuses application.
The first iteration-11 acquisition failure is not yet proven resolved; rerun the
final-review, commit-budget and resource-coordination selectors before acceptance.

Iteration 11 capture/quarantine regression (written, UNRUN): `--preset-final-review`
adds actual production atomic checkpoint write/read/rewrite cases for protected
fixed-time and mandatory-AT stages in both AutoUpdate modes. Assertions cover
detached original/resolved policy data, acquired AUG/OS/route witnesses, actual
checkpoint equipment material (unique, missing, changed and duplicate), typed
version/entry/save provenance negatives, legacy missing policy, real persistence
failure before selected setup application, original timing/credit/fight identity,
native W preservation and zero replacement search/capture/swap/transfer after
unproven reload. This deliberately tests scoped quarantine, not successful policy
restoration: clock/material equality is not a proven durable save identity. See
`docs/DESIRED_STATE_EXECUTOR_PLAN.md` and root WIP for the outstanding rebind/import
and lifecycle requirements. Existing iteration-10 independent results below do not
validate iteration-11 changes.

Iteration 10 authorized scoring regression (written, execution pending): `--preset-final-review` retains the four isolated regular/ITOPOD × AutoUpdate cases and adds `original mandatory-AT zero-reference mix/AutoUpdate={False,True}` with original weights `[1,1,0,1000,1,1,0,0,0,1,1,0,0,0]`, unrestricted Zone=-1, real native PP economics and unfinished mandatory AT. It requires a measured positive original-policy Energy-NGU reference despite zero feasible specialist NGU, finite non-distorted score, ITOPOD selection, exact native funding/conservation, mandatory target/completion gates, frozen references through Apply/replay/refresh and all existing route-loss/safety/no-search checks. Production scoring now preserves positive specialist references, fills missing references only from bounded feasible original-policy seeds before mixed search, and bounds late unreferenced positive utility to its own weight. This supersedes the historical specialist-only/epsilon contract below; no new runtime pass is claimed. Diagnostics remain gated by `NGU_ROUTE_DIAGNOSTICS=1`.

Iteration 9 fixture isolation (execution pending): the specialist-only frozen-reference contract and numeric regressions explicitly retain the epsilon floor for zero references. No production scoring semantics changed. ITOPOD excludes Energy NGU and regular-zone utility, whose feasible mandatory-AT reference was proven zero; it retains positive Magic/PP/boost/AT/adventure weights and asserts finite positive references for active objectives. Both mandatory AT allocations/targets and the unfinished completion gate remain checked. ITOPOD damages and exactly repairs native AT funding; regular farming retains native NGU repair. Both check exact AT receipt accounting and Energy conservation. Route-loss/parity/no-search and descriptor checks remain. Verbose diagnostics now require `$env:NGU_ROUTE_DIAGNOSTICS = '1'`; route assertion failures always include compact acquired metrics, score, normalization and reference keys. The real production zero-reference scoring distortion remains unresolved in WIP, not fixed by this isolation. This supersedes the iteration-8 unconditional diagnostic output and iteration-2 ITOPOD Energy-NGU repair descriptions below.

Iteration 8 diagnostic-only change (execution pending): `--preset-final-review` prints bounded `ROUTE-DIAG BEGIN/END` blocks for all four acquired-route cases before the route-kind assertion. Output includes acquired execution weights/Zone, retained Plan references/normalization and candidates/specialists (metrics, contributions, feasibility, support budget/drain/remaining), a clearly labeled fresh post-commit control snapshot (eligibility/reasons, PP/boost/HP/defense rows, up to eight regular alternatives), and native floor-1 progress/PP total. Arrays are capped at 64 entries and candidate groups at 16. No candidate is forced and all existing assertions remain. Iteration 7 independently disproved the explicit-Zone hypothesis: both ITOPOD cases still select zone 0 with numeric Zone=-1; regular cases pass. The dynamic suite passed 361 assertions against production MVID `7975ec70-431e-4001-bb27-717d2066c4df`. Those are preceding results, not validation of this diagnostic addition.

Iteration 6 native-index repair (execution pending): read-only native IL confirmed direct perk index 107 and transitive perkEffect indices 229/230, exceeding the acquired-route fixture's inherited 100 perk levels. That fixture now uses the established 300 zero-level rows. Structural assertions cover itemMaxxed through 39, perk level/effect/difficulty rows through 230 and quirk level/effect/difficulty rows through 73, retaining zero levels and native boost/recycling getters. Descriptor, native-target/rotation and nonfarming cases passed in the preceding orchestrator run; all four acquired-route cases still require rerunning with this repair. No production gate or assertion was weakened.

Iteration 4 fixture repair (execution pending): this checkout's `SimpleJSON.JSONNode.Parse` does not materialize bare scalar roots. New descriptor mutations/clones and the regular-route Zone now parse a container and extract its scalar, preserving number/string types rather than accidentally testing null rejection. The farming observation fixture initializes the native recycling/boost inputs used by `SetupAdventureDynamic`, retaining real getters and all safety/accounting assertions. Acquisition failures include exact waiting/failure/block fields and runtime status. The prior iteration's final-review run failed six cases; these repairs are not a passing result.

- Versioned equipment identity: `--preset-final-review` covers the isolated acquisition-bound equipment codec in both AutoUpdate modes. JSON round-trip and moved unique exact-material matches resolve; missing/changed/duplicate/aliased identity, unknown/coerced versions, unknown material schema and target-slot remapping reject. Controlled empty stays empty even over occupied native slots. Export ignores unexplained native material changes. Checks assert zero searches, broad captures, transfers or swaps and unchanged receipt/history/scheduler state. Checkpoint persistence and policy restoration are not wired by this slice. Iteration 3 checks are written, not executed.

- Bounded farming observation: `--preset-final-review` adds regular-zone and ITOPOD acquisitions in both AutoUpdate modes, with mixed PP/boost/zone/resource weights. Broad-versus-bounded replay compares scores, metrics, hits, native floor data and funding. Native NGU damage is repaired exactly; unchanged refresh performs no transfer. Missing recommendations, lost repeatable floors, changed native Lazy floor, Beast Mode and failed live boost-economics capture reject before funding. Planning/route-table/floor-forecast/search counters remain unchanged. `--adventure-dynamic` additionally checks stable resolved farming intent in both AutoUpdate modes while retaining existing changed-task acquisition tests. New checks are written, not yet executed in iteration 2.

- Equipped-only execution observation: `--preset-final-review` includes both AutoUpdate modes with an unlocked ITOPOD but no farming objective. A broad-capture control builds real floor rows; execution observation retains native pools/powers/caps while producing no inventory alternatives, zone rows, floor rows or material fingerprint. Changed native NGU funding is repaired exactly, unchanged refresh writes nothing, acquisition/route identity remains, and planning-capture/search counters stay unchanged. The fixture observes both full and execution capture entry points so existing deadline/failure/count checks still exercise the actual refresh path. These are written checks, not a report that this checkout has passed them.

- Adventure Goal preference: `--adventure-goal-preference` checks that Equip, Preview + Apply, allocation refresh and stopping either preset preserve both enabled and explicitly disabled Adventure Goal settings.
- Commit deadlines: `--preset-commit-budget` exercises 21 cases with the actual operation stopwatch forced to 4,000 ms at preparation and native application boundaries, without sleeps or replacing deadline checks. Prepared Equip/Apply and real recall/reallocation refresh finish verified funding despite elapsed preparation time. Preparation expiry still waits with backoff and no native mutation; arbitrary render exceptions still latch; consumed manual tokens cannot replay. See [the fix and live evidence](../../docs/preset-commit-budget.md).
- Capacity objectives: `--capacity-objectives` checks compiled energy/magic total-capacity capture, preview and replay against independent native getters, high-tier cap gear over opposite-cap/adventure alternatives, near-empty pools, full-horizon swap overhead, unrelated level/NGU/adventure locks, finite-target auto-advance, and the exact base-magic-cap unlock boundary. These preview/replay checks perform no recalls, native gear swaps or live game actions.
- Capacity policy lifecycle: the same selector also runs both cap objectives through committed Equip and Preview + Apply with AutoUpdate enabled and current support. It verifies actual isolated inventory swaps, exact cap-item ownership, native getter parity, search-free refresh, owned boost invalidation, one reoptimization that equips a newly stronger cap challenger, and stable subsequent refresh. Only the existing isolated inventory/boost boundaries are used; there are no live game actions.
- Global NGU weights: append `--global-ngu-weights` for compiled capture authority, deprecated inline argument rejection, positive-ratio preview invalidation before mutation, cache eviction, stale replay rejection, immutable applied observations, temporary-owner cleanup deferral, dashboard recall/re-equip, scheduled AutoUpdate reacquisition, non-updating stage credit/ownership protection, and legacy breakpoint global selection/exclusions. See [runtime lifecycle](../../docs/global-ngu-weights.md). These checks do not cover live Unity scheduling.
- Global execution: committed global IDs/library revisions, default Auto-update searches on Equip, cached-policy replay when off, allocation refresh without search, saved-versus-acquired metadata, explicit Preview + Apply seeding, Clear quick ownership, and scheduled execution without self-pausing the plan. Empty stage bindings retain inline semantics; `@active` resolves the plan selection. These fixture assertions are not a live-validation report.
- Scheduled Iron Pill: `--iron-pill` checks actual weighted-stage quick-refresh blood allocation with zero saved Blood weight, repeated reservation, unchanged policy/entry/history and restoration to the acquired magic split without search. `--preset-boost` checks own boosts, waiting/displaced allocation receipts, exceptions, foreign material rejection, AutoUpdate re-equip, cached revaluation and pending scheduled restoration. See [the separate live verification](../../docs/IRON_PILL_WEIGHTED_STAGE_FIX.md).
- R06/R10: actual Preview/Apply removes Gold, rejects the now-unaffordable higher-ranked digger and successfully activates an affordable lower-ranked digger at the planned level. Candidate budgeting uses reduced GPS before support selection, and the response uses fresh actual metrics with original baseline/normalization.
- R10: actual potential/permanent beard getter capture, active/inactive/disabled effects, current resource/adventure/NGU/drop parity, and immediate candidate Gold/drop changes at fixed levels.
- R11: successful and partially failing direct beard changes invalidate cached requirements and observed receipts in `finally`, leaving the minute clock untouched.
- R19: compiled preset preflight refuses final capacity below retained training and a legal-final accessory move whose staging temporarily drops below retained training. Both failures preserve actual gear and recall/swap counters.
- R20: native finite target capture fails closed under auto-advance. A native flag edit between actual Preview and Apply invalidates material identity without consuming the preview, recalling resources or changing the user's preference. Unlimited targets remain supported.
- R21: native `addMagic(long)` controls versus the production exact transfer around `2^53`, including odd values, exact idle/NGU remainders, paid/unpaid progress, unchanged startup gold and overflow-before-write behavior.
- R32: fresh feasibility/observed-result replay retains the original preview baseline and normalization rather than rebasing the response after natural progress.
- Mixed-focus API: retained production, explicit percentages, weighted contributions and source keys stay tied to original best-found references across passive progress and observed apply. Zero/unavailable references report null ratios. Applied responses preserve reference metadata without reintroducing specialist rows. Advisory sequence apply is rejected before recalls or token consumption.
- ITOPOD capture: actual native per-floor progress, highest-reached-minus-one bounds, numeric enemy HP/defense capture, material fingerprint participation, and read-only recall/swap counters. Multihit timing remains a declared numeric proxy; this suite does not claim full native fight simulation or live routing coverage.
- Lazy ITOPOD: a native-data PP/enemy fixture with respawn-dominated timing prefers a multihit floor different from `Character.calculateBestItopodLevel`. Purchased/enabled capture constrains the actual preview to the native-selected floor; either flag's change invalidates material and actual Apply before mutation. Passive power changes reject incompatible exact replay without replacing the policy. Native getter comparisons cover low-power and maximum-floor clamps. The defensive route guard preserves floor-input drafts on rejection. Negative controls execute native `enemyDeath` and prove that an enabled purchased shifter really replaces the old multihit floor after one kill; a compatible preview floor survives eleven repeated native kills without disabling the preference or adding an owner.
- Shared support: native W/Stat getters, exact native levels greater than one and beyond `2^53`, purchased maximum/preflight, pod ID/text restoration on success/throw, free diggers at zero GPS, exact empty selections, duplicate/disabled/flag validation, late GPS loss before funding, partial native throw journaling, and refusal to adopt foreign selections/flags/inactive levels/list references/epochs or superseded receipts.
- Explicit inactive cleanup inputs: empty-selection native zero restoration, nonselected nonzero inputs at zero GPS without drain or slot consumption, zero at purchased maximum zero, selected-ID conflicts, duplicate/negative/out-of-range/over-max rejection before controller writes, availability/pod checks even with empty selection, shared partial-failure journal and pod restoration, preservation of unrelated foreign levels, and combined original-selection plus explicit inactive restoration through one setter/receipt.
- Clear postcondition: exact clear/activate-11/throw negative control, flag-only interference, a real native clear/refresh foreign activation callback, and a positive empty-clear/refresh-throw control that remains owned.
- Pod drafts: visible menu 44 executes the native initial/final text overwrite on borrowed and nonborrowed pods; pre-clear drafts/IDs survive normal and partially failing exact application. Foreign drafts during `setID`, input assignment, submission refresh and nonborrowed refresh are preserved, not overwritten or submitted. Replaced inputs/epochs are never restored into; secondary restoration errors do not mask primary failures or stop restoration of remaining owned pods.

`NumericIntegrationChecks.csproj` links all preset numeric/oracle sources and the real pure resource/digger helpers, with a separate intermediate directory. `EquipmentChecks.csproj` remains a separate data-only native Inventory swap check. There is no live-game or Unity-rendering coverage claim.

## Coordinator Integration

The source-linked preset manifests compile `ResourceProjection.cs`, `ResourceAllocation.cs`, `DiggerAllocation.cs`, `ActivityPolicy.cs`, `SimpleJson.cs`, and `JsonFiles.cs`, and embed the actual registry as `NGUInjector.ActivityPolicies.json`. The normal production manifest includes these helpers plus game-dependent `ResourceProjectionCapture.cs`. Both paths use `ResourceProjection.NormalizeRaw`; no production normalization or manifest handoff remains pending. Compiled-adapter checks require a successful fresh production build.

See [Automation Improvements](../../docs/AUTOMATION_IMPROVEMENTS.md) for the shared numeric/ownership boundaries and [completed verification](../../docs/AUTOMATION_IMPROVEMENTS.md#completed-verification) for recorded full-run results. Remaining work is in [temp.md](../../../temp.md). This documentation pass did not run a build, this fixture, numeric checks or live actions. Coverage described from current source is not evidence that the current checkout has passed; recorded results below retain their historical scope.

## Shared Support Integration

The exact stable models/APIs and partial-receipt semantics are documented in [support-core.md](../../docs/support-core.md). Pure support checks run with `dotnet run --project checks/SharedSupport/SharedSupportChecks.csproj -c Release`. Both private exact-support entry points accept optional `SupportDiggerLevel[] inactiveLevels = null`; existing three-argument preset calls retain their behavior. Historical assertion totals below are reports from their respective runs, not fixed current suite sizes.

The production manifest includes both support sources. Run a normal production `Rebuild`, not an imported/incremental build, before executing compiled adapter checks. No compile-import handoff remains pending.

## Historical Offline Reports

These reports do not establish live correctness of current global library migration, Equip/cache ownership, all current objectives or stage inheritance. The native-shape/menu-44 tests cover inspected native paths, not full rendering or all game versions. Current phase-support/Quick live evidence belongs to the separately owned [Phase Support Live Verification](../../docs/PHASE_SUPPORT_LIVE_VERIFICATION.md), not this fixture.

Earlier task-owner-reported offline verification: normal production Release `Rebuild` passed without temporary targets; this native adapter suite passed 315 assertions; the integrated Combat Prep suite passed 4,668 assertions; shared support passed 1,222 assertions. Both preset numeric runners' separately recorded result was 1,495 assertions each. This documentation pass did not rerun them. The native-shape/menu-44 text-path tests cover the inspected menu/ID guard, visible text overwrite, pod drafts and receipt `Completed` semantics, not full native rendering or all game versions.

Mixed-focus follow-up, 2026-10-17: fresh normal production Release `Rebuild` passed; this runner passed 351 assertions; both preset numeric runners passed 1,554 assertions. No live operations or restarts were performed. See [mixed-focus search and API details](../Presets/README.md#mixed-focus-search) for retained-production fields, support constraints, practical timing and remaining native-data approximations. Combat Prep and shared-support totals above belong to the earlier verification, not this follow-up.

The Lazy ITOPOD fixture runs the complete native kill method, `activateLazyItopod`, `setOptimalFloor`, floor validation and native optimum getter. Only kill rendering writes, loot drops and log rendering are omitted; the zone-entry helper executes the original ITOPOD level/count and saved/controller-zone write slices, excluding Unity rendering/Random state and unrelated scene reset work. Native PP progress on every kill is checked. This is an isolated native control-flow/data test, not full Unity rendering, live farming, or a combat simulation.

Lazy ITOPOD follow-up verification: normal production Release `Rebuild` passed, both numeric runners passed 1,561 assertions each, and this native adapter runner passed 427 assertions. The prior 351-assertion native suite remains covered. No installed DLL, save, running game or UI was modified by these checks; no restarts or live operations were performed.

## Adventure Float Checks

`CheckAdventureFloatParity` compares the built production capture/projection with the installed `Character.totalAdvAttack`, `totalAdvDefense`, inventory totals and cube getters. Native beard/digger arithmetic remains active. Test-only callbacks supply non-unit AT/NGU/challenge/perk/hack/wish/card inputs (card isolation is restricted to Adventure Stat); they do not replace the outer getter expressions, stores, clamps or cube/support formulas.

Coverage includes an exact native `2315575.25` reference near the reported failure, 33 adjacent input floats, 600 multi-slot setups tested for both power and toughness, exact owned gear-total comparisons, 30 full captures and read-only recall/swap counters. Native residuals are measured in ULPs, not hidden by a general tolerance. Eight-ULP, nonfinite and clamp-transition negative controls fail; an omitted NGU multiplier fails in every corpus row, and legacy early-rounding/cube mistakes also exceed the cap. All non-adventure capture comparisons remain exact. See [the precision policy and limits](../Presets/README.md#adventure-precision) for the seven-ULP acceptance cap, observed four/five-ULP residuals and the distinction from bit-identical emulation. No live-operation or Unity-rendering verification is claimed.

Historical recorded result (not rerun by this documentation pass): normal production Release `Rebuild` passed, both preset numeric runners passed 1,580 assertions each, and this runner passed 4,075 assertions. Adventure comparisons were 1,130 exact, 28 above and 42 below native; maximum difference was four binary32 ULPs. The complete owned gear-total comparisons stayed exact. No production tolerance was widened for resource/NGU/GPS/drop getters, and no shared support file was edited.
