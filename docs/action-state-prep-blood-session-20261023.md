# Prep And Blood Session State

Implementation notes for the prep/blood/rebirth ownership slice of
`action-state-simplification-20261023.md`.

## Runtime Contract

- Fight preparation retains one typed session attempt: requested identity, acquired
  options/selection, original fruit objects/counters, consumed fruit steps, and boss
  admission status. There is no action-record collection, preparation journal, or
  export/import path. Manual rebirth requests are session-only.
- Preflight rejection leaves an uninvoked step retryable. An invoked unverified
  fruit cancels its bounded preparation. A later ripe fruit is not consumed again
  while that preparation waits for boss admission. Native-busy fights block setup
  before gear/support/recall, including a fight appearing during selection.
- Pending manual fight preparation retains its acquired request across admission
  denial and advances without requiring the optional WinForms status form.
- Pit, spin, and threshold spells retain only their current native opportunity's
  attempted marker. Native counters/cooldown origins remain authoritative; an
  ambiguous attempt is not a permanent action-kind/dependency quarantine.
- Prep, pit, and final rebirth do not purchase diggers. The ordinary coordinator
  owns the sole purchase opportunity. `MoneyPitThreshold` is only minimum feed
  eligibility, not a purchasing reserve.
- Rebirth accepts native committed counter/time proof even when the old grant is
  retired by that rebirth. Challenge and normal engagement share one pass-local
  attempt. `Invoked`, not preflight denial, consumes that attempt.

## Iron Pill Checkpoint

One fixed `iron-pill-window-v1.json` stores owner (plan), native rebirth number and
clock origin, original start/deadline, acquired save seconds/minimum worth,
`retryNotBefore`, and phase (`starting`, `saving`, `closed`). It is not an action
journal. Loading validates the complete object and bounded targets/backoff.

`starting` is saved before NUMBER flush. A verified flush changes the in-memory
phase to `saving` before saving that checkpoint. A failed second write cannot
reflush in this session. Restored `saving` resumes the original timer/reservation
without NUMBER entry; restored `starting` cancels locally for 900 seconds. Cast,
timeout, cancellation, and plan changes retire the in-memory window before
closure/history/UI work. Failed closure storage cannot recast in memory.

Legacy `finite-actions-v1` files are never parsed or rebound. If that directory
exists and the new fixed checkpoint does not, one conservative closed window
backs off Iron Pill alone for 900 seconds. No legacy record blocks EXP,
inventory, startup, or ordinary recurring work. No universal exactly-once promise
is made for arbitrary game-save/sidecar rollback or crashes during failed IO.

## Integration Seams

- `RunFightPrep(plan, consumePowerFruits, summaryPrefix, onBossPushStarted = null,
  opportunity = null)` has no digger-purchase option.
- Stage entry retries should capture their live attempt identity and use
  `FightPreparationCanRetry(identity)`, with the existing scheduler/entry/epoch
  guards. Keep the persisted consumed stage-entry marker; do not restore prep.
- Call `BeginRebirthAutomationPass()` once at the start of each ordinary pass.
  Stop that pass when `ProcessPendingManualRebirth()` returns true. Also honor
  `RebirthAttemptedThisPass` after a normal/challenge native attempt. Do not clear
  that stop signal while retiring the old epoch inside the same pass.
- The removed helpers are `ExportFightPreparationCommitment` and
  `ImportReviewedFightPreparation`; persistence/ingress must not recreate them.
- Existing mixed native-action/persistence fixtures still using old records or
  result durability need their respective owners' updates. The prep-specific
  `CheckActionPreparationIdentityRegressions` keeps its existing dispatcher entry;
  the blood checks keep the existing `blood` selector. No shared Program or
  repository manifest was edited in this slice.

## Verification

Source-linked tests live in `checks/ActionSessions/` with separate prep and blood
native/controller/setup boundaries. The production session orchestration,
finite runner, typed leaves, and real temporary checkpoint IO run unchanged.
They are not native-controller acceptance or production build evidence.

The checked-in source projects are `checks/ActionSessions/PrepSessionChecks.csproj`
and `checks/ActionSessions/BloodSessionChecks.csproj`. Run each with
`dotnet run --project <project> -c Release`; both are included in `verify-ui.ps1`.
Their intermediates/output are isolated by project name.

Verified source-unit results: prep/rebirth 66 assertions, blood 61 assertions.
The updated native `ActionPreparationCommitmentChecks.cs` and
`IronPillReservationChecks.cs` require the later coordinated injector/native check
build; none was run during parallel writes. No deployment, live game action,
index change, or commit was performed.
