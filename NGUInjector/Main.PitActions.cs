using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private object _pitAttemptModel, _spinAttemptModel;
        private string _pitAttemptOpportunity, _spinAttemptOpportunity;
        private sealed class PitFeedAction : FiniteAction
        {
            internal MoneyPitManager.RewardPreview Reward;
            internal Func<bool> SetupVerified;
            internal override bool Prerequisites()
            {
                var actual = MoneyPitManager.PreviewReward();
                return SetupVerified() && Character.realGold > Math.Max(200000d, Settings.MoneyPitThreshold)
                    && Character.pit.pitTime.totalseconds >= Character.pitController.currentPitTime()
                    && actual.Tier == Reward.Tier && actual.Roll == Reward.Roll && actual.RandomSlots.SequenceEqual(Reward.RandomSlots);
            }
            internal override string Observe() => ActionNumber(Character.pit.totalGold) + "|" + ActionNumber(Character.realGold)
                + "|" + ActionNumber(Character.pit.pitTime.totalseconds);
            internal override void Invoke()
            {
                var method = typeof(PitController).GetMethod("engage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) throw new MissingMethodException("PitController.engage");
                method.Invoke(Character.pitController, null);
            }
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                var values = before.Split('|');
                var total = double.Parse(values[0], System.Globalization.CultureInfo.InvariantCulture);
                var gold = double.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture);
                if (Character.pit.totalGold == total + gold && Character.realGold == 0d && Character.pit.pitTime.totalseconds == 0d)
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                // A failed reward callback may have granted a reward without reaching accounting.
                // Unchanged visible accounting is NOT proof that native engage did nothing.
                return MutationDisposition.UnknownOutcome;
            }
        }

        internal string RequestMoneyPit()
        {
            if (CustomBossFightActive()) return "Money Pit waiting for the current native boss fight to stop.";
            var pit = Character.pit;
            if (Character.pit.pitTime.totalseconds < Character.pitController.currentPitTime()) return "Money Pit skipped: pit is not ready.";
            var minimumFeedGold = Math.Max(200000d, Settings.MoneyPitThreshold);
            if (Character.realGold <= minimumFeedGold) return "Money Pit skipped: gold is below the feed minimum.";
            var reward = MoneyPitManager.PreviewReward();
            var id = ActionIdentity("pit-feed", "money-pit", ActionNumber(Character.pit.tossCount));
            if (ReferenceEquals(_pitAttemptModel, pit) && _pitAttemptOpportunity == id.Key)
                return "Money Pit skipped: this native feed opportunity was already attempted.";
            var needsBlood = Character.realGold >= 1e50 && Settings.ManageMagic && Character.wishes.wishes[4].level > 0;
            var domains = ExecutionDomain.FiniteAction | ExecutionDomain.Gear | ExecutionDomain.Support | ExecutionDomain.Route
                | ExecutionDomain.Inventory | ExecutionDomain.NativeSettings | (needsBlood ? ExecutionDomain.Magic : 0);
            if (ExecutionOwnsDomain(domains)) return "Money Pit waiting for current execution ownership.";
            ExecutionClaim claim = null;
            var reconcile = false;
            try
            {
                claim = BeginExecutionClaim(id.Owner, id.Acquisition, domains, () => !CustomBossFightActive());
                if (Character.adventure.zone != -1 && !CaptureExecutionBaselineRouteRetirement(claim)())
                    return "Money Pit waiting for the current adventure owner.";
                var original = LoadoutManager.CaptureExactLoadout();
                var gear = reward.LevelsEquipment ? MoneyPitLoadout.Select(original,
                    original.Concat(Character.inventory.GetConvertedInventory().Where(x => x.slot < Controller.curSpaces())),
                    reward.AllEquipment ? original.Select(x => x.slot).ToArray() : reward.RandomSlots) : original;
                var support = needsBlood ? SelectActionSupport(CaptureSupportState().Selection.Beards, 10) : CaptureSupportState().Selection;
                reconcile = true;
                var setup = AcquireFiniteActionSetup(claim, gear, support, reward.LevelsEquipment, needsBlood);
                if (!FiniteActionSetupVerified(claim, setup)) return "Money Pit waiting for verified selected setup.";
                if (needsBlood)
                {
                    var rituals = ExactPitRitualDemand();
                    SetExecutionResourcePolicy(claim, new ResourceSetupIntent { Owner = id.Owner, Magic = true,
                        Desired = new Dictionary<ResourceTarget, long[]> { { ResourceTarget.BloodRitual, rituals } },
                        Authorized = () => ExecutionClaimCurrent(claim) });
                    ReconcileExecutionResourceClaim(claim);
                    if (!Character.bloodMagic.ritual.Select(x => x.magic).SequenceEqual(rituals)) return "Money Pit waiting for exact ritual funding.";
                }
                var action = new PitFeedAction { Identity = id, Reward = reward, Footprint = domains, ChangesEquipmentMaterial = reward.LevelsEquipment,
                    SetupVerified = () => FiniteActionSetupVerified(claim, setup) };
                var reviseMaterial = CapturePitMaterialRevision(gear, reward, action);
                var grant = ResolveExecutionGrant(id.Owner, id.Acquisition, domains, () => ExecutionClaimCurrent(claim), claim);
                var outcome = RunFiniteAction(action, grant);
                if (outcome.Invoked) { _pitAttemptModel = pit; _pitAttemptOpportunity = id.Key; }
                if (outcome.Committed && outcome.AuthorityCurrent && claim.Epoch.Matches())
                {
                    reviseMaterial();
                    ObserveExecutionClaimProgress(setup, gear);
                }
                if (!outcome.Verified) return "Money Pit: " + (outcome.Reason ?? outcome.Disposition.ToString());
                // Presentation follows retirement of this native opportunity.
                var message = "Money Pit Reward: " + Character.pitController.pitText.text;
                LogPitSpin(message);
                return message;
            }
            finally { if (claim != null) EndExecutionClaim(claim, reconcile); }
        }

        private Action CapturePitMaterialRevision(ih[] gear, MoneyPitManager.RewardPreview reward, FiniteAction action)
        {
            var before = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var expected = gear.Select(item => {
                var copy = (Equipment)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(item.equipment, null);
                var hits = reward.AllEquipment ? 1 : reward.RandomSlots.Count(x => x == item.slot);
                if (copy.id != 0) copy.level = Math.Min(100, copy.level + hits);
                return LoadoutPresetPrimitiveKey(copy);
            }).ToArray();
            action.ExpectedEquipmentMaterial = item => {
                var index = Array.FindIndex(gear, x => ReferenceEquals(x.equipment, item));
                return index < 0 ? null : expected[index];
            };
            var inline = CaptureInlineMaterialRevision(); var focus = CaptureFocusMaterialRevision(); var combat = CaptureCombatPrepMaterialRevision();
            return () => {
                if (!LoadoutManager.ExactLoadoutMatches(gear)
                    || gear.Where((item, i) => LoadoutPresetPrimitiveKey(item.equipment) != expected[i]).Any())
                    throw new InvalidOperationException("Pit reward material does not match the exact native level-only footprint; current setup requires review.");
                inline?.Invoke(); focus?.Invoke(); combat?.Invoke();
                foreach (var policy in RetainedExecutionPresetPolicies())
                {
                    if (policy.Items == null || policy.Material == null) continue;
                    var revised = (string[])policy.Material.Clone(); var changed = false; var owned = true;
                    for (var i = 0; i < policy.Items.Length; i++)
                    {
                        var slot = Array.FindIndex(gear, x => ReferenceEquals(x.equipment, policy.Items[i]));
                        if (slot >= 0)
                        {
                            if (policy.Material[i] != before[slot]) { owned = false; break; }
                            revised[i] = expected[slot]; changed |= revised[i] != policy.Material[i];
                        }
                        else if (LoadoutPresetPrimitiveKey(policy.Items[i]) != policy.Material[i]) { owned = false; break; }
                    }
                    if (!owned || !changed) continue;
                    policy.Material = revised;
                    ApproveExecutionPolicyMaterialRevision(policy);
                }
            };
        }

        private static long[] ExactPitRitualDemand()
        {
            var c = Character; var result = new long[c.bloodMagic.ritual.Count];
            var budget = Math.Max(0L, c.magic.curMagic);
            for (var i = result.Length - 1; i >= 0; i--)
            {
                var ritual = c.bloodMagic.ritual[i]; var ctrl = c.bloodMagicController.bloodMagics[i];
                if (i >= c.bloodMagicController.ritualsUnlocked() || c.bossID <= ritual.boss
                    || ritual.progress <= 0 && c.realGold <= ctrl.currentCost()) continue;
                var divider = c.settings.rebirthDifficulty == difficulty.normal ? c.bloodMagicController.normalSpeedDividers[i] * 50000d
                    : c.settings.rebirthDifficulty == difficulty.evil ? c.bloodMagicController.evilSpeedDividers[i] * 50000d
                    : c.bloodMagicController.sadisticSpeedDividers[i] * ctrl.sadisticDivider();
                var cap = divider / ((double)c.totalMagicPower() * ctrl.totalBloodMagicSpeedBonus()) * 1.000002d;
                if (!(cap >= 0d && cap < long.MaxValue)) throw new InvalidOperationException("Invalid pit ritual cap.");
                result[i] = Math.Min(budget, (long)cap + 1); budget -= result[i];
            }
            return result;
        }

        private sealed class DailySpinAction : FiniteAction
        {
            internal override bool Prerequisites() => Character.daily.spinTime.totalseconds >= Character.dailyController.targetSpinTime();
            internal override string Observe() => ActionNumber(Character.daily.spinTime.totalseconds);
            internal override void Invoke() => Character.dailyController.startNoBullshitSpin();
            internal override MutationDisposition Classify(string before, string after, Exception error) => Character.daily.spinTime.totalseconds == 0d
                ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.UnknownOutcome;
        }

        internal string RequestDailySpin()
        {
            var daily = Character.daily;
            var opportunity = ActionNumber((long)Math.Round(Character.totalPlaytime.totalseconds - Character.daily.spinTime.totalseconds));
            var id = ActionIdentity("daily-spin", "daily-spin", opportunity);
            if (ReferenceEquals(_spinAttemptModel, daily) && _spinAttemptOpportunity == id.Key)
                return "Daily Spin: this native opportunity was already attempted.";
            var action = new DailySpinAction { Identity = id, Footprint = ExecutionDomain.FiniteAction };
            var result = RunFiniteAction(action, ResolveExecutionGrant(id.Owner, id.Acquisition, action.Footprint, () => true));
            if (result.Invoked) { _spinAttemptModel = daily; _spinAttemptOpportunity = id.Key; }
            if (!result.Verified) return "Daily Spin: " + (result.Reason ?? result.Disposition.ToString());
            var message = "Daily Spin Reward: " + Character.dailyController.outcomeText.text;
            LogPitSpin(message); return message;
        }
    }
}
