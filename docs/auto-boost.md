# Custom Auto-Boost

Custom auto-boost targets equipped gear, gear in auto-merge inventory slots,
and locked gear elsewhere in inventory. Auto-merge slots are the zero-based
range `[0, InventoryController.totalInvMergeSlots())`; an item does not need to
be locked to qualify in that range. Unlocked gear outside that range is excluded.

The shared `InventoryManager.GetCustomBoostSlots` selector drives both boost
application and boost-transform selection. Blacklisted items, non-equipment,
and gear with no remaining boost deficit are excluded. Equipped non-heart gear
comes before inventory non-heart gear; equipped hearts and then inventory hearts
come last. Inventory targets retain ascending slot order.

Each Power, Toughness, and individual Special deficit is clamped to zero before
accumulation. An overcapped stat cannot cancel another stat's missing boosts.
Target calculation still honors `SpecialBoostBlacklist`.

Enabled means eligible, not guaranteed immediate boosts. In the default order,
even an equipped heart can wait behind unfinished non-heart gear in auto-merge
slots. Transform selection follows that same order, so an earlier Power/Toughness
target can also delay producing Special boosts for a heart. Set an explicit
priority for the heart when it should be boosted before those items.

`Settings.PriorityBoosts` (persisted as `_priorityBoosts`) overrides this default
order by item ID. Listed eligible items come first in list order, including hearts;
unlisted items retain the default order above. All eligible copies of a listed ID
are selected, in their stable default order. Repeated IDs use their first list
position and never duplicate targets. Priorities do not admit unlocked gear outside
auto-merge slots, non-equipment, or already boosted gear. `BoostBlacklist` always
wins, including when the same ID is prioritized. Empty or null priorities leave
the default order unchanged. Both boost application and transform selection use
this same ordering; the separate locked-unmaxed-boost transform priority is unchanged.

## Scheduling And Recovery

Custom auto-boost runs once per eligible 10-second automation pass, before
harvesting, automatic-stage entry, and Combat Prep early returns. It remains
independent of legacy/global automation. Inventory drag, a running manual
preview-selected Combat Prep policy, pending live cleanup, and an in-progress
prep gear transaction suppress boosting.

Running automatic Combat Prep accepts same-instance boost growth through its
existing material-change detection. Unfinished paid AT retains both acquired
targets and recaps allocations; a new equipment search waits until both targets
are reached. Boosting never renews the run's original deadline or restoration
capture. A boost exception is logged locally so prep maintenance still runs.

Checkpoint-only cleanup recovery must not starve inventory maintenance. Boosting
does not acknowledge recovery, restore old gear, or acquire resource ownership.
The resource coordinator now retires obsolete restoration permissions and
automatically reconciles the current intent under the original stage deadline;
no guarded stage Restart/Set/Skip is required. See [automatic reconciliation](automatic-reconciliation.md).
An invalidated cached non-auto-updating preset defers boosting until its replay
only outside Combat Prep/recovery; replay is prohibited during that ownership.
A suspended preset does not adopt material changes made while it is not owner.

An earlier October 20, 2026 stoppage was confirmed in the live runtime as auto-boost
enabled with no active prep run but `cleanupRecoveryRequired: true`. Previously
the main loop returned before boosting on every pass. The rebuilt backend fixes
that scheduling starvation without clearing the durable safety marker; the later
automatic-reconciliation fix also resolves the obsolete allocation stop. Loading
the new DLL requires restarting the game and reinjecting; an already running
process does not pick up a rebuild automatically.

## Owned Equipment Telemetry

Every runtime-state build includes `equipmentBoosts` at the root:

```json
{
  "equipmentBoosts": {
    "available": true,
    "reason": "",
    "items": [
      { "id": 100, "name": "Equipment name", "copies": 3, "level": 80, "fullyBoosted": false }
    ]
  }
}
```

This read-only snapshot includes equipped gear (including a nonempty second
weapon), accessories, and **all** inventory equipment, even unlocked inventory
outside merge slots. IDs are unique and sorted numerically. Boost priorities,
boost blacklists, locks, merge eligibility, and historical item-maxxed flags do
not filter this ownership list. Empty slots and non-equipment are excluded.

An ID is omitted only when **every** owned copy is level 100 or higher **and**
physically fully boosted. `copies` counts all owned copies of a retained ID,
including complete copies. `level` is the minimum level among its incomplete
copies, where incomplete means level below 100 or any physical boost deficit.
`fullyBoosted` is true only when **all owned copies** of that ID are physically
max boosted at their respective current levels. Thus a level-0 fully boosted
copy remains listed with `fullyBoosted: true` until it also reaches level 100.

The equipment-priority UI also filters received rows with `level >= 100` and
`fullyBoosted: true`, including on refresh. Hiding a row never removes its saved
priority or blacklist preference.

Physical completion uses `Extensions.IsFullyBoosted`: attack, defense, and each
native special stat are checked independently against the current-level,
floored `CalcCap` cap. Absent stats are ignored; an overcapped stat cannot cancel
another stat's deficit. `SpecialBoostBlacklist` is deliberately ignored here
even though normal boost-target deficits still honor it.

If game ownership data is unavailable or capture fails, only this section becomes
`{ "available": false, "reason": "...", "items": [] }`; partial results are not
published. An empty readable inventory is available with an empty array. Capture
performs no controller actions, merges, boosts, setting changes, or file writes,
and cannot abort the rest of runtime-state construction.

## Verification

After building the injector and `checks/BlackBarChecks.csproj`, run
`checks/bin/BlackBarChecks.exe <injector.dll> <game-managed-directory> auto-boost`
for compiled regressions. These data-only checks cover eligibility, default and
explicit priority ordering, duplicate copies, blacklist precedence, independent
physical-stat completion, current-level cap rounding, ownership aggregation,
completion omission, exact runtime placement, empty/missing data, and isolated
snapshot failures, including independent nonnegative target deficits. They do not
exercise live Unity boost application or transform-controller actions. The
ExternalUi source-linked harness substitutes only the equipment/formula boundary;
physical formulas and owned-equipment
snapshots are checked against the compiled injector and real game data types.

After building `checks/CombatPrepIntegration/CombatPrepIntegrationChecks.csproj`,
run its executable with `<injector.dll> <game-managed-directory> --auto-boost`.
These compiled checks exercise scheduling through the real inventory manager and
native Power/Toughness/Special consumption, restored recovery with no live run,
cached-preset deferral, automatic entry/replanning, unfinished AT gates, suppression
guards, and exception continuation. Only external rendering/RNG/fixture boundaries
are substituted; equipped instances and consumed boosts are checked directly.
Boost merging, recycling chains, cube consumption, live Unity rendering and
callback ordering are not covered by that fixture.

October 20 offline verification against the same rebuilt Release backend passed:
454 auto-boost data assertions; 184 focused lifecycle/native assertions; 8,138 full
Combat Prep assertions; 10,732 full preset integration assertions (including 404
boost ownership assertions); and the complete black-bar suite. No running game,
save, runtime settings, or checkpoint was modified by verification.
