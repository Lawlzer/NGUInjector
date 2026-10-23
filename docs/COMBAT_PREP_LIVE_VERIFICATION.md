# Combat Prep Live Verification

Historical live evidence for the implementation tested below. See [Automation Improvements](AUTOMATION_IMPROVEMENTS.md) for later integration fixes and their separate offline verification boundary; this record's original test facts are unchanged.

## Scope

Verified on October 16, 2026 against the installed Steam game, using the rebuilt Release injector and native desktop UI. The player was in Normal difficulty using Wandoos 98. These observations validate this captured state; they do not establish identical forecast accuracy for every difficulty, OS, loadout, or progression level.

The planner is available under **Misc > Auto Augments + Wandoos**. Its default total budget is 7200 seconds, with independent 3% background energy and magic limits. Preview is read-only; Start is explicit. AT preparation is included in the budget, and actual AT targets gate later Wandoos allocations.

## Completed Two-Hour Run

Run ID: `be27b82d1c5b4ee58dd01fce8dc16d8b`.

| Phase | Budget | Policy |
| --- | --- | --- |
| Wandoos AT preparation | 900 seconds | 25% of preparation energy to AT-3, 75% to AT-4; no Wandoos allocation |
| Augments | 3150 seconds | Dedicated augment gear; budgeted magic Wandoos after AT completed |
| Wandoos | 3150 seconds | Dedicated Wandoos gear; both resources capped where possible |

- Completed at the original 7200-second wall-clock deadline.
- Actual AT levels reached the targets: energy-side 1055, magic-side 1291.
- Final Wandoos dump levels: energy 208731, magic 222175.
- Predicted combined Augments x Wandoos multiplier: `8.78638165053966e21`.
- Observed combined multiplier: `8.786947800606195e21`.
- Observed result was **0.0064435% above forecast**.
- The settings file's SHA-256 was unchanged throughout the run.
- Both Wandoos allocations remained zero throughout preparation; magic Wandoos began only after the actual AT targets were met.
- Native automatic-nuke timer was restored to its original value. The saved automatic-nuke preference was not changed.
- The existing `24hr` plan resumed at stage index 5 after completion.

### Alternatives At That Starting State

These are equal-horizon forecasts from the same initial capture, not separate completed two-hour experiments. Gains are relative to that capture's combined Augments x Wandoos bonus.

| Evaluated policy | Projected combined gain |
| --- | --- |
| AT, augments with light magic Wandoos, then dedicated Wandoos | 27.9480x |
| Augments, AT, then dedicated Wandoos | 26.0947x |
| Augments, then dedicated Wandoos without extra AT | 25.4523x |
| AT, then dedicated Wandoos | 20.2647x |
| Dedicated Wandoos without extra AT | 15.6069x |
| Augments, then NGU with budgeted Wandoos | 8.3614x |
| Dedicated augments with budgeted magic Wandoos | 7.8880x |

After completion, a fresh native-UI preview recommended a different policy, Augments then Wandoos. That is a new forecast from the improved state, not a replacement for the completed run's original prediction.

## Short Live Checks

| Check | Outcome |
| --- | --- |
| 120-second AT then Wandoos | Completed; actual combined result 0.007324% below forecast; no W during AT |
| 120-second augments with budgeted magic Wandoos | Completed; actual combined result 0.372408% above forecast |
| 60-second NGU with budgeted Wandoos | Completed; reported final combined multiplier matched forecast |
| Explicit cancellation during AT | Cancelled; targets/resources released; settings unchanged |
| Native boss combat preemption | Run cancelled safely, exposing the automatic-nuke interaction described below |
| Native Misc UI | Defaults, alternatives, owned phase gear, scientific stat formatting, completed run and exact observed allocations verified |

## Native Automatic Nuke Interaction

Early long-run attempts were interrupted by the game's automatic-nuke path, which runs outside injector automation loops. A failed native nuke can briefly set both fight flags even without a useful fight. The original combined ownership guard correctly yielded, but that made long preparation unreliable.

Combat prep now leases and pauses the native automatic-nuke timer while it owns training. It does not change the user's auto-nuke preference or clear fight flags. Manual combat still preempts the run. Cleanup restores the original timer only while its exact instances/epoch remain owned and no conflicting activity has taken over. Native-update regression checks cover two virtual hours; the completed real two-hour run verified this path in practice.

## Evidence And Reproduction

Machine-local reports are under `%LOCALAPPDATA%\Temp\opencode`:

- `combat-prep-live-20261016-191147-9d8864`: successful full run, original preview, start-time replay, samples, final runtime, and summary.
- `combat-prep-live-20261016-175315-2c46d5`: short AT/Wandoos test.
- `combat-prep-live-20261016-175530-c1e145`: short augment test.
- `combat-prep-live-20261016-175731-fe75d3`: background test.
- `combat-prep-live-20261016-175832-bda024`: cancellation test.
- `native-final-be27-completed.png` and `native-final-be27-postrun-alternatives.png`: actual native UI screenshots.

The live script below is **not an offline test**: without `-PreviewOnly`, it explicitly starts training and can spend gold. It uses the workspace bridge, records reports, checks zero Wandoos allocation before/during AT, and cancels only its own active run on failure. Do not run it during another manual fight, gear operation, or planner run.

```powershell
# Read-only two-hour comparison:
.\checks\CombatPrepIntegration\Invoke-LiveCombatPrep.ps1 -DurationSeconds 7200 -PreviewOnly

# Short actual training and cancellation checks:
.\checks\CombatPrepIntegration\Invoke-LiveCombatPrep.ps1 -DurationSeconds 120 -Family 'AT then Wandoos'
.\checks\CombatPrepIntegration\Invoke-LiveCombatPrep.ps1 -DurationSeconds 120 -Family 'AT then Wandoos' -CancelAfterSeconds 5

# Actual two-hour execution of the current recommendation:
.\checks\CombatPrepIntegration\Invoke-LiveCombatPrep.ps1 -DurationSeconds 7200
```

## Offline Verification

`verify-ui.ps1 -IncludeInjector -GameFolder <installed game>` includes the pure planner and compiled-backend integration suites. Final verification passed the frontend checks/build, Rust tests/build, external UI, sustained allocation, protected timing, black-bar/gear checks, and preset checks. Focused planner browser cases pass on desktop and mobile; the full browser suite passed 432 cases with four existing touch-only skips.

## Limits

- Search is bounded and coarse-to-refined, not globally optimal over every gear combination and schedule.
- Owned gear and future AT levels are projected; future loot, resource refill after a cap increase, and unmodeled NGU/support growth are not credited.
- Current beard/digger selections and digger levels are frozen, not optimized per phase.
- Bootup and other non-gear factors are disclosed capture-time approximations. Live rates are refreshed during execution.
- Automatic free AT from Wish 190 is explicitly unsupported; capture rejects that state rather than silently projecting frozen levels.
- Challenges and unsupported game schemas fail closed. Existing Wandoos work prevents an automatic OS reset; a fresh OS decision is pinned for the rebirth.
- Combined combat gain does not assign a universal value to NGU growth or Adventure Power/Toughness.
