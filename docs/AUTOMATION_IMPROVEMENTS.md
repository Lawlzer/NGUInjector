# Automation Improvements

Current implementation follow-up to the [historical integration review](AUTOMATION_INTEGRATION_REVIEW.md). R01-R28 and follow-up fixes R29-R33 are implemented. The [active work list](../../temp.md) contains only remaining work and decisions; this guide preserves completed behavior and verification rather than reopening historical findings.

For the current automatic stage, mixed production/support search, forward bootup/Wish 190, precision policy and latest recorded live results, see [Automatic Combat And Mixed Focus](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md). Historical verification below is not a claim about the game's current final configuration.

## Current Behavior

| Path | Selection and execution | Lifetime |
| --- | --- | --- |
| Scheduled stages / Quick focus | Quick matches standard presets by gear/energy/magic intent and copies their priorities defensively; unmatched special focuses use declared defaults. Ordinary selectors use actual remaining pools, upfront bar affordability and the execution pair policy. Configured gear/support intent stays separate from temporary Blood/Iron-Pill allocation overlays. | Quick is a manual indefinite override, not timed Combat Prep. Protected stage credit requires valid application/entry receipts. |
| Combat Prep | Bounded timed AT/Augments/Wandoos search maximizes terminal A*W within the entered budget. Same-gear current versus deterministic standard phase support is included before comparison, with fixed captured levels and per-profile net GPS. Cached exact gear/support replay, actual target gates, one original deadline and once-per-rebirth OS choice govern execution. | Runtime overlay; cancellation respects exact gear, support receipts, targets, epoch and native-nuke timer ownership. Owned cleanup restores original active support and only owned touched inactive levels. |
| Automatic Augments + Wandoos stage | `ExecutionMode: 1` uses the same Combat engine with fresh start/eligible boundary/material GPS or equipment-change planning. Original elapsed budget includes entry fight, fruits, waits, planning and setup. Continuation retains actual gates, no-AT-after-W history, pools, OS and deadline. | Scheduled runtime overlay; safe cleanup retries are bounded, obsolete restoration permissions retire, and current intent reconciles automatically without manual acknowledgement. Ordinary saved rows are not batch-migrated. |
| Weighted presets | Six normalized retained-production goals, optional planned support portfolios (at most 64 policies, top-three beam), exact-policy feasibility replay and observed post-apply metrics. Immediate beard effects and candidate GPS precede digger/blood budgeting. | One-shot; normal beards may rotate afterward. Advisory sequences are not executable schedules. The 120-second preview token is not a sustain timer. |
| Temporary detours | Token/epoch-aware snapshots, preflight before mutation, explicit restoration/release and application invalidation. | Feature-specific Titan/Yggdrasil/fruit-cap/Money Pit/manual lifetimes, not a global owner framework. |

The later regressions also preserve generic AUG in a completed-but-held final stage using a rolling estimate without reopening AT preparation (R29), retain an existing Quick owner on read-only replacement rejection (R30), use the metadata gear goal for actual Magic NGU equipment (R31), and retain the original preset baseline/normalization while replaying fresh progress and reporting the applied candidate (R32). Gear map/epoch pairs and digger snapshots publish only after successful capture; Ygg acquires locks only after saving, and cleanup discards only its matching digger token (R33).

## Shared Utilities

All paths remain adapters around distinct objectives. The normal production manifest includes these helpers and embeds the shared policy; no temporary build imports are required.

| Utility under `NGUInjector/Managers/` | Responsibility |
| --- | --- |
| `ActivityPolicy` | Validated metadata from one JSON file: referenced priority sets, defaults, Quick intent, UI presets, preparation and support configuration. |
| `ResourceProjection` | Pure native-field E/M cap/power arithmetic, preserving float/double boundaries, clamps, rounding and post-clamp power potions. `NormalizeRaw` normalizes the ordered 14 raw resource specs once. |
| `ResourceProjectionCapture` | Game-dependent capture of native fields and ordered raw equipment specs, including offhand scaling; delegates normalization/projection rather than duplicating formulas. Combat Prep and presets share these numeric rules. |
| `ResourceAllocation` | `ShareCapped` uses exact weighted largest-remainder allocation with index tie-breaks for ordinary/preset sharing. `SplitPair` preserves Combat's fixed-pair rule: floor the second share, retain the first remainder, then redistribute within caps; zero-weight lanes stay excluded. |
| `DiggerAllocation` | Forward drain, affordable-level search and one deterministic affordability/backfill selection used by both ordinary weighted support and presets. It is not a global support optimizer. |
| `SupportProjection` / `Main.Support.cs` | Shared fixed-level support evaluation/planning, native-order Gold GPS and W/Stat effects, read-only capture, exact native level transactions and partial mutation receipts. Presets and Combat delegate to this [support core](support-core.md). Both sources are in the production manifest. |
| `LoadoutManager` / `GearOptimizer` | Complete exact-instance maps include explicit empties; composition retains unrelated unique occupants. Read-only preflight checks retained allocations through intermediate swaps, then application verifies the observed map. Legacy partial loadouts still leave omitted destinations alone. |

