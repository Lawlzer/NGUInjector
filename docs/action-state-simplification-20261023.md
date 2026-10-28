# Action State Simplification

## Status

COMPLETE, verified offline on 2026-10-24. This work supersedes the persistent
finite-action replay/UNKNOWN policy described in earlier executor/review records.
No live game actions or deployment were performed.

## Approved Behavior

- Diggers get one independent purchase opportunity per ordinary automation pass.
  Buy at most one native maximum-level increment: affordable unowned diggers first,
  then the cheapest eligible upgrade, with deterministic ties. Remove the gold
  reserve; keep the existing purchase on/off permission and native unlock gates.
- EXP purchases replan from current native balances/stats each ordinary pass.
  A failed or unverified attempt stops its current batch, not future automation.
- Inventory mutations stop using stale snapshots after an unverified result and
  resume only after a fresh inventory scan. Protect retained items and foreign edits.
- Remove the per-action disk ledger and permanent UNKNOWN dependency blocking.
  Keep direct native preconditions, reentrancy/ownership checks, and small session
  state for actual one-shot steps; do not replace the journal with a database.
- Cancel interrupted bounded preparation, titan, and tower attempts on restart.
  Ordinary automation resumes from current native state without importing old
  grants, cleanup authority, or action-history quarantine.
- Arbitrary combinations of rolled-back game saves and sidecars are out of scope.

## Persistent State Boundary

Keep user settings, plans/preset definitions, earned stage progress and consumed
stage-entry markers. Keep command-ID deduplication separate from native action
history. Preserve a bounded Iron Pill saving checkpoint if needed for the original
timer and one-time entry flush; never create one action file per tick or purchase.
Audit acquired-selection snapshots separately from attempt journals, preserving
promised current-stage/frozen-selection behavior without requiring action replay.

Retired action/route/preparation records must not block startup or recurring work,
even when malformed or unwritable. Do not delete user configuration or fabricate
earned training while removing those records.

## Implemented Design

`FiniteActionLedger.cs` and its per-action disk writes/import/dependency checks
are removed. `RunFiniteAction` is now a stateless native boundary with only a
reentrancy latch. Its result records this call's invocation, observed commitment,
and current authority; it neither journals results nor bans a future action type.
An unverified result stops the current batch or bounded attempt. The next ordinary
purchase pass or inventory scan observes current native state and decides again.

The ordinary 10-second loop has the only digger purchase call, before prep/stage
early returns. Prep, pit and rebirth no longer add hidden purchases. Exact-price
gold is accepted, unaffordable unowned rows do not starve an affordable upgrade,
and callback/reentrant attempts cannot cause a second purchase in that pass.
`UpgradeDiggers` remains the spending permission. `MoneyPitThreshold` remains
only the independent Money Pit feed eligibility threshold, never a digger reserve.

Compatible verified digger growth updates pinned prep rates and renews affected
route grants through bounded exact evidence, without changing active digger
levels, acquisition, death limits, phase targets or original deadline. Foreign
changes remain rejected. EXP aborts its old plan after an unverified purchase.

Inventory distinguishes no eligible work, verified application and rescan-required
outcomes. A rescan requirement unwinds the entire outer batch; later work cannot
reuse its stale slots/donors. Only a verified leaf can approve the exact target's
material revision, replacing rather than mutating the old witness array.

Interrupted bounded routes and preparation are session-local and cancelled after
restart. Existing native fights still protect their prerequisites until idle.
Cancelled automatic prep runs only safe background work until the original stage
deadline; it cannot reacquire paid AT/Augment/Wandoos work or reset the budget.

## What Still Persists

| State | Purpose |
| --- | --- |
| Settings, plans, preset library and migration receipts | User configuration and deletion/import intent cannot be reconstructed from native game state. |
| One stage-progress checkpoint per plan | Earned/applied time, entry/cycle, completed/skipped work, original deadlines, consumed entry fights and necessary frozen stage choices. Native levels alone do not reveal this history. |
| One `iron-pill-window-v1.json` | Original saving start/deadline, acquired targets, context, retry time and starting/saving/closed phase. Restored saving does not repeat the entry NUMBER flush. |
| Command-ID claims and results | An explicitly delivered UI command is not executed twice after transport retries. This is separate from recurring native purchases. |
| Existing bounded pill/rebirth histories | Optional user-visible reporting; never action admission authority. |
| Explicit SaveSafety/test data | Original native save/configuration bytes and restore metadata for deliberate safety/testing workflows. |

