# Custom Automation Questions And Blockers

This file tracks questions and implementation blockers discovered while building the Custom automation system.

## Needs User Decision

- No open user decisions right now.

## Decisions

- Manual `Fight` action must never rebirth. Rebirth is a separate explicit `Rebirth` action.
- Keep the old Allocation page. Treat Custom as the cleaner automation layer beside it, not a replacement right now.
- Prefer generated goal-based equipment loadouts over saved exact item-ID presets.
- "Custom plans vs profile resource allocations" means: Custom plans currently control rebirth timing, stage equipment goals, and fight/rebirth actions; the old allocation profile still controls energy/magic/R3 spending until Custom gets its own allocation controls later.
- `Fight` gear scoring is toggleable per custom plan: `sqrt(Power * Toughness)`, `Power * Toughness`, or `Power + Toughness`, with Move Cooldown applied as a multiplier for all modes. Default remains `sqrt(Power * Toughness) * Move Cooldown`.
- Digger upgrades should only happen before rebirth and/or before Money Pit, not during manual fight prep.
- Percent stages should be edited as readable text for now, not a heavy grid UI. The saved plan still stores structured JSON.
- NUMBER-climb defaults are `100x` after `8m`; the stage plan defaults to a 30-minute shape: `5m` Time Machine, `3m` Augments + Blood, `3m` Time Machine, `1m` Augments + Blood, `3m` Wandoos, then `5m/5m/5m` Time Machine/Augments+Blood/Wandoos. After 30m it repeats that final `5m/5m/5m` block.
- Adventure Goal is Custom automation, independent from Custom rebirth/auto-rebirth and from the old main/global automation toggle. It supports optimized ITOPOD, a selected farm zone, or dynamic boost farming; locked farm zones fall back to optimized ITOPOD. Gold-push can interrupt briefly and then return to the configured goal.
- One-click Focus Goals should pause global auto-rebirth and custom rebirth-plan use before applying their focus.
- Gold digger focus budgets should use at most `75%` of gross GPS, selected by relative weights among the diggers requested for that focus.
- Adventure gold-push is independent from rebirth automation, but it still requires Adventure Goal to be enabled. This avoids global script enable immediately starting a gold-gear zone push from a default checked gold-push box.
- Custom fruit activation should only run during active custom auto-rebirth plans with a planned duration over `1h`.
- Auto boost should finish any locked boost item below level `100` first, then prioritize weapons, other equipped gear, locked inventory auto-merge gear, hearts last, then Infinity Cube power/toughness at the selected ratio.
- Custom one-click focus actions should settle briefly after resource recall and after gear swaps before allocating resources, because gear-cap changes can otherwise expose stale/negative idle resource values.
- Custom NGU goal is plan-level and currently has two modes: PAWGs (`Energy NGU` indices `0,1,5,3` plus `Magic NGU` index `2`) and Adventure + Drop Chance (`Energy NGU` indices `4,6` plus `Magic NGU` index `6`).
- One-click resource focuses should keep reallocating until another focus, fight prep, rebirth, or a Custom rebirth plan takes over. They now top off idle resources every frame and still recall/rebalance after the 10s automation profile pass.
- Sustained one-click focus buttons are toggles: the active button is highlighted blue, and clicking it again stops sustained focus and releases the focused resources.
- One-click `Augments + Blood` uses the saved Focus Goals estimated time for augment optimization; default remains `300s`. Scheduled Custom stages use the active stage's remaining seconds instead.
- Built-in `24hr`, `1h-3m-rebirth`, and `infinite` plans should be auto-created when missing and selectable from quick buttons in Auto Rebirth.
- Built-in `1h-3m-rebirth` should be auto-created when missing. It is a 1h plan requiring time plus NUMBER `>= 1x`, with an 8m Wandoos cap push before a final 3m Augments + Blood finisher.
- Custom NUMBER-based rebirth triggers wait 15s after becoming ready, and restart that confirmation wait after a large NUMBER spike.

## API Blockers To Verify

- No current API blockers from the latest live pass.

## Implementation Notes

