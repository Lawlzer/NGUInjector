# Native Fixture Integration

## Verified Outcome

Updated 2026-10-23. Each assigned full native suite has a retained passing result.
The table below supersedes the historical failures later in this report. These
passes are offline fixture evidence, not release or live-game acceptance.

Logs are under `C:\Users\Kevin\AppData\Local\Temp\opencode`.

| Gate | Retained result | Production MVID | Log |
| --- | --- | --- | --- |
| Full PresetIntegration | PASS 16,411 assertions | `9c8b9350-23de-46a8-b202-04a92ef0a829` | `executor-native-acceptance-final.log`, line 161 |
| Full CombatPrepIntegration | PASS 12,011 assertions | `9c8b9350-23de-46a8-b202-04a92ef0a829` | `executor-native-acceptance-final.log`, line 192 |
| Full ManualAdventureOverlay | PASS 297 assertions, zero failed groups | `9c8b9350-23de-46a8-b202-04a92ef0a829` | `executor-native-acceptance-final.log`, line 211 |
| Full BlackBar | PASS 24,603 assertions | `031156cb-2b38-47d4-84e4-3115b9e65aa1` | `executor-native-blackbar-current-full.log`, line 38 |
| Finite-action selector | PASS 217 assertions, including 40 preparation-identity assertions | `cdf5046f-9175-4d8e-bd0b-29d22bd3fc1f` | `executor-native-finite-current-final.log`, lines 12-14 |

The acceptance log's BlackBar run FAILED before the final fixture fixes; its
23,735 completed-group assertions are NOT a pass count. The later standalone
BlackBar log establishes its full pass on the different MVID shown above. Its
groups include StageSupport 409, scheduling 305, protected timing 2,853 and Iron
Pill 459. The expected UnknownOutcome diagnostic belongs to the deliberately
rejected-pill negative case and does not invalidate that full passing result.

### Combined Run Evidence

An earlier combined run was reported passing all four assigned suites on MVID
`e928aa81-8ece-4dfe-b950-724613cabc50`, starting at
`2026-10-23T00:54:38.5456648-05:00`. A previously queued verifier subsequently
acquired the mutex and overwrote its `executor-native-all-green-final.log`.
That pathname no longer provides retained evidence for the earlier artifact.

The replacement log starts at `2026-10-23T01:07:42.2708337-05:00`, identifies MVID
`797b7640-54a6-41a8-965a-9caf9dfe2dbf`, and ends at line 152 after execution
persistence's 347 assertions, before the core-group/full-Preset completion.
The verifier and fixture processes have exited. No full-suite PASS, aggregate
completion or exit status was retained for this replacement run: INCONCLUSIVE,
not a pass or an assertion-failure diagnosis. No further verifier was launched
while finalizing this report. Do not combine either MVID with the other run's
results or use the per-suite table as a same-artifact combined acceptance claim.

## Fixture Changes

All production/native builds and EXE runs use `checks/VerifyExecutorOffline.ps1`
and the same named mutex. No production source, index/staging, commit or live
state was edited by this fixture stream.

- NativeFightFixture.cs supplies actual native model/controller/table inputs,
  including hard-required Fu, real gear, digger costs and fruit quirks. Positive
  manual preparation executes the REAL command-file admission with an actual
  exclusive injector lease, durable accepted request, scoped original opportunity,
  real selected setup/process/action grant and finite-action ledger. It does not
  set a readiness/grant-success Boolean. Missing requests and unavailable Fu still
  reject. StageSupport now PASS 409 assertions in full BlackBar.
- BlackBar positive fixed/loop/terminal fights now have concrete prerequisite data
  instead of sparse unavailable models. QuickLoad now has its native W bootup
  clock. Its old epoch-reset positive case uses actual explicit checkpoint review
  and restart, not a reset-to-new-budget shortcut. Eighteen fail-closed checkpoint
  and two reset preservation timelines remain enabled.
- Iron Pill thresholds are acquired BEFORE opening the real durable saving window;
  a new regression preserves its original bytes despite later threshold edits.
  Unknown native NUMBER invocation is never repeated when a scene reference is
  repaired or the backoff expires. All independent Iron Pill groups now execute
  and aggregate failures. Short-plan timing starts at its intended clock BEFORE
  checkpoint acquisition. Fight preemption uses real identified preparation.
- Finite CURRENT B now advances via the actual saved-plan revision/entry-bound
  OverrideCustomStage command, checks the durable B row and entry, and verifies
  actual B after temporary claim release. The action owner's new preparation
  commitment partial is linked into all three shared native manifests and called
  from the finite selector. Current finite selector PASS 217 = 177 + 40.
