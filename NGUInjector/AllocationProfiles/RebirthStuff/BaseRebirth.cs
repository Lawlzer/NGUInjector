using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NGUInjector.Managers;

namespace NGUInjector.AllocationProfiles.RebirthStuff
{
    internal enum ChallengeType
    {
        Basic,
        NoAug,
        TwentyFourHour,
        OneHundredLC,
        NoEquip,
        Troll,
        NoRebirth,
        LaserSword,
        Blind,
        NoNGU,
        NoTimeMachine
    }

    internal class RCTarget
    {
        internal ChallengeType Challenge { get; set; }
        internal int Index { get; set; }

        public override string ToString()
        {
            return $"{Challenge}-{Index}";
        }
    }

    internal abstract class BaseRebirth
    {
        internal static BaseRebirth CreateRebirth(double target, string type, string[] challenges)
        {
            type = type.ToUpper();
            if (type == "TIME")
            {
                return new TimeRebirth
                {
                    CharObj = Main.Character,
                    ChallengeTargets = ParseChallenges(challenges),
                    RebirthController = Main.Character.rebirth,
                    RebirthTime = target
                };
            }
            
            if (type == "NUMBER")
            {
                return new NumberRebirth
                {
                    CharObj = Main.Character,
                    ChallengeTargets = ParseChallenges(challenges),
                    RebirthController = Main.Character.rebirth,
                    MultTarget = target
                };
            }

            if (type == "BOSSES")
            {
                return new BossNumRebirth
                {
                    CharObj = Main.Character,
                    ChallengeTargets = ParseChallenges(challenges),
                    RebirthController = Main.Character.rebirth,
                    NumBosses = target
                };
            }

            return new NoRebirth();
        }

        private static Dictionary<string, ChallengeType> CMap = new Dictionary<string, ChallengeType>
        {
            {"BASIC", ChallengeType.Basic},
            {"NOAUG", ChallengeType.NoAug},
            {"24HR", ChallengeType.TwentyFourHour},
            {"100LC", ChallengeType.OneHundredLC},
            {"NOEC", ChallengeType.NoEquip},
            {"TC", ChallengeType.Troll},
            {"NORB", ChallengeType.NoRebirth},
            {"LSC", ChallengeType.LaserSword},
            {"BLIND", ChallengeType.Blind},
            {"NONGU", ChallengeType.NoNGU},
            {"NOTM", ChallengeType.NoTimeMachine},
        };

        internal RCTarget[] ChallengeTargets { get; set; }
        protected Rebirth RebirthController;
        internal abstract bool RebirthAvailable();
        protected Character CharObj;
        protected BaseRebirth()
        {
            CharObj = Main.Character;
            RebirthController = CharObj.rebirth;
        }

        internal static RCTarget[] ParseChallenges(string[] challenges)
        {
            if (challenges == null)
                return new RCTarget[0];

            var parsed = new List<RCTarget>();
            foreach (var c in challenges.Select(x => x.ToUpper()))
            {
                if (!c.Contains("-"))
                    continue;

                var split = c.Split('-');
                var challenge = split[0].ToUpper();
                if (!CMap.ContainsKey(challenge))
                    continue;

                if (!int.TryParse(split[1], out var index))
                    continue;

                parsed.Add(new RCTarget
                {
                    Index = index,
                    Challenge = CMap[challenge]
                });
            }

            return parsed.ToArray();
        }

        protected bool EngageChalRebirth(string rbType)
        {
            return Main.reference != null && Main.reference.InvokeFinalRebirthAction(rbType);
        }

        protected bool BaseRebirthChecks()
        {
            return CharObj.rebirthTime.totalseconds > RebirthController.minRebirthTime() && !CharObj.challenges.noRebirthChallenge.inChallenge;
        }

        protected bool EngageRebirth()
        {
            return Main.reference != null && Main.reference.InvokeFinalRebirthAction("engage");
        }