- Named custom plans should live under `%userprofile%/Desktop/NGUInjector/custom-plans/`.
- Custom plan edits should auto-save to the active plan file.
- `settings.json` should keep global toggles and the active custom plan file/name.
- Gear optimizer should use live owned/equipped gear and output item IDs for existing loadout equip code.
- Fruit of Power alpha is currently handled as fruit ID `1` via `yggdrasilController.fruits[0].consumeFruit(1)`.
- Fu Manchu is currently handled as beard ID `0` via `allBeards.activateBeard(0)`.
- Stat digger is ID `2`. This was inferred from `AllGoldDiggerController.totalStatBonus(offset, skipCheck)`, whose IL uses digger index `2`.
- Custom-tab manual buttons are wrapped by a shared UI error handler. Exceptions are logged through `Main.LogError(...)`, surfaced on the Custom tab status label, and button-click failures show an error dialog.
- Custom tab now uses generated `Equipment Loadout` controls instead of saved loadout management.
- `Equipment Loadout` and `Adventure Goal` live on the `Custom Misc` tab so the main Custom tab stays focused on one-click focus and auto-rebirth.
- Custom tab now has immediate Focus Goals for `Augments + Blood`, `Time Machine`, `Wandoos`, `Energy NGU`, `Magic NGU`, `All NGU`, `Adventure`, and `Manual Titans`.
- `Manual Titans` uses adventure digger weight `10000` and drop-chance digger weight `1000`, with no always-on diggers competing for slots.
- Custom plan templates now include `24hr`, `1h-3m-rebirth`, and `infinite`. `24hr` has auto-rebirth enabled with time `24h` plus NUMBER `>= 1x`; `1h-3m-rebirth` has auto-rebirth enabled with time `1h` plus NUMBER `>= 1x`; `infinite` uses the long stage shape but has plan auto-rebirth disabled.
- Focus Goal beard IDs currently assume the game bonus order: Fu Manchu/attack-defense `0`, drop `1`, Reverse Hitler/NUMBER `2`, NGU `3`, Ladybeard/Wandoos `4`, adventure `5`, gold `6`.
- Focus Goal digger IDs currently use: drop `0`, Wandoos `1`, stat `2`, adventure `3`, energy NGU `4`, magic NGU `5`, energy beard `6`, magic beard `7`, PP `8`, daycare `9`, blood `10`, EXP `11`.
- Custom tab has diagnostics probes:
- `Run safe probe` is read-only and writes a timestamped report under `%userprofile%/Desktop/NGUInjector/logs/`.
- `Run probe.txt commands` reads `%userprofile%/Desktop/NGUInjector/probe.txt`; if missing, it creates a command template. These commands can execute live actions only when explicitly run from the button.
- Live validation on 2026-10-13 covered startup cloud-load, `On To The Game!`, safe probe, command probe, all fight scoring modes, live `equip`, fight prep, fruit-not-ready handling, Fu Manchu, stat digger, boss-push, manual rebirth, and custom auto-rebirth with pre-rebirth prep.
- Money Pit automation requires pit readiness and more than 200k gold. It upgrades affordable diggers first while preserving the effective pit gold reserve, then feeds the pit without a Money Pit loadout.
- Daily Spin automation uses the no-bullshit spin path and returns/logs the prize when ready.
- Gear/loadout swaps intentionally call the game's `removeMostEnergy()` and `removeMostMagic()` first, matching the R/T hotkey behavior: keep basic energy training, clear gear-dependent allocations, then swap.
- Custom plan schema version `6` adds NUMBER-climb triggers, percent stages, adventure goal routing, and rebirth history. Adventure routing has since moved to global Custom settings while legacy plan fields remain loadable.
- Custom plan schema version `7` changes Augments stages to use Blood Magic for magic by default.
- Custom plan schema version `10` adds `AutoActivateFruits` and the now-removed per-plan `AutoBoostEnabled` helper toggle.
- Custom plan schema version `11` adds `NguGoal`.
- Custom plan schema version `12` removes per-plan prep toggles; prep is always-on, with only `PrepRequireMaxedFruit` remaining configurable.
- Custom plan schema version `13` removes per-plan auto boost. Auto boost is now a global `Custom Toggles` setting with a selectable cube Power:Toughness ratio.
- Custom plan schema version `14` adds step completion modes. `Time share` preserves old percent-based scheduling, while `Advanced Training P/T` runs a step until Advanced Training power/toughness levels reach the saved targets.
- Adventure Goal UI changes should not run equipment pre-cache synchronously. They are saved as a batched settings update to avoid UI stalls/freezes from repeated save/update/pre-cache cycles.
- Custom fruit activation uses the existing Yggdrasil activation API and removes all energy/magic before each affordable activation; Custom stage allocation then restores the remaining idle resources to the active stage focus.
- Auto boost supersedes old boost-to-cube routing while enabled and runs even when global automation is off, but still allows the old inventory filter/merge/conversion cleanup to run when `ManageInventory` is enabled.
- Custom plan stages, Custom auto-rebirth, Custom Adventure Goal, and Custom Auto Boost run from their own Custom toggles even when the old main/global automation toggle is off.
- Custom plan stages should not auto-pre-cache all generated loadouts on the automation tick. That froze startup once Custom plans began running without the old global toggle; active stages now optimize/cache only the current gear goal, and full pre-cache remains a manual action.
- Gold generated gear uses a direct single-stat best-per-slot path because the general optimizer can spend too long on accessory combinations for gold-push startup routing.
- The Augments + Blood one-click intentionally uses the full selected augment/upgrade energy split; regular stage/preview augment optimization still uses the cap-trimmed allocation path. The one-click and preview/allocation buttons share the saved estimated-time setting.
- The game exposes NGU `cap()` and `capAll...()` methods, but those are one-time caps, not persistent shift-click. Sustained one-click focus is implemented as a Custom-scoped auto allocator: per-frame idle energy/magic distribution using cap deltas, plus a 10s resource recall/rebalance after profile/custom allocation.
- Blood Magic stage allocation uses gross gold/sec and only subtracts projected Blood Magic ritual drain, intentionally ignoring unrelated gold drains/spend such as augmentation purchases.
- Adventure gold-push uses optimized `Gold` gear under the existing gold loadout lock, then relies on the existing combat kill completion path to restore the saved gear, release the lock, and return to the configured Adventure Goal.
- There is no direct verified API for "zone boss already killed" per normal adventure zone, so custom adventure treats one successful gold-push boss kill per highest unlocked zone per rebirth as done, then returns to the configured Adventure Goal.
