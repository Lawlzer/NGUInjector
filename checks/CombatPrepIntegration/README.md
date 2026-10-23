# Combat Prep Integration Checks

Build the real plugin first, then build and run this .NET Framework 4.8 executable:

```powershell
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$game = "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
& $msbuild "NGUInjector\NGUInjector.csproj" /t:Rebuild /p:Configuration=Release "/p:GameFolder=$game" /v:minimal /nologo
& $msbuild "checks\CombatPrepIntegration\CombatPrepIntegrationChecks.csproj" /restore /t:Build /v:minimal /nologo
& "checks\CombatPrepIntegration\bin\CombatPrepIntegrationChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" "$game\NGUIdle_Data\Managed"
```

The checks load the built backend and a game assembly modified **in memory only** with Mono.Cecil. No DLL, live save or game process is modified. External scene/UI, controller recall, slot-count and selected recurring non-gear input getters are replaced. The backend monotonic clock is also an external boundary: existing checks use real Stopwatch time, while activity-timing checks supply deterministic seconds without sleeping. Native support beard/digger effects (including W and Double Stat), gross GPS/drain, purchased-level bonus, selection methods and exact native input methods are NOT patched to constants. Support construction, InputField rendering leaves and partial native-refresh fault injection reuse `PresetIntegration/Program.cs`. The controller swap boundary delegates to the game's real data-only `Inventory` swaps and counts calls. The production planner, optimizer search/equip preflight, capture, gear validation, policy replay, allocation, phase gates, token handling, activity accumulator and cleanup are not substituted. Native Unity swap rendering is not claimed tested.

The data-only `Main` fixture bypasses constructors, so setup explicitly supplies the resource coordinator's empty owner/reason field defaults before runtime JSON serialization. Energy and magic management are explicitly enabled for eligible prep execution; unmanaged tests disable the relevant setting themselves. The real `ResourceWriter` and transfer-before-render behavior remain intact.

Recall substitutes return all backing wish allocations for E/M/R3, blood rituals for magic, and hacks for R3 while retaining basic training and unowned lanes. The standard-row opener in scheduled transition coverage runs actual plan allocation/application before controlled timing accrual; directly marking the scheduler applied is not a valid ongoing resource receipt.

Coverage includes read-only preview and scheduler non-initialization; four actual optimizer goal profiles; strict numeric arguments and bounded schema output; actual serialized AT coefficients; exact E/M power/cap comparisons against game getters over clamp/potion/rounding cases; actual fixed-gear boss formula parity; all nine difficulty/OS base combinations; the broken native upgrade amount overload versus the verified-IL helper and real training getter; capped augment pairs and all five AT weights; no W energy OR magic during AT; separate background caps; independently capped weighted NGU remainders; stale/consumed/foreign tokens; unmanaged rejection without recall; passive-rate reproject; active focus/stage/scheduler guards; late actual AT/augment target gates; original deadline cancellation; target restoration; idempotent cancellation; equipment revision and whole-epoch rejection; existing W dump/partial preservation; safe fresh OS selection and once-per-rebirth pinning.

Game training ticks and native Unity rendering are not driven. Native support model/input controller methods do execute against isolated fixture objects. Tests set actual training levels to exercise transition gates; they do not claim a live end-to-end timing measurement. The pure planner's tick, float-approximation, paid-bar and numerical tests remain separately owned under `checks/CombatPrep`.

Append `--preset-interruption` to run only the scheduled-prep-to-Equip regressions.
They cover owned gear/support cleanup before replacement, unchanged stage entry/deadline,
safe resume, late cancellation isolation, rejection before interruption, and pending/recovery
blocks. Manually started prep and nonmanual preset acquisition remain blocked. These checks
use committed global preset definitions and the compiled production Equip path, without live actions.

Append `--automatic-reconciliation` for orphaned/expired and unexpired restart
continuation, exact persisted paid-AT gates, legacy/malformed checkpoint fallback,
bounded cleanup, current-intent funding, foreign/stale coordinator isolation, and
real checkpoint failure/retry checks. The complete suite includes this selector's
cases. See [the recovery contract and verification](../../docs/automatic-reconciliation.md).

