# Auto EXP Checks

## Run

From the repository root:

```powershell
.\verify.ps1 -Scope Source
.\verify.ps1 -Scope Native -Suite AutoExp
# Focused native case-name fragment:
.\verify.ps1 -Scope Native -Suite AutoExp -Selector 'private stat'
```

Source includes the planner executable and requires .NET 8 or newer. Native requires desktop MSBuild/.NET Framework 4.8 and the installed game assemblies: set `NGU_IDLE_DIR` or pass `-GameFolder`. These are executable checks, not `dotnet test` projects. A failed case does not suppress remaining selected cases. The executable returns 1 for failure or 2 for an unmatched filter; inspect the public runner's report/exit separately.

Native `-Selector` accepts a case-insensitive name fragment for this suite. The retained `finite/` name prefix selects purchase guard checks, not durable action history. A filtered pass is not the full AutoExp suite or an all-scopes gate.

`verify.ps1` uses one incremental native MSBuild invocation to compile production and selected fixture dependencies, including PresetIntegration for AutoExp, then runs separate executables under shared verification ownership. Do not edit those sources or run direct builds/EXEs over an active verifier. Reports retain phase logs and artifact identity; contention/defer/drift exits 2 and appends `artifacts/agent-handoff.md`. See [Testing And Save Safety](../../docs/testing.md). No injection, deployment or live purchase is performed.

## Production Boundary

- `AutoExpChecks.csproj` source-links the unchanged production `Managers/ExpPurchasePlanner.cs`. It does not compile a fake partial `Main`.
- `NativeChecks.csproj` references PresetIntegration for its existing data-only native object construction, loaders, temporary directories and presentation boundaries.
- The native executable runs compiled `Main.CustomExp`, `Main.PurchaseActions`, `RunFiniteAction`, and real execution-grant/prerequisite checks. No purchase classifier or admission policy is copied into a stub.
- Real Energy/Magic price functions and private purchase bodies execute, including affordability, EXP debits, float additions, integer additions and cap clamps. Native R3 purchase/cost calls and total-stat reads during AutoExp throw and are counted.
- Temporary assembly copies add cost, invocation, result and EXP-write observers. Existing scene rendering substitutes record all nine native settings inputs and visible price snapshots. Real InputField focus state is used for manual-edit protection.
- Hardcaps are explicit scalar inputs for small-limit cases. Price offsets and before/after-invocation faults are boundary injections, not replacement accounting.

## Repeatable Contract

Each ordinary pass plans from current BASE stats, ratios, unlocks and EXP. A batch invokes each planned stat route at most once. Its result reports `Completed`, `Invoked`, `Spent` and `Last`.

Both predicted power sums explicitly round through four-byte `Single` storage before the existing 2^24 clipping/no-op checks. A CLR floating local can otherwise retain extra precision and authorize payment for a gain the native power field cannot store. Native cost agreement, exact gain classification and all original float expectations remain unchanged.

An unverified attempt stops the remaining old plan. The next ordinary pass reads current native state and plans again; it does not replay saved amounts or consult disk history. A native exception before mutation permits a fresh next-pass attempt. Exact accounting after a native render exception can still verify commitment. A separate presentation failure stops the batch without undoing the native purchase.

`RunFiniteAction` returns current `Invoked`, `Committed`, `AuthorityCurrent`, `Disposition`, `Reason` and `Error`; `Verified` means committed with current authority. Purchase admission has no `Durable`, `PreviouslyCommitted`, file-replacement dependency or durable unknown quarantine.

The batch and leaf each reject callback reentry, including presentation after the native runner returns. Borrowed inputs are returned only while the same settings object, grant and exact borrowed value remain owned; newer edits survive. No currency or stat compensation is attempted after foreign interference.

## Retained Coverage

The original 96 cases comprised 26 planner and 70 integration registrations. All their mathematical/native behaviors remain covered; persistence assertions are intentionally replaced by the repeatable contract.