An application receipt is not merely a cached search result. Competing equipment/support mutation invalidates normal setup before protected credit resumes; stage identity, fight receipts and earned scheduler history are retained. A failed native swap can leave partial equipment, so failure does not imply rollback or successful application.

## Examples And Data

- An implicit Wandoos stage makes one energy-intent-based paid-AT decision for both lanes. Preparation orders come from `PreparationPriorities`; magic Wandoos cannot begin during that paid preparation. Wish 190 skips paid preparation and Combat now forecasts native unlocked free AT throughout all phases, including its evolving W speed, without a paid-resource charge or menu-target gate.
- A Magic NGU Quick request preserves non-owned energy/R3 allocations when the exact gear transition is legal. Read-only rejection leaves the previous Quick focus intact; failure after acquiring a successor is a different boundary and does not resurrect the old owner.
- An unaffordable top-ranked digger no longer consumes a slot while an affordable lower-ranked request exists. Planned Gold-beard activation changes candidate GPS before preset digger selection, not after ranking.
- A preset applied after natural progress replays the same cached items/policy with fresh feasibility and amounts. Its response compares the observed candidate with the original preview baseline and denominators, not a newly captured baseline.
- Quick uses normal matching preset priorities indefinitely, including overflow; existing gear/augment/allocation helpers still run. This introduces no timed optimizer into Quick. Combat's separate budget accepts 15-minute, one-hour and 24-hour horizons, rather than treating the two-hour default as policy.
- Combat's `Current` is actual original gear AND original support. Boss reporting keeps that fixed final setup (Mode B), not training support or newly optimized fight gear. No W lane is funded before any later AT phase or during AT; gear/support setup and polling consume the original deadline.
- Combat checks all used pinned phase selections before acquiring ownership and rechecks exact affordability before funding. It never resizes a pinned digger selection after income changes. Foreign support edits are retained, not overwritten by cleanup.
- Combat advances the native bootup ramp and completion-only set bonus, including a zero-speed fresh boot, using forward native-order W factors rather than dividing zero live speed. Bounded adaptive boundary refinement improves coarse family seeds; it does not establish a global optimum.
- Comparisons expose separate Energy/Magic allocation opportunity costs alongside W dump-equivalent output times. Preserve unknown flags and lane separation; neither metric invents NGU growth or universal NGU utility.

Edit `NGUInjector/Data/ActivityPolicies.json` for supported policy changes; see [Activity Policies](activity-policies.md) for field rules and examples. Existing explicit saved-plan priority arrays remain overrides and are not retroactively rewritten. The backend embeds the data and the UI imports it at build time: rebuild backend and UI, then reinject the rebuilt backend and reload the rebuilt UI. There is no runtime hot reload or fallback defaults file.

## Scope And Assumptions

Search remains bounded, not globally optimal over arbitrary gear/schedules. Projection uses captured native fields and parity checks, not inferred optimizer scores. It does not credit future loot or resource refill after a cap increase. Support requests remain subject to unlocks, slots, core-first beard policy and affordability; requested support is not proof of observed activation.

Questions reserved for later discussion, with no behavior change implied:

- Exhaustive joint gear/support optimization and future beard/digger growth. Current-versus-standard phase support at captured levels is already implemented; it is not an exhaustive search or a future-growth forecast.
- Higher-fidelity farming/float-threshold forecasts and selected-policy timing diagnostics. The earlier 150-second -1.4343541695% shortfall remains unresolved; later 300-second accuracy is scenario-specific, not universal.
- A global utility tradeoff between terminal Augments x Wandoos and permanent NGU progression. The current objectives intentionally remain separate.
- Whether preset sequences should ever execute or sustain ownership. They are advisory and Apply remains one-shot.
- Whether fixed applied-policy time should require positive production. Current valid-but-zero-funded fixed stages can earn time; mandatory AT completion still requires actual levels.

