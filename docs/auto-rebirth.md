# Auto-Rebirth Defaults

Fresh global settings and newly created custom plans default auto-rebirth permission
to on. Missing serialized fields use the same default; explicit saved `false` values
remain off through loading, saving, unrelated settings patches, and plan copies.
Existing files are not rewritten just to apply defaults.

The `infinite` built-in deliberately keeps auto-rebirth off, including when its named
template is loaded without an explicit permission field. Copies retain the source's
choice. The `default`, `24hr`, and `1h-3m-rebirth` built-ins permit auto-rebirth.

## Controls And Live Permission

`Allow auto-rebirth` in Rebirth conditions is the saved plan's only rebirth switch.
`Enable plan` / `Disable plan` sits beside Running / Stopped in the plan card header;
the plan selector is below it. Enabling a plan honors its saved rebirth permission.
Disabling a plan turns live auto-rebirth off without changing that saved choice.

Connected `savePlan` commands synchronize live `_autoRebirth` only when the stored
`AutoRebirthEnabled` permission actually changes, and only if that same plan is
already running when the command executes on the game thread. The comparison uses
the authoritative stored plan before and after saving, not the editor baseline.
Unrelated saves preserve the live switch in either direction, including a native
live opt-out while the saved permission remains on. There is no follow-up UI
settings patch, new protocol intent, implicit plan start, plan selection change,
or change to legacy `GlobalEnabled`. Offline saves and saves of stopped or other
plans do not change live switches. Use the updated injector for this behavior.
Autosave still confirms enabling the saved permission. The controller consumes
acknowledged live flags and refreshes live status on save failure without replacing
the checkbox draft or its saved baseline.

If the plan write succeeds but the live settings write fails, the error remains
visible and the checkbox edit stays pending. A successfully saved permission of
`false` blocks rebirth even if live `_autoRebirth` remains `true`. Retrying the same
plan save does not replay live synchronization, because there is no new stored
permission transition; in particular it cannot silently enable rebirth. To apply
the saved permission after resolving the write failure, explicitly Disable plan
and then Enable plan, which honors the saved choice. To retry enabling without
restarting the plan, reload the saved plan, uncheck and save the permission, then
check it again and confirm. Do not toggle the permission on to recover a failed
disable; keep it off and explicitly Disable plan instead.

Permission does not start plan execution: `UseCustomRebirthPlan` and legacy
`GlobalEnabled` still default off.

## Retired Controls

Plan & Rebirth keeps rebirth conditions, stages and schedule templates together.
Budget/fixed/flexible summary badges are removed; each flexible stage shows its
nominal assigned time and normalized share directly in the stage bar. These are
configured estimates, not live time remaining. Exhausted/overrun warnings remain.

Rebirth history is a read-only table in Logs, labeled with the selected plan.
It shows local timestamp, reason, duration, boss, NUMBER before/after and gain,
with full records in closed Details disclosures. It uses the loaded plan history
without changing plans or committing drafts. Raw plan JSON and plan-library tools
are now collapsed under Advanced rather than appearing in Plan & Rebirth; their
buffers and the main plan editor remain mounted across tab changes.

The NUMBER-after-minimum-time (`NumberClimb`) condition is retired. New plans
default it off and migrations no longer enable it. Its serialized enabled, age,
and multiplier fields remain compatible with persisted plans, but are ignored in
trigger evaluation, readiness, NUMBER confirmation, and rebirth reasons, even if
a legacy file says enabled. Only Time and NUMBER participate in Any / All matching;
a plan with only the retired condition enabled cannot trigger auto-rebirth.
Existing NUMBER confirmation, protected-work, and never-lower-NUMBER guards remain.

The maxed prep fruit preference is no longer shown in either rebirth editor. Its
saved `PrepRequireMaxedFruit` value and runtime behavior are unchanged.

Backend persistence regressions run with
`dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj` on Windows.
They use production settings/model/store code with the suite's Unity JSON boundary,
including missing-field initializer bypass; they do not run live Unity startup.
