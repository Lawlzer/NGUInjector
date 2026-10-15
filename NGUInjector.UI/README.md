# NGUInjector.UI

Standalone **Tauri 2 / Rust + Svelte 5 / TypeScript** controller. The Unity/Mono automation stays in the injected C# DLL. The app belongs to the existing NGUInjector repository, not the neighboring `temp` checkouts.

## Run

Prerequisites: Node 22.12+ or 24+, Rust stable, Microsoft C++ Build Tools, and WebView2. Bridge regression checks need the .NET 8 SDK/runtime. Building the injector also needs .NET Framework 4.8 targeting tools and the NGU Idle managed assemblies.

From the repository root:

```powershell
.\start-ui.ps1          # Builds the native executable if missing, then opens it
.\start-ui.ps1 -Dev     # Vite + Tauri hot reload
.\start-ui.ps1 -Build   # Rebuild the native executable before opening
.\verify-ui.ps1
.\verify-ui.ps1 -BrowserTests -IncludeInjector -GameFolder "C:\path\to\NGU IDLE"
```

Or from this folder:

```powershell
npm ci
npm run tauri dev
npm run tauri -- build --bundles nsis
```

The native executable is `src-tauri/target/release/nguinjector-ui.exe`; installers are under `src-tauri/target/release/bundle/` after a bundled build. Neither opening the app nor verification starts the game, injects code, or performs live actions.

## Connect

1. Stop/unload any currently injected instance before changing startup mode.
2. Open the controller and use **Enable External UI** on the Dashboard.
3. Build/inject the updated C# DLL through the existing injection workflow. Start the game yourself; do not invoke restart scripts merely to open the controller.
4. The dashboard becomes Connected after successful injector initialization.

The startup marker is `%USERPROFILE%\Desktop\NGUInjector\external-ui.enabled`. Alternatively set `NGUINJECTOR_EXTERNAL_UI=1` in the **game process environment** before launching it. A marker is not proof of connection; the UI requires a fresh heartbeat and `status: running`.

The default path deliberately matches the existing injector, even if Windows redirects the Desktop to OneDrive. The folder picker can select another existing data folder for offline editing; it does not change the injected DLL's runtime path. Remove the marker before a subsequent injection to return to WinForms.

If a refresh cannot read `runtime.json` because another process has it locked (Windows error 32), the UI automatically retries every five seconds until the read succeeds. The retry message clears on recovery, unsaved edits are retained, and writes/game commands are not replayed.

## Features

- Dashboard: heartbeat, active plan/focus, loop progress, command results, and one full-width, bar-driven plan editor.
- Blood Magic: live full-magic time-to-goal estimates, explicit unavailable/error reasons, Iron Pill eligibility blockers, saved reservation thresholds, and the last ten successful automatic casts.
- Focus goals: Time Machine, Augments + Blood, Wandoos, NGU, Advanced Training, adventure, fight, and titans.
- Adventure: adventure routing and zones, manual combat options, automatic inventory boosting, and cube Power:Toughness tuning.
- Misc: manual inventory/loadout preview and equip, augment tools, general automation and tuning, Iron Pill settings, utilities, and data-folder setup.
- Dashboard plan editor: create/copy/delete plans, built-in shortcuts, auto-rebirth conditions, gear presets, reordered stages, loop boundaries, resource priorities, black-bar lookahead, AT targets, and selected-plan NGU weights.
- Stages: select a bar segment to open its settings directly below the bar; select it again or use **Close** to hide the panel. Only one panel is visible at a time. Drag the whole segment left/right to reorder, or use Alt+Left/Right (Alt+Up/Down also works). There is no separate grip or move button. Durations accept seconds/minutes/hours/%. Fixed times are exact; percentage stages divide the remaining schedule budget. AT stages wait for their targets rather than consuming timed shares.
- Live progression: the selected-plan bar overlays current position and completion only when fresh runtime telemetry matches its entire stage sequence. Live progress never selects a panel, changes the editor plan, or advances the game. Other-plan, mismatched, pending-edit, and offline states leave the bar editable with an explanatory note; there is no separate runtime timeline.
- Logs: bounded tails rather than reading an ever-growing log into memory.
- Advanced JSON: validated unsaved buffers and conflict checks. Plan JSON, including exact large numeric values, is in collapsed **Advanced plan JSON** details before **Plan library** near the bottom of Dashboard; the **Advanced** tab contains Settings JSON and game data only.

