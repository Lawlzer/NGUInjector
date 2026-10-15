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
