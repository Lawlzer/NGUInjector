# Custom Automation Plan

## User Notes, Direct

Create a new page, "custom", with new/cleaner/better UI/UX compared to everything else, but no colors. Keep it clean/focused, just better.

Start with an "auto-rebirth" setting/menu.

We will want to be able to create/design full "loadouts", which are things like:

- set these gold diggers: `[]`
- put `%` of energies into these places: `[]`
- put magic into `[]`
- enable these beards: `[]`
- pick this loadout: `[]`
- etc.

Question: can NGUInjector let us say "equip all magic cap gear"? Is that an option they have, something easily implementable, or not really?

For auto-rebirth, we want to be able to set examples like:

- rebirth after 1 hour
- rebirth when NUMBER >= 100x
- select one condition or both conditions at any point

We want to load/save auto-rebirth settings/plans.

Example plan:

- "24 hour rebirth": rebirth after 24h.
- For the first hour, select "time machine" loadout `[3]`.
- Preferably this would be done as "gear that optimizes time machine", but that may not be easily implementable.
- Select certain time machine gear/etc.
- Put all energy into time machine machine speed.
- Put all magic into time machine gold multiplier.
- Set target amounts to `0` for both.
- Set gold diggers to `[these]` when we can.
- Set gold diggers to using this `%` of our gold or less, for example drop chance can use `35%` of our gold, adventure can use `25%`.
- Select these beards.
- All of that would be a "time machine" preset.
- Then, for the next 3h, do another configured phase.

While this whole plan is running:

- Whenever/before titans spawn, put on gold gear / try to kill them with gold gear.
- If we cannot autokill one, swap to a certain loadout.
- This part can be implemented later.

We also need to automate blood magic:

- When should energy/magic be put into blood magic?
- Look into the current codebase, what is automatable, what is already automated, then decide.

Before auto-rebirth, for "fight", automatically:

- swap gear to highest power/toughness average gear
- eat fruit of power A, the Yggdrasil thing
- enable the attack/toughness beard, Fu Manchu beard, beard 1 / likely slot id 0
- disable all gold diggers
- enable stat digger at max
- do nuke
- spam fight boss until we run out of HP
- do all of this before trying to rebirth, maybe 5 seconds before
- have a "rebirth" button to trigger this manually too
- manual trigger should also spend money on new gold digger levels, any/all we can buy, cheapest to most expensive; after buying one level, that one costs more

## Current Codebase Reality

- Auto-rebirth exists, but `AutoRebirth` is currently just the enable switch.
- The actual rebirth rule comes from allocation profile JSON via `Breakpoints.Rebirth` or legacy `RebirthTime`.
- Existing rebirth rule types are `TIME`, `NUMBER`, and `BOSSES`.
- Gear/loadouts currently work by exact item IDs, not by stat optimization.
- "Equip all magic cap gear" is not currently implemented as a simple option.
- A stat-based gear optimizer is feasible, but it is a separate feature: inspect inventory/equipped gear, read item stats, score items for a goal, respect equipment slots/accessories, and equip the best valid set.
- Digger automation exists by explicit digger IDs and affordability recapping, but not by configurable per-digger gold percentage budgets or verified stat-digger selection.
- Yggdrasil harvest/activation automation exists, and targeted Fruit of Power alpha consumption is now implemented for rebirth prep. `Fight bosses` skips fruit consumption.
- Boss nuke/fight automation exists, and a first boss-push state machine is now wired for fight prep.
- Blood magic automation is partial: blood spell thresholds exist, and allocation profile priorities can put magic into blood rituals.
- Beard automation now supports enabling Fu Manchu for fight prep.

## Clean Product Model

### Custom Page

Add a new `Custom` page that becomes the cleaner home for our own automation.

Principles:

- clean, compact layout
- no new color theme
- grouped sections instead of scattered checkboxes
- readable labels and descriptions
- save/load plans by name
- auto-save edits to the active plan
- separate manual `Fight` and `Rebirth` actions
- manual `Fight` must never rebirth
- make goal-based loadouts easy to apply at any time
- surface Custom-page errors directly in the UI and logs
- catch/report errors from global automation loops, Unity update, and file watchers
- provide an in-game diagnostics/probe surface for safe state checks and explicit live action commands
- start with working small pieces, then expand

### Plans

A plan is a named automation configuration, for example:

- `24 hour rebirth`
- `time machine opener`
- `boss push before rebirth`
- `gold titan prep`

Long-term plan structure:

- plan name
- rebirth triggers
- ordered phases
- per-phase loadout selection
- global interrupt rules, such as titan handling
- pre-rebirth actions

Plan persistence:

- store named plan files under `%userprofile%/Desktop/NGUInjector/custom-plans/`
- one active plan is selected in settings
- edits auto-save to the active plan
- plan selector can create/load/copy/delete plans
- keep the old Allocation page available; Custom lives beside it for now
- keep `settings.json` for the active plan name and global enable flags

### Rebirth Triggers

Support selectable conditions:

- time elapsed since rebirth, for example `1h` or `24h`
- number multiplier target, for example `100x`
- boss-count target can stay supported later because the code already has this concept

Trigger behavior:

- user can enable one condition
- user can enable multiple conditions
- if multiple are enabled, user chooses `Any` or `All`
- `Any`: rebirth when any selected condition is met
- `All`: rebirth only when all selected conditions are met

### Phases

A phase is a time window inside a plan.

Example:

- phase 1: `0h-1h`, time machine loadout
- phase 2: `1h-4h`, next loadout
- phase 3: `4h-24h`, long-run loadout

Each phase can eventually configure:

- gear/loadout strategy
- energy allocation
- magic allocation
- R3 allocation
- diggers and digger budgets
- beards
- blood magic behavior
- Wandoos/NGU difficulty if needed

### Reusable Loadouts

Long-term, reusable loadout chunks can be attached to phases. The current UI does not expose saved preset management; it generates equipment loadouts directly from goals.

Examples:

- `time machine`
- `boss fight prep`
- `gold/titan`
- `blood magic`
- `adventure push`

Long-term loadout fields:

- gear strategy: goal-based optimizer first, exact item IDs later only if needed
- diggers: exact IDs plus optional gold budget percentages
- energy targets/priorities
- magic targets/priorities
- R3 targets/priorities
- beards to enable
- fruits to eat/activate
- combat behavior

Implemented v1:

- generated equipment loadouts are available from the Custom tab without saved preset management
- loadouts are selected by optimizer goal and can be applied immediately
- fight loadouts support selectable scoring: `sqrt(Power * Toughness)`, `Power * Toughness`, or `Power + Toughness`, with Move Cooldown applied as a multiplier

Implemented v2:

- custom plans have a percent-based stage scheduler
- stage lines use `Name | Percent | EnergyFocus | MagicFocus | GearGoal`
- supported stage focuses: `None`, `TimeMachine`, `Augments`, `Wandoos`, `BloodMagic`
- stage percentages are translated into absolute seconds using the plan's stage duration
- default NUMBER-climb stages are now 30 minutes total: `0-5m` Time Machine, `5-8m` Augments + Blood, `8-11m` Time Machine, `11-12m` Augments + Blood, `12-15m` Wandoos, then `15-20m` Time Machine, `20-25m` Augments + Blood, `25-30m` Wandoos
- after 30 minutes, the scheduler repeats the final 15-minute fallback block: `5m` Time Machine, `5m` Augments + Blood, `5m` Wandoos
- the `Augments` stage uses energy for the augment optimizer and magic for the Blood Magic optimizer
- Blood Magic optimizer allocates 100% magic to the unlocked ritual with best blood/sec while keeping gross gold/sec minus that ritual's projected blood-magic cost/sec strictly positive
- auto-rebirth has a NUMBER-climb trigger: rebirth when NUMBER is at least `100x` after at least `8m`
- custom plans keep a last-20 rebirth history with timestamp, rebirth time, before/after NUMBER, multiplier, boss ID, and trigger reason
- the Custom tab shows a live `Now` status for current stage/adventure work
- the Custom tab includes an augment optimizer preview/allocation action; stage Augments focus uses the same optimizer
- global Adventure Goal routing can send the character to optimized ITOPOD, a selected farm zone, or dynamic boost farming; locked farm zones fall back to optimized ITOPOD and report that in `Now`
- Adventure gold-push can briefly interrupt the current Adventure Goal with gold gear, then restore gear and return to the configured goal

