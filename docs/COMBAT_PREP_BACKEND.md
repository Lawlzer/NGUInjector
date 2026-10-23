# Combat Prep Backend

`Main.CombatPrepCapture.cs` captures read-only game/owned-gear data. `Main.CombatPrep.cs` adapts the pure planner to the external command protocol and a bounded, runtime-only execution overlay. Numeric projection, allocation, support and exact gear-map foundations are shared with ordinary/preset paths; their objectives and lifetimes remain distinct. Support capture, formulas, budgeting, exact native inputs and mutation receipts are exclusively the [shared support core](support-core.md), not Combat copies. See [Automation Improvements](AUTOMATION_IMPROVEMENTS.md).

`Main.AutoCombatPrep.cs` adds explicit scheduled `ExecutionMode: 1` using this same planner/executor with remaining-budget replanning, durable continuation and cleanup recovery. See [Automatic Combat And Mixed Focus](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md) for that contract, forward bootup/free AT, lane opportunity costs and latest evidence. The manual commands below retain exact cached-policy replay rather than automatic re-search.

The objective is maximum terminal Augments x Wandoos (A*W) within the entered fixed budget, not minimum focus time or a global tradeoff against NGU progression. The 7200-second default accepts any whole-second budget in `1..86400`, including 15m, 1h and 24h. Boss reporting uses Mode B: original equipped gear AND original physical support as the fixed final reporting setup, with no fight-gear optimization. Dashboard Quick remains an indefinite manual override using matching standard presets, with existing helpers still allowed, not this timed search.

## Automatic Adventure Weight

Automatic stages accept optional numeric `AdventureDynamicWeight`, default `0`,
finite and inclusive `0..1000000`; fractions are valid. The stage editor exposes
**Adventure weight** only in automatic mode and retains it when switching modes.
Standard stages ignore this field; their referenced global presets remain independent.
No plan-version bump, automatic migration, or change to stage identity/deadlines is required.

For a positive weight, one read-only captured context feeds bounded joint gear/support
selection for each available phase. The primary goal is 100: Augments, Wandoos,
AT with ratios `[0,0,0,1,1]`, or Energy NGU for the background setup. The entered
adventure weight follows Misc adventure mode/stat rules into PP, boosts, or a selected
regular zone. `15` means relative normalized utility `100:15`, not an energy/magic
reservation, guaranteed farming gain, or capped primary-production loss. The schedule
still maximizes terminal A*W; adventure does not independently determine phase lengths.

Selection uses the shared preset planner's internal `SetupOnly` path: requested-goal
specialists only, no sequence search, and one winning gear/support/route tuple per phase.
Original Current remains the reporting/resource baseline but cannot bypass weighted
selection; missing primary profiles exclude their phase families, including all paid-AT
refinements. Wish 190 still excludes paid AT. Zero weight and manual Combat Prep retain
the original optimizer and current/planned-support comparison unchanged.

Setup ranking is a fixed-factor production approximation over the remaining horizon,
not a combined adventure/prep simulation. Positive native AUG/selected AT/NGU targets
are cleared in the numeric selection snapshot only; disabled targets remain disabled.
The NGU model ranks prep-controlled spill without native auto-advance transfers. Wandoos
setup ranking uses a forward full-boot coefficient, including its completion bonus;
the actual combat forecast still advances the real captured bootup ramp. No capture
changes native targets, resources, gear, support, settings, or a global preset receipt.

The existing prep executor applies exact selected gear/support and retains sole
allocation ownership. Its route-only overlay then maintains the selected regular zone
or exact repeatable ITOPOD floor, honoring native safety, Lazy shifter, and Beast Mode
restrictions. Misc Adventure disabled, Manual Adventure, max-floor/manual rules, and
titan targets defer the secondary preference at effective weight zero; primary prep
continues. Quest, manual, titan, drag, and other combat owners block route writes.

Task/weight edits coalesce at the allocation cadence and use the original remaining
deadline. An acquired unfinished paid-AT gate defers searches, preserves both targets,
and keeps both W allocations zero. Reinjection continues that persisted gate on Current
before adopting weighted setups; targets already completed before acquisition are
recorded completed before replanning. Post-swap rule observations suppress farming-rule
feedback, but manual/titan deferral always takes precedence.

