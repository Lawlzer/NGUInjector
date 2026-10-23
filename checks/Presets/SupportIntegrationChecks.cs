using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void SupportIntegrationChecks()
    {
        var s = Snapshot(); var r = Request(); r.PlannedSupport = true; r.BeardPriority = new[] { 3 };
        s.NguFactors = Enumerable.Repeat(1f, 6).ToArray();
        s.CurrentBeards = new[] { 6 }; s.UnlockedBeards = new[] { 1, 3, 5, 6 }; s.BeardSlots = 1;
        s.BeardTemporary = new[] { 2f, 2f, 3f, 4f }; s.BeardPermanent = new[] { 1.5f, 1.25f, 2f, 2f };
        s.GrossGold = s.GoldActiveGross = 1000; s.GoldInactiveGross = 500;
        s.DiggerSlots = 1; r.DiggerPercent = 75;
        s.Diggers = new[] { new SupportDigger { Id = 4, Name = "NGU", Maximum = 1, ConsumesGold = true, BaseDrain = 750, Growth = 2 },
            new SupportDigger { Id = 0, Name = "Drop", Maximum = 10, ConsumesGold = true, BaseDrain = 100, Growth = 2 } };
        r.DiggerWeights = new double[12]; r.DiggerWeights[4] = 2; r.DiggerWeights[0] = 1;
        var candidate = Evaluate(s, r);
        Check(candidate.GrossGold == 500 && candidate.Support.Budget == 375, "Gold deactivation reduces candidate GPS before support budgeting");
        Check(candidate.Support.Diggers.Single().Id == 0 && candidate.Support.Diggers.Single().Level == 2,
            "R06 shared affordability selection backfills lower ranked affordable request");
        Check(candidate.Support.Drain <= candidate.Support.Budget && candidate.Support.Remaining >= 0, "beard-adjusted digger budget remains sustainable");
        Check(candidate.NguBonus == 3, "fixed-level NGU activation combines potential temporary and permanent getters");
        Check(LoadoutPresetPlanner.BeardBonus(s, new int[0], 0) == 1.5f, "deselecting NGU retains its permanent bonus");
        Check(LoadoutPresetPlanner.BeardBonus(s, new[] { 5 }, 2) == 6, "fixed-level adventure activation uses actual potential");
        Check(LoadoutPresetPlanner.BeardBonus(s, new[] { 1 }, 3) == 8, "fixed-level loot activation uses actual potential");
        s.BeardsDisabled = true;
        Check(LoadoutPresetPlanner.BeardBonus(s, new[] { 1, 3, 5, 6 }, 0) == 1,
            "disabled beards suppress temporary AND permanent effects");
        s = Snapshot(); r = Request(); s.ReservedEnergy = 1500;
        var illegal = Evaluate(s, r);
        Check(!illegal.Feasible && !illegal.Score.HasValue && illegal.Warnings.Any(x => x.Contains("basic training")), "R19 retained training invalidates undersized gear with meaningful reason");
        Stat(s.Items[2], PresetStat.EnergyCap, 50);
        var legal = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[2] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "legal", "Legal");
        Check(legal.Feasible && illegal.ConstraintFitness < legal.ConstraintFitness, "R19 equality is legal and capacity fitness guides search toward it");
        var plan = LoadoutPresetPlanner.Plan(s, r);
        Check(plan.Candidates[1].Feasible, "R19 weighted search prefers legal retained-training capacity");
        s = Snapshot(); s.NguAutoAdvance = true; s.EnergyNgu[0].Target = 10001;
        Rejected(() => LoadoutPresetPlanner.Plan(s, Request()), "auto-advance", "R20 finite unfinished weighted target rejects before forecast");
        s.EnergyNgu[0].Target = 0;
        Check(LoadoutPresetPlanner.Plan(s, Request()).Candidates[0].Feasible && s.NguAutoAdvance, "R20 unlimited targets remain supported without changing preference");
        s.EnergyNgu[0].Target = 10001; s.EnergyNgu[0].Weight = 0;
        LoadoutPresetPlanner.RequireSupportedNguAdvance(s);
        Check(s.NguAutoAdvance, "R20 excluded finite target receives no allocation and does not disable native preference");
    }
}
