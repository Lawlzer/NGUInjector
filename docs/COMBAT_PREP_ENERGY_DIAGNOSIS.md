# Combat Prep Energy Underuse And Allocation Flicker

## Resolution

The diagnosis below records the pre-fix behavior. The October 19 fix replaces
lifetime execution ceilings with live resource observations, preserves forecast
no-future-refill assumptions, and adds ordered NGU then eligible TM fallbacks.
TM is excluded while any current/future augment phase remains to protect its
gold. Native allocation text is refreshed after funding; synchronous recall and
rebuild remain, without a persistent zero display or partial-progress reset.

Verification: a fresh backend and full offline integration suite passed 7,954
assertions, including 662 resource/fallback checks. Pure planner and ordinary
allocator checks passed 6,560 and 2,043 assertions respectively. Installed native
generation, TM gold spending and augment progression execute on fixture objects;
rendering callbacks record final allocation fields. Actual live Unity rendering
has not been verified, and the running game was not reinjected during this fix.
See [execution contracts](COMBAT_PREP_BACKEND.md#execution-ownership) and
[integration coverage](../checks/CombatPrepIntegration/README.md#live-resource-reconciliation-and-fallbacks).

## Original Diagnosis

Read-only diagnosis on October 19, 2026. No runtime commands were submitted,
game/settings/save state changed, or production code modified. Existing runtime
heartbeats, logs, source, and installed-game method inspection supplied the evidence.
This is a diagnosis, not a fix or a new live acceptance test.

## Observed Run

Plan `24hr`, stage index 1, scheduled run
`cda3422335414b7b93038372afda769f`:

| Observation | AT, 15:35:08 local | Augments, 15:40:10 local |
| --- | ---: | ---: |
| Current total energy | 152,916,675 | 162,628,609 |
| Idle energy | 81,117,751 | 90,829,685 |
| Executor energy pool | 71,798,912 | 71,798,912 |
| First active energy lane | 35,899,456 | 35,899,456 |
| Second active energy lane | 35,899,456 | 35,899,456 |
| Replan count | 0 | 1 |
| Replan reason | stage-start | phase-completed:AT |

Both Wandoos AT lanes consume energy, including AT-4, which improves magic
Wandoos. In both phases, the active pair consumes the entire executor pool.
Total minus idle minus the pair is 12 energy; this small remainder was not
independently attributed. It does not explain the tens of millions left idle.

Four consecutive Augments heartbeats at 15:40:10-15:40:13 retained identical
allocations and pool while augment levels increased from 2070 to 2081 and
upgrade levels from 2861 to 2878. The earlier AT observations increased from
981/979 at 15:34:15 to 1023/1021 at 15:35:08; AT ended at 1045/1043 against
planned minimum targets of 1043/1043. Training was progressing.

## Cause 1: Frozen Spendable Pool

The executor's budget is not the currently available resource total:

- `Main.CombatPrepCapture.cs:260-273` captures per-profile usable totals from
  initial current resources and projected caps, reserving basic training energy.
- `Main.AutoCombatPrep.cs:150-156` initializes the scheduled pool from captured
  Current, bounded by any persisted continuation pool.
- `Main.CombatPrep.cs:626-631` recalls resources, then sets the pool to
  `min(previousPool, capturedProfileTotal, liveIdleAfterRecall)`. It can shrink
  but cannot increase.
- `Main.CombatPrep.cs:723-724` bounds actual spending by that pool, captured
  profile totals, idle resources, and the current cap.
- `Main.AutoCombatPrep.cs:203-206` clamps freshly captured profiles to the old
  pool during replanning. A boundary replan does not adopt regenerated energy.
- `Managers/BlackBarAllocator.cs:74-94` receives only the unused portion inside
  that pool for NGU. Idle resources outside it are not fallback NGU budget.

The no-refill rule is explicit in capture assumptions and planner documentation,
not an accidental 50/50 split. The planner carries the same nonincreasing pools
across visited profiles (`Managers/CombatPrepPlanner.cs:699,823-827`). Native
online generation can refill current and idle resources under higher-cap gear,
but execution never enlarges its acquired budget to use that refill.

This explains the observed underuse across AT and Augments and applies to later
Wandoos execution, as well as manual timed Combat Prep. The same restriction
exists for magic; its observed pool was 7,169,533 against 7,661,496 current magic.

### Historical Origin Is Not Captured

The exact first event producing 71,798,912 is not proven. Entry fight gear is a
plausible contributor: `logs/inject.log:512-514` records fight gear and a boss
push immediately before acquisition. `Main.AutoCombatPrep.cs:133-150` performs
entry fight before capture, and `Main.CustomPrep.cs:159-163` equips fight gear.

However, runtime does not expose the historical capture's current total/cap,
each original profile ceiling, incoming persisted pool, or pool-decrease cause.
Initial available energy, Current's cap, a visited phase profile's captured cap,
or a persisted ceiling can all impose the same lasting limit. Do not claim the
fight gear cap was exactly 71,798,912 without additional evidence.

## Cause 2: Recall Paints Zero, Refill Does Not Repaint

`Managers/CombatPrepPlanner.cs:297` sets a 0.5-second allocation interval.
Every successful due pass in `Main.CombatPrep.cs:626-649` unconditionally calls
native resource recall and then directly re-funds the model fields. There is no
unchanged-allocation guard. Phase acquisition can perform an additional recall.

Native recall both zeros the allocation and refreshes its visible menu text.
The executor then writes positive allocations directly at
`Main.CombatPrep.cs:753-756,764,771`, without a corresponding final allocation
text refresh. Thus real synchronous clear/rebuild operations also leave stale
display state:

| Native lane | What repairs allocation text after direct refill |
| --- | --- |
| AT | A level completion or explicit/menu refresh; ordinary progress ticks update the bar, not allocation text |
| Augment/upgrade | The corresponding level completion or explicit/menu refresh |
| Wandoos | Its next enabled/installed native update, nominally every 0.02 seconds |

Fast level completions can make AT/Augments alternate between zero and positive
display values. Slow completions can leave zero displayed despite funded model
state. Wandoos has the same recall behavior but a different text-repair cadence.

Installed native method inspection confirmed `Character.removeMostEnergy`,
`AdvancedTrainingController.removeAllEnergy/refresh/updateAdvancedTraining`,
`AugmentController.removeAllEnergy/refreshMenu/advanceAug/advanceUpgrade`, and
`Wandoos98Controller.removeAllEnergy/removeAllMagic/updateWandoos`. Existing
research copies are under `%LOCALAPPDATA%/Temp/opencode/Assembly-CSharp.il` and
`walderp-inspected/`; they are evidence artifacts, not repository dependencies.

Recall preserves completed levels and partial progress. It does not repay an
already-started augment bar's gold cost. Recall and refill run synchronously on
the Unity thread, with no yield between them: this is not evidence of losing
every other half-second of training. Callback/render timing and frame cost were
not measured. Per-frame dispatch also reaches `TickCombatPrep`, so the Quick
timer alone does not establish the actual refresh spacing.

## Alternatives Checked

- Ordinary stage/Quick allocators yield while Combat Prep is active:
  `Main.AutomationLoop.cs:21,111` and `Main.CustomFocus.cs:285-287`.
- Legacy allocation is disabled at `Main.AutomationLoop.cs:12`.
- The initial AT observation had zero replans, so repeated policy search was
  not causing that symptom. One normal completed-AT boundary replan followed.
- The pair split is not leaving half the run budget unused: both observed
  allocations sum exactly to the pool, while both targets were unfinished.
- Native rendering/recall are substituted in the existing integration fixture.
  Passing allocator fixtures alone cannot establish native display correctness.
- No allocation-error/exception evidence was found in the inspected current
  injector log; runtime had no failure or cleanup-pending status.

## Correction Scope

1. Repair stale native display independently by refreshing affected allocation
   text after the final refill. Avoid treating a visible zero as proof of lost
   model progress.
2. To use regenerated resources, revise capture/profile ceilings, runtime pool
   reconciliation, continuation persistence, and planner assumptions together.
   Removing one minimum leaves other frozen ceilings in place. Retain basic
   training reservations, ownership checks, paid-AT/Wandoos ordering, and the
   original deadline. Using observed refill is distinct from forecasting future
   refill; both decisions need explicit semantics.
3. If removing clear/rebuild churn, reconcile desired versus existing owned
   allocations, preserving cap growth and NGU redistribution. Simply skipping
   unchanged phases would stop necessary cap/priority updates.
4. Add regression cases for resource generation after capture, low-cap to
   high-cap gear transitions, pool persistence/replanning, and native allocation
   text after refill. Record pool-change inputs/reasons for future diagnosis.

No build/test suite was run for this documentation-only diagnosis. Live
heartbeat progression verifies funding and the persistent budget discrepancy;
native method inspection establishes the display mechanism, not a captured
frame-by-frame rendering trace.
