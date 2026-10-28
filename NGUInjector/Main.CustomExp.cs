using System;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private bool _autoExpBatchRunning, _expPurchaseRunning;
        private sealed class ExpPurchaseBatchResult
        {
            internal bool Completed;
            internal int Invoked;
            internal long Spent;
            internal FiniteActionResult Last;
        }

        private static bool AutoExpInputFocused()
        {
            var energy = Character.energyPurchases; var magic = Character.magicPurchases;
            return Character.menuID == 13 && energy != null && (energy.powerInput?.isFocused == true
                || energy.capInput?.isFocused == true || energy.barInput?.isFocused == true)
                || Character.menuID == 32 && magic != null && (magic.powerInput?.isFocused == true
                || magic.capInput?.isFocused == true || magic.barInput?.isFocused == true);
        }

        private ExpPurchaseBatchResult RunCustomAutoExp()
        {
            var batch = new ExpPurchaseBatchResult { Completed = true };
            if (_autoExpBatchRunning || _expPurchaseRunning) { batch.Completed = false; return batch; }
            if (Settings?.CustomAutoExpEnabled != true || Character == null || Character.settings == null
                || Character.highestBoss < 17 || Character.realExp <= 0 || Character.energyPurchases == null)
                return batch;

            _autoExpBatchRunning = true;
            try
            {
                // Purchase refreshes rewrite the game's inputs; do not interrupt a manual edit.
                if (AutoExpInputFocused()) return batch;
                var magic = Character.highestBoss >= 37 && Character.magic != null && Character.magicPurchases != null;
                var stats = new[]
                {
                    ExactBasePower(Character.energyPower), Character.capEnergy, Character.energyBars,
                    magic ? ExactBasePower(Character.magic.magicPower) : 0m,
                    magic ? Character.magic.capMagic : 0m, magic ? Character.magic.magicPerBar : 0m
                };
                var ratios = new[]
                {
                    Settings.CustomExpEnergyRatio, Settings.CustomExpMagicRatio,
                    Settings.CustomExpEnergyPowerRatio, Settings.CustomExpEnergyCapRatio, Settings.CustomExpEnergyBarsRatio,
                    Settings.CustomExpMagicPowerRatio, Settings.CustomExpMagicCapRatio, Settings.CustomExpMagicBarsRatio
                };
                var unlocked = new[] { true, Character.capEnergy >= 100000, true, magic, magic, magic };
                var amounts = ExpPurchasePlanner.Plan(stats, ratios, unlocked, Character.realExp,
                    Character.hardCap(), Character.hardCapPowBar());
                var before = Character.realExp;
                for (var i = 0; i < amounts.Length; i++)
                {
                    var amount = amounts[i];
                    if (amount <= 0) continue;
                    // The game stores power as float: never pay for an unrepresentable increase.
                    if (i % 3 == 0)
                    {
                        var current = i == 0 ? Character.energyPower : Character.magic.magicPower;
                        // CLR locals can retain extra precision; match the native binary32 field write before checking gain.
                        var next = BitConverter.ToSingle(BitConverter.GetBytes(current + (float)amount), 0);
                        if (next >= 16777216f)
                        {
                            // Include purchases that cross float's exact-integer boundary.
                            if ((double)next - current > amount)
                                next = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(next), 0) - 1), 0);
                            amount = Math.Min(amount, (long)Math.Max(0d, (double)next - current));
                            next = BitConverter.ToSingle(BitConverter.GetBytes(current + (float)amount), 0);
                            if ((double)next - current < amount) continue;
                        }
                        if (next <= current || (double)next > Character.hardCapPowBar()) continue;
                    }
                    batch.Last = BuyCustomExpStat(i, amount);
                    if (batch.Last.Invoked) batch.Invoked++;
                    if (!batch.Last.Verified) { batch.Completed = false; break; }
                }
                batch.Spent = Math.Max(0, before - Character.realExp);
                if (batch.Spent > 0)
                    Log("Auto use EXP spent " + batch.Spent + " EXP balancing BASE Energy/Magic stats.");
            }
            catch (Exception e)
            {
                batch.Completed = false;
                LogError("Auto use EXP", e);
            }
            finally { _autoExpBatchRunning = false; }
            return batch;
        }

        // A direct float-to-decimal cast keeps only seven significant decimal digits.
        private static decimal ExactBasePower(float power) => (decimal)(long)power + (decimal)(power % 1f);

        private FiniteActionResult BuyCustomExpStat(int stat, long amount)
        {
            if (_expPurchaseRunning)
                return new FiniteActionResult { Disposition = MutationDisposition.RejectedUnchanged, Reason = "An EXP purchase is already running." };
            _expPurchaseRunning = true;
            try
            {
                var magic = stat >= 3;
                var kind = stat % 3;
                object purchases = magic ? (object)Character.magicPurchases : Character.energyPurchases;
                var suffix = kind == 0 ? "Power" : kind == 1 ? "Cap" : "Bar";
                var method = purchases.GetType().GetMethod("buyCustom" + suffix,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method == null) throw new MissingMethodException(purchases.GetType().Name, "buyCustom" + suffix);

                var fieldName = "custom" + (magic ? "Magic" : "") + suffix + "Amount";
                var field = Character.settings.GetType().GetField(fieldName);
                if (field == null) throw new MissingFieldException(Character.settings.GetType().Name, fieldName);
                var expectedCost = amount / ExpPurchasePlanner.Quanta[stat] * ExpPurchasePlanner.Costs[stat];
                var actualCost = magic
                    ? kind == 0 ? Character.magicPurchases.customPowerCost((int)amount)
                        : kind == 1 ? Character.magicPurchases.customCapCost(amount) : Character.magicPurchases.customBarCost((int)amount)
                    : kind == 0 ? Character.energyPurchases.customPowerCost((int)amount)
                        : kind == 1 ? Character.energyPurchases.customCapCost(amount) : Character.energyPurchases.customBarCost((int)amount);
                if (expectedCost <= 0 || actualCost != expectedCost || Character.realExp < actualCost)
                    return new FiniteActionResult { Disposition = MutationDisposition.RejectedUnchanged, Reason = "EXP price or current budget changed." };

                var current = stat == 0 ? ExactBasePower(Character.energyPower) : stat == 1 ? Character.capEnergy
                    : stat == 2 ? Character.energyBars : stat == 3 ? ExactBasePower(Character.magic.magicPower)
                    : stat == 4 ? Character.magic.capMagic : Character.magic.magicPerBar;
                var identity = ActionIdentity("exp-purchase:" + stat, "auto-exp", current.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var footprint = ExecutionDomain.FiniteAction | ExecutionDomain.NativeSettings
                    | (magic ? ExecutionDomain.Magic : ExecutionDomain.Energy);
                var grant = ResolveExecutionGrant(identity.Owner, identity.Acquisition, footprint, () => Settings.CustomAutoExpEnabled);
                var action = new ExpPurchaseAction { Identity = identity, Footprint = footprint, Grant = grant,
                    Stat = stat, Amount = amount, Cost = actualCost, Currency = Character.realExp, Base = current, Model = Character,
                    Purchases = purchases, NativeSettings = Character.settings, Input = field, Purchase = method };
                var result = RunFiniteAction(action, grant);
                if (result.Verified)
                {
                    var presentation = ResolveExecutionGrant(identity.Owner, identity.Acquisition, footprint, () => Settings.CustomAutoExpEnabled);
                    if (ValidateExecutionGrant(presentation, footprint))
                    {
                        try
                        {
                            if (magic) Character.magicPurchases.refresh();
                            else Character.energyPurchases.refresh();
                            result.AuthorityCurrent = ValidateExecutionGrant(presentation, footprint);
                        }
                        catch (Exception error)
                        {
                            result.Error = error; result.Reason = error.Message;
                            result.Disposition = MutationDisposition.ModelAppliedRenderFailed;
                            result.AuthorityCurrent = false;
                            LogError("Auto use EXP", error);
                        }
                    }
                    else result.AuthorityCurrent = false;
                }
                return result;
            }
            finally { _expPurchaseRunning = false; }
        }
    }
}
