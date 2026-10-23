# Automation Integration Review (Historical)

Implementation follow-up: the original R01-R28 findings and subsequent R29-R33 fixes are implemented. See [Automation Improvements](AUTOMATION_IMPROVEMENTS.md) for current behavior, shared utilities and completed verification, and [temp.md](../../temp.md) for remaining work and decisions. The findings, source locations and test facts below describe the original review, not outstanding defects or new live-test evidence.

Reviewed October 16, 2026 against the current working tree, including untracked planner/preset sources. This is a review report, not an implementation change. Production code, saved plans, settings and the running game were not changed for the review.

## Main Conclusion

The individual systems have useful coverage, but they are not one integrated activity planner. The highest-risk gaps are owner cleanup, stale application receipts, and disagreement between selection/projection and the actual allocation/support policy.

Fix concrete behavior first. Then share numeric and mutation-safety foundations while retaining the different objectives and lifetimes of stages, Combat Prep and weighted presets.

## Current Execution Paths

| Path | How it chooses work | Support and execution | Lifetime |
| --- | --- | --- | --- |
| Scheduled stages | Existing scheduler, priority lists, pinned AUG/RIT selection | Goal gear, configured-primary beard/digger intent, effective resource overlays | Sustained; stage-entry and application caches |
| Quick focus | Builds stage-like intent; uses ordinary selectors/allocators | Goal gear/support on activation; repeated resource and support refreshes | Sustained; explicitly pauses ordinary plan/rebirth switches |
| Combat Prep | Pure bounded AT/Aug/W timeline search and replay | Projected owned phase gear; current beard/digger selection frozen | Sustained runtime overlay; target gates and absolute deadline |
| Weighted loadout presets | Weighted normalized outcomes and advisory two-phase comparisons | Optional planned support; exact candidate replay and one-time allocation | One-shot application; no sustain timer |
| Temporary detours | Titan, Yggdrasil, Money Pit, fruit-cap and manual actions | Separate manager locks, snapshots and cleanup paths | Temporary, with distributed ownership state |

References: `Main.CustomAutomation.cs:287-430`, `Main.CustomSetup.cs:80-170`, `Main.CustomFocus.cs:106-151`, `Main.CombatPrep.cs:342-498`, and `Main.LoadoutPresets.cs:438-513`, under `NGUInjector/`.

New look-ahead behavior is not automatically used by ordinary stages. Implicit/default Wandoos stage allocation retains the half-stage AT heuristic; explicit priority presets can bypass it. Normal AUG priorities retain `AugmentOptimizer`. Combat Prep has its own time/split/loadout search. Weighted preset sequences are advisory, not executable schedules.

## Priority Findings

P1 means a high-priority persistent stall. P2 means a correctness/integration or material policy problem under the stated conditions. P3 means lower-impact precision/cache behavior. These are not all observed in the running game; verification boundaries are stated below.

### Ownership And Rebirth

**R01 / P1: Pre-rebirth Yggdrasil harvesting leaves both managers locked.**

- Source: `NGUInjector/AllocationProfiles/RebirthStuff/BaseRebirth.cs:310-335`; ordinary cleanup at `Managers/YggdrasilManager.cs:71-81`; disabled legacy gate at `Main.AutomationLoop.cs:149-152,190-194`.
- When Yggdrasil management/swapping is enabled, pre-rebirth acquires gear/digger locks, harvests and defers. The next pass engages rebirth without the ordinary legacy cleanup ever running.
- Offline reproduction retained both `Yggdrasil` locks after actual engagement dispatch, rebirth-counter/clock reset and injector runtime reset. The next stage waited indefinitely for the lock.
- Fix: cleanup owned harvesting state before proceeding, including partial acquisition. Do not rely on the disabled legacy loop.

**R02 / P1: Epoch reset drops titan owner identity without releasing its lock.**