Before replacing supporting gear/support or ending the run, an owned farming route
retires to Safe Zone. A newer manual/quest/titan route or externally changed zone/floor
is not overwritten. Native retirement failure retains the gear/support lease, recalls
managed resources, and blocks competing equipment acquisition until retirement can
succeed; the ordinary historical-cleanup retry limit cannot abandon a still-owned
farming route. Telemetry and stable routing do not search or apply another preset.

Runtime schema 1 adds `configuredAdventureWeight`, effective `adventureDynamicWeight`,
`adventureTask`, `adventureTaskKey`, `adventureStatus` (`disabled`, `pending`, `deferred`,
`active`, or `blocked`), and `adventureReplanPending`. Plan rows publish configured
`adventureDynamicWeight`; frontend stage attribution compares it, treating omission as zero.

## Commands

- `previewCombatPrep`: optional `durationSeconds` (integer 1..86400, default 7200), `backgroundEnergyPercent` and `backgroundMagicPercent` (independent finite 0..100 values, default 3). Unknown arguments are rejected. Explicit null numeric arguments and fractional durations are not defaults.
- `startCombatPrep`: exactly `previewId` and `candidateId`, nonempty strings of at most 128 characters. Only a cached server policy can execute. No client phases, equipment IDs, targets or weights are accepted.
- `cancelCombatPrep`: exactly `runId`, a nonempty string of at most 128 characters. A completed/cancelled/failed run's cancellation is idempotent and cannot cancel a newer run.

Preview output is under `data.combatPrepPreview`, schema version 1. Multipliers are finite numbers; resource levels and phase AT targets are exact decimal strings. Combat uses shared C# `ExternalUiActionResultMaxBytes = 256 * 1024`, matching the native action-result transport limit, not the obsolete 64 KiB feature cap. Compact preview data stays below 252 KiB, reserving 4 KiB of envelope headroom; the COMPLETE pretty-printed UTF-8 response, including nested runtime/support, is also measured and must stay below 256 KiB (262144 bytes). Measurement reserves the maximum 128-character command ID and maximum runtime revision. Normal support-rich previews retain all 12 ranked alternatives. When trimming is necessary, lower-ranked duplicate-family extras are removed before the highest-ranked representative of any family, using server policy `Family` and candidate ID mapping, not labels. Only when those extras are exhausted may lower-ranked family winners be removed. Recommendation and baseline are always retained; if even those cannot fit, preview throws without publishing a new cache. Assumptions and errors report the actual byte limit. No schema or command arguments change. The engine still evaluates its bounded policy pool. No preview calls allocation, gear/support swapping, native level input, plan saving or scheduler advancement.

Native `protocol.combatPrep.maxPreviewBytes` advertises the same 256 KiB `protocol.maxResultBytes` action ceiling. The UI validates this negotiated limit and bounds the full UTF-8 envelope. Missing feature metadata retains the conservative 64 KiB fallback (or a smaller advertised overall limit); older native builds advertising 64 KiB stay capped there. Malformed metadata fails closed. The native general-purpose `MAX_RESULT_BYTES` remains 64 KiB in its other contexts; this feature uses the action-result limit, not a universal transport increase.

Both normal heartbeat publications and command acknowledgements share `root.combatPrep`, schema version 1. It is cached execution state, not a new planner/gear search on every publication. The start acknowledgement's runtime supplies the run ID. Idle state is initialized lazily; manual Combat has no saved enable switch. An explicitly configured automatic scheduled row can start when its running plan reaches it; ordinary rows are not migrated implicitly.

## Activity Timing

Runtime `root.combatPrep` adds the following fields without changing `schemaVersion: 1`:

```json
{
  "activityTimingScope": "run",
  "activityTimingSemantics": "exclusive-phase-wall-time-including-setup",
  "activityTimes": [
    { "kind": "AT", "elapsedSeconds": 12.5, "active": false },
    { "kind": "Augments", "elapsedSeconds": 30, "active": false },
    { "kind": "Wandoos", "elapsedSeconds": 7.25, "active": true },
    { "kind": "NGU", "elapsedSeconds": 0, "active": false },
    { "kind": "Background", "elapsedSeconds": 0, "active": false }
  ]
}
```

`activityTimes` always has these five canonical, case-sensitive kinds in this order. `elapsedSeconds` is a finite nonnegative number of monotonic wall-clock seconds, summed across all visits to that activity in this run, including policies replaced by automatic replanning. At most one row is `active`. Unvisited kinds (and idle state without a run) have zero seconds and `active: false`. Background W or leftover NGU allocation during another phase does not receive a second time credit: these are exclusive phase times, NOT productive training time, resource-seconds, forecast durations or scheduler applied-time credit.

