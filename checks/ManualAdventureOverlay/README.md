# Compiled Manual Adventure Overlay Checks

## Current source status — iteration 9 (validation pending)

Latest independent iteration-8 evidence: fresh production/preset/overlay builds PASS,
MVID `7aeaf1c5-05e6-4fca-bd40-3054fc5b571f`; all 11 overlay groups/277 assertions PASS.
Native R3 remains 731 total / 431 idle / 300 wish, with real capacity dependencies.
These are prior-source results, NOT validation of iteration 9.

Selected gear now enters `ApplySelectedSetup` with no recalls, retaining acquired
instances separately from the applied overlay cache. The selected-loadout timeline
additionally invalidates application state and asserts no new optimizer search or
swap. B acquisition now composes retained gear and additive support before the first
shared mutation. Current requirements/policy are separate from receipt observations;
historical receipt adoption/exit flags and full-capture exit replay are deleted.
The successor case additionally acquires a genuinely different native NGU head B
under a combat head claim, records each actual swap, checks zero B flash/A detour,
cached AutoUpdate-false zero-search replay, then direct B exit and retained R3 work.
All new assertions are UNRUN. Scheduled B-transition timing and expanded native gear
failure/foreign/permission timelines remain incomplete.

Both this runner and the full preset runner now use the same fresh-Character
`Program.Setup` hook, `PrepareNativePresetFixture`: native selector/player/enemy/log
dependencies are constructed and the retained native zone-entry model-write slice
enters safe `-1`. `PrepareSupport` does not initialize or reset routes; deliberate
missing-selector and unknown-active-route negatives remain rejection tests. The
resource fixture no longer repeats safe entry after preparation. Failure groups
now print isolated read-only probes for zone, support outcome/continuation/conflict,
current acquired baseline, receipt, resources, search/capture/recall/swap counts and
stage entry/deadline/credit. Unavailable probes do not mask the original failure.
The shared initialization fixes are preserved. Removal retires only the observed
manual route; ordinary acquired-policy refresh verifies funding before activation.

The support-successor timelines have the partial independent evidence above. Pass
counts/live observations below describe older implementations, not iteration 9.
The runner now adds real baseline-definition A -> overlay -> reacquired B -> exit,
no historical pre-acquisition support write, AutoUpdate-false cached B reacquisition
without replacement search, native partial/render interruption,
foreign support and permission-loss callbacks, reset-retained conflict evidence,
fresh explicit reviewed acquisition, and uncontrolled removal without restoration.
Reflection assertions now inspect current `BaselineSupport` intent rather than the
deleted overlay support snapshot. Existing gear coverage remains; this slice migrates
the gear ingress but does not prove scheduled stage-transition timing.

This standalone runner reuses the existing PresetIntegration/CombatPrepIntegration fixture sources unchanged. It loads a freshly built production injector and an in-memory Cecil-isolated game assembly. The three production overlay methods, preset acquisition, support projection and exact native support application are not replaced. Existing fixture substitutions cover rendering, settings persistence, recalls, slot availability and unrelated external getters; see [the fixture boundaries](../PresetIntegration/README.md). No injection, live game actions, installed DLL writes or save access occur.

```powershell
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$game = "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE"
& $msbuild NGUInjector/NGUInjector.csproj /t:Rebuild /p:Configuration=Release "/p:GameFolder=$game" /v:minimal /nologo
& $msbuild checks/ManualAdventureOverlay/ManualAdventureOverlayChecks.csproj /restore /t:Build /p:Configuration=Release /v:minimal /nologo
& checks/ManualAdventureOverlay/bin/ManualAdventureOverlayChecks.exe NGUInjector/bin/Release/NGUInjector.dll "$game\NGUIdle_Data\Managed"
```

The original six groups execute real private methods through reflection:

- Keep gear and zero bonuses, with and without an acquired base preset: unchanged active support, flags, all inactive levels, allocation state, E/M/R3 totals and idle amounts; no recalls, swaps or exact support transaction.
- Selected loadout 1: nonzero Push/Drop bonuses reach the real planner with fight mode 1 and unchanged support. The real preserving actuator equips a combat head via the shared native Inventory boundary. An immediate gear-weight-only edit changes the key, invokes one new search and swaps to a competing loot head without expiring a retry timer. Both successful selections then cache. Actual allocations (including a funded R3 wish), E/M/R3 totals/idle, saved/acquired base definitions, receipt/policy identity and applied history remain unchanged; base receipts remain valid, E/M recall counters stay fixed and exactly two gear swaps execute.
- Additive support: acquired digger weight 3 plus bonus 2 beats base weight 4; beard priorities retain base contributions instead of becoming bonus-only. Repeated minute refreshes and smaller modifiers use the original baseline, never compounded overlay state. Changed modifiers must bypass the minute cache.
- Zeroing modifiers: resolve the current acquired beard/digger selection immediately.
- Disabling the overlay: current-successor support and idempotent reset without recalls or gear swaps.
- Reset ownership guards: changed epochs and user-edited support never receive stale restoration. Disable while either loadout or digger lock is held retains the baseline, applied selection, epoch and foreign lock across retries; releasing the lock restores the exact baseline once. While active, changed bonuses and expired refreshes report a pause without overwriting or adopting foreign support.

Saved library definition, acquired execution definition and applied history are compared unchanged. R3 assertions cover actual positive total/idle sentinels, a wish allocation and native capacity preflight; they do not claim full R3 allocation-controller coverage or an independent R3 recall counter. The shared fixture substitutes the InventoryController head-swap boundary with the game's native Inventory swap plus bonus refresh, not the production planner, preserving actuator, swap planning or capacity preflight. The selected-loadout case initializes neutral late-wish rows required by native R3 cap getters before acquiring its base preset. Combat's lightweight recording stubs separately verify dispatch and preserve legacy manual-rule cache/retry checks; they are not evidence for actual overlay correctness.

## Verification

The following results are historical; rebuild and run the current source before
accepting this source. No current build/assertion total is claimed. The latest
independent iteration-8 result is 11/11 groups and 277 assertions. Rebuild before
accepting iteration-9 composition/deletion changes.

An earlier expanded runner passed 191 compiled overlay assertions across six groups, including actual planner/preserving-actuator coverage. Its UI/native and 46-case browser results are historical, not verification of the current tree. Use `./verify.ps1 -Scope Native -Suite ManualAdventureOverlay` for current scoped verification, or `./verify.ps1` for the full offline gate.

Live verification on 2026-10-19 also observed a Keep-gear toggle retaining the running 24hr plan, unchanged gear/support and funded Time Machine energy/magic lanes. Allocations continued growing across the toggle rather than returning to idle. A manual max-ITOPOD push ran with a three-death budget; exhausting that budget returned the saved mode to Optimal ITOPOD. Live snapshots are observations, not a guarantee that a later push is still active; check current `runtime.maxItopodPush` for that state.

The earlier cache-key regressions are fixed in production: numeric arrays are now explicitly serialized in the gear and support keys. Immediate bonus edits and zeroing modifiers pass without expiring the refresh deadline. Pending disable cleanup and active foreign-support protection are exercised against the production overlay, not the lightweight recording boundaries used by source-linked beard/focus harnesses.

The compiled timing integration separately distinguishes legacy manual-rule ownership (blocks stage credit for gear-owning inline stages) from explicit Manual Adventure overlay ownership (preserves eligible applied training credit). No production files are patched to satisfy these checks.
