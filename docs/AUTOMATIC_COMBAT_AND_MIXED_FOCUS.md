# Automatic Combat And Mixed Focus

Combat execution is updated for the October 22 shared-executor slice; the mixed-focus and live records retain their original build scope. This guide does not certify the game's present configuration. The [phase-support record](PHASE_SUPPORT_LIVE_VERIFICATION.md) and [October 16 Combat record](COMBAT_PREP_LIVE_VERIFICATION.md) remain historical evidence, not verification of this cutover.

**The combat slice passes offline native acceptance, not the full release/live gate.**
Current-resource handoff avoids empty/refill, paused still-owned borrowed fields
return independently, and verified retained focus admits public Preview/Start.
Cancellation/completion reconciles current retained focus without another search;
focus replacement/stop commands remain combat-idle only. Incomplete or foreign
focus rejects before Start consumes its token. [Exact evidence](combat-executor-verification-20261022.md).

## Automatic Stage

In a version-17 stage-scheduled plan, choose **Augments + Wandoos (automatic, includes AT)** and set its time budget. Saved stage `ExecutionMode: 1` selects `AutoAugmentsWandoos`; runtime stage telemetry calls it `executionMode`. Missing/zero means Standard. The automatic mode requires timed completion (`CompletionMode: 0`), not an ordinary AT-stat target gate. Fixed-duration or flexible-share configuration can supply its effective budget, which must not exceed 86400 seconds.

The automatic row uses **elapsed time, including setup**, not the earned applied-time contract of an ordinary fixed row. Its original entry fight, fruit activation, ownership waits, capture/search, gear/support setup, polling and training all spend the same budget. Entry setup cannot acquire a fresh full budget after waiting. The executor stops when either its original monotonic budget or original rebirth deadline expires; cleanup can remain pending afterward, but no extra training window is granted.

`Main.AutoCombatPrep.cs` uses the same pure `CombatPrepPlanner.Plan` as manual Combat Prep. Phase controllers retain choices/timing; current concrete setup and resource requirements enter shared `ApplySelectedSetup`/`ResourceSetupExecutor`, not a combat-local application/restore engine. Remaining-budget selection occurs at stage start, eligible completion, material native net-GPS events and causal owned-equipment events. Automatic execution consumes no manual preview token. Manual Preview/Start replays its cached exact policy without replacement search.

- A completed phase boundary requires its scheduled time AND both actual augment/upgrade or AT-3/AT-4 targets. A late gate consumes later budget, never extends the global deadline.
- A material GPS change is `abs(newNet - plannedNet) >= max(1, abs(plannedNet) * 0.1)`. Small cumulative drift is compared with the acquired phase's reference, not just the last poll.
- Recurring observation is limited to retained selected/equipped material and the acquired route. Owned inventory actions emit causal material events; there is no full-inventory fingerprint/search on resource refresh. Own phase swaps and cosmetic paths do not select again. Foreign maps/material are preserved and require reviewed shared authority. Losing an unselected historical reporting item does not create a restoration obligation.
- An acquired unfinished paid AT gate is never discarded by GPS/equipment replanning. Equipment growth refreshes native caps/power and observed resources without replacing targets or gear/support; `equipmentReplanPending` defers search until both targets are reached.
- Continuation preserves original start/deadlines, entry identity, acquired gates and reporting setup; reporting gear/support are not return targets. Runtime pools are latest observations bounded by native capacity and reserved basic training, not lifetime ceilings. Forecasts do not credit future generation. Completed preparation is not repeated. Paid AT remains forbidden after native/committed W history, and neither W lane is funded before unfinished later paid AT or during it.
- The once-per-rebirth OS pin and existing dumps/partials/allocations remain authoritative. Replanning does not reset W work or switch to a fresh OS. All used support selections are preflighted and each phase verifies exact legality/affordability before funding.

