# Persistence Cutover Handoff

## Complete Browser Gate Green

The ENTIRE current desktop/mobile Playwright suite completed successfully on the
first isolated full run: **852 passed, 4 pre-existing platform skips, 0 failed,
0 flaky, 0 retries, 0 global errors; exit 0**. All **856 discovered cases across
16 files** are accounted for. This supersedes the earlier port-collision and
global-timeout attempts; neither earlier attempt was counted as acceptance.

JSON report start: `2026-10-23T07:43:25.543Z`; measured duration
`363046.053 ms` (6m 3.046s, CLI rounded to 6.1m).

### Isolation And Command

- Selected free loopback port **51478** using a temporary OS-assigned listener;
  neither 1420 nor 1502 was used. No existing server was reused or killed.
- **3 workers**, both configured projects, no grep/project filtering, retries 0,
  max-failures 0. Existing startup warm-up and independent Vite cache were retained.
- Only the WHOLE-SUITE bound was set to 90 minutes, with a 100-minute command bound;
  it finished normally in 6.1 minutes. Per-test 30s and existing assertion/deadline
  limits were not increased. No native/source-unrelated suites ran concurrently
  from D.
- Inspected current configuration/startup/settings-CAS fixtures first. They already
  publish settingsCasVersion 1 and real lossless read/save/revision envelopes.
  No parity failure occurred, so **no fixture, infrastructure, product UI, backend,
  core, test assertion, skip or settings semantics were changed** in this full-run
  task. Focused repair cycles used: zero. UI is left unchanged after green.

Executed from `NGUInjector.UI`:

```powershell
$env:NGUI_TEST_PORT = '51478'
$env:NGUI_TEST_OUTPUT = '<artifact-base>\test-results'
$env:PLAYWRIGHT_JSON_OUTPUT_NAME = '<artifact-base>.json'
$env:PLAYWRIGHT_HTML_OUTPUT_DIR = '<artifact-base>\html'
$env:PLAYWRIGHT_HTML_OPEN = 'never'
npx playwright test --workers=3 --reporter=list,json,html --global-timeout=5400000
```

Actual GUID artifact base:
`C:\Users\Kevin\AppData\Local\Temp\opencode\ngu-browser-final-082b9fa79f974cefb96919c077190d70`.

### Retained Artifacts

- Complete machine-readable report: `<artifact-base>.json` (all tests/results/
  annotations/configuration, empty global errors, final exact counts).
- Complete HTML report: `<artifact-base>\html\index.html`.
- Test outputs/screenshots and `.last-run.json`: `<artifact-base>\test-results`.
  Last-run status is `passed`, failedTests is empty. Failure traces were configured
  retain-on-failure; none were needed because no test failed.
- Complete 870-line terminal/stdout log:
  `C:\Users\Kevin\.local\share\opencode\tool-output\tool_167b970480019iQR0TE6b7Qwyq`.
  It contains the full 856-case list and final 852/4/exit-0 result.
- `<artifact-base>.log` is the PowerShell RUN METADATA transcript (port/options/
  artifact base/exit), not falsely described as the complete native stdout log;
  the complete stdout path is provided above.

### Existing Skips

All four skips are desktop instances of explicitly mobile-only regressions already
present in `e2e/stages.spec.ts`:

1. `vertical touch gestures scroll the page instead of dragging or toggling a stage`
   (line 1362; reason: Vertical scrolling is a mobile regression).
2. `edge-held touch release inserts at the scrolled logical boundary`.
3. `edge-held touch touchCancel stops continuous scrolling without saving`.
4. `edge-held touch Escape stops continuous scrolling without saving`.

The last three are the existing line-1413 parameterized group (reason: Held-edge
overflow is a mobile regression). **All four mobile counterparts PASSED** in this
full run. No expected-failure, failed, timedOut or interrupted result is hidden in
the JSON report. No new skip/expectation/timeout changes were made.

Only this owned documentation was edited after verification. No staging/unstaging,
commit, live operation, deployment, restart, nested agent or parent notification
call. The complete browser gate is green; final native same-artifact capture and
whole-release/live acceptance remain with their owners. No new game-DLL MVID is
claimed by browser verification.

## UI Review Defects Closed

The two independently reviewed UI defects are fixed. This UI-only follow-up does
not broaden the earlier 347-assertion native acceptance or establish full release.

### Outcome And Scope

- ExecutionReview renders the ACTUAL route preview response:
  `data.result.commitments.processes`, `manual`, `towerGear`/`towerFloor`/
  `towerDeaths`/`towerHistory`, and relevant `bossIdentity`/`bossSeconds`.
  Each process shows its owner, named native zone, selected `slot`/`id` gear
  (including explicit empty slots), beards and exact digger levels. Manual history
  shows deaths/limit, owned time, outcome and retained gear; pending tower selection
  remains visible even without an active tower process. Boss history shows the
  remaining fight window only when relevant.
- Rendering is read-only. No new backend fields, native authority, application,
  cleanup promise, raw JSON/material dump or storage explanation was introduced.
  Existing controls, palette, stats/table patterns and mobile wrapping are retained.
- The narrow runCommand change adds rebind-specific unknown-outcome guidance ONLY
  after an IPC submission attempt and without a matching explicit rejection. Enqueue
  failure, result-read failure, mismatched result and timeout instruct the user to
  inspect fresh runtime status and request a fresh review. They never replay an
  approval or request a new token automatically. Local approval tokens remain spent
  before IPC; manual refresh does not restore a cached preview.
