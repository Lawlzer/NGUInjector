# Adventure Route Review Recovery

## Confirmed Failure

The live injector started at 09:37:29 on October 23 reports:

`Route commitments require reviewed same-entry rebind; no acquisition or budget was renewed.`

`CustomAdventureTick` returns on this quarantine before ordinary zone selection or
`TryRouteManualTitan`. The manual titan setting is enabled for Walderp, but its
runtime status remains the initial `Disabled` because the actor never executes.
This is not evidence of an AT, spawn, or exhausted-death-budget failure.

The retained lifecycle record contains no active processes or tower deaths and one
never-started Walderp observation, with no selected gear, deaths, limit, or owned
time. Its explicit review was unreachable: `PreviewExecutionRebind` always chose
the scheduled selection while an unfinished plan retained a selected policy. A
stale selected-policy issue also won after dashboard Equip paused the plan. Live
dashboard resource allocation was verified; that did not release route quarantine.

## Focused Fix

- `Main.ExecutionPersistence.cs` reviews pending routes before selected policies,
  after the unchanged invalid-outer-checkpoint gate. Both scheduled-selection
  eligibility branches now require the plan to be enabled.
- `Main.ExecutionStore.cs` reports the same route-first review owner/reason and
  does not surface paused-stage selection issues as active review obligations.
- No loader classification, automatic uncertainty clearing, receipt fabrication,
  settings edit, stage restart, acquired-selection replacement, or relaxation of
  route import, AT readiness, death budgets, and finite-action admission was added.
- `ExecutionPersistenceChecks.cs` adds a four-way running/paused and
  pending/reviewed-stage matrix. It generates the actual unstarted observation
  through the native AT gate, loads the real sidecar, exercises public review and
  rejection paths, and verifies Walderp zone entry on the next ordinary tick once
  native AT is complete. It does not prove a live kill.

## Verification

| Check | Result |
| --- | --- |
| Pre-fix compiled regression | RED after 28 assertions: stage owner returned instead of `Route processes`; MVID `0addfef0-023f-41fd-bf16-570ebb810984` |
| Focused execution persistence | PASS 624 |
| Focused route executor, including capacity/lifecycle | PASS 1573 |
| Full 13-suite attempt | FAILED; no artifact captured or deployed |
| Full-attempt diagnostic core / route / persistence / finite actions | PASS 2263 / 1573 / 624 / 693 on MVID `416fb731-0f4f-4f0a-b4a5-d6a18ce1ec9b` |
| Whole-worktree whitespace check | PASS |

Logs are `%LOCALAPPDATA%/Temp/opencode/adventure-review-{red,green,routes,full}-20261023.log`.
All native commands used `checks/VerifyExecutorOffline.ps1` and its shared mutex.

The full attempt failed in existing fixtures reflecting removed manager APIs:
`PresetIntegration/Program.cs` shared-support checks, CombatPrep phase-support
checks, and the shared ProtectedTiming/StageSupport fixtures used by BlackBar and
OrdinaryPolicy. `CurrentLock`, `LockType`, and related removed-manager behavior
need fixture migration to actual execution authority, not production compatibility
shims or null guards that silently disable negative tests. These failures are
outside the two changed review functions. Nine other native suites passed.

No game restart, injection, bridge command, save, live review, or configuration
mutation was performed in this investigation. The live game remains blocked on
its existing assembly. Finish the fixture migration and a fresh complete release
gate before a save-backed deployment and explicit route review. Preserve the
user's current paused-plan/dashboard choice; do not restore an older plan setting
from historical monitoring instructions. Live Walderp kill and resumed farming
remain unverified.