Normal allocation, support rotation and auto-rebirth yield while the runtime overlay owns the run. Each lane funds its acquired phase first, then weighted NGUs using Misc global weights, then eligible Time Machine work. TM is excluded while any current/future augment phase remains, since its native bars spend gold needed by the primary policy. Fallbacks never introduce WAN, AUG, paid AT, or ritual spending outside the acquired policy, and never acquire separate gear/support. Resources remain idle only when the permitted sinks cannot use them. Native allocation text is refreshed after direct funding, so recall's temporary zero cannot remain displayed. Quick remains an indefinite manual takeover, not another timed scheduler.

The desktop UI keeps scheduled timing in the Dashboard plan editor. The separate manual Combat Prep budget and preview/start tools live under Advanced, not Misc. Quick's `CustomAugmentsBloodEstimateSeconds` is labeled **Augment estimate (s)** and is not a stage timer or Blood duration. The supported **Activate fruits** switch is directly below the plan stages and saves `AutoActivateFruits` on the selected plan; it retains the existing at-least-one-hour plan/adventure eligibility rule. Dead legacy switches are no longer presented as active automation controls.

Max-growth Yggdrasil consumption runs automatically on the 10-second automation loop, including during Combat Prep and short plans. It checks every fruit page and follows each fruit's in-game Eat/Harvest selection; partially grown fruit is left alone. This does not depend on **Activate fruits** or the legacy `ManageYggdrasil` switch. Existing configured Yggdrasil loadout swaps and ownership guards still apply outside Combat Prep; a blocked required swap retries on the next pass. During Combat Prep, harvesting leaves its gear and diggers untouched. Activation retains its separate eligibility rules.

Editable **NGU weights** are directly visible at the top of **Misc**, without a collapsed legacy disclosure. All nine energy and seven magic inputs autosave to global settings; live allocation is shown separately below. These weights apply to every plan, preset and Quick action, with no per-plan or per-preset override. PAWGs means energy Augments, Wandoos, Gold and Power (indices 0, 1, 3 and 5). Selecting those at weight 1 and magic Yggdrasil alone at weight 1 is an intentional configuration, not a change to shipped defaults. See [migration and running-policy behavior](global-ngu-weights.md).

### Cleanup And Recovery

Termination retires a still-owned unsafe route first, returns individually still-owned targets/timer fields and releases authorized resources through the shared executor. Current successor setup applies directly. Without an explicit return baseline, native gear/support remain unchanged. Historical gear rollback, support restoration, touched-level unions and combat-local resource applications are deleted. Foreign setup evidence survives claim/application resets until freshly preflighted reviewed Equip/Preview+Apply. Native progression and user preferences are not rollback data.

Narrow live release retries use the half-second cadence without renewing phase time. The old four-attempt/two-second historical restoration ladder no longer exists. A still-owned active route cannot be abandoned while its prerequisites are withdrawn; actual unload is refused until observed retirement succeeds. Unknown/foreign routes retain prerequisites. Checkpoint-only `cleanupRecoveryRequired` is durably reconciled before current intent resumes; no synthetic override, new budget or offline credit is granted. Persistence failures retain an actionable reason and retry automatically.

An acquired unfinished paid-AT gate persists its exact targets and phase timing before native target changes or funding. Reinjection captures current equipment/support/resources and continues that gate under the original deadline. Native W work conflicts, legacy checkpoints missing gate data and invalid setup accounting use conservative NGU/TM background allocation until that deadline rather than buying new paid preparation or pretending unknown targets completed. Explicit user cancellation remains distinct from involuntary interruption. See [Automatic Reconciliation](automatic-reconciliation.md) for boundaries and verification.

