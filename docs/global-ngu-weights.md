# Global NGU Weights

Misc settings are the sole NGU selection/ratio configuration: serialized `_energyNguWeights` (nine nonnegative integers) and `_magicNguWeights` (seven), exposed by `SavedSettings.EnergyNguWeights` / `MagicNguWeights`. Defaults are `[0,0,0,0,1,0,1,0,0]` and `[1,0,0,0,0,0,0]`. Plan version stays 17. Runtime advertises `globalNguWeightsVersion: 1`, `globalPresetsVersion: 2`, and `loadoutPresetsVersion: 2`; compact/full settings metadata includes both serialized arrays.

Stages, Quick focus (including fallback NGUs), Combat Prep leftovers, preset capture/search/apply, and acquired-policy refresh all read these settings. Zero excludes a target even for an explicit `NGU-n` priority. Positive explicit priorities remain ordered; generic groups use proportional capped sharing. Legacy allocation-profile NGU/CAPNGU/ALLNGU breakpoints also trigger the global weighted group instead of per-profile target ratios. An entirely excluded legacy NGU lane recalls its old managed resources rather than leaving stale allocations behind.

Inline `previewLoadoutPreset` accepts only `preset`; `applyLoadoutPreset` accepts only `preset`, `previewId`, and `candidateKey`. Deprecated lowercase `energyNguWeights` / `magicNguWeights` arguments are rejected, including arrays identical to the globals. Global-library commands retain their ID/revision contract. Preview identity and material include the global weights; any value change rejects an old Apply before game mutations. Equal-value settings edits do not invalidate a policy.

## Migration

Before any plan repair or normalization, the injector copies the selected legacy plan's effective NGU weights into missing global settings and persists them atomically. Existing global values always win; selecting, copying or importing another plan never changes them. With no selected legacy plan, the defaults above apply. Invalid arrays used for this initial migration fail without overwriting settings. Normal reads ignore retired plan/preset arrays, and subsequent saves strip them, including historical goal/loadout rows. Existing preset IDs and import/deletion receipts remain valid. Offline edits that could discard unmigrated weights require connecting to the injector first.

## Running Policies

- A global weight edit invalidates the preview, reusable policy cache, pending search and stage credit. Policy weight arrays and applied snapshot arrays are observations used for compatibility checks, never alternate configuration sources.
- Quick focus notices value changes before its refresh cooldown and rebalances its owned lanes. Ordinary stages and Combat Prep use globals on their next eligible allocation pass.
- A running preset immediately fails receipt/replay compatibility with old weights. Its receipt is retained while waiting for safe ownership, never silently dropped while excluded allocations remain active.
- On the next eligible preset service pass, matching owned energy and magic lanes are recalled before any new search or replay. Temporary owners, locks, disabled management, epoch changes or foreign allocation edits prevent unsafe recall. Telemetry/status tells the user to restore ownership or recall resources and explicitly re-equip. No foreign allocation is adopted or overwritten to satisfy cleanup.
- Scheduled presets acquired with `AutoUpdate: true` reacquire with the current globals on the next scheduled application pass, retaining stage entry/history and plan/auto-rebirth permissions. The normal readiness, preflight and retry protections remain in effect. Reacquisition is a new optimizer event, not replay of a stale split.
- Dashboard presets and scheduled presets acquired with `AutoUpdate: false` stop their owned allocations and require explicit re-equip or Preview + Apply. They do not silently run an optimizer. A non-updating preset has no reusable cache after a weight edit, so Preview + Apply or enabling AutoUpdate is necessary. Retired receipts cannot earn stage credit or replay allocations.
- Applied snapshot `energyNguWeights` / `magicNguWeights` remain immutable historical observations. `globalWeightsRequireReequip`, `status: "weights-changed"`, `currentOwner`, `requiresReapply` and the reason distinguish a waiting weight change from successful current execution.

## Stage Restoration

A configured or inspected NGU preset is not necessarily the running allocation. Check
`planProgress.activeStageIndex`, the current row's `presetStatus`, the active
`loadoutPreset` receipt, and actual `nguAllocation.energyTargets[].allocated` values.
An earlier Time Machine stage can consume the entire energy pool while the saved
NGU stage remains pending. Selecting a different stage with `setPlanStage` records
an explicit waiver of unfinished prior rows for that pass; it does not edit the plan.

Epoch observation must discard live equipment/resource ownership after a cloud/save
load without suppressing validation of the loaded save's durable stage checkpoint.
Previously, `ObserveCustomRebirthEpoch` indirectly set `_protectedSkipRestore` and
could restart the opener even when a matching checkpoint existed. It now preserves
the pre-observation suppression flag around runtime cleanup. Deliberate resets still
suppress restore; plan/rebirth/origin/rollback and codec checks still reject invalid
checkpoints. Restored fixed work stays unarmed until a fresh application, with no
offline training credit.

Verification on 2026-10-19: the added real-observer integration cases failed against
the old DLL (later saved stage restored as stage zero) and passed after the fix.
The Release build passed, the scheduler integration suite passed 2,017 assertions,
and the full BlackBar suite passed 23,507. A saved live `24hr` run was restarted with
the rebuilt injector and cloud loading: stage 4, generation and entry identity were
retained, the NGU Energy preset reacquired stage ownership, and applied time resumed.
Live energy was 129,198,434 across Augments/Wandoos/Gold/Power NGUs, with 12 retained
in basic training and none idle. The saved stage sequence, NGU weights, and enabled
auto-rebirth setting were unchanged; prior preparation rows were explicitly skipped
for this rebirth to restore the requested NGU activity. The pre-investigation
checkpoint had already been overwritten, so its original validity cannot be proven
from the retained live artifacts.

## Offline Checks

`dotnet run --project checks/SustainedFocus/SustainedFocusChecks.csproj -c Release` exercises source-linked focus/stage allocation, global edits, fallbacks and explicit zero exclusions. `PresetIntegrationChecks.exe <injector.dll> <managed-directory> --global-ngu-weights` exercises the compiled capture/preview/cache/ownership lifecycle with an isolated native-data fixture. These do not validate live Unity scheduling, rendering or save behavior.

Offline verification on 2026-10-19: production Release build passed; SustainedFocus passed 2,043 assertions, the focused global-weight adapter selector passed 44, the full preset adapter suite passed 7,672, Combat Prep integration passed 7,285, BlackBar passed 23,072, and OrdinaryPolicy passed 76. Follow-up retirement checks passed 2,435 storage/migration assertions, 2,139 preset/model assertions and 9,245 ExternalUi assertions. These results describe the shared checkout at verification time, including concurrent changes. Frontend tests and Svelte checking passed; desktop/mobile browser checks use mocked IPC. No live game, installed DLL or save was modified by these checks.
