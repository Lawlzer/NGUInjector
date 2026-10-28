# Persistence Cutover

## Implemented Boundary

- No finite-action, route, preparation or lifecycle journal is read, imported,
  rewritten, deleted or required by the owned persistence, telemetry or save-test
  admission code. Existing malformed/read-only records are inert.
- Dashboard, manual, Quick/F8 and focus selections are bounded live intent only.
  F8 keeps its explicit return baseline within the current session. Restart discards
  that authority without mutating native state. Real current foreign setup/resource
  conflicts retain tokenized scoped review.
- A current-stage selected policy remains in its stage checkpoint to preserve frozen
  equipment/support, scoring references, acquisition, horizon and AutoUpdate choices.
  A valid snapshot restores intent directly; ordinary native admission still verifies
  all effects. No application receipt or action history is imported. An entry with
  no acquired snapshot remains eligible for its first normal selection; saving a
  successor entry does not carry the previous entry's snapshot forward.
- The stage scheduler schema is unchanged: fixed earned credit, entry/cycle,
  completed/skipped obligations, original deadlines, pools and acquired AT/Wandoos
  progress/continuations remain. A restart drops cleanup authority and does not turn
  cancellation into earned completion or grant a new setup budget.
- Native forward rebirth may roll a valid immediately preceding run's checkpoint
  only when rebirth increases by exactly one and native total/run-origin clocks
  dominate the old checkpoint's internally consistent clocks. This is the approved
  normal-forward contract, not arbitrary save/sidecar branch reconciliation. Corrupt,
  backward or multi-rebirth mismatches retain explicit stage recovery. A late helper
  reloads, rather than deletes, an already-current checkpoint and its same-run credit.

## Retained Files

- User settings, custom plans, preset library/definitions and their existing
  CAS/unknown-field handling are unchanged.
- `stage-progress-<plan hash>.json`: one existing checkpoint per plan, including
  only the selected current-stage intent needed for frozen selection semantics.
- `stage-progress-<plan hash>.json.recovery.json`: one bounded latest rejected stage
  checkpoint, written only on explicit corrupt-stage recovery; no action archives.
- Command-ID claims/results remain independently owned by the external UI transport.
- Iron Pill's single bounded phase/timing checkpoint is owned by B; this cutover
  neither implements nor duplicates it.
- Save-safety/save-test journals and protected bundles remain separately necessary
  for explicit save-test restore. They are not native action admission history.

## Coordinator Hooks

These are outside this agent's permitted files and must land before merge gates:

- Remove `EnsureStandaloneSelectionLoaded()` from `Main.cs` startup.
- Remove `PersistLifecycleExecutionEvidence()` from `Main.Lifecycle.cs` quiesce.
  Keep the existing live native-busy and route/settings retirement guards.
- In `Main.ExternalUi.cs`, keep command-ID dedup/claims but remove completed-command
  enumeration/import of `execution-*-selection.json` and the call to the removed
  `ExportAcceptedExecutionProcess()` helper. A claimed command is never replayable
  merely because an acceptance/result write fails.
- `SaveTestBundle.cs` still includes `finite-actions-v1` in `Trees` and all root JSON
  files through `InScope`. Its owner must exclude retired action/route/preparation/
  lifecycle/standalone-selection records from capture, fixture preparation and
  restore scope so malformed/unreadable/read-only retired files are inert there too.
  Preserve explicit save-test recovery journals, configuration and native originals.
- The route/preparation owner removes its old import/export/rebind APIs and stops
  publishing `fight-prep` through `PersistExecutionSetupSelection`; that producer
  hook now accepts live manual/Quick/focus kinds only.
- B's shared `CoordinateRebirthAttempt` must invoke
  `CommitExecutionRebirthCheckpoint()` immediately after verified native +1 progress,
  before resetting runtime or publishing history/UI. Do not require
  `AuthorityCurrent` after a native rebirth that deliberately replaces the epoch.
  The startup rollover path covers interruption before/during checkpoint write.
- Automatic preparation's live cancellation paths must not call
  `CompleteAutomaticEntry` solely because of cancellation. The persistence importer
  no longer imports `_automaticCancelledEntry` or historical cleanup authority.
- Epoch reset's producer-owned route/preparation state must be discarded locally,
  without old cleanup writes or imported authority. The owned lifecycle reset clears
  claims and manual/Quick/focus/preset intent locally; do not call native cleanup
  merely to retire a stale process.

## Verification

`checks/PresetIntegration/ExecutionPersistenceSourceChecks.ps1` verifies the owned
source boundary and compiles only the production pure forward-rebirth predicate
with real JSON codecs in memory. It never builds the injector or references native
assemblies. The existing pure ProtectedTiming runner covers scheduler schema,
earned credit, deadlines, pools and completed/skipped obligations.

Owned verification passed: 59 source/pure predicate/syntax assertions and 66 pure
ProtectedTiming scenarios (1,981 assertions). These are not native integration runs.

Updated `ExecutionPersistenceChecks.cs` covers inert malformed/read-only old records,
normal frozen-stage resume for both AutoUpdate modes, consumed entry fights,
same-session F8 return/discard, real foreign focus/resource review, lifecycle
revocation, corrupt-stage recovery, forward-rebirth crash rollover and late-helper
same-run credit retention. `RetainedFarmingRebindChecks.cs` starts its captured
farming fixture through normal resume without a mandatory review, while preserving
adversarial live foreign-state review controls. These native gates must wait for
merge GO and a fresh merged production artifact; no prior DLL validates this work.

## Current Native Follow-Up (October 24)