Implemented v3:

- the top of the Custom tab has one-click Focus Goals: `Time Machine`, `Augments + Blood`, `Wandoos`, `Energy NGU`, `Magic NGU`, `All NGU`, `Adventure`, and `Manual Titans`
- clicking a Focus Goal pauses global auto-rebirth and custom rebirth-plan use, then applies generated gear, resource allocation, beards, and weighted diggers for that goal
- Augment preview now writes detailed output into a visible preview box and estimates expected work from energy allocation, energy power, augment multipliers, expected focus time, and projected gold budget
- weighted digger focus requests target at most `75%` of gross gold production and skip locked/unleveled diggers
- generated equipment goals now include `Wandoos`
- `Manual Titans` uses optimized fight gear, Fu Manchu/fight beards, adventure digger weight `10000`, and drop-chance digger weight `1000`
- custom plan templates can now create `24hr`, `1h-3m-rebirth`, and `infinite` plans; the long plans use `1h` Time Machine, `30m` Augments + Blood, `30m` Wandoos, `3h` Time Machine, then NGU sustain with energy to Energy NGUs and magic to Magic NGUs
- custom auto-rebirth now refuses to rebirth if the NUMBER multiplier would be below `1x`
- Adventure gold-push only runs when the Custom Adventure Goal is enabled; the old main/global automation toggle is not required for Custom routing
- custom plan helper toggles were moved out of Auto Rebirth; fruit settings live on `Custom Toggles`, and auto boost is now a global toggle
- fruit activation only runs during active custom auto-rebirth plans whose planned duration is over `1h`; it removes energy/magic as needed, activates affordable fruits, then the stage allocator puts remaining resources back into the current focus
- auto boost runs whenever enabled, even if global automation is off; it finishes locked boost items below level `100` when possible, then routes boosts to weapons, equipped gear, locked inventory auto-merge gear, hearts, and finally the Infinity Cube using the selected Power:Toughness ratio
- one-click Focus Goals now recall energy/magic, wait briefly, swap generated gear, wait briefly again, then allocate resources; this avoids stale resource totals after gear-cap changes
- the Augments + Blood one-click uses the full augment/upgrade energy split instead of cap-trimming the requested energy, so it should not intentionally leave a chunk idle
- `Fight bosses` and `Rebirth now` are now at the top of the Custom tab, above the one-click focus buttons
- Augments + Blood has a saved estimated-time setting used by the one-click focus and augment preview/allocation optimizer calculations; default remains `300s`
- Custom plans now have an `NGU goal` selector: `PAWGs (Augments + Wandoos + Power + Gold)` or `Adventure + Drop Chance`
- Energy/Magic/All NGU one-clicks and staged NGU sustain allocate only the selected NGU goal targets
- one-click resource focuses now stay active with Custom-scoped shift-click-style allocation: idle energy/magic is distributed every frame using cap deltas, and resources are recalled/rebalanced after the 10s automation profile pass
- `24hr`, `1h-3m-rebirth`, and `infinite` custom plans are now created automatically if missing, and the merged Custom Rebirth Plans section has quick buttons to select them
- the `1h-3m-rebirth` custom plan uses a 1h schedule with an 8m Wandoos cap push before a final 3m Augments + Blood finisher, and requires time plus NUMBER `>= 1x`
- NUMBER-based Custom rebirth triggers wait 15s after becoming ready, and restart that wait after a large NUMBER spike
- the main Custom tab is now focused on Focus Goals and the compact Custom Rebirth dashboard; history, diagnostics, and future notes moved to `Custom Debug`, and Augment Optimizer moved to `Custom Misc`
- `Rebirth now` queues the manual rebirth through boss-push prep and automatically rebirths after the boss push/fight delay clears
- Adventure Goal changes are now saved as one batched settings update and no longer force equipment pre-cache from the UI change handler
- Custom plan stages, Custom auto-rebirth, Custom Adventure Goal, and Custom Auto Boost now run from their own Custom toggles; the old main/global automation toggle only gates legacy automation
- Custom stage ticks no longer auto-pre-cache every planned generated loadout; manual `Pre-cache planned loadouts` still exists, while active stages optimize/cache only the current goal to avoid startup freezes
- Gold generated gear uses a single-stat fast path instead of the general accessory combination search, preventing Custom Adventure gold-push from pegging the UI thread at startup
- Custom rebirth stages are now edited as vertical step cards instead of hidden raw text. Schema version `14` adds per-step completion modes; the first non-time mode supports `Advanced Training P/T` targets such as `6k/6k`, then advances to the next step in real time.

