# Shared Preset Definitions

`NGUInjector/Data/PresetDefinitions.json` is the definition source for C# preset metadata, new-library seeds, Rust global-library normalization, and frontend metadata consumers. It is embedded as `NGUInjector.PresetDefinitions.json`; Rust uses `include_str!` on the same file. No runtime data file or fallback catalog is required.

## Schema

- `version`: definition schema version, currently `1` (independent of plan/library versions).
- `defaults`: optional preset-field defaults. `SupportMode` is `planned`, objective weights are `[1,1,0,0,0,0,0,0,0,0,0,0,0,0]`, `BeardPriority` and `DiggerWeights` are empty. No `Id`, `Name`, `HorizonSeconds`, global NGU ratios, or new beard fields belong here.
- `objectives`: fourteen ordered `{key,label,seedName}` rows. Array order is the persisted objective index; `seedName` preserves shipped capitalization separately from editor labels. Energy Cap and Magic Cap append at indices 12 and 13; legacy six-, ten-, and twelve-entry weights pad with zeroes.
- `beards`: seven ordered `{id,name}` rows for IDs `0..6`: Fu Manchu, Drop chance, NUMBER, NGU, Ladybeard, Adventure, Gold.
- `diggers`: twelve ordered `{id,name,objectiveWeights,minimumAdventureWeight}` rows for IDs `0..11`. Each coefficient array has fourteen entries in objective order, with zero cap coefficients. Default digger weight is its dot product with normalized objective weights, plus `minimumAdventureWeight` when either minimum adventure constraint is positive. Persisted `DiggerWeights` still contains twelve entries.
- `presets`: seventeen explicit shipped overrides with `Id`, `Name`, `Weights`, reviewed Energy/Magic priorities, and any other non-default fields. The original nine fixed specialists and adventure-dynamic retain their order, followed by five composites and the two cap specialists. Composites are All NGU, Augments + Blood, AT Adventure, AT Wandoos, Adventure Stats. Balanced is no longer seeded; existing saved Balanced definitions and references remain valid. AT Adventure retains blood plus AT objectives and adventure AT ratios; AT Wandoos retains Wandoos plus AT objectives and Wandoos AT ratios.
- `newPreset`: editor creation overrides, with `Name: "All NGU"`, the default objective weights, and reviewed NGU-first fallback orders. The caller supplies a fresh ID.

`ActivityPolicies.json` is the single source for new beard choices: `coreBeards` supplies `BeardPriority` and `optionalBeards` supplies `BeardRotation`. Raw `presets` and `newPreset` overrides must not duplicate either field. C# seed/new creation overlays those policy arrays; frontend creation helpers likewise derive their core/pool from that policy. Metadata checks require raw overrides to omit the fields and generated seed/new rows to agree with the policy.

## C# Contract

`NGUInjector.CustomPlans.PresetDefinitions` is an internal static class in `CustomPlans/PresetDefinitions.cs`:

```csharp
internal static JSONObject DefaultJson();
internal static string[] ObjectiveKeys { get; }
internal static string[] ObjectiveLabels { get; }
internal static string[] BeardNames { get; }
internal static string[] DiggerNames { get; }
internal static double[] DefaultDiggerWeights(double[] normalizedWeights, bool minimumAdventure);
internal static JSONArray SeedRows();
internal static JSONObject NewPresetJson();
```

JSON results and metadata arrays are independent copies. `DefaultDiggerWeights` expects fourteen already-normalized weights and does not normalize them again. Seed/new rows merge defaults with explicit overrides; only these creation helpers apply current policy beard choices. `NewPresetJson()` intentionally has no ID. The model consumes `DefaultJson()`; the planner consumes `DefaultDiggerWeights(...)`.

`PresetLibraryStore` seeds when `presets.json` does not exist. Existing libraries retaining recognized built-in IDs receive missing cap presets once, recorded by the injector-owned `CapPresetsAdded` root marker. Existing definitions are not replaced; later cap deletions remain deleted. Empty/custom-only libraries remain untouched. Historical twelve-entry migration receipts retain their identity through aliases to fourteen-entry definitions, including deletion tombstones. Do not add `BeardRotation` or active resource priorities to normalization defaults: omission is part of historical serialized migration identity. Existing `BeardPriority: []` remains unchanged. Rust still preserves unknown fields and obsolete `HorizonSeconds` tokens; C# drops the obsolete horizon during serialization.

On initialization, the injector adds only `EnergyPriorities`, `MagicPriorities`, and `ResourcePrioritiesReviewed` to existing built-ins matching the frozen pre-priority catalog in `BeforeResourcePriorities`. Matching includes the complete normalized definition, including unknown fields, and recognizes only the historical automatic beard shape or the exact shipped core/rotation pair. Any explicit priority field, customization, renamed definition, saved Balanced, or missing/deleted row is left alone. Migration runs before legacy plan imports, does not rewrite plan files or import receipts, and is idempotent without another root marker. Rust does not independently migrate offline libraries.

