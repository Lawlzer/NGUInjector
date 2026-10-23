# Codebase Review: 2026-10-22

## Continuation Status

Status: COMPLETE for the approved offline review and implementation. Frontend,
Rust, source-linked checks, and all 13 native suites pass. Execution claims and
current-intent reconciliation are authoritative; historical snapshot restoration
was not reintroduced. Live Unity/soak validation was not performed. No commit was
made because the shared index contains extensive pre-existing work, not an
independently verified commit tree for this review.

Continuation scope: reconcile native/source-linked checks with the new contract;
validate retention of the original fixes; implement atomic settings revisions
and shared persistence fixtures; diagnose browser startup; and apply the narrow
legacy-loop, numerical-accounting, readiness, and checkpoint simplifications
where the current implementation still contains the reviewed duplication.

The shared index and unrelated changes remain untouched. No live game actions
or deployment are authorized. Verification and commit safety will be evaluated
against the resulting source tree rather than inferred from historical passes.

### Continuation Results

The execution-claim migration and review improvements are implemented and verified
offline. The earlier cutover blockers and results below are historical; the final
gate results in the next section supersede them.

- Settings now use exact-byte revisions from read through connected/offline
  compare-and-swap and committed lossless receipts. All 114 persistent setters
  were audited for failed writes, identical retries, and restart durability.
  Shared Rust/C# settings cases live in `checks/settings-contract.json`; see
  `settings-persistence.md` for the protocol and cooperating-writer guarantee.
- Current-intent native fixtures replace historical restoration requirements
  without fake grant/successor flags. Ownership, route, finite-action, support,
  ordinary-policy, and EXP checks execute the actual relevant boundary.
- Entry restart retires only the exact still-owned obsolete route. Entry fights
  use their persisted entry-specific opportunity instead of rebirth-wide dedup.
- Required beard identity participates in selection caches, and frozen/claim-owned
  support verifies native availability, capacity, active IDs/flags, and unlocks.
  All 19 prerequisite timelines now pass; full SustainedFocus passes 2,328
  assertions and ActivityPolicy integration passes 264.
- Projected resource budgets and equipment bonus aggregation have shared
  implementations, retaining their distinct native binary32 evaluation paths.
  Numeric/oracle checks pass 8,653 assertions and NumericIntegration passes 8,006.
- The unreachable legacy loop bodies are already removed in the current cutover.
  Readiness uses actual typed application/claim evidence and hard prerequisite
  exceptions rather than interpreting diagnostic strings as authorization.
- `TryCommitProtectedEntryChange` consolidates three entry-marker commit/rollback
  paths. Failed persistence restores only the captured scheduler/entry and never
  replaces a successor. Multi-state explicit overrides keep their own transaction.
- The existing explicit execution-review workflow is retained; review does not
  fabricate continuity, authorize unknown finite effects, or silently replay them.
- Browser startup uses a private Vite cache and real graph readiness. The CAS-aware
  browser fixtures now exercise required revisions and commit receipts. The final
  browser run passed 852 tests, four existing skips, zero failures/flaky results.
- AutoExp retains all 96 original cases plus 35 new controls: 131 cases / 64,294
  assertions across pure planner and real compiled purchase/ledger checks.

The independent adversarial pass found no confirmed P1/P2 issue in the scoped
current implementation. It explicitly required fresh CAS browser acceptance and
same-artifact native verification, rather than adding another ownership layer.

The first broad native run passed every runnable suite against MVID
`e74ddf9b-2c41-4aa8-bb0c-93b255564b83`; OrdinaryPolicy exposed five obsolete-fixture
compile errors. That fixture is now migrated and passes all 167 assertions.
The native AutoExp suite is included in the shared gate rather than left outside
the default release checks.

### Final Verification: 2026-10-23

The application source fingerprint matched before and after the final gates:
`91049dcaebdd348c0f48d6340d12bdd1f02cd5c2`. It covers tracked and nonignored
untracked production C#/project, frontend TypeScript/Svelte, and Rust source.
It is a source-consistency witness, not a commit or live Unity acceptance.

