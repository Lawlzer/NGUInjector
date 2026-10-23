# UI/UX Simplification Plan

## Status And Goal

Requested follow-up proposal, grounded in current components and navigation. It is
not an implemented visual redesign or evidence that the executor cutover is done.
The actual redesign should follow a verified authoritative execution contract.
Source review was performed; no screenshot-based visual validation is claimed.

Goal: fewer places to visit, fewer overlapping concepts, clearer live truth and
more room for the task being performed. Keep functionality, safe drafts, exact
values, accessibility and command protection; remove presentation clutter.

## Current Friction

`App.svelte` exposes Dashboard, Plan & Rebirth, Presets, Adventure, Misc, Logs and
Advanced. Misc combines roughly eight unrelated areas; Advanced contains another
large collection. Navigation follows implementation categories more than tasks.

Plan building crosses destinations: stages live in the plan editor, the default
preset lives in Misc, and create/copy tools live in Advanced. Dashboard mixes live
monitoring with configuration for the browsed plan, which need not be the running
plan. Quick preset tiles execute and open an editor, while inspection is a separate
workflow. A large library turns the operational view into an expanding tile list.

Good existing behavior to preserve: one mounted preset editor/library, one selected
stage settings panel, field validation, lossless large numeric values, serialized
saves, conflict-aware rebasing and retained in-flight/invalid drafts.

## Navigation

Use four primary areas with local task destinations, not four giant scrolling pages:

| Area | Contents |
| --- | --- |
| Run | Actual controlling task, stage/phase, verified execution, resource allocation, meaningful blockers, bounded Quick launcher and explicit actions |
| Plans | Plan browser/create/copy, default stage preset, stage editor, rebirth conditions, plan Blood/Iron Pill settings and templates |
| Automation | Local Presets, Adventure, Resources and Maintenance workspaces |
| Activity | Command outcomes, rebirth/Iron Pill history, filtered logs and diagnostics |

Connection/data folder and advanced utilities belong in a small app menu. Add a
searchable command palette for destinations, tools, plans and preset inspection.
Opening a search result must not execute a mutation unless explicitly labeled and
confirmed where warranted.

## Before And After

| Current | Proposed |
| --- | --- |
| Dashboard Quick plus full inline Presets editor | Compact Run launcher; inspect in a contextual surface or open the full Presets workspace; explicit Use/Stop separate from inspection |
| PlanGear in Misc | Default stage preset beside plan identity and stages |
| Plan creation/copy in Advanced | Plan browser actions with a small create/copy dialog |
| Dashboard Blood/Iron Pill configuration | Plan settings own editing; Run shows the actual running/global result and a scoped edit link |
| NGU weights and allocation as separate cards | Resources workspace aligns desired ratios, last applied ratios and observed amounts |
| Boost enablement on Adventure and boost priorities in Misc | Maintenance > Boosting combines related controls |
| Adventure rules/manual bonuses/titans spread across cards | One Adventure task workspace with compact summaries and focused editors |
| Zone reference at bottom of Misc | Contextual reference beside zone selection, plus an independent read-only entry |
| Combat Prep planner and runtime buried in Advanced | Full Combat Prep workspace reached from Run/Automation; separate planning and live detail |
| Logs, histories and recent outcomes in different locations | Activity with filters and correct plan/all-plan scope |

Remove Misc and the Advanced card dump after relocating functions. Remove duplicate
stage-jump/repeat controls. Show preview results when they exist instead of permanent
empty result cards. Do not delete retained serialized settings or unknown fields as
part of presentation cleanup.

## Run Screen

The primary screen should answer four questions in order:

1. What is controlling the game?
2. Is that requested work actually applied?
3. What is waiting, blocked or expected next?
4. Which explicit action can I take?

Use one strong execution panel, compact observation rows, attention only when
needed, and a bounded searchable Quick launcher. Make Stop running plan accessible
even while a different plan is being edited. Distinguish editor selection from live
position and enabled intent from verified execution. Do not use one green state
for all of those concepts.

Allocation should show separate ordered Energy/Magic lanes and observed funding,
including why lower priorities are dormant. Read the executor's authoritative
projection; do not build a frontend ownership resolver or infer success from saved
configuration equality. Legitimate fully capped idle capacity is not a red alert.

