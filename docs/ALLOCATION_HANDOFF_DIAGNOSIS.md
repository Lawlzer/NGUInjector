# Allocation Handoff Diagnosis

Historical pre-change investigation. The resource-composition implementation
and its verification are recorded in [Resource Coordination](resource-coordination.md).

Investigation: 2026-10-20. Source inspection, existing runtime/log/result reads,
and isolated source-linked checks only. No commands were submitted to the game,
no preferences were changed, and no application behavior was edited.

## Conclusion

The recurring symptom is not one universally stuck allocation loop. Resource
recall and allocator ownership are separate operations. A handoff can recall
resources, invalidate or retire the previous owner, and then leave no owner
authorized to allocate. Recurring ticks do not themselves restore authorization.
Several explicit safety/stop branches persist indefinitely by design.

The important distinction is between desired execution (a plan is enabled or a
manual action was requested) and a valid acquired application receipt. The
current implementation can retain the former while refusing the latter.

## Observed Running Session

`C:\Users\Kevin\Desktop\NGUInjector\runtime.json` was updating during inspection.
Samples around 01:56-01:57 local on October 20 reported:

- Plan `24hr` enabled, current stage index 2: `AT Adventure`.
- Mandatory power and toughness targets both 3000.
- Preset `builtin_atAdventure`, acquired AutoUpdate true, no pending saved edit.
- Last acquired application at `2026-10-20T06:52:43.3704562Z`.
- Receipt classified `historical`, `currentOwner: displaced`, `active: false`.
- No Quick focus, Combat Prep run, pending cleanup or cleanup-recovery marker.
- All reported energy NGU allocations zero; these are not a complete idle-energy
  ledger, and the Combat Prep actual fields do not expose adventure AT lanes.
- Status: `Stage 3/4: AT Adventure; Waiting for mandatory AT targets; acquired
  policy is no longer valid. Explicit Equip or a new stage/restoration event is
  required.`

That status uniquely identifies the return at
`Main.GlobalPresets.cs:879-885`. At the pass emitting it:

- A receipt exists for the current stage owner and preset ID.
- Equipment application is not marked invalidated.
- Exact equipped slots and equipment object references match the receipt.
- The relaxed receipt check, ignoring allocation/support observations and the
  waiting marker, has failed.
- Earlier apply-blocker, failed-event, readiness and retry gates have passed.

Thus this is not simply a missed timer, changed allocation totals, an active
cleanup owner, or evidence that another owner physically replaced the gear.
The coordinator returns before refreshing or reacquiring. AutoUpdate does not
override this branch.

The exact first invalidating predicate is NOT established. Receipt validation
returns one boolean for epoch/identity, native AT target scope/auto-advance,
route, selected-item primitive material and other ownership checks
(`Main.GlobalPresets.cs:667-699`). Runtime classification exposes no failing
predicate (`Main.LoadoutPresets.cs:770-818`). Existing command-result snapshots
in this session predate the latest acquisition, so they do not bracket its loss.

Do not infer that a user edited native AT targets, or that auto-boost caused
this instance, merely from the generic `displaced` label.

A later sample at `2026-10-20T02:02:22.8066222-05:00` retained the same
historical/displaced acquisition, but `customStatus` had become
`WARNING: Custom automation left 72,534,807 idle energy and 7,228,625 idle magic
after allocation.` This directly confirms idle resources and the generic
warning replacing the more useful blocker status; it does not expose which
receipt predicate originally failed.

## Reachable Persistent States

| Trigger | Resource/owner transition | Why ticks do not recover |
| --- | --- | --- |
| Start manual Quick/global training, then stop it | Manual takeover disables plan and auto-rebirth; stop recalls still-owned lanes and clears manual ownership | Stop does not resume the prior plan; nothing remains selected to refresh |
| Click an already active Quick tile | UI sends Stop, not re-equip | Same explicit stop behavior; browsing/saving a preset does not activate it |
| Change global NGU weights under a dashboard preset | Safe preparation recalls both owned lanes and retires the old weights | Dashboard explicitly requires re-equip even with AutoUpdate true; scheduled owners reacquire automatically only with acquired AutoUpdate true |
| Gear-only external optimize while dashboard preset is sole owner | Actual swap recalls resources; successful command clears manual preset ID | No dashboard dispatcher remains, and the earlier plan is still disabled |
| Same-owner/same-gear receipt becomes invalid without a restoration event | Old receipt remains but cannot authorize refresh | Stage/dashboard coordinators demand explicit acquisition instead of treating arbitrary invalidity as permission to overwrite |
| Scheduled preset application or refresh throws after mutation starts | Resources may already be recalled; receipt is absent or cleared | Failed event is latched until explicit Equip/new stage event; transient readiness retry does not cover partial mutation |
| Legacy sustained focus activation/refresh throws | Manual takeover may already have paused plan and recalled energy; catch clears sustained owner | No retry owner or restoration of previous plan switches; refresh failure even resets focus status to idle |
| Automatic Combat Prep loses safe cleanup authority | Historical marker may outlive the live run | Previously blocked all allocation indefinitely; automatic reconciliation now retires old write permissions and resumes the current intent under the original deadline. See [the fix](automatic-reconciliation.md). |

Relevant production locations:

- Manual preset takeover: `Main.LoadoutPresets.cs:583-631`.
- Manual preset stop and per-lane recall: `Main.GlobalPresets.cs:633-664`.
- Quick tile toggle behavior: `NGUInjector.UI/src/lib/Presets.svelte:172-187`.
- Weight retirement: `Main.GlobalPresets.cs:156-202`.
- Gear-only handoff: `Main.ExternalUi.cs:316-326` and
  `Managers/LoadoutManager.cs:140-146`.