- The common native inventory fixture initializes MacGuffins and the real combat
  manager normally initialized by Main.Start. Its new identified
  boost leaves dispatch to the preserved REAL native boost controller bodies,
  rather than the data-only gear-swap boundary. Their unavailable inventory refresh
  recomputes the existing actual-equipped bonus cache even after native errors.
  Typed native committed-error evidence is observed without changing it. The
  complete auto-boost selector now PASS 203 assertions, including real consumption,
  original AT/deadline preservation and error continuation.
- Resource admission now tests actual quiescence/domain revision revocation instead
  of obsolete feature-lock authority: PASS 87. Preset resource coordination uses
  actual temporary claims, current pool conservation, scoped native-target service,
  typed model/render outcomes, retained acquisitions and real explicit restarts.
  Rendering retries cannot masquerade as repeated accounting: exact native model
  writes remain asserted. Unapproved material still grants NO credit. PASS 1,991.
- A late full-Preset failure was duplicate fixture recipe IDs when the route group
  runs twice. The adventure factory now explicitly replaces only its OWN existing
  fixture recipe by ID, using the real revision-bound library save. Other recipes,
  persistence and cross-plan references remain intact. Isolating only the library
  while sharing plan files was tested and correctly rejected by reference integrity;
  that attempted fixture isolation was replaced, not production validation weakened.
- VerifyExecutorOffline.ps1 now accepts -LogPath and records native stdout/stderr,
  builds, MVIDs and assertion results while holding the mutex. Transcript-only
  logging did not capture native output, so actual native output is tee'd instead.

## Resolved Findings

Production fixes cited below were made by their owning streams; fixture fixes
stayed in this stream's files. Original negative, persistence and native-effect
assertions remain enabled.

1. Iron Pill exactness: the action owner added an explicit binary32 storage-rounding
   boundary in PillBloodAction.Classify. The real stored expected/actual values
   match exactly: `40400000,40400000,41100000,3db851eb`. No ULP tolerance or invented
   fixture stats were used. Full BlackBar now passes all 459 Iron Pill assertions.
2. Explicit manual preparation takeover: the action owner's handoff now accepts
   the real explicit replacement while preserving original opportunities and
   prerequisites. No local ledger deletion or fabricated ownership was added.
   The scheduling group passes all 305 assertions.
3. Weighted farming: the core owner changed the route admission proof to consume
   CombatPrep's actual coupled phase/resource authority rather than require an
   absent normal `_executionBaselineResourceIntent`. Ordinary preset admission
   still requires its real baseline. All 16 weighted groups pass 685 assertions;
   full CombatPrep passes 12,011. See `executor-core-cutover-20261022.md` for the
   separate callback/foreign-authority proof matrix.
4. Missing command result: the exercised JSON publication failure came from the
   StageSupport fixture leaving the static automation status null. The fixture now
   restores the production default through `SetCustomAutomationStatus(null)`.
   Iron Pill preemption also supplies actual Energy cap/current of `123456L` to
   match its idle pool. Identified command admission, accepted result and durable
   ACK remain required. The earlier no-process/fallback-acquisition explanation
   was speculation, not a separately reproduced current production defect.
5. Hard Fu prerequisite: the coordinator's source fix passes full SustainedFocus
   2,328 assertions and the explicit execution-core selector 159 assertions,
   including all 19 hard-support timelines / 104 assertions. Missing Fu still
   rejects; native fixtures use Fu Manchu ID 0 with real prerequisite inputs.
6. Overlay restart and automatic entry fight: the owning core/route changes pass
   the original real explicit-restart and new-entry timelines in the full Preset
   and CombatPrep runs. The CURRENT-B fixture now acquires B through the actual
   saved-plan entry-bound command rather than publishing a bare setup.
7. Fixture isolation: duplicate dynamic recipe IDs are replaced only for the
   fixture's own ID through real CAS save; unrelated recipes and cross-plan
   references remain intact. Native retirement initializes the real combat manager
   and MacGuffins rather than hiding missing lifecycle prerequisites.

Earlier focused evidence, retained for traceability rather than current full-suite
acceptance:

| Gate | Actual result | Production MVID |
| --- | --- | --- |
| Native StageSupport | PASS 391 | `96e9d743-eb37-43b9-b50a-7cfb1617cbb7` |
| Full native auto-boost selector | PASS 203 | `204a0dda-ce8e-45c3-ab3c-cc5461bb6c5d` |
| Native resource admission | PASS 87 | `ffd25418-3c23-4555-bc74-a7c9298b85ba` |
| Native preset coordination | PASS 1,991 | `f3fa4402-772e-4502-96b3-ddd09c77c384` |
| Native epoch/restart retry ownership | PASS 91 | `ee43588b-161c-4cfa-9bff-5ae8dcc04f03` |
| Overlay full | PASS 297 | `14c76714-fbc3-4591-b9bb-563f3cfa0680` |
| Full Preset before late duplicate-ID isolation | FAIL after 15,531; all five diagnostic selectors pass, including core 1,424, routes 719, finite 217 and persistence 347 | `c39e26d9-8d0d-4c4e-9e15-d452a919abc4` |
| Full CombatPrep before final weighted-route diagnosis | FAIL after 10,683; resource, auto-boost, activity timing 1,459 and interruption 175 passed before six farming-route groups | `c39e26d9-8d0d-4c4e-9e15-d452a919abc4` |

## Scope

Assigned fixture migration has full per-suite passing evidence, with the combined
log limitation described above. This is not release acceptance. No production
ownership/actuator code, parent external verifier, staging, commits, deployment or live game actions were
changed/performed by this fixture stream. Existing staged/unstaged changes were
preserved. Parallel owners continued changing production and their own fixtures;
the recorded MVIDs identify actual observations rather than one immutable release.

## Implemented

- PresetIntegration's OWN LoadGame and LoadPlugin now invoke the finite-action
  presentation/RNG hooks before serializing their in-memory assemblies. The
  native NUMBER render-failure assertion now passes; native accounting and real
  grants are not substituted.
- CombatPrep positive exact-support primitives call the existing
  InvokeGrantedSupportActuator helper. Three explicit raw-negative assertions
  prove missing authority rejects even matching support, preserves native values,
  and reaches no native support callback.
- NativeSettingsJsonBoundary.cs extracts the existing Unity field codec for shared
  use. Only Unity JSON calls are replaced; real validation, CAS, atomic writes,
  publication, reload and unknown-field preservation remain exercised. It is
  linked into the owned Preset/CombatPrep/Overlay/BlackBar manifests. The common
  CombatPrep loader exposes the OPTIONAL ConfigureSettingsJsonFixture hook so
  other linked fixtures do not gain a compile dependency on PresetIntegration.
- Each fresh Preset Character now uses its existing unique checkpoint directory
  for settings files too. Reusing one settings filename with a NEW settings
  object's empty loaded revision caused genuine CAS conflicts between cases.
  Deliberate reloads continue using the SAME directory/file.
- The settings IO-failure check locks atomic replacement of the SAME settings
  file after valid CAS admission, rather than redirecting to a different file.
  The reload check uses a fresh real settings instance, not a mutation of a
  private cache field that the newer CAS implementation no longer consults.
- Overlay interference uses admitted Gear/Support claims instead of obsolete
  whole-feature lock tokens. Callback revocation changes the ACTUAL manual
  support intent, not unrelated Magic management permission. All original
  resource/gear/support/current-successor assertions remain.
- Removed all fixture references to _protectedSkipRestore. BlackBar's two reset
  timelines now preserve the original checkpoint, entry, deadline and credit.
  All 18 counter/rollback/origin/corrupt/ghost-history cases reject admission,
  preserve exact bytes, reject forced saves, and exercise explicit reviewed
  restart with exact archived evidence, zero new credit and spent-token rejection.
  Positive restore and actual applied-credit scenarios remain in the full run.
- Removed obsolete beard-completion fixture assignments; unavailable native
  setup must reject BEFORE resource writes. The rebirth IL wiring check follows
  selected final setup and the real synchronous multiplier-refresh adapter rather
  than requiring removed RotateBeards writers.
- CombatPrep's unload fault matrix now tests zero resource-release authority,
  actual W evidence preservation, inert retained callbacks, and no paid-AT replay
  after reinjection. Running-state pre/post-release checkpoint faults are retained.
  Unload checkpoint faults cover both before quiesce and after bounded retirement.
  The cleanup-recovery test verifies revoked lifecycle authority and actual inert
  old-coordinator calls, not a diagnostic CleanupPending Boolean as authority.
- The scheduled-overlay fresh-entry fixture uses the existing real explicit
  restart helper, not ResetProtectedStageProgress as a new-budget shortcut.
- BlackBar executes every top-level group and aggregates failures instead of
  letting one obsolete fixture prevent the remaining full checks. Failure counts
  are explicitly NOT called pass counts. Native fixture PDBs provide exact lines.

## Gate

