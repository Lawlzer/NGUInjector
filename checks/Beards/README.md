# Beard Checks

```powershell
dotnet run --project checks/Beards/BeardChecks.csproj -c Release
```

Source-links current `BeardManager.SelectBeards`, `ActivityPolicy`, the actual
embedded registry and its parser. Only native observations are supplied by the
game boundary. Selection must not mutate native support, levels, currency or UI.
The older controller-shaped boundary's writer counters are only tripwires in this
executable. ActivityPolicy integration now reuses the SustainedFocus source executor.

## Selection Contract

```csharp
int[] SelectBeards(IEnumerable<int> requiredBeards, bool randomize,
    int[] hardRequired = null);
```

- Hard prerequisites precede core and optional utility, are deduplicated, and must
  be unlocked and fit all slots. Invalid, unavailable or over-cap hard sets reject
  rather than truncating.
- Without hard prerequisites, unlocked core `1,3,5,6` precedes optional requests.
  Gold requires seven normal Troll completions, not merely seven existing rows.
- Optional requests are filtered and deduplicated in caller order. Remaining
  unlocked slots are filled without duplicates.
- Non-random selection maximizes current-set retention, then retained Fu/Gold.
  Random selection excludes the current set whenever a legal alternative exists.
- Unavailable native context rejects before the native capacity getter. Available
  getter failures propagate. Zero capacity selects empty; negative capacity rejects.

## Coverage

The independent legal-subset oracle covers capacities 1-7, all 16 optional/Gold
request masks, every current set within capacity, both randomization modes and
Fu/Gold retention tie breaks. Repeated minute-like inputs cover locked/unlocked
Gold and possible/impossible swaps without relying on particular random draws.
Hard prerequisites exhaust all 128 subsets, capacities 0-7, both unlock states and
both randomization modes, including duplicate requests, unavailable and illegal IDs.

There are no calls to removed `EquipBeards`, `RotateBeards` or `EnableFuManchu`.
Exact mutation, retry, cadence, foreign/epoch and persistence tests belong to the
real selected executor in SustainedFocus, not a replacement manager workflow.
See [Support's migration map](../Support/README.md#obsolete-check-map) for the old
Gold/Fu/snapshot assertions, native-actuator boundary and remaining gate blockers.
No Unity process, live save, installed assembly or production build is touched.
