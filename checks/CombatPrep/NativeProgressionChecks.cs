using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void CheckNativeProgressionMath()
    {
        // Independent tick transcription of Wandoos98Controller.updateWandoos/bootupSpeedFactor
        // and AdvancedTrainingController.updateAdvancedTraining, validated by the native suite.
        var random = new Random(1903600);
        for (var sample = 0; sample < 100; sample++)
        {
            var s = Snapshot(random.Next(1, 80), random.Next(1, 80));
            s.WandoosBootupElapsedSeconds = sample % 5 == 0 ? 0 : random.Next(100) / 50d;
            s.WandoosBootupDurationSeconds = sample % 3 == 0 ? 1 : 3;
            s.WandoosBootupCompletionFactor = sample % 2 == 0 ? (double) 1.1f : 1;
            s.FreeAdvancedTraining = sample % 2 == 0;
            s.AtEnergy.Unlocked = sample % 7 != 0; s.AtMagic.Unlocked = sample % 11 != 0;
            s.AtEnergy.TargetLevel = s.AtMagic.TargetLevel = 1; // ignored by FREE zero-allocation AT
            s.AtEnergy.Progress = .75; s.AtMagic.Progress = .25;
            s.AtEnergy.LevelFactor = .1; s.AtMagic.LevelFactor = .2;
            s.OperatingSystems[0].EnergyBaseTime = 128; s.OperatingSystems[0].MagicBaseTime = 256;
            s.WandoosEnergy.Progress = .5; s.WandoosMagic.Progress = .25;
            s.AllocationRefreshSeconds = sample % 3 == 0 ? .26 : .5;
            var phases = new[] { Phase("NGU", .52), Phase("Wandoos", 2.48) };
            // NGU is terminal in the phase grammar: use an unproductive augment prefix instead.
            s.Augments = new[] { Tier() }; s.Augments[0].Augment.Unlocked = s.Augments[0].Upgrade.Unlocked = false;
            phases[0] = Phase("Augments", .52);
            var projected = Project(s, phases);
            long el = 0, ml = 0, ae = 0, am = 0, allocE = 0, allocM = 0;
            double ep = .5, mp = .25, eCost = 0, mCost = 0;
            var start = (int) Math.Round(projected.Phases[0].DurationSeconds * 50);
            var refresh = (int) Math.Round(s.AllocationRefreshSeconds * 50);
            for (var tick = 0; tick < 150; tick++)
            {
                if (tick >= start)
                {
                    var elapsed = s.WandoosBootupElapsedSeconds + tick / 50d;
                    var boot = elapsed >= s.WandoosBootupDurationSeconds ? s.WandoosBootupCompletionFactor : elapsed / s.WandoosBootupDurationSeconds;
                    var eu = (1 + .1 * ae) * boot / 128;
                    var mu = (1 + .2 * am) * boot / 256;
                    if ((tick - start) % refresh == 0)
                    {
                        allocE = eu * s.Current.EnergyTotal <= 1e-9 ? 0 : Math.Min(s.Current.EnergyTotal, (long) Math.Ceiling(1 / eu));
                        allocM = mu * s.Current.MagicTotal <= 1e-9 ? 0 : Math.Min(s.Current.MagicTotal, (long) Math.Ceiling(1 / mu));
                    }
                    var eq = eu * allocE; var mq = mu * allocM;
                    if (eq > 1e-9) ep += eq;
                    if (mq > 1e-9) mp += mq;
                    if (ep >= 1) { el++; ep = 0; }
                    if (mp >= 1) { ml++; mp = 0; }
                    eCost += allocE / 50d; mCost += allocM / 50d;
                }
                if (s.FreeAdvancedTraining && s.AtEnergy.Unlocked) ae++;
                if (s.FreeAdvancedTraining && s.AtMagic.Unlocked) am++;
            }
            Equal(el, projected.WandoosEnergyLevel, "native ramp/free AT energy levels " + sample);
            Equal(ml, projected.WandoosMagicLevel, "native ramp/free AT magic levels " + sample);
            Near(ep, projected.WandoosEnergyProgress, "native ramp/free AT energy partial " + sample);
            Near(mp, projected.WandoosMagicProgress, "native ramp/free AT magic partial " + sample);
            Near(eCost, projected.EnergyResourceSeconds, "native ramp held energy cost " + sample);
            Near(mCost, projected.MagicResourceSeconds, "native ramp held magic cost " + sample);
            Equal(ae, projected.AtEnergyLevel, "free AT advances through prefix and W " + sample);
            Equal(am, projected.AtMagicLevel, "free AT independent lane unlock " + sample);
            Near(ae > 0 ? 0 : .75, projected.AtEnergyProgress, "free completion discards initial partial");
        }

        var fresh = Snapshot(100, 100);
        fresh.WandoosBootupDurationSeconds = 3600;
        fresh.Current.WandoosEnergySpeedWithoutAt = fresh.Current.WandoosMagicSpeedWithoutAt = 100;
        var firstHold = Project(fresh, Phase("Wandoos", .5));
        Equal(0L, firstHold.WandoosEnergyLevel, "fresh zero speed cannot allocate before next refresh");
        Near(0, firstHold.EnergyResourceSeconds, "fresh zero speed incurs no held W cost");
        var later = Project(fresh, Phase("Wandoos", 1));
        True(later.WandoosEnergyProgress > 0, "fresh boot recovers productive speed after zero capture");
        fresh.FreeAdvancedTraining = true;
        var freePlan = CombatPrepPlanner.Plan(fresh, 2);
        True(freePlan.Success, "free AT native context plans");
        True(freePlan.Alternatives.All(x => x.Phases.All(p => p.Kind != "AT")), "Wish190 never creates paid AT phases");
        Throws(() => Project(fresh, Phase("AT", 1)), "free AT refuses explicit paid-AT phases rather than fake gates");
        var freeOnly = Project(fresh, Phase("NGU", 1));
        Equal(50L, freeOnly.AtEnergyLevel, "one free level per native tick, not one per refresh");
        Near(0, freeOnly.EnergyResourceSeconds, "free AT has no energy opportunity cost");
        fresh.AtEnergy.Level = long.MaxValue - 1;
        Equal(long.MaxValue, Project(fresh, Phase("NGU", 1)).AtEnergyLevel, "free level arithmetic saturates safely");
        var extreme = Snapshot(1, 0);
        extreme.WandoosBootupDurationSeconds = 1e300; extreme.WandoosBootupElapsedSeconds = .1;
        extreme.Current.WandoosEnergySpeedWithoutAt = 1e300;
        extreme.OperatingSystems[0].EnergySpeedFactor = extreme.OperatingSystems[0].EnergyBaseTime = 1e300;
        Near(.1, Project(extreme, Phase("Wandoos", .02)).WandoosEnergyProgress, "tiny bootup and huge speed retain the full native rate instead of clamping a factored coefficient early");
        extreme.WandoosBootupDurationSeconds = double.NaN;
        True(!CombatPrepPlanner.Plan(extreme, 1).Success, "nonfinite bootup capture is rejected");
        extreme.WandoosBootupDurationSeconds = 3600; extreme.WandoosBootupCompletionFactor = .9;
        True(!CombatPrepPlanner.Plan(extreme, 1).Success, "native completion cannot reduce ramp speed");

        var lanes = Snapshot(100, 200);
        var cost = Project(lanes, Phase("Wandoos", 1));
        Near(cost.ResourceFocusEquivalentSeconds, cost.EnergyResourceFocusEquivalentSeconds + cost.MagicResourceFocusEquivalentSeconds, "separate lane opportunity costs sum to existing total");
        Equal("current", cost.ResourceReferenceProfileKey, "opportunity cost names actual reference gear");
        True(cost.EnergyResourceFocusCostKnown && cost.MagicResourceFocusCostKnown, "lane opportunity cost flags are independent");
        lanes.Profiles = new[] { new CombatPrepProfile { Key = "ngu", Goal = "NGU", SupportKey = "frozen", MagicTotal = 200, MagicCap = 200 } };
        cost = Project(lanes, Phase("Wandoos", 1));
        True(!cost.EnergyResourceFocusCostKnown && cost.MagicResourceFocusCostKnown && !cost.ResourceFocusCostKnown, "one missing reference lane does not hide the other lane's measured cost");
    }

    private static void CheckAdaptiveTiming()
    {
        var s = Snapshot(100, 100);
        s.AtEnergy.BaseTime = s.AtMagic.BaseTime = 100;
        s.AtEnergy.LevelFactor = s.AtMagic.LevelFactor = 5;
        s.OperatingSystems[0].EnergyBaseTime = s.OperatingSystems[0].MagicBaseTime = 10000;
        var coarse = new[] { .125, .25, .5, .75 }.SelectMany(f => new[] { 0d, .25, .5, .75, 1 }.Select(w =>
            Project(s, Phase("AT", 64 * f, w), Phase("Wandoos", 64 * (1 - f))))).Max(x => x.TotalBonus);
        var result = CombatPrepPlanner.Plan(s, 64);
        True(result.Success, "adaptive timing fixture succeeds");
        var winner = result.Alternatives.First(x => x.Family == "ATThenWandoos");
        True(winner.TotalBonus > coarse, "shrinking time refinement strictly improves the old coarse AT/W grid");
        Near(64, winner.Phases.Sum(x => x.DurationSeconds), "refinement preserves deadline");
        Near(winner.TotalBonus, Project(s, winner.Phases).TotalBonus, "refined policy replays actual targets");
        True(result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "adaptive work stays inside candidate bound");

        s = Snapshot(100, 100); s.Augments = new[] { Tier() };
        s.Augments[0].Augment.CostCoefficient = s.Augments[0].Upgrade.CostCoefficient = 0;
        s.Augments[0].TierExponent = 2;
        s.AtEnergy.Unlocked = s.AtMagic.Unlocked = false;
        s.OperatingSystems[0].EnergyBaseTime = s.OperatingSystems[0].MagicBaseTime = 10000;
        coarse = new[] { .125, .25, .5, .75 }.Select(f =>
            Project(s, Phase("Augments", 32 * f), Phase("Wandoos", 32 * (1 - f)))).Max(x => x.TotalBonus);
        result = CombatPrepPlanner.Plan(s, 32);
        True(result.Success, "adaptive augment timing fixture succeeds");
        winner = result.Alternatives.First(x => x.Family == "AugmentsThenWandoos");
        True(winner.TotalBonus > coarse, "shrinking time refinement improves coarse augment/W schedules");
        Near(winner.TotalBonus, Project(s, winner.Phases).TotalBonus, "refined augment schedule replays actual augment gates");
    }
}