## Phase Support Checks

`--adventure-weight` selects the automatic-stage secondary-objective regressions in
`AdventureWeightRegressions.cs`; the full suite also runs them. Sixteen groups cover
the actual 100:15 request, primary/mixed/adventure-only item tradeoff, exact joint
support/gear mapping, zero/manual equivalence, fresh bootup, Wish 190, native target
immutability, routing without a global owner, deferred modes/rules, coalesced edits,
paid-AT/reinjection gates, partial route failure, and safe retirement before weaker
gear or support restoration. Persistent native route-retirement failure must not
release a competing Equip even after historical cleanup retry exhaustion. Native
scene/rendering boundaries are isolated; production search, projections, allocation,
target gates, support, and routing validation remain real. No live game is accessed.

`PhaseSupportIntegration.cs` adds same-gear current/planned variants before search; native W/Gold/Stat parity; ordinary four/five-slot beard rules; background magic Lady protection without an extra W digger; purchased-bound exact input; fresh pinned replay without resizing; all-used-phase native preflight before token/recall/gear/OS effects; owned partial receipt recovery and foreign receipt rejection; selection/flag/inactive-level/model/epoch/lock edits; multi-phase touched-ID unions and inactive zero/nonzero restoration; cleanup despite resource/target/gear errors; fixed original-support boss reporting; and additive bounded phase/runtime JSON. The existing tests now capture up to eight dedicated profiles, not four frozen-support profiles.

The latest main-task-owner-reported offline pass has 4,668 assertions against a fresh compiled backend. Compiled preset/shared-adapter checks independently pass 315 assertions; pure shared support passes 1,222 and the pure Combat planner passes 5,530, including 15-minute, one-hour and 24-hour nine-profile income/performance cases. These are offline tests only, not rerun by this documentation pass. Other fixture consumers opt in to support construction/rendering only when they link this runner's support file, so their manifests need no new dependency.

The production manifest includes `Managers\SupportProjection.cs` and `Main.Support.cs`. Use a normal production Release `Rebuild` before running compiled checks; temporary source imports are neither needed nor evidence of production integration. The shared support integration is complete, not a pending manifest-owner handoff.

## Explicit Live Verification

`Invoke-LiveCombatPrep.ps1` is a separate opt-in live training tool, not part of this offline executable. It defaults to 120 actual seconds and starts the recommended cached policy unless `-PreviewOnly` is supplied. It can spend in-game gold. `-Family 'AT then Wandoos'` selects that available family, and `-CancelAfterSeconds 5` exercises cancellation without extending the original deadline. Reports are written under `%LOCALAPPDATA%\Temp\opencode`; `-BridgePath` overrides the workspace bridge location.

The October 16 real two-hour run and native UI evidence remain historical in [COMBAT_PREP_LIVE_VERIFICATION.md](../../docs/COMBAT_PREP_LIVE_VERIFICATION.md). Current phase-support and Quick live scenarios are to be recorded separately by the main task owner in [PHASE_SUPPORT_LIVE_VERIFICATION.md](../../docs/PHASE_SUPPORT_LIVE_VERIFICATION.md). That expected document is not created here; these offline checks do not establish completion of all current live scenarios or the game's final configuration.

Review regressions additionally cover raw sub-floor boss inputs with clamp-after-gain engine output and clamped runtime actuals; native Wish 190 progression; fractional duration rejection; exclusion of un-equippable low-cap profiles without poisoning Current; legal per-profile pools and used-loadout depletion shared by engine/runtime; exact maps across a phase that fills an empty slot and a return to Current; direct cancel/deadline/unload ownership and drag safety; force-restoration on safe unload despite configured plan owners; explicit AT-3/4 and implicit AT with empty/explicit magic priorities, managed/unmanaged old WAN, Iron Pill masking, preserved non-AT priorities, and normal-stage pre-gear blocking without applied credit.

Follow-up regressions inject a controller refresh failure immediately after the first owned swap of a two-item acquisition. They verify surfaced failure, safe rollback despite configured plan owners, bounded flag cleanup, and refusal to roll back under new drag/lock/epoch interference. Primary generic/stat AT (`AT`, `AT-0`, `AT-1`, `AT-2`) is tested for WAN exclusion and managed/unmanaged entry behavior; an NGU/WAN/TM policy with a later generic AT fallback must retain its exact priorities and actual W allocation.

