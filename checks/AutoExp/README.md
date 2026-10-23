# Auto EXP Checks

## Run

From the repository root:

```powershell
dotnet run --project checks/AutoExp/AutoExpChecks.csproj
& checks/AutoExp/VerifyOffline.ps1 -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE' -LogPath 'C:\Users\Kevin\AppData\Local\Temp\opencode\autoexp-native.log'
```

The planner executable requires .NET 8 or newer. The native executable requires desktop MSBuild/.NET Framework 4.8 and the installed game assemblies. These are executable checks, not `dotnet test` projects. Each selected case runs even after another case fails. Failure returns 1; an unmatched filter returns 2.

Use `dotnet run --project checks/AutoExp/AutoExpChecks.csproj -- 'long caps'` or native `VerifyOffline.ps1 -GameFolder ... -Filter 'private stat'` to select case-insensitive name fragments. A filtered run is not a full AutoExp pass.

`VerifyOffline.ps1` delegates to `checks/VerifyExecutorOffline.ps1 -Suite AutoExp`. The shared gate rebuilds production and the referenced fixture, then runs every native AutoExp case while holding `Local\NGUInjector-Executor-Offline-Verification`. `-Filter` is forwarded as the single shared-gate selector. It never injects into or controls a running game. This rebuilds the current worktree; it is not frozen-artifact verification.

## Real Code

- `AutoExpChecks.csproj` compiles the unchanged production `Managers/ExpPurchasePlanner.cs` directly. It deliberately does not compile a fake partial `Main` alongside `Main.CustomExp`.
- `NativeChecks.csproj` references the existing PresetIntegration executable as the native boundary fixture. Its existing setup, loaders, finite purchase rendering seam, and real temporary checkpoint directories are reused by reflection. No friend assembly is needed. Shared setup in `checks/CombatPrepIntegration/Program.cs` no longer resets the deleted legacy `CurrentLock` properties; new fixture models already start with fresh real execution-claim state.
- The native executable invokes compiled `RunCustomAutoExp`/`BuyCustomExpStat`, `ExpPurchaseAction`, `RunFiniteAction`, execution grants/prerequisites, and the real disk-backed `FiniteActionLedger`. No runner, grant validation, ledger, or purchase classifier is copied or replaced.
- `NativeBoundary.cs` wraps actual native objects for the retained case inputs. Real Energy/Magic private purchase bodies, price formulas, affordability checks, currency debits, float additions, integer bar additions, and cap clamps execute. Every native EXP debit is observed for transient underflow. Native R3 purchase/cost calls and total-stat reads during AutoExp throw and are counted.
- Boundary observers are added to temporary assembly copies before the existing fixture loads them. Logging is recorded; result returns are observed without changing them. Scene rendering remains the existing finite fixture's substitution. Its callback records all nine real settings inputs and native costs as display snapshots; it is not a Unity widget test. The actual InputField focus flag/getter exercises manual-edit admission.
- Hardcaps are controlled scalar inputs, retaining the old small-hardcap fixtures. Price-offset and before/after-invocation faults are explicit boundary injections, not replacement cost/accounting implementations. Native hardcap derivation and live Unity listeners/formatting are not claimed tested.

## Safety Mapping

The old **96 cases** were **26 planner + 70 integration**. All 96 registrations are retained; none was dropped to make the migration green. `Stubs.cs` is removed because its replacement game accounting and partial `Main` are no longer used.

| Old Cases | Count | Current Location And Safety Contract |
|---|---:|---|
| Every `planner/` registration | 26 | `PlannerChecks.cs`, same names/inputs/literal results and 2,000 seed-13680 samples. Default/edited/scaled ratios, unlocks, exact/short prices, cap quanta, fractional headroom, hardcaps, int bounds, long caps/budgets, extreme-ratio regression, transaction endpoints, immutability, independent BigInteger costing. |
| Toggle, zero/negative EXP, three missing prerequisites, boss 16 | 7 | Same `IntegrationChecks.Register` cases, now invoking compiled AutoExp with actual native objects. No currency/stat/input mutation or native call. |
| Manual inputs 0 through 5 | 6 | Same focused-menu pause and subsequent-resume cases, using real InputField focus state. |
| Bosses 17/36; missing magic/model controller | 4 | Same energy-only purchases and costs, no Magic debit. |
| Energy cap 99,999/100,000 | 2 | Same independent unlock boundary and literal purchases. |
| All six routes, two active-menu displays, edited eight settings | 4 | Same BASE-only purchases, exact costs, input restoration and visible snapshot literals. Cost-query assertions allow the new real finite preflight rechecks, but require every route and exact amount. |
| Eleven power/request/gain tuples for both Energy/Magic | 22 | Same complete tuple list: below/at/above 2^24, upward/downward rounding, no-op amounts, and the `1f + 16777216` crossing. EXP paid must equal actual representable gain. |
| Five additional float regressions for both lanes | 10 | Same unrepresentable gain, seven-digit decimal-cast no-op, 64-unit hardcap headroom, partial gain/unused EXP, and unaffordable upward-rounding controls. |
| Cost disagreement; current-budget recheck | 2 | All six mismatched native costs still refuse. External EXP consumer now runs after the first durable result/presentation and before the next purchase, preserving the between-purchases budget contract. Currency interference *inside* native accounting is separately checked as unknown. |
| Every stat throws before/after mutation | 12 | Same reflected exception identity and all-input return checks. A before-mutation exception is durable unknown and withholds dependents; an exact after-mutation result is durable committed/render-failed and may continue. The exception is asserted in the real finite result, not an obsolete outer throw-and-stop log. Each case reloads disk evidence and refuses replay of the original opportunity. |
| 300 seed-250 integration fixtures, five ticks each | 1 | Same independent recorded-price conservation, transient nonnegative EXP, hardcaps, all-input return, unrelated state and no-R3 checks. A positive native-purchase count prevents vacuous success if all grants refuse. |