An interval starts with `phaseElapsedSeconds`, before phase resource recall, gear/support setup and target acquisition. Outgoing time settles before phase replacement and before automatic replan capture/search/preflight. Preparation before the first phase, replanning, outgoing target restoration between phases, and terminal cleanup/retries are not charged to activities, so the sum need not equal run `elapsedSeconds`. Late target gates count the actual phase wall time, even beyond a scheduled phase budget. An open interval is included at snapshot time without settling it or mutating run timing. The first terminal handoff freezes timing BEFORE cleanup, including cancellation, failure, deferred cleanup, retry and ownership relinquishment; recaching runtime cannot extend terminal timing.

Storage is bounded to five in-memory counters and one open interval per `runId`. Timing is not checkpointed or reconstructed from elapsed/offline time, and a resumed/reinjected run starts its counters at zero even for the same scheduled entry. Use `owner: "scheduled"`, `planName`, `stageIndex`, and `stageEntry` (the existing run `OwnerPlan`/`OwnerStage`/`OwnerEntry` identity) to match the plan UI row, and `runId` to distinguish run-local resets. Do not label these as cumulative stage, rebirth or historical totals.

## Capture Sources

The installed `Assembly-CSharp` IL was inspected, rather than deriving coefficients from ETAs:

- `AdvancedTrainingController.baseTime` and `.levelFactor` are captured separately for indices 3 and 4. Both consume energy, using `sqrt(totalEnergyPower()) * totalAdvancedTrainingSpeedBonus()`.
- `InventoryController.specBonus` accumulates raw special points in weapon/head/chest/legs/boots/offhand/accessory order, then calls `getBonusFactor` once per special type. Projected loadouts reproduce that order and the actual offhand factor and boss-effectiveness getters.
- `Character.totalEnergyPower` and `totalMagicPower` use float prefixes, double non-gear factors, the game's lower/hard clamps, potion multipliers AFTER clamping, then a float result. Both cap getters preserve their different float/double expression boundaries and `Convert.ToInt64` rounding. Current-loadout projection must match the actual getters within 1e-6 before planning.
- Augment progress is sampled read-only and normalized by sampled energy, current energy power and current level plus one. Augment speed for another gear profile is a separate gear-factor ratio. `getBaseAugCost`, `getBaseUpgradeCost`, `augDiscount`, `baseBoost` and `augTierBonus` supply costs and tier formulas. `AllAugsController.totalBonus` applies the no-augs challenge factor, NGU factor and sadistic nerf BEFORE its final clamp. The adapter never divides a clamped current total to infer the global factor.
- Wandoos profile coefficients are reconstructed in native getter order without AT or bootup, substituting projected owned gear and shared beard-W/digger-W effects exactly once. Current parity is checked against native speed; zero fresh-boot speed is never divided. The engine advances captured native bootup elapsed/duration and its completion-only set bonus across all phases. Support levels remain fixed, including native Single boundaries. Resource power is not a Wandoos multiplier.
- OS bases use the inspected difficulty-specific float constants and are checked against the current OS getters. The installed `OSLimit()` is 400 for every OS, so `OSFactor()` does not change between OSes. Bonus offsets/exponents reproduce the actual 98/MEH/XL formulas; the planner independently validates current getter parity.
- Every profile sets nullable `CombatPrepProfile.NetGoldPerSecond` to its shared evaluation's exact net GPS, including the selected Gold beard and purchased-bound digger drain. Snapshot income retains the original native net GPS as fallback. Gold changes are native forward-order calculations, never an inverse ratio or assumed gear-Gold bonus. Removing rituals does not credit an invented income increase.
- Original equipment AND original physical support form one fixed final boss-reporting setup. DTO `BaselineBossAttack` / `BaselineBossDefense` are **UNCLAMPED RAW** stats before the game's final 100 floor. Capture computes that formula directly, never tries to recover it from a clamped getter. Engine output must be `max(100, rawBaseline * gain)` for baseline and forecasts. Runtime `CombatPrepBossStats(finalGear, raw, reportSupport)` freshly evaluates that original support with native potential/effective Stat effects, rather than treating temporary training support as free combat gain. Unchanged support retains exact native Double precision. Actual A, W and terminal A*W always use native current getters; forecasts and actuals have the same objective.
- Wish 190 is captured as free AT: after native basic-training unlock it advances one level/tick at zero allocation regardless of disabled/reached menu targets, throughout every phase. Paid AT phases are excluded, not charged or gated artificially. W uses pre-tick AT levels conservatively. Pending banked AT transfer is refused and changed Wish activation invalidates the epoch semantics. Native-method offline checks cover progression/bootup; this is not current-engine live Wish/AT evidence.

