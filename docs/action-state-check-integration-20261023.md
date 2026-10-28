# Shared Check Integration

## Wiring

- `PresetIntegrationChecks --repeatable-purchases` dispatches
  `CheckRepeatablePurchaseActions()`. The full PresetIntegration run also calls it.
- PresetIntegration, CombatPrepIntegration, and ManualAdventureOverlay manifests
  compile the shared `RepeatablePurchaseChecks.cs` partials. Both native game
  loaders call `Program.ConfigureRepeatablePurchaseGameFixture` after the existing
  finite-action hook. The hook observes actual native digger prices/invocations;
  native upgrade and gold-accounting bodies remain intact.
- `OwnershipChecks inventory-rescan` and the full Ownership run call
  `CheckInventoryRescans()`. Its manifest compiles `InventoryRescanChecks.cs`.
  `PrepareGame` configures its scene adapters after retaining native `swapHead`;
  `PreparePlugin` configures the native inventory leaf/result observers.
- `PresetIntegrationChecks --material-storage` isolates approved-material metadata
  retry without bypassing its real native inventory effect or the full-suite call.
- Preserve the concurrently added NGU startup and ordinary-tower authority checks,
  including their compile links in CombatPrepIntegration and ManualAdventureOverlay.
- No production manifests or runtime implementations are changed by this slice.
  Other agents own repeatable-purchase, inventory-rescan, preparation, persistence,
  and route check implementations; their files are linked, not replaced here.

## Contract Map

| Retired Assertion | Current Coverage |
| --- | --- |
| Result durability authorizes native success | `FiniteActionNativeChecks`, `MoneyPitRecoveryChecks`, and `NativeFightFixture` use invocation, exact native commitment, and current authority. `Verified` means `Committed && AuthorityCurrent`. |
| Lost result writes quarantine subsequent blood actions or rebirth | `CheckNativeActionHistoryIgnored` holds malformed/locked old records and a non-directory ledger path while current NUMBER actions and real final setup verification succeed. Old files remain unchanged; no rebirth is invoked by setup proof alone. |
| UNKNOWN is a persistent dependency/type ban | `CheckNativeUnverifiedRetry` rejects unexpected arithmetic, then admits a fresh capture from actual blood/NUMBER state. Healthy Energy still allocates the grown pool with the original basic-training accounting. Inventory rescans exercise fresh identities/locations and stop the stale outer batch. |
| Same identity implies reusable historical credit | Stale NUMBER/EXP captures report no invocation or commitment. Fresh native captures can progress without action history. Exact stat/currency and NUMBER arithmetic remain asserted. |
| Digger leaf construction supplies a reserve and omits captured context | Shared purchase coverage calls `PurchaseActionDiggers()` with real native unlocks and grants. It asserts one maximum increment and exact native debit per pass, including the next pass after render failure. The dedicated repeatable suite retains changed-price rejection and fresh retry coverage. Both direct EXP fixture captures set `Model`. |
| Pit result/import records suppress all later opportunities | `CheckPitSessionFaults` keeps a bounded same-opportunity attempt marker, admits a new native opportunity with verified material, and ignores malformed/locked retired storage. Unknown or revoked material still requires the original scoped gear authority guard. |
| Ordinary fruit UNKNOWN blocks every later harvest/rebirth pass | Native checks establish that the interrupted tooltip follows timer consumption but precedes the harvest counter. `YggdrasilChecks` preserves that partial result, performs no replay while the timer is zero, and admits a genuinely ripe later opportunity using its actual seed formula. Rebirth readiness does not invent a missing counter or wait on old action history. |
| Preparation records/selection writes must precede boss admission | `StageSupportChecks`, `ProtectedTimingIntegrationChecks`, and shared native preparation checks inspect the live typed attempt, selected setup, real claim/grant and native Fu activation. A locked retired prep file cannot block the same session's retry or cause another gear search/boss process. |
| Accepted-command sidecars establish deduplication | `NativeFightFixture.Request` verifies the retained `.claimed` command, consumed input, cleared dispatch scope and unchanged result/attempt/process after a duplicate command ID. It requires no retired `.accepted.json` action import. |
| OS pin files authorize execution or quarantine later requests | Combat fixtures check current native setup grants, bounded session invocation, cancellation on uncertainty and preserved Wandoos work across coordinator replacement. Valid, malformed and exclusively locked old OS files are inert. These groups are included in the passing full CombatPrep suite below. |
| Coarse boost stubs may approve observed material | Preset boost, capacity, inline-stage and metadata-retry fixtures use actual native boost/deletion bodies through `RunInventoryLeaf`. Only scene refresh/RNG boundaries are substituted. Metadata failure expects `RescanRequired` while the native result remains verified; fixtures no longer seed policy material or ownership. |
| Cancelled setup wall-clock expiry advances an unexpired stage | `CheckAutomaticTransitionSaveFailure` checks unchanged entry/start/deadline, completion flags and earned credit, plus zero positive paid AT/AUG/W writes both immediately after cancellation and just before the original deadline. Only then does it exercise locked-checkpoint transition failure at the actual original deadline. |
| Cleanup retries must continue despite a legitimate same-entry successor | `CheckCleanupCadence` starts deferred terminal release at its original deadline and proves no resource successor exists. Its exact retry-count, no-replayed-accounting, deduplicated-warning and still-owned native-timer checks remain intact. |
| A missing overlay base receipt is dereferenced before acquisition evidence is visible | `CheckManualAdventureOverlay` now requires a real initial receipt before accessing its policy. Failure emits existing scheduled-acquisition diagnostics and reports status, resource outcome, waiting reason, actual exception, native zone and ordinary-route observation. It neither seeds route authority nor adopts a foreign route. |

