# Ordinary Policy Checks

Focused offline regressions for ordinary AUG/AT/Wandoos decisions and terminal
holding. All policy, cap/share mathematics, claims, resource reconciliation,
native accounting, scheduler credit and checkpoint coordination execute from the
current compiled injector. There is no substitute `Main`, grant-success flag,
private allocation writer, or hand-authored scheduler snapshot.

## Run

From the repository root, use the shared verifier for EVERY production/native
build and executable run:

```powershell
& .\checks\VerifyExecutorOffline.ps1 -Suite OrdinaryPolicy `
  -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE' `
  -LogPath 'C:\Users\Kevin\AppData\Local\Temp\opencode\ordinary-policy-native.log'
# Optional focused real-coordinator timeline:
& .\checks\VerifyExecutorOffline.ps1 -Suite OrdinaryPolicy -Selector --terminal-hold `
  -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
```

The verifier holds `Local\NGUInjector-Executor-Offline-Verification` across the
production Release rebuild, fixture rebuild and run. Initial fixture-only
MSBuild compilation also requires that mutex. Do not run an unlocked EXE.

`OrdinaryPolicyChecks.csproj` references the existing BlackBar project so the
fixture dependency is rebuilt even when OrdinaryPolicy is the ONLY selected
suite. It does not run BlackBar's full or selector entry point. The verifier
supplies the executable's three required arguments: Managed directory, injector
DLL and BlackBar EXE. An optional fourth argument is `--terminal-hold`.

## Migration Map

No obsolete requirement is skipped. Reflection only accesses internal compiled
APIs; it does not supply authority or replace decisions.

| Previous assertion / boundary | Current exercised contract |
| --- | --- |
| Source `Main` resource/epoch adapter, missing `ExecutionResourceBudget` and `ExecutionBaselineResourceMapMatches` | Real compiled `Main.Execution` and `Main.CustomFocus` resolve grants, observe maps and reconcile both lanes. |
| `AllocatePriorities` private/legacy writer tests | Actual `CustomStageAllocator.ApplyStage` executes the current ordinary producer and shared actuator. All seven AUG pairs and both related AT pairs retain first-token largest remainders in both orders. |
| `OptimizeAndAllocateIdle` | Pure `OptimizeForActualBudget` decision, then real `RequestAugmentAllocation` claim/policy/reconciliation/end lifecycle. No removed writer is recreated. |
| `OptimizeAndAllocateTarget` | Pure native `OptimizeTarget` pin, then the same actual production command. The surviving `OptimizeAndAllocate` best adapter is also exercised directly. |
| Optimizer `updateMenu` exception means failed allocation | Current per-target presentation failures retain `ModelAppliedRenderFailed` evidence and verified committed funding. Both exact bars, unchanged Magic and conserved Energy remain mandatory. |
| Raw target recall to force stage rendering | Fault the actual first authorized native transfer instead. Five AUG/upgrade/AT/Energy-W/Magic-W cases verify typed outcomes, real receipts, committed transfers and presentation-only retry with ZERO repeated model writes. No recall substitute remains. |
| Fixture-authored completed scheduler snapshot | Actual four-second main/quick execution, durable checkpoint machinery and applied-time credit complete the schedule; the unfunded trailing row has zero visits. Repeated zero-remaining passes retain the current entry and AUG funding. |
| Source terminal negatives | Real scheduler state covers unfinished/nonfinal expiration, disabled scheduling, unrelated stage objects and wrong active rows. Resolving a negative invalidates application evidence, so the CURRENT entry is reapplied before earning more time. |
| Completed terminal targets / W preparation | Completed AUG targets return the exact pool idle with a valid receipt. Terminal W uses actual zero remaining time and cannot reopen paid AT. Rebirth remains disabled; snapshots do not mutate scheduler state. |

The original numerical requirements remain: standalone tier-specific ratios can
rank tiers differently from equal ordinary shares; AUG selects from the pool
AFTER earlier AT caps, not current/pre-priority Energy; capped sharing is exactly
51/949 in the regression; pinned refresh performs zero redundant model writes;
zero budget/time reject; full combined start prices cannot spend the same wallet
twice; paid partials are not charged again; an unaffordable pin repins without
historical recall; and both Sadistic cap branches agree with native progress.
Explicit menu, challenge, boss-36/37 upgrade and completed-target guards are tested.