`Managers/ResourceProjectionCapture.cs` captures native fields and ordered raw
resource specs. Pure `ResourceProjection.NormalizeRaw` and `Project` provide the
shared normalization and cap/power arithmetic used by both Combat Prep and
presets; this adapter no longer owns a duplicate formula. Complete map composition,
explicit empties and intermediate retained-capacity preflight use the shared
`LoadoutManager` / `GearOptimizer` boundaries.

### Native Upgrade Overload Bug

The installed **`AugmentController.getUpgradeProgressPerTick(long amount)` ignores `amount`**. Its normal/evil/sadistic IL loads `Character.totalCapEnergy()` instead. Sampling it with 1 or 100 gives the same result and incorrectly normalizing by the argument drastically overstates upgrade speed.

The existing `Extensions.getUpgradeProgressPerTick(controller, amount)` explicitly substitutes an amount into the actual **no-argument training getter's** IL formula, preserving normal float versus evil/sadistic double arithmetic. Capture and allocation call that helper explicitly, never the broken instance overload. This is read-only: neither allocation nor levels are temporarily changed. The integration checks compare it with the real no-argument training getter at multiple allocations. This is an intentional correction to the planner README's suggested native-overload sampling recipe, not a planner-model change.

## Execution Ownership

The shared-combat slice passes offline native acceptance, including direct current
resource handoff, paused still-owned borrowed-field returns and public combat
admission from verified retained focus. Incomplete/foreign focus rejects before
token consumption or mutation. Focus replacement/stop commands remain combat-idle
only; cancellation/completion reconciles the CURRENT retained focus without another
optimizer search or historical return ladder. This is not the full release/live
gate. See [current evidence](combat-executor-verification-20261022.md).