Read-only diagnosis on October 20, 2026 at 20:41 local time found `24hr` enabled at
Augments + Wandoos with `active: false`, an empty run ID, and
`cleanupRecoveryRequired: true` in both live telemetry and its saved checkpoint.
Energy was 165331986 with 165331974 idle; all 8446570 Magic was idle. The original
rebirth deadline was 7590.700 seconds versus current elapsed 22397.008 seconds.
`CombatPrepActive` includes durable recovery, so resource reconciliation returned
`Waiting`/`combat-prep` before normal allocation and the scheduler could not advance.
Its empty allocation reason and stale Adventure status did not describe the blocker;
the actionable reason was in `planProgress.reason` and `cleanupRecoveryReason`.
The session started at 20:23 and restored saved progress. Available evidence did not
identify which earlier shutdown or cleanup event left the marker outstanding.
No recovery acknowledgement, stage move, settings change, or live training was performed during that diagnosis. The automatic reconciliation change below supersedes the diagnosed permanent-latch behavior.

Existing ordinary saved plans are **not batch-migrated** into automatic stages. Use the stage editor explicitly and inspect the resulting saved row. For this user's requested configuration, `24hr` was explicitly saved/read back with its three 900-second preparation rows merged into one 2700-second automatic row. Their `FightOnEntry` OR remains true; Time Machine, AT Adventure targets and NGU intent were preserved. Three mixed presets were added: Energy + Magic NGU, Energy NGU + ITOPOD PP, and Energy NGU + boosts. Before/after evidence: `combined-24hr-20261017-180531-{before,after}.json`. This is a backed-up individual edit, not a shipped migration.

The user's subsequent timing update sets **3600 seconds Time Machine, then 3600 seconds automatic Augments + Wandoos**, including its AT decisions. The 86400-second rebirth horizon, later AT Adventure/NGU rows and every other plan field remain unchanged. Save/readback evidence: `24hr-hourly-preparation-20261017-215438-{before,after}.json`. This configuration-only update preserved the user's active Manual Titans override; it did not restart the current rebirth or move its stage.

On October 18, 2026, the user authorized a one-time cleanup of the live `Desktop/NGUInjector/presets.json` library from 30 entries to six: Time Machine and Energy NGU imported from `24hr`, AT Adventure, and the three mixed NGU presets above. Retained definitions and all 15 migration receipts were preserved; receipts prevent deleted historical imports from reappearing. The saved plans were not edited, and `24hr` keeps its existing inline stage execution rather than being converted to weighted presets. This is local data cleanup, not a change to shipped preset defaults or migration behavior.

## Forward Forecast

The Combat engine advances native Wandoos bootup across every phase, including phases with no dumps. Capture supplies elapsed bootup, native duration and the completion-only set bonus. The base duration is 3600 seconds, or 3240 with the completed XL set, reduced by native evil level-100 challenge completions down to half. Speed ramps as elapsed/duration, then becomes 1 or `1.1f` with the completed Wandoos set. Switching OS preserves the clock. Capture reconstructs W-speed factors without AT or bootup in native order; it does not divide a zero fresh-rebirth speed to recover a coefficient. Installed/enabled Wandoos is required; future installation is not modeled.

Wish 190 is supported as **free AT progression**, not paid preparation. After the actual basic-training unlock, native AT advances one level per 50-Hz tick at zero allocation, even with disabled/reached menu targets. The engine advances unlocked AT lanes in every positive-duration phase and excludes paid AT phases/resource charges. W uses pre-tick AT levels conservatively; Unity callback ordering is not guaranteed. Pending banked-level transfer is rejected, and changed Wish activation invalidates capture semantics. Free AT does not impose an unnecessary magic-W target gate.

Allocations are held between 0.5-second refreshes; completions discard overflow and grant at most one level per tick. Bootup/free-AT variation is evaluated on the shared clock, with native-method offline checks, not a bit-identical Unity float/timer model. Gold-at-bar-start, per-profile net income, visited-loadout pool loss and actual target gates remain part of ordered replay.

