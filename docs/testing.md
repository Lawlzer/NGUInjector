# Testing And Save Safety

Two public PowerShell 7 entrypoints separate offline verification from game-facing
work. Run them from the repository root. Internal native/UI helpers are not public
alternatives, and neither entrypoint automatically deploys an injector.

Live requires a compatible deployed backend. Runner changes and compiled test
evidence do not establish actual Unity/native acceptance; **no actual live
transaction is certified here**. Historical failures/results remain in their dated
reports, not as current build blockers in this guide.

## Commands

```powershell
# All offline scopes (All is the default). Native needs installed game references.
$env:NGU_IDLE_DIR = 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
.\verify.ps1
.\verify.ps1 -Scope UI
.\verify.ps1 -Scope Source
.\verify.ps1 -Scope Harness
.\verify.ps1 -Scope Native -Suite PresetIntegration -Selector '--execution-core'
# An explicit -GameFolder can replace NGU_IDLE_DIR:
.\verify.ps1 -Scope Native -GameFolder 'C:\path\to\NGU IDLE'

# Game-facing, read-only diagnostics (default); no prepare/save/load commands.
.\test.ps1
# Decode existing fixture files without reading the runtime or loading the game.
.\test.ps1 -Mode Inspect -FixtureManifestPath '.\test-fixtures.example.json'
# Capture a fresh local backup, scan and diagnose; longer-save loading needs opt-in.
.\test.ps1 -Mode Prepare
# Only when currently authorized; fixtures must already exist and backend must support them.
.\test.ps1 -Mode Live -ConfirmLiveSaveLoad -FixtureManifestPath '.\test-fixtures.example.json'
# Explicit recovery of the runtime's existing interrupted save-test session.
.\test.ps1 -Mode Restore
```

## Offline Verification

| `verify.ps1 -Scope` | Boundary |
| --- | --- |
| `All` (default) | UI, Source, Native and Harness; no live bridge access, game launch or injection. |
| `UI` | Svelte/TypeScript, frontend tests/build, Rust formatting/tests/check and browser coverage. |
| `Source` | Source-linked C# planners, coordinator/bridge and session checks with their declared fixture boundaries. |
| `Native` | Compiled injector and selected installed-game fixture suites; `-Suite` selects suites and `-Selector` narrows one suite. |
| `Harness` | Offline save-safety/runner harness checks, not a real game fixture transaction. |

Native uses one incremental desktop MSBuild invocation of `checks/NativeVerification.proj`
to compile production and selected fixture dependencies. Test programs remain
separate assemblies/executables. Full native artifact capture is supported, without
automatic deployment; capture and scoped results do not imply a full release.
The helpers under `checks/verification/` are internal, not additional entrypoints.

Use quiet source ownership BEFORE starting verification and retain ownership of
shared outputs throughout it. A named lock cannot stop an uncoordinated editor or
direct build. Matching start/end fingerprints detect no endpoint drift; they do
**not** prove a coherent snapshot or rule out edits/reverts between observations.
Do not queue behind another owner, edit tested sources, or build directly over it.

Reports under `artifacts/verification/<run ID>/` retain JSON, per-phase command/exit/log evidence, source identity and native
artifact identity where applicable. Contention, explicit defer or source/artifact
drift exits **2**, with an append to `artifacts/agent-handoff.md`, never a pass.
Use exact arguments for an intentional checkpoint:

```powershell
.\verify.ps1 -Scope Native -DeferReason 'Production source owner is still editing' `
    -HandoffNote 'Next: owner checkpoints quiet sources, then verify the full Native scope.'
