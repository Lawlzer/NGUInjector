# Source Combat Decisions

```powershell
dotnet run --project checks/Combat/CombatChecks.csproj -c Release
pwsh -File checks/Combat/VerifyRegressions.ps1
```

This .NET 8 executable tests production **decisions**, not executor authority.
AdventureRule, SimpleJson and ZoneHelpers are source-linked. SourceProjection uses
the installed BuildTools Roslyn parser to extract the exact combat decision and
read-only adventure-rule methods from the current production files at build time.
The extraction rejects executor types in its output and fails on missing/ambiguous
method names. Generated code lives only under obj.

Combat decisions terminate in record-only move requests with native button/active
predicates extracted from CombatHelpers. There is no Main.Execution substitute,
grant, claim, selected-support, snapshot, restore or successor model. Native route
and native setting boundaries throw if accidentally reached. The harness deliberately
does not compile route producers or advertise grant/native-effect acceptance.

Retained coverage includes all five Walderp enemy types; true/false, unavailable and
malformed prompts; every distinct alternate; shared and individual cooldowns;
time dilation/Red Liquid; repeated timed prompt cycles; survival/regen thresholds;
skill unlocks; buff/defense priorities; ordinary fast/non-fast combat; ordered,
inclusive, unlocked adventure-rule reads; nonfinite telemetry; all titan countdown,
prerequisite, numeric-boundary and exact selected-version dispatch cases. Later
native achieved results are explicit boundary inputs here, not fabricated authority.

VerifyRegressions runs isolated source mutants for prompt inversion, attack
reservation and inclusive rule thresholds. Each must fail behavioral assertions,
not compilation. Production inputs are never edited.

Coupled lifecycle tests moved to the compiled native route selector and TitanGold.
The old fake snapshot/lease producers and their historical-restore assertions were
removed, not patched with always-successful grants. See
`docs/route-combat-review-20261022.md` for the requirement mapping and remaining
acceptance boundaries. Native commands use the fail-fast shared-output verifier:

```powershell
.\verify.ps1 -Scope Native -Suite PresetIntegration -Selector '--route-executor-cutover'
.\verify.ps1 -Scope Native -Suite TitanGold
```