- Matching `success:false` remains a known rejection, without an uncertainty warning.
  Pre-submission rejection remains known. Read-only rebind/restart preview failures,
  including timeout, do not claim an uncertain mutation. Timeout copy avoids the
  old generic retry hint and duplicate unknown-outcome warning.
- All current CAS controller/settings-transport changes were preserved. Browser
  fixtures use the real completed CAS boundary (`settings_json` + exact revision),
  and route previews use `commitments` rather than invented preset-style fields.

Changed files in this follow-up:
`NGUInjector.UI/src/lib/ExecutionReview.svelte`, `src/lib/controller.ts` (submission
marker and targeted catch guidance only), `tests/controller.test.ts`,
`e2e/execution-review.spec.ts`, and this report. Rust changes are FORMAT ONLY in
`src-tauri/src/main.rs`'s rebind validation blocks and
`src-tauri/src/execution-rebind-tests.rs`. No C# backend/core, route/combat,
shared-native fixture/manifest, settings API or settings transport semantics changed.
No staging/unstaging, commit, live operation, deployment, restart, nested agents,
parent notification calls or WIP edits.

### Tests And Actual Results

Added 10 focused Vitest cases and 4 Playwright test definitions (8 new desktop/
mobile executions). Existing review and recovery browser cases remain, for 12 total.
Tests mock only backend/native IPC, clocks and results; actual controller/App/
ExecutionReview are used. Assertions cover visible route slot/item/support values,
manual/tower/boss budgets, preview-only behavior, no horizontal mobile overflow,
exact-token approval, failed-token consumption through manual refresh, explicit
fresh-token approval, known rejection, read-only failures/timeouts and no replay.

| Verification | Actual result |
| --- | --- |
| `npm test` in UI | FINAL PASS 2,082 tests / 30 files, including all CAS regressions |
| `npm run check` | PASS zero errors/warnings |
| `npm run build` | PASS |
| `npx playwright test "e2e/execution-review.spec.ts"` with unique `NGUI_TEST_PORT` / `NGUI_TEST_OUTPUT` | FINAL PASS 12 desktop/mobile tests |
| `rustfmt --edition 2021 --config skip_children=true "src/main.rs" "src/execution-rebind-tests.rs"` | Formatted only the two files whose owned rebind blocks failed the initial check; no semantic edits |
| `cargo fmt --check` in src-tauri | PASS full crate check, repeated after formatting |
| `cargo test` | PASS all 179 Rust tests, including existing persistence/CAS regressions |
| `git diff --check` | PASS |

Final isolated browser run uses port `14942`, output
`C:\Users\Kevin\AppData\Local\Temp\opencode\execution-review-ui-handoff-20261022`.
Earlier green isolated runs on ports 14939/14940/14941 also passed all 12 flows.

Red-before-fix evidence: submitted approval enqueue/read/mismatch/timeout cases
failed for missing fresh-review guidance; the route browser case failed because a
successful real-shaped preview had no route process region. Two additional preview
timeout regressions failed on the old generic uncertain-mutation timeout copy.
The initial old browser fixture also exposed its obsolete object settings transport;
the fixture was corrected to the current CAS contract, not the production boundary.

These are UI/transport-boundary checks, not native route/application/cleanup or
elapsed soak evidence. No production/game DLL was built or deployed in this UI task;
no new MVID or full backend acceptance is claimed. The two requested UI defects have
no remaining blocker in this scope.

## Final Scoped Rebind Verification

This section supersedes the earlier investigation/handoff sections below. D's
requested recovery/preparation seams are implemented and the focused native gate
is GREEN. This is not a full combined-release or live-operation acceptance claim.

Final production MVID: `2ad8d8a8-d40b-4865-ade7-c4538d97ba27`.

| Current verification | Actual result |
| --- | --- |
| `& "checks/VerifyExecutorOffline.ps1" -Suite PresetIntegration -Selector --execution-persistence -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"` | PASS 347 native assertions; production and fixture rebuilt under the shared named mutex |
| `dotnet run --project "checks/ExternalUi/ExternalUiChecks.csproj" -c Release -- --native-settings` | PASS 171 assertions; CAS-owned files/fixtures unchanged |
| UI `npm run check` | PASS zero errors/warnings |
| `git diff --check` | PASS |

Only these implementation/test/UI files were changed for the scoped recovery work:
`NGUInjector/Main.ExecutionPersistence.cs`, `Main.ExecutionStore.cs`,
`Main.ExecutionIngress.cs`, `checks/PresetIntegration/ExecutionPersistenceChecks.cs`,
and the minimal `NGUInjector.UI/src/lib/ExecutionReview.svelte` scope/phase labels.
This owned report is also updated. No core, CAS, route/combat, shared native Program,
manifest, native action fixture, settings transport or verification-script edits.
No staging/unstaging, commit, live access, deployment, restart, WIP edit, nested
agent or parent task notification.

### Production Recovery

- `PreviewExecutionRebind` now derives the permission/recovery footprint from the
  ORIGINAL selected candidate's `ControlsEnergy` / `ControlsMagic`, owned native AT
  gate, selected gear/support and actual coupled route/recall side effects. It does
  not request both lanes from global Manage switches. Capacity-only review requests
  no Energy/Magic authority. Exact setup review retains acquired focus resource
  scope and includes recall only for an actual gear difference.