- A preview expires after 120 monotonic-clock seconds. Start verifies the whole rebirth/save epoch, material menu/support/slot context, exact equipped instances, ownership and equipment stat revisions. Support identity includes exact beard/digger models, lists, rows, controllers, pods and inputs. Selection, ALL current/inactive levels, independent active flags, purchased bounds, unlocks, slots and availability are material; passive temporary beard levels and gold growth are not. Start recaptures passive rates/gold/progress using ONLY cached profile gear and pinned support selections, re-evaluating exact feasibility without resizing or reselecting even if income increased. Invalid profiles are removed, never silently replaced under the same key. A selected policy referencing one fails before token consumption.
- Owned candidate arrays retain actual equipment references. Every phase applies and verifies the complete wanted slot map, including actively emptying wanted zero slots. Ownership is recorded from that verified map, not blindly accepted from whatever happened to be equipped. It never repins an augment pair with `AugmentOptimizer`.
- A candidate whose projected energy cap cannot hold unrecallable basic training is excluded from both planner profiles and executable gear metadata. Current remains full usable even when a rejected or unused alternative has a low cap. Each legal profile captures `min(current available energy, projected energy cap - reserved basic)` and `min(current available magic, projected magic cap)`. Forecast engine state carries depletion across **actually used** loadouts only, with no future cap-refill credit. Runtime instead reconciles observed resources after recall: energy is `min(live idle, native cap - reserved basic)` and magic is `min(live idle, native cap)`, clamped nonnegative. Actual regeneration can increase runtime pools; increasing capacity alone does not invent resources. Captured profile totals and saved continuation pools are not runtime spending ceilings. Full gear-projected caps/power are still captured separately, not replaced with optimizer scores.
- Phase-specific support is included in forecast profiles BEFORE comparison (`PhaseSpecificSupport = true`), not applied only after choosing an old frozen-support policy. `Current` is captured first from original physical gear AND original support, remaining the common baseline rather than a planned-support replacement. Each Augments/AT/Wandoos/NGU owned-gear result supplies at most two same-gear profiles, `*-current` and `*-planned`, each with explicit goal metadata, exact gear map and support evaluation. Keys are unique and colon-free. This is a bounded current-versus-deterministic-policy comparison, not global support or combined gear/support optimization. No future beard/digger level growth is forecast; actual rates refresh while running.
- Planned support uses `ActivityPolicy.BuildSupport` for actual phase intent: AT=`AT/NGU`, Augments=`AUG/NGU`, Wandoos=`WAN/WAN`, NGU=`NGU/NGU`. Beard priority is ordinary core first, then requirements, retained originals (Fu/Gold preferred), then other optionals, filtered/capped only by shared legal availability. Ladybeard is not assumed to fit four core slots. Positive background magic adds Lady as a secondary requirement to Augments/NGU; background energy also protects it for NGU's Background variant. Neither case inserts a W digger into background NGU priorities. Requests use actual purchased maxima and ordinary `ActivityPolicy.DiggerBudgetFraction` (0.75), enforced. Pinned-current profiles use fraction 1 without the ordinary policy budget constraint, but still require native legality and nonnegative net GPS.
- Before consuming a preview token, pausing owners, recalling resources or changing OS/gear, execution runs full shared native `PreflightExactSupport` for EVERY used phase's pinned selection, including future-phase pod readiness and budget. On each phase entry gear is applied first, support second, and funding only follows full verification. Setup uses the original absolute deadline. An exact, feasible already-selected state avoids an unnecessary clear/apply, records the original all-level expected state, and owns no support cleanup work.
- Expected support is the shared all-level/independent-flag `SupportState`, checked with exact object identity and bounded shared support continuation evidence. Only typed verified/known-partial progress advances it. Each refresh rechecks purchased/slot/availability and pinned affordability without resizing. Foreign support is retained; its shared model-scoped uncertainty survives application resets and claim removal. Passive beard growth remains allowed.
- Normal slow/quick resource allocation, beard rotation, adventure equipment routing and auto-rebirth yield to the overlay. Protected stage application ownership is unavailable and existing timing credit is suspended, not invented. No `SavedSettings` switch or plan is changed.
- Focus, equip, augment-allocation, manual-stage, max-ITOPOD-start and probe-command actions are guarded. Manual fight prep/rebirth cancel and preempt the overlay. Unload ends it. Equipment locks, boss fights, external gear/support/OS/target changes and epoch changes cancel cleanly rather than train under another policy.
- OS invocation follows verified current setup. Atomic `combat-prep-os.json` version 2 records rebirth context, stable run/entry/action identity and invocation authorization before the native reset; persisted version 1 remains readable. Only an unlocked fresh OS with zero dumps, partials and allocations may change. Unknown native outcomes are not replayed after retry/reset/reload; model-committed presentation failure is distinguished and does not reset later W progression. Cancellation never restores an old OS. Unreadable metadata permits only the current OS. This does not claim universal save-rollback exactly-once guarantees.
- Phase budgets use absolute monotonic wall-clock boundaries from the original start, including setup. Both actual AT targets and both augment-pair targets gate phase advancement when late. Neither W lane is funded during AT or before any future AT phase. The global deadline never extends; unfinished targets at the deadline cancel with a reason.
- Augments use equal capped pair shares. AT uses the returned capped 0/25/50/75/100 percent split, with zero-weight lanes excluded. Background W has independent energy/magic percentage ceilings; full W uses live usable pools capped at one tick/bar. Actual amount getters find W/augment one-tick caps without inverting a clamped float value. Each explicitly bounded remainder funds global weighted NGUs first, then eligible buffered-cap TM. Zero NGU weights remain exclusions, not permission to ignore TM. TM is denied while the current or any future acquired phase is Augments, protecting primary gold; RIT/WAN/AUG/AT are never fallback destinations. If all permitted destinations are capped, complete or unavailable, the remainder stays idle. Neither fallback changes gear/support or the original deadline.
- Phase mathematics emits a read-only exact resource map at the 0.5-second cadence. The SAME `ResourceSetupExecutor` as presets/focus/inline applies differences and verifies native accounting; combat owns no `ResourceApplication` or separate release application. Rendering failures do not repeat model transfers. Scoped foreign Magic evidence survives application replacement while healthy Energy may continue; full prerequisite/resource verification is still required for phase/action admission.
- Normal stages also prohibit magic W during implicit AT, explicit `AT-3`/`AT-4` priorities, and primary `AT`/`AT-0`/`AT-1`/`AT-2` priorities. A later generic `AT` fallback behind NGU/WAN is not primary AT and retains its original policy. Managed old magic W is recalled before entry even if magic priorities are empty/None, and Iron Pill cannot reinsert it. Unmanaged existing magic W blocks AT before gear setup/allocation and prevents applied-stage credit, without recalling that external magic. Explicit non-AT user priorities retain their meaning.
- Selected training menu targets are temporarily unbounded so game auto-advance cannot redirect the owned pair. They are restored only while still owned. External target edits cancel and are retained.
- Future-phase targets are checked again immediately before acquisition, so edits made during an earlier phase are neither overwritten nor trained against stale bounds.
- Completion/cancel retires a still-owned route before releasing prerequisites and applies no implicit historical gear/support return. Current successor gear/support B apply directly; no successor preserves actual setup. Original gear/support remain reporting/acquired Current-policy inputs, never rollback targets. Native W/AT progression and scheduler commitments are not restored from snapshots. Resource removal still has a controlled-empty detour, and paused narrow returns remain blocked as described above; those are not completed executor contracts.
- Gear/support application enters shared `ApplySelectedSetup`, with explicit E/M recall and basic/unmanaged/R3 protection. Known partial work continues toward the SAME acquired selection under its original deadline; no rollback search or historical gear/support cleanup ladder remains. Unverified prerequisites cannot fund dependent resources or admit routes/OS actions. Foreign gear/support/route evidence survives ordinary reset/retry. Reviewed Equip/Preview+Apply or a durably accepted entry-bound stage action can authorize fresh setup only under its exact context/conflict revision; subsequent foreign callbacks revoke that review.
- Route activation rechecks full setup, native borrowed settings and resource footprints at callback boundaries. Foreign resource changes during routing enter the shared lane evidence. An owned unsafe route whose retirement fails keeps prerequisites and blocks handoff. Actual unload attempts retirement before invalidating authority and is refused while that owned route remains active. Foreign routes retain their native prerequisites rather than being overwritten.
- Recurring refresh observes selected/equipped material and the bounded acquired route; it does not scan full inventory or forecast alternative profiles. Owned auto-boost emits a causal material revision; selection events coalesce and unfinished acquired AT gates still defer replacement searches. Unexplained material changes require review rather than becoming automatic replacement authority.

