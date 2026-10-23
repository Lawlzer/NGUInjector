# Automatic Reconciliation

The October 20, 2026 recovery fix removes the permanent manual-acknowledgement stop
after automatic Combat Prep loses its historical restoration receipts. An enabled
plan converges toward its current desired state rather than waiting indefinitely
to restore an obsolete snapshot.

## Intent And Constraints

Preset objective weights and planned beard/digger portfolios continue to describe
the desired setup. Temporary combat phases provide scoped intent; current resource
allocation reuses the shared projection/writer and the existing phase-first, capped
NGU/TM background policy. Removing a temporary owner allows the current plan/preset
to reapply from fresh native observations. This does not convert ordered legacy
priorities into numeric weights or change the user's saved ratios.

Not every requirement is a soft weight. Managed-lane permissions, exact finite
targets, purchased support limits, gold affordability, the original elapsed
deadline, OS pin and the no-W-during-paid-AT rule remain hard constraints. Large
arbitrary weight multipliers cannot safely replace those constraints.

## Lifecycle

- Only the active coordinator services reconciliation; telemetry stays read-only.
- Safe synchronous cleanup may restore changes still owned by the terminating run.
- Live cleanup retries have a nonrenewable four-attempt/two-second limit.
- Lost authority permanently drops target callbacks, gear restoration permission,
  support receipts and native-timer permission without historical native writes.
- Entry/epoch-bound retirement is saved before normal acquisition resumes. Actual
  file-lock/permission failures retain an actionable wait and retry automatically.
- Current-intent reconciliation proceeds in the same pass after retirement. It
  captures current gear/support/resources instead of reconstructing old objects.
- Expired entries advance through normal scheduler logic, with no synthetic skip,
  restarted preparation budget or protected credit for time spent disconnected.
- Explicit Cancel can finish an entry; involuntary interruption preserves pending
  continuation and its original deadline.

## Interrupted Preparation

Before paid AT is funded or its native menu targets are overwritten, a bounded
checkpoint record stores the acquired targets, weight and phase timing. A fresh
acquisition resumes that exact gate under the same entry/deadline. Both W resource
lanes remain zero until both targets are reached or the global budget expires.

All native W levels, progress and allocations are checked before accepting that
gate. Conflicting W history is saved before cleanup recalls can erase allocation-only
evidence. If persistence fails, cleanup does not recall that evidence away.

Old checkpoints cannot supply targets that were never stored. Previously acquired
entries with missing/invalid gate metadata, conflicting W history or invalid setup
accounting use safe NGU/TM background intent until the original deadline. They do
not buy new paid preparation, grant extra time, or claim unknown AT work completed.
Completed preparation history, observed resource pools and the OS pin are retained.

## Verification

Offline verification against the normal Release DLL, MVID
`bdd17352-8607-428d-a35f-703714438c69`:

- Automatic reconciliation selector: 1,129 assertions passed.
- Full compiled combat integration: 9,417 assertions passed.
- Compiled preset integration: 10,732 assertions passed.
- Earlier implementation pass: compiled ownership 10,224 assertions, pure protected
  timing 1,717 assertions/64 scenarios, pure CombatPrep planner 6,560 assertions.
- UI recovery/progression unit tests: 687 passed; controller suite: 284 passed.
- Scoped desktop/mobile browser tests: 124 passed, using mocked native IPC.
- Svelte check: zero errors/warnings; native desktop Release build passed.
- Additional source-linked checks: sustained focus 2,580 assertions, external UI
  9,492 assertions, manual Adventure overlay 191 assertions, Titan Gold 1,167
  assertions/70 cases. The final ownership/preset reruns also passed 10,224/10,732.

The regressions exercise actual compiled capture, planning, allocation, cleanup and
checkpoint boundaries with isolated game objects. They are not live Unity rendering
or callback-order verification.

## Live Deployment

The rebuilt backend and desktop UI were deployed on October 20, 2026. A fresh
quicksave and backup preceded a graceful game restart and normal reinjection;
the injected dist DLL SHA-256 matched the verified Release DLL. No manual stage
restart/set/skip, preset Equip, settings patch or plan/library save was used to
resolve recovery.

The new injector session began at `2026-10-20T22:42:49.5795133-05:00`. It restored
the original `24hr` generation, automatically retired the orphaned marker and
advanced the expired Augments + Wandoos entry to its configured AT Adventure
successor. That stage's configured fight-on-entry ran before normal allocation.
An initial short deployment check observed this legitimate combat wait; it was
not another historical cleanup stop.

Six observations across 50 seconds, from 22:46:17 to 22:47:07 local time, all showed:

- Resource execution `Applied`, owner stage 3/4 AT Adventure.
- `cleanupRecoveryRequired: false`.
- Energy 72,534,819, idle Energy 0.
- Magic 7,885,325, idle Magic 0.
- Plan enabled and auto-rebirth enabled, preserving their pre-deployment values.

Settings and saved plans retained their original hashes. The library also matched
immediately after deployment; an unrelated concurrent edit subsequently added
`AdventureDynamicWeight: 25` to NGU Energy and was preserved, not overwritten.
The verification quicksaved afterward, and the rebuilt desktop UI was reopened.

Machine-local backup and evidence:
`C:\Users\Kevin\AppData\Local\Temp\opencode\automatic-reconciliation-live-20261020-224211\`.
`verification.json` records the six observations, preserved preferences, concurrent
edit and DLL hash; `verified-runtime.json` records final native telemetry. This
establishes live recovery and sustained funding in that state, not every future
stage, restart timing or native rendering behavior.

A separate preset reapplication timeout later stranded energy while this recovery
flag remained clear. That failure and its subsequent fix are recorded in
[Preset Commit Budget](preset-commit-budget.md); the observation above is historical,
not evidence that no later allocation failure occurred.
