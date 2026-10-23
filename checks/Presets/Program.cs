using System;
using System.Linq;
using NGUInjector.Managers;

internal static partial class Program
{
    static partial void ActivityChecks();
    static partial void AdventureObjectiveChecks();
    static partial void CapacityObjectiveChecks();
    static partial void ResourceDemandChecks();
    static partial void LanePriorityChecks();
    private static int MaximumPlanEvaluations => 6000 + 64 + LoadoutPresetPlanner.Keys.Length * 3 + 2 * (LoadoutPresetPlanner.Keys.Length + 1);
    private static int assertions;
    private static void Check(bool value, string name) { assertions++; if (!value) throw new Exception(name); }
    private static void Near(double a, double b, string name) { Check(Math.Abs(a - b) <= Math.Max(1, Math.Abs(b)) * 1e-9, name); }
    private static void Stat(PresetItem item, PresetStat stat, double percent)
    {
        item.Stats[(int)stat] = percent;
        if (stat == PresetStat.EnergyPower) item.ResourceSpecs[(int)ResourceSpec.EnergyPower] = (float)percent;
        if (stat == PresetStat.MagicPower) item.ResourceSpecs[(int)ResourceSpec.MagicPower] = (float)percent;
        if (stat == PresetStat.EnergyCap) item.ResourceSpecs[(int)ResourceSpec.EnergyCap] = (float)(percent * 100);
        if (stat == PresetStat.MagicCap) item.ResourceSpecs[(int)ResourceSpec.MagicCap] = (float)(percent * 100);
    }
    private static PresetSnapshot Snapshot()
    {
        var current = new PresetItem { Id = 40, Group = -5, SourceSlot = -5, Name = "Current" };
        var energy = new PresetItem { Id = 41, Group = -5, SourceSlot = 0, Name = "Energy" };
        Stat(energy, PresetStat.EnergyPower, 400);
        var magic = new PresetItem { Id = 42, Group = -5, SourceSlot = 1, Name = "Magic" };
        Stat(magic, PresetStat.MagicPower, 400);
        var balanced = new PresetItem { Id = 43, Group = -5, SourceSlot = 2, Name = "Balanced" };
        Stat(balanced, PresetStat.EnergyPower, 230); Stat(balanced, PresetStat.MagicPower, 230);
        return new PresetSnapshot { Items = new[] { current, energy, magic, balanced }, Current = new[] { current },
            Slots = new[] { -5 }, Factors = new[] { 1d }, Energy = 1000, Magic = 1000, EnergyCap = 1000, MagicCap = 1000,
            EnergyPower = 1, MagicPower = 1, BasePower = 1000, BaseToughness = 1000, Power = 1000, Toughness = 1000,
            EnergyCapacity = new ResourceProjection { Energy = true, Capacity = true, BaseCap = 1000 },
            MagicCapacity = new ResourceProjection { Capacity = true, BaseCap = 1000 },
            EnergyPowerProjection = new ResourceProjection { Energy = true, BasePower = 1 },
            MagicPowerProjection = new ResourceProjection { BasePower = 1 },
            Drop = 1, Respawn = 4, AttackSeconds = 1, IdleModifier = 1.2, GrossGold = 1000, GoldActiveGross = 1000, GoldInactiveGross = 1000, Gold = 1000,
            EnergyNgu = new[] { new PresetNgu { Level = 10000, HardCap = 1000000000, Weight = 1, Coefficient = .001 } },
            MagicNgu = new[] { new PresetNgu { Level = 10000, HardCap = 1000000000, Weight = 1, Coefficient = .001 } },
            Available = new[] { true, true, true, false, false, false },
            Rituals = new[] { new PresetRitual { Id = 0, Coefficient = .0001, Blood = 10, Cost = 1, Unlocked = true } } };
    }
    private static PresetRequest Request() { return new PresetRequest { Weights = new[] { 1d, 1, 0, 0, 0, 0 }, Swap = 0, Horizon = 1000 }; }
    private static PresetCandidate Evaluate(PresetSnapshot s, PresetRequest r)
    { return LoadoutPresetPlanner.Evaluate(s, r, s.Current, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "current", "Current"); }
    private static void Rejected(Action action, string message, string name)
    {
        try { action(); } catch (InvalidOperationException error) { Check(error.Message.Contains(message), name); return; }
        Check(false, name);
    }
    private static void IdentityChecks()
    {
        var token = new string('a', 32);
        var guard = new PresetPreviewIdentity { Id = token, Definition = "canonical-preset", Material = "inventory-and-support-v1",
            EnergyWeights = new[] { int.MaxValue }, MagicWeights = new[] { 1 }, Created = 10, Rebirth = 3, Elapsed = 20, Total = 120 };
        guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 130, 3, 35, 135);
        Check(PresetPreviewIdentity.IsToken(token) && !PresetPreviewIdentity.IsToken(new string('A', 32)), "preview token format and exact 120-second validity boundary");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 130.001, 3, 35, 135), "expired", "expired token rejected");
        Rejected(() => guard.Validate(new string('b', 32), guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 3, 20, 120), "invalid", "different server token rejected");
        Rejected(() => guard.Validate(token, "edited-preset", guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 3, 20, 120), "definition", "edited preset requires new preview");
        Rejected(() => guard.Validate(token, guard.Definition, new[] { 1 }, guard.MagicWeights, guard.Material, 20, 3, 20, 120), "weights", "changed energy weights rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, new[] { 2 }, guard.Material, 20, 3, 20, 120), "weights", "changed magic weights rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, "inventory-and-support-v2", 20, 3, 20, 120), "material", "changed material fingerprint rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 4, 20, 120), "epoch", "rebirth epoch rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 3, 19, 120), "clock", "rebirth time rollback rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 3, 19.999, 120), "clock", "sub-tick rollback is not forgiven");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 3, 20, 119), "clock", "total playtime rollback rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 20, 3, 30, 135), "epoch", "changed save time origin rejected");
        Rejected(() => guard.Validate(token, guard.Definition, guard.EnergyWeights, guard.MagicWeights, guard.Material, 9, 3, 20, 120), "invalid", "monotonic clock rollback rejected");
    }
    private static void ReplayChecks()
    {
        var s = Snapshot(); var r = Request(); r.PlannedSupport = true; r.BloodPercent = 0;
        s.Diggers = new[] { new SupportDigger { Id = 10, Name = "Blood", Maximum = 20, BaseDrain = 1, Growth = 2, ConsumesGold = true, PerLevel = .1 } };
        s.DiggerSlots = 1;
        var plan = LoadoutPresetPlanner.Plan(s, r); var policy = plan.Specialists[2];
        Check(policy.BloodPercent == 100 && policy.Blood > 0 && policy.MagicNgu == 0, "blood full-focus overrides fixed zero blood split explicitly");
        r.BloodPercent = 100;
        Check(LoadoutPresetPlanner.Plan(s, r).Specialists[1].BloodPercent == 0, "magic NGU full-focus overrides fixed all-blood split");
        var fresh = s.ProgressionCopy(); fresh.GrossGold = fresh.GoldActiveGross = fresh.GoldInactiveGross = 2000; fresh.EnergyNgu[0].Level += 50;
        fresh.Rituals = new[] { new PresetRitual { Id = 0, Unlocked = true, Coefficient = .0001, Cost = 1, Blood = 10 },
            new PresetRitual { Id = 1, Unlocked = true, Coefficient = .0001, Cost = 1, Blood = 1e6 } };
        var better = new PresetItem { Id = 99, SourceSlot = 99, Group = -5, Name = "Newly better item" };
        Stat(better, PresetStat.EnergyPower, 1e9); Stat(better, PresetStat.MagicPower, 1e9);
        fresh.Items = fresh.Items.Concat(new[] { better }).ToArray();
        Check(LoadoutPresetPlanner.Plan(fresh, r).Candidates[1].Items[0].Id == 99, "fresh optimizer would choose different gear in replay regression fixture");
        var replay = LoadoutPresetPlanner.Replay(fresh, r, plan).Specialists[2];
        Check(replay.Ritual == policy.Ritual, "replay cannot replace ritual with a better new ritual");
        Check(replay.BloodPercent == policy.BloodPercent && replay.Items.SequenceEqual(policy.Items), "replay preserves selected split and exact gear");
        Check(replay.Support.Diggers.Single().Level == policy.Support.Diggers.Single().Level, "replay does not upgrade support when fresh GPS increases");
        var missing = fresh.ProgressionCopy(); missing.Items = fresh.Items.Where(x => x.SourceSlot != policy.Items[0].SourceSlot).ToArray();
        Rejected(() => LoadoutPresetPlanner.Replay(missing, r, plan), "gear", "exact-policy replay rejects missing gear instead of choosing a new item");
        var smaller = fresh.ProgressionCopy(); smaller.GrossGold = smaller.GoldActiveGross = smaller.GoldInactiveGross = 1;
        Check(!LoadoutPresetPlanner.Evaluate(smaller, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), plan.Normalization, policy.Key, policy.Label, policy).Feasible,
            "infeasible pinned support is rejected rather than resized");
        fresh.Rituals = fresh.Rituals.Where(x => x.Id != policy.Ritual).ToArray();
        Check(!LoadoutPresetPlanner.Evaluate(fresh, r, policy.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), plan.Normalization, policy.Key, policy.Label, policy).Feasible,
            "missing pinned ritual is rejected, not replaced");
        s = Snapshot(); r = Request(); r.Weights = new[] { 0d, 0, 0, 0, 1, 0 }; r.Zone = 0;
        s.Available[4] = s.Available[5] = true;
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 10, IdleToughness = 10, OneHitPower = 1000, NormalRatio = 1,
            BoostValues = new[] { 10d }, Chances = new[] { .5 }, Caps = new[] { 1d } },
            new PresetZone { Id = 1, IdlePower = 10, IdleToughness = 10, OneHitPower = 1000, NormalRatio = 1,
            BoostValues = new[] { 1000d }, Chances = new[] { .5 }, Caps = new[] { 1d } } };
        Check(Evaluate(s, r).Zone == 0, "selected regular zone constrains boosts, not just zone objective");
        r.Zone = 3; s.Available[3] = true; s.PpPerKill = Enumerable.Repeat(200d, 100).ToArray();
        Check(!Evaluate(s, r).Feasible && !Evaluate(s, r).Score.HasValue, "locked selected zone cannot silently fall back to ITOPOD");
        r.Zone = -1; var automatic = Evaluate(s, r);
        Check(automatic.Zone == 1, "unconstrained boost reference really prefers better zone");
        s.Zones = s.Zones.Where(x => x.Id == 0).ToArray();
        Check(!LoadoutPresetPlanner.Evaluate(s, r, automatic.Items, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), automatic.Key, automatic.Label, automatic).Feasible,
            "replay rejects unavailable pinned route instead of finding another");
    }
    private static void BeardRotationChecks()
    {
        var s = Snapshot(); var r = Request(); r.PlannedSupport = true;
        s.BeardSlots = 5; s.UnlockedBeards = Enumerable.Range(0, 7).ToArray();
        r.BeardPriority = new[] { 1, 3, 5, 6 }; r.BeardRotation = new[] { 0, 2, 4 };
        var initial = Evaluate(s, r);
        Check(initial.Support.Beards.OrderBy(x => x).SequenceEqual(new[] { 0, 1, 3, 5, 6 }), "preview fills initial optional slots deterministically");
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(initial.Support.Beards), "repeated preview does not roll RNG");
        var random = new Random(42); var current = initial.Support.Beards;
        for (var i = 0; i < 100; i++)
        {
            var next = LoadoutPresetPlanner.SelectRotationBeards(s, r.BeardPriority, r.BeardRotation, current, random);
            Check(r.BeardPriority.All(next.Contains) && next.Length == 5, "required beards stay enabled across rotations " + i);
            Check(!next.OrderBy(x => x).SequenceEqual(current.OrderBy(x => x)), "minute roll avoids unchanged selection when alternatives exist " + i);
            current = next;
        }
        s.BeardSlots = 2;
        Check(LoadoutPresetPlanner.SelectRotationBeards(s, r.BeardPriority, r.BeardRotation, current, random).SequenceEqual(new[] { 1, 3 }), "required order wins when cap is below required count");
        s.UnlockedBeards = new[] { 0, 1, 2, 3, 4, 5 }; s.BeardSlots = 5;
        Check(LoadoutPresetPlanner.SelectRotationBeards(s, new[] { 6, 3 }, new[] { 4 }).SequenceEqual(new[] { 3, 4 }), "locked required IDs and excluded pool never fill unused slots");
        r.BeardPriority = new int[0]; r.BeardRotation = new int[0];
        Check(Evaluate(s, r).Support.Beards.Length == 0, "both explicit empty arrays mean no beards, not legacy auto");
        r.PlannedSupport = false; s.CurrentBeards = new[] { 3 };
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(s.CurrentBeards), "current support ignores explicit empty beard settings");
        r.BeardPriority = new[] { 1, 5 }; r.BeardRotation = new[] { 0, 2, 4 };
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(s.CurrentBeards), "current support ignores nonempty enabled and optional settings");
        Check(!Evaluate(s, r).Support.Notes.Any(note => note.Contains("rerolls")), "current support never advertises minute rotation");
        r.BeardRotation = null;
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(s.CurrentBeards), "null rotation preserves legacy current support");

        var d = new SupportDigger { Id = 4, Level = 4, Maximum = 10, BaseDrain = 100, Growth = 2, ConsumesGold = true };
        s.UnlockedBeards = Enumerable.Range(0, 7).ToArray();
        s.Diggers = new[] { d }; s.DiggerSlots = 1; s.GoldActiveGross = 2000; s.GoldInactiveGross = 400;
        s.CurrentDiggers = new[] { d }; s.CurrentBeards = new[] { 6 };
        r.DiggerWeights = new double[12]; r.DiggerPercent = 0;
        Check(Evaluate(s, r).Support.Diggers.Single().Level == 4 && Evaluate(s, r).Support.Beards.Single() == 6,
            "current support ignores digger weights and budget while preserving observed levels and Gold");
        r.DiggerPercent = 75;
        var acquired = new PresetSupport { Beards = new[] { 6 }, Diggers = new[] { d }, DiggerWeights = new double[12], Notes = new string[0] };
        acquired.DiggerWeights[4] = 1; r.PlannedSupport = true;
        var reduced = LoadoutPresetPlanner.RotationSupport(s, r, acquired, new[] { 3 });
        Check(reduced.Diggers.Single().Level == 2 && reduced.Diggers.Single().Drain(2) <= 300, "Gold removal reduces acquired digger funding to new budget");
        Check(acquired.Diggers.Single().Level == 4 && acquired.Beards.Single() == 6, "rotation funding never mutates acquired support");
        Check(LoadoutPresetPlanner.RotationSupport(s, r, acquired, new[] { 6 }).Diggers.Single().Level == 4, "Gold return restores exactly the acquired level ceiling");
        r.PlannedSupport = false;
        Rejected(() => LoadoutPresetPlanner.RotationSupport(s, r, acquired, new[] { 3 }), "requires planned", "rotation funding cannot run for current support");
    }

    private static void ProgressionChecks()
    {
        var s = Snapshot(); var r = Request(); r.Horizon = 100;
        Stat(s.Items[1], PresetStat.EnergyPower, 1900); Stat(s.Items[2], PresetStat.MagicPower, 1900);
        s.EnergyNgu[0].Target = 10001;
        var p = LoadoutPresetPlanner.Plan(s, r);
        var bestSingle = p.Candidates.Concat(p.Specialists).Where(x => x.Score.HasValue).Max(x => x.Score.Value);
        Check(p.Sequence.Score > bestSingle + .01, "target saturation makes a real two-phase sequence superior to every single setup");
        var phase = s.ProgressionCopy(); LoadoutPresetPlanner.ProjectPhase(phase, p.Specialists[0], 25);
        Near(phase.EnergyNgu[0].Level, 10001, "phase one completes useful NGU target");
        LoadoutPresetPlanner.ProjectPhase(phase, p.Specialists[1], 75);
        Near(phase.EnergyNgu[0].Level, 10001, "phase two cannot earn the completed target again");
        Near(s.EnergyNgu[0].Level, 10000, "sequence projections never mutate captured NGU levels");
        r.Swap = 60; var costly = LoadoutPresetPlanner.Plan(s, r);
        Check(costly.Sequence == null || costly.Sequence.Improvement <= .001, "swap cost reverses the useful saturation sequence");
        r.Swap = 0;
        s.EnergyNgu = new[] { new PresetNgu { Level = 10000, Target = 10001, HardCap = 1000000000, Weight = 1, Coefficient = .001 },
            new PresetNgu { Level = 10000, Target = 0, HardCap = 1000000000, Weight = 1, Coefficient = .001 } };
        s.Available[3] = true; s.PpPerKill = Enumerable.Repeat(200d, 100).ToArray();
        r.Horizon = 275;
        p = LoadoutPresetPlanner.Plan(s, r);
        var forward = s.ProgressionCopy(); var reverse = s.ProgressionCopy();
        var first = LoadoutPresetPlanner.ProjectPhase(forward, p.Specialists[0], 25);
        var second = LoadoutPresetPlanner.ProjectPhase(forward, p.Specialists[1], 250);
        var reversedFirst = LoadoutPresetPlanner.ProjectPhase(reverse, p.Specialists[1], 250);
        var reversedSecond = LoadoutPresetPlanner.ProjectPhase(reverse, p.Specialists[0], 25);
        Check(first[0] + second[0] > reversedFirst[0] + reversedSecond[0] + .05,
            "order matters because phase-two sharing excludes a target completed in phase one");
        Near((first[3] + second[3]).Value, (reversedFirst[3] + reversedSecond[3]).Value,
            "frozen-rate PP time sharing stays order independent, unlike target-aware NGU progression");
        s = Snapshot(); r = Request(); Stat(s.Current[0], PresetStat.EnergyCap, 100); s.EnergyCapacity.BaseCap = 500;
        Stat(s.Items[2], PresetStat.EnergyCap, 300);
        var weights = LoadoutPresetPlanner.NormalizeWeights(r.Weights); var norms = Enumerable.Repeat(1d, 6).ToArray();
        var lowCap = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, weights, norms, "low", "Low cap");
        var highCap = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[2] }, weights, norms, "high", "High cap");
        phase = s.ProgressionCopy(); LoadoutPresetPlanner.ProjectPhase(phase, lowCap, 25); LoadoutPresetPlanner.ProjectPhase(phase, highCap, 75);
        Near(phase.Energy, 500, "phase-two higher cap does not refill phase-one cap-shrunk pool");
        var shares = LoadoutPresetPlanner.NguShares(new[] { new PresetNgu { Coefficient = 1, HardCap = 1000000000, Weight = int.MaxValue } }, 1000000, 1, 5);
        Near(shares[0], 253, "initial allocation matches five-second production cap with extra tick and rounding buffer, not full horizon");
    }
    private static bool SameValues<T>(T[] left, T[] right)
    {
        return left == null || right == null ? left == right : left.SequenceEqual(right);
    }

    private static bool SupportSemanticallyEqual(PresetSupport left, PresetSupport right)
    {
        if (left == null || right == null) return left == right;
        return SameValues(left.Beards, right.Beards) && SameValues(left.DiggerWeights, right.DiggerWeights)
            && SameValues(left.Notes, right.Notes) && left.Budget == right.Budget
            && left.Drain == right.Drain && left.Remaining == right.Remaining
            && (left.Diggers == null || right.Diggers == null ? left.Diggers == right.Diggers
                : left.Diggers.Length == right.Diggers.Length && left.Diggers.Zip(right.Diggers, (a, b) =>
                    a.Id == b.Id && a.Name == b.Name && a.Maximum == b.Maximum && a.Level == b.Level
                    && a.BaseDrain == b.BaseDrain && a.Growth == b.Growth && a.StartBonus == b.StartBonus
                    && a.PerLevel == b.PerLevel && a.ConsumesGold == b.ConsumesGold).All(equal => equal));
    }

    private static PresetSupport CopyCheckSupport()
    {
        return new PresetSupport { Beards = new[] { 6, 3 }, DiggerWeights = new[] { .25, 0, .75 },
            Notes = new[] { "acquired", "pinned" }, Budget = 1234, Drain = 321, Remaining = 913,
            Diggers = new[] {
                new SupportDigger { Id = 4, Name = "Gold", Maximum = 123, Level = 7, BaseDrain = 2.5,
                    Growth = 1.2, StartBonus = 3.5, PerLevel = .75, ConsumesGold = true },
                new SupportDigger { Id = 2, Name = "Free", Maximum = 456, Level = 8, BaseDrain = 4.5,
                    Growth = 1.3, StartBonus = 5.5, PerLevel = .25, ConsumesGold = false } } };
    }

    private static void SupportCopyChecks()
    {
        var baseline = new PresetCandidate { Support = CopyCheckSupport() };
        var copy = baseline.Copy();
        Check(SupportSemanticallyEqual(copy.Support, baseline.Support), "candidate copy retains every acquired support value and metadata");
        Check(!ReferenceEquals(copy.Support, baseline.Support)
            && !ReferenceEquals(copy.Support.Beards, baseline.Support.Beards)
            && !ReferenceEquals(copy.Support.Diggers, baseline.Support.Diggers)
            && !ReferenceEquals(copy.Support.DiggerWeights, baseline.Support.DiggerWeights)
            && !ReferenceEquals(copy.Support.Notes, baseline.Support.Notes)
            && copy.Support.Diggers.Zip(baseline.Support.Diggers, (a, b) => !ReferenceEquals(a, b)).All(independent => independent),
            "candidate support copy owns independent arrays and every digger row");
        copy.Support.Beards[0] = 0; copy.Support.DiggerWeights[0] = 99; copy.Support.Notes[0] = "observed";
        copy.Support.Budget = 1; copy.Support.Drain = 2; copy.Support.Remaining = 3;
        foreach (var digger in copy.Support.Diggers)
        {
            digger.Id = 99; digger.Name = "changed"; digger.Maximum = 1; digger.Level = 0;
            digger.BaseDrain = 9; digger.Growth = 8; digger.StartBonus = 7; digger.PerLevel = 6;
            digger.ConsumesGold = !digger.ConsumesGold;
        }
        Check(SupportSemanticallyEqual(baseline.Support, CopyCheckSupport()), "mutating every copied support field cannot redefine acquired baseline");
        var absent = new PresetCandidate();
        Check(absent.Copy().Support == null, "candidate copy preserves uncontrolled null support");
        var sparse = new PresetCandidate { Support = new PresetSupport { Beards = null, Diggers = null, DiggerWeights = null, Notes = null } };
        Check(!ReferenceEquals(sparse.Copy().Support, sparse.Support) && SupportSemanticallyEqual(sparse.Copy().Support, sparse.Support),
            "candidate copy preserves null versus controlled-empty support fields");
        var empty = new PresetCandidate { Support = new PresetSupport { Beards = new int[0], Diggers = new SupportDigger[0], Notes = new string[0] } };
        Check(SupportSemanticallyEqual(empty.Copy().Support, empty.Support) && !SupportSemanticallyEqual(empty.Support, sparse.Support),
            "candidate copy preserves controlled-empty support without conflating null");
    }

    private static void IronPillChecks()
    {
        var before = assertions;
        var s = Snapshot(); var r = Request(); r.BloodPercent = 0;
        s.Magic = 5000; // The acquired candidate, not the captured uncapped pool, owns the budget.
        s.MagicNgu = new[] {
            new PresetNgu { Level = 10000, HardCap = 1000000000, Coefficient = .001, Weight = 1 },
            new PresetNgu { Level = 10000, HardCap = 1000000000, Coefficient = .001, Weight = 3 },
            new PresetNgu { Level = 10000, HardCap = 1000000000, Coefficient = .001, Weight = 0 } };
        s.Rituals = new[] {
            new PresetRitual { Id = 0, Unlocked = true, Coefficient = .0001, Blood = 20, Cost = 1 },
            new PresetRitual { Id = 1, Unlocked = true, Coefficient = .01, Blood = 10, Cost = 1 },
            new PresetRitual { Id = 2, Unlocked = false, Coefficient = 1, Blood = 1e6, Cost = 1 },
            new PresetRitual { Id = 3, Unlocked = true, Coefficient = 1, Blood = 1e6, Cost = 1000 } };
        var candidate = Evaluate(s, r);
        Check(candidate.Feasible && candidate.MagicPool == 1000 && candidate.Blood == 0 && candidate.Ritual == -1,
            "Iron Pill fixture acquires capped magic with a zero-blood policy");
        Check(candidate.MagicShares.SequenceEqual(new long[] { 250, 750, 0 }), "Iron Pill fixture original exact NGU split");
        var overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, candidate);
        Check(!ReferenceEquals(overlay, candidate) && overlay.Ritual == 1 && overlay.Blood == 101,
            "Iron Pill selects highest sustainable blood per second, not largest blood per fill, locked or unaffordable ritual");
        Check(overlay.MagicNgu == 899 && overlay.MagicShares.SequenceEqual(new long[] { 225, 674, 0 }),
            "Iron Pill caps the productive ritual and redistributes exact overflow by global NGU weights");
        Check(overlay.Blood + overlay.MagicNgu == candidate.MagicPool && s.Magic == 5000,
            "Iron Pill never allocates uncapped captured magic or mutates snapshot resources");
        Check(candidate.Ritual == -1 && candidate.Blood == 0 && candidate.BloodPercent == 0 && candidate.MagicNgu == 1000
            && candidate.MagicShares.SequenceEqual(new long[] { 250, 750, 0 }), "Iron Pill leaves the original acquired candidate immutable");
        Check(r.BloodPercent == 0 && r.Weights.SequenceEqual(new[] { 1d, 1, 0, 0, 0, 0 }) && overlay.BloodPercent == 0,
            "Iron Pill never rewrites the preset request or pins a temporary percentage");
        Check(ReferenceEquals(overlay.Items, candidate.Items) && SupportSemanticallyEqual(overlay.Support, candidate.Support)
            && !ReferenceEquals(overlay.Support, candidate.Support)
            && overlay.Zone == candidate.Zone && overlay.Floor == candidate.Floor && overlay.Score == candidate.Score,
            "Iron Pill retains acquired gear, support, route and score");

        var activityPolicy = candidate.Copy();
        activityPolicy.Energy = 600; activityPolicy.EnergyShares = new long[] { 600 };
        activityPolicy.Activities = new PresetActivityAllocation { TimeMachineEnergy = 50, WandoosEnergy = 60,
            AugmentId = 4, AugmentEnergy = 70, UpgradeEnergy = 80, AT = new long[] { 20, 30, 40, 25, 25 },
            TimeMachineMagic = 100, WandoosMagic = 200, Os = 2 };
        activityPolicy.MagicNgu = 700; activityPolicy.MagicShares = new long[] { 175, 525, 0 };
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, activityPolicy);
        Check(overlay.Blood == 101 && overlay.MagicNgu == 599 && overlay.MagicShares.SequenceEqual(new long[] { 150, 449, 0 }),
            "Iron Pill subtracts dedicated TM and Wandoos magic before reserving or sharing overflow");
        Check(overlay.Energy == 600 && overlay.EnergyPool == 1000 && overlay.EnergyShares.SequenceEqual(new long[] { 600 })
            && overlay.Activities.TimeMachineEnergy == 50 && overlay.Activities.WandoosEnergy == 60
            && overlay.Activities.AugmentId == 4 && overlay.Activities.AugmentEnergy == 70 && overlay.Activities.UpgradeEnergy == 80
            && overlay.Activities.AT.SequenceEqual(new long[] { 20, 30, 40, 25, 25 }), "Iron Pill preserves every energy allocation exactly");
        Check(overlay.Activities.TimeMachineMagic == 100 && overlay.Activities.WandoosMagic == 200 && overlay.Activities.Os == 2
            && overlay.Blood + overlay.MagicNgu + overlay.Activities.MagicTotal == 1000, "Iron Pill preserves dedicated magic and installed OS");
        Check(activityPolicy.Blood == 0 && activityPolicy.Ritual == -1 && activityPolicy.MagicNgu == 700
            && activityPolicy.MagicShares.SequenceEqual(new long[] { 175, 525, 0 }) && activityPolicy.Activities.EnergyTotal == 400
            && activityPolicy.Activities.MagicTotal == 300, "Iron Pill leaves original activity candidate and allocations immutable");

        activityPolicy.ActivityGoldSpent = 999;
        Check(ReferenceEquals(activityPolicy, LoadoutPresetPlanner.ReserveIronPillMagic(s, activityPolicy)),
            "Iron Pill unpaid startup cannot double-spend activity gold when only the exact ritual cost remains");
        activityPolicy.ActivityGoldSpent = 998;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, activityPolicy);
        Check(overlay.Ritual == 1 && overlay.Blood == 101 && overlay.MagicNgu == 599
            && overlay.MagicShares.SequenceEqual(new long[] { 150, 449, 0 }), "Iron Pill unpaid startup resumes with gold strictly above cost after activity spending");
        activityPolicy.ActivityGoldSpent = 1001;
        Check(ReferenceEquals(activityPolicy, LoadoutPresetPlanner.ReserveIronPillMagic(s, activityPolicy)),
            "Iron Pill clamps negative liquid gold after activity spending and leaves original allocations alone");
        s.Rituals[1].Paid = true;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, activityPolicy);
        Check(overlay.Ritual == 1 && overlay.Blood == 101 && overlay.MagicNgu == 599,
            "Iron Pill paid ritual needs no second startup payment after activities consume liquid gold");
        Check(s.Gold == 1000 && activityPolicy.ActivityGoldSpent == 1001 && activityPolicy.Blood == 0
            && activityPolicy.MagicShares.SequenceEqual(new long[] { 175, 525, 0 }), "Iron Pill startup reservation never mutates liquid gold, activity spending or acquired allocations");
        s.Rituals[1].Paid = false;

        s.MagicNgu[0].Weight = s.MagicNgu[1].Weight = 0;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, candidate);
        Check(overlay.Ritual == 1 && overlay.Blood == 101 && overlay.MagicNgu == 0 && overlay.MagicShares.SequenceEqual(new long[3]),
            "Iron Pill with all-zero NGU weights leaves overflow idle, never enables excluded rows");
        s.MagicNgu[0].Weight = 1; s.Available[1] = false;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, candidate);
        Check(overlay.Blood == 101 && overlay.MagicNgu == 0 && overlay.MagicShares.SequenceEqual(new long[3]),
            "Iron Pill cannot allocate overflow to unavailable NGU even with a positive weight");
        s.Available[1] = true;
        s.MagicNgu[0].Target = s.MagicNgu[0].Level;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, candidate);
        Check(overlay.Blood == 101 && overlay.MagicNgu == 0 && overlay.MagicShares.SequenceEqual(new long[3]),
            "Iron Pill leaves completed NGU target overflow idle");
        s.MagicNgu[0].Target = 0; s.MagicNgu[1].Weight = 3;

        var cappedNgu = s.ProgressionCopy();
        cappedNgu.MagicNgu[0].Level = 0; cappedNgu.MagicNgu[0].Target = 1; cappedNgu.MagicNgu[0].Coefficient = 1;
        cappedNgu.MagicNgu[1].Weight = 0;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(cappedNgu, candidate);
        Check(overlay.Blood == 101 && overlay.MagicNgu == 2 && overlay.MagicShares.SequenceEqual(new long[] { 2, 0, 0 }),
            "Iron Pill overflow respects the NGU target cap and leaves surplus magic idle");

        s.Rituals = new[] { new PresetRitual { Id = 4, Unlocked = true, Coefficient = .01, Blood = 10, Cost = 10 } };
        var funded = candidate.Copy(); funded.GrossGold = 300;
        funded.Support = new PresetSupport { Drain = 50 };
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, funded);
        // 50 magic costs exactly 250 GPS; the best sustainable plateau begins at 34, buffered to 35.
        Check(overlay.Ritual == 4 && overlay.Blood == 35 && overlay.MagicNgu == 965
            && overlay.MagicShares.SequenceEqual(new long[] { 241, 724, 0 }), "Iron Pill uses buffered start of gold-limited production plateau after digger drain");
        Check(LoadoutPresetPlanner.BloodFills(.01 * 33) < LoadoutPresetPlanner.BloodFills(.01 * overlay.Blood)
            && LoadoutPresetPlanner.BloodFills(.01 * 49) == LoadoutPresetPlanner.BloodFills(.01 * overlay.Blood)
            && LoadoutPresetPlanner.BloodFills(.01 * 50) * 10 == 250, "Iron Pill retains maximum sustainable production without wasting the whole plateau");
        s.Gold = 10;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill unpaid startup rejects gold equal to cost");
        s.Gold = 0; s.Rituals[0].Paid = true;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, funded);
        Check(overlay.Ritual == 4 && overlay.Blood == 35 && overlay.MagicNgu == 965, "Iron Pill already-paid partial ritual remains productive without startup gold");
        s.Rituals[0].Paid = false;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill unpaid unaffordable ritual does not steal NGU magic");
        s.Gold = 1000; s.Rituals[0].Unlocked = false;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill locked-only ritual leaves allocation untouched");
        s.Rituals[0].Unlocked = true; s.Rituals[0].Coefficient = 1e-12;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill nonproductive ritual below native progress gate leaves allocation untouched");
        s.Rituals[0].Coefficient = .01; funded.MagicPool = 7;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, funded);
        Check(overlay.Blood == 7 && overlay.Ritual == 4 && overlay.MagicNgu == 0 && overlay.MagicShares.SequenceEqual(new long[3]),
            "Iron Pill funds a productive pool-limited partial ritual instead of requiring a full cap");
        funded.MagicPool = 0;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill empty magic pool is a no-op");
        funded.MagicPool = 1000; funded.GrossGold = 50;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill zero gold after diggers is a no-op");
        funded.GrossGold = 300; funded.Feasible = false;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill cannot rescue an infeasible candidate");
        funded.Feasible = true; s.Available[2] = false;
        Check(ReferenceEquals(funded, LoadoutPresetPlanner.ReserveIronPillMagic(s, funded)), "Iron Pill locked blood magic is a no-op");
        s.Available[2] = true; s.Rituals[0].Coefficient = .001; s.Rituals[0].Cost = 1;
        funded.GrossGold = 80; // 30 GPS after the existing 50 GPS digger drain.
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, funded);
        Check(overlay.Blood == 501 && overlay.MagicNgu == 499 && overlay.MagicShares.SequenceEqual(new long[] { 125, 374, 0 }),
            "Iron Pill net GPS 30 reserves buffered 500-magic plateau start, not 999 magic");
        funded.GrossGold = 75;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, funded);
        Check(overlay.Blood == 335 && overlay.MagicNgu == 665 && overlay.MagicShares.SequenceEqual(new long[] { 166, 499, 0 }),
            "Iron Pill strict net GPS 25 uses first productive 334 magic plus rounding buffer, not 499 magic");
        Check(LoadoutPresetPlanner.BloodFills(.001 * 333) < LoadoutPresetPlanner.BloodFills(.001 * overlay.Blood)
            && LoadoutPresetPlanner.BloodFills(.001 * overlay.Blood) < 25 && LoadoutPresetPlanner.BloodFills(.001 * 500) == 25,
            "Iron Pill strict GPS boundary excludes the faster plateau at exact available income");
        s.Rituals[0].Coefficient = .25;
        overlay = LoadoutPresetPlanner.ReserveIronPillMagic(s, funded);
        Check(overlay.Blood == 1 && overlay.MagicNgu == 999 && overlay.MagicShares.SequenceEqual(new long[] { 250, 749, 0 }),
            "Iron Pill caps the rounding buffer at the sustainable amount instead of crossing a one-unit plateau");
        Console.WriteLine("Iron Pill numeric regressions: " + (assertions - before) + " assertions");
    }

    public static void Main()
    {
#if PRESET_MODEL_CHECKS
        ModelChecks();
#endif
        SupportCopyChecks(); LanePriorityChecks(); ActivityChecks(); AdventureObjectiveChecks(); CapacityObjectiveChecks(); EmptySlotReplayChecks(); IdentityChecks(); ReplayChecks(); BeardRotationChecks(); ProgressionChecks(); NumericChecks(); SupportIntegrationChecks(); MixedFocusChecks(); IronPillChecks(); ResourceDemandChecks();
        var s = Snapshot(); var r = Request(); var p = LoadoutPresetPlanner.Plan(s, r);
        Check(p.Candidates[1].Items[0].Id == 43, "actual mixed search chooses compromise, not either specialist");
        Check(p.Specialists[0].Items[0].Id == 41 && p.Specialists[1].Items[0].Id == 42, "actual specialist searches");
        var score = p.Candidates[1].Score.Value;
        r.Weights = new[] { 9d, 9, 0, 0, 0, 0 };
        Near(LoadoutPresetPlanner.Plan(s, r).Candidates[1].Score.Value, score, "weight scale invariance");
        var repeated = LoadoutPresetPlanner.Plan(s, r);
        Check(repeated.Candidates[1].Items.Select(x => x.Id).SequenceEqual(p.Candidates[1].Items.Select(x => x.Id)), "deterministic search");
        r.Weights = new[] { 0d, 1, 1, 0, 0, 0 }; r.BloodPercent = 0; var ngu = Evaluate(s, r);
        r.BloodPercent = 100; var blood = Evaluate(s, r);
        Check(ngu.Metrics[1] > blood.Metrics[1] && blood.Metrics[2] > ngu.Metrics[2], "shared magic tradeoff");
        Check(ngu.Energy == blood.Energy && blood.MagicNgu + blood.Blood <= s.Magic, "both lanes eligible, magic not double counted");
        Near(LoadoutPresetPlanner.BloodFills(.4), 50d / 3, "blood tick quantization");
        Near(LoadoutPresetPlanner.BloodFills(10), 50, "blood one fill per tick cap");
        Near(LoadoutPresetPlanner.BloodFills(1e-9), 0, "blood game minimum progress gate");
        s.GrossGold = s.GoldActiveGross = s.GoldInactiveGross = .1; Check(Evaluate(s, r).Metrics[2] == 0, "blood cannot spend more than shared gold");
        s.GrossGold = s.GoldActiveGross = s.GoldInactiveGross = 1000; s.Rituals[0].Unlocked = false; Check(Evaluate(s, r).Metrics[2] == 0, "ritual unlock gate");
        s.Rituals[0].Unlocked = true; s.Gold = 0;
        Check(Evaluate(s, r).Metrics[2] == 0 && Evaluate(s, r).Blood == 0, "startup liquid gold gate, no phantom allocation");
        s.Rituals[0].Paid = true;
        Check(Evaluate(s, r).Metrics[2] > 0, "already paid partial ritual can run without startup gold");
        s.Gold = 1000;
        s.DiggerSlots = 1;
        s.Diggers = s.CurrentDiggers = new[] { new SupportDigger { Id = 10, Level = 1, Maximum = 1, BaseDrain = 995, Growth = 2, ConsumesGold = true } };
        Check(Evaluate(s, r).Metrics[2] == 0, "blood uses gold remaining after active diggers, not gross GPS twice");
        s.GrossGold = s.GoldActiveGross = s.GoldInactiveGross = 1001; Check(Evaluate(s, r).Metrics[2] > 0, "ritual resumes when shared gold remains strictly positive");
        s.GrossGold = s.GoldActiveGross = s.GoldInactiveGross = 1000; s.CurrentDiggers = new SupportDigger[0];
        s.Diggers = new[] { new SupportDigger { Id = 10, Name = "Blood", Maximum = 100, BaseDrain = 10, Growth = 2, ConsumesGold = true, PerLevel = .1 } };
        s.DiggerSlots = 1; r.PlannedSupport = true; r.DiggerPercent = 10;
        var supported = Evaluate(s, r);
        Check(supported.Support.Drain <= 100 && supported.Support.Diggers[0].Level == 4, "planned digger levels obey verified level-minus-one drain");
        r.BeardPriority = new[] { 6, 1, 3 }; s.UnlockedBeards = new[] { 1, 3 }; s.BeardSlots = 1;
        Check(Evaluate(s, r).Support.Beards.SequenceEqual(new[] { 1 }), "beard request filtered by unlocks and active capacity");
        s.GrossGold = s.GoldActiveGross = s.GoldInactiveGross = 0; Check(Evaluate(s, r).Support.Diggers.Length == 0, "unaffordable digger omitted");
        s = Snapshot(); r = Request();
        var targets = new[] { new PresetNgu { Level = 0, Target = 3, HardCap = 1000000000, Weight = 1, Coefficient = 1000 } };
        Near(LoadoutPresetPlanner.NguRate(targets, 1000, 1000, 10), .3, "NGU target cap");
        Check(LoadoutPresetPlanner.NguRate(targets, 1000, 1000, 10) <= 50, "NGU tick cap");
        var shares = LoadoutPresetPlanner.Share(new[] { 1d, 100 }, 10, new[] { 1d, 1 });
        Near(shares[0], 1, "capped share"); Near(shares[1], 9, "redistribution");
        r.MinimumPower = 2000; Check(!Evaluate(s, r).Feasible && !Evaluate(s, r).Score.HasValue, "power constraint");
        r.MinimumPower = 0; r.Weights = new[] { 0d, 0, 0, 1, 0, 0 };
        Check(!Evaluate(s, r).Score.HasValue, "positive unavailable goal does not disappear");
        r.Weights = new[] { 0d, 0, 0, 0, 1, 1 }; s.Available[4] = true; s.Available[5] = true;
        s.Zones = new[] { new PresetZone { Id = 0, IdlePower = 900, IdleToughness = 900, OneHitPower = 1000,
            NormalRatio = 1, BoostValues = new[] { 10d }, Chances = new[] { .5 }, Caps = new[] { .6 } } };
        var farm = Evaluate(s, r); Near(farm.Metrics[4].Value, 1, "boost table value and timing");
        s.Drop = 100; Near(Evaluate(s, r).Metrics[4].Value, 1.2, "drop cap");
        s.Zones[0].Id = 20; s.Drop = 8; Near(Evaluate(s, r).Metrics[5].Value, .4, "rooted loot not linear");
        s.BaseToughness = 100; Check(!Evaluate(s, r).Feasible, "farming safety threshold");
        s.BaseToughness = 1000; s.Available[3] = true; s.PpPerKill = Enumerable.Repeat(200d, 100).ToArray();
        r.Weights = new[] { 0d, 0, 0, 100, 0, 1 };
        var tower = Evaluate(s, r);
        Check(tower.Zone == 1000 && tower.Metrics[3] > 0 && tower.Metrics[5] == 0, "ITOPOD and regular-zone farming are mutually exclusive");
        s.Current[0].Stats[(int)PresetStat.MoveCooldown] = 10000;
        Near(Evaluate(s, r).Metrics[3].Value, tower.Metrics[3].Value, "move cooldown is not respawn");
        s.Items[1].Stats[(int)PresetStat.Respawn] = 80;
        var respawn = LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "respawn", "Respawn");
        Near(respawn.Metrics[3].Value, 200 / 1.8, "actual respawn gear changes kill timing");
        s.Items[1].Stats[(int)PresetStat.Respawn] = 1000;
        Near(LoadoutPresetPlanner.Evaluate(s, r, new[] { s.Items[1] }, LoadoutPresetPlanner.NormalizeWeights(r.Weights), Enumerable.Repeat(1d, 6).ToArray(), "respawn", "Respawn").Metrics[3].Value,
            respawn.Metrics[3].Value, "respawn gear reduction capped at 80 percent");
        r.Weights = new[] { 0d, 0, 0, 1, 0, 10000 };
        var regular = Evaluate(s, r);
        Check(regular.Zone == 20 && regular.Metrics[3] == 0 && regular.Metrics[5] > 0, "zone focus cannot also earn ITOPOD PP");
        s = Snapshot(); r = Request(); s.ReservedEnergy = 900;
        Check(Evaluate(s, r).Energy <= 100, "basic training reserved against projected cap");
        s = Snapshot(); r = Request();
        s.Slots = new[] { -5, 10000 }; s.Factors = new[] { 1d, 1 };
        var acc = new PresetItem { Id = 50, Group = 10000, SourceSlot = 10000, Name = "Current accessory" };
        var replacement = new PresetItem { Id = 51, Group = 10000, SourceSlot = 10, Name = "Power accessory" };
        replacement.Stats[0] = 600; s.Items[1].Stats[0] = 600;
        s.Current = new[] { s.Current[0], acc }; s.Items = s.Items.Concat(new[] { acc, replacement }).ToArray(); r.MinimumPower = 2000;
        var constrained = LoadoutPresetPlanner.Plan(s, r).Candidates[1];
        Check(constrained.Feasible && constrained.Items.Any(x => x.Id == 41) && constrained.Items.Any(x => x.Id == 51), "search can cross an infeasible multi-slot constraint valley");
        s = Snapshot(); r = Request(); r.Swap = 1000;
        var expensive = LoadoutPresetPlanner.Plan(s, r);
        Check(expensive.Candidates[1].Items[0].Id == 40, "swap cost reverses gear recommendation");
        s = Snapshot(); r = Request(); p = LoadoutPresetPlanner.Plan(s, r);
        Check(p.Sequence != null && p.Sequence.Score <= p.Candidates.Concat(p.Specialists).Max(x => x.Score), "linear frozen time-sharing cannot invent synergy");
        Near(p.Sequence.FirstSeconds + p.Sequence.SecondSeconds, r.Horizon, "time sharing wall-clock budget");
        r.Swap = 5; p = LoadoutPresetPlanner.Plan(s, r);
        var first = p.Candidates.Concat(p.Specialists).Single(x => x.Key == p.Sequence.First);
        var initialCost = first.SetupChanged ? 5 : 0;
        Near(p.Sequence.FirstSeconds + p.Sequence.SecondSeconds + p.Sequence.SwapSeconds + initialCost, r.Horizon, "initial and internal swap costs charged exactly once");
        var two = new PresetSnapshot { Slots = new[] { -5, -6 }, Factors = new[] { 1d, .5 } };
        Check(!LoadoutPresetPlanner.Legal(two, new[] { s.Items[0], s.Items[0] }), "distinct IDs/instances");
        Check(LoadoutPresetPlanner.Legal(two, new[] { s.Items[0], s.Items[1] }), "offhand slot legality");
        Near(LoadoutPresetPlanner.Totals(two, new[] { s.Items[0], s.Items[1] })[(int)PresetStat.EnergyPower], 200, "offhand factor");
        Check(!LoadoutPresetPlanner.Legal(new PresetSnapshot { Slots = new[] { -1 } }, new[] { s.Items[0] }), "wrong slot rejected");
        var rejected = false; try { LoadoutPresetPlanner.NormalizeWeights(new double[6]); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "all zero rejected");
        s = Snapshot(); r = Request(); r.Horizon = 3600;
        s.Slots = new[] { -5 }.Concat(Enumerable.Range(10000, 20)).ToArray(); s.Factors = Enumerable.Repeat(1d, 21).ToArray();
        s.Current = new[] { s.Current[0] }.Concat(Enumerable.Range(0, 20).Select(i => new PresetItem { Id = 1000 + i, SourceSlot = 10000 + i, Group = 10000 })).ToArray();
        s.Items = s.Items.Concat(s.Current.Skip(1)).Concat(Enumerable.Range(0, 180).Select(i => {
            var item = new PresetItem { Id = 2000 + i, SourceSlot = 100 + i, Group = 10000 };
            Stat(item, PresetStat.EnergyPower, i % 30 * 10); Stat(item, PresetStat.MagicPower, (180 - i) % 31 * 10); return item;
        })).ToArray();
        s.EnergyNgu = Enumerable.Range(0, 9).Select(i => new PresetNgu { Coefficient = .001, Level = 10000, HardCap = 1000000000, Weight = 1 }).ToArray();
        s.MagicNgu = Enumerable.Range(0, 7).Select(i => new PresetNgu { Coefficient = .001, Level = 10000, HardCap = 1000000000, Weight = 1 }).ToArray();
        var timer = System.Diagnostics.Stopwatch.StartNew(); var bounded = LoadoutPresetPlanner.Plan(s, r); timer.Stop();
        Check(timer.ElapsedMilliseconds < 5000 && bounded.Evaluated <= MaximumPlanEvaluations, "bounded realistic inventory and two-phase search");
        Check(bounded.Candidates.Concat(bounded.Specialists).All(c => LoadoutPresetPlanner.Legal(s, c.Items)), "all searched loadouts retain distinct identity and legal slots");
        Console.WriteLine("Inventory search: " + bounded.Evaluated + " evaluations in " + timer.ElapsedMilliseconds + " ms");
        Console.WriteLine("Preset checks passed: " + assertions);
    }
}