- The real production caller is `ReauthorizeReviewedExecution(review)` invoked by
  tokenized approval AFTER semantic/definition/instance/material/footprint checks.
  It calls CORE's actual `ReauthorizeExecutionDomains(grant, domains, reviewed)`;
  D does not implement another conflict/resource-clear method. The predicate binds
  one-use token, coordinator/native epoch/settings, original entry, named domain
  revisions, conflict revision, exact reviewed native resource objects/maps and
  current permissions. Successful approval spends the token and makes preceding
  grants stale. Failed token/permission/epoch/domain/native-map checks clear nothing.
- Only the named scope is reauthorized. D removed its blanket E+M invalidation and
  selected-setup conflict-clearing bypass. Metadata-only completed-process tokens
  have `None` footprint and are spent WITHOUT invoking domain reauthorization.
  A native-setting scope is refused if its unresolved evidence belongs to another
  owner; an AT review cannot globally acknowledge somebody else's target/settings.
- Existing LIVE native gear/support evidence may be retained ONLY when owner,
  acquisition, original definition/horizon, selected references/material and current
  exact native setup independently match the reviewed selection. This lets the
  ordinary shared resource replay repair healthy reviewed Energy while preserving
  an unreviewed Magic conflict. A restart still cannot fabricate an application
  receipt from serialized policy data.
- Existing command names/args are unchanged. Preview's additive `domains` and
  `observedResources` fields identify its exact scope. No Main.ExternalUi adapter
  change or whitelist expansion is required from the CAS owner.

### Preparation Process Import

- Exact kind `fight-prep` serializes the REAL
  `ExportFightPreparationCommitment()` output. Reviewed import calls the REAL
  `ImportReviewedFightPreparation(JSONObject, SelectedSetupRequirements)` API.
  There is no synthetic process, new GUID/acquisition, setup-only success stub,
  original-start/window renewal, or repeated entry fight.
- D validates exact ledger identity/original bytes, plan/entry/start, selected gear/
  support/material and latest durable stage/state. The pre-effect selection file
  can be older than the ledger's `selected`/`setup-ready` phase; review binds the
  latest actual durable phase, rather than overwriting it or granting a new budget.
- C's CURRENT original `fruitHarvests` and actual
  `PreparationFruitOpportunity(preparationIdentity, fruitId)` are used, not a
  current-counter/new-opportunity substitute. Review carries additive
  `observedEffectHistory` with original effect IDs and durable/native observations.
  Dirty, INVOCATION, UNKNOWN, rollback-inconsistent and unproven original effects
  remain scoped blocked. D does not flush storage or invoke fruit effects in Preview.
- A still-ambiguous `fight-requested` remains blocked before reauthorization/import;
  it cannot be replayed as a new preparation. Actual route retirement followed by
  C's durable COMMITTED classification DOES import terminal history without a claim,
  gear/resource/action authority or another entry fight. Original manual/automatic
  relationship is restored by C, and the completed selected intent is tombstoned
  inactive while retaining its evidence.
- C's no-scheduler entry, manual completion relation and stable fruit-opportunity
  fixes are present and consumed. Earlier report text saying those APIs/fixes were
  absent is historical and no longer a current blocker.

### Native Acceptance Scope

The 347-assertion selector includes original same-entry/reload/AT/W/OS/history and
corrupt-checkpoint/F8/focus cases plus:

- REAL grant-checked ResourceApplication/native-accounting callback foreign edits,
  explicit Preview+Approve, then ordinary SAME-entry native convergence. It asserts
  actual funded maps AND actual repair writes, not an existing receipt/status alone.
- Independent Energy-only and Magic-only acquisition/review/recovery while the other
  lane is unmanaged; Basic Training plus nonzero R3 current/idle/wish funding remain
  unchanged. Original acquisition/definition/entry/deadline/W history and optimizer
  search count are retained.
- Bad token, permission loss, domain revision, native epoch/model replacement and
  post-Preview native map change all perform zero injector model writes and leave
  the unresolved evidence intact. Spent review/preceding ordinary grants are stale.
- Energy recovery preserves a separate foreign Magic map AND its unresolved evidence
  and Magic-only grant; subsequent ordinary execution actually converges Energy.
  Persisted UNKNOWN irreversible action evidence is neither erased nor rearmed.
- Capacity-only native acquisition actually equips a larger-capacity selected item
  with preserving accounting and ZERO resource model writes. Review with BOTH lanes
  unmanaged has no Energy/Magic grant and preserves allocations through ordinary
  service. See the core status limitation below; it is not reported as Applied.
- Selected fight preparation persists before rejected preserving-capacity preflight,
  reloads the same original request/selection through C's importer, verifies through
  native shared setup, and admits the ORIGINAL route process without selection or
  budget renewal. A different request cannot supersede it; repeated conditions do
  not recreate the process. Original per-fruit UNKNOWN history invalidates review,
  even after newer native fruit-count observations, without writes/search/fight entry.
- Completed manual fight-only preparation is imported after actual route retirement
  without becoming automatic rebirth preparation; terminal approval does not revoke
  or clear an unrelated domain and writes an inactive tombstone.

