using System;
using System.Globalization;
using System.Linq;

namespace NGUInjector.Managers
{
    internal struct SupportDiggerLevel
    {
        internal readonly int Id;
        internal readonly long Level;
        internal SupportDiggerLevel(int id, long level) { Id = id; Level = level; }
    }

    internal sealed class SupportSelection
    {
        private readonly int[] beards;
        private readonly SupportDiggerLevel[] diggers;
        internal int[] Beards => (int[])beards.Clone();
        internal SupportDiggerLevel[] Diggers => (SupportDiggerLevel[])diggers.Clone();
        internal string StableKey { get; }
        internal SupportSelection(int[] beardIds, SupportDiggerLevel[] diggerLevels)
        {
            beards = (beardIds ?? new int[0]).OrderBy(x => x).ToArray();
            diggers = (diggerLevels ?? new SupportDiggerLevel[0]).OrderBy(x => x.Id).ThenBy(x => x.Level).ToArray();
            StableKey = string.Join(",", beards.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()) + "|"
                + string.Join(",", diggers.Select(x => x.Id.ToString(CultureInfo.InvariantCulture) + ":" + x.Level.ToString(CultureInfo.InvariantCulture)).ToArray());
        }
    }

    internal sealed class SupportDigger
    {
        internal int Id;
        internal string Name;
        internal long Maximum, Level;
        internal double BaseDrain, Growth, StartBonus, PerLevel;
        internal bool ConsumesGold;
        internal double Drain(long level) { return DiggerAllocation.Drain(BaseDrain, Growth, level, ConsumesGold); }
    }

    // Numeric capture only. All levels, including inactive diggers, belong in Diggers.
    internal class SupportContext
    {
        internal bool BeardsAvailable = true, DiggersAvailable = true, BeardsDisabled;
        internal int DiggerSlots, BeardSlots;
        internal int[] CurrentBeards = new int[0], UnlockedBeards = new int[0];
        internal SupportDigger[] Diggers = new SupportDigger[0], CurrentDiggers = new SupportDigger[0];
        // NGU, Gold, Adventure, Loot. W and Stat retain their native, different precisions below.
        internal float[] BeardTemporary = Enumerable.Repeat(1f, 4).ToArray(), BeardPermanent = Enumerable.Repeat(1f, 4).ToArray();
        internal float[] BeardEffective;
        internal float BeardWTemporary = 1, BeardWPermanent = 1, BeardWEffective = float.NaN;
        internal double BeardStatTemporary = 1, BeardStatPermanent = 1, BeardStatEffective = double.NaN;
        internal float DiggerLevelBonus = 1;
        internal double GrossGold, GoldActiveGross, GoldInactiveGross;
        internal double[] DiggerEffective;
        internal SupportSelection CurrentSelection => new SupportSelection(CurrentBeards,
            CurrentDiggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray());
        internal void CopySupportFrom(SupportContext source)
        {
            BeardsAvailable = source.BeardsAvailable; DiggersAvailable = source.DiggersAvailable; BeardsDisabled = source.BeardsDisabled;
            DiggerSlots = source.DiggerSlots; BeardSlots = source.BeardSlots; CurrentBeards = source.CurrentBeards; UnlockedBeards = source.UnlockedBeards;
            Diggers = source.Diggers; CurrentDiggers = source.CurrentDiggers;
            BeardTemporary = source.BeardTemporary; BeardPermanent = source.BeardPermanent; BeardEffective = source.BeardEffective;
            BeardWTemporary = source.BeardWTemporary; BeardWPermanent = source.BeardWPermanent; BeardWEffective = source.BeardWEffective;
            BeardStatTemporary = source.BeardStatTemporary; BeardStatPermanent = source.BeardStatPermanent; BeardStatEffective = source.BeardStatEffective;
            DiggerLevelBonus = source.DiggerLevelBonus; DiggerEffective = source.DiggerEffective;
            GrossGold = source.GrossGold; GoldActiveGross = source.GoldActiveGross; GoldInactiveGross = source.GoldInactiveGross;
        }
    }