## Verification Boundary

Use the normal build/check commands in [Ownership](../checks/Ownership/README.md), [Preset Integration](../checks/PresetIntegration/README.md), [Activity Policy](../checks/ActivityPolicy/README.md) and [Combat Prep Integration](../checks/CombatPrepIntegration/README.md). The [October 16 two-hour live verification](COMBAT_PREP_LIVE_VERIFICATION.md) and [October 17 phase-support/Quick record](PHASE_SUPPORT_LIVE_VERIFICATION.md) remain evidence for those earlier builds/scenarios, including their then-verified final plan state. Latest [automatic/mixed evidence](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#recorded-live-evidence) passes all three mixed Preview/Apply cases and a 300-second scheduled Augments/background-W -> boundary replan -> dedicated-W -> NGU hold, with +0.0027537721244143927% terminal combined error. Current-engine live AT/Wish 190 and controlled live GPS shock are not covered; native-method offline tests are separate. The latest harnesses require a separate finalizer, so the current required `24hr` NGU/auto-rebirth-off handoff remains pending.

## Completed Verification

The following earlier phase-support pass used the integrated production project and a normal Release `Rebuild`, without temporary source imports. It is retained as a recorded pass, not current aggregate suite totals. Later bootup/mixed/precision checks have their own results in the linked test READMEs and [current guide](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md). This documentation-only pass inspected sources and artifacts; it did not rerun builds/tests or interact with the game.

| Area | Recorded phase-support result |
| --- | --- |
| Frontend/native unit tests | 1363 frontend tests and 98 Rust tests passed |
| Browser tests | 458 cases passed; four skips |
| Pure Combat/shared support | 5530 Combat/resource and 1222 shared-support assertions passed |
| Compiled Combat/presets | 4668 Combat integration and 315 native preset-adapter assertions passed |
| Ownership/sustained focus | 382 ownership and 2017 sustained-focus assertions passed |
| Registry/setup | 876 registry and 149 registry/setup assertions passed |

These are offline results, not substitutes for the separately owned live recordings. Native-shape and menu-44 pod-text checks exercise the inspected native input/guard paths and receipt completion, not every game version or full Unity rendering. Preset adventure capture now has an explicit adventure-only seven-ULP acceptance policy; other preset current resource/NGU/GPS/drop getter checks remain exact. See [precision limits](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#farming-and-precision), not a bit-identical combat claim.

### Previous Pass

The following earlier verification results are preserved unchanged as a previous pass, not the latest suite totals. That pass used the normal production project without temporary source imports and was not rerun for its subsequent documentation-only cleanup.

```powershell
.\verify-ui.ps1 -IncludeInjector -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
```

| Area | Recorded result |
| --- | --- |
| Build/check | Release injector build, Svelte/TypeScript check, frontend build, Rust formatting and compile checks passed |
| Frontend/native unit tests | 1285 frontend tests and 97 Rust tests passed |
| Browser tests | 442 desktop/mobile cases passed; four existing gesture skips |
| Pure planners and policy | 4979 Combat/resource, 1495 weighted-preset, 776 registry and 95 registry/setup assertions passed |
| Support | 31089 support-manager and 134 availability/receipt assertions passed |
| Beard policy | 226047 assertions across 19771 scenarios passed |
| Compiled allocation/gear | 21729 black-bar/gear and 4304 Combat integration assertions passed |
| Ownership/presets | 327 lifecycle, 134 preset-adapter and 94 exact-equipment assertions passed |
| Ordinary policy | 76 native policy and 32 compiled terminal-hold assertions passed, including skipped trailing rows |
| Existing suites | External UI, sustained focus, protected timing and other verification checks passed; `git diff --check` passed |

For that previous pass, some source-linked fixtures emitted native-name/unused-field warnings; the production Release build and Svelte check reported no errors. The implementation pass associated with those results did not invoke the live bridge, change game settings, restart, reinject or commit. Its then-pending loading/live-validation note is historical, not a claim about later task-owner activity. Use the separate [phase-support live record](PHASE_SUPPORT_LIVE_VERIFICATION.md) for that earlier evidence and the [automatic/mixed guide](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#recorded-live-evidence) for the latest recordings and remaining gaps.