| Original Cases | Count | Current Assertions |
|---|---:|---|
| Every `planner/` registration | 26 | Same default/edited/scaled ratios, literal outputs, unlocks, exact/short budgets, quanta, hardcaps, transaction endpoints, extreme ranges, independent BigInteger costing and 2,000 seeded samples. |
| Toggle, zero/negative EXP, missing prerequisites, boss 16 | 7 | No purchase, currency/stat/input mutation or swallowed error. |
| Manual input 0 through 5 | 6 | Focused-menu pause, then successful resume. |
| Bosses 17/36; missing magic/model controller | 4 | Energy-only native purchases. |
| Energy cap 99,999/100,000 | 2 | Exact independent cap unlock boundary. |
| All six routes, two active-menu displays, edited ratios | 4 | BASE-only selection, exact native prices, all saved inputs and visible price restoration. |
| Eleven power/request/gain tuples for both lanes | 22 | Below/at/above 2^24, upward/downward rounding, no-op requests and the `1f + 16777216` crossing; payment equals representable gain. |
| Five additional float regressions for both lanes | 10 | Seven-digit decimal-cast trap, no-op gains, 64-unit headroom, unused EXP and upward-rounding affordability. |
| Cost disagreement; current-budget recheck | 2 | All six routes independently reject mismatched prices; a batch stops on its first refusal. External EXP use between leaves cannot reach an unaffordable old-plan purchase. |
| Every stat throws before/after mutation | 12 | Exception identity, exact native prefix, input return and batch outcome. Unknown pre-mutation results stop only that batch; fresh passes finish remaining purchases. Exact committed render failures never undo accounting. |
| 300 seeded integration fixtures, five ticks each | 1 | Native price conservation, nonnegative transient EXP, hardcaps, all-input return, unrelated state and no R3. A positive native-call count prevents vacuous grant-refused success. |

## Additional Controls

49 native cases supplement the original 70 integration registrations:

- Six disabled-lane refusals followed by successful valid-grant retry.
- Six competing NativeSettings claims admitted during preflight; stale grants cannot borrow inputs or invoke.
- Six preflight price changes followed by successful fresh retry.
- Six preflight hardcap changes refusing a paid, clamped gain, followed by a valid-limit retry.
- Six newer borrowed-input edits that must survive cleanup.
- Six fresh identical-state requests proving old opportunities are not disk-deduplicated.
- Two fractional BASE-power cases.
- Two outer presentation failures followed by ordinary fresh replanning without repeating the committed prefix.
- Two unexpected currency consumers proving current-batch stop, retained foreign state and next-pass recovery.
- Two lifecycle refusals, one NativeSettings claim release/resume, and two unavailable/obsolete-history cases proving no purchase file I/O admission.
- Two native/presentation callback-reentry cases checking both the whole batch and direct purchase leaf.

The current source registers 26 planner and 119 native cases. Both have been executed successfully for the repeatable contract; see the retained evidence below. Previous ledger-based green totals are not used as verification of this contract.

## Digger Integration

`checks/PresetIntegration/RepeatablePurchaseChecks.cs` registers 64 parameterized native scenarios covering digger selection, affordability, unlock/table/context guards, compatible claims, failure/reentry, owned-versus-foreign maximum growth, and ordinary-loop cadence. The coordinator's PresetIntegration manifest, `Program.ConfigureRepeatablePurchaseGameFixture` game-loader hook, and `--repeatable-purchases` selector are wired. No shared manifest or Program entry point is edited by this purchase change. All 64 scenarios have completed successfully.

The parent preset fixture substitutes `Wandoos98Controller.wandoosBonus()` with constant one. The purchase game hook restores that getter's original installed-game IL only while this selector is active, preserving the parent's fallback for unrelated tests. This keeps nonzero-level manual-prep baseline parity real rather than clearing levels or changing the planner's validation/formulas.

