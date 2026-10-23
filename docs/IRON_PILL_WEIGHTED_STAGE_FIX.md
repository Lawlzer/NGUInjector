# Iron Pill Weighted-Stage Fix

## Diagnosis

On 2026-10-19, the live `24hr` plan was running its weighted `NGU Energy` row (index 3), with automatic rebirth, automatic blood spells and automatic Iron Pill enabled. The saving target was 900 seconds OR worth 100.

The injector log recorded saving starting at rebirth second 76560, waiting at second 77470 with `blood 0/100`, and timing out at second 77521. The existing `RIT` priority overlay applied only to inline stages. Weighted stages replayed their acquired preset's zero-blood policy instead, so saving disabled blood spending without funding production.

Auto-boost was a second blocker: it changed equipped item material without an equipment-update event, making the preset receipt historical/displaced and preventing subsequent allocation refreshes.

## Behavior

Current user configuration, updated after the verification below: `24hr` explicitly sets `IronPillSaveSeconds: 900` and `IronPillMinWorth: 0`. Saving is time-only (15 minutes); worth no longer permits an early cast. The settings were saved through the live revision-checked plan API and verified in runtime telemetry, without changing other plan fields, stage entry or automatic-rebirth preference. The 900-second OR worth-100 values below describe the earlier fix-verification run, not this later preference.

- Scheduled weighted allocation refresh temporarily borrows shared magic for one productive, sustainable blood ritual while Iron Pill reserves blood. This does not require positive saved Blood weight or a saved blood split.
- Dedicated Time Machine/Wandoos allocations, all energy allocations, gear, support, native AT targets, stage entry and earned history remain owned by the acquired preset.
- Ritual selection respects unlocks, native strict start affordability, liquid gold reserved for TM/Augments and strictly positive GPS after digger drain. It caps funding near the native black bar and can use a sustainable partial allocation. Gold-limited discrete fill plateaus return unused magic to eligible NGUs.
- The overlay is never stored in the acquired execution. Cast/cancellation restores the acquired split and ritual on refresh, rather than pinning the temporary blood policy. Dashboard/manual presets do not receive this automatic overlay.
- Automation-owned boosts of exactly owned equipped items trigger re-equip when AutoUpdate is on. When off, only the causally observed boost's material is accepted, and the same acquired decisions are revalued without search. Pending cached restoration is replayed before another boost. Arbitrary foreign/manual material changes still fail closed.
- Existing eligibility, cooldown lookahead, time-OR-worth, cast validation, timeout/backoff and automatic-rebirth gates are unchanged.
- Runtime Iron Pill diagnostics now include finite-or-null cooldown, blood and worth, minimum blood, exact-long ritual allocations and observed spell toggles.

## Offline Verification

Fresh normal production Release rebuild succeeded using the installed game assemblies. Recorded checks for this change:

| Suite | Result |
| --- | --- |
| Presets numeric | 2,176 assertions |
| PresetIntegration numeric | 1,905 assertions |
| Compiled native preset integration | 8,124 assertions, including 58 scheduled pill and 394 boost recovery assertions |
| Compiled blood | 469 assertions, including 369 reservation assertions |
| Compiled protected timing | 1,825 assertions |
| SustainedFocus | 2,043 main assertions plus its separately reported focused checks |
| ExternalUi | 9,260 assertions; two existing unused-field fixture warnings |
| UI Iron Pill and plan blood settings | 88 tests |

The compiled preset runner accepts `--iron-pill` and `--preset-boost` for focused regressions. These checks use isolated native/data fixtures, not the running game. They cover exact allocation/restoration, pinned policy preservation, no periodic optimizer search, own-versus-foreign material changes, waiting events, partially failing boosts and scheduled cached restoration. Numeric cases additionally cover gold-limited funding, exclusions and activity-gold preservation. External UI cases parse actual emitted JSON, including missing/nonfinite cooldown state and exact long ritual allocations.

## Live Verification

The injector was rebuilt and deliberately restarted after saving the game. Original runtime/settings, preset library, plan read response and logs were archived before testing. No game timers, unlocks, blood balances or Iron Pill thresholds were altered for the cast.

Observed on the actual `24hr` stage:

1. The scheduled preset acquired valid ownership at the existing stage entry `84d528949c7a417eb6cd526d242688db:8`.
2. Saving moved 7,568,056 magic into ritual index 5, leaving energy NGUs running. All three automatic blood-spending toggles were off.
3. Native blood production accumulated 18,000,000 through 108,000,000 blood. Worth reached 101.
4. At **2026-10-19 12:06:51 local / 17:06:51 UTC**, automatic Iron Pill cast successfully after **80.99848022524384 seconds**, at rebirth second **78977.31711742238**, with **worth 101**. History persisted the successful cast; native blood cleared and cooldown reset to approximately 41,400 seconds.
5. Reservation released, ritual allocations returned to zero, NUMBER auto-spending resumed and magic returned to NGUs (3,784,028 each in Yggdrasil and EXP at the first post-cast sample).
6. Subsequent real auto-boost/re-equip events retained valid scheduled ownership, advancing actual allocations rather than leaving the preset displaced. The stage entry and plan revision remained unchanged.

The final build additionally includes the cached-restoration boost deferral, covered by the scheduled native fixture. It was separately deployed and checked for normal ownership/cooldown continuation; the natural cast above preceded that isolated guard addition. No second cast was forced through the native cooldown.

Local evidence directory: `C:\Users\Kevin\AppData\Local\Temp\opencode\iron-pill-20261019`. It contains baseline data, command receipts, archived logs, sampled native runtime observations and final postcondition summary. Game saves in that directory are local evidence, not repository fixtures.

This establishes the reported scenario's native saving, production, cast, history and restoration cycle. It is not a claim that every possible account/unlock combination or the next full cooldown was observed live. The original plan settings and preset definitions are retained, with `24hr` running at NGU and the user's original automatic-rebirth preference restored.
