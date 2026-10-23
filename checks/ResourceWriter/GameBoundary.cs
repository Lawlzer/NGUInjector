using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector
{
    internal static class Main { internal static Character Character; }
}

// Data-only external game boundary. The production writer is source-linked, not replaced.
internal sealed class Character
{
    internal long idleEnergy, curEnergy, capEnergy;
    internal Stats stats = new Stats();
    internal Clock rebirthTime = new Clock { totalseconds = 1000 }, totalPlaytime = new Clock { totalseconds = 2000 };
    internal Magic magic = new Magic();
    internal Res3 res3 = new Res3();
    internal Ngu NGU = new Ngu();
    internal At advancedTraining = new At();
    internal Augments augments = new Augments();
    internal Machine machine = new Machine();
    internal Wandoos wandoos98 = new Wandoos();
    internal Blood bloodMagic = new Blood();
    internal Wishes wishes = new Wishes();
    internal Hacks hacks = new Hacks();
    internal Training training = new Training();
    internal FruitData yggdrasil = new FruitData();
    internal Ui NGUController = new Ui(), advancedTrainingController = new Ui(), augmentsController = new Ui(),
        timeMachineController = new Ui(), wandoos98Controller = new Ui(), bloodMagicController = new Ui(),
        wishesController = new Ui(), hacksController = new Ui(), allOffenseController = new Ui(),
        allDefenseController = new Ui(), yggdrasilController = new Ui();
    internal int EnergyRecalls, MagicRecalls, Res3Recalls, AllEnergyRecalls;
    internal Func<int, bool, bool> RecallOverride;
    internal Action<int, bool> AfterRecall;

    internal void removeMostEnergy()
    {
        EnergyRecalls++;
        if (RecallOverride?.Invoke(0, false) == true) return;
        ReturnEnergy();
        AfterRecall?.Invoke(0, false);
        Ui.Render();
    }
    private void ReturnEnergy()
    {
        foreach (var row in NGU.skills.Concat(wishes.wishes)) { idleEnergy += row.energy; row.energy = 0; }
        for (var i = 0; i < advancedTraining.energy.Length; i++) { idleEnergy += advancedTraining.energy[i]; advancedTraining.energy[i] = 0; }
        foreach (var row in augments.augs) { idleEnergy += row.augEnergy + row.upgradeEnergy; row.augEnergy = row.upgradeEnergy = 0; }
        idleEnergy += machine.speedEnergy + wandoos98.wandoosEnergy;
        machine.speedEnergy = wandoos98.wandoosEnergy = 0;
    }
    internal void removeMostMagic()
    {
        MagicRecalls++;
        if (RecallOverride?.Invoke(1, false) == true) return;
        foreach (var row in NGU.magicSkills.Concat(wishes.wishes).Concat(bloodMagic.ritual)) { magic.idleMagic += row.magic; row.magic = 0; }
        magic.idleMagic += machine.goldMultiMagic + wandoos98.wandoosMagic;
        machine.goldMultiMagic = wandoos98.wandoosMagic = 0;
        AfterRecall?.Invoke(1, false);
        Ui.Render();
    }
    internal void removeAllRes3()
    {
        Res3Recalls++;
        if (RecallOverride?.Invoke(2, false) == true) return;
        foreach (var row in hacks.hacks.Concat(wishes.wishes)) { res3.idleRes3 += row.res3; row.res3 = 0; }
        AfterRecall?.Invoke(2, false);
        Ui.Render();
    }
    internal void removeAllEnergy()
    {
        AllEnergyRecalls++;
        if (RecallOverride?.Invoke(0, true) == true) return;
        foreach (var values in new[] { training.attackEnergy, training.defenseEnergy })
            for (var i = 0; i < values.Length; i++) { idleEnergy += values[i]; values[i] = 0; }
        ReturnEnergy();
        AfterRecall?.Invoke(0, true);
        Ui.Render();
    }
}

internal sealed class Row { internal long energy, magic, res3, augEnergy, upgradeEnergy; }
internal sealed class Stats { internal long rebirthNumber; }
internal sealed class Clock { internal double totalseconds; }
internal sealed class Magic { internal long idleMagic, curMagic, capMagic; }
internal sealed class Res3 { internal long idleRes3, curRes3, capRes3; }
internal sealed class Ngu { internal List<Row> skills = Rows(9), magicSkills = Rows(7); internal static List<Row> Rows(int n) => Enumerable.Range(0, n).Select(_ => new Row()).ToList(); }
internal sealed class At { internal long[] energy = new long[5]; }
internal sealed class Augments { internal Row[] augs = Ngu.Rows(7).ToArray(); }
internal sealed class Machine { internal long speedEnergy, goldMultiMagic; }
internal sealed class Wandoos { internal long wandoosEnergy, wandoosMagic; }
internal sealed class Blood { internal List<Row> ritual = Ngu.Rows(8); }
internal sealed class Wishes { internal List<Row> wishes = Ngu.Rows(20); }
internal sealed class Hacks { internal List<Row> hacks = Ngu.Rows(16); }
internal sealed class Training { internal long[] attackEnergy = new long[6], defenseEnergy = new long[6]; }
internal sealed class FruitData { internal List<Row> fruits = Ngu.Rows(20); }

internal sealed class Ui
{
    internal static Action OnRender;
    internal static readonly List<int> Activated = new List<int>();
    internal static int Caps;
    internal Ui[] NGU, NGUMagic, augments, bloodMagics, trains, fruits;
    internal Ui defense, attack, block, wandoosEnergy, wandoosMagic;
    internal int Page;
    internal Ui(bool leaf = false)
    {
        if (leaf) return;
        var rows = Enumerable.Range(0, 20).Select(_ => new Ui(true)).ToArray();
        NGU = NGUMagic = augments = bloodMagics = trains = fruits = rows;
        defense = attack = block = wandoosEnergy = wandoosMagic = rows[0];
    }
    internal static void Render() { OnRender?.Invoke(); }
    internal void updateText() { Render(); }
    internal void updateAugTexts() { Render(); }
    internal void updateUpgradeTexts() { Render(); }
    internal void updateSpeedText() { Render(); }
    internal void updateGoldMultiText() { Render(); }
    internal void updateBloodMagicText() { Render(); }
    internal void updateMenu() { Render(); }
    internal void refreshMenu() { Render(); }
    internal void changePage(int page) { Page = page; Render(); }
    internal void activate(int index) { Activated.Add(index); Render(); }
    internal void cap() { Caps++; Render(); }
}
