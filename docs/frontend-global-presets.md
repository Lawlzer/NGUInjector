# Global Preset Frontend

The UI has one controller-owned global library draft and saved baseline, independent
of the browsed plan. Dashboard Quick and the Presets tab render the same mounted
`Presets.svelte` instance. Switching tabs or plans retains library drafts and previews.
Dashboard tiles apply their saved preset on click and open details underneath. Clicking
the active dashboard-owned preset again sends `stopFocus`, releasing Quick ownership
and its managed allocations without restoring prior gear or resuming the plan. Stop
does not require saved/valid library drafts or Equip capability, only a fresh session
supporting `stopFocus`. Tiles highlight only active dashboard-owned presets, never
presets applied by a plan. Clicking a matching plan-owned preset takes manual ownership,
disabling the plan and auto-rebirth. The details panel's Equip button always equips, never toggles. Closing
details does not reapply or discard drafts. Library management lives in Presets;
selection there, navigation, rendering and saving never apply equipment. Plan-level
active preset and fight scoring controls live in Misc > Gear settings.
Equipping, applying, refreshing or stopping a preset preserves the Adventure Goal
enabled setting, including an explicit manual disable. Preset routing ownership
does not change that preference.
Dashboard's Inspect preset selector opens the same editor without equipping or
stopping anything. Both views expose discard for pending library edits.

Explicit Equip can interrupt scheduled combat prep. It safely retires its still-owned
route and releases narrow borrowed settings/resources, then applies current preset
intent directly. It does not restore historical gear or support. With no explicit
return baseline, unclaimed setup remains unchanged. Foreign gear/support requires
the existing freshly preflighted reviewed Equip/Preview+Apply boundary.
The October 22 offline combat-slice acceptance closed the empty/refill handoff,
paused borrowed-field and active-focus admission blockers. See
`combat-executor-verification-20261022.md` for that historical evidence and its
follow-up. This does not establish the full executor release/live gate; removing
the restoration UI is not proof of full release acceptance.
The interrupted stage retains its entry and original deadline rather than being skipped
or receiving a new time budget. Live pending cleanup temporarily blocks Equip;
checkpoint-only recovery now reconciles automatically. Manually started combat prep
requires explicit cancellation. Invalid library revisions,
unavailable objectives and missing cached policies are rejected before interruption.
If later search or preflight fails, the plan remains enabled and can resume the same entry.

Zone reference lives in Misc as a separate read-only tool, available without a selected
plan or preset. Its zone selection persists across tabs and does not change farming
settings. Recommendations show Power, Toughness and HP side by side, with wiki sources
and source notes kept behind a disclosure.

Normal pages prioritize controls, results and actionable blockers. Do not repeat obvious
input semantics or describe storage, scheduling contracts, cache policies and other
implementation details beside each control; keep those explanations in documentation.
Preset setup shows only the Auto-update control, without cache-policy helper text.
The last-applied card retains its name, status and actionable change notices, but omits
the raw application timestamp and acquired-policy auto-update description.
The full-width editor precedes results. Goals use a compact table of active weights
with Add goal and Remove actions; removal stores zero. A weight being edited to zero
stays visible until removed or another preset is selected. Invalid drafts remain editable.
Add goal offers Dynamic adventure instead of new fixed PP, Boosts or Zone goals.
Previously saved fixed farming goals remain visible and editable, including combinations.
Dynamic adventure is an ordinary weighted goal in the goal table, not a separate
add-preset action. It can be combined with non-adventure goals without creating a new
preset or rewriting existing farming definitions.
AT ratios appear beside goals only while the Advanced Training weight is positive;
disabling AT preserves its ratios. If an invalid ratio draft remains while AT is disabled,
a Reset AT ratios action offers recovery without enabling AT or silently discarding edits.
Beards and diggers share a responsive two-column layout that stacks on smaller screens.
Keep the Quick takeover warning and actionable
validation, stale-preview and re-equip notices, without explanatory control copy.
Minimum adventure stats have their own disclosure. Farming-zone, blood-share and
swap-overhead inputs are no longer exposed. Non-default values in existing definitions
remain visible in a reset-only Saved overrides row; Reset to automatic explicitly restores
the shared defaults (auto zone, auto blood split, five-second forecast setup cost).
Unrelated edits do not silently rewrite existing overrides or change acquired policies.

## Native Contracts

- `read_preset_library({runtimeDir})` returns a JSON string containing
  `{Version:1, Presets:[], __externalUiRevision:<lowercase SHA-256>}`.
