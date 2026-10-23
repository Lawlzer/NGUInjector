# Test-All And Save Safety

## Status

Implemented: offline test orchestration, read-only diagnostics, verified local native
backups, decoded longest-playtime selection, guarded recovery to a newer save, fixture
registration/inspection, and the global desktop save-safety warning.

**Destructive live fixture testing is deliberately BLOCKED.** There is no claim that
loading `24hour-ready-test`, clearing allocations, running its stages, and restoring
the original game is implemented or live-verified. A JSON report never counts an
unexecuted fixture or an unverified restoration as a pass.

Native build 1260 independently autosaves to local files and Steam Cloud, awards
Steam achievements, and applies offline progress during loading. Mod autosaves and
automation action/checkpoint files add other writers. Reloading a native save is not
an exact transaction rollback. These findings were established by offline IL inspection,
not by diagnostic injection into the game.

## Commands

Run from the repository root with PowerShell 7:

```powershell
# All offline checks. No live bridge access, game launch, or injection.
.\test-all.ps1 -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'

# Read-only observations of the currently running injector.
$report = .\diagnostics.ps1 -ExpectedPlan '24hr'
$report.checks | Format-Table sample, id, status, message -Wrap

# Explicit live preparation: fresh LOCAL backup, full save scan, then recovery
# to a greater-playtime save if necessary. Requires the rebuilt injector.
.\test-all.ps1 -Mode Prepare -ConfirmLiveSaveLoad `
    -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'

# Register and safely decode one or more fixture saves without loading them.
.\test-all.ps1 -FixtureManifestPath '.\test-fixtures.example.json' `
    -GameFolder 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE'
```

`Offline` is the default mode. `Prepare` captures the current game before choosing
any recovery save. Without `-ConfirmLiveSaveLoad`, discovering a longer-playtime save
retains the backup and returns BLOCKED without loading. An already-current game is
not needlessly reloaded or given a new scheduler entry.

`-SkipOfflineChecks` is available for bounded diagnostic/preparation work, but its
result is always incomplete/BLOCKED, never a full test-all pass. `-Mode Live` performs
preparation, then returns BLOCKED at the fixture-isolation gate before any old fixture
load. It does not enable a plan, reset allocations, or waive unfinished stages.

Exit codes: `0` selected mode passed, `1` failure, `2` blocked/incomplete. Reports and
phase logs are written under a new `%LOCALAPPDATA%\Temp\opencode\ngu-test-all-*`
directory. The parent must exist. Reports record native command IDs/outcomes,
protected backup identity, fixture inspection, individual phases, and restoration
status. Timeouts are UNKNOWN outcomes; commands are not replayed.

The offline gate runs UI verification with browser coverage once, then all 13 native
executor suites independently, even if UI verification failed. Save codec, bridge,
packaged net48 dependency/codec, diagnostics, and harness checks run separately. The
packaged check byte-loads the plugin without a sibling probing path and checks all
nine embedded dependency resources and repeated assembly-resolution identity.
Existing `verify-ui.ps1` can still stop
its own later source checks on an earlier UI failure; this is reported, not hidden.

## Save Validation

Protective backups use the game's native encoded export, not adjacent diagnostic
JSON or a copy of an existing autosave. Capture temporarily stamps the export's native
`lastTime`, restores the live timestamp, flushes a new unique file, reopens it, verifies
its SHA-256, and decodes those exact bytes. Exported total playtime, rebirth number,
rebirth age, and player name must match the current game.

`captureLocalSave` does **not** request a cloud write. Backups and manifests live in
`%USERPROFILE%\Desktop\NGUInjector\save-safety\backups`. Nothing overwrites or prunes
them automatically.

The safe decoder uses `System.Formats.Nrbf`, never `BinaryFormatter.Deserialize` or
the game's deserializer during inspection. It verifies the native checksum, required
record types/fields, supported version `361..1260`, finite nonnegative clocks, and
`rebirthTime <= totalPlaytime`. The file limit is 32 MiB. Inspection does not construct
game or mod objects. The native loader is used only after a local candidate has been
validated and pinned; imported saves should still be trusted local files.

Discovery is nonrecursive and bounded to 4,096 files across the game's native save
directory, injector `NGUSave.txt`, and protective backups. Other disks, nested mod
profiles, arbitrary backups, and Steam's opaque remote cache are not claimed as scanned.
The runtime compares against the **longest known decoded save**, not every save that
could exist elsewhere. Background inspection processes one file per frame and
rediscovers changes about once a minute. Destructive authorization forces byte-based
inspection of every candidate; timestamps optimize display only.

The ranking field is `PlayerData.totalPlaytime.totalseconds`. File modification time,
filename, current rebirth age, NUMBER, and injector uptime never determine the winner.
Different hashes tied for maximum playtime require review. Equal copies are not
ambiguous. Cross-player loading is refused. Longest-save loading rechecks the session,
selected hash, candidate contents, native outcome, rebirth, and clocks; automation's
normal selected-policy review requirements remain in effect afterward.

Known maxima and unresolved equal-playtime branches are retained in `high-water.json`,
including across reinjection. A lower replacement, missing file, failed read, or failed
evidence write cannot silently erase a previously observed higher progression.
Preparing/loading/completion journals use flushed content and atomic publication,
retaining previous evidence. This is not a guarantee against every power/storage fault.

