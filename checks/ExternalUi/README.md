# External UI Bridge Checks

Run from the repository root on Windows with a .NET SDK capable of targeting
.NET 8 and the .NET 8 runtime/reference pack installed:

```powershell
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj
```

No NuGet packages, game assemblies, injection tools, or machine-specific paths
are required. Build outputs stay in this project's `bin/` and `obj/` subdirectories.
Every run uses a unique child of `Path.GetTempPath()` and removes it during
cleanup. The program returns 0 on success and 1 on assertion or cleanup failure.
Non-Windows execution fails explicitly because these checks rely on Windows
file-sharing and replacement semantics; assertions are not silently skipped.

The checks cover strict JSON, numeric precision, atomic writes,
lifetime leases, readiness, command claims/no replay, expiry, settings patches,
watcher pumping/retries, plan revision conflicts, normalized reads, unknown-field
preservation, history, plan management, optimizer dispatch, and shutdown failures.
Audit regressions additionally cover settings patch/reload invalidation, no-op and
unselected plan saves, native/patch/load setting ranges, Iron Pill disk history
retention and read-only ownership, positive-Int32 command estimate overrides,
and blood/Iron Pill metadata availability, blockers, and independent errors.

The project links the actual production lifecycle, loader, bridge, watcher pump,
JSON helpers, settings, plan models/store, blood-estimate model, and Iron Pill
eligibility policy. It does not
copy those implementations. `Stubs.cs` supplies Unity objects, scene setup,
forms, action/optimizer boundaries, enum stand-ins, and a reflection-based
`JsonUtility` substitute using built-in `System.Text.Json`. Unused game-state
fields intentionally start unset, so CS0649 is suppressed.

These are executable bridge/persistence regression checks, not proof of live
Unity startup, serialization equivalence, gameplay behavior, optimizer quality,
real Unity coroutine cancellation, WinForms event delivery, or Rust/frontend
integration. No game process is started or accessed. Update boundary stand-ins
when the corresponding production game-facing contracts change.

## Preset Contracts

The current editable weighted authority is the runtime folder's `presets.json`,
shared by Dashboard Quick, Presets and plan/stage references. Plans use
`ActivePresetId`; stages use a global `PresetId` or `@active`. Historical plan
`LoadoutPresets`, `GoalPresets` and `ActiveGoalPresetIndex` are retained provenance,
not another editable library. NGU ratios belong only to global settings; imported definitions
use durable migration receipts, and existing inline stages retain their original
cap/overflow behavior until explicit conversion.

`LoadoutPresetChecks.cs` links the production model, plan store and global library
store. Its assertions cover six/ten/twelve weight compatibility, strict
`AutoUpdate` booleans (missing defaults true, explicit false retained),
preset-owned five AT ratios, retired E9/M7 NGU field removal, exact unknown JSON preservation,
historical-array provenance, migration and revision-aware persistence. The first
ten weights are production objectives; positions 10/11 add immediate Adventure
power/toughness. Historical arrays keep source definitions rather than receiving
modern global defaults, and external plan saves cannot edit that provenance.

`LoadoutPresetBridgeChecks.cs` exercises the retained inline v2
`previewLoadoutPreset` / `applyLoadoutPreset` dispatch with substituted game
adapters: readiness, argument validation, no replay and runtime/result forwarding.
Those inline calls are not global ID/revision execution. The bridge fixture does
not execute the production numeric optimizer or global session cache and cannot
prove live Equip, Auto-update re-equip behavior or stage inheritance. See
[frontend contracts](../../docs/frontend-global-presets.md),
[library check source](../PresetLibrary/Program.cs) and
[compiled adapter checks](../PresetIntegration/README.md) for their separate scope.
Auto-update means re-optimize on equip, not a timer; editing/saving never applies
a setup. This documentation pass did not run any of these suites or live actions.

## Max ITOPOD Push

