# Ownership Integration Checks

Offline .NET Framework 4.8 regression runner for R01-R04, R23-R28 and ownership follow-ups R30/R31/R33. See the [historical review](../../docs/AUTOMATION_INTEGRATION_REVIEW.md) and [current improvements](../../docs/AUTOMATION_IMPROVEMENTS.md).

The runner loads a freshly compiled injector and a Cecil-modified, in-memory copy of the installed game assembly. It source-links the existing Combat Prep fixture helpers without running that fixture's test list. All files it creates at runtime are under `%TEMP%/opencode/OwnershipChecks-*` and are deleted on exit. It never attaches to a process, invokes the bridge, writes installed assemblies, changes a save, or starts an injector.

## Run

```powershell
$msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$managed = "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed"
& $msbuild "NGUInjector.sln" /restore /t:Build /p:Configuration=Release /v:minimal /nologo
& $msbuild "checks\Ownership\OwnershipChecks.csproj" /restore /t:Build /p:Configuration=Release /v:minimal /nologo
& "checks\Ownership\bin\OwnershipChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" $managed
```

Run from the repository root against a freshly built production DLL. The normal solution/project manifest includes the shared helpers and embedded activity registry; no temporary source-inclusion target is needed. Recorded full-run results belong in [completed verification](../../docs/AUTOMATION_IMPROVEMENTS.md#completed-verification), not a second fixed assertion total here. Remaining work is in [temp.md](../../../temp.md).

## Review 2 Regressions

Read-only focus preflight rejection must preserve an already acquired Quick owner, including requested/applied/sustained identity, status, target pins, weight-array identity and values, allocator receipts, refresh timestamps, settings, allocations, and equipment. The regression starts real Energy NGU and Time Machine focuses, rejects an infeasible Magic NGU transition, then runs the original focus's sustained allocator and verifies that it actually funds its feature. A separate unmanaged-Wandoos/AT refusal verifies that mutating `PrepareAtEntry` is not entered during read-only preflight.

With no previous owner, rejection retains the existing failed-request UI state. A controller failure after acquiring the successor clears the previous applied receipt but retains the new sustained intent and pending activation. Checks verify future retry backoff, no immediate recalls/swaps/resource changes, and successful setup/application of the new focus when the retry becomes due. Failure must not resurrect the old owner or silently clear the new request.

Retry fixtures include the real empty blood model needed to capture the complete allocation context. An unchanged context permits the due retry; a management-policy edit blocks automatic retry without mutation and requires explicit reapplication. Magic focus additionally rejects loss of non-owned energy/R3 idle capacity with zero or small funded allocations, even when those lanes are globally managed. The full pool, not just funded bars, must fit the proposed gear before any settings, recall or swap effects.

The metadata regression parses valid embedded-policy JSON with only Magic NGU's gear goal changed from 3 (Magic NGU) to 4 (All NGUs), temporarily installs that parsed private fixture definition, and restores it in `finally`. Real optimizer controls select different exact heads for the two goals. The actual focus application must equip the All NGU choice, not merely report the metadata intent. No shared policy JSON or production registry code is edited.

Both bugs were reproduced before the corresponding production fix: rejection replaced the previous Energy NGU request, and the actual equipped head followed hardcoded Magic NGU rather than the parsed All NGU goal. These are offline regressions, not live-game validation.

### Quick Fixture Accounting

Quick uses the same explicit standard-preset orders as normal stages, including
AUG overflow. With NGU/TM, Wandoos and AT locked in the handoff fixture, both real
Energy NGU and Time Machine acquisitions put the original 10,000 energy into AUG
as 5,000 augment plus 5,000 upgrade, leaving zero idle.

The old fixture then added 15,000 AT energy and assigned `curEnergy - idleEnergy`
as only 15,000, incorrectly omitting that existing AUG allocation. The unchanged
production `FocusUnownedAllocations` snapshot correctly rejected the actual
25,000 allocation sum before the Magic transition was even attempted. This was
a fixture accounting failure, not production clearing or a snapshot defect.

The corrected capacity-rejection seed preserves all acquired non-AT allocations
and sets idle energy from an independent enumeration of every actual energy lane.
At the current fixture cap of 1,000,010,000, idle is 999,985,000. Assertions verify
conservation after acquisition, seeding and resumed sustained allocation, and
compare every lane before/after rejected production transitions in addition to
the existing typed snapshot, owner, target-pin, weight and receipt assertions.
The separate unmanaged-Wandoos/AT case explicitly invokes the existing game-boundary
recall before replacing its resource distribution; that setup recall is outside
the production preflight's no-recall observation window. No production validation
or allocation path is substituted to accommodate the seed.

## Snapshot Failure Boundaries

Current and temporary gear snapshots are published only after both the complete map and its epoch have been captured successfully. Gear and digger Ygg acquisition save their snapshots before taking the Ygg lock. An existing Ygg lock still returns success without recapturing or equipping.

The regressions retain an older snapshot, install different current equipment/diggers, and induce real capture failures by removing scene item metadata, epoch stats, or digger drain metadata. They verify that previous map/epoch pairs and digger tokens remain intact, no Ygg lease is acquired for a failed save, no foreign snapshot is restored or discarded, and no unowned gameplay action occurs. Coordinator coverage also verifies that gear genuinely acquired before a digger-save failure is restored, while the previous digger snapshot and current unrelated diggers are untouched. Existing post-acquisition equip-failure rollback/release coverage remains enabled.

Negative controls reproduced all three original boundaries independently: an epoch failure replaced the previous saved gear map; after fixing atomic publication, gear-save failure left a Ygg gear lock; after fixing gear acquisition, digger-save failure left a Ygg digger lock. The fixes publish snapshots atomically and save before acquiring either Ygg lock. Cleanup checks `SavedDiggersToken` and uses `DiscardSavedDiggers(expected)` so an old owner cannot discard a successor's snapshot; epoch mismatch releases matching ownership without restoring old-save state.

## Coverage

### Automatic Max-Growth Yggdrasil

Build only the checks project and append `yggdrasil` to run its separate native-harvest fixture against an already built injector:

```powershell
& $msbuild "checks\Ownership\OwnershipChecks.csproj" /restore /t:Build /p:Configuration=Release /v:minimal /nologo
& "checks\Ownership\bin\OwnershipChecks.exe" "NGUInjector\bin\Release\NGUInjector.dll" $managed yggdrasil
```

The 19 scenarios retain compiled `AutomationRoutine`, `YggdrasilManager`, native `anyFruitMaxxed`, `fruitMaxxed`, `harvestTier`, both native `consumeAll` overloads, native fruit deactivation, and native `clearAllActiveDiggers`. The ordinary fixture's constant-false max predicate and manager/digger-clear substitutions remain unchanged outside this selector. A post-load hook replaces only the page scene boundary so `curPage` and displayed slot IDs actually change, rather than inheriting the ordinary fixture's page no-op. Direct manager checks pass the explicit `allowSwap` parameter; automatic routing supplies it from actual combat-prep activity.

- Maxed fruits with IDs 0, 9 and 20 exercise all three fruit pages and mixed native eat/harvest choices. Partly grown fruit on each page and locked fruit must remain untouched.
- Every starting page is restored; repeat calls with no newly maxed fruit perform no native consumption or page/refresh work. No-maxed-fruit checks run with configured swaps both off and on.
- Actual ordinary-loop routing harvests with legacy management, activation and global flags disabled; a real store-loaded five-minute plan with automatic activation disabled cannot suppress harvesting. Inaccessible Ygg skips native dispatch.
- Active, same-epoch combat-prep runtimes run with configured Ygg swaps both off and on. `ExpectedSupport` comes from production `CaptureSupportState`, including every inactive digger level. Actual `CombatPrepSupportStillOwned` must remain true; support levels/selections, all equipped gear, native clear counts, swap counts and resource recalls must remain unchanged. An injected Ygg rendering failure proves harvest is reached before the combat-prep guard without requiring unrelated combat allocation substitutions.
- A contrast control explicitly permits swapping during seeded prep: native clearing preserves inactive digger levels, temporary Ygg setup changes them, and authentic support ownership then refuses the changed state. This catches the fixture's former incorrect level-zeroing behavior and proves the new ownership assertions can expose the P1 regression.
- Reward and refresh exceptions restore the current page with configured swaps both off and on. Configured gear/digger ownership is asserted at the reward boundary; success and failure restore exact original gear/digger selections, release leases and discard owned snapshots.
- Foreign gear and digger owners block configured harvesting without mutation or lock leaks. Releasing the foreign owner permits a later successful retry.

Native `consumeAll()` currently scans every fruit ID, not just the visible page. Entry observations verify the manager still invokes pages 0, 1 and 2 and never selects `consumeAll(bool)`, while native filters and `eatFruit` choose the reward leaves. Reward amount formulas and live scene rendering are not covered: `consumeFruit(int)`/`harvest(int)` are deterministic engine/reward boundaries, and `refreshMenu` is a rendering boundary. No manager or automation-loop policy is replaced. These focused scenarios are not added to the default run because its older ownership checks deliberately use substituted harvest inputs.

Pre-fix verification against injector MVID `34c489e4-0bd4-41d8-a2dc-8e86f15c404f` reproduced 11 failing scenarios: repeated/no-max dispatch, disabled-flag/short-plan/active-prep routing, and exception page restoration. The other six controls passed. Final Release MVID `9fa4ae4a-8a1a-49d5-a413-f899d2abf346` passed all 19 scenarios (503 assertions), including prep-safe harvesting with configured swaps, and the full default Ownership suite (10,140 assertions).

Append `money-pit` to the runner command for focused Money Pit checks. They retain native reward selectors, equipment leveling, cumulative Looty allocation, exact selection/composition, and data-only swaps. Scripted external RNG inputs verify all tier/roll ranges, exact random targets/cosmetic draws, and restoration of caller/saved Pit states; they do not implement or validate Unity's native PRNG. Equipment checks cover heart/pendant/Looty priority, source-zone/titan-version ordering, non-maxed copies, failed preparation, milestone inventory staging, and token/epoch-safe pending restoration. See [Money Pit preparation](../../docs/money-pit.md).

- Real two-pass pre-rebirth coordination, actual game engagement/counter/clock dispatch, harvest exceptions, and failure after acquiring gear but during digger setup.
- Titan gold/fight epoch retirement on counter, clock, inventory-reference, and run-origin changes; no restoration of old-save equipment references.
- Same-epoch unload restoration, foreign lock/manual-setting preservation, and a successor using the same gear-lock kind but a different snapshot token.
- Active fight-Titan maintenance before querying a pending gold route.
- Exact sparse accessory composition, duplicate-ID clearing, unrelated occupant retention, all-empty maps, full-inventory refusal, and saved/temp restoration of original empty slots.
- Actual native `InventoryController.swapHead` allocation rejection, with only its rendering/tooltip leaves replaced; shared projected-capacity preflight agrees before resource mutation. Separate accessory staging checks reject an unsafe intermediate map even when the final cap is legal.
- Manual external optimize success and a controller error after a real data swap; old stage receipts are invalidated, scheduler/entry identity and the running-plan policy remain intact, and the normal coordinator reapplies gear.
- Gearless Quick refresh still requires the deferred fight-entry receipt.
- Fruit-cap activation exceptions restore in `finally`; lost ownership preserves the successor and leaves Quick refresh pending after its lock is released.
- Initial Magic NGU preserves actual unmanaged energy/R3 allocations and refuses infeasible gear before plan/rebirth switches or recalls.
- Money Pit rejects a foreign digger lease before upgrades/recall and restores exact temporary digger levels/activation after a pit exception.

Most coordinator tests use data-only inventory swaps and substituted scene/UI/support-getter leaves. The explicit native-head test retains the real controller guard and rollback IL. This does not claim live rendering, every native accessory/weapon guard, or live timing coverage. Harvest and pit engagement are deterministic boundary substitutes, not actual game harvest/spending.

## API Contract

`LoadoutManager.CaptureExactLoadout()` returns fresh `ih` wrappers for all currently unlocked equipment destinations, including zero IDs. Nonempty entries retain exact `Equipment` identity; empty matching is by zero ID, not empty-object identity.

`LoadoutManager.ComposeExactLoadout(original, selectedDestinations)` expects selected wrappers already assigned to destination slots. It does not optimize or assign destinations. It clears former locations of moved selected instances and conflicting IDs, retains unrelated unique original occupants, and returns new wrappers without changing either input or game data.

`LoadoutManager.ApplyExactLoadout(wanted, resourcesAlreadyRemoved = false, ignoreLock = false)` requires a complete map for the current unlocked topology. It reuses the existing `PlanEquipGear` topology algorithm for nonempty destinations, adds safe inventory staging for explicit empties, preflights retained basic-training capacity and unmanaged E/M/R3 totals (including idle) across every intermediate state, and verifies the whole observed map. Gear-swap recalls apply only to eligible managed lanes. It returns `null` on success or a failure string. A native/controller exception can leave a partial application; callers must keep their application receipt invalid and must not assume rollback.

`LoadoutManager.ExactLoadoutMatches(wanted)` checks complete topology, IDs, exact nonempty instances, and explicit empties.

`GearOptimizer.CheckExactSwapCapacity(wanted, retainedEnergy, retainedRes3)` is a read-only additional preflight for callers retaining non-owned allocations. It checks current projection parity and every planned intermediate/final capacity. Quick Magic NGU captures a typed read-only allocation map, performs this preflight before settings mutation, recalls only its owned magic lane, and verifies the non-owned map afterward. Energy/R3 allocation fields and their UI are never cleared, so no allocation reconstruction or unrelated state rollback is needed.

`Main.InvalidateCustomEquipmentApplication()` suspends protected credit, clears stage gear/support application receipts, and blocks Quick reuse until normal application. It deliberately preserves stage/fight-entry identity, scheduler objects, earned history, and saved pause settings.

## Integrated Boundaries

- Production compiles `ResourceProjection`, `ResourceProjectionCapture`, `ResourceAllocation`, `DiggerAllocation` and `ActivityPolicy`, and embeds `NGUInjector.ActivityPolicies.json` in the normal build.
- Projection uses `Create(Character, bool energy, bool capacity)` and `GetBonuses(IEnumerable<ih>)`; raw normalization is shared with presets rather than duplicated.
- `EquipGear` accepts optional fourth argument `ih[] exactMap`; `PlanEquipGear` accepts optional ninth argument `ih[] exactMap`. Ordinary C# partial-loadout calls retain their meaning. Reflection fixtures supply the extra argument explicitly; no compatibility overload or placeholder stub is required.
- Ygg cleanup releases only matching epoch/token ownership, restores only in the same epoch, and discards its saved gear/digger snapshots. Both acquisition paths save before locking, including capture-failure boundaries.
- Preset preflight checks intermediate/final retained-allocation capacity before consuming its token, pausing owners or recalling resources, not only inside the executor. Preset application remains one-shot.
