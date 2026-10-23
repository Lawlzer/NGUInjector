using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    // Only the live-game boundary is substituted; JSON, hashing, migration and disk IO are production code.
    internal sealed class Main
    {
        internal static Main reference;
        internal static SavedSettings Settings;
        internal static void Log(string text) { }
        internal static void UpdateForm(SavedSettings settings) { }
        internal void ForgetStageProgress(string name) { }
        internal void InvalidateCustomRuntimePlanCache() { }
        internal void ValidateScheduledPlanEdit(string name, CustomPlan plan) { }
    }
}

namespace NGUInjector.Managers
{
    internal static class ZoneHelpers
    {
        internal static readonly int[] TitanZones = {6, 8, 11, 14, 16, 19, 23, 26, 30, 34, 38, 42};
    }
    internal enum GearOptimizationGoal
    {
        EnergyCap, MagicCap, EnergyNgu, MagicNgu, AllNgu, TimeMachine, Blood, Gold, Drop,
        Fight, Augments, AdvancedTraining, Wandoos, ManualAdventure, AdventureGold
    }
    internal static class GearOptimizer
    {
        internal static readonly Dictionary<int, string> GoalLabels = Enum.GetValues(typeof(GearOptimizationGoal))
            .Cast<GearOptimizationGoal>().ToDictionary(value => (int) value, value => value.ToString());
        internal static readonly Dictionary<int, string> FightScoreModeLabels = new Dictionary<int, string> {{0, "default"}, {1, "product"}, {2, "sum"}};
    }
}