`MaxItopodPushChecks.cs` checks real dispatch for `startMaxItopodPush` and
`cancelMaxItopodPush`: integer limits 1 through 1000, integral numeric tokens,
missing/null/wrong-type/extra arguments, range rejection, no replay, and boundary
exceptions. It verifies immediate post-action `runtime.maxItopodPush` in compact
acknowledgements and full runtime files, plus heartbeat/status forwarding of
independently supplied live state. Command detours under modes 0..2 do not persist
settings; cancelling a pending or adopted mode-3 run also disarms that selection.

Program checks additionally gate both commands before readiness, advertise them,
and unload with an active push. The real lifecycle must cancel it before disposing
resources and publishing terminal runtime, exactly once even across repeated
unloads. Only `StartMaxItopodPush`, `CancelMaxItopodPush`, and
`GetMaxItopodPushRuntime` are action boundaries in this project; gameplay and death
accounting are tested against the real source-linked implementation in Combat.

## Durable History

`IronPillHistoryChecks.cs` runs the actual `SavedSettings` save/load/patch methods
with the `JsonUtility` boundary in both normal mode and optional
`OmitCustomIronPillHistory` mode. Omission applies to both serialization and
deserialization, defaults off, and is restored in `finally`; the rest of the
bridge checks retain their normal Unity substitute. Before the persistence fix,
omission reproduced a successful in-memory append with no history array on disk
and no persistence exception.

History is now backend-owned SimpleJSON data, not dependent on Unity custom-array
support. Saves explicitly emit `_customIronPillHistory`, including `[]` when empty.
Load parses the original JSON directly and strips that field from the Unity
deserialization payload. Patches copy the latest live history into their candidate,
including unsaved entries, rather than reconstructing it from Unity or stale disk
state. Existing atomic-write-before-publish behavior, read-only history patch
protection, unrelated unknown fields, and exact unknown numeric tokens remain intact.

New automatic records require finite, nonnegative wait/rebirth seconds and worth
within the inclusive game range `0..1e8`. Invalid appends fail before changing
memory or disk. Loading skips malformed/out-of-range rows and retains the first ten
valid records in stored newest-first order; timestamps are strings preserved
verbatim, with null in-memory timestamps written as empty strings for older entries.
Missing/null history defaults empty. A non-array history container or non-finite JSON
refuses the reload before publishing settings. `MassUpdate` filters invalid/null
records and also retains at most ten.

The regression covers Add/Save/reload/MassUpdate/ApplyPatch round trips, all four
record fields, newest-ten retention, unsaved-live versus stale-disk history, stale
source-JSON replacement, unknown-field retention, invalid row/type/range filtering,
zero/worth-cap/max-finite numeric boundaries, failed atomic writes, watcher baselines,
and unchanged null-save-path/disabled-save guards. All files are fixture data under
the suite's temporary directory. No live cast is reconstructed or inserted.

## Auto EXP Settings

`AutoExpSettingsChecks.cs` also exercises production `SavedSettings` with the existing
reflection JSON boundary temporarily zeroing missing serialized fields, rather than
relying on constructor initializers that Unity `FromJson` does not apply. It covers
the disabled legacy default and all eight default ratios, all-nine-field patches,
save/reload/MassUpdate, inclusive ratio bounds 1 and 1000000000, unknown-field and
unsaved-live-setting preservation, and atomic rejection of invalid ratios or
non-boolean toggles. The boundary option is restored in `finally`.

## Adventure Settings

`AdventureSettingsChecks.cs` exercises production settings with missing Unity field
initializers disabled: the default death limit is 3, legacy modes 0..2 retain their
values, and legacy death limits fall back to 3 or cap at 1000. It covers mode 3,
patch/save/reload/MassUpdate, native death-limit bounds 1..1000, runtime settings
projection, exact unknown-data preservation, unsaved live settings, and atomic
type/range/fraction rejection. A locked settings file forces the native mode-zero
fallback save to fail; memory and disk must retain mode 3 until a successful retry.
The JSON boundary option and global settings reference are restored in `finally`.