Native action/AI/fruit effects beyond these boundaries remain the action/route
owners' acceptance responsibility. The UNKNOWN ledger inputs are explicit fault
injection, not claimed real native fruit consumption or a real Walderp kill.

### Concrete External Limits

1. **Core-owned fully-unmanaged capacity status/admission gate:** actual ordinary
   diagnostic is `Waiting; Preset apply requires a managed resource lane.` Current
   `Main.LoadoutPresets.cs:629-634` uses a no-argument
   `LoadoutPresetApplyBlocker()` that rejects both Manage switches off even for
   acquired capacity-only intent. The corresponding
   `Main.GlobalPresets.GetGlobalPresetApplicationIssue` also tests the global lane
   switches. Native gear/accounting are correct and the D review has no resource
   authority, but fully-unmanaged ordinary preset status is NOT certified Applied.
   Needed core interface/behavior: pass the effective acquired preset/control mask
   into that admission (an overload accepting `CustomLoadoutPreset`, or equivalent)
   and permit a `None` resource mask while retaining preserving-capacity/accounting
   proof and `Recall=None`. Do not enable an unmanaged lane or grant resource writes.
   D did not edit core-owned files to bypass this guard.
2. **Ambiguous started preparation:** if C's ledger is still `fight-requested` or
   UNKNOWN without a real retired/completed classification, D deliberately keeps
   it blocked. Resuming actual already-running boss history needs the route owner's
   original-process continuity API; absence of native boss flags alone is not proof.
3. Full combined suites, native UI/AI/action fault timelines and the live freeze/
   restart/soak remain parent/review/live-owner gates. Only the focused selector and
   source checks above were run by D in this completion.

The interrupted run was not counted. Subsequent failures exposed missing native
R3 coefficient fixtures (ITOPOD/quirk/wish data), which were completed with real
zero-level native data, and an item placed outside unlocked inventory, which was
placed in an actual unlocked slot. No native preflight/authority gate was weakened.

## Earlier Supervised Investigation (Superseded)

Ownership follows `agent-supervision-20261022.md`: D touches only its persistence/
ingress/store partials and ExecutionPersistenceChecks. No Main.ExternalUi/settings/
Rust/settings transport/route/combat/shared Program/manifests/live/WIP/commit edits.

Current implementation adds a real `ReauthorizeExecutionDomains` caller ONLY in
tokenized approval, after revalidating original selection, exact footprint, native
instances/material/context and current allocations in the reviewed resource lanes.
The scope comes from acquired candidate ControlsEnergy/ControlsMagic and actual
setup/recall side effects, not both Manage flags. Approval no longer blankets
Energy/Magic invalidation or grants a conflict-clearing selected-setup bypass.
Preview results add `domains` and `observedResources`; existing command names/args
are unchanged, so no Main.ExternalUi/CAS-owner transport edit is requested.

C's real `ExportFightPreparationCommitment()` and
`ImportReviewedFightPreparation(JSONObject, SelectedSetupRequirements)` are now
wired for exact kind `fight-prep`. D captures the original per-fruit opportunities
before effects, keeps that baseline on retries, binds the latest durable preparation
stage/state into the review, and refuses an uncertain or newly-ready repeated fruit
opportunity. Started `fight-requested` remains scoped blocked unless the actual
route owner can provide a completed/retired classification; no entry fight is replayed.

Concrete C follow-ups observed while wiring (C-owned, not edited by D):
- RunFightPrep and ImportReviewedFightPreparation still dereference
  AutomaticRecoveryEntry with no scheduler. Use D's existing safe
  ExecutionPersistenceEntry for standalone/manual preparation, not a synthetic stage.
- The committed importer returns before restoring original manualRebirth/opportunity
  and currently sets the global completed-prep flag unconditionally. Preserve the
  original process relationship rather than treating manual fight-only completion
  as an automatic rebirth preparation.
- Partial fruit continuation still uses current native harvest count for a new
  opportunity. D conservatively withholds newly-ready fruit after an already
  committed original harvest; C should retain/consume original per-prep effect IDs.

Verification is IN PROGRESS in this section; previous counts below do not validate
these edits. All native runs use the named-mutex wrapper.

## Earlier Continuation Handoff

The sections below this heading retain the earlier partial-cutover report for
context. THIS SECTION supersedes its stale test counts and now-completed gaps.
No live access/deployment/restart/soak/commit/WIP edits/nested agents. No manifests
or shared native Program files edited in this continuation; those remain parent/A
owned. All native commands used the parent's named-mutex wrapper.

### Implemented And Verified

- Diagnosed the original null/empty fight assertion: shipped checkpoint serialization
  normalizes absent identity to `""` BEFORE approval. Native checks now explicitly
  assert that normalization, compare approval against loaded identity, cover a
  nonempty historical identity, and verify actual native fight/nuke flags and no
  new finite-action evidence. No blind assertion relaxation.
- ACTUAL positive same-entry preset reconciliation now runs after approval in both
  AutoUpdate modes. It checks original acquisition/definition/horizon/frozen scoring,
  original entry/credit/deadlines/AT/W/fight history, and no replacement search.
  The saved library definition is edited to Blood BEFORE actual continuation;
  the acquired old definition remains in use without rewriting user configuration.
