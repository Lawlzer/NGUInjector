# Future Work And Decisions

Canonical replacement for the retired workspace `temp.md`, reconciled with
production code on 2026-10-24. These are optional investigations/non-goals, not
missing executor implementations or permission for live work. Required closure
and acceptance stay in [WIP](../../wip.md); historical authorization and the old
auto-rebirth-off handoff are not operative. Do not delete `../temp/` backups.
Source references below are repository-relative; line numbers describe this audit.

## Optional Investigations

| Topic | Remaining Scope And Evidence |
| --- | --- |
| Native farming precision | Native PP capture already exists, but support rescaling, regular-zone/drop/boost tables and hit-time/survival fidelity remain approximations. Check independent native rounding/data boundaries before refining them. `NGUInjector/Main.LoadoutPresets.cs:280-299,544-595`; `NGUInjector/Managers/LoadoutPresetPlanner.cs:809-835`. |
| Selected-policy threshold diagnostics | Selected phases/targets, allocations and partials already exist. Add only missing immutable capture/first-funding/pre-termination coefficients, actual training rates and coherent clocks if needed. Native callback counts remain unavailable unless observed, never inferred from elapsed time. `NGUInjector/Main.CombatPrep.cs:1503-1524,1578-1616`; [diagnostic contract](../checks/CombatPrep/RECORDED_COMBINED_DIAGNOSIS.md#minimum-diagnostic-dto). The old short-run miss is unresolved, not a proved model defect or justification for timing fudge. |
| Future bonus growth | Forecast only justified beard/digger/NGU feedback with native reference evidence. Fixed captured support, bootup/free AT and observed GPS/equipment events already work; owned digger purchases can update pinned rates without changing selections/budgets. `NGUInjector/Main.CombatPrepCapture.cs:282-286`; `NGUInjector/Main.PurchaseActions.cs:187-210`. |
| Eligible live scenarios | Current-engine paid AT, Wish 190 and a controlled GPS event need separate eligible-state live evidence if pursued. Native-method checks and older live AT are not these scenarios. Do not alter unlocks or targets to imply coverage. `NGUInjector/Managers/CombatPrepPlanner.cs:563-568`; `NGUInjector/Main.CombatPrep.cs:679-698`; [recorded limits](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#earlier-results-and-gaps). |

PP progress/s, boost points/s, retained percentages and bounded search scores are
forecasts/proxies, not measured long-run yields, global optima or guaranteed combat
safety. Keep adventure-only seven-ULP acceptance separate from exact non-adventure
parity checks. See [precision limits](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#farming-and-precision).

## Parked Scope

- Executable advisory two-phase sequences remain optional. Global sustained preset
  ownership is implemented separately; legacy inline Apply remains one-shot.
  `NGUInjector/Main.LoadoutPresets.cs:1373-1382`; [preset workflow](loadout-presets.md#workflow).
- Universal activity optimization, future loot prediction and general account-
  progression utility are not current requirements. The ownership rewrite moved
  into the executor effort; do not keep it as a second parked implementation task.
- Arbitrary Combat phase sequences are not supported: AT/Augments may occur once
  each before a terminal phase. This is distinct from configurable ordinary plan
  stages. `NGUInjector/Managers/CombatPrepPlanner.cs:542-568`.

## Intentional Decisions

- Editable presets use one global weighted library. AutoUpdate defaults on and
  means optimize on equip/re-equip, not a timer. Saving does not apply or silently
  replace acquired intent. Off reuses a valid policy; without one, explicit Preview
  + Apply is required. [Preset contract](loadout-presets.md#workflow).
- Quick focus is a manual takeover, not another scheduler or estimate-driven
  expiry. It continues until explicitly changed/stopped within its session.
- Combat maximizes terminal combat stats within the configured available time,
  not minimum completion time. Preserve original elapsed deadlines and actual AT/
  augment gates; never fund Wandoos before/during pending planned paid AT or reset
  dump work. One Wandoos OS per rebirth remains fixed after commitment.
  [Automatic stage contract](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#automatic-stage).
- Retained-production scoring uses finite feasible references, frozen replay
  denominators and bounded late-positive utility, never tiny-denominator
  amplification or unavailable output treated as measured zero.
  [Scoring contract](loadout-presets.md#priorities-and-scores).
- Positive production is not required to earn otherwise verified fixed applied
  time. Mandatory AT still requires actual levels. This is a decision, not an open
  feature task. `NGUInjector/Main.ProtectedTiming.cs:459-469`;
  `NGUInjector/Managers/CustomStageAllocator.cs:805-828`.
- Legal phase support may change at current known levels. Keep-current/pinned
  support cannot be silently resized; preserve core-first beard policy and
  distinguish requested support, observed state and desired feasibility.
- Saved-plan overrides and Misc NGU zero exclusions remain user-owned. Policy/data
  edits require the appropriate rebuild/reload and do not retroactively rewrite
  saved plans. Temporary Blood/Iron Pill funding does not replace gear/support
  intent. Exact budgets, lane permissions, owner/epoch identity, earned entry/fight
  history and no-mutation preflight/current-successor cleanup remain WIP requirements.

The [retired next-steps snapshot](retired-automation-next-steps-20261024.md) preserves
the original evidence and wording only; it is not a second backlog. Session-local
action/persistence rules in [the newer contract](action-state-simplification-20261023.md)
override older durable replay requirements.
