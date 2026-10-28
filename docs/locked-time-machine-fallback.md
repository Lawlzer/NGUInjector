# Locked Time Machine Fallback

`CustomPlanStage.UseWandoosWhenTimeMachineLocked` is an opt-in boolean, defaulting
to false for new and existing rows. The plan store round-trips it and rejects
nonboolean values. It does not change shared focus defaults or global NGU weights.

For an opted-in inline stage with a locked/challenged Time Machine or missing TM
model/controller, `CustomStageAllocator` resolves Energy to `WAN` and Magic to
`RIT,WAN`. Eligibility uses Wandoos and Blood rather than the declared TM focus.
Setup requests Wandoos+Blood support and Wandoos gear; `GearGoal=-1` stays
uncontrolled. The original stage and protected scheduler entry are retained.
Explicit priorities do not gain implicit first-half Wandoos AT. When TM becomes
available, declared priorities and gear resume; opted-in explicit Magic policies
remain Blood-first during Iron Pill reservation.

Allocation still uses the existing exact managed-lane resource executor. Its full
lane maps clear old NGU allocations, including native backing-array tails. Disabled
lanes remain untouched and cannot grant fixed-stage credit for requested work.
Missing shared support prerequisites still block setup; fallback does not bypass
ownership or native-model validation.

Blood remains subject to the existing optimizer's gold-sustainability prerequisite.
The native TM challenge reports zero gross GPS, so no ritual is sustainable there:
Blood stays first in priority order but receives no allocation, and Wandoos is the
only remaining Magic destination. This change does not introduce spending from a
finite gold bank to override that prerequisite.

The plan-generation stream must opt in each early TM/Augment/Wandoos phase, keep
explicit priorities NGU-free with Magic `RIT` first, and leave the final 60-second
Augment phase unflagged. Shared 24hr/default plan definitions are unchanged here.

## Verification

`checks/PresetIntegration/LockedTimeMachineFallbackChecks.cs` is registered in the
normal suite and selectable with `--locked-time-machine-fallback`. It exercises
native allocations, gear/support, protected credit, availability transitions, the
final-minute switch, disabled lanes, old NGU cleanup, and flag persistence.

October 24, 2026 offline results, using the verification mutex for production builds:

- Focused fallback selector: 330 assertions passed (328 focused plus 2 fixture).
- Existing inline-stage selector: 469 assertions passed.
- Pure protected timing: 66 scenarios, 1,981 assertions passed.
- Full BlackBar gate failed in existing stage-support and preparation-retirement
  checks; it is not a release pass. SustainedFocus source checks also had missing
  fixture dependencies on concurrent execution/route changes.
- The older deployed game was observed read-only and contained fruit-claim and
  route-retirement errors. No deployment or successful live fallback validation is
  claimed by these offline results.