Planner and runtime pair splits use `ResourceAllocation.SplitPair`, preserving
Combat's second-share floor/first-share remainder and capped redistribution.
Ordinary weighted/preset sharing instead uses `ShareCapped`'s largest-remainder
policy; sharing a helper class does not make these rounding contracts identical.
Manual Adventure with no current setup requirement preserves phase gear/support;
it is not an implicit request to return to historical entry gear.

## Support JSON

All fields are additive under schema version 1. `baseline.support`, EACH `candidates[].phases[].support`, runtime `combatPrep.plannedSupport` (current phase), and `combatPrep.actualSupport` use this compact shape:

```json
{
  "beards": [{ "id": 4, "name": "Ladybeard" }],
  "diggers": [{ "id": 1, "name": "Wandoos", "level": "12", "drain": 100 }],
  "grossGoldPerSecond": 400,
  "drainPerSecond": 100,
  "netGoldPerSecond": 300,
  "budgetFraction": 0.75,
  "policy": "phase",
  "wandoosBeardMultiplier": 2,
  "wandoosDiggerMultiplier": 3
}
```

The example is structural, not a policy-generated selection. IDs are native IDs; digger level strings preserve exact Int64 values. Financial/effect fields are finite numbers; multipliers are absolute, not ratios. `policy` is `current` or `phase`. Phases additionally expose `supportVariantKey`, the pinned server profile key. Actual support is freshly computed from the observed current selection, with fraction 1 / `current`, not copied from the planned row; unavailable observations are null with a warning. Planned support is null without a phase. Preview baseline always describes original physical support.

`budgetFraction` is the validation cap supplied to that evaluation, not observed usage and not necessarily the ordinary policy fraction. In particular, observed/current fraction 1 does not mean 100% of income is being drained. The UI labels planned rows **GPS budget limit** and observed rows **Share of gross GPS in use**, computing the latter from `drainPerSecond / grossGoldPerSecond`; zero or invalid gross GPS makes usage unavailable.

Runtime `supportStatus` reports `idle`, `pending`, `verified` or `ownership-lost`.
`blockedDomain`/`blockedReason` report actual unresolved setup authority; `reason`
retains current waiting/terminal context and `osOutcome` distinguishes finite OS
outcomes. The deleted historical pipeline no longer emits `supportRestoration`.
The UI renders planned versus actual support and actionable setup reasons only,
not a historical restoration receipt or a promise to undo native progression.

