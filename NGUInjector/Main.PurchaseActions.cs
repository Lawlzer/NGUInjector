using System;
using System.Linq;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class DiggerPurchaseAction : FiniteAction
        {
            internal int Id;
            internal long Level;
            internal double Gold, Cost, Reserve;
            internal override bool Prerequisites() => Character.diggers.diggers[Id].maxLevel == Level
                && Character.realGold == Gold && Character.allDiggers.upgradeCost(Id) == Cost
                && Cost > 0d && Cost < Gold - Reserve && Level < Character.allDiggers.hardCapLevel(Id);
            internal override string Observe() => ActionNumber(Character.diggers.diggers[Id].maxLevel) + "|" + ActionNumber(Character.realGold);
            internal override void Invoke() => Character.allDiggers.upgradeMaxLevel(Id);
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                if (Character.diggers.diggers[Id].maxLevel == Level + 1 && Character.realGold == Gold - Cost)
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                // Native refresh may throw after commitment; unchanged is not assumed safe to retry.
                return MutationDisposition.UnknownOutcome;
            }
        }

        internal void PurchaseActionDiggers(bool ignoreSetting = false, double reserveGold = -1d)
        {
            if (!ignoreSetting && !Settings.UpgradeDiggers) return;
            var reserve = reserveGold < 0d ? Settings.MoneyPitThreshold : reserveGold;
            if (!(reserve >= 0d && reserve <= double.MaxValue)) return;
            for (var attempt = 0; attempt < 10000; attempt++)
            {
                var id = Enumerable.Range(0, Character.diggers.diggers.Count)
                    .Where(i => Character.diggers.diggers[i].maxLevel < Character.allDiggers.hardCapLevel(i))
                    .Select(i => new { Id = i, Cost = Character.allDiggers.upgradeCost(i) })
                    .Where(x => x.Cost > 0d && x.Cost <= double.MaxValue).OrderBy(x => x.Cost).FirstOrDefault();
                if (id == null || !(id.Cost < Character.realGold - reserve)) return;
                var identity = ActionIdentity("digger-purchase:" + id.Id, "digger-purchases", ActionNumber(Character.diggers.diggers[id.Id].maxLevel));
                var action = new DiggerPurchaseAction { Identity = identity, Id = id.Id, Cost = id.Cost, Reserve = reserve,
                    Gold = Character.realGold, Level = Character.diggers.diggers[id.Id].maxLevel,
                    Footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Support };
                var result = RunFiniteAction(action, ResolveExecutionGrant(identity.Owner, identity.Acquisition, action.Footprint,
                    () => ignoreSetting || Settings.UpgradeDiggers));
                if (!result.Verified) return;
            }
        }

        private sealed class ExpPurchaseAction : FiniteAction
        {
            internal int Stat;
            internal long Amount;
            internal long Cost, Currency;
            internal decimal Base;
            internal object Purchases, NativeSettings;
            internal FieldInfo Input;
            internal MethodInfo Purchase;
            internal ExecutionGrant Grant;
            private decimal CurrentBase() => Stat == 0 ? ExactBasePower(Character.energyPower) : Stat == 1 ? Character.capEnergy
                : Stat == 2 ? Character.energyBars : Stat == 3 ? ExactBasePower(Character.magic.magicPower)
                : Stat == 4 ? Character.magic.capMagic : Character.magic.magicPerBar;
            private long CurrentCost() => Stat == 0 ? Character.energyPurchases.customPowerCost((int)Amount)
                : Stat == 1 ? Character.energyPurchases.customCapCost(Amount) : Stat == 2 ? Character.energyPurchases.customBarCost((int)Amount)
                : Stat == 3 ? Character.magicPurchases.customPowerCost((int)Amount)
                : Stat == 4 ? Character.magicPurchases.customCapCost(Amount) : Character.magicPurchases.customBarCost((int)Amount);
            internal override bool Prerequisites() => ReferenceEquals(NativeSettings, Character.settings)
                && CurrentBase() == Base && Character.realExp == Currency && Currency >= Cost && Cost > 0 && CurrentCost() == Cost;
            internal override string Observe() => CurrentBase().ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + ActionNumber(Character.realExp);
            internal override void Invoke()
            {
                var original = Input.GetValue(NativeSettings);
                var wanted = Stat % 3 == 1 ? (object)Amount : (int)Amount;
                try
                {
                    if (!reference.ValidateExecutionGrant(Grant, Footprint)) throw new InvalidOperationException("EXP input authority changed.");
                    Input.SetValue(NativeSettings, wanted);
                    if (!reference.ValidateExecutionGrant(Grant, Footprint)) throw new InvalidOperationException("EXP purchase authority changed.");
                    Purchase.Invoke(Purchases, null);
                }
                finally
                {
                    // Only return the exact field still borrowed in this native settings object.
                    if (reference.ValidateExecutionGrant(Grant, Footprint) && ReferenceEquals(NativeSettings, Character.settings)
                        && Equals(Input.GetValue(NativeSettings), wanted)) Input.SetValue(NativeSettings, original);
                }
            }
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                if (CurrentBase() == Base + Amount && Character.realExp == Currency - Cost)
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                return MutationDisposition.UnknownOutcome;
            }
        }
    }
}