`verify-ui.ps1` completed successfully: Svelte/TypeScript zero errors/warnings;
2,082 frontend tests; production Vite build; Rust formatting, 179 tests and
compile check; and every configured source-linked check. Notable source results:
ExternalUi 9,824, PresetLibrary 4,749, source Combat 4,536, SustainedFocus 2,328,
explicit core 159, pure timing 1,981, CombatPrep planner 6,689, Presets 8,653,
ActivityPolicy integration 264, Support 31,048, Beards 224,405, numeric integration
8,006, ResourceWriter 1,553, FiniteActions 103, and route syntax/inventory 3,081
assertions. Pure EXP planner: 26 cases / 59,410 assertions.

The complete native verifier rebuilt production ONCE and ran every default suite
successfully against DLL MVID `e392ae00-e23a-448c-bd9c-8f17f75ca598`, under the
shared named mutex. This is retained same-artifact evidence, unlike earlier
per-suite passes on different builds.

| Full Native Suite | Passed Assertions |
| --- | ---: |
| PresetIntegration | 16,830 |
| CombatPrepIntegration | 12,011 |
| ManualAdventureOverlay | 297 |
| BlackBar | 24,603 |
| Ownership | 10,321 |
| TitanGold | 1,497 across 71 cases |
| PresetEquipment | 94 |
| GearEquip | 20,805 |
| OrdinaryPolicy | 167 |
| WinFormsBlood | 174 |
| StageTargetsNative | 48 |
| ResourceAggregation | 4,646 |
| AutoExp | 4,884 across 105 cases |

Assertion counts overlap and are not an additive independent-coverage total.
Existing fixture-only unused-field warnings remain; no check failure remains.

The current CAS-aware browser suite passed 852 tests on desktop/mobile, with
four existing skips, zero failures and zero flaky results. Existing assertions,
timeouts, and retry settings were not weakened. Native IPC mocks now implement
the actual settings JSON/revision/commit-receipt contract instead of the removed
object-only transport.

Commands, from the appropriate repository/UI directory:

```powershell
.\verify-ui.ps1
npm run test:ui -- --workers=8 --reporter=dot,json --global-timeout=660000
.\checks\VerifyExecutorOffline.ps1 -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" -LogPath "<unique approved temporary log path>"
```

Retained artifacts under `C:\Users\Kevin\AppData\Local\Temp\opencode`:

- `ngu-review-complete-aed84285e540427ba933667c4173d820.log`: final production/native
  builds, MVIDs, all full suite outputs, and native EXP results. The verifier
  returned success after all 13 suites; this pathname was not reused.
- `ngui-cas-final-full-1525-6ccb55e52b284f249918411d0ca3ece0.json`: browser report;
  its stats record expected 852, skipped 4, unexpected 0, flaky 0.

Final diff/usage review and `git diff --check` passed. The independent adversarial
review found no confirmed P1/P2 defect in its inspected current scope. Confidence
is high for these offline regressions, not proof of a defect-free application.
CAS covers cooperating writers, not arbitrary editors ignoring ownership locks;
plan and settings files are not one multi-file transaction. Unknown finite
effects remain non-replayable. Live rendering, timing, reward delivery, full
Walderp/tower permutations and long-running restart/soak behavior remain outside
this task's verified boundary.

### Historical Cutover

During the earlier restore-caller audit, concurrent work replaced snapshot-based
titan and harvest ownership with execution claims. At that point,
`Main.Execution.cs:514` explicitly states that release resolves the current
successor and records no original gear. `Main.TitanAutomation.cs:308` now uses
`EndRouteProcess`; `Managers/YggdrasilManager.cs:14` delegates harvesting to the
new action coordinator. Reintroducing historical restoration would contradict
that new contract.

The additional legacy titan/harvest restoration tests reproduced failures before
that replacement. Their focused fixes passed against pre-refactor DLL MVID
`c55d3cf7-ec10-4cdf-9a4b-c166584340f4` (162 titan and 102 harvest assertions;
full TitanGold 88 cases / 1,329 assertions), but full Ownership failed at
CheckStageReceipts with an inline-setup NullReferenceException. The then-current
source-linked Support build also failed because its fixture lacked
SelectedSetupRequirements and LoadoutManager.SnapshotEpoch. Those legacy
helpers/tests were subsequently migrated; their old passes were never used as
acceptance evidence for the replacement contract. Conflicting work was preserved
rather than reverted.

Another process staged the shared worktree during this phase, including this
record. This review did not stage, unstage, or commit those changes. The approved
active ownership contract is the execution-claim model. Source consistency was
confirmed during final verification.

## Scope And Status