- Source: `NGUInjector/Main.CustomAutomation.cs:633-645,1026-1062,1389-1408`.
- Reset clears active gold/fight titan flags and indices directly. The `Gold`/`Titan` manager lock survives; the cancellation method can no longer identify an active owner.
- Offline reproduction confirmed both variants and the subsequent blocked gear stage.
- Fix: retire the lease before discarding owner identity. On save replacement, release old ownership without applying equipment references from the old save.

**R03 / P2: Unload omits active titan/adventure detour cleanup.**

- Source: `NGUInjector/Main.Lifecycle.cs:42-48`.
- Combat Prep and max-ITOPOD work are ended, but active temporary titan/gold ownership is not. Callbacks then stop with temporary gear/locks retained.
- Offline unload fixtures confirmed the titan variants. Fix same-epoch restoration before clearing references; do not restore old-save objects.

**R04 / P2: A pending gold titan can starve an active fight titan's maintenance.**

- Source: `NGUInjector/Main.CombatRouting.cs:54-57`; `Main.CustomAutomation.cs:780-797,825-844`.
- Gold routing runs first and returns handled while waiting for the active fight owner's lock. The fight route's completion/timeout cleanup is consequently not serviced.
- Fix: service existing ownership before new acquisitions, or allow its maintenance through the waiting branch.

### Diggers And Beard Support

**R05 / P2: Ordinary digger capping omits the level-one offset.**

- Source: `NGUInjector/Managers/DiggerManager.cs:201-235`, especially line 216.
- Drain is `base * growth^(level-1)`, but inversion omits `+1`. With base 100, growth 2 and budget 150, the real source-linked manager selects level zero instead of affordable level one. Budget 750 selects level two instead of three.
- Fix: calculate the correct candidate, clamp it, and verify its forward drain at rounding boundaries.

**R06 / P2: Unaffordable requests occupy the selection shortlist without affordable backfill.**

- Source: `NGUInjector/Managers/DiggerManager.cs:138-160`; `Managers/LoadoutPresetPlanner.cs:685-704`.
- Both paths select the top slot count before affordability. A one-slot, unaffordable Wandoos request can clear all diggers while an affordable lower-weight drop request is available.
- Fix: deterministic affordability-aware selection and share recomputation. This does not require a global optimization solver.

**R07 / P2: Temporary digger restoration does not restore levels or weighted budgets.**

- Source: `NGUInjector/Managers/DiggerManager.cs:72-115,239-243`; active caller `Managers/MoneyPitManager.cs:36-39`.
- Snapshots contain IDs only. Restore re-caps through unrestricted max-affordable logic, not the original weighted policy. An offline probe changed drain from 384/1600 to 1408/1600, exceeding the ordinary 75% budget.
- Fix: save IDs and levels, restore affordable original levels explicitly, or immediately invalidate/reapply the active owner's weighted policy.

**R08 / P2: Digger upgrading can recurse without progress.**

- Source: `NGUInjector/Managers/DiggerManager.cs:245-278`.
- Hard-capped candidates are not excluded. Native `upgradeMaxLevel` can return without increasing the maximum; the recursive caller selects and retries the same candidate.
- Fix: exclude hard caps and detect progress in a bounded iterative loop. This is an advanced-progression condition, not a claim that the current save reaches it.

**R09 / P2: ManualAdventure gear scoring silently overrides independent resource-support intent.**

- Source: `NGUInjector/Main.CustomSetup.cs:86,103-112`.
- A stage training Wandoos/NGU/Blood but selecting ManualAdventure gear loses those resource-specific digger requests because the gear goal clears the request list.
- Fix: pass explicit manual-adventure activity intent from `SetupForManualAdventure`; keep ordinary gear scoring independent from resource support.

**R10 / P2: Planned preset beards do not change immediate projected bonuses or GPS.**