`checks/VerifyExecutorOffline.ps1` shares EXACTLY the parent's named mutex:
`Local\NGUInjector-Executor-Offline-Verification`. It holds it across the normal
production Release Rebuild, all fixture rebuilds and every EXE invocation. It
waits up to 30 minutes, handles abandoned mutex ownership and always releases it.
The parent `A:\misc\NGU-Automation\temp\verify-executor-offline.ps1` is unchanged.

`verify-ui.ps1 -IncludeInjector` delegates ALL its production/native work to this
repo verifier. The default verifier retains every previous native suite and adds
TitanGold, WinFormsBlood, native StageTargets and Framework ResourceAggregation.
The source gate retains its full suites and adds the explicit SustainedFocus
execution-core selector, FiniteActions, ResourceWriter, NumericIntegration,
AutoExp and RouteExecutorSyntax. ResourceAggregation also runs inside both full
source Presets runners; its standalone Framework project requires MSBuild/EXE,
not `dotnet run`.

If full Preset fails, the SAME built fixture/DLL still executes all five cutover
selectors for diagnostics. Their passes NEVER replace the full failure. Suite
failures aggregate and fail the gate after the other suites run. Supplying multiple
matching Preset flags in one EXE is rejected rather than silently running only
the first. The two-argument CombatPrep `--check CheckName` form remains supported.

BackendIncludes.targets was deleted after auditing project/script imports: no
actual reference remained. Historical documentation mentions are not imports.

## Commands

The retained evidence and final combined attempt used the following repo-root
commands. Each invocation rebuilds production while holding the shared mutex;
results must retain their own MVID. The combined attempt's current log is
inconclusive as described above, despite the filename.

```powershell
$game = 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
$logs = 'C:\Users\Kevin\AppData\Local\Temp\opencode'
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration,CombatPrepIntegration,ManualAdventureOverlay,BlackBar -GameFolder $game -LogPath "$logs\executor-native-acceptance-final.log"
& .\checks\VerifyExecutorOffline.ps1 -Suite BlackBar -GameFolder $game -LogPath "$logs\executor-native-blackbar-current-full.log"
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration -Selector --finite-action-cutover -GameFolder $game -LogPath "$logs\executor-native-finite-current-final.log"
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration,CombatPrepIntegration,ManualAdventureOverlay,BlackBar -GameFolder $game -LogPath "$logs\executor-native-all-green-final.log"
dotnet run --project checks/SustainedFocus/SustainedFocusChecks.csproj -c Release
dotnet run --project checks/SustainedFocus/SustainedFocusChecks.csproj -c Release -- --execution-core
```

Earlier diagnostic commands also ran from the repo root with a 1,200,000 ms tool
timeout. These are historical commands, not additional final acceptance results.

```powershell
$game = 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
& .\checks\VerifyExecutorOffline.ps1 -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration,CombatPrepIntegration,ManualAdventureOverlay,BlackBar,ResourceAggregation -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration -Selector --manual-overlay -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite PresetIntegration -Selector --settings-json -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite CombatPrepIntegration -Selector @('--check','CheckCleanupRecoveryRegressions') -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite CombatPrepIntegration -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite BlackBar -GameFolder $game
& .\checks\VerifyExecutorOffline.ps1 -Suite BlackBar -Selector stage-support -GameFolder $game
```

Initial reproductions used the unchanged parent verifier with
`-Suite PresetIntegration -Selector --finite-action-cutover` and
`-Suite CombatPrepIntegration`. Both failed for the reported fixture boundaries.
No unlocked production build or game-native fixture EXE was invoked.

## Historical Results

The following pre-fix results are superseded by the verified per-suite results at
the top. They document the original failure progression, not current blockers.