Search is bounded, not globally optimal. Named families use coarse seeds, then refine six family winners per OS by moving internal boundaries at horizon `/16`, `/64`, `/256`, rounded to allocation cadence. Every trial resimulates actual phases/targets. At most 1440 complete policies are evaluated (1380 in the documented maximum paid-AT shape); up to 12 diverse alternatives are returned. See [planner checks](../checks/CombatPrep/README.md#search-and-execution).

### Opportunity Costs

The backend and comparison UI expose two distinct metrics:

| Metric | Meaning |
| --- | --- |
| `energyFocusEquivalentSeconds`, `magicFocusEquivalentSeconds` | Completed W dump gains divided by each lane's declared reference dump rate at final AT/end bootup, including tick rounding/caps. These measure output gained, not diverted allocation. |
| `energyResourceFocusEquivalentSeconds`, `magicResourceFocusEquivalentSeconds` | Each lane's prep resource-seconds divided by its full-focus reference allocation. `resourceReferenceProfileKey` identifies that reference; the corresponding `*ResourceFocusCostKnown` flag distinguishes unknown from zero. |

Keep Energy and Magic separate in comparisons; do not add them into one elapsed duration or value score. Missing/unknown data is unavailable, not a measured zero. The pure engine's aggregate resource-cost field is bookkeeping, not a universal NGU utility. No lost NGU levels, permanent bonus value or global A*W-versus-NGU tradeoff is invented.

## Mixed-Focus Presets

In **Presets**, create a named definition in the selected plan, set weights and horizon, Preview, then explicitly Apply the feasible candidate. Saving/browsing a definition does not activate it. Six goal weights are ordered `energyNgu, magicNgu, blood, pp, boosts, zone`. They are relative preferences normalized to sum to one, not independent resource reservations.

With positive measured references, the score is `sum(normalizedWeight[g] * production[g] / frozenDenominator[g])`: retained production, not improvement-only utility or multiplied equipment bonuses. Positive feasible specialist references are preserved. Missing/zero references can use positive feasible production from a bounded original-policy seed pass before mixed optimization: current and specialist gear, each with at most two compatible already-found support selections (at most `2 * (Keys.Length + 1)` evaluations). Hard gates, original fixed splits, route constraints and support permissions still apply. No final mixed winner is a reference. These best-found references are not guaranteed global maxima and remain frozen through fresh replay/Apply. Retained ratios may exceed 100%; zero/unavailable references still have null retained percentages. If a goal remains without a positive reference, known zero output earns zero and a later positive output earns one unit of scoring utility, not division by epsilon. Thus late discoveries cannot revive the zero-reference distortion. Positively weighted unavailable production still makes the score unavailable; valid zero completed levels is not an error.

Planned support is implemented as joint bounded production search, not just a priority-only choice. At most **64 legal deduplicated beard/digger policies** are generated. Full support passes alternate with gear coordinate search using a **top-three support beam**, and every trial rescores actual modeled NGU/blood production, magic split and the shared adventure route. Explicit beard priorities fix their filtered/capped selection; otherwise the ordered unlocked core prefix is mandatory and only optional slots vary. Explicit digger weights preserve eligibility/ranking/proportional allocation, with reduced total funding alternatives. Automatic portfolios compare seed, immediate-only, specialist/tilted funding and 100/50/25/0 percent use of the requested GPS budget. Gold activation precedes digger/blood affordability. Future support growth is not credited; current-support mode never substitutes selections.

Search uses a 6000-trial ceiling and cooperative 1.8-second search, 2.6-second sequence and 3-second capture/replay/apply clocks, not hard real-time guarantees. Final bounded baseline/seed comparisons retain compatible specialist gear even after search exhaustion, under the original mixed request and frozen references; they do not import a specialist-only magic split or incompatible support. This remains best-found heuristic search. See [mixed-focus checks](../checks/Presets/README.md#mixed-focus-search) for the extra bounded comparisons.

NGU production counts discrete completed levels over the horizon, ignoring initial partial bars and freezing bonus feedback. The bounded integrator certifies conservative completed levels within 0.5% of that model (small gains exact), or reports unavailable. Positive allocation can legitimately produce zero completed levels at 900 seconds and positive levels at 7200 seconds. These are not universal cross-NGU bonus utilities.

### Farming And Precision

ITOPOD uses captured native `progressGained(floor)` and repeatable floors through `min(maxItopodLevel, highestReached - 1)`. With native enemy data, comparison uses upper HP/defense rolls, native float `1.05f` scaling, conservative minimum `0.8` idle damage, half-defense subtraction and floored damage, allowing **at most four hits**. Without enemy data it falls back to the existing one-hit range, not invented multihit rates. PP units are **progress/s**, not spendable PP/hour. Farming shares one route; a regular zone cannot simultaneously earn ITOPOD PP.

When Lazy ITOPOD is both purchased and enabled, native kills reset its floor. Search therefore constrains each candidate to its native shifter-selected floor; incompatible pinned replay rejects before mutation rather than disabling the preference or installing a sustained route owner. The two flags are separately fingerprinted. Apply verifies native start/end inputs, but existing live RPC does not independently expose the current floor. Later stats can move an enabled shifter's floor normally.

These remain farming rate proxies: no player-health/death/regen/paralysis or frame-timer simulation, expected random-roll distribution, potion expiry or bonus feedback. PP digger rescaling approximates native factor-order/long rounding; regular-zone thresholds and Almanac boost/drop tables remain approximations. Beast Mode farming fails closed without toggling the preference.

Current preset capture uses `LoadoutPresetPlanner.AdventureParity` for **adventure power/toughness only**: at most seven binary32 ULPs, rejecting nonfinite values and discrepancies crossing native clamp endpoints. It preserves the native F-prefix/cube comparison and float-local stores; accepted nonzero differences are warned with both values. Resource/NGU/GPS/drop current-getter comparisons stay exact. Seven ULPs is a declared acceptance policy, not bit-exact normative behavior or a universal error guarantee. Native checks measured a four-ULP maximum in the 1200-comparison corpus (an earlier corpus reached five), and reject eight ULPs/omitted multipliers. See [precision policy](../checks/Presets/README.md#adventure-precision) and [native checks](../checks/PresetIntegration/README.md#adventure-float-checks).

### One-Shot Apply

The 120-second token binds exact gear, support, split, ritual, route, definition/NGU ratios and epoch. Complete preflight precedes token consumption, equipment-receipt invalidation and mutation. Fresh Apply replays that policy with fresh feasibility/progress and observed post-apply metrics, retaining original baseline/reference metadata; it does not search a replacement. After mutation, failure may be partial, not rollback.

Apply submits directly without a popup, preserves custom-plan/auto-rebirth/manual-adventure settings, clears Quick focus and idle boss push, allocates once and optionally routes once. Equipment receipts are invalidated without resetting stage identity/history, allowing the next plan pass to reconcile normally. It is **not sustained**: runtime is an inactive `applied-snapshot` requiring explicit reapply. The running plan, normal beard rotation and other owners may replace support/gear/allocations on their next pass. The two-phase sequence is advisory, not an executable Apply target. A later support difference does not negate verification of the immediate application.

## Recorded Live Evidence

All artifact paths below are machine-local under `C:\Users\Kevin\AppData\Local\Temp\opencode`, not repository fixtures. Earlier runs overlapped the user's manual game activity. The final controlled run below was repeated after the user explicitly handed over exclusive control; earlier observations must not be treated as isolated causal evidence.

### Mixed Preview/Apply

`verify-combined-20261017-222953-1250f64f/summary.json` reports `passed: true` for **all three** Preview/Apply cases. Each actually allocated **93155410 Energy** and **3042500 Magic** to NGUs. These are observed allocation counts, not measured production rates.

| Case | Forecast horizon | Recorded observed-Apply forecast |
| --- | ---: | --- |
| E + M NGU | 7200 s | E retained 100%, M retained 18.181818%; 104 E and 2 M completed levels projected; score 0.59090909 |
| E + PP | 900 s | 93.07063781842112 progress/s; ITOPOD zone 1000, applied floor 166 verified by native start/end inputs |
| E + boosts | 900 s | 7.449704800853756 boost points/s; regular zone 12 |

All three searches terminated on budget. The E/M result is a successful bounded search/application, not a promise of the best possible balance or equal retained percentages. PP/boost numbers are forecast outputs recalculated after observed Apply, **not long-run measured farming yields**. Retention above 100% on those applied rates is relative to frozen best-found references. Route zone/idle attack were observed; floor 166 has no independent live floor telemetry. `*-later-support.json` explicitly permits later ordinary rotation.

### Earlier Scheduled Run

`verify-combined-20261017-223038-065fcf9c/summary.json` reports overall/scheduled pass for a 300-second automatic stage and its 20-second ordinary NGU successor hold. Run ID: `743fac66a7b345fdbcb0d9efa5e378b7`.

- Observed path: Augments with budgeted magic W, one `phase-completed:Augments` boundary replan, dedicated Wandoos, then NGU handoff/hold. `replanCount: 1`; original `durationSeconds: 300`, rebirth deadline `13053.458730869781`, with no extension.
- Final execution forecast A*W: `6.8671992551646446e19`; actual terminal A*W: `6.867388362183461e19`; signed `(actual - forecast) / forecast`: **+0.0027537721244143927%**. Augment total matched; the summary's explicit acceptance tolerance was 2%, not the measured error.
- Paid AT was **not** exercised: existing W work already committed the continuation to no further paid AT. Success is not current-engine live AT coverage or Wish 190 coverage.
- The summary parser records `actualPolicyPhasesAvailable: false`/unavailable numeric target telemetry. A separate native inspection JSON does expose selected phases/targets. Do not substitute earlier manual preview targets for executor targets or infer native callback counts from elapsed time.

Native desktop-window evidence from the [inspection script](../../temp/inspect-combined-native.ps1) is `combined-native-20261017-173123-misc.png` plus its paired `.json`. The screenshot shows **Automatic plan stage**, stage 2 (one-based), this run ID, 5m budget, 28s elapsed and Augments active. The JSON correlates before/after runtime and UI Automation text, including stage `executionMode: 1`, phase targets and actual allocations. This is Augments-phase UI evidence during the latest run, not a screenshot of the later boundary or final handoff.

### Earlier Results And Gaps

The earlier 300-second `verify-combined-20261017-214640-e57ee3ae` scheduled portion passed at **+0.009723531887169715%**, but the overall report **failed** its later mixed preview on adventure power `2315574.75` versus native `2315575.25`. The subsequent native-float capture/precision changes and successful three-case report above supersede that mixed-preview failure, not its recorded overall result.

The earlier 150-second target-limited recording `verify-combined-20261017-205013-044c36d5` missed its combined forecast by **-1.4343541695%**. Its numerical cause remains unresolved; short integer-threshold timing loss is plausible, not proved, and the later disclosure of concurrent manual activity prevents treating that recording as an isolated model-defect reproduction. Later accuracy is not universal. See the [numerical diagnosis and missing telemetry](../checks/CombatPrep/RECORDED_COMBINED_DIAGNOSIS.md).

Native-method offline tests cover bootup/free AT, current-engine paid AT gates, material GPS events and native same-instance equipment growth. Current live state/unlock constraints prevented a new-engine live AT or Wish-190 run; older live AT in the phase-support record is historical only. No controlled live GPS-shock test is claimed. Native-method tests isolate rendering/scene leaves and do not certify Unity callback ordering or every game/JIT version.

### Controlled Final Run

After the user handed over exclusive game control, the injector and desktop UI were rebuilt/restarted and the complete live workflow passed in `verify-combined-20261017-231447-7cad9127/summary.json`, with no cleanup errors. Run ID: `417fe1e0cd944eddadca2d756924b3e8`.

- The automatic 300-second stage ran Augments/background magic W, replanned once at the completed augment boundary, ran dedicated W, restored owned support, and handed off to actual E/M NGU through its 20-second successor hold. The original deadline and run identity were retained.
- Final forecast A*W: `1.0165787959709072e20`; actual: `1.0166283446342887e20`; signed error **+0.004874060287102672%**. The explicit test acceptance setting was 2%, not an accuracy guarantee.
- All three mixed Preview/Apply cases passed in the same controlled session. E+M projected approximately 100% Energy and 42.857% Magic retention over 7200 seconds. E+PP and E+boosts used 900-second forecasts. Actual applied NGU allocations were E `80274896`, M `3955250`; subsequent normal automation may redistribute them.
- Applied PP production `93.07063781842112` progress/s and boosts `7.4996319085693575` points/s are modeled rates, not measured long-run yields. Frozen references and the original baseline survived Apply. One-shot ownership was released as intended.
- Read-only 900/3600/86400-second Combat previews passed the separate-lane opportunity-cost contract. Paid AT was not exercised because existing W work excluded further paid preparation. Wish 190 and controlled GPS shocks remain native-method offline coverage, not claimed live unlocks/events.
- Native desktop evidence during this controlled run: `combined-native-20261017-181532-misc.{png,json}`. It observed the automatic Augments phase without issuing training commands.

The final full `verify-ui.ps1 -IncludeInjector -BrowserTests` pass succeeded: 1452 frontend tests, 98 Rust tests, 496 browser passes with four existing skips, 6560 pure Combat assertions, 5929 compiled Combat assertions, 1580 preset numeric assertions, 4075 compiled native preset assertions and 1699 protected-timing assertions. The native desktop Release build also passed. The additional 24-hour unsaturated bootup/free-AT stress passed its 24-second CPU bound; that is offline performance evidence, not a full-day live run.

### Final Handoff

After the controlled harness, `24hr` was enabled at its actual post-merge Energy NGU row (zero-based index 3), automatic rebirth was disabled, Quick was cleared, and no Combat/cleanup owner remained active. Normal Adventure/boost/pit/blood preferences were restored and progress quicksaved. The eight temporary test plans were removed through the normal delete command; reports/backups remain available.

Runtime report: `phase-support-final-20261017-182100.json`. The desktop UI was reopened against the real selected plan rather than a deleted test definition. `combined-native-20261017-182207-dashboard.{png,json}` shows `24hr` RUNNING, Auto-rebirth Off, Quick None and positive Energy/Magic NGU allocation. Fresh runtime is the authority after this historical observation. Optional follow-ups are in [temp.md](../../temp.md).

## Source Map

| Contract | Implementation / checks |
| --- | --- |
| Stage mode, budgets, durable identity | `CustomPlans/CustomPlan.cs`, `CustomPlans/CustomPlanStore.cs`, `Managers/ProtectedStageScheduler.cs`, `Main.ProtectedTiming.cs` |
| Automatic capture/replan/shared execution | `Main.AutoCombatPrep.cs`, `Main.CombatPrep.cs`, `Main.CombatPrepCapture.cs`; `checks/CombatPrepIntegration/ScheduledIntegration.cs`, `EquipmentReplanRegressions.cs`, `CleanupRecoveryRegressions.cs` |
| Bootup, free AT, adaptive search/costs | `Managers/CombatPrepPlanner.cs`; [pure checks](../checks/CombatPrep/README.md), [native progression checks](../checks/CombatPrepIntegration/README.md) |
| Mixed production/support/ITOPOD/precision | `Managers/LoadoutPresetPlanner.cs`, `Main.LoadoutPresets.cs`; [numeric checks](../checks/Presets/README.md), [compiled adapter checks](../checks/PresetIntegration/README.md) |

Source paths are relative to `NGUInjector/` unless prefixed `checks/`. Shared exact support API/receipts are documented in [support-core.md](support-core.md); detailed manual command safety is in [Combat Prep Backend](COMBAT_PREP_BACKEND.md), and preset library/Apply usage in [Weighted Loadout Presets](loadout-presets.md).