- Source: `NGUInjector/Managers/LoadoutPresetPlanner.cs:415-446,678-708`; capture at `Main.LoadoutPresets.cs:133-139,227-228,300-307`; application at `:487,543-564`.
- Planned selection changes active beard IDs, but metrics retain the old effective NGU/drop/adventure/Gold multipliers. Freezing future levels is reasonable; freezing immediate activation effects is not an executable counterfactual.
- Example: Gold-active gross GPS 1000 becomes 500 when deselected, but planned diggers can be accepted with drain 750. Apply changes beards first and native digger activation rejects the now-unsustainable level after mutation.
- Successful post-support recapture can correct allocations, but cannot repair earlier ranking/preflight. Returned result JSON is constructed before support application.
- Fix: capture temporary potential and permanent multipliers separately; evaluate the selected active set at unchanged levels, then calculate candidate GPS before diggers and blood. Alternatively reject unsupported beard transitions before mutation.

**R11 / P2: Direct preset beard changes leave the normal requirement cache valid.**

- Source: `NGUInjector/Main.LoadoutPresets.cs:543-555`; `Main.CustomBeards.cs:74-81`; `Main.CustomSetup.cs:139-150,187-191`.
- Immediate resume of the same normal activity can hit its old requirement key and mark beard setup complete without restoring the changed selection. A minute rotation eventually repairs it.
- Fix: invalidate application identity after direct mutation, preserving the independent rotation clock; also verify observed selection rather than trusting only the request cache.

**R12 / P3: Beard cache loses priority order.**

- Source: `NGUInjector/Main.CustomBeards.cs:73`; order-sensitive selection at `Managers/BeardManager.cs:43`.
- Sorted cache keys equate requests that choose different optional beards when capacity is limited. A changed Ladybeard/NUMBER order can be ignored until the minute boundary.
- Final rebirth directly reapplies NUMBER, so this is not a demonstrated engagement-time NUMBER loss.
- Fix: preserve the effective optional priority order in the key.

### Ordinary Allocation Versus Forecasts

**R13 / P2: Ordinary augment selection accepts unaffordable unstarted bars.**

- Source: `NGUInjector/Managers/AugmentOptimizer.cs:138-144,267-272`; pinning at `Managers/CustomStageAllocator.cs:329-353`.
- The selector prorates the next bar's cost by expected work. Native execution and BlackBarAllocator require the full start price. Repeated `OptimizeTarget` success can keep an unaffordable tier pinned while allocation gives it zero energy.
- Fix: enforce full immediate start affordability for unpaid bars, separately from future-income estimates and already-paid partial bars.

**R14 / P2: Ordinary augment selection and execution use different pair splits.**

- Source: `NGUInjector/Managers/AugmentOptimizer.cs:111-123`; `Managers/CustomStageAllocator.cs:349-353`; `Managers/BlackBarAllocator.cs:300-333`.
- Ranking assumes tier-specific unequal shares. Stage execution discards those amounts and uses equal capped sharing. Selection also estimates a broader pool than the remainder reaching AUG after reservations/earlier priorities.
- Fix: rank against the exact ordinary allocation policy and available budget. Keep intentionally different standalone policies explicit.

**R15 / P2: Standalone augment caps include an extra 50000 factor in Sadistic.**

- Source: `NGUInjector/Managers/AugmentOptimizer.cs:514-531`; correct comparison in `Managers/BlackBarAllocator.cs:523-529` and `Extensions.cs:163-183`.
- Offline native-getter comparison found a real one-tick cap of 501 versus a legacy returned allocation of 25025051 at offset zero. The raw formula discrepancy is 50000x before rounding.
- Fix both augment and upgrade branches' difficulty factor. Ordinary stage allocation avoids this specific error because it recomputes its own caps.

**R16 / P2: Implicit Wandoos AT is inferred separately for magic and can withhold W with no paid AT.**

- Source: `NGUInjector/Managers/CustomStageAllocator.cs:418-424,542-551`.
- Energy-NGU/Magic-Wandoos defaults can suppress magic W in the first half even though energy schedules no Wandoos AT. Wish 190 also leaves the timed paid-prep phase enabled although paid AT caps are zero.
- Both cases were reproduced offline. Combat Prep's explicit Wish-190 rejection does not correct ordinary-stage behavior.
- Fix: one stage-wide preparation decision derived from energy policy; skip unnecessary paid preparation for free AT.

### New Planner And Preset Boundaries

**R17 / P2: Combat Prep overwrites edits to future phase targets.**

