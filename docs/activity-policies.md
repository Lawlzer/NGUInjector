# Activity Policies

`NGUInjector/Data/ActivityPolicies.json` is the shared metadata source for legacy
inline priorities, special-focus intents and ordinary support policy. It is not
the current editable weighted preset library.
The backend embeds this exact file with logical name
`NGUInjector.ActivityPolicies.json`; the UI imports it from
`../../../NGUInjector/Data/ActivityPolicies.json` in `src/lib/stage-presets.ts`.
There is no runtime file fallback, second defaults copy or hot reload. Rebuild the
backend and UI after data edits, reinject the rebuilt backend, and reload the rebuilt UI.

## Global Presets

Dashboard Quick and the Presets tab share one mounted in-page editor and one
revision-aware draft for the runtime folder's `presets.json`, independent of the
plan being browsed. That global library is the editable authority for weighted
definitions. All ten production objectives are supported: Energy NGU, Magic NGU,
blood, ITOPOD PP progress, boosts, zone farming, Time Machine, Augments, Wandoos
and Advanced Training. Current source also appends immediate Adventure power and
Adventure toughness, making twelve weight positions; six- and ten-entry stored
definitions zero-pad to twelve. Immediate adventure stats are not AT gains.
Presets own five AT ratios, support policy and constraints. E9/M7 NGU weights come only from Misc global settings; plans and presets cannot override them.

`AutoUpdate` defaults to true: optimize on Equip/re-equip, never on a background
timer or ordinary allocation refresh. Off requires a valid session policy;
missing/incompatible policies fail rather than silently searching. Explicit
Preview + Apply seeds one. Editing/saving definitions does not apply them or
replace an acquired policy; saved changes require re-equip. Dashboard execution
pauses the plan/auto-rebirth only after preflight, while scheduled execution
retains its owner.

Plans store `ActivePresetId`; standard stages store an explicit global `PresetId`
or `@active`. New rows use `@active` after a saved active definition is selected.
Stage timing, mandatory targets and Fight on entry remain stage concerns;
automatic Augments + Wandoos is a separate execution mode. A referenced stage's
retained legacy focus/gear/priority fields do not define its allocation.

Migration imports historical per-plan `LoadoutPresets` and supported goal/stage
definitions with durable collision-safe receipts. Imported definitions capture
missing legacy NGU ratios from their source plan. Retained `LoadoutPresets`,
`GoalPresets` and `ActiveGoalPresetIndex` are provenance, not a second editable
library; plan saves reject changes to those fields. Receipts prevent deleted
imports from being resurrected. Existing empty-binding inline stages keep their
original capped/overflow behavior until explicit conversion, even if a standard
tuple was recognized or an active global preset was imported. Migration issues
identify rows requiring review/conversion rather than asserting equivalence.

See [loadout presets](loadout-presets.md), [frontend contracts](frontend-global-presets.md)
and [numeric activities](preset-activities.md) for the execution/model boundaries.
Their ten-objective schema descriptions predate the two appended immediate-stat
objectives in current source; they do not limit the current editor to ten weights.

## Legacy Priority Metadata

- `prioritySets` contains 18 unique named priority sequences. `defaults`, `preparation` and `stagePresets` reference these keys instead of duplicating inline arrays. Each referenced set must be valid for its lane.
- `defaults` maps stable stage-focus IDs/names to implicit backend orders. `stagePresets` retains historical inline templates with orders and gear/resource intent, also used for migration recognition. Those templates intentionally differ from implicit defaults, including AUG overflow and AT Wandoos ordering. Preserve array order for retained legacy helper indexes; these are not global preset IDs or current selector options.
- Existing explicit inline-stage priority arrays remain overrides; editing a shared default/template does not retroactively rewrite them. Legacy template helpers copy resolved arrays only when explicitly invoked (including the retained inline AT-target setup action); current global selectors store references instead of copying these orders.
- `preparation` supplies dynamic implicit Wandoos AT orders through `PreparationPriorities(bool energy)`. The stage-wide decision to prepare, paid/free AT checks and no-Wandoos-during-AT enforcement remain runtime logic.
- `focuses` maps quick-focus names to numeric gear and resource intents. Fight gear does not imply boss support when a lane has resource intent. Manual adventure support requires an explicit intent flag, not the gear ID.
- `features` declares lane support and indexed-token counts: `-1` means unsupported, `0` means group-only, and a positive count permits canonical indexes from zero up to count minus one. NGU supports 9/7 energy/magic indexes, AUG 14 energy, AT 5 energy, and RIT 8 magic.
- Only existing executable handlers are accepted. Changing orders, adding an existing token to an order, or changing positive digger weights needs no handler code. Adding a genuinely new handler requires implementation first; an unknown token in the manifest fails validation clearly.
- `coreBeards` remains `[1,3,5,6]`, including Gold. `optionalBeards` remains `[0,2,4]`. Together they supply the preset planner's default composition through `DefaultBeardPriority()`; the beard manager and normal cache also consume the core policy.
- `support` retains the existing baseline/resource/manual requests. Its baseline beard request is deliberately `[1,3,5]`, separate from the manager's core-first activation policy. Do not add Gold to that request as an attempted deduplication.
- `support.diggers.imminentTitan` supplies opportunistic imminent-Titan requests/weights through `ImminentTitanDiggers()`. Runtime still decides whether a Titan is imminent. `support.diggerBudgetFraction` configures the ordinary weighted gross-GPS budget (currently `0.75`); it is not a wallet-spending limit or a replacement for preset-specific budgets.