### Diagnostics / Probe

Implemented v1:

- `Run safe probe` performs read-only checks and writes a report to `%userprofile%/Desktop/NGUInjector/logs/`.
- `Run probe.txt commands` reads `%userprofile%/Desktop/NGUInjector/probe.txt` and runs explicit commands.
- If `probe.txt` does not exist, the button creates a commented template instead of running anything.
- Safe/read-only commands: `status`, `augment-preview`, `optimize <goal> [sqrt|product|sum]`.
- Live action commands: `equip <goal> [sqrt|product|sum]`, `fight-prep`, `fruit-alpha`, `fu-manchu`, `stat-digger`, `clear-diggers`, `money-pit`, `daily-spin`, `boss-push`, `stop-boss-push`, `rebirth`.
- Probe output is logged and written to timestamped report files.

Implemented validation:

- startup script can restart NGU, inject, request cloud load, click `On To The Game!` when it appears, and then run startup probes
- safe probe and command probe both run successfully after cloud-load startup
- fight scoring modes are selectable: `sqrt(Power * Toughness)`, `Power * Toughness`, and `Power + Toughness`, with Move Cooldown applied as a multiplier
- live probes validated `equip`, `fight-prep`, Fu Manchu, stat digger, boss-push, manual rebirth, and custom auto-rebirth with pre-rebirth prep
- manual fight prep no longer upgrades diggers; digger upgrades are reserved for pre-rebirth and Money Pit paths
- manual fight prep skips Fruit of Power alpha; rebirth prep consumes it unless `Only consume maxed Fruit of Power alpha` blocks it
- Money Pit checks pit readiness and gold over 200k; it upgrades affordable diggers before feeding while preserving the effective pit reserve and does not use a Money Pit loadout
- Daily Spin uses the no-bullshit prize path when ready
- safe probe on 2026-10-13 validated Custom plan schema version `13`, automatic built-in plan presence, NGU goal selection, always-on prep reporting, fruit activation gating, and global auto boost settings without startup/update errors

## Suggested Implementation Order

### 1. Custom Page Skeleton

Build the `Custom` page first.

Initial sections:

- Auto Rebirth Plan
- Plan Notes / Upcoming Features
- Manual Actions, with disabled placeholders until implemented

### 2. Auto-Rebirth Plan v1

Persist settings:

- custom plan enabled
- plan name
- time trigger enabled
- time trigger seconds/minutes/hours
- number trigger enabled
- number multiplier target
- trigger mode: `Any` or `All`

Behavior:

- when custom plan is enabled, override the allocation profile rebirth rule
- when disabled, existing profile rebirth behavior remains unchanged
- auto-save field changes to the active plan file

### 3. Save/Load Named Plans

Implement named plans in v1 rather than deferring them:

- create `%userprofile%/Desktop/NGUInjector/custom-plans/`
- create a default plan if no plans exist
- list plans on the Custom page
- selecting a plan loads it and updates the active plan setting
- changing fields auto-saves the selected plan
- `New` creates a clean plan
- `Copy` duplicates the current plan
- `Delete` removes the selected plan after confirmation

Use safe file names derived from plan names.

## Gear Optimizer Investigation

Reference: `https://gmiclotte.github.io/gear-optimizer/#/` and `https://github.com/gmiclotte/gear-optimizer`.

Findings:

- The project is a React app with a separate optimizer module.
- The source has generated item data in `data/Items.js`.
- The item model includes item id, name, slot, zone/set, level, and stat bonuses.
- The optimizer supports priorities like magic cap, energy cap, time machine, NGU, wishes, beards, gold drops, drop chance, power, toughness, etc.
- The algorithm uses pareto filtering and a dynamic-programming/knapsack-style search, then handles accessories with iterative replacement.
- The site is useful as a reference for stat names, scoring goals, and item data shape.
- The repository does not appear to expose a license through GitHub's license endpoint, so we should not copy code verbatim unless we get permission or confirm licensing.

Practical implementation guidance:

- Do not embed the React app in NGUInjector.
- Do not try to run JavaScript from the mod.
- Implement a native C# gear optimizer inside NGUInjector.
- Use the optimizer project as conceptual reference only unless licensing is clarified.
- Prefer reading live game inventory/equipped item data where possible, because NGUInjector already has access to the game objects.
- If live stat data is hard to access, use a generated/static item-stat table as a fallback, but validate item IDs against the installed game.

Recommended stages:

1. Goal-based loadouts first: filter candidates by relevant stats, remove dominated items, then test remaining combinations.
2. Add accessory selection and locked slots.
3. Add multi-stat scoring goals: `Energy NGU`, `Magic NGU`, `Time Machine`, `Magic Cap`, `Power/Toughness average`, `Gold`, `Drop Chance`.
4. Add exact-ID loadouts later only where generated goal-based selection is insufficient.
5. Add deeper optimization only if brute force is too slow after filtering.

Initial scoring goals:

- `Magic Cap`: maximize magic cap stat.
- `Energy Cap`: maximize energy cap stat.
- `Energy NGU`: combine energy cap, energy power, and NGU speed.
- `Magic NGU`: combine magic cap, magic power, and NGU speed.
- `Time Machine`: combine energy cap, energy power, magic cap, and magic power.
- `Fight`: maximize average or product of power and toughness.
- `Gold`: maximize gold drop/gold gain stats.
- `Drop`: maximize drop chance.

Risk/effort estimate:

- Exact-ID loadouts: easy.
- Brute-force stat optimizer: moderate, mostly depends on how easy it is to read item stat bonuses from live game objects and how many accessories remain after filtering.
- Full optimizer equivalent to the website: medium/high, because it needs correct item data, accessory slot handling, locks, offhand effects, caps, and priority chaining.
- Copying website code directly: not recommended because it is JavaScript/React, not C#, and licensing is unclear.

### Brute-Force Optimizer Plan

Use brute force first, with safe reductions before brute force.

Process:

1. Build owned/equippable candidate items from current inventory and currently equipped gear.
2. For the requested goal, keep only relevant stats.
3. Split candidates by slot: weapon, head, chest, pants, boots, accessories.
4. Remove dominated items within each slot.
5. Brute-force all remaining main-slot combinations.
6. Brute-force accessories when the filtered candidate count is reasonable.
7. If accessory count is too high, first cap to the best candidates by score or add a combinatoric limit.
8. Return the winning item instances, then equip by resolving each selected item object's current slot at swap time.

Implemented v1 note:

- The optimizer preserves concrete item instances internally and equips by resolving the selected `Equipment` object's current slot at swap time.
- Accessory search is capped at `1,500,000` combinations for runtime safety. Very large accessory pools are brute-forced over the best capped candidate set rather than every owned accessory.