Preparation still covers mixed Energy-NGU/Magic-W, paid stage-wide W exclusion,
empty/different Magic focus, unavailable/completed/free AT, every explicit AT bar,
primary versus fallback generic AT, unmanaged Energy, and managed/unmanaged
Magic entry. Numeric selection itself must leave native allocations unchanged.

IL checks inspect the input production DLL: ordinary and quick-coordinator
branches must each reach their OWN stage resolution/application, including their
compiler-generated callbacks. An ordinary fallback cannot satisfy the quick
branch guard. Credit must observe the actual baseline map; the explicit AUG
adapter must use the full real claim lifecycle. Removed writers must stay absent.

## Boundaries

The in-memory game loader retains the existing fixed external bonus/income inputs
and presentation hooks. Native locks, target completion, prices, progress,
difficulty getters and resource accounting are NOT replaced. `realBaseGold` is
seeded consistently with the fixed forward-GPS factors and income so production
support parity checks remain enabled.

The shared BlackBar loader supplies its existing diagnostic hooks and offline
settings JSON codec. Its real fixture supplies coherent native inventory/support
models, an exclusive injector lease, actual plan/checkpoint stores and a current
production coordinator. Seeding training inputs preserves the epoch, inventory,
support and native/static controller identities. Fixture files live only in its
unique temporary directory and are disposed after each case.

Every top-level policy group is attempted; terminal integration is attempted even
if policy groups fail. Failures aggregate and attempted assertions are never
reported as a full pass. The default suite includes all groups; the explicit
terminal selector is diagnostic, not a replacement for the full result.

No installed-game save, bridge command, deployment, injection, restart or live
action is performed. Unity tick timing and actual later gold debit remain outside
this offline suite. A focused pass does not rerun or certify the other 11 native
suites on the same production artifact.

## Verification

Migration verified 2026-10-23. Logs are under
`C:\Users\Kevin\AppData\Local\Temp\opencode`.

| Gate | Retained result | Evidence |
| --- | --- | --- |
| Original fixture-only Rebuild under shared mutex | RED: five compiler errors, zero tests executed | `ordinary-policy-red-20261023.log`: missing `ExecutionResourceBudget`, `RequestAugmentAllocation`, `ExecutionBaselineResourceMapMatches`, `OptimizeAndAllocateIdle`, `OptimizeAndAllocateTarget` |
| Full focused native OrdinaryPolicy | GREEN: 167 assertions, exit 0 | `ordinary-policy-native-20261023-final-v2.log`; input production MVID `28cd2860-e5a2-4e35-94c3-78fb911a0ead` |
| Resumed full focused native OrdinaryPolicy before property-access correction | RED: builds pass; explicit allocation stops at `MissingFieldException: _resourceSetupExecutor`; 108 attempted policy assertions, not a pass count; terminal 42 passes | `ordinary-policy-resume-20261023-01.log`; input production MVID `6dd2382e-ced2-4dca-9024-d7061b91c448` |
| Resumed full focused native OrdinaryPolicy after property-access correction | GREEN: all 167 assertions, suite exit 0 and verifier completion | `ordinary-policy-resume-20261023-02.log`; input production MVID `6710f028-0a88-43fa-9b35-5d772ec02005` |

Final assertion breakdown: selection 14, ordinary pairs 18, affordability/repin 10,
unlocks 5, Sadistic numerics 5, preparation 17, stage presentation 25, explicit
real-authority allocation 19, integration IL 12, terminal main/quick timeline 42.
That is 125 policy assertions plus 42 real-coordinator assertions. Two new policy
groups explicitly cover unlock boundaries and production IL; existing allocation
and terminal requirements are migrated rather than skipped.

Earlier native logs `-01`, `-02`, `-03` and `-final` are failed harness iterations,
not pass evidence. The `-04` log is an intermediate 162-assertion pass on MVID
`dac48c7b-dbb3-439a-b9d6-15074b6fa120`, before strengthening pure-decision,
typed explicit-outcome and quick-path IL assertions. Both `-final-v2` and the
resumed `-02` run record the full 167-assertion result. The correction reads the
actual executor property through the property-aware reflection helper, not the
shared fixture's field-only accessor; no outcome or authority is substituted.
No combined 12-suite or live acceptance is claimed.