    internal sealed class SupportState
    {
        internal SupportSelection Selection { get; }
        private readonly SupportDiggerLevel[] levels;
        private readonly int[] beardFlags, diggerFlags;
        internal SupportDiggerLevel[] Levels => (SupportDiggerLevel[])levels.Clone();
        internal int[] BeardFlags => (int[])beardFlags.Clone();
        internal int[] DiggerFlags => (int[])diggerFlags.Clone();
        internal string StableKey { get; }
        internal SupportState(SupportSelection selection, SupportDiggerLevel[] allLevels, int[] beardActiveFlags = null, int[] diggerActiveFlags = null)
        {
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            levels = (allLevels ?? throw new ArgumentNullException(nameof(allLevels))).OrderBy(x => x.Id).ToArray();
            beardFlags = (beardActiveFlags ?? selection.Beards).OrderBy(x => x).ToArray();
            diggerFlags = (diggerActiveFlags ?? selection.Diggers.Select(x => x.Id).ToArray()).OrderBy(x => x).ToArray();
            StableKey = selection.StableKey + "|" + string.Join(",", levels.Select(x => x.Id.ToString(CultureInfo.InvariantCulture)
                + ":" + x.Level.ToString(CultureInfo.InvariantCulture)).ToArray()) + "|"
                + string.Join(",", beardFlags.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()) + "|"
                + string.Join(",", diggerFlags.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
        }
    }

    internal sealed class SupportEvaluation
    {
        internal SupportSelection Selection;
        internal bool Feasible;
        internal string Reason;
        internal double GrossGold, Drain, NetGold, Budget;
        internal double BeardNGU, BeardW, BeardStat, BeardGold, BeardAdv, BeardLoot;
        internal double DiggerLoot, DiggerW, DiggerStat, DiggerAdv, DiggerEnergyNGU, DiggerMagicNGU,
            DiggerEnergyBeard, DiggerMagicBeard, DiggerPP, DiggerDaycare, DiggerBlood, DiggerEXP;
    }

    internal static class SupportProjection
    {
        internal static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        internal static double BeardBonus(SupportContext c, int[] beards, int id)
        {
            if (c.BeardsDisabled) return 1;
            var active = beards.Contains(id);
            var unchanged = active == c.CurrentBeards.Contains(id);
            if (id == 0) return unchanged && !double.IsNaN(c.BeardStatEffective) ? c.BeardStatEffective : Math.Max(1d, (active ? c.BeardStatTemporary : 1d) * c.BeardStatPermanent);
            if (id == 4) return unchanged && !float.IsNaN(c.BeardWEffective) ? c.BeardWEffective : Math.Max(1f, (float)((active ? c.BeardWTemporary : 1f) * c.BeardWPermanent));
            var index = Array.IndexOf(new[] { 3, 6, 5, 1 }, id);
            if (index < 0) return 1;
            if (unchanged && c.BeardEffective != null) return c.BeardEffective[index];
            return Math.Max(1f, (float)((active ? c.BeardTemporary[index] : 1f) * c.BeardPermanent[index]));
        }

        internal static double GrossGold(SupportContext c, int[] beards)
        { return beards.Contains(6) ? c.GoldActiveGross : c.GoldInactiveGross; }

        // No offset overloads: native Stat's offset is added AFTER cubing, unlike every linear getter.
        internal static double DiggerBonus(SupportContext c, SupportSelection selection, int id)
        {
            var selected = selection.Diggers.FirstOrDefault(x => x.Id == id);
            if (!selection.Diggers.Any(x => x.Id == id) || selected.Level <= 0) return 1;
            var d = c.Diggers.SingleOrDefault(x => x.Id == id);
            if (d == null) return double.NaN;
            if (c.DiggerEffective != null && c.CurrentDiggers.Any(x => x.Id == id && x.Level == selected.Level)) return c.DiggerEffective[id];
            if (id == 2) return Math.Max(1d, (1d + (d.StartBonus + Math.Pow(selected.Level, 3d) * d.PerLevel)) * (float)c.DiggerLevelBonus);
            return Math.Max(1f, (float)((float)(1f + (float)(d.StartBonus + selected.Level * d.PerLevel)) * (float)c.DiggerLevelBonus));
        }

