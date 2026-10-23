# Numeric Preset Activities

## Integration Contract

`Main.PresetActivities.cs` extends the numeric preset engine; it does not invoke legacy focus/goal dispatch. The production project includes it explicitly. No additional package or assembly reference is required. It reuses the existing `Main.CombatPrepCapture.cs` native coefficient helpers and `CombatPrepPlanner.cs` DTOs.

- Call `CapturePresetActivities(capture.Snapshot, preset)` after base capture, before material fingerprinting and Plan/Evaluate/Replay, including observed post-apply captures.
- Call `ConfigurePresetActivities(request, preset)` from the preset request builder. It copies `AdvancedTrainingWeights` into `request.ATWeights`.
- Call `ApplyPresetActivities(candidate)` after resource recall and exact NGU/blood allocation. The candidate carries exact TM/W resource amounts, installed OS, AUG tier index/pair amounts, and AT indices 0..4. Application transfers those amounts without searching, resizing, switching OS, or editing targets.
- Capture the base NGU rows with Misc global E9/M7 target weights. `AutoUpdate` is model data for the runtime session/cache owner, not a numeric optimizer setting.

## Mandatory Stage AT Targets

Call `ApplyPresetStageTargets(snapshot, stage)` immediately after `CapturePresetActivities` for a scheduled execution context, before material fingerprinting, Plan/Evaluate/Replay and preflight. Apply it consistently to initial, fresh replay and observed captures. Null stages and modes other than `CustomStageCompletionMode.AdvancedTrainingStats` (1) are no-ops. Activity capture resets prior stage constraints; do not reuse a stage-adjusted snapshot as a fresh capture for another context.

The hook maps stage `TargetAdvancedTrainingToughness` to AT index 0 and `TargetAdvancedTrainingPower` to index 1. Positive unfinished targets override captured menu targets, including -1, and enable those snapshot lanes. Nonpositive requirements become disabled target -1, not unlimited target 0. Completed required rows are disabled for allocation. Block and Wandoos AT rows 2..4 retain their original menu-derived targets/availability. No coefficient, progress, native menu, preset, stage or persisted definition is changed.

Native coefficient capture already retains all five AT coefficients even when menu targets are -1. A mandatory stage may only enable lanes after successful native availability/parity capture; it never overrides locks, pending bank transfers, Wish-190 restrictions or parity failures. Positive targets outside the exact double integer range are rejected.

The snapshot stores captured `ATGoalWeight`, copied `ATWeights`, and stage-only `RequiredATTargets` (two Int64 values in toughness/power order). Every unfinished required lane must have both a positive global AT goal weight (`Weights[9]`) and a positive target weight at its AT index. Otherwise the hook throws an explicit `InvalidOperationException` before target overrides or live mutation. Completed/nonrequired rows need no positive weight. No ratio is silently rewritten. Candidate evaluation and replay also mark policies infeasible when any unfinished required lane receives zero energy, including pure legacy-goal specialists and cached zero-AT policies. An already completed stage can report known-zero AT production with zero allocation; advancement still uses the actual native stage completion gate.

Stage replay deterministically clips completed/nonrequired stat lanes to zero without redistributing that energy into other AT rows or changing the cached policy. Exact amounts on unfinished required rows remain pinned. This is target completion, not another optimization pass, and prevents an earlier-completing stat lane from blocking continuation toward the other stage requirement.

**Runtime ownership:** a snapshot override does not change native `AdvancedTrainingController.hitTarget()`. The global-preset runtime temporarily owns native targets 0/1 using the stage's positive requirements (disabled -1 for nonrequirements), retains that scope while native ticks run, and restores only still-owned menu values on release. Completion uses `CustomStageAllocator.AdvancedTrainingStatsReached(stage)`; forecasts never mark the stage complete. Native AT auto-advance with unfinished mandatory work is unsupported and rejected before mutation; no preference is changed automatically.

## Schema

Persisted `Weights` is normalized to twelve entries, preserving the original positions: `energyNgu, magicNgu, blood, pp, boosts, zone, timeMachine, augments, wandoos, advancedTraining, adventurePower, adventureToughness`. Six- and ten-entry persisted definitions are accepted and zero-padded, not renormalized in storage. Pure numeric callers may still supply six or ten entries; internal weights, availability, reasons, metrics and reference arrays use twelve. Only indices 3..5 are farming. Indices 10/11 maximize immediate native adventure-stat projections, not AT production, and remain independent of energy/magic pools, forecast duration and swap overhead. Their availability is independent of native level gain and Beast Mode farming restrictions.

Short numeric weight/normalization arrays are padded before planned-support selection, not just before scoring. Six- and ten-lane cached reference arrays replay as twelve lanes while preserving their original entries: missing references stay null and missing normalization entries use a neutral denominator of one. No new native availability or production is invented; legacy snapshot availability/reasons and cached metric arrays remain unmodified.

Preset definitions no longer own `HorizonSeconds`; legacy values are ignored and stripped on serialization. Plan stages own execution duration. Scheduled searches use remaining stage time, or remaining plan horizon for untimed stages, with a one-hour comparison fallback when no future horizon remains. Standalone searches use a one-hour forecast without expiring. Each acquired policy retains its forecast context across allocation refresh; re-equip can use a new context without rewriting the shared definition.

