using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector
{
    internal partial class Main
    {
        internal static Character Character;
        internal static readonly List<string> Logs = new List<string>();
        internal static void Log(string message) { Logs.Add(message); }
    }
}

// Selection is source-linked; native writers below are tripwires for the pure checks.
internal sealed class Character
{
    internal Buttons buttons = new Buttons();
    internal Beards beards = new Beards();
    internal Challenges challenges = new Challenges();
    internal Diggers diggers = new Diggers();
    internal AllBeardsController allBeards;
    internal AllGoldDiggerController allDiggers;
    internal double BaseGps = 10000;
    internal double GoldMultiplier = 10;
    internal double realGold = 123456;
    internal int menuID = 0;
    internal Clock rebirthTime = new Clock();
    internal Adventure adventure = new Adventure();
    internal Wandoos wandoos98 = new Wandoos();

    internal Character(int cap)
    {
        allBeards = new AllBeardsController(this, cap);
        allDiggers = new AllGoldDiggerController(this);
    }

    internal double grossGoldPerSecond() { return BaseGps * (beards.beards[6]?.active == true ? GoldMultiplier : 1); }
    internal double goldPerSecond() { return grossGoldPerSecond() - allDiggers.totalGPSDrain(); }
}

internal sealed class Clock { internal double totalseconds = 0; }
internal sealed class Adventure { internal int zone = 0; }
internal sealed class Wandoos { internal bool disabled = false; internal long wandoosEnergy = 0, wandoosMagic = 0; }
internal sealed class Buttons { internal Button beards = new Button(); internal Button diggers = new Button(); }
internal sealed class Button { internal bool interactable = true; }
internal sealed class Challenges { internal Challenge trollChallenge = new Challenge(); }
internal sealed class Challenge { internal int curCompletions = 7; }
internal sealed class Beard { internal bool active; }
internal sealed class Beards
{
    internal List<Beard> beards = Enumerable.Range(0, 7).Select(_ => new Beard()).ToList();
    internal List<int> activeBeards = new List<int>();
    internal bool disabled = false;
}

internal sealed class AllBeardsController
{
    private readonly Character character;
    internal int Cap;
    internal int Refreshes;
    internal int ClearCalls;
    internal int CapCalls;
    internal bool ThrowOnCap;
    internal int RefuseActivation = -1;
    internal int RefuseDeactivation = -1;
    internal readonly List<int> Activated = new List<int>();
    internal readonly List<int> Deactivated = new List<int>();

    internal AllBeardsController(Character character, int cap) { this.character = character; Cap = cap; }
    internal int capBeards()
    {
        CapCalls++;
        if (ThrowOnCap) throw new InvalidOperationException("cap getter reached");
        return Cap;
    }
    internal void refreshMenu() { Refreshes++; }
    internal void activateBeard(int id)
    {
        Activated.Add(id);
        // The real low-level API does NOT enforce Gold's unlock. The manager must do it.
        if (id == RefuseActivation || character.beards.activeBeards.Count >= Cap || character.beards.activeBeards.Contains(id))
            return;
        character.beards.activeBeards.Add(id);
        character.beards.beards[id].active = true;
    }
    internal void deactivateBeard(int id)
    {
        Deactivated.Add(id);
        if (id == RefuseDeactivation || !character.beards.activeBeards.Contains(id))
            return;
        if (id == 6)
            character.allDiggers.clearAllActiveDiggers();
        character.beards.activeBeards.Remove(id);
        character.beards.beards[id].active = false;
    }
    internal void clearActiveBeards()
    {
        ClearCalls++;
        throw new InvalidOperationException("clearActiveBeards is forbidden");
    }
}

internal sealed class Digger
{
    internal bool active;
    internal long curLevel = 10;
    internal long maxLevel = 100;
}
internal sealed class Diggers
{
    internal List<Digger> diggers = Enumerable.Range(0, 12).Select(_ => new Digger()).ToList();
    internal List<int> activeDiggers = new List<int>();
    // Deliberately unrelated to the active set. Restoration must not apply a saved loadout.
    internal List<int> loadoutDiggers = new List<int> { 1, 2 };
}