Reviewed the current working tree, not only HEAD: frontend state/transport,
Rust/C# persistence and command boundaries, stage scheduling/rebirth/lifecycle,
combat/equipment/support ownership, and numerical resource/EXP accounting.
The priorities were lost edits, unintended actions, unearned stage credit,
unrestored gear, and purchases without their paid gain. Optional architecture
changes were kept separate from demonstrated bugs.

Sixteen original bug classes were reproduced and fixed in the reviewed snapshots.
The continuation validates their safety requirements under the replacement
architecture, rather than retaining superseded restoration APIs. Additional
prerequisite, route-retirement, entry-fight and CAS defects were resolved. This is
a risk-based review, not proof that every path is defect-free. No live game
actions, injection, save loading, or deployment were performed. The offline gate
is complete; live validation remains outside the verified boundary.

The repository already contained extensive modified and untracked application,
test, and documentation files. Other work continued during the review. Those
changes were preserved; this review staged no files and created no commit.

## Historical Evidence

Each fixed behavior below was supported by implementation tracing and failing
regression output before its fix. Passing controls exercise the behavior that
must remain valid. Native tests use isolated data-only game fixtures; their
rendering, time, and other unavailable engine boundaries are explicitly replaced.

| Fixed Behavior | Implementation | Regression Evidence |
| --- | --- | --- |
| Confirmation/preflight cannot authorize a destructive command in a replacement folder or injector session | `NGUInjector.UI/src/lib/controller.ts`, `runCommand` | Deferred confirmation and preflight cases assert zero enqueues after context changes |
| Bulk settings saves detect backend changes even before polling observes them, retaining pending edits | `controller.ts`, `saveSettings` | Fresh-read conflict, context-change, read-failure, captured-baseline, and newer-draft cases |
| Native large double settings remain numbers instead of becoming rejected lossless tokens during cloning | `controller.ts`, `cloneSettings` | Controller-through-real-API tests preserve nested native numbers and still reject genuine unsafe lossless tokens |
| Failed persisted native setters leave live state unchanged and allow an identical retry | `NGUInjector/SavedSettings.cs` | Five-property/two-direction failure and retry matrix for AutoRebirth, UseCustomRebirthPlan, CustomPlanFile, GlobalEnabled, and CombatEnabled |
| Sparse historical presets accepted by C# can be read/saved/copied offline without changing provenance | `NGUInjector.UI/src-tauri/src/main.rs`, `validate_loadout_presets` | Five Rust tests preserve historical source shape, exact unknown tokens, protected fields, and rejection of explicitly malformed values |
| Closing automatic rebirth permission/readiness retires its preparation rather than permanently interrupting stages | `NGUInjector/Main.CustomPrep.cs`, `Main.AutomationLoop.cs` | Actual callback checks cover permission, structural obligation, and trigger changes without rebirth |
| Explicit inline gear/support edits replace setup intent while preserving stage history and entry identity | `NGUInjector/Main.InlineStageSetup.cs` | Real store edit/reload tests cover changed gear, support-only and cosmetic edits, positive earned work, and foreign-conflict preservation |
| NUMBER confirmation advances during automatic-stage execution and prevents another cycle after obligations finish | `Main.CustomPrep.cs`, `Main.AutomationLoop.cs` | Both callbacks tested during ownership waits and an active automatic executor with funded native resource lanes; target loss/spike and reconfirmation covered |
| Missing, cancelled, unreadable, or invalid QuickLoad does not discard stage progress | `NGUInjector/Main.cs`, `QuickLoad` | Seven rejected/cancelled cases preserve authority; accepted native load invalidates before dispatch, including mutation-then-throw |
| Successful manual fight-prep takeover retires old rebirth intent without stopping its replacement push | `Main.CustomPrep.cs`, `RunManualFightPrep` | Running/completed automatic and manual-rebirth handoffs; rejected loadout/digger/plan-load attempts retain previous intent |
| Rejected gold-gear restoration retains recovery responsibility instead of releasing the lease | `NGUInjector/Main.CustomAdventure.cs`, `Managers/LoadoutManager.cs` | Native full-inventory/empty-slot failure followed by successful recovery after space is available |
| Partial/throwing gold acquisition cannot fall through to ordinary farming | `Main.CustomAdventure.cs`, `Managers/CombatManager.cs` | Partial acquisition/restoration and exception cases assert exact snapshot, lease, gear, and blocked routing |
| Disabling Adventure services an active gold detour's cleanup | `Main.CustomAdventure.cs`, `Main.CombatRouting.cs`, `Main.Lifecycle.cs` | Disable during healing/combat, repeated ticks, and pending-cleanup unload cases |
| Manual max-ITOPOD does not fight without verified support | `NGUInjector/Main.MaxItopodPush.cs` | Entry and active-tower failures withdraw to Safe Zone; recovery preserves death budget |
| Epoch cleanup retires the matching stale gold owner before its snapshot token disappears | `Main.CustomAdventure.cs`, `Managers/LoadoutManager.cs` | Real native mutation plus failed observation produces UnknownOutcome; epoch/token/successor cases verify no orphan lease or foreign cleanup |
| Power purchases crossing float's exact-integer boundary do not pay for lost gains | `NGUInjector/Main.CustomExp.cs` | 22 energy/magic cases below/at/above 2^24, exact and rounded gains, paid costs, retained EXP, and input restoration |

