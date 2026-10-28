# Stage Fight Schedules

Plan schema 18 replaces `FightOnEntry` with one nullable `FightSchedule` per stage.
Both standard and automatic Augments + Wandoos stages support it. The Svelte and
native editors expose Fights, Start from, First fight after, and optional repeat.

```json
{
  "FightSchedule": {
    "Id": "example-recurring-fight",
    "Anchor": 1,
    "DelaySeconds": 60,
    "IntervalSeconds": 180
  }
}
```

`Anchor: 0` starts at stage entry. `Anchor: 1` starts at the first verified,
stage-owned positive Augment or AugmentUpgrade funding, not phase selection or
setup. Delay and interval are nonnegative Int32 seconds; interval zero means once.
IDs are unique within the plan and contain 1..128 ASCII letters, digits, underscores
or hyphens. Unknown extension fields are preserved.

## Timing

- A 180-second entry delay and 180-second interval gives opportunities at 3m, 6m,
  9m, etc. The example above gives opportunities 1m, 4m, 7m after Augments funding.
- Timing uses elapsed rebirth/game time after the anchor, not productive training
  credit. If Augments never receives verified funding, its anchor remains unset.
- Service is polling-based. An opportunity becomes eligible at its boundary; native
  ownership, permissions, preparation and persistence can defer actual combat.
- Missed slots coalesce. At most one attempt is active, with no backlog of fights
  after a pause. Slots elapsed during an active attempt are consumed on retirement.
- The definition is frozen for the current stage entry. Timing/anchor/ID edits take
  effect next entry or explicit restart; disabling stops future admissions. Disabling
  never removes prerequisites from an already active native fight.
- Stage exit expires outstanding opportunities. A loop/restart creates a new entry.
  Schedules do not independently gate rebirth or renew automatic stage budgets.
- Fight preparation retains the old entry-fight behavior: boss push, without power
  fruit consumption or digger purchases.

## Automatic Preparation Handoff

A scheduled fight suspends the existing automatic run instead of cancelling and
replanning it. Its acquired policy, phase, AT/Augment targets, augment tier, OS,
and original run/phase deadlines remain unchanged. Owned routes and borrowed target
menus are released before fight preparation. On release, a verified successor witness
allows fresh setup and resource reconciliation for the retained phase. Historical
resource allocations are not restored, and manual changes are not adopted as authority.

Resumption rechecks the original deadline, ownership, native combat, targets, gear,
support and the no-paid-AT-after-Wandoos invariant. Native nuke pause remains serviced
while reacquisition is blocked. Partial-resume cancellation returns only still-owned
borrowed fields. Unexpected/manual interference keeps conservative cancellation and
safe-background behavior rather than granting a new preparation budget.

## Persistence And Migration

The existing stage checkpoint stores the acquired schedule, anchor and consumed slot.
Admission must be durable before invoking a fight. A failed anchor write retains the
original observation in memory for retry rather than moving the anchor forward.
Live attempts, suspension objects, native references and claims are not restored.
Restart during automatic suspension falls back conservatively under the old deadline.
Consumed opportunities are not blindly replayed; this is not an exactly-once native
combat guarantee across arbitrary crashes or save rollback.

Legacy `FightOnEntry: true` becomes an entry-anchored, zero-delay, once-only schedule
with deterministic ID `legacy-entry-v1-<zero-based-row>`. Existing consumed entry
markers suppress replay. False/absent becomes null; explicit `FightSchedule: null`
wins over a valid legacy true. Canonical saves remove `FightOnEntry`. Reads do not
rewrite source bytes. Reorder/copy preserves schedule IDs; new schedules get new IDs.
Schedule-only edits do not revoke active claims or reset earned stage progress.

Connected UI/bridge operations require `fightScheduleVersion: 1`, `planVersion >= 18`
and existing timing capabilities. Future plan versions are rejected before normalization.
Update the injector and UI together: already-deployed old binaries cannot be made to
understand schema 18 through a file migration alone.

`planProgress.fightSchedule` reports entry/ID, state, next fight time, remaining seconds
and a blocker reason. Projection is read-only and the UI only attaches it to matching,
fresh current-stage telemetry. It never drives execution.

## Regression Coverage

- `checks/ExternalUi/FightSchedulePersistenceChecks.cs`: strict JSON validation,
  migration, roundtrip, identity, exact numeric tokens and disabled-source preservation.
- `checks/ProtectedTiming/Program.cs`: schedule edits preserve scheduler identity/credit.
- `checks/CombatPrepIntegration/AutomaticEntryRegressions.cs`, selector
  `--check CheckStageFightSchedules`: cadence, anchor funding, durability, native
  handoff, same-policy continuation, foreign edits, Wandoos admission, nuke lease,
  and partial-resume cleanup.
- `checks/PresetIntegration/StageEntryHandoffChecks.cs`, selector
  `--stage-fight-schedules`: standard-stage funding, recurring admission and ownership.
- UI `fight-schedule.test.ts`, Rust `fight-schedule-tests.rs`, and stage browser
  cases: duration drafts, migration, stable identity, readback, capability rejection,
  current-entry telemetry and desktop/mobile controls.
