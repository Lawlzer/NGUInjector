# Phase Support Verification

## Implemented Contract

- Quick focus is an indefinite manual override using the same matching standard stage preset, priorities, gear and support rules. Its estimate horizon is not an expiry or a request to run the timed Combat planner.
- Combat Prep maximizes terminal Augments x Wandoos within the entered total budget, including setup and preparation. Budget shortcuts are 15 minutes, one hour, two hours and 24 hours.
- Current support and deterministic phase-planned support are evaluated on owned phase gear. Legal beard selection, current stored levels, purchased digger maxima, slots, Gold income and drain are captured and projected by the shared support core.
- Exact pinned support is checked before mutation, applied before resource funding, and verified through native effects and list/flag/level observations. Fresh replay does not resize a cached selection.
- Cleanup restores the original selection and owned touched inactive levels only while the run still owns the support state. Partial writes, foreign edits, replaced objects and changed epochs cannot be adopted as successful restoration.
- Support levels and unmodeled future growth remain fixed forecast assumptions. This is a bounded policy comparison, not global optimization over all support allocations or account progression.

Implementation and native formula/ownership details: [Shared Support Contract](support-core.md). The October 16 two-hour run is [historical evidence](COMBAT_PREP_LIVE_VERIFICATION.md), not a new phase-support test.

## Live Environment And Preparation

Verified October 17, 2026 against the installed Steam game, Normal difficulty, Wandoos 98. The save/settings were backed up under `%LOCALAPPDATA%\Temp\opencode\phase-support-backup-20261017-103658`. Automatic rebirth was disabled before testing; competing activities were temporarily paused. The rebuilt injector was loaded by a graceful restart/cloud-load workflow after a successful quicksave. The rebuilt native desktop UI was also loaded.

Gold beard was not available in the observed live selections. Gold activation/deactivation and associated digger affordability remain native-backed offline tests, not a claimed live unlock/activation test.

## Real Training Results

All completed runs stopped at their original wall-clock deadlines. The live harness checked that every observed verified phase support selection matched its pinned beard IDs and exact digger levels, and that both Wandoos allocations stayed zero before/during planned AT.

| Test | Outcome | Combined A/W forecast error |
| --- | --- | --- |
| 120 seconds dedicated Wandoos with planned support | Completed; exact owned support restored | +0.0007171% |
| 300 seconds AT, augments with background magic Wandoos, dedicated Wandoos | Completed; actual AT targets gated Wandoos; exact owned support restored | -0.0042670% |
| Planned Wandoos cancelled after about 5.5 seconds | Cancelled; exact owned support restored | Not compared with an unfinished full-horizon forecast |
| 60 seconds NGU with 3% background energy/magic Wandoos | Completed; no support mutation needed | -0.0000011144% |

The settings file was unchanged during each controlled run. Quicksaves preserved the resulting game progress.

### Three-Phase Run

Run ID: `4b7d7ba6127d4fbc975fdc6ebf8dab88`.

| Phase | Budget | Support |
| --- | --- | --- |
| AT | 7.5 seconds | Current selection; AT energy split 75% / 25%; zero Wandoos resource allocation |
| Augments | 219.5 seconds | Ladybeard; Drop/Adventure diggers level 32, Magic NGU digger level 12; budgeted magic Wandoos after AT |
| Wandoos | 73 seconds | Ladybeard; Drop/Adventure diggers level 30, Wandoos digger level 34 |

Both running phase selections retained the legal core beards and Fu Manchu within the available five slots. The planner did not assume locked Gold beard activation.

- AT targets reached: energy 1060, magic 1292.
- Final Wandoos dumps: energy 218376, magic 231970.
- Predicted combined multiplier: `1.0186256975174933e22`.
- Observed combined multiplier: `1.0185822326140042e22`.
- Augment-phase net GPS: approximately `7.397689e17`.
- Wandoos-phase net GPS: approximately `8.836256e17`.

Native UI Automation and actual native-window screenshots confirmed planned versus observed support during Augments and Wandoos. The short AT phase was captured in runtime telemetry, not in a native screenshot.

### Restoration Versus Later Rotation

Restoration is a verified historical operation, not a promise that normal automation will leave that set unchanged. The exact cleanup path compares the native all-level/selection state with the captured original before reporting `restored`.

In this run, the log recorded completion at 11:09 local time, immediately followed by a successful ordinary minute beard rotation (`inject.log` lines 333-335 at verification). That explains why a later observed beard set differed while the diggers had returned to their original levels. The UI now states this explicitly and shows current observed support independently. Its actual GPS usage is drain/gross, not the 100% validation ceiling used to inspect current selections.