## Legacy Focus API

The retained `focusGoal` path is an indefinite manual override until changed or
stopped, not the Dashboard's global preset editor. `FocusStageDefinition(goal)`
matches the unique `(GearGoal, EnergyFocus, MagicFocus)` legacy template tuple,
not a label, and returns defensive priority copies. Duplicate template tuples
fail metadata validation. Unmatched special tuples, including Adventure, Fight
and Manual Titans, receive copies of their declared defaults and retain special
support behavior. Existing inline-stage arrays remain overrides and are not
rewritten. Legacy gear optimizers, augment estimates and allocation selectors
still run on this path; it is not the numeric weighted optimizer. Dashboard Fight
and Titans remain commands rather than production presets.

## Combat Prep

Combat Prep is separate: it maximizes terminal Augments x Wandoos within the entered fixed budget, not minimum focus time or global NGU utility. The 7200-second default does not restrict 15-minute, one-hour or 24-hour budgets. For each owned AT/Augments/Wandoos/NGU gear result it compares original support with one deterministic standard phase selection before searching schedules. `BuildSupport` receives AT=`AT/NGU`, Augments=`AUG/NGU`, Wandoos=`WAN/WAN`, NGU=`NGU/NGU`. Core-first beard priorities, phase requirements, retained originals and remaining optionals are filtered/capped by actual unlocks and slots. Background W intent can protect Ladybeard when a slot exists; it does not add a W digger to background NGU requests.

Planned phase diggers use purchased bounds and `DiggerBudgetFraction`; current-support variants instead validate exact native sustainability with fraction 1 and no ordinary policy-budget enforcement. That fraction is a validation ceiling, not measured usage or an instruction to spend it all. UI observed usage is actual drain / gross GPS, separate from the planning limit. Shared support capture, effects, GPS, exact application and receipts live in [support-core.md](support-core.md); this is bounded current-versus-standard selection at captured levels, not exhaustive support optimization or future growth.

For example, reorder `prioritySets.TimeMachineEnergy` to change that implicit
energy default; point a legacy template at an existing lane-valid set to reuse it; or
change an imminent-Titan weight / the GPS fraction to tune ordinary support.
These are build-time data edits, not new executable handlers or saved-plan migrations.

The loader validates complete JSON using the existing strict `JsonFiles` parser,
then validates types, IDs, tokens, handler capacity limits, and required policy
sections. It throws a named-resource error on malformed or missing metadata.
Internal registry collections are private; callers receive defensive array
copies and immutable metadata entries. Backend token recognition trims and
uppercases input, while editor validation still requires exact uppercase tokens.

Dynamic allocation, AT phase decisions, Blood overlays, unlock checks, and live equipment
application remain in their existing code. This registry is build-time metadata,
not a second weighted allocation framework or a user-facing preset editor.

The normal production build compiles the registry and embeds the resource;
source-linked checks use that same JSON. See [Activity Policy Checks](../checks/ActivityPolicy/README.md)
for commands/API boundaries and [Automation Improvements](AUTOMATION_IMPROVEMENTS.md)
for shared utility architecture and deferred modeling questions.

## Verification Scope

This documentation cleanup did not run checks or live actions. Source-linked,
numeric and compiled native-fixture coverage is separate from live validation;
historical totals in check READMEs retain their original scope and do not establish
current global Equip/cache/stage behavior in a running game. Live reports remain
separately owned in [Phase Support Live Verification](PHASE_SUPPORT_LIVE_VERIFICATION.md).
