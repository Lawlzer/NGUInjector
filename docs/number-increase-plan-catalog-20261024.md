# NUMBER Increase Plan Catalog

The built-in catalog is exactly `24hr`, `15m-NUMBER-increase`,
`30m-NUMBER-increase`, and `1h-1m-NUMBER-increase`. Empty plan names and
default/deletion fallback selections resolve to `24hr`.

Existing built-in files are never repaired or regenerated during initialization
or catalog listing. In particular, saved `24hr` bytes, configuration, history,
and protected scheduler checkpoints remain unchanged, including legacy files
with omitted arrays. Fresh `24hr` creation retains its original schedule.

## One-Time Retirement

Store initialization retires only `default.json`, `infinite.json`, and
`1h-3m-rebirth.json`. Originals are copied byte-for-byte to the runtime sibling
directory `retired-custom-plans-number-v1/`, outside `custom-plans/`.
If settings select a retired name, `settings.json` is also backed up and the
selection is persisted as `24hr` before any obsolete file leaves the catalog.
Run permission, global NGU ratios, and other settings are not intentionally
changed. The existing settings CAS path protects a loaded legacy selection.

After snapshot backup and selection persistence, each obsolete pathname is
atomically moved without overwrite to its backup path plus `.retired`. That
moved object is verified against the snapshot and retained, never deleted by
pathname. If an external atomic replacement races the move, both versions
remain preserved, and a later catalog replacement is never deleted. A moved
archive is also a per-file receipt: retry after interruption skips that name,
including any replacement created after the move.

Backup, selection-persistence, or byte-verification failures abort retirement
without discarding unbacked data. A differing existing backup is never
overwritten. Successful retirement writes `completed.json` in the backup
directory. Subsequent store construction and listing do not repeat retirement,
even if a user later creates a plan with one of the retired names.

Legacy `infinite` and `1h-3m-rebirth` templates remain only for deserialization
of shipped documents with omitted arrays/defaults, not catalog creation or
deletion protection.

## NUMBER Plans

| Plan | Rebirth Time | Prep Start | TM | Augments | Wandoos | Final Augments | Hold |
|---|---:|---:|---:|---:|---:|---:|---:|
| `15m-NUMBER-increase` | 900 | 900 | 210 | 210 | 420 | 60 | none |
| `30m-NUMBER-increase` | 1800 | 1800 | 435 | 435 | 870 | 60 | none |
| `1h-1m-NUMBER-increase` | 3660 | 3600 | 885 | 885 | 1770 | 60 | 60 flexible |

All times are seconds. These plans require both time and a NUMBER multiplier
of `1.000001`, retain normal NUMBER confirmation/protected-work safety, enable
automatic blood spells with spaghetti/counterfeit goals of zero, disable Iron
Pill, and allow fruit preparation without requiring maxed fruit.

The 15/30-minute stages remain entirely fixed. The 61-minute plan has 3600
seconds of protected fixed work followed by a weight-one flexible hold
(`DurationSeconds = 0`, `Percent = 1`). Its remaining horizon funds 60 seconds
of Augments/Blood without blocking `ProtectedWorkComplete` at 3600; the time
trigger still prevents rebirth before 3660. This is a fresh-factory change only:
previously saved 61-minute plans are never automatically rewritten.

All phases use standard inline allocation and Blood Magic focus. Energy chains
are `TM,WAN,AUG`, `AUG,WAN,TM`, and `WAN,TM,AUG`. Magic chains start with `RIT`,
then `TM,WAN` except Wandoos uses `WAN,TM`. Explicit chains contain no NGU and
remain inline across preset-library migration, clone, save, and restart.
Gear follows TM/Augments/Wandoos. The first three phases enable
`UseWandoosWhenTimeMachineLocked`; final Augments and the 61-minute hold do not.
No phase opts into `AutoAugmentsWandoos` or fight-on-entry.

## Timing Handoff

`CustomPlan.RebirthPrepStartSeconds` is an additive schema-17 nonnegative Int32
field. Missing values read as zero, preserving readiness-based behavior for
existing plans. External and native persistence reject invalid values rather
than clamping; exact whole decimal/exponent JSON numbers are accepted.

`Main.CustomPrep.cs` / `Main.AutomationLoop.cs` use positive values to begin
all-fruit prep at elapsed rebirth age after
`ProtectedWorkComplete(plan)`, before the fight and independently of final
NUMBER/time-trigger readiness, and retain completed prep until rebirth.
Final Augments finish before prep/fight starts. Interruptions or late activation
can delay the fight and rebirth; elapsed time does not fabricate applied stage
credit. The 61-minute flexible hold does not postpone prep until minute 61.
Both editors retain the prep time and locked-TM fallback field.

Ordinary harvesting reserves fruit for pending preparation so it cannot harvest
power fruits for seeds before prep force-eats them. Completed preparation starts
a fresh NUMBER confirmation window, including the existing 24-hour plan, without
replaying fruits or bosses while waiting for confirmation.

## Source-Linked Checks

Run under the shared `Local\NGUInjector-Executor-Offline-Verification` mutex:

```powershell
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj -- --plan-catalog
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj -- --plan-blood
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj -- --adventure-dynamic-weight
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj
dotnet run --project checks/PresetLibrary/PresetLibraryChecks.csproj
```

These fixtures use temporary runtime directories and stub game boundaries.
Historical ExternalUi tests explicitly seed a legacy `default` fixture after
initialization instead of depending on production to recreate it.

## Verification And Deployment

The final source-linked ExternalUi suite passed 10,155 assertions and the preset
library suite passed 5,667 assertions. Production Release builds succeeded.
On production MVID `a4b89f19-cc40-4209-b60d-6855c999da58`, the fruit/timing native
selector passed 2,058 assertions, locked-TM fallback passed 330, full combat prep
passed 12,587, and full manual adventure overlay passed 297.

Frontend unit tests passed 2,253 tests; Svelte check reported zero errors/warnings.
Rust passed all 189 tests with `--test-threads=1` after an initial parallel run
hit a temporary-file atomic-publication access-denied error in an existing stage
override test. The focused desktop/mobile plan-library browser check passed.

The complete native release gate is NOT green: PresetIntegration stops in
`StageEntryHandoffChecks` on its reflection lookup of `CombatPrepAlternative.Phases`,
and BlackBar reports stage-support and preparation-retirement failures. The first
full attempt also exposed missing shared fixture includes for the new fallback
checks; those includes were fixed and both affected complete suites then passed.
Logs are under `C:\Users\Kevin\AppData\Local\Temp\opencode`:
`number-plans-full-native-20261024.log` and
`number-plans-linked-native-20261024.log`.

No live plan files were changed and no game/injector restart or deployment was
performed. Catalog retirement and creation run when a verified updated injector
is deployed. The currently running older injector still recreates/protects its
old built-ins, so deleting its files directly is not a durable deployment path.