        internal static SupportEvaluation Plan(SupportContext context, int[] beardPriorities, DiggerBudgetRequest[] requests, double gpsFraction)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var beards = !context.BeardsAvailable || context.BeardsDisabled ? new int[0] : (beardPriorities ?? new int[0])
                .Where(context.UnlockedBeards.Contains).Distinct().Take(Math.Max(0, context.BeardSlots)).ToArray();
            // Request weights/maximum are caller policy. Drain and purchased bounds are authoritative capture data.
            var legal = (requests ?? new DiggerBudgetRequest[0]).Where(x => x != null).Select(x => {
                var d = context.Diggers.SingleOrDefault(y => y.Id == x.Id);
                return d == null ? null : new DiggerBudgetRequest { Id = d.Id, Maximum = Math.Min(d.Maximum, x.Maximum), Weight = x.Weight,
                    BaseDrain = d.BaseDrain, Growth = d.Growth, ConsumesGold = d.ConsumesGold };
            }).Where(x => x != null).ToArray();
            var allocated = DiggerAllocation.Plan(legal, context.DiggersAvailable ? context.DiggerSlots : 0, GrossGold(context, beards) * gpsFraction);
            return Evaluate(context, new SupportSelection(beards, allocated.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), gpsFraction, true);
        }

        internal static SupportEvaluation Evaluate(SupportContext context, SupportSelection selection, double budgetFraction, bool enforceBudget)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            var b = selection.Beards; var d = selection.Diggers;
            var result = new SupportEvaluation { Selection = selection, GrossGold = GrossGold(context, b) };
            result.Budget = result.GrossGold * budgetFraction;
            if (!Finite(budgetFraction) || budgetFraction < 0 || budgetFraction > 1) result.Reason = "Invalid GPS budget fraction.";
            else if (b.Distinct().Count() != b.Length || d.Select(x => x.Id).Distinct().Count() != d.Length) result.Reason = "Duplicate support IDs.";
            else if (b.Length > Math.Max(0, context.BeardSlots) || b.Any(x => !context.UnlockedBeards.Contains(x))
                || b.Length > 0 && (!context.BeardsAvailable || context.BeardsDisabled)) result.Reason = "Beard selection is locked, disabled or exceeds slots.";
            else if (d.Length > Math.Max(0, context.DiggerSlots) || d.Length > 0 && !context.DiggersAvailable
                || d.Any(x => x.Level <= 0 || !context.Diggers.Any(y => y.Id == x.Id && x.Level <= y.Maximum))) result.Reason = "Digger selection is locked or exceeds purchased levels/slots.";
            if (result.Reason != null) return result;
            result.Drain = d.Sum(x => context.Diggers.Single(y => y.Id == x.Id).Drain(x.Level));
            result.NetGold = result.GrossGold - result.Drain;
            result.BeardNGU = BeardBonus(context, b, 3); result.BeardW = BeardBonus(context, b, 4); result.BeardStat = BeardBonus(context, b, 0);
            result.BeardGold = BeardBonus(context, b, 6); result.BeardAdv = BeardBonus(context, b, 5); result.BeardLoot = BeardBonus(context, b, 1);
            var effects = Enumerable.Range(0, 12).Select(id => DiggerBonus(context, selection, id)).ToArray();
            result.DiggerLoot = effects[0]; result.DiggerW = effects[1]; result.DiggerStat = effects[2]; result.DiggerAdv = effects[3];
            result.DiggerEnergyNGU = effects[4]; result.DiggerMagicNGU = effects[5]; result.DiggerEnergyBeard = effects[6]; result.DiggerMagicBeard = effects[7];
            result.DiggerPP = effects[8]; result.DiggerDaycare = effects[9]; result.DiggerBlood = effects[10]; result.DiggerEXP = effects[11];
            if (!Finite(result.GrossGold) || result.GrossGold < 0 || !Finite(result.Drain) || !Finite(result.Budget)
                || effects.Any(x => !Finite(x) || x < 1) || new[] { result.BeardNGU, result.BeardW, result.BeardStat, result.BeardGold, result.BeardAdv, result.BeardLoot }.Any(x => !Finite(x) || x < 1))
                result.Reason = "Non-finite or invalid support projection.";
            else if (result.NetGold < 0 || enforceBudget && result.Drain > result.Budget) result.Reason = "Exact digger selection is no longer affordable.";
            result.Feasible = result.Reason == null;
            return result;
        }

        internal static double ForwardGrossGold(double realBaseGold, long bossMulti, long barFills, long speedGold, long goldMulti,
            float bloodGold, float beardGold, double nguTM, double challenge, bool timeMachineChallenge)
        {
            return timeMachineChallenge ? 0d : realBaseGold * bossMulti * barFills * speedGold * goldMulti * bloodGold * beardGold * nguTM * challenge;
        }
    }
}