## Native Automatic Nuke Timer

The installed `BossController.Update` IL (around lines 135936-136012 in the inspected assembly dump) runs independently of injector allocation loops. With `Character.settings.autoNukeOn` and `Character.arbitrary.boughtAutoNuke`, it advances **`Character.arbitrary.nukeTimer`**, a public `PlayerTime`, while idle. A separate threshold branch calls `startNuke()` and resets the timer at 60 seconds; that branch does not recheck the preference. `startNuke()` sets native `isFighting` and `nukeBoss`, and an unsuccessful nuke can clear them again on a later 20ms fight tick. Injector early returns alone therefore cannot protect a long prep run.

Execution acquires an ephemeral lease on the exact timer/arbitrary/boss instances while idle, retains the original timer value, and sets the timer to zero through the verified `PlayerTime.setTime(double)` method. Every active `TickCombatPrep`, BEFORE the half-second allocation guard, checks manual/plugin combat and equipment preemption first, then holds the owned native timer at zero. Neither fight flag is cleared, and neither `autoNukeOn` nor `boughtAutoNuke` is ever assigned or restored by combat prep. User preference edits remain user-owned. The run's start time, phase budgets and global deadline are unchanged.

Cached reflection validates the exact field types, setter signature and 60-second threshold getter. Unknown schema, absent timer data or invalid timer values fail closed for **execution**, with an explicit candidate blocker; read-only snapshot/preview computation still works. Ownership also requires the same epoch and exact native instances, with only bounded natural idle-clock advance since the last reset. Foreign timer replacement/value changes are not overwritten.

Normal cancel, completion, failure and safe unload restore the ORIGINAL timer value only if the lease is still owned and there is no manual/native fight or equipment conflict. Manual prep/rebirth and other explicit owner handoffs do not rewind the timer. The elapsed prep duration is never credited to the automatic-nuke clock. Runtime warnings explicitly disclose the pause and whether restoration succeeded or was declined.

Before any termination cleanup, the log records the run ID, reason, current native timer, `autoNukeOn`, purchase flag, native fight flags, `CurrentLoadoutLock` and `CurrentDiggerLock`. Combat/equipment preemption reasons name every active predicate (`bossController.isFighting`, `bossController.nukeBoss`, `BossPushRunning`, manual/pre-rebirth preparation, or the exact lock predicate), rather than emitting only a generic owner message.

## Verification

See `checks/CombatPrepIntegration/README.md` for the compiled-backend checks and their explicit external boundaries. Offline checks never inject or restart the game. The October 16 real-game two-hour run, native UI checks, forecast errors, and reproduction commands remain unchanged in [COMBAT_PREP_LIVE_VERIFICATION.md](COMBAT_PREP_LIVE_VERIFICATION.md).

The October 19 activity-timing pass rebuilt the backend into isolated temporary output and passed 7,283 compiled integration assertions, including 1,354 deterministic timing assertions in `ActivityTimingRegressions.cs`. Only the external monotonic-clock input is controlled; accumulation, actual phase setup, automatic planning/replanning, terminal cleanup and retry logic execute unchanged. This verifies run-local timing and read-only snapshots offline, not a live training-time measurement.

`checks/CombatPrepIntegration/PhaseSupportIntegration.cs` reuses preset support fixture construction, native effects and native input boundaries. It covers deterministic variants, four/five-slot Lady behavior, background intent, pinned replay after income growth/loss, future-phase preflight before token/recall/OS/gear, partial-write ownership, foreign edits, receipt/model/epoch replacement, multi-phase touched-level restoration, cleanup despite outer errors, fixed original-support boss reporting and additive bounded JSON. These fixture results are offline, not themselves live validation.

That October 16 run does not validate later integration. Subsequent [phase-support/Quick evidence](PHASE_SUPPORT_LIVE_VERIFICATION.md) is preserved unchanged, and the [current automatic/mixed guide](AUTOMATIC_COMBAT_AND_MIXED_FOCUS.md#recorded-live-evidence) records the latest 300-second scheduled and mixed Preview/Apply results with their gaps. Earlier full-pass totals are retained in [completed verification](AUTOMATION_IMPROVEMENTS.md#completed-verification); individual test READMEs distinguish later checks. This documentation pass performed no live operations and makes no current-final-configuration claim.