## Warnings And Recovery

The desktop app shows a global, non-dismissible red alert when the loaded game is
behind a known save or recovery is unresolved. It includes loaded/longest hours and
the known save path. Incomplete inspection, ambiguous branches, invalid metadata,
and unavailable older-backend telemetry remain visible amber warnings. Missing
telemetry uses an accessible status region; actual reported safety issues use alerts.
Disconnecting does not erase a previously reported problem.

The injector's manual quicksave first retains a verified local backup and refuses
quicksave/cloud publication when its safety check is not verified. This gate does
**not** intercept the native game's or other mods' independent autosaves/cloud calls.

A failed or interrupted guarded load keeps ordinary injector execution quiesced,
including after reinjection. Status remains observable, but no automatic uncertainty
clear, checkpoint reset, rebind, or rollback is performed. F7 cannot bypass a retained
recovery interlock. Recovery requires operator review of the retained journal and
native saves; an unattended recovery/fixture transaction is not yet available. Do not
delete evidence merely to silence a warning.

Total playtime is a regression witness, not an immutable save identity. Matching or
higher clocks cannot prove that every acquired choice or irreversible action belongs
to the same branch. Native loading can also alter state and add offline progress.

## Diagnostics

`diagnostics.ps1` reads only existing runtime JSON. Optional report output must be a
new path outside the runtime folder. It does not create markers, enqueue commands,
optimize, change settings, save, or load the game.

It samples actual desired/observed resource allocations using exact integer strings,
domain ownership, gear instance verification, beard/digger support and affordability,
native settings, NGU zero exclusions, idle pools, plan identity, session continuity,
and save safety. Results use PASS, FAIL, WAITING, INCONCLUSIVE, or UNCONTROLLED.

Independent NGU ratio/cap verification is explicitly INCONCLUSIVE where current
weighted-preset caps/budgets are not published. Historical buffered caps and old
"weights applied" flags are not evidence of current distribution. Idle resources
are reported, not blindly treated as failure or proof of productive fallback work.
No fallback priorities are modified by these scripts.

## Fixture Extension

`test-fixtures.example.json` registers the existing 24-hour-ready save. Add named
early-rebirth, AT, capped-NGU, scarce-resource, and fallback cases as real snapshots
become available. The current manifest path/scenario/plan/stage fields describe tests;
they do not authorize execution. Registered files are decoded and hash-checked, then
reported as `INSPECTED_NOT_EXECUTED`.

A standalone read-only inspector is also available:

```powershell
dotnet run --project .\checks\SaveSafety\SaveInspector.csproj -- `
    --directory 'C:\Users\Kevin\AppData\LocalLow\NGU Industries\NGU Idle'
```

Before implementing live fixtures, provide an isolated execution environment covering
native autosaves, Steam cache/account effects, PlayerPrefs/mod state, settings/stores,
finite-action ledgers, stage/route/selection checkpoints, and queued commands. Then add
a durable begin/load-fixture/restore/verify protocol. Success must require verified
restoration, not just a successful native load return or a `finally` block. Fixed
fixture tests and disposable current-save tests should share that protocol.

## Verification Notes

The real native save directory was inspected read-only: 407 files decoded without
errors at that observation. `24hour-ready-test.txt` had total playtime
`2283976.2493571` seconds; the greatest on-disk save at that observation was
`NGUSaveSteam.txt`, `2291037.4353386564` seconds. These are historical file observations,
not a claim about the currently loaded game.

Source-linked bridge tests use real temporary files and replace native export/load
and lifecycle boundaries. Codec tests never load serialized types. Browser coverage
uses mocked IPC on desktop/mobile. See `save-safety-net48-closure-20261023.md` for the
separate packaged codec/dependency smoke and its explicit Unity Mono limits.

Focused checks passed: 81 save-bridge cases (1,391 assertions), 1,419 codec assertions,
93 diagnostics cases, 54 orchestration cases, 78 warning-component tests, and 16
desktop/mobile warning browser cases. The packaged net48 byte-load check also passed
on the real fixture without loading game or Unity assemblies. Production compilation
passed with one existing unused-field warning.

The full offline gate was actually executed and remains **red**. Its last browser
run had 872 passes, eight failures, and four skips. After fixing the save-safety
telemetry construction regression, the native follow-up passed PresetIntegration
(18,865 assertions) and Ownership (10,321 assertions/486 scenarios); CombatPrepIntegration
and BlackBar still failed support/timing/ownership and gear/boost checks. These are not
a full release pass. Retained evidence:

- Full report: `%LOCALAPPDATA%\Temp\opencode\ngu-test-all-20261023-173850-0a0d14610dbf4a7bb87bfaf56aa95693\report.json`.
- Native follow-up: `%LOCALAPPDATA%\Temp\opencode\ngu-save-safety-native-20261023-telemetry-7d8f4d842bd04ab0a62814e7f2044a5b.log`.

No live game commands, real backup/export calls, save loads, deployment, injection,
or restarts were performed during this implementation. Real saves were inspected
read-only; native export/load behavior was exercised through the offline boundaries.
