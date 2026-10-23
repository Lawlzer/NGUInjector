# Compiled Gear Checks

Run from the repository root after building the injector:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "NGUInjector.sln" /t:Build /p:Configuration=Release "/p:GameFolder=C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /verbosity:minimal
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "checks\GearEquip\GearEquipChecks.csproj" /restore /t:Build /verbosity:minimal
& "checks\GearEquip\bin\GearEquipChecks.exe" "$PWD\NGUInjector\bin\Release\NGUInjector.dll" "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
```

The runner loads the production injector unchanged and executes both preserving APIs, the legacy actuator, and the existing gear planner/search suites. Native equipment bonus conversions, Character capacity getters, InventoryController swap capacity guards/rollback and Inventory data swaps remain real. In-memory game substitutions disable rendering, supply fixed external progression multipliers/slot availability, and count all three recall boundaries. No game DLL is rewritten on disk and no live save is accessed.

Checks cover a real successful no-recall swap, unchanged basic/AT/NGU/augment/TM/Wandoos/blood/wish/hack allocations and E/M/R3 totals/idle, current capacity overflow, native/projection mismatch, unsafe intermediate accessory staging with safe final capacity, unsafe final maps, idle-only retention, loadout locks, cached Manual Adventure optimization, and no-op behavior in both APIs. Changed legacy swaps still reach all three recall boundaries. Live Unity rendering/scheduling remains outside these checks.

`EquipGearPreservingResources(ih[] gear)` returns null on success or a failure string without recalling resources. Like the optimizer's ordinary equip mode, input order assigns weapon/accessory destinations; it does not request saved-slot restoration. `OptimizeAndEquipPreservingResources(goal, mode)` returns the usual `GearOptimizerResult` and respects the loadout lock. Both reject unverifiable or insufficient capacity rather than recalling to make room. Current `cur*` totals include idle; the guard conservatively retains the larger of `cur*` and `idle*` for each resource and checks every simulated operation before mutation.