- Positive selected-exact-setup-v2 import is implemented for F8, retained sustained
  focus and manual gear (`manual` and `manual-gear` aliases). Context, exact codec
  resolution, material/reference uniqueness, support semantics, original focus
  AUG/ritual/global exclusions/OS/AT inputs, paused-plan entry, settings/epoch/
  coordinator/domain/conflict revisions and short-lived one-use tokens are bound.
  Preview does not equip or acquire grants for native effects. Approval publishes
  current-session intent; ordinary service replays through the real shared executor.
- F8 EXPLICITLY acquires its original return selection with core's real
  `SetExecutionReturnBaseline`, persisting it as reviewed semantic selection, NOT
  historical cleanup authority. Actual CURRENT normal baseline wins over this
  explicit fallback. Native restart/review/apply/second-toggle verifies exact
  no-successor return and original acquisition, with no search.
- `ClearPersistedExecutionSelection(string kind)` writes inactive tombstones before
  stop/supersession. A has wired focus/dashboard/manual hooks; actual native focus
  stop and F8 stop tombstones are tested. Core causal-material pending-save handling
  calls the existing `PersistApprovedExecutionMaterialRevision` hook.
- `CurrentExecutionCommandId` is scoped in try/finally around external dispatch.
  Async commands durably write `.accepted.json` admission BEFORE their callback,
  then semantic process identity/entry/deadline/selected commitments BEFORE ACK.
  Tokens are not exported as authority. Actual claimed dedup is unchanged; locked
  admission persistence withholds the callback and cannot replay later. New source
  boundary tests prove pre-ACK publication, actual request opportunity, lock failure
  before callback and dedup. `ExportAcceptedExecutionProcess` uses real runtime
  process IDs when available, otherwise the actual request ID, never a new GUID.
- Quiesce now services B's exact `EndRouteProcess` / tower pause leaves, combat's
  exact route retirement and narrow borrowed target/nuke returns, then core
  `ServiceExecutionRetirement`. All grants are the CORE grants; no competing
  retirement-grant method or Running bypass was reintroduced. Native unknown boss
  activity refuses bounded retirement and retains prerequisites; retry after native
  completion succeeds. Actual Quick/ordinary/resource callbacks cannot write after
  revocation. Lifecycle semantics are captured before retirement, and prior bytes
  archived before replacement. Native quit logs/preserves unresolved evidence and
  stops callbacks; it does not promise an indefinite wait.
- F7/cloud use the same quiesce path. Epoch reset ends only stale authority without
  successor reconciliation, reloads semantic review candidates and never clears the
  action ledger. Removed `_protectedSkipRestore` and all its owned assignments;
  there is NO old mismatch-to-new-uncredited-budget fallback. No Main caller of
  RestorePitGear/CleanupStaleHarvest remains.
- Explicit corrupt-checkpoint recovery is accessible through
  `PreviewExecutionRestart({stageIndex, checkpointRevision, planRevision})`, then
  existing `ApplyExecutionRebind({token, checkpointRevision})`. UI requires an
  explicitly selected row and separate review/apply; no implicit row choice.
  Native tests prove old bytes retained, token spent once, zero fabricated credit,
  zero hidden allocation writes. Archives use bounded sibling names
  `stage-recovery-<guid>.json` / `stage-rebirth-<guid>.json`: native testing caught
  and fixed desktop CLR MAX_PATH failure from appending to the 64-hash filename.
- B's actual `ExportRouteExecutionCommitments`, `ImportRouteExecutionCommitments`
  and `RebindRouteExecutionCommitments()` APIs are wired into startup/review/apply.
  D preview additionally validates full route gear through the EXISTING descriptor
  resolver, not ID tie-breaking, and binds actual current instance references.
  This is process import, not a setup-only claim. B's remaining process-specific
  restrictions below are NOT claimed solved by wiring the API.
- Runtime adds read-only pending finite-action identity/state/durability observations.
  It does not initialize/invoke the action runner from telemetry.

### Actual Commands And Results

| Command | Actual result |
| --- | --- |
| `& "A:\misc\NGU-Automation\temp\verify-executor-offline.ps1" -Suite PresetIntegration -Selector --execution-persistence` | FINAL PASS 147 native assertions, MVID `d4371c3d-ef8e-4afc-8999-ad7095c002b1`; preceding 147 run `9c129ccb` and earlier 98/109/135 runs passed their then-current subsets |
| `dotnet run --project "checks/ExternalUi/ExternalUiChecks.csproj" -c Release` | PASS 9,822 assertions; native/action transport boundaries explicitly substituted |
| UI `npm run check` | PASS zero errors/warnings |
| UI `npm test` | PASS all 2,052 tests / 29 files |
| UI `npm run build` | PASS |
| UI `npx playwright test "e2e/execution-review.spec.ts"` | PASS all 4 desktop/mobile preview/approve and explicit recovery flows |
| src-tauri `cargo test` | PASS all 171 tests, including pre-existing persistence regressions |
| `git diff --check` | PASS |
| Original source `--native-settings` selector, final rerun | PASS 171 assertions; original test file unchanged |

Initial continuation native build was transiently blocked by A's in-flight
ExecutionCoreCutoverChecks local names; no fake symbols were added. Initial new
browser fixture omitted the required externalUiEnabled heartbeat field; corrected
the fixture, not readiness safety. Initial async acceptance exposed a nullable
source-boundary JSON process ID; fixed safe string handling. Initial recovery
native test exposed MAX_PATH as above. Failed runs are not reported as passes.

### Still Required Before Whole-Goal Acceptance