Native pit tier/selector/RNG boundaries, cumulative milestones, same-ID/full-bag
staging, exact rewarded instances, CURRENT-successor selection and no historical
restore flashes remain. Foreign gear, resource bars, income, model, epoch, borrowed
input and lifecycle checks remain; no grants or ownership results are fabricated.
Earned stage progress/checkpoint durability and command-ID deduplication are not
replaced by finite-action history. Iron Pill's single bounded checkpoint remains
with the blood/preparation implementation and existing native checks.

## Native Verification

After coordinator GO, every build/run used `checks/VerifyExecutorOffline.ps1` and
its named mutex. Logs have unique names under
`C:\Users\Kevin\AppData\Local\Temp\opencode`; none was overwritten. Production
source was not edited. No live action, deployment, index or commit operation ran.

The final combined run on 2026-10-24 passed all four owned full suites in one
mutex acquisition and against one freshly built production artifact:

- Production MVID: `4101a7f4-0990-4bd6-9850-406692144bf8` (all four loaders report it).
- Log: `shared-native-ordinary-route-four-diagnostic-20261024-024142-b3ea82aad21643beb4f3539ab6165683.log`.
- Command: `checks/VerifyExecutorOffline.ps1 -Suite @('PresetIntegration', 'CombatPrepIntegration', 'BlackBar', 'Ownership')`, with explicit game path and the unique log above.
- Verifier exit: zero. No selectors or failed-suite fallback diagnostics were needed.

| Full Suite | Assertions | Scenarios |
| --- | ---: | --- |
| PresetIntegration | 20402 | Full suite |
| CombatPrepIntegration | 12587 | Full suite |
| BlackBar | 24758 | Full suite |
| Ownership | 10500 | 511 |

These totals already include their component checks and must not be added to
focused/component counts. Counts are retained run evidence, not fixed future
documentation contracts. PresetIntegration takes about 12.5 minutes on this
workspace; use a terminal timeout longer than ten minutes for its full run.

## Overlay Follow-up

The reported initial-base NRE on the coordinator's older `0f218456...` artifact
did not reproduce on the current tree. Only acquisition diagnostics were added
in this follow-up; no production behavior or fixture route initialization was
changed. The initial native Safe Zone fixture and the actual receipt/ownership
requirements remain in force. This is current-artifact verification, not a claim
that the root cause of the older artifact has been established.

`--manual-overlay` passed 280 assertions on MVID
`0c39efd2-ddde-440d-bd0a-31815a87152c`, with log
`shared-native-overlay-initial-diagnostic-wait-20261024-023408-25f9013ad766423f9e052facbc413b12.log`.
The subsequent full-order four-suite run above also passed, including both new
startup/tower groups and the existing foreign-route ownership guards. The first
focused command timed out waiting for the coordinator's mutex, before any build
or log was started; it was not counted as a test failure or a pass.

All ordinary-route/startup production changes and the coordinator's shared
manifest additions were preserved. No ordinary-route observation was reset or
fabricated to make these checks pass.

## Earlier Evidence

Earlier logs remain intact:

- `shared-native-original-budget-four-green-20261024-014952-9c2ec7b0b4734d098eb9f4ce959e583a.log`: previous four-suite pass on MVID `06916f0a-bd2d-428a-92cf-7f6572d6141e`, before the concurrent ordinary-route/startup work.
- `shared-native-original-budget-full-20261024-011742-1bfe175ec19247889bfad3c04c5d6849.log`: terminal timeout; incomplete, not a pass.
- `shared-native-original-budget-full-long-20261024-012853-2c6cdb06278e45609abb6df9a69de88a.log`: MVID `fa8e61da-bae8-4a8c-8a56-d938ca35cc80`; three full passes, then the stale cleanup-cadence fixture identified in CombatPrep.
- `shared-native-terminal-cleanup-20261024-014527-0608f7e9b7b4459b83352d44947b1a19.log`: focused cleanup recovery passed 165 assertions on MVID `5deba771-6c4a-46e5-9d3f-f11704310ea2`.
- `shared-native-combat-full-budget-20261024-014604-324cddbc3a164a15b9002c487f0ede2a.log`: full CombatPrep passed 12587 assertions on MVID `5d00cd00-3d27-4547-847d-a3f0b5afc335` before the final consolidated run.

## Remaining Gates

No new product bugs or remaining failures were found in these four full suites.
The earlier material-array, native Wandoos loader, inventory-rescan, route and
execution-persistence blockers were resolved by their respective owners and are
covered by the final full passes. The latest follow-up changed only the shared
overlay acquisition diagnostics and this report. The earlier shared expiry and
cleanup fixture corrections remain intact.

The coordinator still owns the remaining source/native/frontend gates and the
complete one-artifact acceptance run. The concurrently migrated SustainedFocus
legacy-lock source fixture was not edited or worked around here. Passing these
four full suites is not a claim that all 13 native suites or all other gates passed.