Dominated item rule:

- Item A dominates item B for a goal when A is greater than or equal to B on every relevant stat, and strictly greater on at least one relevant stat.
- Dominated items can never be optimal for that goal and can be removed.

Example:

- For `Energy NGU`, relevant stats are `Energy Cap`, `Energy Power`, and `NGU Speed`.
- A helmet with `{ Energy Power: 300 }` dominates a helmet with `{ Energy Power: 100 }`.
- A helmet with `{ Energy Power: 300 }` does not dominate a helmet with `{ Energy Power: 100, Energy Cap: 50 }`, because that is a tradeoff.

Website-derived relevant stats:

- `Energy Cap`: `Energy Cap`.
- `Magic Cap`: `Magic Cap`.
- `Energy NGU`: `Energy Cap`, `Energy Power`, `Raw NGU Speed`.
- `Magic NGU`: `Magic Cap`, `Magic Power`, `Raw NGU Speed`.
- `All NGUs`: `Energy Cap`, `Energy Power`, `Magic Cap`, `Magic Power`, `Raw NGU Speed`.
- `Energy Time Machine`: `Energy Cap`, `Energy Power`.
- `Magic Time Machine`: `Magic Cap`, `Magic Power`.
- `Time Machine`: `Energy Cap`, `Energy Power`, `Magic Cap`, `Magic Power`.
- `Gold`: `Gold Drops`.
- `Drop`: `Drop Chance`.
- `Blood`: `Magic Cap`, `Magic Power`.
- `Energy Beards`: `Energy Power`, `Energy Bars`, `Raw Beard Speed`.
- `Magic Beards`: `Magic Power`, `Magic Bars`, `Raw Beard Speed`.
- `Beards`: `Energy Power`, `Energy Bars`, `Magic Power`, `Magic Bars`, `Raw Beard Speed`.
- `Energy Wandoos`: `Energy Cap`, `Raw Wandoos Speed`.
- `Magic Wandoos`: `Magic Cap`, `Raw Wandoos Speed`.
- `Wandoos`: `Energy Cap`, `Magic Cap`, `Raw Wandoos Speed`.
- `Wishes`: `Energy Cap`, `Energy Power`, `Magic Cap`, `Magic Power`, `Resource 3 Cap`, `Resource 3 Power`, `Raw Wish Speed`.
- `Hacks`: `Resource 3 Cap`, `Resource 3 Power`, `Raw Hack Speed`.
- `Fight`: not directly defined by the optimizer project as one combined factor; we define it ourselves as power/toughness average or product.

Website-derived scoring model:

- Gear stats add together before scoring.
- Most percentage stats start at `100`.
- `Power`, `Toughness`, and `Respawn` start at `0`.
- Score is the product of each relevant stat divided by `100`.
- Some compound goals use exponents/weights.
- Item stat value scales by item level as `base * (1 + level / 100)`.
- Offhand weapon stats are multiplied by the offhand percentage.
- The website can hard-cap stats using current/base cap data, but we can defer that until basic optimizer works.

Initial score formulas:

- `Energy Cap`: `EnergyCap`.
- `Magic Cap`: `MagicCap`.
- `Energy NGU`: `(EnergyCap / 100) * (EnergyPower / 100) * (NGUSpeed / 100)`.
- `Magic NGU`: `(MagicCap / 100) * (MagicPower / 100) * (NGUSpeed / 100)`.
- `All NGUs`: `(EnergyCap / 100)^0.5 * (EnergyPower / 100)^0.5 * (MagicCap / 100)^0.5 * (MagicPower / 100)^0.5 * (NGUSpeed / 100)`.
- `Time Machine`: `(EnergyCap / 100)^0.5 * (EnergyPower / 100)^0.5 * (MagicCap / 100)^0.5 * (MagicPower / 100)^0.5`.
- `Blood`: `(MagicCap / 100) * (MagicPower / 100)`.
- `Gold`: `GoldDrops / 100`.
- `Drop`: `DropChance / 100`.
- `Fight`: choose `sqrt(Power * Toughness)`, `Power * Toughness`, or `Power + Toughness`; all modes multiply by Move Cooldown.

