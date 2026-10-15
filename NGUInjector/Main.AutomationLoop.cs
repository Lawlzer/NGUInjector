using System;
using System.Linq;
using System.Reflection;
using NGUInjector.AllocationProfiles.RebirthStuff;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal static bool LegacyAutomationEnabled => false;

        // Stuff on a very short timer
        void QuickStuff()
        {
            try
            {
                ObserveCustomRebirthEpoch();

                CustomPlan activeCustomPlan = null;
                if (Settings.UseCustomRebirthPlan && CustomPlanStore != null)
                {
                    activeCustomPlan = LoadRuntimeCustomPlan();
                }

                RunCustomBloodSpellToggles(activeCustomPlan);
                RunQuickCustomResourceRefresh(activeCustomPlan);
                WarnIfCustomAutomationHasIdleResources(activeCustomPlan);

                if (!LegacyAutomationEnabled || !Settings.GlobalEnabled)
                    return;

                //Turn on autoattack if we're in ITOPOD and its not on
                if (Settings.AutoQuestITOPOD && Character.adventureController.zone >= 1000 && !Character.adventure.autoattacking && !Settings.CombatEnabled)
                {
                    Character.adventureController.idleAttackMove.setToggle();
                }

                if (Settings.AutoFight)
                {
                    var needsAllocation = false;
                    var bc = Character.bossController;
                    if (!bc.isFighting && !bc.nukeBoss)
                    {
                        if (Character.bossID == 0)
                            needsAllocation = true;

                        if (bc.character.attack / 5.0 > bc.character.bossDefense && bc.character.defense / 5.0 > bc.character.bossAttack)
                            bc.startNuke();
                        else
                        {
                            if (bc.character.attack > (bc.character.bossDefense * 1.4) && bc.character.defense > bc.character.bossAttack * 1.4)
                            {
                                bc.beginFight();
                                bc.stopButton.gameObject.SetActive(true);
                            }
                        }
                    }

                    if (needsAllocation)
                    {
                        _profile.DoAllocations();
                    }
                }

                if (Settings.AutoMoneyPit)
                {
                    MoneyPitManager.CheckMoneyPit();
                }

                if (Settings.AutoSpin)
                {
                    MoneyPitManager.DoDailySpin();
                }

                if (Settings.AutoQuestITOPOD)
                {
                    MoveToITOPOD();
                }

                if (Settings.AutoSpellSwap)
                {
                    var spaghetti = (Character.bloodMagicController.lootBonus() - 1) * 100;
                    var counterfeit = ((Character.bloodMagicController.goldBonus() - 1)) * 100;
                    var number = Character.bloodMagic.rebirthPower;
                    Character.bloodMagic.rebirthAutoSpell = Settings.BloodNumberThreshold > 0 && number < Settings.BloodNumberThreshold;
                    Character.bloodMagic.goldAutoSpell = Settings.CounterfeitThreshold > 0 && counterfeit < Settings.CounterfeitThreshold;
                    Character.bloodMagic.lootAutoSpell = Settings.SpaghettiThreshold > 0 && spaghetti < Settings.SpaghettiThreshold;
                    Character.bloodSpells.updateGoldToggleState();
                    Character.bloodSpells.updateLootToggleState();
                    Character.bloodSpells.updateRebirthToggleState();
                }
            }
            catch (Exception e)
            {
                LogError("QuickStuff", e);
            }
        }

        // Runs every 10 seconds, our main loop
        void AutomationRoutine()
        {
            try
            {
                ObserveCustomRebirthEpoch();
                ProcessPendingManualRebirth();

                var customAutoBoost = Settings.CustomAutoBoostEnabled;
                CustomPlan activeCustomPlan = null;
                if (Settings.UseCustomRebirthPlan && CustomPlanStore != null)
                {
                    activeCustomPlan = LoadRuntimeCustomPlan();
                }

                if (CustomRebirthReady(activeCustomPlan))
                    CancelCustomAdventureGoldPush("Custom rebirth is ready");

                if (Settings.CustomAutoMoneyPitEnabled)
                {
                    var moneyPit = MoneyPitManager.CheckMoneyPit();
                    if (!string.IsNullOrWhiteSpace(moneyPit) && moneyPit.StartsWith("Money Pit Reward", StringComparison.Ordinal))
                        SetCustomAutomationStatus(moneyPit);
                }

                if (customAutoBoost && !Controller.midDrag)
                {
                    _invManager.ManageCustomAutoBoost();
                }

                RunCustomBloodSpellAutomation(activeCustomPlan);
                RunCustomPlanAutomation(activeCustomPlan);

                RunCustomFruitActivation(activeCustomPlan);
                RunCustomSustainedFocusAllocation(true);
                WarnIfCustomAutomationHasIdleResources(activeCustomPlan);
                if (RunCustomRebirthAutomation(activeCustomPlan))
                {
                    _timeLeft = 10f;
                    return;
                }

                if (!LegacyAutomationEnabled || !Settings.GlobalEnabled)
                {
                    _timeLeft = 10f;
                    return;
                }

                ZoneHelpers.OptimizeITOPOD();

                if ((Settings.ManageInventory || customAutoBoost) && !Controller.midDrag)
                {
                    var converted = Character.inventory.GetConvertedInventory().ToArray();
                    if (Settings.ManageInventory)
                    {
                        var boostSlots = _invManager.GetBoostSlots(converted);
                        _invManager.EnsureFiltered(converted);
                        _invManager.ManageConvertibles(converted);
                        _invManager.MergeEquipped(converted);
                        _invManager.MergeInventory(converted);
                        if (!customAutoBoost)
                            _invManager.MergeBoosts(converted);
                        _invManager.MergeGuffs(converted);
                        if (!customAutoBoost)
                        {
                            _invManager.BoostInventory(boostSlots);
                            _invManager.BoostInfinityCube();
                            _invManager.ManageBoostConversion(boostSlots);
                        }
                    }

                    if (customAutoBoost)
                    {
                        _invManager.ManageCustomAutoBoost();
                    }
                }

                if (Settings.SwapTitanLoadouts || Settings.ManageGoldLoadouts && Settings.NeedsGoldSwap())
                {
                    LoadoutManager.TryTitanSwap();
                    DiggerManager.TryTitanSwap();
                }

                if (Settings.ManageYggdrasil && Character.buttons.yggdrasil.interactable)
                {
                    _yggManager.ManageYggHarvest();
                    _yggManager.CheckFruits();
                }

                if (Settings.AutoBuyEM)
                {
                    //We haven't unlocked custom purchases yet
                    if (Character.highestBoss < 17) return;

                    var ePurchase = Character.energyPurchases;
                    var mPurchase = Character.magicPurchases;
                    var r3Purchase = Character.res3Purchases;

                    var energy = ePurchase.customAllCost() > 0;
                    var r3 = Character.res3.res3On && r3Purchase.customAllCost() > 0;
                    var magic = Character.highestBoss >= 37 && mPurchase.customAllCost() > 0;

                    long total = 0;

                    if (energy)
                    {
                        total += ePurchase.customAllCost();
                    }

                    if (magic)
                    {
                        total += mPurchase.customAllCost();
                    }

                    if (r3)
                    {
                        total += r3Purchase.customAllCost();
                    }

                    if (total > 0)
                    {
                        var numPurchases = Math.Floor((double)(Character.realExp / total));

                        if (numPurchases > 0)
                        {
                            var t = string.Empty;
                            if (energy)
                            {
                                t += "/exp";
                            }

                            if (magic)
                            {
                                t += "/magic";
                            }

                            if (r3)
                            {
                                t += "/res3";
                            }

                            t = t.Substring(1);

                            Log($"Buying {numPurchases} {t} purchases");
                            for (var i = 0; i < numPurchases; i++)
                            {
                                if (energy)
                                {
                                    var ePurchaseMethod = ePurchase.GetType().GetMethod("buyCustomAll",
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    ePurchaseMethod?.Invoke(ePurchase, null);
                                }

                                if (magic)
                                {
                                    var mPurchaseMethod = mPurchase.GetType().GetMethod("buyCustomAll",
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    mPurchaseMethod?.Invoke(mPurchase, null);
                                }

                                if (r3)
                                {
                                    var r3PurchaseMethod = r3Purchase.GetType().GetMethod("buyCustomAll",
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    r3PurchaseMethod?.Invoke(r3Purchase, null);
                                }
                            }
                        }
                    }
                }

                _profile.DoAllocations();
                RunCustomPlanAutomation(activeCustomPlan);
                RunCustomSustainedFocusAllocation(true);
                WarnIfCustomAutomationHasIdleResources(activeCustomPlan);

                _profile.CastBloodSpells();

                if (Settings.AutoQuest && Character.buttons.beast.interactable)
                {
                    var converted = Character.inventory.GetConvertedInventory().ToArray();
                    if (!Character.inventoryController.midDrag)
                        _invManager.ManageQuestItems(converted);
                    _questManager.CheckQuestTurnin();
                    _questManager.ManageQuests();
                }

                if (Settings.AutoRebirth)
                {
                    if (Settings.UseCustomRebirthPlan && CustomPlanStore != null)
                    {
                        if (RunCustomRebirthAutomation(activeCustomPlan))
                        {
                            _timeLeft = 10f;
                            return;
                        }
                    }
                    else
                    {
                        _profile.DoRebirth();
                    }
                }
            }
            catch (Exception e)
            {
                LogError("AutomationRoutine", e);
            }
            _timeLeft = 10f;
        }

        private bool RunCustomRebirthAutomation(CustomPlan activeCustomPlan)
        {
            if (!Settings.AutoRebirth || !Settings.UseCustomRebirthPlan || CustomPlanStore == null)
                return false;

            var plan = activeCustomPlan ?? CustomPlanStore.Load(Settings.CustomPlanFile);
            var rebirth = new CustomPlanRebirth(plan);
            if (rebirth.RebirthAvailable() && RunCustomPreRebirthPrepIfNeeded(plan))
                return true;

            var history = CaptureCustomRebirthHistory(DescribeCustomRebirthReason(plan));
            if (_profile.DoRebirth(rebirth))
            {
                ResetCustomPreRebirthPrep();
                ResetCustomAutomationRuntime();
                SaveCustomRebirthHistory(Settings.CustomPlanFile, history);
                return true;
            }

            return false;
        }
    }
}