```

A selector requires one suite and is not full-suite proof. Preserve
`checks/Combat/VerifyRegressions.ps1` as separate behavioral source-mutation coverage;
it checks assertion rejection rather than compilation and is not consolidated here.
See [agent workflow](agent-workflow.md) for the single handoff/checkpoint contract.

## Game Modes

`test.ps1` never runs an offline gate and accepts neither `-SkipOfflineChecks` nor
`-GameFolder`. Its default runtime is `%USERPROFILE%\Desktop\NGUInjector`; override
with `-RuntimeDir`. Reports/phase logs go to a new `%LOCALAPPDATA%\Temp\opencode\ngu-test-*`
directory, or an existing `-ReportParent` outside the runtime directory.

| `-Mode` | Effect |
| --- | --- |
| `Diagnostics` (default) | Read current runtime diagnostics without publishing game commands. |
| `Inspect` | Decode registered existing fixture files; no runtime read or game load. The standalone inspector may build into the report directory; this is not an offline gate. |
| `Prepare` | Capture a NEW local native backup, scan, optionally load a longer-playtime save, then diagnose. Loading requires `-ConfirmLiveSaveLoad`; otherwise retain the backup and return BLOCKED. No automatic resume/rebind/stage/settings workaround. |
| `Live` | Require `-ConfirmLiveSaveLoad`, existing inspected fixtures and compatible backend capability; run sequential fixtures and attempt protected-original restore in `finally`. |
| `Restore` | Explicitly recover a known backend save-test session, including a quiesced recovery state. |

An already-current game is not needlessly reloaded or assigned a new entry.
Inspect/Live default to `test-fixtures.example.json`, whose configured local paths
must exist. Exit **0** means only the selected mode passed, **1** failure, **2**
blocked/incomplete. Reports retain request IDs, UNKNOWN outcomes, inspection,
fixture generation, backup identity and verified restoration separately. A timeout
does not prove no effect; persistent command claims/results are never deleted or replayed.

**Native offline progress, Steam Cloud and achievements are accepted side effects.**
Native loads retain/report progression gains rather than requiring identical native
bytes afterward. A VM or separate Steam account is not required, but explicit live
authorization/confirmation and the native/local-file restore checks still are.

## Save Validation

Backups are fresh native encoded exports, not diagnostic JSON or autosave copies.
Capture temporarily stamps native `lastTime`, restores the live timestamp, flushes
a unique file, reopens/hash-verifies it and decodes those exact bytes. Exported player,
rebirth and clocks must match the game. `captureLocalSave` requests no cloud write;
`%USERPROFILE%\Desktop\NGUInjector\save-safety\backups` is never automatically pruned.

Inspection uses `System.Formats.Nrbf`, not `BinaryFormatter.Deserialize` or the game
loader, and constructs no game/mod objects. It verifies checksum, required records,
versions `361..1260`, finite nonnegative clocks and `rebirthTime <= totalPlaytime`,
with a 32 MiB limit. Native loading is separate and requires a validated, pinned,
trusted local candidate.

Discovery is nonrecursive, at most 4,096 files across native saves, injector
`NGUSave.txt` and protective backups. Other disks/nested profiles/arbitrary backups/
opaque Steam cache are not claimed scanned. Background inspection is bounded per
frame with periodic rediscovery; destructive admission rechecks candidate bytes.
Timestamps are a display optimization, not progression or mutation authority.

Rank by `PlayerData.totalPlaytime.totalseconds`, never file date/name, rebirth age,
NUMBER or injector uptime. Compare against the longest KNOWN decoded save. Different
hashes tied at maximum require review; equal copies are not ambiguous. Cross-player
loads are refused. Loading rechecks session, selected hash/bytes, native outcome,
rebirth and clocks; normal selected-policy review requirements remain in effect.

`high-water.json` retains maxima/ambiguous branches across reinjection. Missing or
lower replacements, read failures and failed evidence writes cannot erase higher
progress. Preparing/loading/completion evidence is flushed/atomically published;
this is not a universal power-loss, storage-fault or save-branch identity guarantee.

## Warnings And Recovery

The desktop UI retains red alerts for a behind save/unresolved recovery and amber
warnings for incomplete/ambiguous/unavailable evidence, including across disconnects.
Manual quicksave retains a verified local backup and refuses cloud publication on
unverified safety; it does NOT intercept native-game/other-mod autosaves or cloud calls.

Live creates an immutable original save and verified bundle under
`save-safety/tests/<session ID>`. A fresh `Main` component from the already-loaded DLL
retains continuous `.injector.lock` ownership per fixture: no new injection or game
restart. Ordinary mutations/hotkeys are fenced; logs and command claims/results are
never rolled back. Original settings/plans/presets, earned stage/frozen choices and
scoped native saves are preserved, including originally absent paths. Transient
claims/cleanup authority are not imported; retired action journals remain inert.
Fixture output is archived, and production inspection/high-water updates pause so
fixture captures cannot become production recovery candidates. Steam effects remain.

Restore is attempted in `finally`, including after failed assertions or lost start
acknowledgement. Pre-dispatch client session IDs/cancellation tombstones guard late
starts; fixture generations/hashes guard late commands. Unknown loads are not replayed.
The recovery host survives component replacement/startup failure and admits status/
explicit restore while ordinary work is quiesced. Cold recovery verifies pinned
manifest/bundle hashes and actual runtime/native roots; protected journals/bundles
are outside the restored file set. F7 cannot bypass testing or unresolved recovery.

A returned load or executed `finally` is NOT a restore pass: require native progression,
verified original files and fresh-owner readiness. Best-effort disposable-output
archive errors/unarchived hashes are reported without blocking original return;
corrupt originals, unsafe paths or failed original writes remain real failures.
Never delete recovery evidence to silence a warning. Clocks are regression witnesses,
not immutable branch identity or universal exactly-once proof; native offline gains
are accepted, not byte-identical game-state rollback.

## Diagnostics

Default `test.ps1` calls the read-only `diagnostics.ps1` observer. It reads runtime
JSON; optional standalone report output is a new path outside runtime. No marker,
game command, optimization, setting, save or load is published. Checks inspect exact
integer allocations, ownership, gear/support/affordability, native settings, NGU zero
exclusions, idle pools, plan/session continuity and save safety. `automationErrors`
must be present/empty; missing telemetry is inconclusive, not zero errors.

Results distinguish PASS, FAIL, WAITING, INCONCLUSIVE and UNCONTROLLED. Only verified
fixture session/generation/hash fencing permits intentional older-save alarms to be
labelled EXPECTED; UI alerts/evidence stay visible. Independent NGU caps/ratios remain
INCONCLUSIVE without current authoritative budgets. Old buffered caps/weight flags
are not distribution proof; idle is reported, not blindly passed/failed. No fallback
priority is modified by diagnostics.

The launcher also fails fast rather than rebuilding/consuming shared distribution
files during verification. Prefer a captured immutable artifact with
`-NoBuild -InjectorDirectory <capture>/injector` for an explicitly authorized launch. A direct
MSBuild/UI build that ignores ownership is outside the verifier's safety guarantee.

## Fixture Extension

Manifest v1 supports `allocation`, `early` and `fallback`; `plan` defaults to `24hr`,
and zero-based `stage`/`stageIndex = -1` retains natural selection. The example uses
existing `24hour-ready-test.txt` for allocation/fallback without altering its bytes.
Only confirmed Live executes fixtures; Inspect reports `INSPECTED_NOT_EXECUTED`.

Allocation resets nonbasic Energy/Magic and beards/diggers before the fresh owner,
preserving purchased progression/basic training, then checks native funding/gear/
affordable support. Fallback sets positive-weight NGU targets to their current
positive levels, never changes levels/weights or invents fallback priorities.
Zero-level NGU is unsupported here: native target zero means unlimited, not complete.
Fixture-only changes are not saved as production preferences.

Use genuinely early snapshots for `early`; locked support is inconclusive, not a
fake pass. Live waits for repeated progressing samples up to a bounded settling
limit, allowing ordinary ten-second ticks. Desired/observed equality does not prove
independent cap/ratio math, and limited telemetry remains explicitly inconclusive.

## Evidence Limits

Source-linked bridge/harness tests use real temporary files/queues and substitute
native export/load/lifecycle leaves. Codec inspection never constructs serialized
game types. Compiled session tests and packaged dependency/codec checks remain
distinct from actual Unity/native fixture loading, fresh-owner readiness and final
restoration. See [packaged-codec limits](save-safety-net48-closure-20261023.md).

No generated report, documentation count, scoped PASS or compiled test establishes
full release or long-run live acceptance. The earlier integration failures and
counts are historical; they are not a current unbuildable-project diagnosis.
[Current WIP](../../wip.md) owns remaining acceptance and the intended daily-plan
state; [future work](future-work.md) owns optional scenarios. The newer
[action/session contract](action-state-simplification-20261023.md) overrides retired
replay journals. No live transaction, deployment, mutation or commit was performed
for this documentation update.