The dark Dashboard puts **Quick** first, then fight/rebirth/save actions and plan controls. **Enable plan** and **Auto-rebirth: On/Off** apply directly, without activation popups. Auto-rebirth enables the selected plan's rebirth permission as well as the global/run switches, so its conditions can take effect immediately. Disabling the plan also disables global auto-rebirth; independent adventure, boost, pit, and spell automation remain unchanged. Manual rebirth still requires confirmation.

Primary navigation is **Dashboard, Adventure, Misc, Logs, Advanced**; there is no separate AFK Rebirthing tab. The top **Plan & Rebirth** card retains the visible **Plan** selector and live toggles, with **Edit plan** scrolling to the single full-width editor. The stage bar comes first. **Schedule & templates**, **Gear settings**, **Rebirth settings**, **Rebirth history**, **NGU weights**, and **Advanced plan JSON** start collapsed. **Plan library** is the last collapsed card; open it to access **Plan to edit**, built-in shortcuts, and create/copy/delete controls. Rebirth triggers and numeric targets are edited only under **Rebirth settings**, not inline at the top. All plan controls remain on Dashboard.

Energy/Magic/All NGU Quick buttons use the active plan's **NGU weights**, not the plan open for editing. Open the dedicated collapsed **NGU weights** section in the Dashboard plan editor; it edits the plan selected by **Plan to edit** (or the synchronized top **Plan** selector), not implicitly the active plan. All nine energy and seven magic targets have labeled integer controls, accepting `0..2147483647`. Zero disables a target; positive weights are ratios, not percentages. With weights 2:1, uncapped targets receive two-thirds and one-third of the resource; shares redistribute when buffered caps are reached. All-zero weights skip generic NGU for that resource, allowing later energy or magic priorities.

The outer resource priority chain is still ordered: `WAN, NGU, TM` funds Wandoos before the weighted NGU group, then Time Machine. Explicit singleton tokens such as `NGU-4, NGU-6` remain ordered, not weighted. Runtime telemetry identifies weighted allocation as `strategy: weighted-ngu-buffered-cap` with `capBasis: last-allocation-pass`. Root `energyWeights`/`magicWeights` arrays and all 9/7 target rows report desired weights; rows include `index`, `name`, `weight`, exact decimal-string `allocated`, and nullable `bufferedCap`/`belowBufferedCap`. The dashboard shows selected positive-weight targets and any nonzero actual allocations, even if their selected weight is zero. Exact large amounts remain available in titles and details.

The runtime NGU allocation display is read-only; the Dashboard also contains editable plan controls. `energyAppliedWeights`/`magicAppliedWeights` (arrays or null) and `energyWeightsApplied`/`magicWeightsApplied` distinguish selected weights from weights actually applied. Blocked or unknown application never implies the desired weights took effect; existing spend remains visible. **Edit NGU weights** opens its disclosure and scrolls to the dashboard card (`id="ngu-weights"`), preserving the selected editor plan without changing tabs, loading the active plan, or saving anything. The card identifies its selected plan separately from the active plan. NGU weights are not duplicated in Misc.

Version 16 plans persist `EnergyNguWeights: int[9]` and `MagicNguWeights: int[7]`. New backend defaults select energy Adventure Alpha (index 4) and Drop Chance (index 6) at weight 1, and magic Yggdrasil (index 0) at weight 1; all others are zero. Existing legacy plans are migrated by the backend, never by rendering the editor. Missing arrays, legacy plans, or unsupported injectors show a reconnect/reload message. Weight changes require a fresh connected read/save bridge advertising `runtime.planVersion >= 16`. Editing unrelated legacy fields preserves the plan version, saved durations, and unknown fields without injecting weight defaults.

The **Energy NGU Quick focus and stage preset** both allocate energy and magic to their selected NGU groups while keeping energy-focused gear. The stage preset uses `EnergyFocus: 5`, `MagicFocus: 6`, and `GearGoal: 2`; All NGU uses balanced NGU gear. Applying a stage preset replaces saved priority overrides with that preset's allocation and preserves plan NGU weights. Reload the rebuilt injector to apply allocation changes; changing a plan alone cannot update an already-loaded DLL.

