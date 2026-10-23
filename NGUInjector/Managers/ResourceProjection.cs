using System;
using System.Linq;

namespace NGUInjector.Managers
{
    internal enum ResourceSpec { EnergyPower, EnergyPower2, EnergyPower3, MagicPower, MagicPower2, MagicPower3,
        AllPower, EnergyCap, EnergyCap3, MagicCap, MagicCap3, AllCap, NGU, NGU2 }

    // Forward Character getter arithmetic. Float prefixes and post-cap potions are intentional.
    internal sealed class ResourceProjection
    {
        private static readonly float[] RawDivisors = { 100f, 1000, 100000, 100, 1000, 100000, 100000,
            10000, 1000000, 10000, 1000000, 1000000, 10000, 1000000 };

        internal bool Energy, Capacity;
        internal long BaseCap, CapLimit = long.MaxValue;
        internal double PowerLimit = float.MaxValue;
        internal float BasePower, Itopod = 1, Macguffin = 1, Quirk = 1, Wish = 1,
            Potion1 = 1, Potion2 = 1;

        internal static float[] NormalizeRaw(float[] raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.Length != RawDivisors.Length) throw new ArgumentException("Exactly 14 resource specs are required.", nameof(raw));
            var bonuses = (float[])raw.Clone();
            for (var i = 0; i < bonuses.Length; i++) bonuses[i] /= RawDivisors[i];
            return bonuses;
        }

        internal static float[] EquipmentBonuses(int[] slots, Func<int, int, float> bonus,
            Func<int, float> factor, bool nativePrefix)
        {
            var raw = new float[RawDivisors.Length];
            var weapon = Array.IndexOf(slots, -5);
            var accessories = nativePrefix ? Enumerable.Range(0, slots.Length).Where(i => slots[i] >= 10000)
                .OrderBy(i => slots[i]).ToArray() : null;
            for (var stat = 0; stat < raw.Length; stat++)
            {
                float value;
                if (nativePrefix)
                {
                    Func<int, float> at = slot => {
                        var i = Array.IndexOf(slots, slot);
                        return i < 0 ? 0f : bonus(i, stat);
                    };
                    // Keep the native F-stack prefix, including the un-stored offhand product.
                    // Capture queries its factor even when the offhand index is absent (-1).
                    value = at(-5) + at(-1) + at(-2) + at(-3) + at(-4) + at(-6) * factor(Array.IndexOf(slots, -6));
                    foreach (var i in accessories) value += bonus(i, stat);
                }
                else
                {
                    // Planner snapshots deliberately store each product and addition as binary32.
                    value = weapon < 0 ? 0f : bonus(weapon, stat);
                    for (var i = 0; i < slots.Length; i++)
                        if (i != weapon) value = (float)(value + (float)(bonus(i, stat) * factor(i)));
                }
                raw[stat] = value;
            }
            return NormalizeRaw(raw);
        }

        internal double Project(float[] bonuses)
        {
            var first = (int)(Capacity ? Energy ? ResourceSpec.EnergyCap : ResourceSpec.MagicCap
                : Energy ? ResourceSpec.EnergyPower : ResourceSpec.MagicPower);
            // Do not force extra float casts/locals: native IL retains evaluation-stack precision
            // until its double conversion. Extra conv.r4 instructions change Framework results.
            double value;
            if (Capacity)
            {
                value = Energy ? (double)((float)BaseCap * Itopod * Macguffin)
                        * (1d + bonuses[first] + bonuses[first + 1] + bonuses[(int)ResourceSpec.AllCap])
                    : (float)BaseCap * Itopod * Macguffin
                        * (1f + bonuses[first] + bonuses[first + 1] + bonuses[(int)ResourceSpec.AllCap]);
            }
            else
            {
                value = Energy ? BasePower * Itopod
                        * (1f + bonuses[first] + bonuses[first + 1] + bonuses[first + 2] + bonuses[(int)ResourceSpec.AllPower])
                    : BasePower * Itopod * Macguffin
                        * (1f + bonuses[first] + bonuses[first + 1] + bonuses[first + 2] + bonuses[(int)ResourceSpec.AllPower]);
                if (Energy) value *= Macguffin;
            }
            value *= Quirk; value *= Wish;
            if (!Capacity) return (float)(Math.Min(PowerLimit, Math.Max(1, value)) * Potion1 * Potion2);
            // The double representation of Int64.MaxValue must not be converted to Int64.
            if (value >= CapLimit) return CapLimit;
            return Math.Min(CapLimit, Math.Max(1, Convert.ToInt64(Math.Max(1, value))));
        }
    }
}