- Source: `NGUInjector/Main.CombatPrep.cs:466-482,509-522,552-558`.
- Active-phase edits cancel correctly. Future-phase edits do not: changing AT bounds during Augments or augment targets during AT survives until entry, then is temporarily overwritten with zero while old captured bounds drive allocation.
- Both directions were reproduced through actual Tick/phase entry/allocation. Cleanup preserves the edited value afterward; the problem is training/spending beyond its changed bound in the meantime.
- Fix: validate future targets immediately before acquiring their phase, without overwriting externally edited values.

**R18 / P2: Sparse accessory placement can invalidate an otherwise legal combat preview.**

- Source: `NGUInjector/Main.CombatPrepCapture.cs:129-141`.
- With two accessory slots and sole accessory A in slot two, the optimizer returns A once. Capture writes it to slot one while preserving A in slot two, then throws conflicting IDs. Moving A to slot one makes preview succeed without changing ownership.
- Actual Optimize results and both control captures were tested offline.
- Fix: construct a unique-instance complete destination map, including explicit empty positions and retained unrelated occupants.

**R19 / P2: Preset feasibility permits gear below retained basic-training allocation.**

- Source: `NGUInjector/Managers/LoadoutPresetPlanner.cs:407-408,478-483`; application at `Main.LoadoutPresets.cs:466,476-485`.
- Capacity below reserved basic training merely becomes zero usable energy, not an infeasible candidate. Native controller swaps reject that final state after token consumption/ownership pause/recall.
- Fix final feasibility and examine intermediate swap states, which can temporarily remove capacity gear even when the final map is legal.

**R20 / P2: Preset NGU forecasts ignore native auto-advance.**

- Source: `NGUInjector/Managers/LoadoutPresetPlanner.cs:513-538,633-638`; `Main.LoadoutPresets.cs:256-285,567-585`.
- Forecast stops at targets under fixed shares. Native auto-advance can transfer allocations into an unfinished zero-weight NGU. The flag is neither modeled nor material to preview identity.
- Fix: model/fingerprint transfers or reject this unsupported target/auto-advance combination before mutation. Do not silently change the global player preference.

**R21 / P3: Preset blood allocation passes exact integers through a double parser.**

- Source: `NGUInjector/Main.LoadoutPresets.cs:573-575`.
- Values above 2^53 can change when the human-facing input parser converts text to double and back to long. An upward-rounded blood share can also clip the next NGU share.
- Fix: use an exact-long allocation route and verify observed deltas.

### Gear Handoffs And Application Receipts

**R22 / P2: Combat Prep skips restoration for Manual Adventure, which does not apply gear.**

- Source: `NGUInjector/Main.CombatPrep.cs:638-656`; `Main.CombatRouting.cs:110-123`.
- The enabled boolean is treated as a gear-applying successor. Normal cancellation/completion leaves prep gear equipped; subsequent Manual Adventure routing does not restore it.
- Offline controls confirmed original restoration with the setting off and retained prep gear with it on.
- Fix: distinguish an active activity from a successor that will actually install replacement gear.

**R23 / P2: Initial Quick Magic NGU clears its non-owned energy lane.**

- Source: `NGUInjector/Main.CustomFocus.cs:127-140,474-482`; `Managers/LoadoutManager.cs:39-44`.
- Sustained magic allocation leaves energy alone, but activation's shared gear preparation recalls E/M/R3. Its magic-only allocator never restores energy, including when ManageEnergy is false.
- Fix: explicitly preserve non-owned allocations where legal, or refuse the transition; do not silently recall an unmanaged lane.

**R24 / P2: Explicit gear application leaves the stage's gear receipt valid.**

- Source: `NGUInjector/Main.ExternalUi.cs:273-292`; `Main.CustomAutomation.cs:607-624`; `Main.ProtectedTiming.cs:56-68`.
- Manual optimize/equip can change gear while `_customStageGearDone` remains true. The same stage can continue allocation and protected credit without reconciling its configured gear.
- Fix: invalidate/suspend application ownership. Whether to pause the plan or reclaim its gear on the next pass is a separate user-facing policy choice.