        protected bool AnyChallengesValid()
        {
            if (ChallengeTargets.Length == 0)
                return false;

            var cc = CharObj.allChallenges;
            foreach (var rc in ChallengeTargets)
            {
                var i = rc.Index;
                switch (rc.Challenge)
                {
                    case ChallengeType.Basic:
                        if (i > cc.basicChallenge.maxCompletions || i != cc.basicChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.NoAug:
                        if (i > cc.noAugsChallenge.maxCompletions || i != cc.noAugsChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.TwentyFourHour:
                        if (i > cc.hour24Challenge.maxCompletions || i != cc.hour24Challenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.OneHundredLC:
                        if (i > cc.level100Challenge.maxCompletions || i != cc.level100Challenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.NoEquip:
                        if (i > cc.noEquipmentChallenge.maxCompletions || i != cc.noEquipmentChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.Troll:
                        if (i > cc.trollChallenge.maxCompletions || i != cc.trollChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.NoRebirth:
                        if (i > cc.noRebirthChallenge.maxCompletions || i != cc.noRebirthChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.LaserSword:
                        if (i > cc.laserSwordChallenge.maxCompletions || i != cc.laserSwordChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.Blind:
                        if (i > cc.blindChallenge.maxCompletions || i != cc.blindChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.NoNGU:
                        if (i > cc.NGUChallenge.maxCompletions || i != cc.NGUChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    case ChallengeType.NoTimeMachine:
                        if (i > cc.timeMachineChallenge.maxCompletions || i != cc.timeMachineChallenge.currentCompletions() + 1)
                            continue;
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return false;
        }

        protected bool TryStartChallenge()
        {
            if (ChallengeTargets.Length == 0)
                return false;

            var cc = CharObj.allChallenges;
            foreach (var rc in ChallengeTargets)
            {
                var i = rc.Index;
                switch (rc.Challenge)
                {
                    case ChallengeType.Basic:
                        if (i > cc.basicChallenge.maxCompletions || i != cc.basicChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageBasicChallenge");
                    case ChallengeType.NoAug:
                        if (i > cc.noAugsChallenge.maxCompletions || i != cc.noAugsChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageNoAugsChallenge");
                    case ChallengeType.TwentyFourHour:
                        if (i > cc.hour24Challenge.maxCompletions || i != cc.hour24Challenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engage24HourChallenge");
                    case ChallengeType.OneHundredLC:
                        if (i > cc.level100Challenge.maxCompletions || i != cc.level100Challenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engagelevel100Challenge");
                    case ChallengeType.NoEquip:
                        if (i > cc.noEquipmentChallenge.maxCompletions || i != cc.noEquipmentChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageNoEquipChallenge");
                    case ChallengeType.Troll:
                        if (i > cc.trollChallenge.maxCompletions || i != cc.trollChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageTrollChallenge");
                    case ChallengeType.NoRebirth:
                        if (i > cc.noRebirthChallenge.maxCompletions || i != cc.noRebirthChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageNoRebirthChallenge");
                    case ChallengeType.LaserSword:
                        if (i > cc.laserSwordChallenge.maxCompletions || i != cc.laserSwordChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageLaserSwordChallenge");
                    case ChallengeType.Blind:
                        if (i > cc.blindChallenge.maxCompletions || i != cc.blindChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageBlindChallenge");
                    case ChallengeType.NoNGU:
                        if (i > cc.NGUChallenge.maxCompletions || i != cc.NGUChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageNGUChallenge");
                    case ChallengeType.NoTimeMachine:
                        if (i > cc.timeMachineChallenge.maxCompletions || i != cc.timeMachineChallenge.currentCompletions() + 1)
                            continue;
                        return EngageChalRebirth("engageTimeMachineChallenge");
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return false;
        }

        internal bool DoRebirth()
        {
            if (PreRebirth())
                return false;

            if (Main.reference == null) return false;
            try
            {
                if (!Main.reference.PrepareFinalRebirthAction() || !Main.reference.RefreshFinalRebirthMultipliers()) return false;
                if (!CharObj.challenges.inChallenge && ChallengeTargets.Length > 0 && AnyChallengesValid())
                    return TryStartChallenge();
                return EngageRebirth();
            }
            finally { Main.reference.ReleaseFinalRebirthAction(); }
        }

        protected bool PreRebirth()
        {
            if (Main.Settings.ManageYggdrasil && YggdrasilManager.AnyHarvestable())
            {
                if (Main.Settings.SwapYggdrasilLoadouts && Main.Settings.YggdrasilLoadout.Length > 0)
                {
                    if (!YggdrasilManager.TryPrepareHarvest())
                    {
                        Main.Log("Delaying rebirth to wait for ygg loadout/diggers");
                        return true;
                    }

                    try { YggdrasilManager.HarvestAll(); }
                    finally { YggdrasilManager.CleanupOwnedHarvest(); }
                    Main.Log("Delaying rebirth 1 loop to allow fruit effects");
                    return true;
                }

                YggdrasilManager.HarvestAll();
                Main.Log("Delaying rebirth 1 loop to allow fruit effects");
                return true;
            }

            YggdrasilManager.CleanupOwnedHarvest();
            Main.reference?.PurchaseActionDiggers();

            CastBloodSpells(true);
            return Main.reference == null || !Main.reference.RebirthFinitePrerequisitesResolved;
        }

        protected void CastBloodSpells(bool rebirth)
        {
            Main.reference?.RequestThresholdBloodSpells(rebirth);
        }
    }
}