### Stage Presets And Timing

Each bar segment and stage-panel title shows the base preset identified by its saved energy focus, magic focus, and gear, without adding a stored preset ID. Energy and magic priority orders are independently editable: changing, clearing, or omitting them does not turn a known base preset into **Custom**. Unknown focus/gear combinations show **Custom**, even if the saved Name matches a preset. Existing stage names remain stored; selecting a preset updates the saved name and replaces both priority orders with the explicit orders below, preserving timing, targets, FightOnEntry, cap buffer, plan NGU weights, and unknown fields. Label-only changes no longer restart sequential timing. All panels remain mounted while hidden, preserving duration drafts per logical stage across selection changes, save readback, reordering, and tab changes.

| Base Preset | Energy Priority Order | Magic Priority Order |
| --- | --- | --- |
| Time Machine | `TM, NGU, WAN, AUG, AT` | `TM, RIT, NGU, WAN` |
| Augments + Blood | `AUG, NGU, WAN, TM, AT` | `RIT, NGU, WAN, TM` |
| Wandoos | `WAN, NGU, TM, AUG, AT` | `WAN, RIT, NGU, TM` |
| Energy NGU / All NGU | `NGU, WAN, TM, AUG, AT` | `NGU, RIT, WAN, TM` |
| Magic NGU | Empty (energy untouched) | `NGU, RIT, WAN, TM` |
| AT Adventure | `AT, NGU, WAN, TM, AUG` | `RIT, NGU, WAN, TM` |
| AT Wandoos | `AT-3, AT-4, WAN, NGU, TM, AUG, AT` | `WAN, RIT, NGU, TM` |

**Add stage** applies Time Machine, and both stage templates use these shared preset orders. Wandoos starts with Wandoos explicitly rather than relying on the backend's implicit first-half AT allocation; use the separate AT Wandoos preset to train Wandoos AT first. Existing saved orders are not rewritten merely by opening the editor.

Live attribution still requires the same plan file and version, stage count and order, completion modes, effective durations, focuses, gear, configured priorities, AT targets when applicable, scheduler mode, and repeat boundary. A reported revision must match; an omitted revision still requires the full sequence match. Legacy configured arrays may be empty for focus defaults; effective runtime `energyPriorities`/`magicPriorities` may differ. Neither configured nor effective priorities determine base preset labels. Missing configured metadata or pending plan/timing edits hides live attribution, but does not change a known base preset's label. Runtime `planName` identifies the plan file even if its stored Name differs. Telemetry never loads another plan or navigates to a stage.

Stage buttons support **Left/Right**, **Home**, and **End** to move selection and focus. A single click or touch tap toggles the panel; a horizontal drag reorders without toggling selection. Tiny touch movements remain taps, while vertical gestures allow page scrolling. **Add stage** creates a new segment and opens its panel. Inside the panel, edit the preset, completion, duration or AT targets, energy/magic focuses and priorities, and **Fight on entry**. Gear is set automatically when selecting a focus preset; there is no per-stage Gear goal control. Existing custom stage gear remains unchanged until an explicit preset action, and plan-level **Gear settings** remain available. **Repeat from here** toggles the loop boundary; **Remove stage** deletes that stage. Reordering follows logical stage identity, including the selected panel, duration drafts, and repeat boundary.

Mouse, touch, and pen use the same pointer-capture drag path, with a 7-pixel movement threshold before a horizontal drag starts. Stage buttons have `draggable="false"`; reordering does not depend on HTML5 `dragstart`, `dragover`, or `drop`. Escape, plan replacement, or a namespace change cancels a drag without saving a reorder.

Use **Stage timing** above the bar to convert all timed stages to **Time (m / h)** or **Percent (%)**, preserving their effective seconds. Enter durations directly as `25m`, `35m`, `2h`, `90s`, or `30%`. Bare numbers retain the displayed suffix. Enter or blur commits a duration; Escape cancels its draft. Seconds must be whole and positive. Legacy mixed schedules remain readable: percentages split the budget left after fixed durations. Open **Schedule & templates** for **Schedule length**, also accepting `30m` or `2h`, the repeat selector, and templates. Fixed-only schedules use the sum of their stage times. Version 14 plans must be migrated by the injector before using fixed times.