**R25 / P2: Quick refresh can bypass a deferred fight-on-entry.**

- Source: `NGUInjector/Main.CustomAutomation.cs:298-335,415-430`.
- A gear-less short stage commits its entry key before a locked entry fight. After unlocking, quick refresh accepts GearGoal < 0 without the fight receipt and can finish the stage before the next slow pass.
- Fix: require the entry-fight receipt independently of the gear-less shortcut.

**R26 / P2: Temporary loadout snapshots cannot restore original empty slots.**

- Source: `NGUInjector/Managers/LoadoutManager.cs:47-58,315-337`; `Extensions.cs:71-87`.
- Snapshots omit empty slots. An accessory installed into an originally empty slot during a titan/gold/F8 detour can remain after restoration.
- Fix: distinguish complete exact maps from intentional partial loadouts. Do not change all omitted legacy destinations to empty globally.

**R27 / P2: Fruit cap-gear failure can leave temporary gear marked as stage gear.**

- Source: `NGUInjector/Main.CustomAutomation.cs:453-479,513-529`.
- Restoration is not in finally, and stage gear remains marked done during the mutation. A failed activation/menu refresh or partial equipment application can return to quick allocation and protected credit in temporary gear.
- Fix: suspend/invalidate before the swap and restore through finally; retain retryable unapplied state if restoration fails.

**R28 / P2: Money Pit changes magic before checking whether its digger swap is blocked.**

- Source: `NGUInjector/Managers/MoneyPitManager.cs:26-35`.
- In the high-gold/Wish-4 branch, a skipped locked operation has already recalled magic and capped rituals. Without another resource owner, the previous allocation is not restored.
- Fix: ownership preflight before any mutation.

## Important Limits, Not Additional Bugs

- Combat Prep freezes current beard/digger selection and levels. It does not currently search dedicated per-phase support. This is explicit, but it prevents claiming that every full-focus comparison includes the best support loadout.
- Combat capture retains current bootup in W speed and does not populate the engine's early/late bootup fields. A fresh-RB two-hour forecast can undercount substantial later training. This is a disclosed approximation, but a high-value next modeling improvement.
- Combat maximizes terminal Augments x W. It does not price foregone permanent NGU progression, so its recommendation is not universal account optimization.
- Resource-seconds and residual NGU budgets are not realized NGU production. Targets, weights and caps can leave resources idle. The UI currently exposes dump-equivalent focus time rather than all engine opportunity-cost fields.
- Fixed applied-policy duration is not guaranteed positive production. Zero-funded but available fixed stages can earn time by the explicit current contract; mandatory AT-target stages cannot finish until levels are reached. Changing this is a policy change, not a bug fix.
- Core-first beard priority is intentional. Lower slot capacity can exclude requested optional beards; request success must not be interpreted as every requested beard fitting.
- Ordinary Blood/Iron-Pill overlays intentionally preserve configured gear/support intent. Do not infer a different gear/digger goal from every temporary resource-priority promotion.
- Preset application is explicitly one-shot. Its 120 seconds are preview-token validity, not a sustain duration. Last applied is historical metadata, not proof of current equipment/support ownership.

## Shared Utilities Worth Extracting

| Foundation | Existing duplication | Recommended boundary |
| --- | --- | --- |
| Resource stat projection | `Main.CombatPrepCapture.cs:259-330`; `Managers/LoadoutPresetPlanner.cs:18-49` | Numeric E/M cap/power calculation, raw spec order, offhand, float intermediates, clamps, rounding and post-cap potions. Keep progression objectives out. |
| Complete gear snapshot/preflight | `LoadoutManager.cs:315-337`; Combat capture/map helpers; preset exact-instance capture | Exact instances, explicit empties, revisions, topology, resource reservations, planned swaps and observed partial results. Preserve partial-loadout APIs. |
| Affordability | Ordinary digger math, preset digger math, Gold-beard restoration, augment bar-start logic | Verified digger drain/level primitives; ID+level snapshots; explicit wallet/start-cost versus recurring-GPS budgets. Keep selection policy separate. |
| Integer allocation/pool arithmetic | `BlackBarAllocator.cs:180-220`; `LoadoutPresetPlanner.cs:641-663`; Combat two-lane sharing | Shared exact arithmetic with explicitly different tie-break/remainder policies. Buffered caps and one-tick caps remain distinct policies. |
| Ownership/application receipt | Stage/focus booleans, manager locks, Combat epoch/gear lease, preset synchronous apply | Requested, effective, applied and observed state; owner/epoch identity; domain-specific permissions; idempotent cleanup and receipt invalidation. Not one global boolean lock. |

