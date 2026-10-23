# Plan Blood Magic

Blood Magic controls on the desktop Dashboard and in the in-game settings window edit the selected plan. Each plan can independently configure:

- Automatic blood spell management.
- Blood Spaghetti loot-bonus goal and Counterfeit Gold gold-bonus goal. A goal of zero disables that goal.
- Automatic Iron Pill permission.
- Iron Pill saving time and minimum worth. Either enabled threshold can trigger a cast; zero disables that threshold, and both zero attempt a cast as soon as it is ready.

The desktop card identifies both the editing plan and the runtime scope. Editing a different plan does not apply its settings to the running plan. Live estimates and Iron Pill status are hidden when they belong to another plan or to Quick mode. Successful Iron Pill cast history remains shared across plans.

The Dashboard keeps Iron Pill saving-time and minimum-worth controls plus live blockers, saving status, and retry delays; it has no casting-rules disclosure. The last ten successful automatic casts appear in a dedicated **Iron Pill** section under **Logs**, labeled **All plans**, rather than on Dashboard. History is read-only and uses the backend-owned settings snapshot.

## Existing Settings

The five new plan fields are optional: `AutoBloodSpellsEnabled`, `BloodSpaghettiGoalPercent`, `BloodCounterfeitGoalPercent`, `IronPillSaveSeconds`, and `IronPillMinWorth`. Missing or null fields inherit their corresponding legacy global settings, preserving existing configurations without rewriting plan files on reads. Explicit `false` and zero are saved overrides. Desktop **Use legacy defaults** clears those five overrides, but does not change the existing `AutoIronPillEnabled` permission, which defaults to true.

Only a running plan supplies overrides to automation. Quick mode and no-running-plan operation retain the legacy global behavior, regardless of which plan is open in the editor. Switching running plans starts a separate Iron Pill saving window rather than inheriting the previous plan's timer. All existing unlock, cooldown, auto-rebirth, resource-management, and preparation guards still apply.

## Persistence And Telemetry

Overrides use plan schema 17 and survive connected saves, offline saves, and plan copies. The injector advertises `runtime.planBloodMagicVersion: 1`; connected desktop edits require that capability. Numeric validation is consistent across the frontend, C# store, and Rust offline store: whole-number goals from 0 to 100000, whole-number saving seconds from 0 to 604800, and finite worth from 0 to 100000000.

`runtime.bloodGoals.planName` and `runtime.ironPill.planName` identify the running plan file, or are empty for legacy-global operation. Blood telemetry includes effective `autoBloodSpellsEnabled`; Iron Pill telemetry includes effective `saveSeconds` and `minWorth`.

## Verification

- `NGUInjector.UI/tests/plan-blood-magic.test.ts`: optional-field validation and boundary values.
- `NGUInjector.UI/e2e/plan-blood-magic.spec.ts`: actual Dashboard/controller edits, independent plans, inheritance, capability guards, and desktop/mobile behavior using mocked native IPC.
- `NGUInjector.UI/src-tauri/src/plan-blood-tests.rs`: offline normalization, validation, persistence, copying, and clearing overrides.
- `checks/ExternalUi/PlanBloodPersistenceChecks.cs`: C# plan persistence and legacy fallback contracts. Run the ExternalUi harness with `--plan-blood`.
- `checks/SustainedFocus/BloodGoalPriorityChecks.cs` and the compiled `blood` checks: effective runtime goals, automation guards, plan transitions, and reservation behavior against offline fixtures.
- `checks/WinFormsBlood`: compiled in-game control handlers with real plan persistence, without launching the full Unity settings window.

These checks do not replace live Unity/game verification.