- `save_preset_library({runtimeDir, libraryJson})` receives lossless JSON including
  that revision. A successful command result is followed by a validated library readback.
- `previewGlobalPreset` and `equipGlobalPreset` accept only
  `{presetId, libraryRevision}`. Apply additionally requires `{previewId, candidateKey}`.
- Actions require a fresh same-folder injector session advertising
  `globalPresetsVersion:2`, `globalNguWeightsVersion:1` and the relevant command. No action saves other drafts.
- Global weights have fourteen positions: energy NGU, magic NGU, blood, PP, boosts, zone,
  Time Machine, augments, Wandoos, advanced training, adventure power, adventure toughness,
  energy cap and magic cap. Legacy six-, ten- and twelve-entry weights pad
  with zeroes. Presets own five AT ratios only. Nine energy NGU and seven magic NGU weights belong exclusively to Misc global settings, not plans or presets.
- Adventure power/toughness priorities maximize immediate projected stats through gear
  and support. They are separate from AT training priorities and do not select a farming route.
- Energy Cap and Magic Cap presets enable Auto-update, retain current support, and give
  only their respective total-capacity objective a positive weight. Cap goals use immediate
  projected native capacity, not currently filled resources or production rates.
- Optional `AdventureDynamicWeight` is numeric, defaults to zero, and accepts 0..1000000.
   A preset needs a positive fixed weight or a positive dynamic weight. Runtime adds the
   full weight to the current Adventure farming metric (PP index 3, Boosts 4, Zone 5),
   without adding a separate power priority or replacing fixed weights. Adventure mode
  and rules determine the task. Boost/zone metrics already account for drops and kill speed.
  The dynamic task controls the single shared farming route, overriding a saved Zone
  constraint; large blends scale uniformly to preserve ratios within execution bounds.
  Auto-update treats changed tasks as re-equip events, while unchanged refreshes do not
  search. Disabled Auto-update requires a compatible cached task or explicit Preview + Apply.
  See `adventure-automation.md` for ownership, manual deferral and rule-feedback handling.
- Dynamic Preview, Apply and Equip require runtime `adventureDynamicPresetsVersion:1`,
  checked in the controller and native command queue; fixed presets remain usable without it.
  Requests still use only committed ID/revision and, for Apply, the preview token/candidate.
  Dynamic previews must carry `effectiveWeights` (14 finite nonnegative numbers),
  `adventureTask` (label), and `adventureTaskKey` (string). Results use effective weights
  for metric applicability and show the resolved task; Last applied shows the runtime task.
  Fixed backend previews also publish `effectiveWeights`, without task metadata. Weight
  validation is independent; both task strings are required for dynamic definitions or
  whenever either task field is present. Global validation uses the committed definition
  held by the controller, not an embedded response preset. Saved/readback and applied
  comparisons normalize omitted `AdventureDynamicWeight` to zero.
- Presets have no duration. Plan stages own execution timing. Scheduled acquisition uses
  the stage's remaining time as its forecast context (bounded to 1..604800 seconds).
  Untimed AT stages use the remaining plan horizon, falling back to a one-hour forecast
  once that horizon is exhausted. Standalone Preview/Equip uses a one-hour forecast,
  not an expiry. Allocation refresh retains the acquired forecast context without searching.
  Legacy `HorizonSeconds` is ignored and removed on normalization/save.

Migration also recognizes exact receipts from the immediately previous preset schema:
ten normalized weights and `HorizonSeconds` defaulting to 3600, or the original valid
integer from the retained legacy definition. This identity-only fallback preserves
edited imported rows and deletion tombstones without rewriting receipts or restoring
the old saved schema. All other validated fields and unknown JSON tokens still enter
the hash; twelve-entry source definitions and invalid legacy horizons do not acquire
an old-schema alias. Invalid horizons remain accepted and ignored by the current schema.

## Editing And Safety

Library edits autosave after a 350 ms pause, without enqueueing equipment commands.
Preview, Apply and Equip wait for the saved library. Saves merge external changes by stable preset ID,
reject overlapping edits, and retain newer per-field edits and row additions/removals
across pending writes and readback. Unknown JSON fields and large numeric tokens
remain lossless. Reload merges rather than discards drafts; explicit discard asks first.
Known double fields normalize by runtime value so .NET round-trip formatting does
not cause false readback failures. Unknown numeric tokens retain exact text.
Folder navigation and unload protection include library dirtiness.
Invalid values remain local until corrected; failed writes retain drafts and expose
Retry saving rather than replaying indefinitely. Newer edits made during a pending
write are queued after readback, including reversions to the previous saved value.
Normal settings, plan controls, inline Blood/Iron Pill values and valid advanced JSON
also save automatically. Advanced JSON retains incomplete or conflicting text locally.
Saving an unchanged library is an explicit controller no-op: it performs no native
read/write, keeps the library revision and preview token, and does not enqueue an action.
Polling, tab navigation and reopening preset details never optimize or re-equip a
policy, regardless of the saved AutoUpdate setting.