Also useful later: a typed BoostFarm numeric-data capture API instead of reflecting private Almanac fields from preset capture. This is lower priority than the correctness boundaries above.

### Do Not Unify

- Combat's Augments x W objective with presets' weighted normalized utility.
- Ordinary target selectors, future-state simulators and game actuators into one opaque function.
- Ordinary configured support intent with temporary effective allocation overlays.
- Preset advisory sequences with executable Combat phases.
- Native-nuke/OS lifetime rules with a generic lease implementation.
- Search-cache validity with equipment-application validity: a search result may remain optimal while its gear is no longer equipped.

## Suggested Fix And Refactor Order

1. Fix orphaned locks and unload cleanup, then digger no-progress recursion. These are stall/process-safety boundaries.
2. Correct digger affordability/backfill and exact level restoration. Add successful real-manager coverage before extracting shared affordability helpers.
3. Correct planned beard effects/GPS and normal beard-cache invalidation/order.
4. Align ordinary AUG selection, affordability and Sadistic caps with its actual execution policy; correct stage-wide implicit AT detection.
5. Fix exact gear maps, reserved-resource preflight and owner handoffs, including future-target changes and Quick Magic's non-owned lane.
6. Extract shared resource projection and integer allocation primitives, retaining independently checked formulas and current rounding contracts.
7. Introduce one ownership/receipt source gradually: stages/focus first, then Combat/presets, then small groups of temporary detours. Old predicates must delegate rather than remain a competing authority.
8. Extend future bootup and phase-support projection, then consider explicit sustain/sequence behavior for presets if wanted. Do not silently change their one-shot contract.

Keep bug fixes separate from behavior-preserving extraction. Keep saved plan schema, command shapes, stage-entry identities, earned scheduler history and existing preset token semantics stable during refactoring. Split migrations into small reviewable groups instead of a whole-application rewrite.

## Verification And Coverage

Ran `verify-ui.ps1 -IncludeInjector -GameFolder <installed Steam game>` against this worktree. It passed frontend check/build, 1249 frontend tests, 97 Rust tests, external UI, sustained focus, protected timing, 2722 pure-planner assertions, 916 compiled Combat integration assertions, gear/black-bar checks and preset checks.

Additional review probes ran only offline, under the approved temporary directory:

- `ngu-support-review-20261016`: real source-linked digger/coordinator probes; beard manager's 226047 assertions across 19771 scenarios; fresh stage-support/Combat integration checks.
- `lifecycle-followup-20261016`: 61 assertions reproducing Yggdrasil's two-pass rebirth lock leak, titan epoch/unload ownership and Manual Adventure restoration. Native rendering/harvesting leaves were substituted; actual coordinator/engagement dispatch and lock state were exercised.
- `CombatPrepFollowup-20261016`: 43 assertions reproducing future-target overwrite, sparse accessory capture and native Sadistic-cap mismatch against a freshly built backend.

No live bridge actions, gear swaps, rebirths, injector restarts or changes to the running game were performed during this review. The prior two-hour Normal/98 result remains evidence for that specific planner scenario, not evidence that every ordinary/preset/lifecycle path is correct.

The largest test gaps are successful ordinary weighted-digger activation, native target auto-advance, actual InventoryController allocation guards during swaps, and transitions between real support owners. Some existing suites replace these exact boundaries with recorders or data-only Inventory swaps. Passing tests therefore do not contradict the findings above.
