# Rebirth Fruit Before Boss Prep

## Behavior

- Rebirth preparation snapshots the entire native fruit collection, object identities, and harvest counters instead of only Alpha/Beta.
- Automatic preparation processes every fruit with native `harvestTier >= 1`, even when the plan's legacy `PrepRequireMaxedFruit` option is true. Unripe and locked fruits are untouched.
- Alpha and Beta are force-eaten. Other fruits keep their native `eatFruit` preference and native reward calculations.
- Manual fight/stage-entry preparation remains fruit-free. Manual rebirth retains the maxed-only safety option.
- All fruit leaves run through the existing finite-action boundary under the verified exact fight setup, before `StartBossPush`. There is no extra Yggdrasil gear swap.
- A denied, uninvoked leaf can retry within the original attempt. A committed leaf is not repeated while boss admission waits. Collection, object, or pending harvest-counter changes cancel the original attempt.
- An invoked but unverified leaf cancels prep and withholds boss admission. Ordinary final-rebirth harvesting also refuses that same fruit object/counter in the same native epoch; it cannot silently retry the ambiguous prep leaf. A new explicit request alone cannot bypass this guard. It can acquire fresh preparation only after the original leaf's epoch, fruit object, or harvest counter proves the native opportunity changed. Unavailable fruit state is not such proof. This safety state remains session-local, not a durable action ledger.
- Explicit reset cancels the retained attempt without fabricating completion or clearing fruit uncertainty. Permission loss is separate: completed automatic prep remains completed, while unfinished prep safely retires its process and retains its acquired identity, options, selected gear/support, and spent fruit steps for resumption.
- Restored automatic permission reacquires exact route/setup authority only after native combat and the old claim have retired. It never revives an ended claim or harvests the same fruit step again. The same suspension rule applies when a boss tick observes permission loss before the readiness observer.

## Integration

`RunCustomPreRebirthPrepIfNeeded(CustomPlan)` and `RunFightPrep` signatures are unchanged. The former checks live automatic permission but not final readiness, so timed one-hour boss prep can run before final rebirth readiness. The timing stream's `CustomRebirthPreparationDue(plan)` and `Main.AutomationLoop` integration are retained. This lifecycle change does not bypass protected work, NUMBER, minimum-time, or challenge checks.

`ObserveCustomRebirthReadiness` retains the timing stream's due-prep condition. Permission off/on no longer destructively resets a valid automatic attempt: completed prep at 3600 seconds remains complete before the 3660-second final gate, and running prep can resume its original known steps. Native epoch, settings object, boss object, and plan file must still match. Legacy zero-threshold readiness loss while fighting retains its existing cancellation semantics; completed legacy prep remains retained through the fresh post-prep NUMBER confirmation window.

An unchanged uncertain fruit can intentionally keep final rebirth waiting. Resolve the native opportunity before issuing a fresh explicit request; neither reset nor permission toggling assumes the previous reward was absent. Foreign setup/resource evidence can still block safe process retirement and therefore resumption. No live-game or restart-persistence guarantee is added.

## Verification

`checks/PresetIntegration/RebirthFruitBeforeBossChecks.cs` covers native fruit ordering, timing thresholds, final-minute protected work, full-loop ordinary-harvest reservation, permission/uncertainty lifecycle, renewed NUMBER confirmation, native-editor cloning, and built-in deletion protection. The fixture observes real native consume/harvest entries and real `StartBossPush` entry. Production selection, authority checks, finite-action execution, harvest accounting, and reward calculations are retained; existing scene-render boundaries are reused.

Final focused verification passed **2,058 native assertions** on production MVID
`a4b89f19-cc40-4209-b60d-6855c999da58`. Full combat prep and manual adventure overlay
also passed after their shared fixture manifests included the new fallback checks.
The broader release gate still fails stage-handoff/support checks, as recorded in
`number-increase-plan-catalog-20261024.md`; no live deployment was performed.

Earlier follow-up verification used `checks/VerifyExecutorOffline.ps1 -Suite PresetIntegration -Selector --rebirth-fruit-before-boss` under `Local\NGUInjector-Executor-Offline-Verification`. The new regressions reproduced the completion, permission-reset, unconditional-uncertainty, and tick-before-observer bugs before their respective fixes. That intermediate run passed **1454 assertions**, including 1200 timed/lifecycle assertions. Log: `C:\Users\Kevin\AppData\Local\Temp\opencode\rebirth-prep-lifecycle-final.log`; production MVID: `e4f49cbb-3c46-4b0c-b2e4-8a010bd36a26`. No live actions, deployment, or commits were performed.

Earlier verification, before the timing/lifecycle follow-up:

Executed under `Local\NGUInjector-Executor-Offline-Verification`, rebuilding production and the native fixture through `checks/VerifyExecutorOffline.ps1`:

| Selector | Result |
| --- | --- |
| `--rebirth-fruit-before-boss` | PASS, 254 assertions, including 252 focused assertions |
| `--finite-action-cutover` | PASS, 586 assertions |
| `--preparation-ingress-entry` | PASS, 128 assertions |

`dotnet run --project checks/ActionSessions/PrepSessionChecks.csproj --configuration Release` also passed all 66 source-linked assertions. The linked CombatPrepIntegration and ManualAdventureOverlay fixtures both compiled successfully under the same mutex after adding their required manifest includes. `git diff --check` passed for the touched tracked files. Builds reported only the existing unassigned-field warnings for `GearOptimizerResult.MutationOutcome` and the shared fixture's `NativeGearSwapped`. No live game actions or commits were performed.

Focused reproduction:

```powershell
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration -Selector --rebirth-fruit-before-boss -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
```