Coordinator merge GO supersedes the pending-merge wording above. Startup's
`EnsureStandaloneSelectionLoaded`, lifecycle persistence and accepted-command
metadata hooks have been removed by their owners. Save-bundle retired-file
filtering is also integrated; this follow-up did not edit those integrations.
Automatic-preparation cancellation remains separately owned and was not edited.

Both reported D failures were already corrected in the merged source before this
follow-up's first build. No additional production fix was needed or applied:

- Retained farming: a saved selection has no objective-only search rank or
  eligibility witness. Treating that absence as `false` rejected otherwise feasible
  saved decisions. Current `PresetCandidate.SetupEligible` is nullable; replay
  rejects explicit `false`, not absent metadata. Import keeps `SetupScore` and
  `SetupEligible` absent while checking actual current setup feasibility. No rank,
  display name, application receipt or historical action authority is fabricated.
- Post-rebirth selection: reset correctly removes the preceding run's recipe.
  The old fixture expected automatic acquisition even with AutoUpdate disabled.
  Current coverage explicitly requires that case to wait for Preview + Apply with
  no searches, gear swaps or accounting writes, while AutoUpdate enabled acquires
  a distinct new-run policy through ordinary reconciliation.

The retained original red evidence is
`shared-native-migration-20261023-185607-df720171021e4ac5a02d95149f0ba3d7.log`
(retained farming) and `native-persistence-diagnostic-20261023.log` (the precise
AutoUpdate-off missing-policy refusal), under
`C:\Users\Kevin\AppData\Local\Temp\opencode`. These are not current failures.
Initial fresh focused runs passed 468 retained-farming and 325 persistence
assertions, before adding the busy-checkpoint regression.

### Busy Checkpoint Proof

`ExecutionPersistenceChecks.CheckBusyStageCheckpoint` now exercises all four
combinations of native `isFighting` / `nukeBoss` and AutoUpdate disabled / enabled:

- Starts from real verified scheduled setup, earns one second through ordinary
  service using the native run clock and an actual heartbeat interval, and acquires
  a real same-session Quick claim that actually equips a distinct same-capacity
  head. No earned/applied array or receipt is seeded. An accidental historical
  return would therefore change the equipped instance and swap counter.
- Busy quiescence saves the current-stage checkpoint but refuses prerequisite
  retirement and revokes old grants. Reset discards Quick/return authority locally.
- Reload restores the original acquisition, definition, horizon, frozen scoring
  references, earned/applied credit, entry/cycle, deadline, completed/skipped state
  and consumed entry fight. The scheduler remains unarmed and no receipt is imported.
- Observations retain Quiescing, show no discarded claims or verified resource/
  support authority, and require no retired-action review. Native boss activity,
  gear instances/material, support, both resource lanes, Basic Training/R3, AT/OS
  and Wandoos history remain unchanged. Resource transfers, gear swaps, recalls,
  route entries, support refreshes and replacement searches all stay at zero.
- Reset, observation and reload preserve the exact saved checkpoint bytes and
  create no lifecycle cleanup journal. Existing ordinary frozen-stage resume tests
  independently verify native continuation without searching or approving history.

Final focused persistence passed 573 total assertions (571 within the group,
including 248 added assertions across the four busy cases). Log:
`d-persistence-final-20261024-4.log`; production MVID
`1f2354fa-e50a-437d-8e38-b34f44185358`.
Final focused retained farming passed 468 total assertions (466 within the group).
Log: `d-retained-final-20261024-1.log`; production MVID
`158a347f-9519-468d-b017-40bc310c2177`. Each selector used the verifier's named mutex
and a fresh production/fixture rebuild; separate builds are not one-artifact
full-release evidence. Result-projection/authority exceptions in the retained
farming log are expected injected negative controls, not failing assertions.
The source/pure rollover check passed 59 assertions; ProtectedTiming passed all
66 scenarios / 1,981 assertions. `git diff --check` passed.

The complete PresetIntegration attempt in `d-preset-full-20261024-1.log`, before
the distinct-Quick-head strengthening, passed both D groups (persistence 567;
retained farming 466), then exceeded the terminal's
600-second bound after manual-titan capacity-consumer coverage. There was no
failing assertion or final suite result before termination. This is incomplete,
not a full-suite pass or a diagnosed route failure. No PresetIntegration child
remained afterward. A fresh coordinated all-suite gate remains necessary; earlier
all-suite counts do not validate this tree. No live restart/deploy, staging, commit
or artifact capture was performed.

### File Boundaries

The D production allowlist remains `Main.ExecutionStore.cs`,
`Main.ExecutionPersistence.cs`, `Main.ExecutionIngress.cs`,
`Main.ExecutionLifecycle.cs`, `Main.ProtectedTiming.cs`,
`Main.ExecutionTelemetry.cs` and `Main.SaveTests.cs`, all under `NGUInjector/`.
Retained D checks are `checks/PresetIntegration/ExecutionPersistenceChecks.cs`,
`RetainedFarmingRebindChecks.cs` and `ExecutionPersistenceSourceChecks.ps1`.
This follow-up changed only `ExecutionPersistenceChecks.cs`, the source-check
runner's status wording and this document; all pre-existing changes remain.

The runtime retained-file allowlist remains the Retained Files section above:
configuration, one stage checkpoint per plan, its one explicit-recovery copy,
command-ID transport claims/results, the separately owned single Iron Pill timing
checkpoint and explicit save-safety/save-test journals/bundles/native originals.
No finite-action, route, preparation, lifecycle or transient-selection restart
history was reinstated or cleaned up.