`AutoUpdate` defaults to true when absent and accepts only a JSON boolean; explicit false survives serialization. Presets no longer store `EnergyNguWeights` or `MagicNguWeights`; those retired fields are ignored and stripped. All NGU capture and allocation uses [Misc global weights](global-ngu-weights.md). `AdvancedTrainingWeights` remains preset-owned and defaults to `[1,1,0,0,0]`. It contains five nonnegative Int32 integers; fractional tokens, numeric strings, booleans and wrong lengths are rejected. All-zero target arrays are valid definitions, but a positively weighted AT objective without eligible targets is unavailable. AT indices 0/1 are Adventure toughness/power, 2 is block, and 3/4 are Wandoos energy/magic speed. All five consume ENERGY. Other unknown fields, including nested JSON numeric lexemes, remain lossless.

## Evaluation And Limits

Every gear/support trial evaluates actual normalized output with conserved integer E/M pools. A proportional allocation seed and bounded full-lane/25%/10% coordinate transfers compare energy NGU, TM, AUG, W and AT energy budgets, and combined NGU/blood, TM and W magic budgets. The existing bounded magic/blood split remains inside each trial. Every unlocked AUG pair is compared using actual captured tier coefficients; its exact index is retained. Search is heuristic, not a global-optimality claim. Specialist references and score dimensions include all twelve objectives, comparing production rates for indices 0..9 and immediate stats for 10/11. Requests with positively weighted immediate stats do not offer two-phase recommendations.

The outer work ceiling is 6,000 evaluations, including unavailable-specialist trials, plus up to 64 current-support rescoring trials and three final support comparisons per objective. Numeric checks derive the twelve-objective upper bound of 6,100 from those limits. Individual numeric forecasts retain their existing fixed iteration budgets and cooperative operation clocks.

TM and AT use inverse-level training coefficients; W uses its constant per-tick dump coefficient. The existing bounded float tick integrator returns conservative completed-level forecasts or unavailable results. Partial initial bars are deliberately discarded. Metrics are TM completed levels/s (sum of E/M), actual augment-total bonus gain/s, W completed levels/s (sum of E/M), and target-weighted AT completed levels/s. These are progression proxies, not utility equivalence between individual bonuses.

TM/AUG reserve all forecast bar-start costs from the same current liquid gold pool, after a pending blood startup reserve. The reservation includes unfinished bars and integration/rounding headroom; it never borrows future income. Recurring blood and diggers still share candidate GPS. This is intentionally conservative: policies requiring future income to finance TM/AUG may be rejected even when the game could eventually afford them. The planner does not forecast TM-driven income growth.

W keeps the installed OS. Captured bootup and AT speed factors are frozen conservatively, rather than crediting future bootup/AT gains. Wish-190 paid AT, pending bank transfers, unavailable native coefficients, failed parity, and unsupported projection bounds are reported unavailable. Extra-activity two-phase sequences are not offered because the old sequence integrator does not carry their gold/bar state. Legacy six-goal sequences retain their previous implementation.

Native evidence was inspected read-only from the installed game's `TimeMachineController` progress/advance/cost methods, `AdvancedTrainingController` progress/divisor/target methods, and existing Wandoos/AUG Combat Prep capture helpers. TM spends gold at each bar start, testing undiscounted affordability before charging `totalDiscount()`. W does not use resource power. No live-game forecast accuracy or mutation test is implied by the offline numeric checks.

TM/AUG forward coefficients are checked against native sample helpers before capture and then stored without level-dependent sample-rounding noise. The private readonly TM base-cost fields are read only and checked against the public current-cost getters; missing fields fail closed. Extra rates include a 0.001% downward numerical margin. The integrator's 0.5% certificate applies to this frozen numeric model, not a bit-identical Unity/JIT claim.

## Focused Checks

Run the existing suite normally for numeric activities:

```powershell
dotnet run --project checks/Presets/PresetsChecks.csproj -c Release
```

The standard project includes activity, adventure-objective and model checks. `ActivityChecks.cs` covers all four one-hot activities, differentiated positive weights, concurrent mixtures, twelve reference dimensions, E/M and shared-gold conservation, unavailable lanes, exact replay (including changed AUG preference, reduced gold/pools and OS changes), and independent float tick oracles. `AdventureObjectiveChecks.cs` covers direct gear/support selection and horizon independence. `ModelChecks.cs` covers defaults, strict boolean/integer arrays, six/ten/twelve compatibility, removed legacy horizons, and lossless unknown JSON.

`StageTargetsNativeChecks.cs` is an alternate compiled .NET Framework fixture entry point, using the existing native integration harness and real AT progress/target methods. Build `checks/PresetIntegration/PresetIntegrationChecks.csproj` with `/p:StartupObject=PresetStageTargetsNativeChecks` and `/p:CustomBeforeMicrosoftCommonTargets=<absolute checks/Presets/StageTargetsNative.targets>`, using a separate `OutputPath` and `IntermediateOutputPath`. Run the resulting executable with `<built NGUInjector.dll> <game Managed directory>`. It proves native menu -1 capture, snapshot stage overrides, genuine conserved allocations, real native ticks and the actual completion gate, plus strict weight/parity rejection, zero/completed targets and unchanged nonmandatory stages. Only rendering/unrelated native fixture boundaries are stubbed; no live game process or save is used.
