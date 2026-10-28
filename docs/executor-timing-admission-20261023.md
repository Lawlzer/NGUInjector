# Executor Timing And Admission Verification

## Scoped Fixes

- `Main.ProtectedTiming.cs`: inline stage ownership now requires `ExecutionSetupVerified()` when setup claims are active. Cached inline observations and resource receipts cannot credit a claim that has not acquired its setup. The existing no-claim and verified composed-claim paths remain available.
- `Main.CustomAutomation.cs`: a new entry fight waits before persisting its receipt when a claim already owns any part of the preparation footprint (gear, support, route, finite action, inventory). Existing live retries and durable replay protection are unchanged. Ownership of this minimal additional production edit was explicitly approved.
- `Main.BossPush.cs` was inspected, not changed. The confirmed receipt bug was at the entry-fight caller, not the boss-push leaf.
- No duration, deadline, entry/fight identity format, or checkpoint schema was changed.

## Fixture Corrections

- `NativeFightFixture.InitializeMain` supplies the managed save-safety collections skipped by uninitialized Unity fixture construction. Protected timing, stage support, and Iron Pill fixtures use it. No production null guard was added by this task.
- Entry-fight regression coverage now includes both loadout and digger claims, no preparation record/credit on denial, and unchanged entry/deadline after release.
- Inventory-drag preparation rejection assertions match the specific native preflight error and retain all previous-intent/process-preservation checks.
- `IronPillReservationChecks.Run` restores `IronPillSetupBoundary.Enabled` in `finally`. Previously `CheckPreemption` enabled the shared boundary, then subsequent native accessory/offhand getters returned fixture zeroes. This caused `GearOptimizerChecks.cs:230` (expected two accessory slots, observed zero) and `CustomAutoBoostChecks.cs:199` (missing offhand slot `-6`). The boundary correction fixes both without touching their production paths or weakening expectations.
- Existing shared fixture compile corrections were validated by the full gate: all 13 fixture builds succeeded, including the source-linked combat fixture projects.

## Offline Verification

All runs used `checks/VerifyExecutorOffline.ps1`, which holds `Local\NGUInjector-Executor-Offline-Verification`, with game assembly references from `C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE`. Logs are retained in `C:\Users\Kevin\AppData\Local\Temp\opencode`.

| Run | Result | Log Filename |
| --- | --- | --- |
| BlackBar `protected` | PASS: 2,947 assertions, including 309 scheduling assertions | `executor-timing-admission-protected-20261023.log` |
| BlackBar `stage-support` | PASS: 405 assertions | `executor-timing-admission-support-20261023.log` |
| Full default 13-suite gate | 11 passed; CombatPrepIntegration and BlackBar failed | `executor-timing-admission-full-20261023.log` |
| Full BlackBar after boundary correction | PASS: 24,693 assertions | `executor-timing-admission-blackbar-20261023.log` |
| CombatPrepIntegration `CheckSupportForeignEdits` with current prerequisite assertion | FAIL: predicate returns true under denied support authority | `executor-timing-admission-combat-support-proof-20261023.log` |
| CombatPrepIntegration `CheckNativeAutoNukePause` with current prerequisite assertion | FAIL: predicate returns true under denied gear authority | `executor-timing-admission-combat-nuke-proof-20261023.log` |

The 11 passing suites in the full run were PresetIntegration, ManualAdventureOverlay, Ownership, TitanGold, PresetEquipment, GearEquip, OrdinaryPolicy, WinFormsBlood, StageTargetsNative, ResourceAggregation, and AutoExp. The full-run production MVID was `96aa789c-05d0-47b3-b99e-fc1bf0cc7004`; the subsequent passing full BlackBar MVID was `eed7bee5-8eed-4a06-9797-3cc37207a02d`.

At this earlier verification checkpoint, BlackBar was resolved by its full rerun, but CombatPrepIntegration remained blocked. The follow-up below closes that authority defect and records the newer shared-build blockers. No release artifact or complete 13-suite pass is claimed.

## Parent Handoff

The original handoff, before ownership was extended to `Main.CombatPrep.cs`, was:

- `Main.CombatPrep.cs:1346-1351`, `CombatPrepRequirementsVerified`, accepts cached `SetupVerified` plus matching material/native values without checking current setup claim authority.
- `Main.CombatPrep.cs:1327-1330`, `ApplyCombatPrepRequirements`, can return early through that predicate. Resource authorization also consumes the same predicate at `Main.CombatPrep.cs:828-832`.
- `checks/CombatPrepIntegration/PhaseSupportIntegration.cs:249` now directly asserts that the real current prerequisite predicate must reject the admitted foreign support claim. It returns true. The existing following assertions still require no setup/resource credit, no dependent writes, and unchanged start/deadline.
- `checks/CombatPrepIntegration/Program.cs:1700` directly reproduces the same predicate result for a foreign gear claim in `CheckNativeAutoNukePause`; existing native timer and saved-preference checks remain intact.
- The fixture's `BeginFixtureClaim` verifies that an unrelated grant is denied by the real execution coordinator before either reproduction. These are not obsolete legacy-lock assumptions. Actual unsafe native writes were not established by these failures; the confirmed observation is acceptance of denied current prerequisites.