1. C's promised FIGHT PREPARATION semantic export/rebind API was NOT PRESENT at
   the last actual source inspection. D did not invent a stub, generate a new
   process identity, or mark a setup-only rebind as process restoration. C must
   expose original per-preparation effect/history commitments and call D's actual
   selected-setup exporter BEFORE dependent native effects. D's real
   `ExportExecutionSetupSelection(kind, definition, setup)` and
   `ImportExactExecutionSelection(JSONObject)` are available within Main partials.
   The process importer must preserve fruit/fight phase evidence, not rerun a
   still-true preparation condition. Wire its actual API into review when returned.
2. C's manual-prep request opportunity still did not read CurrentExecutionCommandId
   at inspection, and manual rebirth still lacked the before-reset
   CommitExecutionRebirthCheckpoint call. APIs/property are implemented; C must
   consume them. Do not infer rebirth commitment from an epoch observer.
3. B's positive route rebind currently explicitly REJECTS imported manual-titan/
   tower death-log cursor restoration, and exact prior zone/settings checks can
   reject the safe route produced by lifecycle retirement. That is safe rejection,
   NOT the requested positive retained process restoration. B must finish its
   specific cursor/history continuity/review policy without refilling deaths/owned
   time or granting imported cleanup authority. Native route/process rebind tests
   and actual combat-phase lifecycle return faults remain required.
4. Parent/A must remove obsolete reflective `_protectedSkipRestore` fixture sets
   in their owned old migration tests, not restore the production flag. No shared
   native Program/manifests were edited here. New D tests remain in the existing
   ExecutionPersistenceChecks.cs selector/full hook; no new native file entry needed.
5. New source ExternalUi ExecutionBoundary names native lifecycle/import operations
   explicitly and NEVER returns fake native review success. It cannot substitute
   for full native route/fight-prep/process acceptance. Full native suites and
   route/titan/tower/fight-preparation/finite-effect persistence fault timelines are
   still parent-owned. No assertion that all WIP gates are complete is justified.

Parent continues the full WIP and real restart/+2h/+4h/+6h checks. Nothing live was
read, mutated, deployed, restarted, scheduled or claimed elapsed by D.

## Outcome

## Continuation Contract

ACTIVE D continuation, manifests/shared Program files are PARENT/A-owned now.
`ClearPersistedExecutionSelection(string kind)` is the real durable stop/
supersession hook for A. Kinds: `focus`, `manual-gear`, `dashboard`, `quick`.
It writes an inactive tombstone preserving prior semantic bytes; invoke BEFORE
retiring intent so a write failure cannot resurrect a stopped owner next restart.
`PersistExecutionSetupSelection` now writes selected-exact-setup-v2 with active,
controlsSupport/requiredBeards, full exact equipment/support, original definition,
native clock context, paused stage entry and acquired focus AUG/ritual/NGU/OS/AT
commitments. A must call after acquiring these decisions and BEFORE funding them.
No new process/acquisition is generated during import. Non-GUID acquisitions use a
deterministic 128-bit descriptor-codec identity only; actual owner/acquisition in
the envelope/grant/process is unchanged and independently validated.

CurrentExecutionCommandId will be scoped to the real external command dispatch
and available to C for manual process opportunity, never a new-GUID fallback.

Implemented the selected-preset semantic importer/review transport, fail-closed
outer-checkpoint preservation, lifecycle revocation wiring, explicit F8 claim,
converted owned UI/probe/action adapters, bounded execution telemetry and shared
manifest/test hooks. This is a PARTIAL offline cutover, NOT the whole requested
persistence/ingress/lifecycle end-state or release acceptance. Integration blockers
below must be completed before deployment.

Stream D owns this report and the persistence/lifecycle/ingress files. No live
bridge/settings/game/deploy/commit, parent WIP edits, or nested agents.

Implemented in `Main.ExecutionPersistence.cs`:

```csharp
internal JSONObject PreviewExecutionRebind(JSONNode args); // empty object
internal string ApplyExecutionRebind(JSONNode args); // token, checkpointRevision
private void PersistApprovedExecutionMaterialRevision(GlobalPresetPolicy policy, string owner);
private void PersistExecutionSetupSelection(string kind, string definition, SelectedSetupRequirements setup);
internal void CommitExecutionRebirthCheckpoint();
internal ExecutionGrant ResolveExecutionRetirementGrant(ExecutionClaim claim,
    ExecutionDomain domains, Func<bool> stillOwned);
internal GearOptimizerResult ApplyManualGearSelectionIngress(GearOptimizationGoal goal, FightScoreMode mode);
```

Core owner: call `PersistApprovedExecutionMaterialRevision(policy, owner)` immediately
after approving new `policy.Material` for the same instances, before any dependent
credit/action/write. It delegates to the existing pre-application persistence hook;
it throws on mismatch or disk failure. Do not swallow this failure and grant credit.

Focus/manual exact-setup producers: `PersistExecutionSetupSelection` writes a
separate versioned `execution-<kind>-selection.json` with exact selected equipment,
support, original definition and paused-plan relationship. F8 calls it before
application. Focus/manual-gear must call it before application, and after approved
causal revisions. Positive exact-setup (non-preset) import/review is NOT implemented
yet; this is an explicit remaining gate, not a claim of full focus durability.

