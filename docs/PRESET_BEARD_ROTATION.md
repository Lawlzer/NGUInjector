# Preset Beard Rotation

## Persisted Contract

- In planned support, `BeardPriority` is the ordered enabled list when `BeardRotation` is an array.
- `BeardRotation` is an optional nullable array of IDs 0 through 6. IDs must be unique within each array, and the arrays must be disjoint.
- Missing/null rotation retains legacy behavior. C# model serialization omits the field in either case, rather than introducing defaults into old definitions or migration hashes. Rust offline read/save preserves the original missing/null distinction; it never adds a rotation default.
- In planned support, present rotation, including `[]`, opts into explicit selection. Enabled IDs fill capacity first, filtering locked IDs; optional pool IDs fill only remaining slots. Excluded IDs never fill vacancies. Both arrays empty means no beards.
- `SupportMode: "current"` (Keep current) preserves observed beards AND diggers. Beard priority/rotation, digger weights and budget remain saved but inactive until switching back to planned support. Neither minute ticks nor cached re-equip override observed support.
- New seed/UI definitions derive enabled `[1,3,5,6]` and optional `[0,2,4]` exclusively from `ActivityPolicies.json` (`coreBeards`/`optionalBeards`), not duplicate fields in raw `PresetDefinitions.json` seed/new overrides. Legacy model defaults remain empty/automatic and are read from `PresetDefinitions.DefaultJson()` without recursive parsing.

## Execution

In planned support, preview and initial Apply select the first eligible optional pool IDs deterministically. No beard search or random draw changes that support selection.

While a planned global receipt owns activity, the beard tick rerolls optional slots after 60 seconds. If another legal combination exists, the previous set is excluded. Required IDs are stable, including their priority when capacity is insufficient. Temporary ownership (including native titan zones and max-ITOPOD pushes), locks, pending readiness, mismatched receipts and saved definition edits do not bypass ownership checks. The cadence belongs to the acquired receipt, not the legacy beard timer.

`GlobalPresetPolicy.Candidate` remains the immutable initial policy/cache. `GlobalPresetReceipt.Execution` starts from the observed post-apply candidate, not preflight allocations, and stores the currently verified execution, including rotated support. Planned allocation refresh replays this execution rather than putting the initial beard selection back. Keep current refresh evaluates observed support without overwriting it. Saved edits remain pending explicit re-equip. Changed global NGU ratios invalidate receipts/caches before replay or rotation; stale owned allocations are retired only when ownership and save-loading guards allow it.

Gold rotation is preflighted against fresh shared support projections. Affordable exact acquired digger levels are preserved. Planned support can re-fund only acquired digger IDs, weights and level ceilings using the numeric funding helper; Gold's return may restore those exact ceilings. Current-support mode never rotates or invokes the support setter. All planned support changes use `ApplyExactSupport`, including Gold's native digger-clear side effect. The pinned gear, route, split, ritual and activity policy are not searched or replaced. Resource allocations are re-evaluated and verified after support application. An infeasible pinned policy rejects rotation; a partial native failure retires ownership using the existing refresh failure path.

## Runtime And Integration

The applied snapshot is not rewritten by rotation. For planned support with an explicit rotation field, `GetLoadoutPresetRuntime()` adds a separate `rotation` object with `lastRotatedAt`, `intervalSeconds`, `beards`, `diggers` (exact `id:level` strings), and `observed`. Keep current has no rotation telemetry. When `observed` is false these values describe the last verified execution, not current temporary-owner support. Forecasts describe initial support, not expected production across future random rotations.

Projects that source-link the model or planner must compile `CustomPlans/PresetDefinitions.cs` and embed `Data/PresetDefinitions.json` as `NGUInjector.PresetDefinitions.json`, in addition to the existing activity-policy resource. Generated new seeds must supply both beard arrays explicitly from the activity policy; raw seed/new overrides omit both fields, and defaults must not insert `BeardRotation` into old rows. Backend/UI/Rust validators must agree on null/omission and disjoint IDs.

Offline checks cover model omission/validation, inactive settings retention, deterministic initial selection, required priority/cap, pool exclusion, repeated rerolls, exact minute cadence, saved/acquired isolation, temporary suppression, native Gold funding, Keep current preservation through preview/apply/refresh/cached re-equip, independent rotation telemetry, and refresh not undoing RNG. These checks do not establish live Unity timing/rendering or in-game behavior.

## Offline Verification

Initial task verification on 2026-10-19 (before the Keep current/global NGU follow-up) used a fresh normal production Release rebuild and the installed game assembly only as an isolated native-data fixture. No running game, installed DLL or save was modified.

| Check | Result |
| --- | --- |
| Production Release rebuild | Passed |
| Presets | 2,135 assertions |
| PresetIntegration compiled adapters | 7,609 assertions |
| PresetIntegration numeric runner | 1,865 assertions |
| Beards | 226,047 assertions |
| Support | 31,089 assertions |
| Beard receipt availability | 134 assertions |
| SharedSupport | 1,222 assertions |
| SustainedFocus | 2,039 assertions |
| ActivityPolicy integration | 149 assertions |
| ExternalUi backend | 13,924 assertions |
| PresetLibrary | 1,991 assertions across 20 checks |

The small source-linked Support/ActivityPolicy fixtures emit existing unused-field warnings. The production build passed without errors. Assertion totals cover the complete corresponding suites, not just the newly added regression cases, and are not a live-validation claim.

Keep current/global NGU integration follow-up: normal production Release rebuild passed; Presets passed 2,139 assertions, compiled PresetIntegration passed 7,672, its numeric runner passed 1,868, Beards passed 226,047, and Support passed 31,089. The compiled suite includes global NGU capture/exclusions/cache retirement and the new Keep current, temporary-owner, post-apply execution and save-loading regressions. Legacy stat-only fixture exclusions now use global settings; late native AT input edits are injected after resource recall because Keep current intentionally performs no beard-controller writes.
