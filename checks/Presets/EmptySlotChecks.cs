using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    private static void EmptySlotReplayChecks()
    {
        var s = Snapshot(); var request = Request();
        var empty = new PresetItem { Id = 0, SourceSlot = -5, Group = -5, Name = "Empty" };
        var weapon = new PresetItem { Id = 41, SourceSlot = -6, Group = -5, Name = "Offhand weapon" };
        Stat(weapon, PresetStat.EnergyPower, 400); Stat(weapon, PresetStat.MagicPower, 400);
        s.Slots = new[] { -5, -6 }; s.Factors = new[] { 1d, .5 };
        s.Current = new[] { empty, weapon }; s.Items = new[] { weapon };
        var plan = LoadoutPresetPlanner.Plan(s, request);
        Check(plan.Candidates[0].Items.Select(x => x.Id).SequenceEqual(new[] { 0, 41 }), "empty-main fixture retains current [0,41]");
        Check(plan.Candidates[1].Items.Select(x => x.Id).SequenceEqual(new[] { 41, 0 }), "source-linked search moves offhand into empty main [41,0]");
        var fresh = s.ProgressionCopy();
        fresh.Current = s.Current.Select(x => new PresetItem { Id = x.Id, SourceSlot = x.SourceSlot, Group = x.Group, Level = x.Level,
            Name = x.Name, Stats = x.Stats.ToArray(), ResourceSpecs = x.ResourceSpecs.ToArray() }).ToArray();
        fresh.Items = fresh.Current.Where(x => x.Id != 0).ToArray();
        var replay = LoadoutPresetPlanner.Replay(fresh, request, plan);
        Check(replay.Candidates[0].Feasible && replay.Candidates[0].Score.HasValue && !replay.Candidates[0].SetupChanged,
            "apply-current replay succeeds even when another cached candidate moved the empty hand");
        Check(replay.Candidates[0].Items.SequenceEqual(fresh.Current), "current replay reuses fresh current empty slots without a phantom setup change");
        Check(replay.Candidates[1].Items.Select(x => x.Id).SequenceEqual(new[] { 41, 0 }), "replay retains moved-empty candidate instead of substituting current offhand occupant");
        Check(replay.Candidates[1].Items[1].Group == -5 && replay.Candidates[1].Items[1].SourceSlot == -6,
            "virtual empty has the destination hand's valid group and slot");
        var movedPolicies = replay.Candidates.Concat(replay.Specialists).Where(x => x.Items.Select(i => i.Id).SequenceEqual(new[] { 41, 0 })).ToArray();
        Check(movedPolicies.Length > 1, "fixture includes multiple roles with the same moved-empty equipment setup");
        Check(movedPolicies.All(x => x.Items.SequenceEqual(movedPolicies[0].Items)), "canonical empties do not manufacture internal swaps between identical replayed setups");

        // Also retain a pinned empty main hand when the only weapon is moving the opposite way.
        weapon.SourceSlot = -5; empty.SourceSlot = -6; s.Current = new[] { weapon, empty };
        var norms = Enumerable.Repeat(1d, 6).ToArray(); var weights = LoadoutPresetPlanner.NormalizeWeights(request.Weights);
        var current = Evaluate(s, request);
        var moved = LoadoutPresetPlanner.Evaluate(s, request, new[] { empty, weapon }, weights, norms, "moved", "Moved");
        var pinned = new PresetPlan { Candidates = new[] { current, moved }, Specialists = new PresetCandidate[0], Normalization = norms,
            ReferenceKeys = Enumerable.Repeat("current", 6).ToArray(), ReferenceProduction = norms.Select(x => (double?)x).ToArray() };
        replay = LoadoutPresetPlanner.Replay(s.ProgressionCopy(), request, pinned);
        Check(replay.Candidates[1].Items.Select(x => x.Id).SequenceEqual(new[] { 0, 41 }) && replay.Candidates[1].Feasible,
            "replay preserves pinned empty-main policy when moving weapon into offhand");
        moved.Items = new[] { new PresetItem { Group = -1, SourceSlot = -5, Name = "Invalid empty group" }, weapon };
        Rejected(() => LoadoutPresetPlanner.Replay(s, request, pinned), "gear", "replay rejects an empty placeholder with the wrong destination group");
    }
}