Manual/Quick/dashboard/focus execution selections and transient action/process
steps are memory-only. Runtime snapshots and logs are UI/debug output, not action
authority. Old action, route, prep, OS-pin and standalone-selection files are
inert and excluded from new save-test captures; existing immutable bundles still
validate their original bytes before safely ignoring retired rows.

The only legacy action-directory check is one bounded Iron Pill migration delay
when no new saving checkpoint exists; it does not parse old records or block other
automation. Unknown resource/gear accounting remains a distinct low-level safety
outcome. Removing a disk action ban does not authorize overwriting foreign state.

No database is needed. The native game already owns currency, purchased stats,
levels, inventory, counters and cooldowns. Memory holds current orchestration;
small atomic files retain the specific user/progress facts listed above. There is
no universal exactly-once guarantee across arbitrary native partial failures and
game-save rollback, nor a new journal attempting to provide one.

## Verification Plan

Use source and actual native offline checks for unowned/cheapest digger ordering,
one purchase per pass, exact affordability, native unlocks, callback failures,
next-pass retries, fresh inventory rescans, native-busy restart cancellation,
Iron Pill timing/no repeated flush, and preserved earned stage work. Retain
resource/equipment foreign-state and command replay protections. Run the complete
source/frontend/Rust/native gates and an independent adversarial review.

No live game actions or deployment are authorized. Preserve unrelated generated
files and concurrent work. Commit only verified intended changes.

## Final Verification

- `verify-ui.ps1` passes: Svelte/TypeScript checks, 2,201 frontend tests, Vite build,
  Rust formatting, 182 Rust tests/compile check, and all configured source checks.
- Added checked-in PrepSession/BloodSession projects to that normal gate: 66 and
  61 source-unit assertions. Stateless action runner: 49 cases / 399 assertions.
- Save-test bundle checks: 125 cases / 4,374 assertions, including old retired-row
  integrity and exact original config/native/progress restoration.
- Browser: 884 passed, four existing skips, no failures on desktop/mobile.
- All 13 full native suites pass against ONE production DLL, MVID
  `d87f52b0-5cf6-4990-a95b-008678457b18`, SHA-256
  `C7C6491264E0764006AB41A24678F22F78715003204370063BFA71233C2853C1`.
- Application source fingerprint matched before/after final verification:
  `a514303234eec2eceb12af1649305e56c64d99b4`.
- Independent adversarial review found no confirmed remaining P1/P2 issue in the
  approved scope. It exposed two integration bugs that were fixed and regression
  tested: stale titan grants after owned purchase growth, and paid automatic-prep
  reacquisition after cancellation.

| Full Native Suite | Passed Assertions |
| --- | ---: |
| PresetIntegration | 20,402 |
| CombatPrepIntegration | 12,587 |
| ManualAdventureOverlay | 297 |
| BlackBar | 24,758 |
| Ownership | 10,500 across 511 scenarios |
| TitanGold | 1,497 across 71 cases |
| PresetEquipment | 94 |
| GearEquip | 20,805 |
| OrdinaryPolicy | 167 |
| WinFormsBlood | 174 |
| StageTargetsNative | 48 |
| ResourceAggregation | 4,646 |
| AutoExp | 5,169 across 119 cases |

Counts overlap and are not an additive coverage total. Focused purchase acceptance
also passed 64 scenarios / 697 assertions. Tests retain real native accounting and
grant checks while substituting unavailable scene/controller inputs; they are not
live Unity/long-running soak evidence.

Retained proof under `C:\Users\Kevin\AppData\Local\Temp\opencode`:

- `action-state-complete-a79d9bceecbe4026b0c14491e289d9f1.log`
- `action-state-verified-2a511348e110459a9c63792bdb8d2c15/manifest.json` and its
  captured tested injector/distribution and native verification log.

No commit was made: concurrent unrelated edits are mixed into the shared worktree.
No generated test output, unrelated files, staging or existing user changes were
included, reverted or committed by this task.