- Invalid receipt refusal: `Main.GlobalPresets.cs:873-885,1063-1071`.
- Mutation latch: `Main.GlobalPresets.cs:568-575,904-914,938-1034`.
- Legacy focus takeover/failure: `Main.CustomFocus.cs:148-208,285-340,511-534`.
- Cleanup recovery: `Main.CombatPrep.cs:75-77,507-514` and
  `Managers/ProtectedStageScheduler.cs:80-91`.

## Native Targets And Material

A native AT target edit is a concrete non-exception route to invalid ownership.
`ServiceGlobalPresetNativeTargets` preserves the edited field and releases its
lease, restoring only still-owned companion fields/allocations. When targets
were changed, it does not mark equipment invalidated. The remaining same-gear
receipt then cannot refresh, and the plan requires a new explicit event
(`Main.GlobalPresets.cs:324-373`). Existing tests deliberately preserve that
behavior (`checks/PresetIntegration/Program.cs:2336-2348`).

Exact gear identity is also weaker than exact material. In-place selected-item
primitive-field changes can invalidate a receipt without changing object
references. Ordinary owned custom boosts are handled: if the relaxed receipt
was valid before boosting, AutoUpdate sets equipment invalidation on changed
material. If validity was already lost, the boost wrapper cannot adopt the
change (`Main.GlobalPresets.cs:709-730`). Successful ordinary support drift,
beard growth or a digger maximum-level upgrade alone does not prove this
particular relaxed-check failure; allocation/support comparison is omitted.

## Stage Changes And Recovery

Skip/set/restart records a durable scheduler transition and clears application
keys. It does not disable the plan. Success acknowledges stage selection, not
successful successor funding (`Main.ProtectedTiming.cs:277-353`).

The quick path requires matching initialized stage keys, valid gear readiness
and any entry-fight receipt (`Main.CustomAutomation.cs:497-519`). Normal stage
setup runs in the 10-second automation loop, potentially after an entry fight.
Consequently a short post-skip gap is expected. Persistent gear/setup failure,
an invalid receipt, or repeated exceptions before stage application can prevent
recovery. The main loop has one outer exception boundary, so unrelated earlier
subsystem failures can starve stage initialization
(`Main.AutomationLoop.cs:105-151,319-323`). That starvation is a conditional
mechanism, not the established cause of the observed invalid-receipt status.

The scheduler cannot rescue this by forgiving required work: fixed time needs
verified application; mandatory AT needs actual native levels. This preserves
correctness but leaves the stage waiting when its allocator cannot reacquire.

## Why Idle Detection Does Not Repair It

- `LegacyAutomationEnabled` is hardcoded false; the old allocator is not a
  fallback (`Main.AutomationLoop.cs:12,35-36,158-162`).
- AutoUpdate means optimization on an admitted equip/re-equip event, not a
  health watchdog. It does not authorize every invalid-receipt replacement.
- Idle detection only writes a warning. It never restarts an owner, retries a
  latch, completes cleanup or restores paused switches
  (`Main.CustomAutomation.cs:266-303`).
- Warning eligibility does not directly include a dashboard global preset ID.
  A lost legacy focus with no other enabled owner can also stop qualifying.
- Generic idle warnings can overwrite specific non-`WARNING:` blocker statuses.
  Conversely, early cleanup/automatic-stage returns bypass warning emission.
- `presetReason` can be empty while `customStatus` contains the actual blocker;
  `historical/displaced` by itself is not an actionable cause.

These guards protect newer/manual intent, avoid repeated partial mutation and
prevent unrequested optimizer searches. The missing piece is explicit,
reason-aware recovery, not a blanket instruction to spend all idle energy.

## Repair Direction

No behavior fix was made in this investigation. Focused follow-up should:

1. Expose the first failed receipt predicate and preserve the transition cause.
2. Distinguish intentional Stop/manual displacement from recoverable loss of
   the still-selected automation's application.
3. Request reconciliation for recognized automation-owned changes and retry
   only where preflight/ownership make it safe, without periodic optimization
   or overwriting newer native settings.
4. Retain a visible failed manual request and an explicit recovery action rather
   than silently clearing its sustaining owner after a transient exception.
5. Verify end-to-end handoffs across repeated ticks, including failure after
   recall, not just command acceptance, safe cleanup and protected-time rules.

Plan-stage restart/set is an explicit reacquisition event; public dashboard
Equip instead takes over manually and disables plan/auto-rebirth. Those are
not interchangeable recovery actions. AutoUpdate false plus an invalidated
cache requires Preview + Apply, not a silent fallback search.

## Verification

Fresh isolated runs during this investigation:

```powershell
dotnet run --project checks/SustainedFocus/SustainedFocusChecks.csproj -c Release
dotnet run --project checks/ProtectedTiming/ProtectedTimingChecks.csproj -c Release
```

Results: 2,043 source-linked focus/allocation assertions passed with stubbed
game boundaries; 63 scheduler scenarios / 1,699 assertions passed. The former
emitted existing lowercase-type/unused-field compiler warnings.

Inspected but not freshly executed compiled preset regressions include weight
retirement/reacquisition (`Program.cs:1188-1238`), material invalidation
(`1635-1639`), stopped ownership staying recalled (`1660-1689`), and native AT
target edit preservation/new-entry recovery (`2336-2348`). These assertions show
that several persistent waits are intentional contracts, not evidence that the
current complete checkout or live Unity handoffs have passed those suites.

No production DLL rebuild/reload, live reproduction, resource recall, stage
action or game mutation was performed. The exact initiating predicate for the
observed live receipt loss remains unobserved.