Two initial persistence findings were already corrected in the latest shared
worktree when implementation began: offline protection of historical provenance
and new-plan auto-rebirth defaults. Those existing fixes were preserved and
verified, not counted as newly implemented fixes.

## Historical Verification

### Frontend And Source-Linked Checks

The combined invocation was:

```powershell
.\verify-ui.ps1 -IncludeInjector -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
```

It completed the following before its later timeout:

| Check | Result |
| --- | --- |
| Svelte/TypeScript | Zero errors or warnings |
| Frontend unit tests | 2,052 tests passed across 29 files |
| Vite production build | Passed |
| Rust formatting, tests, compile check | Passed; 170 tests |
| ExternalUi | 9,817 assertions passed |
| PresetLibrary/global NGU | 4,749 assertions passed |
| Combat | 10,538 assertions passed |
| SustainedFocus | 2,638 assertions passed |
| Pure stage timing | 66 scenarios / 1,981 assertions passed |
| CombatPrep planner | 6,689 assertions passed |
| Preset numeric checks | 3,017 assertions passed |
| ActivityPolicy, SharedSupport, Support, Beard availability and Beards | Passed |
| Production .NET Framework Release build | Passed |
| BlackBar compiled integration | 24,388 assertions passed, including stage support and protected timing |

Additional direct invocations passed:

```powershell
dotnet run --project checks/ResourceWriter/ResourceWriterChecks.csproj
dotnet run --project checks/AutoExp/AutoExpChecks.csproj
dotnet run --project checks/PresetIntegration/NumericIntegrationChecks.csproj -c Release
```

Results: ResourceWriter 1,553 assertions; AutoExp 96/96 cases and 63,703
assertions; separate numeric integration 2,370 assertions. Assertion totals from
overlapping runners must not be added as independent coverage.

### Fresh Native Gates

A subsequent isolated production Release build supplied one DLL to the following
checks. MVID: `abb0be5a-e43f-414f-bde0-189a64cff236`.
The native source/test content fingerprint was unchanged over that verification
window: `eb6838c27d995df25ea71246a306eae28eda4734`.

| Check | Result |
| --- | --- |
| Full Ownership | 12,285 assertions passed |
| Focused gold recovery | 1,995 assertions passed; included in Ownership |
| Full GearEquip | 20,805 compiled gear assertions passed; 974 resource-preserving actuator assertions also reported |
| Full TitanGold | 70 cases / 1,167 assertions passed |
| PresetIntegration inline-stage setup | 462 assertions passed |
| PresetIntegration stage-policy edits | 183 assertions passed |
| PresetIntegration active NUMBER observation | 112 assertions passed |
| Preset EquipmentChecks | 94 assertions passed |
| CombatPrepIntegration CheckSharedCombatSetup | 1,129 assertions passed |

Following the offline persistence-fixture repair described below,
PresetIntegration settings-JSON (117), manual-overlay (276), adventure-dynamic
(399), adventure-goal-preference (18), and capacity-objectives (181) checks passed
against that same production DLL. ExternalUi and PresetLibrary also passed again.

Callers were searched before private/internal signature changes. Targeted tests
were run red before green, followed by broader checks. `git diff --check` passed.
There is no dedicated frontend lint script; available Svelte and Rust checks were
used rather than claiming an unavailable lint gate.

### Incomplete Gates

- The combined wrapper exceeded its 900-second limit during full
  CombatPrepIntegration after `CheckAdventureWeightPartialRouteFailure` passed.
  Its final shared-setup check passed separately. The entire wrapper, subsequent
  full/default native runners, and the full CombatPrepIntegration run cannot be
  reported as green.
