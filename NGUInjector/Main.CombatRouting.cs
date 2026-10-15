using System;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private string _manualAdventureGearKey;
        private string _manualAdventureGearMessage;
        private string _manualAdventureDiggerKey;
        private string _manualAdventureDiggerMessage;

        private void CustomAdventureTick()
        {
            try
            {
                ObserveCustomRebirthEpoch();
                if (TryRoutePriorityTitan())
                    return;

                if (TryRouteManualAdventure())
                    return;

                TryRouteCustomAdventureGoal();
            }
            catch (Exception e)
            {
                LogError("CustomAdventureTick", e);
            }
        }

        private bool TryRoutePriorityTitan()
        {
            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if (CustomGoalBlocksAdventureGoldPush(plan, out var blockedReason))
            {
                CancelCustomTitanGoldKill(blockedReason);
                CancelCustomTitanFightKill(blockedReason);
                return false;
            }

            if (TryRouteCustomTitanGoldKill())
                return true;

            return TryRouteCustomTitanFightKill();
        }

        private bool TryRouteManualAdventure()
        {
            if (!Settings.ManualAdventureEnabled)
            {
                ResetManualAdventureSetup();
                return false;
            }

            if (_questManager != null && _questManager.IsQuesting() >= 0)
            {
                ResetManualAdventureSetup();
                SetCustomAutomationStatus("Manual adventure paused: questing is active.");
                return true;
            }

            var plan = Settings.UseCustomRebirthPlan && CustomPlanStore != null ? LoadRuntimeCustomPlan() : null;
            if (CustomGoalBlocksAdventureGoldPush(plan, out var blockedReason))
            {
                ResetManualAdventureSetup();
                SetCustomAutomationStatus($"Manual adventure paused: {blockedReason}.");
                return true;
            }

            CancelCustomAdventureGoldPush("manual adventure fighting");
            CancelCustomTitanGoldKill("manual adventure fighting");

            var targetZone = Settings.ManualAdventureZone;
            var routeZone = targetZone;
            var fallbackNote = string.Empty;
            if (!CombatManager.IsZoneUnlocked(routeZone))
            {
                if (Settings.ManualAdventureAllowFallback)
                {
                    var fallbackZone = ZoneHelpers.GetMaxReachableZone(false);
                    if (fallbackZone >= 0)
                    {
                        routeZone = fallbackZone;
                        fallbackNote = $"{ZoneName(targetZone)} locked; fallback to ";
                    }
                }

                if (routeZone == targetZone)
                {
                    ResetManualAdventureSetup();
                    SetCustomAutomationStatus($"Manual adventure: {ZoneName(targetZone)} is locked; waiting in Safe Zone.");
                    _combManager.MoveToZone(-1);
                    return true;
                }
            }

            var gearMessage = ApplyManualAdventureGear(routeZone);
            var diggerMessage = ApplyManualAdventureDiggers(routeZone);
            var gearStatus = string.IsNullOrWhiteSpace(gearMessage) ? string.Empty : $" {gearMessage}.";
            var diggerStatus = string.IsNullOrWhiteSpace(diggerMessage) ? string.Empty : $" {diggerMessage}.";
            var bossMode = Settings.ManualAdventureBossOnly ? " boss-only" : string.Empty;
            SetCustomAutomationStatus($"Manual adventure: {fallbackNote}manually fighting{bossMode} {ZoneName(routeZone)}.{gearStatus}{diggerStatus}");
            _combManager.ManualZone(
                routeZone,
                Settings.ManualAdventureBossOnly,
                Settings.ManualAdventureRecoverHealth,
                Settings.ManualAdventurePrecastBuffs,
                Settings.ManualAdventureFastCombat,
                Settings.ManualAdventureBeastMode);
            return true;
        }

        private void ResetManualAdventureSetup()
        {
            _manualAdventureGearKey = null;
            _manualAdventureGearMessage = null;
            _manualAdventureDiggerKey = null;
            _manualAdventureDiggerMessage = null;
        }

        private string ApplyManualAdventureGear(int routeZone)
        {
            var key = $"{routeZone}:manual-adventure-gear:pt-cooldown";
            if (_manualAdventureGearKey == key && !string.IsNullOrWhiteSpace(_manualAdventureGearMessage))
                return _manualAdventureGearMessage;

            var result = GearOptimizer.OptimizeAndEquip(GearOptimizationGoal.ManualAdventure, FightScoreMode.PowerTimesToughness);
            _manualAdventureGearMessage = result.Message;
            if (result.Success)
                _manualAdventureGearKey = key;

            return result.Message;
        }

        private string ApplyManualAdventureDiggers(int routeZone)
        {
            var key = $"{routeZone}:manual-adventure:800:400";
            if (_manualAdventureDiggerKey == key && !string.IsNullOrWhiteSpace(_manualAdventureDiggerMessage))
                return _manualAdventureDiggerMessage;

            var message = DiggerManager.EquipWeightedDiggers(new[]
            {
                Digger(DiggerAdventure, 800, "adventure"),
                Digger(DiggerDropChance, 400, "drop")
            }, 0.75d);
            _manualAdventureDiggerMessage = message;
            if (!message.StartsWith("diggers skipped", StringComparison.OrdinalIgnoreCase))
                _manualAdventureDiggerKey = key;

            return message;
        }

        private void SnipeZone()
        {
            try
            {
                if (!LegacyAutomationEnabled || !Settings.GlobalEnabled)
                    return;

                //If tm ever drops to 0, reset our gold loadout stuff
                if (Character.machine.realBaseGold == 0.0 && !Settings.DoGoldSwap)
                {
                    Log("Time Machine Gold is 0. Lets reset gold snipe zone.");
                    Settings.DoGoldSwap = true;
                    Settings.TitanMoneyDone = new bool[ZoneHelpers.TitanZones.Length];
                }

                //This logic should trigger only if Time Machine is ready
                if (Character.buttons.brokenTimeMachine.interactable)
                {
                    if (Character.machine.realBaseGold == 0.0)
                    {
                        _combManager.ManualZone(0, false, false, false, true, false);
                        return;
                    }
                    //Go to our gold loadout zone next to get a high gold drop
                    if (Settings.ManageGoldLoadouts && Settings.DoGoldSwap && Settings.GoldDropLoadout.Length > 0)
                    {
                        if (LoadoutManager.TryGoldDropSwap())
                        {
                            var bestZone = ZoneStatHelper.GetBestZone();
                            _furthestZone = ZoneHelpers.GetMaxReachableZone(false);

                            _combManager.ManualZone(bestZone.Zone, true, bestZone.FightType == 1, false, bestZone.FightType == 2, false);
                            return;
                        }
                    }
                }

                var questZone = _questManager.IsQuesting();
                if (!Settings.CombatEnabled || Settings.AdventureTargetITOPOD || !ZoneHelpers.ZoneIsTitan(Settings.SnipeZone) ||
                    ZoneHelpers.ZoneIsTitan(Settings.SnipeZone) &&
                    !ZoneHelpers.TitanSpawningSoon(Array.IndexOf(ZoneHelpers.TitanZones, Settings.SnipeZone)))
                {
                    if (questZone > 0)
                    {
                        if (Settings.QuestCombatMode == 0)
                        {
                            _combManager.ManualZone(questZone, false, false, false, Settings.QuestFastCombat, Settings.BeastMode);
                        }
                        else
                        {
                            _combManager.IdleZone(questZone, false, false);
                        }

                        return;
                    }
                }

                if (!Settings.CombatEnabled)
                    return;

                var tempZone = Settings.AdventureTargetITOPOD ? 1000 : Settings.SnipeZone;
                if (tempZone < 1000)
                {
                    if (!CombatManager.IsZoneUnlocked(Settings.SnipeZone))
                    {
                        tempZone = Settings.AllowZoneFallback ? ZoneHelpers.GetMaxReachableZone(false) : 1000;
                    }
                    else
                    {
                        if (ZoneHelpers.ZoneIsTitan(Settings.SnipeZone) && !ZoneHelpers.TitanSpawningSoon(Array.IndexOf(ZoneHelpers.TitanZones, Settings.SnipeZone)))
                        {
                            tempZone = 1000;
                        }
                    }
                }

                if (tempZone >= 1000)
                {
                    if (Settings.ITOPODCombatMode == 0)
                    {
                        _combManager.ManualZone(tempZone, false, Settings.ITOPODRecoverHP, Settings.ITOPODPrecastBuffs, Settings.ITOPODFastCombat, Settings.ITOPODBeastMode);
                    }
                    else
                    {
                        _combManager.IdleZone(tempZone, false, Settings.ITOPODRecoverHP);
                    }

                    return;
                }

                if (Settings.CombatMode == 0)
                {
                    _combManager.ManualZone(tempZone, Settings.SnipeBossOnly, Settings.RecoverHealth, Settings.PrecastBuffs, Settings.FastCombat, Settings.BeastMode);
                }
                else
                {
                    _combManager.IdleZone(tempZone, Settings.SnipeBossOnly, Settings.RecoverHealth);
                }
            }
            catch (Exception e)
            {
                LogError("SnipeZone", e);
            }
        }

        private void MoveToITOPOD()
        {
            if (!LegacyAutomationEnabled || !Settings.GlobalEnabled)
                return;

            if (_questManager.IsQuesting() >= 0)
                return;

            if (Settings.CombatEnabled)
                return;

            if (Settings.DoGoldSwap)
                return;

            //If we're not in ITOPOD, move there if its set
            if (Character.adventureController.zone >= 1000 || !Settings.AutoQuestITOPOD) return;
            Log($"Moving to ITOPOD to idle.");
            _combManager.MoveToZone(1000);
        }

        public void SetResnipe()
        {
            try
            {
                if (!LegacyAutomationEnabled)
                    return;

                if (Settings.ResnipeTime == 0 && !Settings.GoldCBlockMode) return;

                if (Settings.GoldCBlockMode)
                {
                    var furthest = ZoneHelpers.GetMaxReachableZone(false);
                    if (furthest > _furthestZone)
                    {
                        Settings.DoGoldSwap = true;
                        _furthestZone = furthest;
                    }

                    return;
                }

                if (Math.Abs(Character.rebirthTime.totalseconds - Settings.ResnipeTime) <= 1)
                {
                    Settings.DoGoldSwap = true;
                }
            }
            catch (Exception e)
            {
                LogError("SetResnipe", e);
            }
        }
    }
}