internal sealed class AllGoldDiggerController
{
    private readonly Character character;
    internal int Slots = 12;
    internal int Clears;
    internal int Refreshes;
    internal readonly List<int> Activated = new List<int>();
    internal readonly List<int> Recapped = new List<int>();
    internal readonly List<int> LevelsSet = new List<int>();
    internal List<double> baseGPSDrain = Enumerable.Repeat(1d, 12).ToList();
    internal List<double> gpsGrowthRate = Enumerable.Repeat(2d, 12).ToList();
    internal List<bool> consumesGPS = Enumerable.Repeat(true, 12).ToList();
    internal List<GoldDiggerUIController> pods;
    internal bool AllowUpgrades = false;
    internal bool RefuseUpgrades = false;
    internal int UpgradeCalls;
    internal long HardCap = 105;
    internal double UpgradePrice = 1;
    internal AllGoldDiggerController(Character character)
    {
        this.character = character;
        pods = new List<GoldDiggerUIController> { new GoldDiggerUIController(character) };
    }
    internal int maxDiggerSlots() { return Slots; }
    internal void refreshMenu() { Refreshes++; }
    internal double drain(int id) { return character.diggers.diggers[id].active ? drain(id, true) : 0; }
    internal double upgradeCost(int id) { return UpgradePrice; }
    internal long hardCapLevel(int id) { return HardCap; }
    internal double drain(int id, bool evenifnotactive)
    {
        var digger = character.diggers.diggers[id];
        return digger.curLevel <= 0 ? 0 : baseGPSDrain[id] * Math.Pow(gpsGrowthRate[id], digger.curLevel - 1);
    }
    internal double totalGPSDrain()
    {
        return Enumerable.Range(0, character.diggers.diggers.Count)
            .Where(id => consumesGPS[id] && character.diggers.diggers[id].active).Sum(id => drain(id, true));
    }
    internal void activateDigger(int id)
    {
        Activated.Add(id);
        var digger = character.diggers.diggers[id];
        // Matches the real toggle, slot, positive-level and gross-GPS checks.
        if (digger.active)
        {
            digger.active = false;
            character.diggers.activeDiggers.Remove(id);
            return;
        }
        if (character.diggers.activeDiggers.Count >= Slots || digger.curLevel <= 0)
            return;
        digger.active = true;
        if (totalGPSDrain() > character.grossGoldPerSecond())
            digger.active = false;
        else
            character.diggers.activeDiggers.Add(id);
    }
    internal void clearAllActiveDiggers()
    {
        Clears++;
        foreach (var digger in character.diggers.diggers)
            digger.active = false;
        character.diggers.activeDiggers.Clear();
        refreshMenu();
    }
    internal void setLevelMaxAffordable(int id)
    {
        Recapped.Add(id);
        var digger = character.diggers.diggers[id];
        var oldLevel = digger.curLevel;
        digger.curLevel = 0;
        var available = character.goldPerSecond();
        // Mirrors the real truncation WITHOUT +1, even though level 1 drains baseGPSDrain.
        digger.curLevel = Math.Max(0, Math.Min(digger.maxLevel,
            (long)(Math.Log(available / baseGPSDrain[id]) / Math.Log(gpsGrowthRate[id]))));
        if (digger.curLevel == 0 && digger.active)
        {
            digger.active = false;
            character.diggers.activeDiggers.Remove(id);
        }
        if (totalGPSDrain() > character.grossGoldPerSecond())
            digger.curLevel = oldLevel;
        else if (!digger.active && digger.curLevel > 0 && character.diggers.activeDiggers.Count < Slots)
            activateDigger(id);
        refreshMenu();
    }
    internal void upgradeMaxLevel(int id)
    {
        if (!AllowUpgrades) throw new InvalidOperationException("Digger upgrades are forbidden");
        UpgradeCalls++;
        if (RefuseUpgrades || character.diggers.diggers[id].maxLevel >= HardCap || UpgradePrice > character.realGold)
            return;
        character.realGold -= UpgradePrice;
        character.diggers.diggers[id].maxLevel++;
    }
}

internal sealed class InputField { internal string text = "unchanged input"; }

internal sealed class GoldDiggerUIController
{
    private readonly Character character;
    internal int id = 2;
    internal InputField diggerLevelInput = new InputField();
    internal GoldDiggerUIController(Character character) { this.character = character; }
    internal void setID(int newid)
    {
        id = newid;
        updateUI();
    }
    private void updateUI()
    {
        if (character.menuID == 44)
            diggerLevelInput.text = character.diggers.diggers[id].curLevel.ToString();
    }
    internal void setInputField()
    {
        character.allDiggers.LevelsSet.Add(id);
        var digger = character.diggers.diggers[id];
        var level = Math.Min(Math.Max(0, long.Parse(diggerLevelInput.text)), digger.maxLevel);
        var oldLevel = digger.curLevel;
        if (level == 0 && digger.active)
        {
            digger.active = false;
            character.diggers.activeDiggers.Remove(id);
        }
        digger.curLevel = level;
        if (digger.active && character.allDiggers.totalGPSDrain() > character.grossGoldPerSecond())
            digger.curLevel = oldLevel;
        updateUI();
    }
}
