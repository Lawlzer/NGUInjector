using System;
using System.Linq;
using System.Reflection;

namespace NGUInjector.Managers
{
    internal static class MoneyPitManager
    {

        internal sealed class RewardPreview
        {
            internal int Tier, Roll;
            internal bool AllEquipment;
            internal int[] RandomSlots = new int[0];
            internal bool LevelsEquipment => AllEquipment || RandomSlots.Length > 0;
            public override string ToString() => $"tier {Tier}, roll {Roll}" + (AllEquipment
                ? " (all equipped items level up)" : RandomSlots.Length > 0
                    ? $" (level slots {string.Join(",", RandomSlots)})" : " (no equipped-item levels)");
        }

        internal static RewardPreview PreviewReward()
        {
            var preview = new RewardPreview();
            var exponent = Math.Log10(Main.Character.realGold);
            if (double.IsNaN(exponent) || exponent < 5) return preview;
            var bounds = new[] { 7, 9, 11, 13, 15, 18, 21, 24, 27, 30, 50, 55, 60, 65, 70 };
            preview.Tier = 1 + bounds.Count(bound => exponent >= bound);
            if (Main.Character.wishes.wishes[4].level < 1) preview.Tier = Math.Min(11, preview.Tier);
            var upper = preview.Tier >= 12 ? 6 : preview.Tier >= 7 ? 13 : preview.Tier == 6 ? 12
                : preview.Tier == 5 ? 11 : preview.Tier >= 2 ? 10 : 9;

            // Native tiers reset Unity RNG from pitState. Never advance the saved pit or caller's RNG.
            var previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.state = Main.Character.pit.pitState;
                preview.Roll = UnityEngine.Random.Range(1, upper);
                preview.AllEquipment = preview.Tier >= 12 ? preview.Roll == 2 : preview.Tier >= 5 && preview.Roll == 4;
                if (preview.Tier >= 2 && preview.Tier <= 4 && preview.Roll == 4)
                {
                    preview.RandomSlots = new int[preview.Tier == 4 ? 2 : 1];
                    for (var i = 0; i < preview.RandomSlots.Length; i++)
                    {
                        var slot = UnityEngine.Random.Range(Math.Min(-5, -5 - Main.Controller.accessorySpaces()), 0);
                        preview.RandomSlots[i] = slot >= -5 ? slot : slot >= -10 ? 10000 + (-slot - 6) : -1;
                        // randomLevelUp also draws for cosmetic text, including between the two hits.
                        UnityEngine.Random.Range(0, 100);
                    }
                }
                return preview;
            }
            finally { UnityEngine.Random.state = previous; }
        }

        internal static string CheckMoneyPit()
        {
            return Main.reference?.RequestMoneyPit() ?? "Money Pit waiting for execution authority.";
        }

        internal static string DoDailySpin()
        {
            return Main.reference?.RequestDailySpin() ?? "Daily Spin waiting for execution authority.";
        }
    }
}