Reproduce either isolated blocker with the script's `-Suite CombatPrepIntegration -Selector @('--check', 'CheckSupportForeignEdits')` or `-Selector @('--check', 'CheckNativeAutoNukePause')` options plus the game folder and a retained log path.

No commit, live game action, injection, or restart was performed by this task. Existing concurrent edits, including parent-owned telemetry/lifecycle changes, were preserved.

## Combat Authority Closure

Ownership was explicitly extended to `Main.CombatPrep.cs`; no other production file was edited in this follow-up.

- `CombatPrepSetupGrant` resolves a current gear/support grant bound to the same run and selected setup. It does not reuse a cached application grant or native equality as authority.
- `CombatPrepRequirementsVerified` now uses `VerifyExecutionPrerequisites`, validating that grant both before and after the existing phase/material/native observations. Existing consumers (setup reuse, OS, targets, funding, and adventure routing) therefore reject foreign gear/support claims even when native values still match.
- `AdvanceCombatPrep` clears cached setup/resource-phase proof when current setup authority is denied, before deadline classification, cadence throttling, or completed-phase transitions can consume it.
- Reapplication clears the old proof before attempting native setup, so a rejected retry cannot retain historical verification.
- Setup authority remains independent of deadline availability for the terminal result decision. This preserves valid completion at the original deadline while withholding completion under denied setup authority. Independent native nuke-timer pause and cleanup/return authority were not changed.
- `CheckScheduledClaimAuthority` adds four native cases: gear/support denial at an otherwise completed AT boundary, and gear/support denial at global expiry. Assertions cover no dependent writes/Wandoos/replan/AT completion, stable setup/phase/start/duration/deadline/entry, re-verification before admission after release, and cancellation without a replacement budget at expiry. Existing foreign-claim assertions were retained, not weakened.

### Follow-Up Verification

All runs again used the shared mutex through `VerifyExecutorOffline.ps1`; no `ArtifactDirectory` was passed. Logs remain under the approved temp directory listed above.

| Run | Result | Log Filename |
| --- | --- | --- |
| `CheckSupportForeignEdits` | PASS: 51 native assertions | `combat-prerequisite-support-fixed-20261023.log` |
| `CheckNativeAutoNukePause` | PASS: 138 native assertions | `combat-prerequisite-nuke-fixed-20261023.log` |
| `CheckScheduledClaimAuthority` | PASS: 97 native assertions | `combat-prerequisite-scheduled-fixed-20261023.log` |
| Full CombatPrepIntegration | PASS: 12,108 native assertions | `combat-prerequisite-combat-suite-20261023.log` |
| Full default 13-suite gate | 10 suites passed; two fixture builds failed; BlackBar had a new preset-migration failure | `combat-prerequisite-full-gate-20261023.log` |
| Affected-suite recheck | Production build failed before any native suite could run | `combat-prerequisite-affected-recheck-20261023.log` |

The passing full combat suite used production MVID `5668d073-4ef2-4f2f-8339-37b362b6193d`. It included 1,459 activity-timing assertions and 1,126 shared combat-setup assertions with zero failed groups. The later full gate used MVID `f309b6f9-9529-4a7b-87f4-260919c5cc96` and included unrelated concurrent resource-priority changes. These are distinct verification checkpoints, not a green combined release claim.

### New Parent Blockers

- The full gate encountered `CS1503` at `checks/PresetIntegration/DesiredMapExecutionChecks.cs:1290:57` (int-to-string argument mismatch), failing CombatPrepIntegration and ManualAdventureOverlay builds. That source changed again during the run; the current line is different, so the historical compile error was not changed by this task and needs a stabilized rebuild.
- BlackBar failed in `CheckNguSnapshot` at `checks/BlackBarChecks.cs:407`, constructing the real `CustomPlanStore`. Built-in migration reached `CustomLoadoutPreset.Parse` and threw `ResourcePrioritiesReviewed must be a boolean` at `CustomPlans/LoadoutPreset.cs:69`. Inspect the concurrent `PresetLibraryStore.cs:171-181` migration, which copies every priority field from a seed definition, including fields absent from some definitions. No parser/migration production edit was made here.
- The affected-suite recheck then failed production compilation on the concurrent save-test integration. Examples: `Loader.cs:62` cannot resolve `RecordSaveTestFailure`, `Loader.cs:146-147` cannot resolve `SaveTestActive`/`PumpSaveTestRecovery`, and `Main.cs:73` cannot resolve `InitializeSaveTestStartup`. The new `Main.SaveTests.cs` defines those members (`SaveTestActive` at line 25, recovery pump at line 62, startup at line 77), but `NGUInjector.csproj` did not include `Main.SaveTests.cs` or `SaveTestBundle.cs` when inspected. This is outside the granted production ownership.

The combat authority defect is closed by real negative tests and the full combat pass. A clean combined 13-suite gate remains pending stabilization of the unrelated shared worktree changes. No artifact was captured; final capture remains with the parent after telemetry integration.