## Focused Review Regressions

- **R17:** Start-time expected menu targets cover AT-3/4 and the selected future augment/upgrade pair. Edits to either future field in either phase order, including lower, higher and disabled targets, cancel during the earlier held allocation without acquiring or overwriting the edited phase. Unchanged nonzero future targets and active-owned zero targets transition normally; cleanup restores only acquired, still-owned fields.
- **R18:** Actual `Optimize` results with a sole accessory in either of two slots are captured without fake padding. Complete maps contain the instance once and an explicit empty slot; actual phase application and cancellation restore exact original placement. Combat delegates snapshot/composition/application/matching to the common `LoadoutManager` APIs while retaining its bounded transaction rollback.
- **R22:** Cancel, deadline and completion restore exact original gear with Manual Adventure enabled, including when a configured gear-applying stage cannot be treated as an immediate successor. The setting itself is preserved.
- **Shared numeric foundation:** All four `ResourceProjectionCapture.Create` contexts are compared exactly to actual E/M capacity and power getters across random bonuses, MacGuffins, clamp and potion cases. `GetBonuses` is checked against native `specBonus` followed by `getBonusFactor`, including an enabled fractional-factor offhand and reversed input enumeration. Separate pure checks retain literal formula and BigInteger allocation oracles. Extra intermediate float casts from the old preset projection are intentionally not reproduced because native-getter testing exposes different Framework results.

Future bootup is now captured as the native continuous ramp, including fresh-zero recovery and the completion-only Wandoos-set bonus. `NativeProgressionIntegration.cs` checks actual `wandoosBootupTime`, `bootupSpeedFactor`, `advanceBootup`, and both Character W-speed getters against capture. It also drives actual `AdvancedTrainingController.updateAdvancedTraining` ticks: Wish 190 grants one level/tick at zero allocation even with disabled/reached targets, discards partials, requires native basic-training unlock, and stops on a reached target only with positive allocation. The planner therefore uses passive free AT throughout all phases and excludes paid AT phases; it does not reinterpret free growth as a paid target gate. The fixture replaces AT text/bar rendering and W Slider setters only, not progression, target, speed or clock formulas. This remains offline native-method verification, not a live Unity callback-order measurement. Capture requires installed/enabled W and refuses pending banked AT transfers. Raw boss forecast fields, OS pinning, native nuke ownership and the original deadline are unchanged.

The combined native regression calls the real production allocation method every 25 ticks, real W energy/magic advancement and bootup methods each tick, and real free AT updates afterward. Fresh-zero and completion-crossing cases match forecast completed levels exactly, with tight float-rounding bounds for partial bars and held resource costs. Another regression captures a better already-owned W head at zero boot without equipping it, then compares that captured future coefficient against actual native full-boot getters on the exact fixture item. A validated free-AT policy also starts through the actual production executor with both paid AT allocations zero, and changed Wish activation invalidates capture semantics. The mathematical follow-up passes 4,963 integration assertions against a fresh isolated Release backend and 6,547 pure assertions. The shared `PresetIntegration` and `Ownership` fixture consumers also compile unchanged; no live game is accessed.

### Integration Handoff

`Plan` and `Project` signatures and all executable phase fields are unchanged. New snapshot fields are `WandoosBootupElapsedSeconds`, `WandoosBootupDurationSeconds`, `WandoosBootupCompletionFactor`, and `FreeAdvancedTraining`; production capture supplies them. New alternative outputs are `ResourceReferenceProfileKey`, `EnergyResourceFocusEquivalentSeconds`, `MagicResourceFocusEquivalentSeconds`, `EnergyResourceFocusCostKnown`, and `MagicResourceFocusCostKnown`. These are separate lane allocation opportunity costs, not the existing W dump-equivalent times. `Main.CombatPrep.cs` and UI serialization were deliberately not edited; their owners must expose those additive metrics if desired. The private `CaptureCombatPrepProfile` helper no longer accepts its unused `currentSupport` argument; its sole caller was updated after reference inspection. Free AT needs no paid allocation/executor phase, and native target semantics must not be presented as menu-target-bounded growth.

