using System;
using System.Collections;
using System.Linq;

public static partial class Program
{
    private static void CheckNativeCombatProgression()
    {
        Setup();
        var w = Get(Character, "wandoos98"); var wc = Get(Character, "wandoos98Controller");
        var items = Get(Get(Character, "inventory"), "itemList");
        foreach (var fullSet in new[] { false, true })
        foreach (var xlSet in new[] { false, true })
        {
            Set(items, "wandoosComplete", fullSet); Set(items, "xlComplete", xlSet);
            var duration = (float) Invoke(wc, "wandoosBootupTime");
            Equal(xlSet ? 3240f : 3600f, duration, "native bootup duration uses owned completed XL set");
            foreach (var elapsed in new[] { 0d, .02, duration / 2d, duration - .25, (double) duration, duration + 50d })
            {
                Set(Get(w, "bootupTime"), "totalseconds", elapsed);
                var expected = Math.Min(1, (float)elapsed / duration);
                if (expected >= 1 && fullSet) expected *= 1.1f;
                Equal(expected, Invoke(wc, "bootupSpeedFactor"), "native ramp and completion-only 1.1 factor");
                var before = Resources(); var recalls = Recalls; var swaps = Swaps;
                var capture = CallMain("CaptureCombatPrep", .03, .03, null); var s = Get(capture, "Snapshot");
                Near(duration, (double)Get(s, "WandoosBootupDurationSeconds"), "capture uses native bootup getter");
                Near(elapsed, (double)Get(s, "WandoosBootupElapsedSeconds"), "capture uses native bootup clock");
                var p = Get(s, "Current");
                Near((float)Invoke(Character, "totalWandoosEnergySpeed"), (double)Get(p, "WandoosEnergySpeedWithoutAt") * expected, "native energy speed parity including fresh zero");
                Near((float)Invoke(Character, "totalWandoosMagicSpeed"), (double)Get(p, "WandoosMagicSpeedWithoutAt") * expected, "native magic speed parity including fresh zero");
                Equal(true, (double)Get(p, "WandoosEnergySpeedWithoutAt") > 0, "fresh zero boot never erases future base speed");
                Equal(before, Resources(), "native ramp capture read only"); Equal(recalls, Recalls, "native ramp capture never recalls"); Equal(swaps, Swaps, "native ramp capture never equips");
                // Actual native tick: progress getters run before bootup advances.
                Set(w, "wandoosEnergy", 10000L); Set(w, "wandoosMagic", 20000L);
                Invoke(wc, "advanceBootup");
                Near(Math.Min(3600, elapsed >= 3600 ? 3600 : elapsed + (double).02f), (double)Get(Get(w, "bootupTime"), "totalseconds"), "native bootup advances without requiring a dump");
                Set(w, "wandoosEnergy", 0L); Set(w, "wandoosMagic", 0L);
            }
        }

        Setup();
        w = Get(Character, "wandoos98"); wc = Get(Character, "wandoos98Controller");
        var at = Get(Character, "advancedTraining");
        var allAt = Get(Character, "advancedTrainingController");
        var levels = (long[])Get(at, "level"); var targets = (long[])Get(at, "levelTarget");
        var progress = (float[])Get(at, "barProgress"); var allocations = (long[])Get(at, "energy");
        var wish = ((IList)Get(Get(Character, "wishes"), "wishes"))[190]; Set(wish, "level", 1L);
        foreach (var target in new[] { -1L, 1L, 0L })
        {
            levels[3] = 10; levels[4] = 20; targets[3] = targets[4] = target;
            progress[3] = .75f; progress[4] = .25f;
            for (var tick = 0; tick < 50; tick++)
            {
                Invoke(Get(allAt, "wandoosEnergy"), "updateAdvancedTraining");
                Invoke(Get(allAt, "wandoosMagic"), "updateAdvancedTraining");
            }
            Equal(60L, levels[3], "native Wish190 adds 50 levels with zero allocation even with target " + target);
            Equal(70L, levels[4], "native Wish190 magic AT adds 50 levels independently");
            Equal(0f, progress[3], "native free AT discards partial on first completion");
            Equal(0L, allocations[3], "native free AT consumes no energy");
        }
        var captureFree = CallMain("CaptureCombatPrep", .03, .03, null);
        var projected = Project(captureFree, Phase("Wandoos", "current", 1));
        Equal(levels[3] + 50, Get(projected, "AtEnergyLevel"), "pure free AT matches actual native ticks during W");
        Equal(levels[4] + 50, Get(projected, "AtMagicLevel"), "pure free magic AT matches actual native ticks during W");
        var beforeFree = Resources(); var calls = Recalls; var swapsFree = Swaps;
        Preview(10);
        var plan = Get(Get(MainInstance, "_combatPrepPreview"), "Plan");
        Equal(true, ((Array)Get(plan, "Alternatives")).Cast<object>().SelectMany(x => ((Array)Get(x, "Phases")).Cast<object>()).All(x => (string)Get(x, "Kind") != "AT"), "Wish190 preview excludes paid AT rather than imposing false gates");
        Equal(beforeFree, Resources(), "Wish190 preview remains read only"); Equal(calls, Recalls, "Wish190 preview never recalls"); Equal(swapsFree, Swaps, "Wish190 preview never swaps");
        var runFree = StartServerPolicy(captureFree, Phase("Wandoos", "current", 1));
        Equal("running", Get(runFree, "Status"), "validated Wish190 policy starts through actual production executor");
        Equal(0L, allocations[3], "free W policy never buys energy AT");
        Equal(0L, allocations[4], "free W policy never buys magic AT");
        Set(wish, "level", 0L);
        Equal(false, CallMain("CombatPrepEpochMatches", Get(runFree, "Capture")), "Wish190 activation changes invalidate cached progression semantics");
        Set(wish, "level", 1L);
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(runFree, "Id") + "\"}"));
        // Native paid-allocation target path differs: it stops when energy is nonzero.
        targets[3] = 1; allocations[3] = 1; var prior = levels[3];
        Set(Get(allAt, "wandoosEnergy"), "alladvancedTraining", allAt);
        Invoke(Get(allAt, "wandoosEnergy"), "updateAdvancedTraining");
        Equal(prior, levels[3], "native Wish190 honors reached target only with allocated energy");
        allocations[3] = 0;
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[4] = 24999;
        Invoke(Get(allAt, "wandoosEnergy"), "updateAdvancedTraining");
        Equal(prior, levels[3], "native free AT requires actual basic-training unlock");
        var locked = Get(CallMain("CaptureCombatPrep", .03, .03, null), "Snapshot");
        Equal(false, Get(Get(locked, "AtEnergy"), "Unlocked"), "capture uses actual native basic-training gate for free AT");
        CheckNativeWandoosTicks();
        CheckFutureOwnedWandoosGear();
        Setup();
    }

    private static void CheckNativeWandoosTicks()
    {
        foreach (var free in new[] { false, true })
        foreach (var initialBoot in new[] { 0d, 3599.25 })
        {
            Setup();
            var w = Get(Character, "wandoos98"); var wc = Get(Character, "wandoos98Controller");
            var at = Get(Character, "advancedTrainingController");
            Set(Get(Get(Character, "inventory"), "itemList"), "wandoosComplete", true);
            Set(Get(w, "bootupTime"), "totalseconds", initialBoot);
            Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", free ? 1L : 0L);
            var pool = 1000000000L;
            Set(Character, "capEnergy", pool); Set(Character, "curEnergy", pool); Set(Character, "idleEnergy", pool);
            var magic = Get(Character, "magic"); Set(magic, "capMagic", pool); Set(magic, "curMagic", pool); Set(magic, "idleMagic", pool);
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var s = Get(capture, "Snapshot"); var p = Get(s, "Current"); var phase = Phase("Wandoos", "current", 2);
            var projected = Project(capture, phase);
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
            double energyCost = 0, magicCost = 0;
            for (var tick = 0; tick < 100; tick++)
            {
                if (tick % 25 == 0)
                {
                    CallMain("RecallCombatPrepResources");
                    ApplyResolvedCombatResources(phase, s, (long)Get(p, "EnergyTotal"), (long)Get(p, "MagicTotal"), true);
                }
                energyCost += (long)Get(w, "wandoosEnergy") / 50d;
                magicCost += (long)Get(w, "wandoosMagic") / 50d;
                Invoke(wc, "advanceEnergyProgress"); Invoke(wc, "advanceMagicProgress");
                Invoke(wc, "advanceBootup");
                if (free)
                {
                    Invoke(Get(at, "wandoosEnergy"), "updateAdvancedTraining");
                    Invoke(Get(at, "wandoosMagic"), "updateAdvancedTraining");
                }
            }
            Equal(Get(w, "energyLevel"), Get(projected, "WandoosEnergyLevel"), "native W ticks match forecast energy levels (free=" + free + ", boot=" + initialBoot + ")");
            Equal(Get(w, "magicLevel"), Get(projected, "WandoosMagicLevel"), "native W ticks match forecast magic levels");
            // Formula capture is double, native accumulation is float. Exact level agreement
            // above plus tight partial/cost bounds detects ramp, tick-order and hold regressions.
            Equal(true, Math.Abs((float)Get(w, "energyProgress") - (double)Get(projected, "WandoosEnergyProgress")) < 2e-5, "native energy partial stays within float rounding");
            Equal(true, Math.Abs((float)Get(w, "magicProgress") - (double)Get(projected, "WandoosMagicProgress")) < 2e-5, "native magic partial stays within float rounding");
            Equal(true, Math.Abs(energyCost - (double)Get(projected, "EnergyResourceSeconds")) <= Math.Max(2, energyCost * 2e-6), "native held energy allocation cost matches numeric capture");
            Equal(true, Math.Abs(magicCost - (double)Get(projected, "MagicResourceSeconds")) <= Math.Max(2, magicCost * 2e-6), "native held magic allocation cost matches numeric capture");
        }
    }

    private static void CheckFutureOwnedWandoosGear()
    {
        Setup();
        var inventory = Get(Character, "inventory"); var originalHead = Get(inventory, "head");
        var owned = Blank(originalHead.GetType());
        Set(owned, "id", 80); SetEnum(owned, "type", "Head"); Set(owned, "bossRequired", 1); Set(owned, "removable", true);
        SetEnum(owned, "spec1Type", "Wandoos98"); Set(owned, "spec1Cur", 1000000f);
        ((IList)Get(inventory, "inventory"))[0] = owned;
        var w = Get(Character, "wandoos98"); Set(Get(w, "bootupTime"), "totalseconds", 0d);
        var recalls = Recalls; var swaps = Swaps;
        var capture = CallMain("CaptureCombatPrep", .03, .03, null); var s = Get(capture, "Snapshot");
        var future = ((Array)Get(s, "Profiles")).Cast<object>().Single(x => (string)Get(x, "Key") == "wandoos-current");
        Equal(true, (double)Get(future, "WandoosEnergySpeedWithoutAt") > (double)Get(Get(s, "Current"), "WandoosEnergySpeedWithoutAt"), "fresh-zero capture includes better already-owned future W gear");
        Equal(true, ReferenceEquals(originalHead, Get(inventory, "head")), "future gear capture never equips candidate");
        Equal(recalls, Recalls, "future gear capture does not recall"); Equal(swaps, Swaps, "future gear capture does not swap");
        // Fixture-only native getter comparison on that exact already-owned item.
        Set(inventory, "head", owned); ((IList)Get(inventory, "inventory"))[0] = originalHead; RefreshBonuses();
        Set(Get(w, "bootupTime"), "totalseconds", 3600d);
        Near((float)Invoke(Character, "totalWandoosEnergySpeed"), (double)Get(future, "WandoosEnergySpeedWithoutAt"), "captured future owned gear matches actual full-boot native energy getter");
        Near((float)Invoke(Character, "totalWandoosMagicSpeed"), (double)Get(future, "WandoosMagicSpeedWithoutAt"), "captured future owned gear matches actual full-boot native magic getter");
    }
}