Energy and magic priority orders use named selectors with accessible Up/Down
and Remove controls, not token entry. Empty legacy orders retain automatic/focus
defaults. Stage completion choices are Time, % of remaining, and Advanced training
targets; remaining-time values are relative weights, not literal percentages.

New presets and definitions without a support mode default to Optimize support
(`SupportMode: "planned"`). Explicit Keep current choices remain unchanged. Migration
recognizes historical Keep current import receipts for implicit defaults so changing
the default neither duplicates edited imports nor resurrects deleted definitions.

`AutoUpdate` defaults to true, but explicit false remains false. On means optimize
when re-equipped, not on a timer. Off asks the backend to reuse a valid session policy;
the user must Preview + Apply to seed a missing policy. The frontend never silently
substitutes optimization for an unseeded cached Equip request.

Plans select saved global definitions through `ActivePresetId`. Committed stages
using `PresetId:'@active'` require a nonempty `ActivePresetId`; legacy inline plans
without inherited stages may leave it empty. Add stage and template actions require
an explicitly selected saved active definition instead of generating an unsaveable
inherited stage. The inheritance option is disabled until its definition is available.
New stages and ordinary template rows use `PresetId:'@active'`; explicit IDs select another global definition.
Changing the reference preserves timing, completion targets and Fight on entry.
Empty references expose legacy inline editing only on existing stages. Automatic
Augments + Wandoos remains an explicit execution mode, separate from presets.
Its optional stage-level `AdventureDynamicWeight` (finite 0..1000000, absent=0) blends
the current phase's primary objective at 100 with Dynamic adventure at the entered
weight when selecting gear and support. A referenced global preset does not supply
this value. Zero keeps the legacy phase search; positive values use one jointly
scored setup per available phase, without an unscored Current fallback. Schedule
ranking still maximizes projected Augments bonus times Wandoos bonus, and prep alone
controls allocations and timing. See [automatic adventure weighting](COMBAT_PREP_BACKEND.md#automatic-adventure-weight).
AT target completion requires an AT-weighted definition with positive power and
toughness ratios. The AT template selects a compatible saved training definition
explicitly, then inherits the active preset for its remaining flexible row.

Runtime `loadoutPreset.appliedName` and `appliedAutoUpdate` describe the acquired
policy, not newly saved metadata. `savedChangesRequireReequip:true` displays a
single re-equip notice without changing the acquired label
or enqueueing an action. Clear quick and clicking the active Quick tile rely on fresh
backend telemetry to clear the active state; the frontend does not optimistically clear
it. The last-applied receipt can remain visible as historical after stopping.

In Plan & Rebirth, the toolbar has one action; selecting a nonactive stage replaces **Restart current stage**
with **Skip to selected stage**. This uses the existing guarded `setPlanStage`
`set` action for forward or backward navigation; clicking a segment alone never
changes execution. Backward jumps reopen the selected stage and every later stage,
preserving cumulative history and the rebirth clock. The button returns to Restart
when matching live telemetry reports the selected stage active, or selection closes.

Runtime plan progression publishes `activePresetId` on the root, and configured
`presetId`, `resolvedPresetId`, and `presetName` on each stage. Weighted progress
labels and allocation descriptions use only that runtime metadata; retained legacy
focus, gear and priority fields are not execution policy for a weighted stage.
Matching an editor stage requires the same configured binding, resolved identity,
active selection and timing/completion metadata. Missing binding metadata is accepted
only for legacy inline stages. Neither the editor's display name nor its browsed
library is used to infer a live weighted preset name or applied allocation.

## Shared Support Definitions

Manual Adventure's optional **Manual adventure bonuses** disclosure edits three global
`settings.json` arrays of finite doubles in `0..1000000`: `_manualAdventureGearWeights`
has five entries `[Push, Adventure power, Adventure toughness, Drop chance, Gold]`,
`_manualAdventureBeardWeights` has seven entries indexed by beard ID `0..6`, and
`_manualAdventureDiggerWeights` has twelve entries indexed by digger ID `0..11`.
The public C# properties are `ManualAdventureGearWeights`, `ManualAdventureBeardWeights`
and `ManualAdventureDiggerWeights`. Missing arrays default to fixed-length zero vectors;
malformed supplied arrays are rejected rather than resized. Zero means no override.
Positive weights ADD to the base preset's gear/support scores, never replace its goals
or take over the complete setup. These are not preset definitions or per-plan settings.
The controls remain configurable while Manual Adventure is disabled, accept fractions
and `+100`, and use the shared beard/digger catalogs. Numeric edits use the normal
350 ms settings autosave; +100 buttons increment that row's bonus. Reading/rendering
does not write defaults; offline saves fill missing vectors and preserve unrelated settings
and preset files. Existing explicit vectors survive unrelated edits.

Explicit Manual Adventure overlays the running stage or Quick preset without retiring
its resource policy or native AT targets. Keep equipped gear does not run gear search;
a selected combat loadout adds 100 Push weight, plus the configured gear bonuses, to
the acquired base objectives. Gear-only search never applies its allocation result.
Every physical swap verifies retained E/M/R3 capacity at each intermediate slot map;
unsafe swaps are refused rather than recalling resources. The ordinary base allocation
refresh still runs, using actual combat gear/support and its acquired activity policy.
Beard bonuses add to the base ordered rank and digger bonuses add to acquired weights;
zero bonuses do not replace support. Owned support is restored when bonuses are
removed, disabled, or a new base is acquired. Locks defer restoration; foreign edits
and save changes are not overwritten. Preview/Apply remain read-only until application
and require support bonuses to be disabled/restored first; Equip can acquire a new base.
Read-only `runtime.manualAdventureOverlay` reports active/base ownership, loadout,
exact decimal resource totals/idle values, comma-separated allocation lanes, support
selection and equipped IDs for verifying transitions. `available:false` means game
observations are incomplete, never an empty allocation claim.

`NGUInjector/Data/PresetDefinitions.json` supplies preset defaults, the initial
library catalog, objective labels/order, beard/digger names, and digger-weight
coefficients. C#, Rust and TypeScript consume that same file. `ActivityPolicies.json`
owns default enabled beards and the RNG pool. Existing libraries are never reseeded.

In planned support, Preferred beards fill slots first in their editable priority
order; Rotate beards fill remaining slots and rotate once per minute. Off excludes a
beard. New presets use the shared policy. Legacy beard definitions stay unchanged
until a beard is edited or Use default beards is selected, without a legacy badge.
Both lists empty disables all beards, not automatic selection.
Keep current preserves observed beards and diggers without minute rotation.
The beard table renders preferred rows in actual priority order, followed by rotating
and off rows. Up/Down reorders keyed rows, preserves keyboard focus and announces
the new priority. Mode changes retain focus on the moved select. Stored mode values
and beard pools are unchanged.
See `PRESET_BEARD_ROTATION.md` for acquisition, exact support and funding rules.

Planned support exposes one Automatic/Custom digger-weight selector. Automatic stores
an empty weight vector and hides numeric fields. Switching to Custom materializes all
twelve objective-derived defaults from the same shared coefficients the planner uses,
including the adventure increment for positive minimum-stat constraints. Custom initialization
is unavailable while the library contains invalid values; it must not freeze a zero vector
from an uncomputable draft. Returning existing Custom weights to Automatic remains available.
Custom weights appear in one editable table and remain fixed across objective edits. Switching back to
Automatic clears the overrides; a later Custom selection reseeds from current goals.
All-zero custom overrides exclude every digger. Preview results report actual chosen
weights, levels and gold drain; automatic search may choose a different portfolio than
the initial weights. Legacy activity-policy support is separate from weighted presets
and does not provide a second preset-weight configuration location.
Weights are relative eligibility/funding preferences, not levels or percentages;
the automatic planner can compare alternative portfolios and reduced funding.
For dynamic goals, switching Automatic to Custom requires a matching saved preview;
the initial digger vector uses its effective weights instead of freezing unresolved zeroes.

## Verification

- `npm run check`
- `npm test`
- `npm run test:ui -- e2e/presets.spec.ts e2e/component-ux.spec.ts --workers=4`

For a full isolated browser run, set `NGUI_TEST_PORT` to a free port and
`NGUI_TEST_OUTPUT` to a dedicated artifact directory, then run `npm run test:ui`.
The project config disables server reuse when a dedicated port is supplied, and
component fixtures support that port rather than hardcoding 1420.
Separate artifact directories alone
do not prevent another test runner from stopping a reused server. These browser tests mock native
IPC; actual game equipment, policy caching and stage inheritance need live integration
verification against the backend implementing the contracts above.