Finite rebirth owner: invoke `CommitExecutionRebirthCheckpoint()` ONLY after the
identified native rebirth is verified committed, before reset/epoch observation.
It archives the old bytes to `.completed-rebirth-<guid>`; no clock observer is
allowed to remove a checkpoint. The automatic `RunCustomRebirthAutomation` success
branch calls this hook; manual rebirth must do the same. An existing invalid outer
checkpoint is never archived as a completed rebirth.

Reviewed import installs `_globalPresetPolicies[id]`, `_globalPresetApplyingPolicy`
and `_globalPresetApplyingEvent = owner + ":" + id` with the ORIGINAL acquisition,
definition, resolved definition, horizon, AT/AUG/OS, route/support, frozen references
and exact resolved instances. No application receipt or native return lease is
created. Core owner must preserve this acquired policy (including Horizon/Plan)
when consuming the existing continuing-policy path; do not create a replacement
selection or recapture frozen references. Dashboard review sets `_globalPresetManualId`;
normal refresh must consume the retained applying policy even with no old receipt.

Lifecycle boundary: ordinary admission rejects `_executionLifecycle != Running`.
`ResolveExecutionRetirementGrant` is implemented in `Main.ExecutionIngress.cs` and
returns the core `ExecutionGrant`, never a blanket Running bypass. It admits ONLY
an existing exact still-owned claim's Route/NativeSettings subset, in Quiescing,
with a two-second deadline, same generation/epoch/settings/domain revisions and
caller-supplied exact still-owned predicate. Route owners must use it ONLY for an
exact safe-route retirement or narrow still-owned borrowed-field return, not route
activation. It does not admit gear/support/resource/action writes. Route-owner
integration/service is STILL REQUIRED: `Unload` now refuses active owned titan/gold/
combat routes and remains quiesced rather than calling old cleanup writers. No
claim is made that a blocked unload can currently finish automatically.

Action adapters now use the actual implemented C APIs: augment selection ->
`RequestAugmentAllocation(OptimizeBest(...))`, pit/spin -> `RequestMoneyPit` /
`RequestDailySpin`, WinForms harvest -> `PrepareHarvestAction`, identified
`HarvestFruitAction` per fruit, then `ReleaseHarvestAction`. WinForms/probe gear
uses `ApplyManualGearSelectionIngress`. Probe support changes submit exact claims,
not BeardManager/DiggerManager native writers. The unreachable legacy branches
in BOTH QuickStuff and AutomationRoutine were deleted; `LegacyAutomationEnabled`
remains false for shipped configuration/display compatibility.

## Persistence Semantics

- Reads shipped outer checkpoint versions 1/2 and existing selected-policy v1.
  OS readers were not edited. Completed-plan native AT fallback was not changed.
- Import reconstructs original/resolved definitions, original horizon, exact AT
  targets, acquired route/eligibility witnesses, support mode/exact selection,
  acquired AUG/OS/fixed legacy decisions and frozen references. It calls the
  existing equipment descriptor resolver: missing, ambiguous, aliased, changed
  material or unavailable slots reject without replacement search.
- Preview does read-only native capture/evaluation, never Plan/optimizer/apply.
  Apply consumes a 120-second token bound to exact-byte SHA-256 checkpoint revision,
  owner/acquisition/entry, native epoch, coordinator/domain generations, current
  exact references/material, permissions/configuration relationship and setup
  conflict revision. No scheduler transition, earned credit, fight identity,
  AT/W/OS history, finite-action history, application receipt or cleanup authority
  is imported. Approval publishes intent for the next ordinary service, not a
  fictitious applied receipt. The next reload requires review again.
- Invalid outer checkpoint bytes cannot be overwritten by progress saves or
  observation, and GetProtectedStage returns no runnable entry. Only explicit
  `setPlanStage` action `restart` can replace unrecoverable commitments; it first
  copies the original bytes to `.unrecoverable-<guid>`. No silent uncredited budget.
- Dashboard acquired policies persist independently in `execution-selection.json`,
  retaining original definition and paused-plan relationship. F8 persists an exact
  standalone `selected-exact-setup-v1` record, not a fake scheduler stage.

## Files And Manifests

New production partials: `Main.ExecutionPersistence.cs`, `Main.ExecutionStore.cs`,
`Main.ExecutionIngress.cs`, `Main.ExecutionTelemetry.cs`. Edited owned production:
`Main.ProtectedTiming.cs`, `Main.ExecutionLifecycle.cs`, `Main.AutomationLoop.cs`,
`Main.Lifecycle.cs`, `Main.cs`, `Main.ExternalUi.cs`, `Main.CustomProbe.cs`,
`SettingsForm.cs`, `SettingsForm.Custom.cs`, `NGUInjector.csproj`.

Added production Compile entries for those four new D partials AND currently
available C/route files: `FiniteActionLedger.cs`, `Main.FiniteActions.cs`,
`Main.ActionSetup.cs`, `Main.PurchaseActions.cs`, `Main.PitActions.cs`,
`Main.FruitActions.cs`, `Main.BloodActions.cs`, `Main.BloodActionWindows.cs`,
`Main.RebirthActions.cs`, `Main.InventoryActions.cs`, `Main.AugmentActions.cs`,
`Managers/GearOptimizer.RouteSelection.cs`. Re-glob after all streams return for
later additions. No core/route/action implementation file was manually edited.