**AT Adventure** trains adventure power/toughness. **AT Wandoos** trains Wandoos AT first, then funds Wandoos and later energy priorities in the order shown above; its magic starts with Wandoos before rituals and NGUs. Selecting AT power/toughness completion applies AT Adventure; change completion back to Duration before selecting another preset. Restoring timed completion uses the saved fixed duration, or five minutes if none was saved, without taking time from other stages. The 6k/6k template uses untimed AT Adventure followed by timed Energy NGU; the NUMBER template is an exact 30-minute cycle.

Cap buffer (`BlackBarLookaheadSeconds`, formerly **Ahead**) is hidden entirely from normal stage controls. Stored values remain untouched, including when selecting a preset, and can be edited through **Advanced plan JSON**. It reserves enough resources to keep training at maximum speed between allocation refreshes; it does not delay stages. With no AT-target stages, the schedule follows elapsed rebirth time. Adding AT-target completion uses sequential timing from activation. Without repeat, the final stage remains active, and rebirth triggers remain independent of stage completion.

**Fight on entry** is optional per stage (`FightOnEntry: true`, omitted means false; no plan version change). Enable it on the stage immediately *after* each short or later Augments stage to fight with those gains. It waits for gear/digger locks, then uses manual-style fight gear, Fu Manchu, stat digger, and boss push without buying digger upgrades or consuming power fruits. The plan and auto-rebirth switches stay unchanged. After the push, the current stage's gear goal is re-equipped before allocation; **Keep gear** leaves fight gear equipped. Stage time keeps running, so short stages can be skipped during a push or lock wait. Attempts are remembered across plan cache reloads, repeat on each loop entry and after rebirth, and reset on injector reload; individual prep failures are logged, not retried within that entry. Existing built-in/runtime plans are not changed automatically. Reload the rebuilt injector and rebuild/reopen the desktop UI to use the new control.

Click a blood goal, Iron Pill target, or Augments + Blood duration to edit it in place. Enter or **Save** commits that value; Escape or **Cancel** closes the editor. Direct saves do not commit unrelated drafts. Invalid or failed saves keep the edit available. Rebirth targets instead use the plan editor's numeric autosave inputs. Current game readings and cast history are read-only.

Manual gear selection, augment tools, and diagnostics are under **Misc**. Setup and legacy controls are collapsed there. Technical data belongs in expandable details or **Advanced**; raw Plan JSON is collapsed near the bottom of Dashboard, before **Plan library**. Keep primary copy short: action, current state, or a relevant blocker.

Settings and plan edits save automatically. Settings toggles and selects save immediately; numeric settings and plan edits wait 350 ms after the last edit. Writes are serialized, and edits made during a write remain queued. Invalid input and failed writes stay local, with a **Retry saving** action instead of automatic replay. Advanced JSON remains a draft until **Apply edits**, then saves automatically. The plan editor, NGU weights, and Plan JSON buffer remain mounted but hidden off Dashboard, so tab changes do not discard local drafts or reset the expanded Plan JSON details. Settings JSON likewise remains mounted across tab changes. Refreshes preserve pending edits, and selecting another plan flushes pending saves first. AT targets use exact decimal integers; enter `6000`, not `6k`. Built-in shortcuts select for editing only.

Uncommitted duration and schedule-length drafts also participate in discard confirmations and the window-close warning, and prevent starting the plan until finished or cancelled. Stopping remains available. They do not block autosaving other committed plan fields. Finish with Enter/blur or cancel with Escape before switching plans if you want to keep those timing changes.

Iron Pill retains the existing policy: auto blood spells, the plan run switch, global custom auto-rebirth, and the plan's own auto-rebirth switch must be enabled, with at least one hour elapsed and boss stage 100 reached. Eligibility is separate from cooldown and saving thresholds. The UI displays the current blocker without enabling any of these switches for you. Saving unrelated tuning does not restart the sequential stage scheduler.

## Restart Workflow

To explicitly restart NGU, inject the rebuilt DLL, and open the native UI from the repository root:

```powershell
.\start-ngu-and-script.ps1 -ExternalUi -StartUi -ProbeMode safe
```

This uses the existing cloud-load startup workflow. The game is closed gracefully; an unresponsive close aborts unless `-ForceRestart` is explicitly supplied. The launcher requires a fresh running protocol-v3 heartbeat rather than treating the injection tool's assembly pointer as proof of startup. `loader-startup.log` in the runtime folder records loader/lifecycle milestones for diagnosis.

Every run closes all existing standalone injector UI processes before building, including with `-NoBuild` or without `-StartUi`. This force-closes the UI, so finish any pending edits first. The legacy WinForms UI closes with the game; `-StartUi` opens a fresh standalone UI after injection.

If Steam replaces the first game process during startup, the launcher attaches to the replacement and verifies its heartbeat.

## Safety And Storage

The injector owns `.injector.lock` for its lifetime. Offline writes require exclusive ownership of that same file, so a stalled heartbeat or a running WinForms injector cannot accidentally authorize direct edits.

Connected writes use a protocol-v3 command queue executed on Unity's main thread. Commands have unique IDs, expiry times, persistent at-most-once claims, and completion results. A timeout cannot undo an action already running; inspect state/results before retrying.

Plan reads are normalized by the C# implementation when connected. Each read includes a SHA-256 `__externalUiRevision` token. Saves compare it at the actual write boundary and reject stale revisions; history updates also change the revision. This token is transport-only, never stored in the plan. Offline legacy plans must be connected to migrate before editing.

Unknown stored fields are preserved. Plan JSON crosses native IPC as text and uses lossless parsing so unknown large numbers are not rounded by JavaScript. Settings use key patches; unknown settings keys are retained but cannot be changed by an older injector. Runtime history remains backend-owned.

Data stays in the existing folder:

```text
settings.json
profiles/*.json
custom-plans/*.json
logs/*
runtime.json
commands/*.json
commands/claimed/*.claimed
command-results/*.json
```

Claim receipts intentionally remain to prevent replay. Do not delete receipts or reuse command IDs while an injector is running. Arbitrary program execution and general filesystem access are not exposed by the bridge.

## Development Boundaries

- `src/lib/api.ts`: typed native IPC and lossless plan transport.
- `src/lib/controller.ts`: session, pending edits, guarded commands, and save handling.
- `src/lib/model.ts`: pure validation, reordering, conflict handling, and heartbeat rules.
- `src/lib/*Editor.svelte`: declarative editors; `App.svelte` composes the control surface.
- `src/styles.css`: responsive, monochrome visual system without designer files.
- `src-tauri/src/main.rs`: bounded filesystem operations, ownership, queue/result handling, and Rust unit tests.
- `../NGUInjector/Main.ExternalUi.cs`: Unity-thread command dispatch and runtime publication.

WinForms is retained for fallback and older controls. This replaces the active custom-control UI, not the C# game engine or every legacy designer-only page. Allocation profiles can be selected here; their legacy JSON editing remains external.

## Verify

```powershell
npm run check
npm test
npm run build
cargo fmt --manifest-path src-tauri/Cargo.toml --all -- --check
cargo test --manifest-path src-tauri/Cargo.toml --locked
cargo check --manifest-path src-tauri/Cargo.toml --locked
npm run test:ui
```

Browser smoke tests use mocked native IPC, not a live game. For their first run, install Chromium with `npx playwright install chromium`. Live Unity actions and WebView integration require a separate manual smoke test with a disposable save; automated checks do not alter the game.

The native WebView2 failure was confirmed in an isolated offline app using Windows `SendInput` and UI Automation: mouse pointer events arrived, but HTML5 `dragstart` never fired. The old mouse path waited for that event and therefore never started reordering. Unified pointer capture with native HTML dragging disabled fixes this dependency; native-app testing saved both the reordered plan and its updated repeat index. Chromium tests alone do not establish native WebView2 behavior.

Keep `app.windows[].dragDropEnabled` set to `false` in `src-tauri/tauri.conf.json` so Tauri's native file-drop handler does not compete with web interactions. This app does not use native file drops. That setting alone does not fix the missing HTML5 drag start, and stage reordering no longer relies on HTML5 drop handling. `tests/native-config.test.ts` guards the setting; native configuration changes require rebuilding the native executable, not just the frontend.