## Live Quick Focus

The Quick test used a one-second estimate and waited six seconds after each activation. It passed 54 assertions without starting a new Combat run or triggering a timed transition.

| Case | Observed result |
| --- | --- |
| Energy NGU | Remained applied; 88394672 energy and 792500 magic allocated to NGUs; NGU beard and both NGU diggers active |
| Magic NGU replacement | Correctly rejected gear that could not retain the existing energy allocations; Energy NGU ownership and allocations remained intact |
| Wandoos | Remained applied; 1605644 energy and 828074 magic in Wandoos; AT allocations zero; Ladybeard and Wandoos digger active |

The original 300-second estimate and Auto EXP preference were restored. Test focus was explicitly stopped before the normal plan handoff.

## Horizon And Transport Checks

Read-only live evaluations for 900, 3600 and 86400 seconds succeeded, each evaluating 492 policies and returning all 12 alternatives. Recorded calculation times were approximately 93 ms, 193 ms and 2646 ms respectively. These were forecasts, not full live runs at each horizon. A manual titan override active during these later previews remained subject to normal execution blockers.

Support-rich previews originally lost lower-ranked strategy families at the old 64 KiB feature cap. Combat Prep now uses the native bridge's existing bounded 256 KiB complete-action limit; the frontend consumes the advertised preview ceiling. The complete pretty UTF-8 envelope, runtime and maximum wrapper identifiers are measured. Duplicate-family extras are trimmed before family winners only if necessary. A 121345-byte native-backed regression retains all 12 alternatives. Older 64 KiB advertisements remain conservatively enforced.

## Evidence

Machine-local reports under `%LOCALAPPDATA%\Temp\opencode`:

- `combat-prep-live-20261017-104950-afdc11`: dedicated planned-support Wandoos.
- `combat-prep-live-20261017-110425-6558c9`: three-phase run, full preview/start/samples/final/summary.
- `combat-prep-live-20261017-111851-430c8c`: explicit cancellation.
- `combat-prep-live-20261017-111857-26d05e`: NGU/background run.
- `native-readonly-20261017-110430-*`: native Augment/Wandoos screenshots and correlated telemetry.
- `phase-support-final-20261017-112833.json`: first verified normal-plan handoff. Manual controls can supersede this state; final confirmation is recorded separately below.

Quick evidence is in the workspace's `temp/quick-live-20261017-112407.json` and `.summary.json`.

## Offline Verification

The normal verification command and native UI Release build passed. Latest results include 1363 frontend tests, 98 Rust tests, 458 desktop/mobile browser passes with four existing skips, 5530 pure Combat assertions, 1222 pure support assertions, 4668 compiled Combat assertions, 315 compiled preset/support assertions, 382 ownership assertions and 2017 sustained-focus assertions. Existing allocation, gear, beard, policy and scheduler suites also passed.

Native support checks exercise float and double effects, cubic Stat digger behavior, forward Gold GPS, native exact-long input including zero, all-level restoration, partial failures and foreign-state rejection. Visible digger-menu text-refresh effects and borrowed pod drafts are checked offline; this does not claim full live rendering/typing coverage for every game menu.

## Final State Requirement

The existing `24hr` plan was enabled at Energy NGU (zero-based stage 5) with automatic rebirth disabled, no Quick/test owner, and no pending test run. The finalizer uses fresh plan revision/entry metadata and verifies actual allocations after the normal pass, rather than treating an acknowledgment as proof. Normal Adventure/Auto Boost/Auto Pit/Auto Blood preferences were restored, and a quicksave preserved progress.

The last finalizer report is `%LOCALAPPDATA%\Temp\opencode\phase-support-final-20261017-115551.json`, with a fresh runtime assertion at 11:56:01 local; it supersedes earlier handoffs followed by manual overrides. Independent native UI verification at 11:52:17 had confirmed the same requested configuration: `24hr` RUNNING, Auto-rebirth Off, Quick None, and `Sustaining: 6. Energy NGU`. Final observed allocations were 88742682 energy on Adventure Alpha and 3042500 magic on Yggdrasil. NGU beard 3 and Energy/Magic NGU diggers 4/5 were active. Optional beard rotation continued normally.

Native screenshots: `%LOCALAPPDATA%\Temp\opencode\native-readonly-dashboard-20261017-1151.png` and `native-readonly-preview-20261017-1151.png`. The UI returned all 12 support-rich alternatives in a read-only preview and was left on Dashboard. Manual controls intentionally remain able to supersede the plan after verification.