Ordinary purchases honor `UpgradeDiggers`, use no gold reserve, prioritize affordable zero-maxima rows before native price/ID, and invoke at most once per service call. The ordinary-loop owner calls that service once before prep/stage early returns; other automatic callers are removed by their owner.

Background growth retains its `FiniteAction | Support` footprint. A bounded resolved-claim grant admits only current, actually applied setups whose owner/acquisition, claim/setup identities, observations, policy, exact gear and support remain owned. Unprepared claims and foreign setup/state changes still refuse admission. No other owner's claim or selected support levels are replaced.

Verified owned growth advances only the active prep's exact support-material observation and pinned effects/rates. Its purchase-specific prep grant binds the same captured compatible owners and requires the exact post-purchase delta rather than treating the old maximum fingerprint as unchanged. This is an observation proof: the ordinary prep grant may remain suspended by another owner, and growth does not set `SetupVerified` or grant allocation/phase credit. It does not resize support, run an optimizer search, replace policy/setup, reset targets or renew a phase/deadline. Extra or later foreign maximum changes remain unapproved. Growth cases hold a verified overlapping Support/FiniteAction claim during the native call, exercise actual ordinary-grant suspension, and require a real pinned Wandoos-rate increase.

Cadence checks invoke compiled `AutomationRoutine` and `QuickStuff`, including actual manual prep, automatic entry and active automatic prep. They assert one upgrade per ordinary pass, no rapid-pass purchase, next-pass fresh pricing/retry, lifecycle refusal, routine reentry exclusion, guard/timer cleanup after early returns, and independent EXP service after a digger failure. These are offline dispatch checks, not live Unity timer verification.

## Historical Verification

Executed 2026-10-23. Both native commands used the then-current `checks/VerifyExecutorOffline.ps1` under `Local\NGUInjector-Executor-Offline-Verification`, rebuilding that production and selected fixture. The removed wrapper name and results below are historical evidence, not current commands or verification of the new runners.

| Scope | Result | Production MVID |
|---|---|---|
| `dotnet run --project checks/AutoExp/AutoExpChecks.csproj` | 26/26 planner cases; 59,410 assertions | Source-linked pure planner |
| Shared gate `-Suite AutoExp` | 119/119 native cases; 5,169 assertions | `e590abb0-2565-49cc-af91-f5d20661de27` |
| Shared gate `-Suite PresetIntegration -Selector --repeatable-purchases` | All 64 scenarios; 695 local assertions, 697 including shared setup | `1d0d02ac-3db5-4578-a811-417e49a5335e` |

AutoExp totals: 145/145 cases and 64,579 assertions. The purchase selector's shared setup assertions are reported separately, not presented as additional scenarios.

Retained logs in `C:\Users\Kevin\AppData\Local\Temp\opencode`:

- `autoexp-repeatable-owned-full-20261023-2.log`: complete native AutoExp green run.
- `repeatable-purchases-owned-native-20261023-2.log`: final focused purchase green run after the binary32 fix.
- `autoexp-repeatable-owned-full-20261023-1.log`: initial 97/119 result exposing all 22 power-rounding failures; their expectations were retained.
- `repeatable-purchases-ready-c538382a2ff441dab1b9353d2293e8d5.log`: earlier 604-assertion failure caused by the parent fixture's constant-one Wandoos bonus.

The separate rebuilds have different MVIDs; this is scoped worktree verification, not a frozen-artifact or repository-wide pass. The pre-existing CS0649 warnings for `GearOptimizerResult.MutationOutcome` and `Program.NativeGearSwapped` remain.

## Remaining Boundaries

No live save, injector attachment, Unity scheduling, widget/listener behavior, native hardcap derivation or future game-version compatibility is validated. The native fixture's rendering substitutes do not establish live presentation correctness. Route cached-grant reauthorization and unrelated source-linked suites have their own owners and are not claimed verified by these scoped results.