The browser suites run desktop and mobile projects and cover the bar-first Dashboard editor, collapsed controls and plan library, click/tap/close and keyboard selection, whole-segment mouse/touch dragging, mouse reordering with HTML5 drag events blocked and zero native HTML5 events, drag cancellation on Escape/namespace/plan replacement, tiny touch movements and held-edge scrolling, stage add/reorder/repeat/remove, preset gear and stored cap/FightOnEntry preservation, autosave persistence and retries, NGU weight validation, raw JSON Apply, logical-stage and tab-local draft preservation, exact runtime-sequence gating, and runtime/editor independence. The fixture intentionally returns one shared plan body for `read_plan`; the controller tracks the requested editor name. Create/copy/delete checks exercise native requests and visible plan selection/list updates, not backend file persistence or distinct per-plan readback.

The root verification tool also runs the package-free C# bridge suite:

```powershell
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj
```

That suite links the real bridge, lifecycle, settings, and plan-persistence sources. Unity/game-action boundaries are stubbed; see `checks/ExternalUi/README.md` for the limits of those assertions.

## Blood Goal Estimates

The dashboard and numeric settings show current/saved bonus goals, remaining blood, and estimated time to each goal assuming all available magic is assigned to the optimal sustainable blood ritual. The WinForms Automation tab shows the same estimates. The read-only `BloodMagicOptimizer.OptimizeBest()` preview selects the best unlocked/affordable ritual with positive net gold using total available magic, current gear bonuses, and gold income; it does not change allocations. Calculations invert the game's bonus formulas and include each enabled spell's share of unspent blood and projected full-magic production, not the current ritual allocation. A 15% Spaghetti goal requires 163,840,000 total invested blood; 100% Counterfeit Gold requires 512,000,000.

Estimates respect each spell's minimum blood per cast and assume the projected all-magic allocation and current auto-spell split stay unchanged. They do not predict spell split changes when another goal finishes, Iron Pill consumption, or rebirths. Paused spells and optimizer failures have explicit statuses rather than misleading countdowns. The UI identifies the chosen ritual by its zero-based index, matching optimizer diagnostics, and shows the full magic budget and projected per-spell production. Pending external UI edits do not change estimates until saved. A rebuilt injector must be loaded to provide the new `runtime.json` telemetry or WinForms labels.

## Iron Pill

The settings editors persist `_customIronPillSaveSeconds` (integer 0..604800, default 900) and `_customIronPillMinWorth` (finite double 0..1e8, default 0). Actual pill worth is the base Power and base Toughness gained each, capped at 100 million (1e8); higher worth-only targets are unreachable and could block automatic rebirth indefinitely. While eligible, cast when ready and either enabled threshold is reached. Zero disables that threshold; both zero cast immediately when ready. Eligibility uses actual elapsed rebirth time >=3600s and boss stage >=100, plus auto blood spells and the existing active Custom plan resource automation requirements. Reserve blood while ready or within the saving-time cooldown lookahead; worth-only mode uses a 900s lookahead, not a fixed casting delay. Reservation yields to manual rebirth and boss push.

The dashboard, settings page, and WinForms Blood Spells group show the latest ten successful automatic casts from persisted settings history. The backend owns `_customIronPillHistory`, an array of entries with public fields `CastAtUtc` (UTC string), `WaitSeconds`, `Worth`, and `RebirthSeconds` (doubles). History is read-only and never submitted in user settings patches. Frontend and Rust validation reject history patches, including offline saves. An untouched stale advanced JSON history uses the latest snapshot rather than overwriting it.

Backend integration must provide `SavedSettings.CustomIronPillSaveSeconds`, `CustomIronPillMinWorth`, and `CustomIronPillHistory`, normalize/load/copy/persist them with a 1e8 worth limit, reject history edits in C# settings patches, and record only successful automatic casts. The backend must protect manual rebirth/boss-push prep from reservation and persist history without a full WinForms settings reload. The UI does not implement casting, reservation, or history recording. WinForms refreshes only read-only history in its runtime status loop, not the settings editors.
