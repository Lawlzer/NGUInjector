# Native Offline Regression Repair

## Result

All 13 full suites passed through `checks/VerifyExecutorOffline.ps1`, under its
shared native-verification mutex, using the installed game's managed assemblies.
The approved action-state simplification and persistence/preparation contracts
remain authoritative. No retired ledger, standalone-selection path, or imported
cleanup API was restored.

Retained full log:
`C:\Users\Kevin\AppData\Local\Temp\opencode\native-full13-repair3-20261023.log`.

## Production Changes

These are this repair session's incremental changes, not a summary of all existing
worktree changes.

| File | Confirmed Cause And Repair |
| --- | --- |
| `NGUInjector/Main.InventoryActions.cs` | Verified inventory leaves mutated the acquired material array in place. Publish a cloned array containing only the exact approved target delta. |
| `NGUInjector/Main.GlobalPresets.cs` | Preserve the acquired candidate's `SetupScore` in the current-stage selected-policy codec. |
| `NGUInjector/Main.ExecutionStore.cs` | Preserve the same frozen score in session-local dashboard selection data used for live scoped review. No restart file is created. |
| `NGUInjector/Main.ExecutionPersistence.cs` | Import and validate the frozen score. Existing admitted checkpoints that predate the field receive a read-only evaluation of their exact saved decisions, not replacement search. Explicit null, negative, or string scores cannot use that missing-field path. |
| `NGUInjector/Main.CombatRouting.cs` | Ordinary route grants now respect unresolved scoped route/native-settings conflicts. Gold-push retirement can no longer immediately overwrite a foreign route through fallback, including later ticks. |
| `NGUInjector/Main.PurchaseActions.cs` | Wire the existing one-purchase `CaptureRouteSupportGrowthApproval` into the digger purchase result boundary, after finite-runner invalidation. Verified compatible growth renews the original manual-titan grants without renewing its acquisition, death limit, or deadline. |
| `NGUInjector/Main.CombatPrep.cs` | Clear stale `AppliedResourcePhase` before failure handling can cancel a foreign/unverified operation. A previous refresh's phase credit must not survive the failed refresh. |

## Fixture And Contract Repairs

- `checks/PresetIntegration/RepeatablePurchaseChecks.cs`: remap local-variable
  operands when retaining either a constant Wandoos fallback or the already-native
  getter. Native OS formulas and unsupported exception-handler checks remain.
- `checks/PresetIntegration/RouteExecutorCutoverChecks.cs`: construct missing idle,
  heal, and hyper-regen scene inputs; set the tower-mode death limit explicitly;
  improve failed-respawn diagnostics; exercise repeated foreign-route fallback.
- `checks/Ownership/InventoryRescanChecks.cs`: explicitly prioritize the head before
  the chest in first-leaf/later-leaf fault cases, assert that ordering, and construct
  native Wish properties for cube-ratio calculation. Production ordering is unchanged.
- `checks/PresetIntegration/RetainedFarmingRebindChecks.cs`: preserve captured-farming
  coverage and add score round-trip plus explicit null/negative/string rejection.
- `checks/PresetIntegration/ExecutionPersistenceChecks.cs`: test both new-run
  AutoUpdate acquisition and AutoUpdate-off missing-cache refusal; initialize the
  scheduler before using its checkpoint path.
- `checks/PresetIntegration/DesiredMapExecutionChecks.cs`: test valid frozen intent
  resume without mandatory history review, no imported receipt, no replacement
  search, precise invalid-provenance refusal, and snapshot-free first acquisition.
  Compare complete frozen decisions while independently checking refreshed location
  hints, which are observations rather than ownership.
- `checks/PresetIntegration/ExecutionCoreCutoverChecks.cs`: assert session-local
  dashboard retirement instead of calling the removed standalone file-path API.
- `checks/CombatPrepIntegration/Program.cs`: only owned known-partial setup retries;
  foreign drag/lock/epoch acquisitions fail and cannot later swap or fund a phase.
- `checks/CombatPrepIntegration/AutomaticReconciliationRegressions.cs`: cancellation
  never earns completion or refreshes the original deadline. Preserve unresolved AT
  targets while cancelled bounded attempts use conservative background continuation.
  Restart imports no historical cleanup authority; fresh-session failed checkpoint
  transitions retain real IO-failure/retry coverage.
- `checks/CombatPrepIntegration/CleanupRecoveryRegressions.cs` and
  `checks/CombatPrepIntegration/ActivityTimingRegressions.cs`: inject actual unload
  release failures, verify fault consumption, and observe the old run before ordinary
  same-entry continuation creates a distinct run. Keep cadence, stale-callback,
  native-combat handoff, original timing, and no-rollback assertions.
- `checks/CombatPrepIntegration/LiveResourceRegressions.cs`: foreign resource callbacks
  cancel the bounded preparation, clear dependent credit, preserve foreign lanes,
  and cannot replay the phase.
- `checks/CombatPrepIntegration/AutoBoostRegressions.cs` and
  `checks/CombatPrepIntegration/PresetInterruptionRegressions.cs`: restart has no
  fabricated historical cleanup/receipt authority; current maintenance and manual
  intent still proceed. Pending live cleanup remains a real negative control.
- `checks/CombatPrepIntegration/AdventureWeightRegressions.cs`: model interruption by
  replacing the coordinator after checkpointing, rather than first cancelling the
  bounded attempt. Preserve acquired AT gates and reject foreign material callbacks.

## Full Native Proof

| Suite | Assertions | Result |
| --- | ---: | --- |
| PresetIntegration | 19,619 | PASS |
| CombatPrepIntegration | 12,224 | PASS |
| ManualAdventureOverlay | 297 | PASS |
| BlackBar | 24,758 | PASS |
| Ownership | 10,470 | PASS |
| TitanGold | 1,497 | PASS |
| PresetEquipment | 94 | PASS |
| GearEquip | 20,805 | PASS |
| OrdinaryPolicy | 167 | PASS |
| WinFormsBlood | 174 | PASS |
| StageTargetsNative | 48 | PASS |
| ResourceAggregation | 4,646 | PASS |
| AutoExp | 5,169 | PASS |

Counts are per-suite totals; shared coverage is intentionally repeated across suites.
The full gate passed without relying on failed-suite diagnostic selectors.
`git diff --check` also passed.

Tested production MVID: `ac725df2-e572-4ad8-bfd5-c842e9d98660`.

SHA256: `6DAAAE7827C42A36A49785C12201963412E7F4916499D4240A8E8B1924A11D55`.
The Release DLL and `bin/Release/dist/injector/NGUInjector.dll` match.

## Handoff Boundary

No staging, commit, artifact capture, injection, restart, or live mutation was
performed. Files reserved for the parent (logging, external UI, execution telemetry,
resource coordination, error log, ExternalUi checks, UI, and error-reporting docs)
were not edited. Parent telemetry verification and the final coordinated artifact
capture/safe restart/live verification remain separate work; this report claims
offline native verification only.
