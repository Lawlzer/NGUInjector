using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NGUInjector.Managers;
using GameMain = NGUInjector.Main;

internal static class Program
{
    private static int Assertions;
    private sealed class RenderFailure : Exception { }
    private static void Check(bool value, string reason)
    {
        Assertions++;
        if (!value) throw new Exception(reason);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { Assertions++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static Character Reset()
    {
        Ui.OnRender = null;
        return GameMain.Character = new Character { idleEnergy = long.MaxValue, magic = new Magic { idleMagic = long.MaxValue }, res3 = new Res3 { idleRes3 = long.MaxValue } };
    }
    private static int Lane(ResourceTarget target)
    {
        return new[] { ResourceTarget.MagicNgu, ResourceTarget.TimeMachineMagic, ResourceTarget.WandoosMagic, ResourceTarget.BloodRitual, ResourceTarget.WishMagic }.Contains(target)
            ? 1 : target == ResourceTarget.WishRes3 || target == ResourceTarget.Hack ? 2 : 0;
    }
    private static long Idle(Character c, int lane) => lane == 0 ? c.idleEnergy : lane == 1 ? c.magic.idleMagic : c.res3.idleRes3;
    private static void SetIdle(Character c, int lane, long amount)
    {
        if (lane == 0) c.idleEnergy = amount;
        else if (lane == 1) c.magic.idleMagic = amount;
        else c.res3.idleRes3 = amount;
    }
    private static void Seed(Character c, ResourceTarget target, int i, long amount)
    {
        switch (target)
        {
            case ResourceTarget.EnergyNgu: c.NGU.skills[i].energy = amount; break;
            case ResourceTarget.MagicNgu: c.NGU.magicSkills[i].magic = amount; break;
            case ResourceTarget.AdvancedTraining: c.advancedTraining.energy[i] = amount; break;
            case ResourceTarget.Augment: c.augments.augs[i].augEnergy = amount; break;
            case ResourceTarget.AugmentUpgrade: c.augments.augs[i].upgradeEnergy = amount; break;
            case ResourceTarget.TimeMachineEnergy: c.machine.speedEnergy = amount; break;
            case ResourceTarget.TimeMachineMagic: c.machine.goldMultiMagic = amount; break;
            case ResourceTarget.WandoosEnergy: c.wandoos98.wandoosEnergy = amount; break;
            case ResourceTarget.WandoosMagic: c.wandoos98.wandoosMagic = amount; break;
            case ResourceTarget.BloodRitual: c.bloodMagic.ritual[i].magic = amount; break;
            case ResourceTarget.WishEnergy: c.wishes.wishes[i].energy = amount; break;
            case ResourceTarget.WishMagic: c.wishes.wishes[i].magic = amount; break;
            case ResourceTarget.WishRes3: c.wishes.wishes[i].res3 = amount; break;
            case ResourceTarget.Hack: c.hacks.hacks[i].res3 = amount; break;
            case ResourceTarget.BasicAttack: c.training.attackEnergy[i] = amount; break;
            case ResourceTarget.BasicDefense: c.training.defenseEnergy[i] = amount; break;
            default: throw new Exception("Missing test target");
        }
    }
    private static long Read(Character c, ResourceTarget target, int i)
    {
        switch (target)
        {
            case ResourceTarget.EnergyNgu: return c.NGU.skills[i].energy;
            case ResourceTarget.MagicNgu: return c.NGU.magicSkills[i].magic;
            case ResourceTarget.AdvancedTraining: return c.advancedTraining.energy[i];
            case ResourceTarget.Augment: return c.augments.augs[i].augEnergy;
            case ResourceTarget.AugmentUpgrade: return c.augments.augs[i].upgradeEnergy;
            case ResourceTarget.TimeMachineEnergy: return c.machine.speedEnergy;
            case ResourceTarget.TimeMachineMagic: return c.machine.goldMultiMagic;
            case ResourceTarget.WandoosEnergy: return c.wandoos98.wandoosEnergy;
            case ResourceTarget.WandoosMagic: return c.wandoos98.wandoosMagic;
            case ResourceTarget.BloodRitual: return c.bloodMagic.ritual[i].magic;
            case ResourceTarget.WishEnergy: return c.wishes.wishes[i].energy;
            case ResourceTarget.WishMagic: return c.wishes.wishes[i].magic;
            case ResourceTarget.WishRes3: return c.wishes.wishes[i].res3;
            case ResourceTarget.Hack: return c.hacks.hacks[i].res3;
            case ResourceTarget.BasicAttack: return c.training.attackEnergy[i];
            case ResourceTarget.BasicDefense: return c.training.defenseEnergy[i];
            default: throw new Exception("Missing test target");
        }
    }
    private static int Index(ResourceTarget target) => target == ResourceTarget.TimeMachineEnergy || target == ResourceTarget.TimeMachineMagic
        || target == ResourceTarget.WandoosEnergy || target == ResourceTarget.WandoosMagic ? 0 : 1;

    private static void CheckTarget(ResourceTarget target)
    {
        var c = Reset(); var lane = Lane(target); var i = Index(target);
        const long amount = 9007199254740993L;
        var renders = 0;
        Ui.OnRender = () => {
            renders++;
            Check(Read(c, target, i) == amount && Idle(c, lane) == long.MaxValue - amount, "Accounting must precede render: " + target);
        };
        ResourceWriter.Transfer(target, i, amount);
        Check(renders == 1, "Native rendering: " + target);
        Check(Enumerable.Range(0, 3).Where(l => l != lane).All(l => Idle(c, l) == long.MaxValue), "Unowned lanes changed: " + target);
        Ui.OnRender = () => { Check(Read(c, target, i) == 0 && Idle(c, lane) == long.MaxValue, "Recall accounting before render"); };
        Check(ResourceWriter.RecallTarget(target, i) == amount, "Exact long recall");
        Ui.OnRender = () => { throw new RenderFailure(); };
        ResourceWriter.Transfer(target, i, 4, false);
        Check(Read(c, target, i) == 4 && Idle(c, lane) == long.MaxValue - 4, "Suppressed rendering still transfers");
        Throws<RenderFailure>(() => ResourceWriter.Transfer(target, i, 3));
        Check(Read(c, target, i) == 7 && Idle(c, lane) == long.MaxValue - 7, "Throwing rendering must conserve resources");
        Check(!ResourceWriter.RecallOwnedTarget(target, i, 6), "Foreign amount must be retained");
        Check(Read(c, target, i) == 7 && Idle(c, lane) == long.MaxValue - 7, "Mismatch must be inert");
        Throws<RenderFailure>(() => ResourceWriter.RecallOwnedTarget(target, i, 7));
        Check(Read(c, target, i) == 0 && Idle(c, lane) == long.MaxValue, "Throwing recall rendering must conserve resources");
        Ui.OnRender = null;
        Check(ResourceWriter.RecallOwnedTarget(target, i, 0), "Zero ownership match");
        Throws<ArgumentOutOfRangeException>(() => ResourceWriter.Transfer(target, i, -1));
        Throws<ArgumentOutOfRangeException>(() => ResourceWriter.RecallOwnedTarget(target, i, -1));
        Seed(c, target, i, long.MaxValue);
        Throws<OverflowException>(() => ResourceWriter.Transfer(target, i, 1));
        Check(Read(c, target, i) == long.MaxValue && Idle(c, lane) == long.MaxValue, "Destination overflow must be inert");
        Throws<OverflowException>(() => ResourceWriter.RecallTarget(target, i));
        Check(Read(c, target, i) == long.MaxValue && Idle(c, lane) == long.MaxValue, "Recall overflow must be inert");
        Seed(c, target, i, -1);
        Throws<InvalidOperationException>(() => ResourceWriter.Transfer(target, i, 1));
        Check(Read(c, target, i) == -1 && Idle(c, lane) == long.MaxValue, "Negative destination must be inert");
        Seed(c, target, i, 0);
        if (lane == 0) c.idleEnergy = 2; else if (lane == 1) c.magic.idleMagic = 2; else c.res3.idleRes3 = 2;
        Throws<InvalidOperationException>(() => ResourceWriter.Transfer(target, i, 3));
        Check(Read(c, target, i) == 0 && Idle(c, lane) == 2, "Insufficient idle must be inert");
        Check(ResourceWriter.RecallTarget(target, i) == 0, "Empty recall");
        ResourceWriter.Transfer(target, i, 0);
        Check(Idle(c, lane) == 2 && Read(c, target, i) == 0, "Zero transfer");
        ResourceWriter.Transfer(target, i, 2, false);
        Check(ResourceWriter.RecallOwnedTarget(target, i, 2), "Exact ownership match");
        Check(Read(c, target, i) == 0 && Idle(c, lane) == 2, "Successful ownership return");
        if (lane == 0) c.idleEnergy = -1; else if (lane == 1) c.magic.idleMagic = -1; else c.res3.idleRes3 = -1;
        Throws<InvalidOperationException>(() => ResourceWriter.Transfer(target, i, 0));
        Throws<InvalidOperationException>(() => ResourceWriter.RecallTarget(target, i));
        Check(Read(c, target, i) == 0 && Idle(c, lane) == -1, "Negative idle must be inert");
        if (lane == 0) c.idleEnergy = 1; else if (lane == 1) c.magic.idleMagic = 1; else c.res3.idleRes3 = 1;
        Seed(c, target, i, long.MaxValue - 1);
        ResourceWriter.Transfer(target, i, 1, false);
        Check(Read(c, target, i) == long.MaxValue && Idle(c, lane) == 0, "Exact maximum destination");
        Check(ResourceWriter.RecallTarget(target, i) == long.MaxValue && Idle(c, lane) == long.MaxValue, "Exact maximum recall");
    }

    private static void CheckBatches()
    {
        var c = Reset(); c.idleEnergy = 20000;
        var active = 0;
        Parallel.For(0, 1000, _ => ResourceWriter.Execute(() => {
            if (Interlocked.Increment(ref active) != 1) throw new Exception("Overlapping batches");
            ResourceWriter.Transfer(ResourceTarget.EnergyNgu, 0, 3, false);
            ResourceWriter.Execute(() => ResourceWriter.Transfer(ResourceTarget.EnergyNgu, 1, 7, false));
            Interlocked.Decrement(ref active);
        }));
        Check(c.idleEnergy == 10000 && c.NGU.skills[0].energy == 3000 && c.NGU.skills[1].energy == 7000, "Serialized reentrant batches");
        Check(ResourceWriter.Execute(() => 42) == 42, "Generic Execute result");
        Throws<RenderFailure>(() => ResourceWriter.Execute((Action)(() => { throw new RenderFailure(); })));
        Check(ResourceWriter.Execute(() => 43) == 43, "Exceptions release lock");
        Throws<ArgumentNullException>(() => ResourceWriter.Execute((Action)null));
        Throws<ArgumentNullException>(() => ResourceWriter.Execute<int>(null));
    }

    private static void CheckNativeOperations()
    {
        var c = Reset(); c.idleEnergy = c.magic.idleMagic = c.res3.idleRes3 = 0;
        c.training.attackEnergy[0] = 7; c.training.defenseEnergy[0] = 11;
        c.NGU.skills[0].energy = 13; c.NGU.magicSkills[0].magic = 17; c.hacks.hacks[0].res3 = 19;
        ResourceWriter.Recall(true);
        Check(c.idleEnergy == 13 && c.training.attackEnergy[0] == 7 && c.training.defenseEnergy[0] == 11 && c.MagicRecalls == 0 && c.Res3Recalls == 0, "Energy-only native removeMost retains basic and other lanes");
        ResourceWriter.Recall(false);
        Check(c.magic.idleMagic == 17 && c.hacks.hacks[0].res3 == 19, "Magic-only native removeMost");
        ResourceWriter.RecallRes3();
        Check(c.res3.idleRes3 == 19 && c.Res3Recalls == 1, "Native R3 recall");
        ResourceWriter.RecallAllEnergy();
        Check(c.idleEnergy == 31 && c.training.attackEnergy[0] == 0 && c.training.defenseEnergy[0] == 0 && c.AllEnergyRecalls == 1, "Dormant basic recall");
        ResourceWriter.RecallForGearSwap();
        Check(c.EnergyRecalls == 2 && c.MagicRecalls == 2 && c.Res3Recalls == 2, "Gear boundary delegates each lane");
        Ui.Activated.Clear();
        ResourceWriter.ActivateFruit(10);
        Check(c.yggdrasilController.Page == 1 && Ui.Activated.SequenceEqual(new[] { 10 }), "Fruit global index and page");
        Check(c.EnergyRecalls == 2 && c.MagicRecalls == 2, "Fruit never implicitly recalls");
        Throws<ArgumentOutOfRangeException>(() => ResourceWriter.ActivateFruit(-1));
        Throws<ArgumentOutOfRangeException>(() => ResourceWriter.ActivateFruit(20));
        Ui.Caps = 0;
        ResourceWriter.CapBloodRituals();
        Check(Ui.Caps == c.bloodMagic.ritual.Count && c.MagicRecalls == 2, "Native caps without implicit recall");
        Ui.OnRender = () => { throw new RenderFailure(); };
        Throws<RenderFailure>(() => ResourceWriter.Recall(true));
        Throws<RenderFailure>(() => ResourceWriter.CapBloodRituals());
        Throws<RenderFailure>(() => ResourceWriter.ActivateFruit(0));
    }

    private static (ResourceTarget Target, int Index)[] Slots(Character c)
    {
        return Enum.GetValues(typeof(ResourceTarget)).Cast<ResourceTarget>().SelectMany(target => {
            int count;
            switch (target)
            {
                case ResourceTarget.EnergyNgu: count = c.NGU.skills.Count; break;
                case ResourceTarget.MagicNgu: count = c.NGU.magicSkills.Count; break;
                case ResourceTarget.AdvancedTraining: count = c.advancedTraining.energy.Length; break;
                case ResourceTarget.Augment: case ResourceTarget.AugmentUpgrade: count = c.augments.augs.Length; break;
                case ResourceTarget.BloodRitual: count = c.bloodMagic.ritual.Count; break;
                case ResourceTarget.WishEnergy: case ResourceTarget.WishMagic: case ResourceTarget.WishRes3: count = c.wishes.wishes.Count; break;
                case ResourceTarget.Hack: count = c.hacks.hacks.Count; break;
                case ResourceTarget.BasicAttack: count = c.training.attackEnergy.Length; break;
                case ResourceTarget.BasicDefense: count = c.training.defenseEnergy.Length; break;
                default: count = 1; break;
            }
            return Enumerable.Range(0, count).Select(i => (target, i));
        }).ToArray();
    }

    private static Character RecallFixture()
    {
        var c = Reset();
        c.idleEnergy = 101; c.magic.idleMagic = 103; c.res3.idleRes3 = 107;
        // These stale/clamped totals/capacities are not the resource amounts being transferred.
        c.curEnergy = c.capEnergy = c.magic.curMagic = c.magic.capMagic = c.res3.curRes3 = c.res3.capRes3 = 1;
        var n = 0L;
        foreach (var slot in Slots(c)) Seed(c, slot.Target, slot.Index, ++n);
        return c;
    }

    private static long[] Observe(Character c)
    {
        return new[] { c.idleEnergy, c.magic.idleMagic, c.res3.idleRes3 }
            .Concat(Slots(c).Select(slot => Read(c, slot.Target, slot.Index))).ToArray();
    }

    private static bool Recalled(ResourceTarget target, int lane, bool basic)
    {
        return Lane(target) == lane && (basic || target != ResourceTarget.BasicAttack && target != ResourceTarget.BasicDefense);
    }

    private static void Recall(int lane, bool basic)
    {
        if (lane == 2) ResourceWriter.RecallRes3();
        else if (basic) ResourceWriter.RecallAllEnergy();
        else ResourceWriter.Recall(lane == 0);
    }

    private static void CheckNativeRecallPostconditions(int lane, bool basic)
    {
        var c = RecallFixture();
        var before = Observe(c);
        c.RecallOverride = (_, __) => true;
        Throws<InvalidOperationException>(() => Recall(lane, basic));
        Check(before.SequenceEqual(Observe(c)), "Rejected native recall must not be repaired");

        c = RecallFixture();
        var slots = Slots(c);
        var amounts = slots.Select(slot => Read(c, slot.Target, slot.Index)).ToArray();
        var idle = Enumerable.Range(0, 3).Select(l => Idle(c, l)).ToArray();
        Recall(lane, basic);
        for (var i = 0; i < slots.Length; i++)
            Check(Read(c, slots[i].Target, slots[i].Index) == (Recalled(slots[i].Target, lane, basic) ? 0 : amounts[i]),
                "Exact native recall destination map: " + slots[i]);
        for (var l = 0; l < 3; l++)
            Check(Idle(c, l) == idle[l] + (l == lane ? slots.Select((slot, i) => Recalled(slot.Target, lane, basic) ? amounts[i] : 0).Sum() : 0),
                "Exact native recall idle return / unowned pool preservation");
        Check(c.curEnergy == 1 && c.magic.curMagic == 1 && c.res3.curRes3 == 1, "Do not synthesize totals from a recall");

        // Each target's last slot can be silently omitted while total conservation still holds.
        foreach (var target in Enum.GetValues(typeof(ResourceTarget)).Cast<ResourceTarget>().Where(t => Recalled(t, lane, basic)))
        {
            c = RecallFixture();
            var slot = Slots(c).Last(s => s.Target == target);
            var old = Read(c, target, slot.Index);
            c.AfterRecall = (_, __) => {
                Seed(c, target, slot.Index, old);
                SetIdle(c, lane, Idle(c, lane) - old);
            };
            var expected = Observe(c);
            var all = Slots(c);
            for (var i = 0; i < all.Length; i++)
            {
                if (!Recalled(all[i].Target, lane, basic) || all[i] == slot) continue;
                expected[lane] += expected[i + 3]; expected[i + 3] = 0;
            }
            Throws<InvalidOperationException>(() => Recall(lane, basic));
            Check(expected.SequenceEqual(Observe(c)), "Partial native recall remains observed, not concealed: " + target);
        }

        // Unowned composition changes must fail even when they preserve that lane's total.
        foreach (var target in Enum.GetValues(typeof(ResourceTarget)).Cast<ResourceTarget>().Where(t => !Recalled(t, lane, basic)))
        {
            c = RecallFixture();
            var slot = Slots(c).Last(s => s.Target == target);
            var old = Read(c, target, slot.Index);
            var otherLane = Lane(target);
            c.AfterRecall = (_, __) => {
                Seed(c, target, slot.Index, old + 1);
                SetIdle(c, otherLane, Idle(c, otherLane) - 1);
            };
            Throws<InvalidOperationException>(() => Recall(lane, basic));
            Check(Read(c, target, slot.Index) == old + 1, "Unowned native interference must not be repaired: " + target);
        }

        foreach (var delta in new[] { -1L, 1L })
        {
            c = RecallFixture();
            c.AfterRecall = (_, __) => SetIdle(c, lane, Idle(c, lane) + delta);
            Throws<InvalidOperationException>(() => Recall(lane, basic));
            Check(Slots(c).Where(s => Recalled(s.Target, lane, basic)).All(s => Read(c, s.Target, s.Index) == 0),
                "Lost/minted idle is rejected even with fully cleared destinations");
        }
        foreach (var otherLane in Enumerable.Range(0, 3).Where(l => l != lane))
        {
            c = RecallFixture();
            c.AfterRecall = (_, __) => SetIdle(c, otherLane, Idle(c, otherLane) - 1);
            Throws<InvalidOperationException>(() => Recall(lane, basic));
        }

        c = RecallFixture();
        c.AfterRecall = (_, __) => c.wishes.wishes.Add(new Row());
        Throws<InvalidOperationException>(() => Recall(lane, basic));
        Check(c.wishes.wishes.Count == 21, "Changed native collection shape must be rejected without repair");

        c = RecallFixture();
        c.AfterRecall = (_, __) => GameMain.Character = new Character();
        Throws<InvalidOperationException>(() => Recall(lane, basic));
        Check(!ReferenceEquals(c, GameMain.Character), "Changed native character context must not be overwritten");

        c = RecallFixture();
        SetIdle(c, lane, long.MaxValue);
        before = Observe(c);
        Throws<OverflowException>(() => Recall(lane, basic));
        Check(c.EnergyRecalls + c.MagicRecalls + c.Res3Recalls + c.AllEnergyRecalls == 0 && before.SequenceEqual(Observe(c)),
            "Exact recall idle overflow fails before native mutation");

        c = RecallFixture();
        c.wishes.wishes[19].res3 = -1;
        before = Observe(c);
        Throws<InvalidOperationException>(() => Recall(lane, basic));
        Check(c.EnergyRecalls + c.MagicRecalls + c.Res3Recalls + c.AllEnergyRecalls == 0 && before.SequenceEqual(Observe(c)),
            "Negative recalled/unowned allocations fail before native mutation");

        c = RecallFixture();
        c.AfterRecall = (_, __) => { throw new RenderFailure(); };
        Throws<RenderFailure>(() => Recall(lane, basic));
        Check(Slots(c).Where(s => Recalled(s.Target, lane, basic)).All(s => Read(c, s.Target, s.Index) == 0),
            "Throwing native rendering remains an error after the actual recall");
    }

    private static void CheckNativeRecallBoundaries()
    {
        CheckNativeRecallPostconditions(0, false);
        CheckNativeRecallPostconditions(1, false);
        CheckNativeRecallPostconditions(2, false);
        CheckNativeRecallPostconditions(0, true);

        foreach (var lane in Enumerable.Range(0, 3))
        {
            var c = Reset();
            c.idleEnergy = c.magic.idleMagic = c.res3.idleRes3 = 7;
            var target = lane == 0 ? ResourceTarget.WishEnergy : lane == 1 ? ResourceTarget.WishMagic : ResourceTarget.WishRes3;
            Seed(c, target, 19, 9007199254740993L);
            Recall(lane, false);
            Check(Idle(c, lane) == 9007199254741000L && Read(c, target, 19) == 0, "Recall every wish entry with exact longs");
        }

        foreach (var rejectedLane in Enumerable.Range(0, 3))
        {
            var c = RecallFixture();
            c.RecallOverride = (lane, _) => lane == rejectedLane;
            Throws<InvalidOperationException>(() => ResourceWriter.RecallForGearSwap());
            Check(c.EnergyRecalls == 1 && c.MagicRecalls == (rejectedLane >= 1 ? 1 : 0)
                && c.Res3Recalls == (rejectedLane >= 2 ? 1 : 0), "Gear recall stops at first rejected lane");
            Check(Slots(c).Where(s => Lane(s.Target) == rejectedLane).All(s => Read(c, s.Target, s.Index) > 0),
                "Gear recall must not hide the rejected lane");
        }

        var missing = Reset(); missing.idleEnergy = missing.magic.idleMagic = 0;
        missing.res3 = null; missing.hacks = null;
        ResourceWriter.Recall(true); ResourceWriter.Recall(false);
        Check(missing.res3 == null && missing.hacks == null, "Absent unowned R3 models stay absent");
        Throws<InvalidOperationException>(() => ResourceWriter.RecallRes3());
        Check(missing.Res3Recalls == 0, "Missing owned native pool fails before recall");
    }

    private static void CheckDesiredMaps()
    {
        foreach (ResourceTarget target in Enum.GetValues(typeof(ResourceTarget)))
        {
            var c = Reset(); var lane = Lane(target); var index = Index(target);
            SetIdle(c, lane, 100);
            var desired = new System.Collections.Generic.Dictionary<ResourceTarget, long[]>();
            var values = new long[Slots(c).Count(x => x.Target == target)]; values[index] = 60; desired.Add(target, values);
            var renders = 0;
            Action render = () => { renders++; Check(Read(c, target, index) == 60 && Idle(c, lane) == 40, "Whole accounting commits before render"); throw new RenderFailure(); };
            var result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2, render);
            Check(result.Disposition == MutationDisposition.ModelAppliedRenderFailed && result.Verified && result.ModelWrites == 1, "Known render failure is verified, not unknown: " + target);
            result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2, () => renders++, true);
            Check(result.Verified && result.ModelWrites == 0 && renders == 2, "Presentation retry never replays committed transfer: " + target);
            result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2, () => renders++);
            Check(result.Verified && result.ModelWrites == 0 && renders == 2, "Unchanged target performs no model or presentation writes: " + target);
            values[index] = 20;
            result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2);
            Check(result.Verified && Read(c, target, index) == 20 && Idle(c, lane) == 80, "Direct successor diff returns only excess: " + target);
            values[index] = 101; var before = Observe(c);
            result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2);
            Check(result.Disposition == MutationDisposition.RejectedUnchanged && before.SequenceEqual(Observe(c)), "Unaffordable batch rejects before mutation");
            values[index] = 30;
            result = ResourceWriter.Reconcile(desired, false, false, false);
            Check(result.Disposition == MutationDisposition.RejectedUnchanged && before.SequenceEqual(Observe(c)), "Full lane permission before mutation");
            result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2, authorized: () => false);
            Check(result.Disposition == MutationDisposition.RejectedUnchanged && before.SequenceEqual(Observe(c)), "Generation/entry guard before mutation");
            result = ResourceWriter.Reconcile(desired, lane == 0, lane == 1, lane == 2, () => {
                Seed(c, target, index, 31); SetIdle(c, lane, 69); throw new RenderFailure();
            });
            Check(result.Disposition == MutationDisposition.ForeignConflict && result.AffectedLanes == 1 << lane
                && Read(c, target, index) == 31 && Idle(c, lane) == 69, "Foreign callback is scoped and preserved, never accepted as rendering only");
            result = ResourceWriter.Reconcile(new System.Collections.Generic.Dictionary<ResourceTarget, long[]>(), true, true, true);
            Check(result.Verified && result.ModelWrites == 0 && Read(c, target, index) == 31, "Unclaimed target remains uncontrolled");
        }
        var shared = Reset(); shared.idleEnergy = 0; shared.magic.idleMagic = 100;
        shared.NGU.skills[0].energy = 100; shared.training.attackEnergy[0] = 7;
        var map = ResourceWriter.EmptyDesiredMap(true, true);
        map[ResourceTarget.EnergyNgu][1] = 100; map[ResourceTarget.BloodRitual][0] = 100;
        var applied = ResourceWriter.Reconcile(map, true, true, false);
        Check(applied.Verified && shared.NGU.skills[0].energy == 0 && shared.NGU.skills[1].energy == 100
            && shared.bloodMagic.ritual[0].magic == 100 && shared.training.attackEnergy[0] == 7, "Independent lanes preempt directly while basic training is preserved");
        var armed = true; map[ResourceTarget.EnergyNgu][1] = 99;
        applied = ResourceWriter.Reconcile(map, true, true, false, () => armed = false, authorized: () => armed);
        Check(applied.Disposition == MutationDisposition.UnknownOutcome && !applied.Verified, "Epoch/permission change during callback cannot admit dependent work");
        map[ResourceTarget.EnergyNgu][1] = 98;
        applied = ResourceWriter.Reconcile(map, true, true, false, () => shared.NGU.skills[1] = new Row { energy = 98 });
        Check(applied.Disposition == MutationDisposition.UnknownOutcome && !applied.Verified,
            "Identical amounts in replaced native row objects do not silently acquire a new save/model identity");
        var detached = Reset(); detached.idleEnergy = 100;
        var input = new System.Collections.Generic.Dictionary<ResourceTarget, long[]> { { ResourceTarget.EnergyNgu, new long[detached.NGU.skills.Count] } };
        input[ResourceTarget.EnergyNgu][0] = 10;
        applied = ResourceWriter.Reconcile(input, true, false, false, authorized: () => { input[ResourceTarget.EnergyNgu][0] = 90; return true; });
        Check(applied.Verified && detached.NGU.skills[0].energy == 10 && detached.idleEnergy == 90,
            "Caller-owned desired arrays cannot change an already-preflighted transition through an admission callback");
    }

    private static void CheckSharedApplicationEvidence()
    {
        var c = Reset(); c.idleEnergy = 100; c.magic.idleMagic = 100;
        var map = ResourceWriter.EmptyDesiredMap(true, true); map[ResourceTarget.EnergyNgu][0] = 60; map[ResourceTarget.MagicNgu][0] = 70;
        var application = new ResourceApplication(); var renders = 0;
        Ui.OnRender = () => { renders++; Check(c.NGU.skills[0].energy == 60 && c.NGU.magicSkills[0].magic == 70, "Every shared renderer sees both complete lanes"); throw new RenderFailure(); };
        var result = application.Apply(map, true, true);
        Check(result.Verified && application.PresentationPending && result.ModelWrites == 2, "Shared evidence keeps committed model distinct from native presentation");
        var count = renders;
        result = application.Apply(map, true, true, authorized: () => false);
        Check(result.Disposition == MutationDisposition.RejectedUnchanged && application.PresentationPending && renders == count,
            "Rejected authority never renders and cannot erase a pending presentation obligation");
        Ui.OnRender = () => renders++;
        result = application.Apply(map, true, true);
        Check(result.Verified && result.ModelWrites == 0 && !application.PresentationPending && renders > count, "Readmitted presentation retries only rendering, not committed accounting");
        count = renders; result = application.Apply(map, true, true);
        Check(result.Verified && result.ModelWrites == 0 && renders == count, "Shared unchanged target performs zero model and presentation writes");
        Check(ResourceWriter.Reclaimable(true) == 100 && ResourceWriter.Reclaimable(false) == 100, "Reclaimable lane observations include current allocations, not just idle");
        map[ResourceTarget.EnergyNgu][0] = 50;
        Ui.OnRender = () => { c.NGU.magicSkills[0].magic--; c.magic.idleMagic++; };
        result = application.Apply(map, true, true);
        Check(result.Disposition == MutationDisposition.ForeignConflict && result.AffectedLanes == 2, "Shared foreign evidence identifies the affected Magic lane");
        var before = Observe(c); Ui.OnRender = null; map[ResourceTarget.EnergyNgu][0] = 40;
        result = application.Apply(map, true, true);
        Check(result.Disposition == MutationDisposition.ForeignConflict && result.AffectedLanes == 2
            && result.VerifiedLanes == 1 && result.ModelWrites == 1 && c.NGU.skills[0].energy == 40
            && c.NGU.magicSkills[0].magic == 69 && c.magic.idleMagic == 31,
            "Magic uncertainty yields only Magic; healthy Energy follows the current desired map");
        before = Observe(c);
        result = new ResourceApplication().Apply(map, true, true);
        Check(result.Disposition == MutationDisposition.ForeignConflict && result.AffectedLanes == 2
            && result.ModelWrites == 0 && before.SequenceEqual(Observe(c)),
            "A replacement application cannot erase native-model lane uncertainty");
        map[ResourceTarget.EnergyNgu][0] = 30;
        result = application.Apply(map.Where(x => Lane(x.Key) == 0).ToDictionary(x => x.Key, x => x.Value), true, false);
        Check(result.Verified && result.VerifiedLanes == 1 && c.NGU.skills[0].energy == 30
            && application.BlockedLanes == 2, "An Energy-only successor verifies without acknowledging unresolved Magic");
        c = Reset(); c.idleEnergy = c.magic.idleMagic = 100;
        map = ResourceWriter.EmptyDesiredMap(true, true); map[ResourceTarget.EnergyNgu][0] = 10;
        var epoch = new ResourceApplication();
        result = epoch.Apply(map, true, true, render: () => GameMain.Character = new Character());
        Check(result.Disposition == MutationDisposition.UnknownOutcome, "Shared epoch change cannot be treated as verified accounting");
        c = Reset(); c.idleEnergy = c.magic.idleMagic = 100; c.training.attackEnergy[0] = -1;
        map = ResourceWriter.EmptyDesiredMap(true, false); map[ResourceTarget.EnergyNgu][0] = 10;
        result = new ResourceApplication().Apply(map, true, false);
        Check(result.Disposition == MutationDisposition.RejectedUnchanged && c.idleEnergy == 100 && c.NGU.skills[0].energy == 0,
            "Invalid retained basic training blocks dependent Energy accounting before mutation");
        Throws<InvalidOperationException>(() => ResourceWriter.Reclaimable(true));
        map = ResourceWriter.EmptyDesiredMap(false, true); map[ResourceTarget.MagicNgu][0] = 10;
        result = new ResourceApplication().Apply(map, false, true);
        Check(result.Verified && c.magic.idleMagic == 90 && c.training.attackEnergy[0] == -1,
            "Uncontrolled invalid Energy does not authorize repairs or block independent healthy Magic");
    }

    private static void CheckConflictEpochs()
    {
        foreach (var unknown in new[] { false, true })
        foreach (var transition in new[] { "rebirth", "save", "rollback" })
        {
            var c = Reset(); c.idleEnergy = c.magic.idleMagic = 100;
            var application = new ResourceApplication(); var authority = true;
            var desired = ResourceWriter.EmptyDesiredMap(true, true);
            desired[ResourceTarget.EnergyNgu][0] = 60; desired[ResourceTarget.MagicNgu][0] = 70;
            var result = application.Apply(desired, true, true, authorized: () => authority, render: () => {
                if (unknown) authority = false;
                else { c.NGU.magicSkills[0].magic--; c.magic.idleMagic++; }
            });
            Check(result.Disposition == (unknown ? MutationDisposition.UnknownOutcome : MutationDisposition.ForeignConflict),
                "Epoch regression starts with an actual " + result.Disposition);
            var blocked = unknown ? 3 : 2;
            Check(application.BlockedLanes == blocked, "Conflict is quarantined before " + transition);
            c.rebirthTime.totalseconds++; c.totalPlaytime.totalseconds++;
            Check(new ResourceApplication().BlockedLanes == blocked, "Clock advance and replacement owner preserve same-epoch quarantine");
            c.NGU.magicSkills[0] = new Row { magic = c.NGU.magicSkills[0].magic };
            Check(application.BlockedLanes == blocked, "A row replacement alone cannot acknowledge lane uncertainty");
            var stats = c.stats; c.stats = null;
            Check(application.BlockedLanes == blocked, "Unavailable epoch metadata cannot clear quarantine");
            c.stats = stats;
            c.rebirthTime.totalseconds = double.NaN;
            Check(application.BlockedLanes == blocked, "Invalid epoch clocks cannot clear quarantine");
            c.rebirthTime.totalseconds = 1001;
            Check(application.BlockedLanes == blocked, "Restoring the same valid epoch retains quarantine");

            if (transition == "rebirth") { c.stats.rebirthNumber++; c.rebirthTime.totalseconds = 0; }
            else if (transition == "save")
            {
                c.stats = new Stats { rebirthNumber = c.stats.rebirthNumber };
                c.rebirthTime = new Clock { totalseconds = 1001 }; c.totalPlaytime = new Clock { totalseconds = 2001 };
            }
            else { c.rebirthTime.totalseconds = 500; c.totalPlaytime.totalseconds = 1500; }
            c.NGU = new Ngu(); c.idleEnergy = c.magic.idleMagic = 100;
            Check(application.BlockedLanes == 0 && new ResourceApplication().BlockedLanes == 0,
                "Verified " + transition + " epoch retires only historical quarantine on the same Character");
            result = application.Apply(desired, true, true, authorized: () => true);
            Check(result.Verified && result.VerifiedLanes == 3 && c.NGU.skills[0].energy == 60
                && c.NGU.magicSkills[0].magic == 70 && c.idleEnergy == 40 && c.magic.idleMagic == 30,
                "Fresh " + transition + " epoch accepts exact current funding, not historical allocations");
        }

        var changed = Reset(); changed.idleEnergy = 100;
        var uncertain = new ResourceApplication(); var allowed = true;
        var map = ResourceWriter.EmptyDesiredMap(true, false); map[ResourceTarget.EnergyNgu][0] = 10;
        var outcome = uncertain.Apply(map, true, false, authorized: () => allowed, render: () => {
            changed.stats = new Stats(); allowed = false;
        });
        Check(outcome.Disposition == MutationDisposition.UnknownOutcome && uncertain.BlockedLanes == 1,
            "Epoch replacement during an uncertain callback quarantines the observed new epoch");
        var before = Observe(changed);
        outcome = new ResourceApplication().Apply(map, true, false);
        Check(outcome.Disposition == MutationDisposition.UnknownOutcome && before.SequenceEqual(Observe(changed)),
            "The next owner cannot clear uncertainty recorded after the epoch transition");

        changed = Reset(); changed.idleEnergy = 100; allowed = true;
        uncertain = new ResourceApplication(); map = ResourceWriter.EmptyDesiredMap(true, false);
        map[ResourceTarget.EnergyNgu][0] = 10;
        outcome = uncertain.Apply(map, true, false, authorized: () => allowed, render: () => {
            changed.stats = null; allowed = false;
        });
        Check(outcome.Disposition == MutationDisposition.UnknownOutcome && uncertain.BlockedLanes == 1,
            "Uncertainty recorded with unavailable epoch metadata remains quarantined");
        changed.stats = new Stats { rebirthNumber = 1 };
        Check(uncertain.BlockedLanes == 1, "First valid metadata cannot classify an unanchored uncertain write as historical");
        changed.stats.rebirthNumber++;
        Check(uncertain.BlockedLanes == 0, "A subsequent verified rebirth retires the now-anchored historical uncertainty");
    }

    private static void CheckResourceSetupCutoverTimeline()
    {
        var c = Reset(); c.idleEnergy = 100; c.magic.idleMagic = 100;
        c.training.attackEnergy[0] = 7; c.res3.idleRes3 = 23; c.hacks.hacks[0].res3 = 11;
        var executor = new ResourceSetupExecutor(); var renders = 0; var authority = true;
        Func<string, long, long, ResourceSetupIntent> intent = (owner, energy, magic) => {
            var desired = ResourceWriter.EmptyDesiredMap(true, true);
            desired[ResourceTarget.EnergyNgu][0] = energy;
            desired[ResourceTarget.MagicNgu][0] = magic;
            return new ResourceSetupIntent { Owner = owner, Desired = desired, Energy = true, Magic = true,
                Authorized = () => authority, Render = () => renders++ };
        };
        var baseline = intent("preset:baseline", 70, 80);
        var result = executor.Reconcile(baseline);
        Check(result.Verified && result.ModelWrites == 2, "Baseline requirements use the concrete shared resource actuator");
        var temporary = intent("focus:temporary", 90, 0);
        temporary.Desired[ResourceTarget.BloodRitual][0] = 100;
        temporary.Render = () => { renders++; throw new RenderFailure(); };
        result = executor.Reconcile(temporary);
        Check(result.Verified && result.Disposition == MutationDisposition.ModelAppliedRenderFailed && result.ModelWrites == 3
            && c.NGU.skills[0].energy == 90 && c.NGU.magicSkills[0].magic == 0 && c.bloodMagic.ritual[0].magic == 100,
            "Temporary requirements commit before rendering failure, without retaining historical allocations");
        result = executor.Reconcile(temporary);
        Check(result.Verified && result.ModelWrites == 0, "Presentation failure never repeats committed temporary funding");
        // Remove the temporary claim and supply today's changed successor directly.
        var successor = intent("preset:changed-successor", 40, 55);
        result = executor.Reconcile(successor);
        Check(result.Verified && result.ModelWrites == 3 && c.NGU.skills[0].energy == 40
            && c.NGU.magicSkills[0].magic == 55 && c.bloodMagic.ritual[0].magic == 0,
            "Temporary removal goes directly to current successor, never the old 70/80 baseline");
        var count = renders; result = executor.Reconcile(successor);
        Check(result.Verified && result.ModelWrites == 0 && renders == count,
            "Unchanged successor performs no redundant model or presentation work");
        authority = false; successor.Desired[ResourceTarget.EnergyNgu][0] = 99;
        result = executor.Reconcile(successor);
        Check(!result.Verified && result.ModelWrites == 0 && c.NGU.skills[0].energy == 40
            && executor.Current.Desired[ResourceTarget.EnergyNgu][0] == 99,
            "Current rejected intent survives without admitting an action or an unauthorized model write");
        successor.Desired[ResourceTarget.EnergyNgu][0] = 1;
        Check(executor.Current.Desired[ResourceTarget.EnergyNgu][0] == 99,
            "Retained current intent is detached from producer buffers");
        Check(c.training.attackEnergy[0] == 7 && c.res3.idleRes3 == 23 && c.hacks.hacks[0].res3 == 11,
            "Every shared resource transition preserves basic training and unmanaged R3");
    }

    public static int Main()
    {
        try
        {
            foreach (ResourceTarget target in Enum.GetValues(typeof(ResourceTarget))) CheckTarget(target);
            var c = Reset();
            Throws<ArgumentOutOfRangeException>(() => ResourceWriter.Transfer((ResourceTarget)999, 0, 1));
            Throws<ArgumentOutOfRangeException>(() => ResourceWriter.Transfer(ResourceTarget.TimeMachineEnergy, 1, 1));
            Throws<ArgumentOutOfRangeException>(() => ResourceWriter.Transfer(ResourceTarget.EnergyNgu, -1, 1));
            Throws<ArgumentOutOfRangeException>(() => ResourceWriter.Transfer(ResourceTarget.EnergyNgu, 100, 1));
            Check(c.idleEnergy == long.MaxValue && c.machine.speedEnergy == 0, "Invalid targets must be inert");
            CheckBatches(); CheckNativeOperations(); CheckNativeRecallBoundaries(); CheckDesiredMaps(); CheckSharedApplicationEvidence(); CheckConflictEpochs(); CheckResourceSetupCutoverTimeline();
            Console.WriteLine("PASS: " + Assertions + " assertions against production ResourceWriter with data-only game boundaries.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