## Added Controls

41 additional native cases strengthen the migration:

- Six lane-grant refusals with no input borrowing/debit, followed by successful valid-grant retry.
- Six competing NativeSettings claims admitted during finite preflight, after grant resolution. The now-stale grant cannot borrow inputs or purchase; a fresh grant succeeds after claim release.
- Six cost changes between caller validation and finite preflight, followed by a safe price-restored retry.
- Six newer borrowed-input edits that the production return must not overwrite.
- Six clean committed-identity imports that refuse another native invocation and do not grant fresh authority.
- Two fractional BASE-power purchases preserving the existing fraction.
- Two outer presentation exceptions after durable commitment, with restored inputs and no replay after disk import.
- Two unknown native currency-delta cases that return the still-owned input and block every dependent NativeSettings leaf after import.
- Two lifecycle refusals (Quiescing/Unloaded), one conflicting NativeSettings claim with successful release/resume, and two real file-replacement faults testing in-session durable flush versus imported invocation quarantine.

Native settings return is conditional ownership, not unconditional cleanup: newer user inputs must survive. Unknown/uncommitted outcomes do not receive the durable-success presentation refresh. The old between-purchases budget test is not used to claim that unknown visible outcomes are repaired.

## Evidence And Gaps

Initial red: the old combined project fails with **11 compiler errors** for missing `ActionIdentity`, `ExecutionDomain`, `ResolveExecutionGrant`, `ExpPurchaseAction`, `RunFiniteAction`, and `ValidateExecutionGrant`. Its previous 96/96 claim is historical, not current evidence.

Current-assembly red: the first full shared-gate run built successfully but passed **0/111** (111 setup failures), with zero assertions, because shared fixture setup reflected deleted `LoadoutManager.CurrentLock`/`DiggerManager.CurrentLock` properties. Removing that obsolete reset loop, without adding compatibility properties or changing production authority, resolves the setup failure. The retained log is `C:\Users\Kevin\AppData\Local\Temp\opencode\autoexp-current-full-20261023.log`.

Final verification, 2026-10-23:

| Executed Scope | Result |
|---|---|
| `dotnet run --project checks/AutoExp/AutoExpChecks.csproj` | **26/26**, 0 failed, **59,410 assertions**; includes 2,000 deterministic samples. |
| `checks/AutoExp/VerifyOffline.ps1` without a filter, through shared `-Suite AutoExp` | **111/111** native AutoExp cases, 0 failed, **4,944 assertions**; includes all 70 old integration registrations and 41 added cases, plus 300 five-tick fixtures. |
| Combined AutoExp | **137/137**, 0 failed, **64,354 assertions**. |
| Shared `PresetIntegration --finite-action-cutover` gate | **431 native assertions**, exit 0, after production rebuild under the same named mutex. Not counted as AutoExp cases/assertions or a full PresetIntegration pass. |

Current native evidence: `C:\Users\Kevin\AppData\Local\Temp\opencode\autoexp-current-full-green-20261023.log`, completed 111/111 against production MVID `c1483d8b-621c-4b91-a05c-b755b1aba77b`. Shared finite-action evidence: `C:\Users\Kevin\AppData\Local\Temp\opencode\autoexp-shared-finite-20261023.log`. Both commands rebuilt current production under the shared named mutex. The pre-existing CS0649 warnings for `Program.NativeGearSwapped` and `GearOptimizerResult.MutationOutcome` remain. Production source is unchanged by this migration; no index changes, commits, or live actions were made. There is no outstanding AutoExp verification blocker.

Remaining boundaries: no live save, injector attachment, Unity scheduling, actual widget text/listeners/tooltips, native hardcap derivation, or future game-version compatibility is validated. Native purchase/accounting/grant/ledger claims are limited to the installed assemblies and the offline fixture. The targeted PresetIntegration finite gate is not a full PresetIntegration or repository-wide pass.
