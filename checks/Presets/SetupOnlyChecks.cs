using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void SetupOnlyChecks()
    {
        var s = ActivitySnapshot();
        s.Items = Snapshot().Items;
        var r = ActivityRequest(0, 9);
        r.Weights[9] = 100;
        r.SetupOnly = true;
        r.ATWeights = new[] { 0, 0, 0, 1, 1 };
        var plan = LoadoutPresetPlanner.Plan(s, r);
        var winner = plan.Candidates[1];
        Check(winner.Feasible && winner.Score.HasValue && LoadoutPresetPlanner.Finite(winner.Score.Value),
            "setup-only mixed training search returns a finite feasible winner");
        Check(winner.Metrics[0].HasValue && winner.Metrics[9].HasValue && winner.Activities.AT.Skip(3).All(x => x > 0)
            && winner.Activities.AT.Take(3).All(x => x == 0), "setup-only uses requested Wandoos AT lanes, not mandatory adventure lanes");
        Check(plan.Specialists[0].Key == "focus-energyNgu" && plan.Specialists[9].Key == "focus-advancedTraining",
            "setup-only builds both requested positive specialist references");
        Check(plan.Specialists.Where((_, g) => r.Weights[g] == 0).All(c => c.Key == "current" && c.Items.SequenceEqual(s.Current)),
            "setup-only inactive references fall back to current without specialist searches");
        Check(plan.Sequence == null && r.Copy().SetupOnly && !new PresetRequest().SetupOnly,
            "setup-only is internal opt-in, survives copy and never creates a sequence");
        Check(s.RequiredATTargets.Length == 0 && s.AdvancedTraining.All(x => x.Target == 0),
            "setup-only search leaves captured targets unchanged");

        s.Available[9] = false;
        var unavailable = LoadoutPresetPlanner.Plan(s, r).Candidates[1];
        Check(!unavailable.Score.HasValue && !unavailable.Feasible,
            "setup-only cannot silently drop an unavailable positive training objective");
        s = Snapshot(); r = Request(); r.SetupOnly = true;
        Check(LoadoutPresetPlanner.Plan(s, r).Sequence == null,
            "setup-only also suppresses legacy NGU-only sequence search");
        s.NguAutoAdvance = true; s.EnergyNgu[0].Target = s.EnergyNgu[0].Level + 1;
        Rejected(() => LoadoutPresetPlanner.Plan(s, r), "auto-advance",
            "setup-only flag alone does not bypass ordinary NGU execution safety; prep must adapt its numeric capture");
    }
}