## Runtime Blood Metadata

These additive fields are always emitted in `runtime.json`; `protocolVersion`
remains 3. Status values below are exact, lowercase tokens. `reason` is an empty
string on success and human-readable text otherwise, not a machine-stable code.

| Field | Type / Meaning |
| --- | --- |
| `bloodGoals.available` | Boolean; both goal estimates could be read/calculated. Does not mean spells are on or production is positive. |
| `bloodGoals.status` | `"available"`, `"unavailable"`, or `"error"`. |
| `bloodGoals.reason` | Empty when available; missing-dependency explanation when unavailable; logged error message when calculation throws. |
| `bloodGoals.spaghetti`, `bloodGoals.counterfeit` | Goal object when available, explicit `null` otherwise. |
| `ironPill.available` | Boolean; eligibility could be evaluated with the injector, settings, plan store, rebirth time, Blood Magic, and blood-spell state present. |
| `ironPill.eligible` | Boolean when available, explicit `null` on unavailable/error. |
| `ironPill.status` | `"saving"`, `"eligible"`, `"blocked"`, `"unavailable"`, or `"error"`. |
| `ironPill.reason` | Empty when eligible; first human-readable policy blocker when blocked; dependency/error explanation otherwise. |
| `ironPill.reservingBlood` | Boolean when available; whether the magic-only saving overlay is active. |
| `ironPill.savedSeconds` | Elapsed saving-window seconds, or zero outside a window. |
| `ironPill.prepLimitSeconds` | Hard window deadline: `max(900, configured saving seconds) + 60`. |
| `ironPill.retryInSeconds` | Remaining restart guard/backoff, or zero. |

Each goal object retains exactly these existing fields: `goalPercent` (integer),
`currentPercent` (number), `requiredBlood` (number or null), `remainingBlood`
(number or null), `bloodPerSecond` (number), `estimatedSeconds` (number or null),
`status` (human-readable calculation/optimizer status), and `productionAssumption`
(human-readable optimizer assumption). Individual paused, disabled, reached, or
unestimable goals do not make the outer `bloodGoals.available` false.

Iron Pill eligibility uses the same predicate as automatic reservation: no
manual rebirth/pre-rebirth prep/boss push, custom plan running, global custom
auto-rebirth enabled, selected plan auto-rebirth enabled, automatic blood spells
enabled, root plan `AutoIronPillEnabled` true (default when omitted), actual rebirth elapsed time at least 3600 seconds, and boss stage at
least 100. Ready automatic rebirth also blocks reservation. The existing auto-rebirth coupling is intentionally unchanged.
Eligibility is not cast readiness: cooldown, saving thresholds, blood minimum,
and successful-cast checks still govern reservation/casting separately.

The built-assembly BlackBar suite covers the real reservation/readiness policy and
progress wrapper. A saving plan remains `planProgress.status == "running"`,
with `bloodReservedForIronPill` and overridden active-row `magicPriorities`;
configured arrays and inactive rows are not rewritten. This source-linked metadata
suite leaves the unavailable runtime reservation/readiness boundaries inactive.

Plan opt-out checks cover default/legacy true, normalized reads without disk writes,
native/CAS saves and copies retaining false, strict boolean validation without partial
writes, and the shared eligibility/reservation gate without disabling automatic blood spells.

Blood-goal and Iron Pill errors are logged and represented independently; a
failure in either query does not omit the other object. These queries do not
allocate resources, toggle spells, reserve blood, or cast anything.

Saved setting patches reject out-of-range integers: cube power ratio 1..4,
augment/blood estimate seconds 1..100000, and both blood goal percentages
0..100000. Native setters clamp to those ranges; legacy loads normalize bounds
while retaining existing missing/invalid-low defaults. The `expectedSeconds`
command override remains an integer in 1..2147483647.