Native formula evidence is the installed `Assembly-CSharp.dll`, with independently available decompilation under `%LOCALAPPDATA%/Temp/opencode/walderp-inspected/Wandoos98Controller.decompiled.cs`, `Wandoos98.decompiled.cs`, `%LOCALAPPDATA%/Temp/opencode/ngu-tools/decompiled/Character.decompiled.cs`, and `%LOCALAPPDATA%/Temp/opencode/Assembly-CSharp.il`. Relevant IL methods are `Character.totalWandoosEnergySpeed/totalWandoosMagicSpeed`, `AdvancedTrainingController.progressPerTick/updateAdvancedTraining/hitTarget`, and the bootup methods above. These local research files are not test dependencies; the executable loads the provided installed game assembly.

## Live Observation Keys

Schema-v1 `root.combatPrep.actual` retains its multiplier/boss totals and adds the following read-only observations for UI validation:

| Fields | Type And Meaning |
| --- | --- |
| `available` | Boolean: the current game AT/W observation models are available. |
| `energyAtLevel`, `magicAtLevel` | Exact decimal strings for actual AT indices 3 and 4. |
| `energyAtAllocation`, `magicAtAllocation` | Exact decimal strings from `advancedTraining.energy[3]` and `[4]`. Both are ENERGY allocations. |
| `wandoosEnergyLevel`, `wandoosMagicLevel` | Exact decimal strings for actual completed W levels. |
| `wandoosEnergyAllocation`, `wandoosMagicAllocation` | Exact decimal strings for current W resource allocations. |
| `energyWandoosLevelsPerSecond`, `magicWandoosLevelsPerSecond` | Finite numbers from each actual current amount getter's per-tick `q`: `50 / max(1, ceil(1/q))`, at most 50. Zero allocation, disabled W, NaN and nonpositive/tiny rates report zero; positive overflow stays capped at 50. These are tick-rounded current-formula steady rates, not measured wall-clock deltas or forecast end rates. |
| `augmentIndex` | Selected current/last prep augment phase's pair index, or -1 when no same-epoch augment phase is selected. |
| `augmentLevel`, `upgradeLevel`, `augmentEnergyAllocation`, `upgradeEnergyAllocation` | Exact decimal strings for that selected pair; zero strings when no pair is selected. |
| `gold` | Finite nonnegative current game gold, not forecast remaining gold. |

Running observations refresh at allocation tick end and termination. Idle/closed-run observations also refresh on reads at most twice per second, without changing terminal phase/time/outcome fields. Reads within the interval reuse cached state. Unavailable game data reports `available: false` with typed zero defaults. Same-epoch runs retain original fixed boss-reporting gear; without that context, boss observations use the current game's actual getters. No observation path recalls resources, changes targets, swaps gear, or runs an optimizer.

Regressions verify exact strings beyond the JavaScript safe-integer range, actual getter/tick-rounded W rates, zero allocations and non-finite rate handling, selected augment observations, post-cancel clearing, idle freshness, frozen terminal timing, and read-only unavailable state. The shared backend action-result limit is 256 KiB, with a separate 252 KiB compact-preview bound. A native-support-rich preview retains all 12 alternatives, including AT and background families, in a 121,345-byte pretty UTF-8 response, above the obsolete 64 KiB feature cap. Long gear names and multibyte runtime stress actual attachment and command-result writing with the maximum ID and runtime revision. The first required trimming preserves every highest-ranked family representative by removing duplicate-family extras first. More extreme input exercises family-winner fallback while retaining baseline/recommendation; runtime alone above the wire limit rejects without replacing the preview cache. Every successful complete envelope, not just compact data, remains below 262144 bytes. These tests construct isolated fixture state and write only fixture temp results, with no bridge calls or live game interaction.

## Native Auto-Nuke Regression

