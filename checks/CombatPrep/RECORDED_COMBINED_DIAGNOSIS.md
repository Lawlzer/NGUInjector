# Combined Run Numerical Follow-Up

## Scope And Verdict

Analyzed only the existing `verify-combined-20261017-205013-044c36d5` recording and `verify-combined-live-diagnosis-20261017.md` under `%LOCALAPPDATA%/Temp/opencode`, current planner/capture/runtime source, and existing native decompilation/IL. No bridge, live game, original recording, backend build, UI/shared build artifact, or production source was changed. New continuation fields `PaidAtForbidden` and `AugmentsCompleted` and their generation behavior remain intact.

**The 1.4343541695% combined forecast shortfall is real. Its numerical cause is not identifiable from this recording.** No model defect has been proved, and no target reduction, timing fudge, or budget extension is justified. A short integer-threshold timing miss is plausible, but the evidence does not prove that the .865970810-second rebirth-deadline gap alone explains both missing levels. Native float accumulation and capture-rate rounding remain unmeasured contributors, not established root causes.

## Independently Reproduced Evidence

The optional pure executable selector `--recorded-combined <directory>` reads the recording without writing it:

- 151 unique runtime observations, deduplicated by published `updatedAt`.
- First/terminal rebirth times: `6737.891686550021` / `6886.8771990406385`.
- Observed phase-clock delta: `149.91172809999998` seconds.
- Observed rebirth-clock delta: `148.9855124906171` seconds.
- Difference: `.9262156093829` seconds. This is **not** a measured count of lost native callbacks.
- Terminal gap to original rebirth deadline `6887.74316985035`: `.8659708097111434` seconds.
- Augment levels `148 -> 165`; upgrades `298 -> 332`; magic W levels `39617 -> 39712`.
- The fresh execution forecast, not the earlier manual preview, is the accuracy denominator: augment total `370145789033325.06`, W total `43671.08146995269`, combined `1.616466690863426e19`.
- Fitting the fixed bonus representation `offset + scale * (U^2+1) * A^1.4` to the actual samples gives maximum relative residual `4.440892098500626e-16` across all 151 observations. This supports the total-bonus representation, not the training-rate capture.
- That representation reproduces the proposed `A=166, U=333` fresh forecast with relative residual `-2.220446049250313e-16`. These targets are reconstructed, not serialized phase target fields.
- Actual combined shortfall reproduced as `-1.4343541694742168%`.

The first sample's phase-elapsed plus phase-remaining fields sum to approximately `149.8802204` seconds, not exactly 150. Its run-elapsed minus phase-elapsed is approximately `.1056186` seconds. These are published control-clock differences, not the missing exact forecast phase DTO or first-resource-funding timestamp; do not treat all 150 nominal seconds as measured native training time or double-subtract setup from the forecast.

Recent level transition observations:

| Completion | Rebirth Observation Bracket |
| --- | --- |
| Augment 164 | `(6869.788824, 6870.790983]` |
| Augment 165 | `(6878.846575, 6879.848135]` |
| Upgrade 331 | `(6877.841518, 6878.846575]` |
| Upgrade 332 | `(6881.868438, 6882.871030]` |

Under **conditional**, constant-factor `q=C/(L+1)` and exactly 50 callbacks per rebirth second, first-to-last completion brackets imply approximate C ranges `.363031120..369203799` for augment and `1.459922180..1.487680666` for upgrade. Including per-bar ceil-to-tick allowance predicts next-completion windows `6887.838899..6889.013356` and `6886.345205..6887.452917`, respectively. The former begins about .096 seconds beyond the original rebirth deadline. This is a reason **not** to declare the .866-second gap sufficient proof. The intervals are not definitive bounds on actual callbacks: float accumulation, rate changes, publication/sample coherence and native scheduling are not measured. They cannot replace the missing captured coefficients and partials.

## Native Formula And Timing Review

Evidence: `%LOCALAPPDATA%/Temp/opencode/Assembly-CSharp.il`:

- `AugmentController.Start`, lines 113084-113103, schedules `updateAug` with `InvokeRepeating(..., 0, .02f)`.
- `updateAug`, lines 113105-113135, calls augment then upgrade after actual boss unlock checks. Neither increment uses a supplied wall/rebirth delta.
- `advanceAug` / `advanceUpgrade`, lines 113572-113894, require positive allocation, check native targets, pay gold at zero-progress bar start, add a float rate to float progress, reset progress to zero on completion, and grant at most one level per callback.
- Normal augment training getter, lines 113982-114250, evaluates the base expression in the order `power / divider / 50000 * (float)allocation / (float)(level+1)`, then the captured non-energy multipliers, clamps, and returns float.
- The augment amount overload, lines 114252 onward, evaluates `(float)amount * power / 50000 / divider / (float)(level+1)`. This is mathematically the same linear expression but **not bit-identical float evaluation order**.
- Normal upgrade training getter, lines 114501-114771, uses `(float)allocation * power / 50000 / divider / (float)(level+1)`. Evil/sadistic upgrade branches use double base arithmetic. The existing verified-IL upgrade sampling helper matches the actual training expression rather than the broken native amount overload.

Current capture samples the amount getter/helper, normalizes out sample amount and current energy power, and restores `(level+1)` to obtain the coefficient. The planner scales that coefficient by visited profile power, augment factor, allocation and inverse next level. It preserves paid partials, gold-at-start ordering, held allocations, overflow discard and original tick horizon. No missing level exponent or extra factor of 50 was found. The mathematically fixed-factor total also matches the observed totals to rounding precision.

The recorded allocation `46167587` itself rounds to `46167588f` in native float amount arithmetic. Native float rate/accumulation and double coefficient/ceil batching can disagree at individual completion thresholds; that approximation is already disclosed. Without the actual start coefficient, power, partials and callback counts, neither its contribution nor a larger capture-rate mismatch can be measured here. Do not infer a 1.43% rate error from a 1.43% **integer-level bonus** error.

The new pure counterfactual uses explicitly synthetic C values `.36/1.44` and initial partials `.98/.98` at the recorded starting levels. With all coefficients/targets unchanged, 150 seconds yields `166/333` while 149.12 seconds yields `165/332`. A native-style float progress transcription agrees on the synthetic 150-second completed levels. This proves the threshold mechanism is possible; it does **not** fit or identify the actual recording's missing inputs.

## Minimum Diagnostic DTO

Record one immutable selected-policy diagnostic at fresh forecast creation, then coherent observations immediately after first funding and immediately before termination/recall. Required data:

- Injector/planner build identifier, capture ID, run/stage entry and policy-generation identity. This recording predates later continuation work; current source is not proof of the exact loaded binary.
- Exact selected phase DTOs: profile/support keys, augment tier, durations, augment/upgrade targets, menu bounds and continuation flags. Include integer projected tick horizon, not only nominal run seconds or multiplied totals.
- Initial selected augment/upgrade levels, float partials and modeled paid flags; terminal/pre-recall levels and float partials. Record both phases' actual initial state if a transition occurs.
- Selected profile usable pools, energy power, augment speed factor, captured lane progress coefficients/exponents, cost coefficients/exponents, initial gold and resolved profile net GPS.
- Actual allocations and actual **no-argument training getter** rates after funding each lane. Do not use the broken native upgrade amount overload. At later refreshes capture changed power/rates, zero-allocation intervals and gold/affordability changes.
- Coherent monotonic wall, rebirth and total-play clocks at forecast capture, first funding, each observation and stop, together with both immutable deadlines. Timestamp the actual-value observation itself, not only JSON publication or controller phase acquisition.
- For a definitive scheduling attribution, observed native callback counts for the selected augment controller over the funded interval, if available. Mark them unavailable rather than deriving them from wall or rebirth time. Initial/final partials plus rates narrow the tick count, but direct counts distinguish scheduling loss from formula error most clearly.

This can be a bounded diagnostic DTO for selected phases only; there is no need to publish every candidate/profile or alter runtime policy, targets or timing. Runtime serialization belongs to its owner and was not changed here.

## Isolated Verification

Only the source-linked pure project was built/run, with `--artifacts-path` outside shared `bin/obj`. Original recordings were read only. The default suite passes **6,560 assertions**, including the other agent's continuation flag checks. The new opt-in 24-hour native-boot/free-AT unsaturated search passes **29 assertions**, evaluates **516** candidates, and measures **15,994ms wall / 13,109ms CPU**, below the existing 24,000ms daily CPU bound. Two OSes remain below one bar/tick at the final full pool; their positive-rate W progression cannot use the saturated batching shortcut. This measures a native-valid full free-AT shape, not a universal bound for arbitrary DTO extremes or Unity/Mono runtime overhead.