Implementation notes:

- Prefer live game item stats if accessible.
- If live game item stats are hard to read, generate a static C# table from the optimizer's item data or from NGU game metadata.
- Do not copy the optimizer JavaScript directly unless license/permission is clarified.
- Store optimizer output as item IDs so it can flow through existing NGUInjector loadout equip logic.

### 4. Manual Rebirth Prep Button

Add manual buttons that run fight prep or the full pre-rebirth prep sequence without immediately redesigning all automation.

Target sequence:

- upgrade cheapest diggers while affordable
- disable gold diggers
- enable stat digger at max; stat digger ID is `2`
- eat Fruit of Power A if possible
- enable Fu Manchu / attack+toughness beard if possible
- equip fight gear/loadout
- nuke or fight bosses until HP stops us

`Fight bosses` runs the fight setup and boss push only. `Rebirth now` runs the full sequence, including Fruit of Power alpha, before rebirthing.
- never rebirth from the `Fight` button
- use a separate explicit `Rebirth` button for manual rebirths

### 5. Digger Budgeting

Add per-digger budget settings:

- drop chance digger max `%` of gold income/drain
- adventure digger max `%`
- stat digger max `%`
- other diggers later

Integrate into current `DiggerManager.RecapDiggers()` logic.

Implemented v1 note:

- Focus Goals use weighted digger requests capped to `75%` of gross GPS.
- Digger IDs used by Focus Goals: drop `0`, Wandoos `1`, stat `2`, adventure `3`, energy NGU `4`, magic NGU `5`, energy beard `6`, magic beard `7`, PP `8`, daycare `9`, blood `10`, EXP `11`.
- Base always-on focus diggers are drop, adventure, energy beard, magic beard, PP, and daycare; focus-specific diggers compete by weight if slots are capped.

### 6. Goal-Based Presets v1

Start with generated goal-based loadouts because they are the most useful Custom-page behavior.

Fields:

- gear goal/strategy
- digger IDs
- energy priorities
- magic priorities
- beards placeholder

Implemented v1 note:

- The Custom tab now exposes `Equipment Loadout` controls for generated optimizer goals and an `Apply generated loadout now` action.
- `Pre-cache planned loadouts` warms stage, plan, fight-prep, and Adventure gold-push equipment goals for the current rebirth.
- Focus Goals are the first reusable goal/preset layer. They are immediate actions today; the staged rebirth planner can reuse this layer later.

Black-bar priority allocation:

- Stage presets and sustained Focus Goals use `Managers/BlackBarAllocator.cs` with buffered caps. Version 16 plans use `EnergyNguWeights` (9 whole-number weights) and `MagicNguWeights` (7). Positive weights divide resources proportionally; zero excludes a target from generic `NGU`. Capped shares redistribute, while the outer priority chain and explicit `NGU-n` singleton tokens remain ordered. All-zero weights skip the group and allow downstream priorities.
- `EnergyPriorities` and `MagicPriorities` are ordered string arrays on each stage. Blank/missing arrays use that focus preset's default chain. Explicit orders override the focus; changing the focus does not discard them.
- Example energy order: `WAN, NGU-4, TM` funds Wandoos, then energy Adventure NGU, then Time Machine. If an earlier priority cannot reach its buffered cap, it receives all remaining resource and lower priorities get none. In `WAN, NGU, TM`, the NGU priority shares its budget by weights before spilling to Time Machine. Adventure Alpha and Drop Chance weights 1:1 yield 500/500 from 1000 when neither is capped; 2:1 yields 667/333. Integer remainders use largest fractional shares with ascending-index ties, including migrated plans.
- Wandoos and Blood Magic have flat resource caps. NGUs, augment/upgrade bars, Time Machine, and Advanced Training have level-dependent caps. Their projected requirement includes 50 possible levels per second plus a tick guard, stops at positive level targets, and always rounds resource requirements up.
- `BlackBarLookaheadSeconds` defaults to 5, with a minimum of 3, covering the two-second resource-refresh cadence. Iron Pill prep uses 12 seconds because it only refreshes on the ten-second automation routine. Sustained focus rebalances on that two-second cadence even with no idle resource, so growing primary caps reclaim allocations from downstream priorities.
- Caps use actual power/speed/difficulty bonuses, not hard-cap-clipped estimates or NGU's partial-cap modifier. Resource shortages do not pretend an unreachable cap is black-barred.
- Common tokens: `WAN`, `TM`, and `NGU` (the positive-weight group). Energy also supports `NGU-0..8`, `AUG` (pinned best pair), `AUG-0..13` (alternating augment/upgrade), `AT` (power/toughness), and `AT-0..4`. Magic also supports `NGU-0..6`, `RIT` (pinned best sustainable ritual), and `RIT-0..7`. Explicit NGU tokens work even when their generic weight is zero.
- The Energy NGU preset uses both resources (`EnergyFocus=5`, `MagicFocus=6`) with energy-focused gear (`GearGoal=2`); All NGU differs by using balanced NGU gear. New plans default to energy Adventure Alpha/Drop Chance weights 1:1 and magic Yggdrasil weight 1, with all others zero. Old PAWGs/AdventureDrop fields migrate to equivalent weights, preserving their old magic targets; only an intentional edit changes an existing plan to Yggdrasil. Applying a stage preset retains weights and explicit priority overrides.
- Edit all 16 weights in the desktop controller's Misc page for the plan selected in AFK Rebirthing; the card identifies the editing and active plans separately. The dashboard is read-only telemetry for the active plan, and its Misc shortcut preserves the selected plan without loading or saving. Runtime uses `strategy=weighted-ngu-buffered-cap`, configured arrays, applied lane snapshots/flags, and exact allocation/cap decimal strings. Pending or blocked settings are not reported as successfully applied. Legacy plan reads normalize without writing; saves remove `NguGoal` and retain unknown fields/history/CAS revisions. Missing or malformed plans fail closed rather than executing or saving defaults. Weighted saves/copies require a version-16 injector; rollback requires compatible plans as well as the older DLL.
- Locked, completed, disabled, and currently unaffordable targets are skipped. Blood rituals are selected after higher priorities allocate, and multiple rituals share a cumulative gold-income budget.
- If all configured targets are capped, complete, or unavailable, excess resources stay idle rather than being wasted above cap. Successful capped passes suppress the idle warning; context changes invalidate that exemption.
- Wandoos stages retain their existing early Advanced Training preparation phase when using defaults. An explicit priority order bypasses that phase.
- `checks/BlackBarChecks.csproj` exercises ordered spillover, shortages, cap growth/preemption, projection/rounding boundaries, token validation, saved-stage round trips, and idle-exemption reset without modifying a running game/save.

### 7. Stat-Based Gear Optimizers

Continue expanding the goal-based optimizer and generated loadout flow.

Initial optimizer goals:

- highest power/toughness average
- magic cap
- time machine
- gold drop/gold gain

This requires reading item stats and defining scoring rules.

### 8. Blood Magic Scheduler

Map what current profile priorities can already do.

Then add higher-level controls:

- when to allocate magic to blood rituals
- when to stop blood rituals
- when to cast Iron Pill
- when to cast MacGuffin A/B
- when to reserve blood for rebirth number spell

### 9. Titan Interrupt Rules

Later feature.

Global rules while a plan is running:

- before titan spawn, switch to gold gear/loadout
- try to kill with gold gear
- if autokill is not possible, switch to combat loadout
- restore phase loadout afterward

## Open Questions

- Should plans eventually get their own energy/magic/R3 allocation controls, or keep using the old Allocation page indefinitely?
- Boss push still needs live in-game validation around HP/fight-state behavior.