The fixture preserves the REAL `BossController.Update`, `autoNukeThreshold`, `startNuke`, and failed-nuke logic. Only `Update`'s external `UnityEngine.Time.deltaTime` call is replaced with a controlled 0.25-second input; boss portrait rendering is a no-op boundary. A positive control triggers the real threshold/start/reset path and the brief failed-nuke pulse. It also demonstrates that the second native threshold branch can fire with `autoNukeOn` false, so toggling a saved preference is neither necessary nor sufficient protection.

The timed-run regression executes 28,800 native Update/owner-tick pairs, representing TWO VIRTUAL GAME HOURS. The native auto-nuke preference and purchase flag remain enabled, native fight/nuke flags never trigger, and the owned timer is reset every active tick despite the half-second allocation guard. Run start/duration remain fixed, and an expired global deadline still cancels and restores the original timer, after which native automatic nuking can resume. This is a deterministic offline game-time simulation, not a claim of a completed live two-hour run.

Additional checks cover cancel/completion/failure/unload restoration, explicit handoff refusal, timer-reference/value/epoch ownership loss, preservation of user preference edits, manual fight and real manual `startNuke` preemption before the allocation guard, unchanged fight flags/timer values, and exact lock/plugin-boss preemption diagnostics observed BEFORE resource cleanup. Missing timer data leaves preview readable but candidates unavailable and rejects execution without resource recall or gear swaps. No saved preference, pure engine, UI, live save or running game is modified.

## Live Resource Reconciliation And Fallbacks

`LiveResourceRegressions.cs` covers observed generation beyond captured totals,
native cap loss and refill, restored capacity without invented generation, basic
reservations, independent E/M budgets, stale persisted pools, fresh replan capture,
and NGU-before-TM fallback eligibility/caps. Fixture recall includes NGU and TM;
real native E/M generation methods supply resource growth before production
`TickCombatPrep`. Native TM income growth preserves unfinished AT gates.

Rendering-boundary hooks record the actual funded values and conserved resources
seen by AT, augment/upgrade, W, NGU and TM text updates. TM rendering exceptions
exercise committed transfers and full cleanup. These hooks do not certify live
Unity rendering or frame order.

Native gold positive controls demonstrate that TM can starve a primary augment.
Current/future augment phases therefore deny TM while retaining NGU fallback;
tests drive native augment completion, then verify TM becomes eligible only after
leaving the last augment phase. No unrelated WAN/AUG/AT/RIT fallback is permitted.

The October 19 resource-fix verification rebuilt the backend and checks in
isolated `%LOCALAPPDATA%\Temp\opencode\energy-fallback-fix` outputs. Full integration
passed **7,954 assertions**, including **662 resource/fallback assertions** and
**1,358 activity-timing assertions**. The unchanged pure planner passed **6,560**;
the ordinary sustained-focus/allocator suite passed **2,043**. No live game/save
or runtime files were modified by verification.

## Run Activity Timing

`ActivityTimingRegressions.cs` adds ten deterministic test groups (sixteen scenarios) and 1,354 assertions through the compiled backend. They cover canonical/typed bounded zero rows; preparation excluded from activity time while spending the run budget; both prep orders and all five activity kinds; phase setup included; late actual target gates; open intervals included without settling counters or rewriting earlier snapshots; resource/gear read-only behavior; retained AT time after its policy disappears; successive boundary, GPS and equipment replans; capture/search time excluded even on failed replanning; real failed phase acquisition; cancel/completed/failed terminal freeze before slow cleanup; deferred cleanup retries and combat relinquishment; terminal recache/idempotent cancellation; owner plan/stage/entry identity; and resumed-run zero counters without checkpointed/offline timing.

The activity-timing verification pass on October 19, 2026 built a fresh Release backend and this executable into isolated `%LOCALAPPDATA%\Temp\opencode` outputs. The full offline suite passed **7,283 assertions**, including the 1,354 new timing assertions. The support-rich 12-alternative response remained bounded at **136,446 UTF-8 bytes**. Fixture setup also initializes the real rebirth model required by current scheduler availability checks. These are offline checks only; no live operations or game/runtime files were modified. The exact additive schema-v1 contract is documented in [Combat Prep Backend](../../docs/COMBAT_PREP_BACKEND.md#activity-timing).
