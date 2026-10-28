# Automation Improvements

Implementation follow-up to the [historical integration review](AUTOMATION_INTEGRATION_REVIEW.md). R01-R28 and follow-up fixes R29-R33 are implemented. [Current WIP](../../wip.md) owns required closure/acceptance; [future work](future-work.md) owns optional topics and intentional decisions. Historical commands, counts and incident statuses below are evidence only, not a second backlog or current live instructions. The newer [action/session contract](action-state-simplification-20261023.md) overrides obsolete durable replay/restore assumptions.

For the current automatic stage, mixed production/support search, forward bootup/Wish 190, precision policy and latest recorded live results, see [Automatic Combat And Mixed Focus](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md). Historical verification below is not a claim about the game's current final configuration.

## Current Behavior

| Path | Selection and execution | Lifetime |
| --- | --- | --- |
| Scheduled stages / Quick | Global weighted presets supply acquired gear/support and independent resource priorities; retained inline stages keep their ordinary cap/share semantics. Configured setup stays separate from temporary Blood/Iron-Pill funding. | Quick is a session-local indefinite manual takeover, not timed Combat Prep. Protected stage credit requires verified application and entry ownership. |
| Combat Prep | Bounded timed AT/Augments/Wandoos search maximizes terminal A*W within the entered budget. Current versus legal phase support, fixed captured levels, per-profile GPS, exact replay, actual targets, original deadline and committed OS govern execution. | Shared setup/resources and current-successor handoff; only exact still-owned borrowed fields return. Historical gear/support restoration is removed. |
| Automatic Augments + Wandoos stage | `ExecutionMode: 1` uses the same Combat engine with fresh start/eligible boundary/material GPS or equipment-change planning. Original elapsed budget includes entry fight, fruits, waits, planning and setup. Continuation retains actual gates, no-AT-after-W history, pools, OS and deadline. | Scheduled runtime overlay; safe cleanup retries are bounded, obsolete restoration permissions retire, and current intent reconciles automatically without manual acknowledgement. Ordinary saved rows are not batch-migrated. |
| Weighted presets | Retained-production and immediate-stat goals, bounded legal support portfolios, exact-policy replay and observed post-apply metrics. Support/GPS precede affordability and funding. | Global owners sustain acquired intent; legacy inline Apply is one-shot. Advisory sequences are not executable schedules; a Preview token is not a sustain timer. |
| Temporary detours / finite effects | Current-session claims/grants, verified prerequisites and identified native action boundaries. Exit resolves current successor intent, not a historical setup snapshot. | Bounded session-local processes; ordinary purchases/rescans use fresh native state, not a per-action disk replay ledger. |

The earlier regressions preserve completed-but-held AUG without reopening AT preparation (R29), an existing Quick owner on read-only rejection (R30), actual Magic NGU gear intent (R31), and original scoring references during fresh replay (R32). R33 addressed the then-existing snapshot/lock capture problem; that historical fix is not a requirement to restore removed manager workflows. Current claims and session rules take precedence.

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

Optional precision, diagnostic, bonus-growth and sequence investigations are tracked
only in [future work](future-work.md). Fixed applied time without positive production
is intentional; mandatory AT still checks actual levels. Global preset ownership is
implemented and is separate from optional executable advisory sequences.

## Verification Boundary

Use `verify.ps1` (all offline scopes by default), or narrow with `-Scope UI|Source|Native|Harness`.
Native supports `-Suite`/`-Selector` and installed references via `NGU_IDLE_DIR` or
`-GameFolder`. See [Testing And Save Safety](testing.md) for reports, native capture,
fail-fast contention/defer/drift and guarded game modes. The public game-facing
runner is `test.ps1`; it never runs an offline gate or deploys a backend.

The [October 16 live record](COMBAT_PREP_LIVE_VERIFICATION.md),
[October 17 phase-support record](PHASE_SUPPORT_LIVE_VERIFICATION.md) and
[automatic/mixed recordings](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#recorded-live-evidence)
remain specific to their builds/scenarios. They do not establish current-engine
paid AT/Wish-190/GPS-event coverage or a final-build daily cycle. The old auto-rebirth-
off handoff is historical; [current WIP](../../wip.md) owns the final-state target.
Runner changes/compiled tests are not actual game acceptance; Live requires a
compatible deployed backend and no actual live transaction is certified here.

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