- An early browser run reported 817 passes, 13 failures, and 4 skips while sources
  were changing. Its mismatched UI-copy expectations were not reliable evidence
  of 13 product bugs.
- A fresh-server browser run on port 1487 reported 824 passes, 8 failures, and 4
  skips across desktop/mobile. Failures were initial desktop page/module request
  timeouts in adventure-rules and autosave-editors. A focused two-worker retry on
  port 1488 with 60-second test limits reproduced startup failures and hit its
  240-second command limit. Root cause remains unestablished; this is not a clean
  browser pass, and no production workaround was added without evidence.
- Full ManualAdventureOverlay initially failed because transactional setters now
  reach Unity JsonUtility directly, outside the fixture's old SaveSettings
  substitution. The offline adventure fixture now explicitly disables settings
  persistence; separate transactional persistence checks remain enabled. The
  shared externally added JSON-boundary fixture work was preserved. A full
  overlay run passed 12 groups / 297 assertions, but repeats exposed intermittent
  selected-loadout observer failures in `checks/ManualAdventureOverlay/Program.cs`
  (the gear-request/weight assertions around lines 197 and 226). The latest repeat
  had one failed group. Assertions were not weakened; consistent green status is
  blocked pending diagnosis.
- Source content changed during the broad combined checks. The later native
  window was stable, but there is no single frozen commit tree covered by every
  reported check.

## Historical Decisions

- Frontend fresh-read conflict checking is not backend atomic compare-and-swap.
  An external settings write between read and write can still be overwritten.
  Plan/library revisions already provide stronger protection; adding settings
  CAS is a separate cross-language protocol decision, not a claimed fix here.
- Gold cleanup restores only matching token/epoch and bounded owned actuator
  outcomes. UnknownOutcome remains blocked even if observation later recovers;
  replay or inferred restoration would risk overwriting a foreign/manual action.
  There is no supported safe in-place acknowledgement command today. The user
  must verify/correct equipment manually; a genuine save/rebirth epoch change or
  full game restart retires old authority but does not certify gear restoration.
- Manual fight prep and manual rebirth are not interchangeable with automatic
  preparation. The fixes preserve explicit manual authority, failed-takeover
  intent, mandatory stage obligations, durable rebirth gates, and earned history.
- QuickLoad invalidates immediately before native loading, not after success:
  native loading may mutate and then throw. Validation/cancellation has no such
  authority to discard the current scheduler.
- Live Unity rendering, scheduling, and reward delivery remain unverified.
  Confidence is high for reproduced targeted regressions and moderate for broad
  release readiness because the incomplete gates above remain open.

## Improvement Disposition

1. Implemented shared Rust/C# settings fixtures for validation, defaults, protected
   history and exact tokens; retained sparse historical-preset/provenance tests.
2. Implemented revision-checked settings reads/writes and committed lossless
   receipts across frontend, Rust, connected commands and C# native setters.
3. The current cutover already removed both unreachable legacy loop bodies.
   Retained profile calculations were not removed merely because execution moved.
4. Implemented shared resource-budget and equipment-bonus calculations, with
   differential/native oracles preserving distinct binary32 evaluation paths.
5. Implemented one same-entry checkpoint mutation/commit/rollback helper for the
   three equivalent marker transitions. Explicit multi-state overrides retain
   their specific transaction rather than an unnecessary generic framework.
6. Current readiness is based on typed selected/application evidence and actual
   native prerequisites. Diagnostic strings do not grant support authority. Hard
   beard prerequisites are verified in cached, frozen and claim-owned paths.
7. Retained the cutover's explicit preview/approve execution-review workflow.
   Review is scoped to observed setup/entry evidence; it never permits replay of
   unknown irreversible finite effects or manufactures historical continuity.

## Commit Status

No commit was made by this review. `git status`, unstaged/staged diffs, and the
last ten commits were inspected. The index was initially empty; concurrent work
later staged extensive changes, which were left untouched. Fixes depend on
pre-existing work and new execution-claim files outside the staged snapshot.
The complete working tree is offline-verified, but there is no independently
verified staged commit tree containing only this review's authorized deltas.

Implementation and offline verification are complete. The remaining commit
decision is how to assemble the existing shared staged/untracked work without
silently including unrelated changes. Live acceptance, if desired separately,
must not be inferred from offline fixture passes.