New `checks/PresetIntegration/ExecutionPersistenceChecks.cs` plus selector
`--execution-persistence` and inclusion in the full Preset runner. Added Compile
entries to PresetIntegration, CombatPrepIntegration and ManualAdventureOverlay,
all of which compile the shared Preset partial. No CombatFixture Program edit.
These native tests are wired but UNRUN: positive review in both AutoUpdate modes,
same entry/credit/deadline/fight/history/acquisition, no writes/search, consumed /
expired token, changed checkpoint/domain revision, reload review, corrupt outer
checkpoint preservation. Parent must run against a fresh production DLL.

Source ExternalUi adds `ExecutionBoundary.cs` and a Compile entry. Native review
endpoints THROW NotSupportedException there, never fabricate authority/success.
Real authority belongs to the production-assembly native checks. Lifecycle bridge
assertions intentionally now assert no old cleanup writer after quiescence and
retained unresolved push evidence; native retirement is NOT simulated as passing.
Pre-existing `NativeSettingsPersistenceChecks.cs` was preserved unchanged.

UI: new minimal conditional `ExecutionReview.svelte`, mounted by App, with explicit
Preview/Approve and selected gear/support. Review component is keyed by runtime
folder/session, consumes its local token before IPC, and appears only when runtime
requests review. Rust exact argument validation plus new
`execution-rebind-tests.rs`; pre-existing `persistence-regression-tests.rs` unchanged.
Runtime adds `executionReview` and `execution` (actual desired/observed/verified
gear/resource arrays, support match versus desired feasibility, native AT/OS,
route observations, claim identities, lifecycle/domain waiting reasons). It does
not call scheduler Update, optimizer, application or cleanup.

## Actual Verification

No run_and_extract_errors tool was available; terminal commands were used.

| Command (repository root unless noted) | Result |
| --- | --- |
| `dotnet run --project "checks/ExternalUi/ExternalUiChecks.csproj" -c Release -- --native-settings` | PASS 171 assertions; original file unchanged |
| `dotnet run --project "checks/ExternalUi/ExternalUiChecks.csproj" -c Release` | PASS 9,817 assertions, rerun after augment adapter change; source transport/native boundaries explicitly substituted |
| UI directory: `npm run check` | PASS, zero errors/warnings |
| UI directory: `npm test` | PASS 2,052 tests / 29 files |
| UI directory: `npm run build` | PASS |
| src-tauri directory: `cargo test execution_rebind_tests -- --nocapture` | PASS 1 test |
| src-tauri directory: `cargo test persistence_regression_tests -- --nocapture` | PASS original 5 tests |
| `git diff --check` | PASS |

Source bridge emits its existing unused-field warnings and one equivalent new
boundary-only `_executionRebindReview` warning. No production build was run under
parallel-build discipline. No new native persistence test has been executed.
No review browser/desktop/mobile e2e test was run. Existing UI tests/typecheck/build
are NOT proof of actual native review or same-entry executor continuation.

## Remaining Blockers

1. CORE MUST consume the reviewed `_globalPresetApplyingPolicy` using its acquired
   ORIGINAL preset/resolved definition/Horizon/Plan, not the current library row or
   newly constructed acquired policy. Current core continuing path still constructs
   a new policy and uses the supplied horizon, and ReconcileGlobalPreset initially
   selects the library row when no old receipt exists. This is especially important
   for saved-definition edits and dashboard review. Positive native continuation
   cannot be accepted until fixed and tested. Also wire the approved causal-material
   persistence hook before dependent credit/actions, with rollback on save failure.
2. NON-PRESET exact-setup positive importer/review is missing. F8 records semantic
   selection and uses a current-session claim, but restart cannot yet review/rebind
   that claim. Legacy focus/manual exact producer persistence hooks are available
   but not called by those core-owned producers; original focus targets/decisions
   need durable semantic import, not a setup-only substitute. Do not claim Quick/
   legacy focus durability complete.
3. Lifecycle retirement service must integrate the bounded grant with route/tower/
   combat actors and exact borrowed-setting returns. Currently refusal is safe but
   may remain quiesced indefinitely; no Running toggle workaround is allowed. Actual
   native quit cannot be delayed indefinitely. Unknown native boss activity needs
   the route-owner safety treatment, not boolean cancellation. Manual verified
   rebirth must call the committed-checkpoint archival hook before reset.
4. Corrupt-outer explicit restart has a backend `setPlanStage` path, but normal UI
   stage gating may not expose a usable restart when scheduler state is unmatched.
   Add a revision-bound explicit recovery control, without silently selecting a row.
5. Existing durable command-file claims/dedup and unknown-command no-replay were
   preserved. A separate durable accepted asynchronous process-ID record before ACK
   is not implemented in this stream; validate/finish it with route/action owners.
6. Finish native end-to-end reviewed SAME-entry replay (not just approval),
   same horizon/frozen references/AT/W/OS/history before final acceptance; fresh
   production/native fixture builds, full regression suites and fault injection
   remain parent-owned. Source-linked Focus/Combat real-authority boundaries remain
   their owners' integration responsibility. No fake production symbols were added.
7. Audit all callbacks/remaining legacy config-only references and exact UI telemetry
   completeness in final integration. This report is not proof every native writer
   in other streams is converted or every telemetry domain is verified.

Parent owns WIP/live/restart and actual bounded +2h/+4h/+6h observations. None were
performed or scheduled by D; none are claimed completed.