| Run | Result | Production MVID |
| --- | --- | --- |
| Combined full assigned suites | Full Preset FAIL after 9,143 assertions; full CombatPrep FAIL after 7,009; Overlay PASS 297; BlackBar FAIL with 20,882 completed-group assertions; ResourceAggregation PASS 4,646 | `4532768f-f5ca-40ee-a2e1-e419e612a0c3` |
| Same-DLL diagnostic foundation | PASS 42 | `4532768f-f5ca-40ee-a2e1-e419e612a0c3` |
| Same-DLL execution core + full commit budget | PASS 778 = 150 + 628, 20 budget cases | `4532768f-f5ca-40ee-a2e1-e419e612a0c3` |
| Same-DLL route executor | PASS 430 | `4532768f-f5ca-40ee-a2e1-e419e612a0c3` |
| Same-DLL finite actions | FAIL after 14 assertions, CURRENT B expected 551, actual 550 | `4532768f-f5ca-40ee-a2e1-e419e612a0c3` |
| Same-DLL execution persistence | PASS 147 | `4532768f-f5ca-40ee-a2e1-e419e612a0c3` |
| Focused cleanup recovery after migration | PASS 155 | `e38b29af-525c-430a-9e77-7a759da5e253` |
| Focused settings JSON + transactional persistence/global presets | PASS 118 | `addddc0c-63d0-4b83-9beb-4aacb0f083c1` |
| Earlier focused manual overlay after explicit restart migration | FAIL after 263 assertions, before the owner fixes described above | `17e90535-82fe-48ca-a861-c10e71d6663e` |
| Earlier full CombatPrep | FAIL after 7,263 assertions at automatic entry fight dispatch | `af463706-1994-4fc7-8383-f6f0cc196365` |
| Earlier full BlackBar | FAIL: StageSupport 337 assertions/2 failed groups; protected timing 2,555 assertions/4 failed groups (including 12 scheduling failures); Iron Pill failure; 20,882 completed-group assertions | `5f413b47-ef17-4333-b177-8203d6a29fc3` |
| Earlier focused StageSupport after strengthening missing-call IL guards | FAIL after 337 assertions, same two fight-prep groups; all other groups including final-multiplier guards execute | `8c05024c-42a7-48c8-a809-462667ee44c5` |
| Earlier complete all-native registry | TitanGold PASS 65 cases/1,201; equipment PASS 94; native StageTargets PASS 48; all other suite failures retained | `7f4639e7-573d-4a1c-8349-b89b0d331b8d` |

The earlier combined run took 87.31s for full
Preset, 65.65s for core including deliberate deadline tests, 87.14s for full
CombatPrep, 40.36s for Overlay and 33.14s for BlackBar. An earlier standalone full
CombatPrep reached its next failure in 97.79s. These historical timings do not
explain the final inconclusive run or establish live game performance.

## Source Evidence

Source commands actually run with `dotnet run --project <path> -c Release`.
SustainedFocus rows include the later hard-Fu fix; the other rows retain earlier
source-only observations and are not a new combined source gate.

| Project / selector | Actual result |
| --- | --- |
| FiniteActions/FiniteActionsChecks.csproj | PASS 28, explicitly source-unit authority boundary |
| ResourceWriter/ResourceWriterChecks.csproj | PASS 1,553 |
| RouteExecutorSyntax/RouteExecutorSyntaxChecks.csproj `-- NGUInjector` | PASS 2,548 syntax/writer-inventory assertions, not native acceptance |
| PresetIntegration/NumericIntegrationChecks.csproj | PASS 8,006 |
| Presets/PresetsChecks.csproj | PASS 8,653 |
| ProtectedTiming/ProtectedTimingChecks.csproj | PASS 66 scenarios/1,981 assertions |
| SustainedFocus full | PASS 2,328 after the coordinator's hard-Fu fix |
| SustainedFocus `-- --execution-core` | PASS 159, including 19 hard-support timelines / 104 assertions |

Both verification scripts parse successfully. `git diff --check` passes.
Ambiguous multi-selector admission was also tested and rejected BEFORE any build.
The whole frontend/Rust/UI verification script was NOT run by this stream.

## Remaining Boundaries

- Retained evidence does not establish a final all-four same-artifact pass: the
  earlier combined log was overwritten and its replacement is inconclusive.
  A future acceptance run should use a unique log pathname and retain the wrapper's
  terminal exit/completion output as well as native output. This report did not
  launch another run or infer success from process exit or partial assertion totals.
- The full `verify-ui.ps1` frontend/Rust/browser gate and complete default native
  registry were not rerun by this stream. Historical failures in Ownership,
  GearEquip, WinFormsBlood and OrdinaryPolicy are not reclassified as current
  failures or passes here; their newer evidence belongs to their owners.
- Started preparation route rebind and broader action/rebirth/inventory fault
  acceptance remain release-owner concerns documented in
  `executor-actions-cutover-20261022.md`. The 40 newly integrated identity checks
  and real native auto-boost coverage do not prove every external integration
  boundary in that report is closed.
- Deployment, installed-game restart, reinjection, game-save/sidecar branch
  behavior and live soak were not performed. Offline passes do not authorize them
  or claim exactly-once behavior across arbitrary save branches.

The narrow fixture interface for other owners is
`NativeSettingsJsonBoundary.Configure(AssemblyDefinition plugin)` BEFORE writing
the in-memory plugin. Compile the shared source and reference System.Web.Extensions.
This is only a codec replacement; do not remove real persistence tests, initialize
fake grants, restore removed flags or change production ownership to satisfy checks.