Energy Cap and Magic Cap have exactly one positive objective, `AutoUpdate: true`, and `SupportMode: current`. They maximize immediate projected native total capacity, including AllCap, higher-tier cap specs and offhand scaling, without valuing current resource pools, power, bars, NGU output or adventure stats. Capacity does not incur forecast swap downtime. Support is retained rather than optimized. Normal legality and retained-basic-training safety still apply. Auto-update uses the existing Equip/re-equip lifecycle, including invalidation after owned boosts; allocation refresh does not run a search.

## Resource Priorities

Every seed and new preset has `ResourcePrioritiesReviewed: true`. These creation-only policies do not change gear/support goal weights. `>` means useful-cap funding first, then real surplus only; locked, completed, excluded, or unaffordable optional activities yield to later entries. NGU ratios and zero exclusions remain in Misc.

| Preset | Energy | Magic |
| --- | --- | --- |
| Energy NGU, Magic NGU, All NGU | NGU > WAN > TM > AUG > AT | NGU > RIT > WAN > TM |
| Adventure/dynamic/stat/cap presets | NGU > WAN > TM > AUG > AT | NGU > RIT > WAN > TM |
| Blood | NGU > WAN > TM > AUG > AT | RIT > NGU > WAN > TM |
| Time Machine | TM > NGU > WAN > AUG > AT | TM > RIT > NGU > WAN |
| Augments | AUG > NGU > WAN > TM > AT | NGU > RIT > WAN > TM |
| Wandoos | WAN > NGU > TM > AUG > AT | WAN > RIT > NGU > TM |
| Advanced Training | AT > NGU > WAN > TM > AUG | NGU > RIT > WAN > TM |
| Augments + Blood | AUG > NGU > WAN > TM > AT | RIT > NGU > WAN > TM |
| AT Adventure | AT > NGU > WAN > TM > AUG | RIT > NGU > WAN > TM |
| AT Wandoos | AT > WAN > NGU > TM > AUG | RIT > NGU > TM |

AT Wandoos uses its existing `[0,0,0,1,1]` ratios within the AT activity. Capacity/stat-only presets still select gear exclusively for their original objective; their NGU-first orders authorize allocation without adding production goals. Reviewed priorities require `resourceLanePrioritiesVersion: 2`; version 1 did not support this separation.

## Checks

```powershell
dotnet run --project checks/PresetLibrary/PresetLibraryChecks.csproj -c Release
dotnet run --project checks/Presets/PresetsChecks.csproj -c Release
dotnet run --project checks/PresetIntegration/NumericIntegrationChecks.csproj -c Release
cargo test --manifest-path NGUInjector.UI/src-tauri/Cargo.toml preset_library
```

The library suite checks C# default/seed serialization parity, stable catalogs/composites, defensive copies, all digger basis vectors with/without adventure minima, and policy consistency. Existing library checks cover migration identities, saved edits/deletions, receipts, and no reseeding. Rust checks cover all shared defaults, missing-versus-invalid fields, explicit overrides, legacy padding, unknown exact numeric tokens, and horizon preservation. Beard checks reject malformed arrays, fractional/out-of-range IDs, duplicates and enabled/optional intersections at read/save and inline-action boundaries; offline roundtrips preserve rotation omission, null, empty arrays and explicit order without reseeding.

The independent-resource-priority change was verified offline with 5,667 library assertions, 9,027 source preset/planner assertions, 8,380 numeric-integration assertions, 189 Rust tests, 2,253 frontend tests, and all 100 desktop/mobile preset browser cases. Svelte checking and the frontend production build passed. The final isolated production DLL (`bc016b32-0cb0-4b2b-acc9-18fc69491bee`) passed 268 reviewed-executor assertions and the full native preset fixture's 25,851 assertions, including real allocation/permission and policy import/rebind paths. Independent review's ritual-startup-margin and inactive-WAN-proposal findings were fixed and regression-tested. These are offline results, not deployment or live-game acceptance; the running game and installed settings were not changed for this task.

Frontend `tests/preset-support.test.ts` checks shared new defaults, sparse normalization without legacy conversion, exclusive beard modes, explicit no-beards, objective-derived digger weights, fixed overrides/reset, safe input parsing and numeric-format parity. Controller readback tests cover native double formatting without sacrificing exact unknown tokens. Desktop/mobile preset browser tests cover autosave, priority ordering, reset behavior and Dashboard inspection without game actions. The e2e catalog fixture selects actual shared seed definitions rather than maintaining a second catalog.

Every source-linked check project compiling `LoadoutPreset.cs` or `LoadoutPresetPlanner.cs` must also compile `PresetDefinitions.cs` and embed the JSON with its exact logical name. Seed/new creation additionally needs the existing activity policy resource. Compiled integration fixtures that load the production DLL inherit its resources; they do not need a second metadata implementation.
