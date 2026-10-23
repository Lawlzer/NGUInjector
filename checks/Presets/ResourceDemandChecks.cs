using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    static partial void ResourceDemandChecks()
    {
        var before = assertions;
        ResourceDemandLifecycleChecks();
        ResourceDemandEligibilityChecks();
        ResourceDemandSharingChecks();
        Console.WriteLine("Resource demand numeric regressions: " + (assertions - before) + " assertions");
    }

    private static PresetSnapshot ResourceDemandSnapshot()
    {
        var s = Snapshot();
        s.Magic = 5000;
        s.MagicNgu = new[] {
            new PresetNgu { Level = 10000, HardCap = 1000000000, Coefficient = .001, Weight = 1 },
            new PresetNgu { Level = 10000, HardCap = 1000000000, Coefficient = .001, Weight = 3 },
            new PresetNgu { Level = 10000, HardCap = 1000000000, Coefficient = .001, Weight = 0 } };
        s.Rituals = new[] {
            new PresetRitual { Id = 0, Unlocked = true, Coefficient = .0001, Blood = 20, Cost = 1 },
            new PresetRitual { Id = 4, Unlocked = true, Coefficient = .01, Blood = 10, Cost = 1 } };
        return s;
    }

    private static void ResourceDemandLifecycleChecks()
    {
        var s = ResourceDemandSnapshot(); var r = Request(); r.BloodPercent = 30;
        var baseline = Evaluate(s, r);
        Check(baseline.Feasible && baseline.BloodPercent == 30 && baseline.Ritual == 4 && baseline.Blood == 300
            && baseline.MagicNgu == 700 && baseline.MagicShares.SequenceEqual(new long[] { 175, 525, 0 }),
            "demand fixture has an evaluated weighted baseline with a fixed fallback fraction");
        Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, false)),
            "demand off returns the exact baseline without replacing its fallback fraction");

        foreach (var flags in new[] { new[] { true, false }, new[] { false, true }, new[] { true, true } })
        {
            var overlay = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1]);
            var name = "demand Iron Pill=" + flags[0] + " blood goals=" + flags[1];
            Check(!ReferenceEquals(overlay, baseline) && overlay.Ritual == 4 && overlay.Blood == 101
                && overlay.MagicNgu == 899 && overlay.MagicShares.SequenceEqual(new long[] { 225, 674, 0 }),
                name + " reserves the same capped sustainable blood once, including ordinary catchup");
            Check(overlay.Energy == baseline.Energy && ReferenceEquals(overlay.EnergyShares, baseline.EnergyShares)
                && overlay.EnergyPool == baseline.EnergyPool && overlay.MagicPool == 1000
                && overlay.Blood + overlay.MagicNgu == overlay.MagicPool,
                name + " conserves both capped resource pools and every energy allocation");
            Check(ReferenceEquals(overlay.Items, baseline.Items) && SupportSemanticallyEqual(overlay.Support, baseline.Support)
                && !ReferenceEquals(overlay.Support, baseline.Support)
                && ReferenceEquals(overlay.Metrics, baseline.Metrics) && ReferenceEquals(overlay.RawMetrics, baseline.RawMetrics)
                && overlay.Score == baseline.Score && overlay.Zone == baseline.Zone && overlay.Floor == baseline.Floor
                && overlay.BloodPercent == 30 && overlay.ActivityGoldSpent == baseline.ActivityGoldSpent,
                name + " does not search gear, alter support, rewrite forecasts or pin temporary fallback fractions");
            Check(baseline.Ritual == 4 && baseline.Blood == 300 && baseline.MagicNgu == 700
                && baseline.MagicShares.SequenceEqual(new long[] { 175, 525, 0 })
                && s.Magic == 5000 && s.Gold == 1000 && s.MagicNgu.Select(x => x.Weight).SequenceEqual(new[] { 1, 3, 0 })
                && s.MagicNgu.All(x => x.Level == 10000) && s.Rituals[1].Coefficient == .01 && !s.Rituals[1].Paid,
                name + " never mutates the baseline or captured progression, resources and rituals");
        }
        var wrapped = LoadoutPresetPlanner.ReserveIronPillMagic(s, baseline);
        Check(wrapped.Ritual == 4 && wrapped.Blood == 101 && wrapped.MagicShares.SequenceEqual(new long[] { 225, 674, 0 }),
            "existing Iron Pill API remains a concrete equivalent wrapper");

        // A new preset and new global weights become authoritative even while the demand is active.
        var fresh = s.ProgressionCopy(); fresh.MagicNgu[0].Weight = 3; fresh.MagicNgu[1].Weight = 1;
        var nextRequest = Request(); nextRequest.Weights = new[] { 3d, 1, 0, 0, 0, 0 }; nextRequest.BloodPercent = 60;
        var nextBaseline = Evaluate(fresh, nextRequest);
        Check(nextBaseline.Blood == 600 && nextBaseline.MagicNgu == 400
            && nextBaseline.MagicShares.SequenceEqual(new long[] { 300, 100, 0 }), "preset switch evaluates its own fresh fallback baseline");
        var active = LoadoutPresetPlanner.ComposeResourceDemands(fresh, nextBaseline, true, true);
        Check(active.Blood == 101 && active.MagicNgu == 899 && active.MagicShares.SequenceEqual(new long[] { 674, 225, 0 }),
            "active demand follows the switched baseline and current global weights, not previously observed shares");
        var catchup = LoadoutPresetPlanner.ComposeResourceDemands(fresh, nextBaseline, false, true);
        Check(catchup.Blood == 101 && catchup.MagicShares.SequenceEqual(active.MagicShares),
            "Iron Pill release leaves ordinary eligible catchup funded without stacking reservations");
        var released = LoadoutPresetPlanner.ComposeResourceDemands(fresh, nextBaseline, false, false);
        Check(ReferenceEquals(released, nextBaseline) && released.Blood == 600 && released.MagicNgu == 400
            && released.MagicShares.SequenceEqual(new long[] { 300, 100, 0 }) && released.BloodPercent == 60,
            "completed demand releases to the caller's new baseline, not old baseline or active allocations");
        Check(baseline.Blood == 300 && baseline.MagicShares.SequenceEqual(new long[] { 175, 525, 0 })
            && nextBaseline.Blood == 600 && nextBaseline.MagicShares.SequenceEqual(new long[] { 300, 100, 0 })
            && s.MagicNgu[0].Weight == 1 && fresh.MagicNgu[0].Weight == 3,
            "switch, catchup and release leave both independent baselines and snapshots unchanged");

        var dedicated = nextBaseline.Copy();
        dedicated.Energy = 600; dedicated.EnergyShares = new long[] { 600 };
        dedicated.Activities = new PresetActivityAllocation { TimeMachineEnergy = 50, WandoosEnergy = 60,
            AugmentId = 4, AugmentEnergy = 70, UpgradeEnergy = 80, AT = new long[] { 20, 30, 40, 25, 25 },
            TimeMachineMagic = 100, WandoosMagic = 200, Os = 2 };
        dedicated.Blood = 420; dedicated.MagicNgu = 280; dedicated.MagicShares = new long[] { 210, 70, 0 };
        active = LoadoutPresetPlanner.ComposeResourceDemands(fresh, dedicated, false, true);
        Check(active.Blood == 101 && active.MagicNgu == 599 && active.MagicShares.SequenceEqual(new long[] { 449, 150, 0 })
            && active.Blood + active.MagicNgu + active.Activities.MagicTotal == 1000,
            "ordinary demand borrows only shared magic and conserves exact total with dedicated TM/Wandoos");
        Check(active.Energy == 600 && active.EnergyShares.SequenceEqual(new long[] { 600 })
            && ReferenceEquals(active.Activities, dedicated.Activities) && active.Activities.EnergyTotal == 400
            && active.Activities.MagicTotal == 300 && active.Activities.Os == 2,
            "ordinary demand preserves TM, Wandoos, augment, upgrade and all AT energy lanes");
        Check(dedicated.Blood == 420 && dedicated.MagicNgu == 280 && dedicated.MagicShares.SequenceEqual(new long[] { 210, 70, 0 })
            && ReferenceEquals(dedicated, LoadoutPresetPlanner.ComposeResourceDemands(fresh, dedicated, false, false)),
            "dedicated fallback fractions remain immutable and are restored only from the supplied baseline");
    }

    private static void ResourceDemandEligibilityChecks()
    {
        foreach (var flags in new[] { new[] { true, false }, new[] { false, true }, new[] { true, true } })
        {
            var s = ResourceDemandSnapshot(); var r = Request(); r.BloodPercent = 0;
            var baseline = Evaluate(s, r);
            var name = "eligibility Iron Pill=" + flags[0] + " blood goals=" + flags[1];
            s.Rituals = new[] { new PresetRitual { Id = 4, Unlocked = true, Coefficient = .01, Blood = 10, Cost = 10 } };
            baseline.GrossGold = 300; baseline.Support = new PresetSupport { Drain = 50 };
            var active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1]);
            Check(active.Ritual == 4 && active.Blood == 35 && active.MagicNgu == 965
                && active.MagicShares.SequenceEqual(new long[] { 241, 724, 0 })
                && LoadoutPresetPlanner.BloodFills(.01 * active.Blood) * 10 < 250,
                name + " uses the best strictly sustainable partial plateau after digger drain");
            baseline.ActivityGoldSpent = 990;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " cannot double-spend liquid activity gold at the exact unpaid startup cost");
            s.Rituals[0].Paid = true;
            Check(LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1]).Blood == 35,
                name + " already-paid ritual does not require another startup payment");
            s.Rituals[0].Paid = false; baseline.ActivityGoldSpent = 0;

            s.Available[2] = false;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " unavailable blood leaves lower-priority baseline allocations untouched");
            s.Available[2] = true; s.Rituals[0].Unlocked = false;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " locked ritual cannot starve NGU");
            s.Rituals[0].Unlocked = true; s.Rituals[0].Coefficient = 1e-12;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " no productive blood below native progress gate cannot starve NGU");
            s.Rituals[0].Coefficient = .01; s.Rituals[0].Blood = 0;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " zero blood yield cannot starve NGU");
            s.Rituals[0].Blood = 10; s.Rituals[0].Cost = 0; s.Gold = 0;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " unpaid zero-cost ritual still obeys strict native liquid gold gate");
            s.Rituals[0].Paid = true;
            active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1]);
            Check(active.Blood == 101 && active.MagicNgu == 899 && active.Ritual == 4,
                name + " paid zero-cost ritual gets productive cap without stealing leftover NGU");
            baseline.GrossGold = 50;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " zero income after diggers retains existing reservation policy even for zero-cost ritual");
            baseline.GrossGold = 300; baseline.Feasible = false;
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " infeasible baseline cannot be rescued by a demand");
            baseline.Feasible = true; s.Rituals = new PresetRitual[0];
            Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, flags[0], flags[1])),
                name + " no eligible request leaves baseline intact");
            Check(baseline.Blood == 0 && baseline.MagicNgu == 1000 && baseline.MagicShares.SequenceEqual(new long[] { 250, 750, 0 }),
                name + " every eligibility path leaves the baseline allocation unchanged");
        }
    }

    private static void ResourceDemandSharingChecks()
    {
        var s = ResourceDemandSnapshot(); var r = Request(); r.BloodPercent = 0;
        var baseline = Evaluate(s, r);
        s.MagicNgu[0].Weight = s.MagicNgu[1].Weight = 0;
        var active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true);
        Check(active.Blood == 101 && active.MagicNgu == 0 && active.MagicShares.SequenceEqual(new long[3])
            && baseline.MagicPool - active.Blood - active.MagicNgu == 899,
            "all-zero global exclusions leave exact surplus idle rather than enabling an excluded row");
        s.MagicNgu[0].Weight = 1; s.Available[1] = false;
        active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true);
        Check(active.Blood == 101 && active.MagicNgu == 0 && active.MagicShares.SequenceEqual(new long[3]),
            "unavailable NGU receives no demand overflow despite positive global weight");
        s.Available[1] = true; s.MagicNgu[0].Level = s.MagicNgu[0].HardCap;
        active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true);
        Check(active.Blood == 101 && active.MagicNgu == 0 && active.MagicShares.SequenceEqual(new long[3]),
            "hardcapped NGU cannot absorb demand overflow");
        s.MagicNgu[0].Level = 0; s.MagicNgu[0].Target = 1; s.MagicNgu[0].Coefficient = 1;
        active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true);
        Check(active.Blood == 101 && active.MagicNgu == 2 && active.MagicShares.SequenceEqual(new long[] { 2, 0, 0 })
            && active.Blood + active.MagicNgu + 897 == baseline.MagicPool,
            "finite NGU target cap conserves the budget with exact unallocated surplus");
        baseline.MagicPool = 7;
        active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true);
        Check(active.Blood == 7 && active.Ritual == 4 && active.MagicNgu == 0,
            "pool-limited demand can use productive partial blood instead of requiring the full cap");
        baseline.MagicPool = 0;
        Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true)),
            "empty shared magic cannot preempt the baseline");
        baseline.MagicPool = 1000; baseline.Activities = new PresetActivityAllocation { TimeMachineMagic = 400, WandoosMagic = 600 };
        Check(ReferenceEquals(baseline, LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true)),
            "fully dedicated magic cannot be borrowed by a blood demand");

        s = ResourceDemandSnapshot(); baseline = Evaluate(s, r);
        baseline.MagicPool = long.MaxValue;
        s.Rituals = new[] { s.Rituals[1] };
        s.MagicNgu[0].Coefficient = s.MagicNgu[1].Coefficient = 1e-20;
        active = LoadoutPresetPlanner.ComposeResourceDemands(s, baseline, false, true);
        Check(active.Blood == 101 && active.MagicNgu == long.MaxValue - 101
            && active.MagicShares.SequenceEqual(new long[] { 2305843009213693927, 6917529027641081779, 0 })
            && checked(active.Blood + active.MagicNgu) == long.MaxValue,
            "demand composition conserves every unit of an Int64-sized weighted magic pool");
        Check(baseline.Blood == 0 && baseline.MagicNgu == 1000 && baseline.MagicShares.SequenceEqual(new long[] { 250, 750, 0 }),
            "large-pool redistribution never overwrites input allocation arrays");
    }
}