## Editing And Popups

Use the right surface rather than making everything a popup:

| Surface | Use |
| --- | --- |
| Inline edit | One threshold, duration, target or toggle |
| Docked non-modal inspector | Selected stage, allocation detail, candidate equipment/support |
| Full workspace | Plan construction, preset definitions and Combat Prep alternatives |
| Small modal dialog | Create/copy, permanent deletion, replace-all-stages, explicit conflict resolution |
| Mobile sheet/full-screen detail | Contextual inspector when side-by-side space is unavailable |

One contextual surface at a time. Non-modal inspectors do not trap focus; modal
dialogs contain focus and return it to the invoker. Confirm destructive or
irreversible consequences, not routine editing, Preview or navigation.

Keep specific confirmations for rebirth, deletion, replacing all stages, unload,
mutating probe commands and genuine unsaved-work discard. Preserve the concise
Quick takeover consequence: it pauses plan/auto-rebirth. Do not add generic
confirmation dialogs as substitutes for clear action labels.

## Visual Direction

Use a precision instrument-panel aesthetic rather than a wall of equal cards:

- Warm graphite canvas, slightly lighter working surfaces, restrained borders.
- Sea-glass accent for selection and primary actions; amber Energy and muted violet Magic where those distinctions carry meaning.
- Strong central execution readout; quieter secondary configuration; fewer nested card outlines.
- Readable 14-16 px working text and tabular numerals. Monospace only for exact values/technical details.
- Distinguish live state, selection, warning and error with text/position/shape as well as color.
- No decorative subtitles, boilerplate helper paragraphs, routine saving badges, glass effects or charts without a decision they support.

Keep the existing focus visibility, dark-theme foundations, reduced-motion support
and responsive behavior. Validate actual contrast, density and clipping with
screenshots before claiming the new appearance is better.

## Draft And Data Safety

Lift drafts by folder/entity/logical-stage identity or preserve mounted editor
lifetime. Navigation, closing an inspector, polling and reference selection must
not equip, restart, preview or discard work. Closing an autosaved editor is not
undoing saved changes; label actions accordingly.

Keep field-local validation, conflict-aware nonoverlapping merges, exact numeric
tokens, unknown JSON fields, revision/entry checks and command at-most-once
semantics. Conflict UI should show local/latest values for the affected fields,
not offer a blanket force-overwrite. A timed-out command is not automatically
replayed to make the interface look responsive.

Audit hidden active legacy conditions and README/source drift. A non-default
condition that still affects execution needs a concise summary and an editing
route, not silent preservation behind a misleading simplified form.

## Accessibility And Native Boundaries

Provide keyboard completion, focus restoration, accessible disabled reasons,
tap-accessible exact values and visible alternatives to drag-only stage ordering.
Do not announce every polling update. At narrow widths, stack task workspaces and
keep horizontal overflow local to real tables. Validate 200% zoom and long names.

Browser mobile-size testing is not native mobile support; the existing desktop
Tauri window has a minimum width. Verify WebView2 focus, folder dialogs, close
protection and pointer capture separately from mocked browser IPC. New navigation
or custom dialogs do not require broad filesystem/shell permissions.

## Delivery And Acceptance

1. Settle executor telemetry and retained behavior contracts before redesigning live status.
2. Extract Run, Plans, Automation and Activity views while preserving controller authority and draft lifetime.
3. Move related workflows together; split launcher/editor/results and planner/runtime presentation.
4. Add contextual inspectors, small dialogs and searchable navigation.
5. Apply visual hierarchy; delete superseded surfaces/styles and update docs.
6. Validate real screenshots, keyboard/touch flows and the native desktop app.

Acceptance includes: create/configure/start a plan without unrelated pages; browse
Plan B while Run remains about Plan A; no mutation from inspection/navigation;
draft/conflict safety across remount/reconnect; truthful Applied/Waiting/Blocked/
Unavailable states; no frontend-invented timing; no replay after command uncertainty;
correct dangerous-action confirmation; accessible desktop/narrow layouts; native
permission and close-protection verification.

Success means fewer places to visit and fewer concepts to remember, not fewer
capabilities, weaker checks or more hidden state.
