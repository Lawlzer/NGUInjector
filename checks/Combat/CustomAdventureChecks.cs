using System;
using System.Collections.Generic;
using System.Reflection;
using NGUInjector.Managers;
using Runtime = NGUInjector.Main;

namespace NGUInjector
{
    internal static partial class CombatChecks
    {
        private static readonly MethodInfo AdventureRoute = typeof(Runtime).GetMethod("TryRouteCustomAdventureGoal",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo AdventureCancel = typeof(Runtime).GetMethod("CancelCustomAdventureGoldPush",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo AdventureReset = typeof(Runtime).GetMethod("ResetCustomAdventureRuntime",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static void CheckCustomAdventure()
        {
            CheckGoldPushEligibility();
            CheckGoldPushSuccess();
            CheckGoldPushDeath();
            CheckGoldPushNewTarget();
            CheckGoldPushHealingAndRerolls();
            CheckGoldPushCancellation();
            CheckGoldPushGearBoundaries();
            CheckGoldPushInterruptions();
            CheckAdventureRuntimeReset();
            CheckHighBossNonTitanTargets();
        }

        private static Fight NewAdventure(int mode = 0, int boss = 101, double seconds = 1017)
        {
            var fight = new Fight(enemyType.boss);
            Runtime.Settings.CustomAdventureEnabled = true;
            Runtime.Settings.CustomAdventureGoldPush = true;
            Runtime.Settings.CustomAdventureMode = mode;
            Runtime.Settings.CustomAdventureZone = 2;
            fight.Character.bossID = boss;
            fight.Character.EffectiveBossId = boss;
            fight.Character.rebirthTime.totalseconds = seconds;
            fight.Character.buttons.brokenTimeMachine.interactable = true;
            fight.Character.adventure.zone = -1;
            fight.Character.adventure.autoattacking = true;
            fight.Controller.currentEnemy = null;
            // The game disables manual attack buttons while idle autoattack is on.
            foreach (var move in fight.Attacks) move.button.interactable = false;
            return fight;
        }

        private static void Route(Fight fight)
        {
            ExpectValue("custom adventure handles tick", true, (bool)AdventureRoute.Invoke(fight.RuntimeInstance, null));
        }

        private static T AdventureState<T>(Fight fight, string field)
        {
            return (T)typeof(Runtime).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(fight.RuntimeInstance);
        }

        private static void ExpectValue<T>(string name, T expected, T actual)
        {
            _assertions++;
            if (Equals(expected, actual)) return;
            _failures++;
            Console.Error.WriteLine($"FAIL: {name}; expected [{expected}], got [{actual}]");
        }

        private static void ExpectGoldState(Fight fight, string name, bool active, bool entered, int target,
            int attempted, LockType gearLock, string equipped)
        {
            ExpectValue(name + ": active", active, AdventureState<bool>(fight, "_customAdventureGoldPushActive"));
            ExpectValue(name + ": entered", entered, AdventureState<bool>(fight, "_customAdventureGoldPushEnteredZone"));
            ExpectValue(name + ": target", target, AdventureState<int>(fight, "_customAdventureGoldPushZone"));
            ExpectValue(name + ": attempted", attempted, AdventureState<int>(fight, "_customAdventureAttemptedGoldPushZone"));
            ExpectValue(name + ": lock", gearLock, LoadoutManager.CurrentLock);
            ExpectValue(name + ": equipped", equipped, LoadoutManager.Equipped);
        }

        private static int SelectedZone(int mode) => mode == 0 ? 1000 : mode == 1 ? 2 : 3;

        private static void ExpectSelectedGoal(Fight fight, int mode, string name)
        {
            ExpectValue(name + ": selected zone", SelectedZone(mode), fight.Character.adventure.zone);
            if (mode != 0) return;
            ExpectValue(name + ": optimal start floor", "5", fight.Controller.itopodStartInput.text);
            ExpectValue(name + ": optimal end floor", "5", fight.Controller.itopodEndInput.text);
        }

        private static void ExpectStart(Fight fight, int target, string name)
        {
            fight.Expect(name, "save:prior-gear", "acquire:Gold", "optimize:Gold:PowerTimesToughness:True", "zone:" + target);
            ExpectGoldState(fight, name, true, true, target, -1, LockType.Gold, "gold-gear");
            ExpectValue(name + ": actual entry", target, fight.Character.adventure.zone);
        }

        private static void CheckGoldPushEligibility()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var progress in new[] { (Boss: 89, Zone: 10), (Boss: 90, Zone: 12), (Boss: 101, Zone: 13) })
            foreach (var boundary in new[] { (Seconds: 1017d, Detour: true), (Seconds: 3599.99d, Detour: true), (Seconds: 3600d, Detour: false) })
            {
                var name = $"gold eligibility mode={mode}, boss={progress.Boss}, seconds={boundary.Seconds}";
                var fight = NewAdventure(mode, progress.Boss, boundary.Seconds);
                Route(fight);
                if (boundary.Detour)
                    ExpectStart(fight, progress.Zone, name);
                else
                {
                    fight.Expect(name + ": cutoff routes directly to farm", "zone:" + SelectedZone(mode));
                    ExpectGoldState(fight, name, false, false, -1, -1, LockType.None, "prior-gear");
                    ExpectSelectedGoal(fight, mode, name);
                }
            }

            var effective = NewAdventure(boss: 1001);
            effective.Character.EffectiveBossId = 89;
            Route(effective);
            ExpectStart(effective, 10, "raw boss does not impose cutoff or unlock beyond effective progress");

            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var gate in new[] { "disabled", "TM locked", "TM challenge" })
            {
                var fight = NewAdventure(mode);
                if (gate == "disabled") Runtime.Settings.CustomAdventureGoldPush = false;
                if (gate == "TM locked") fight.Character.buttons.brokenTimeMachine.interactable = false;
                if (gate == "TM challenge") fight.Character.challenges.timeMachineChallenge.inChallenge = true;
                Route(fight);
                fight.Expect(gate + ": no gold equip", "zone:" + SelectedZone(mode));
                ExpectGoldState(fight, gate, false, false, -1, -1, LockType.None, "prior-gear");
                ExpectSelectedGoal(fight, mode, gate);
            }
        }

        private static void CheckGoldPushSuccess()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                var fight = NewAdventure(mode);
                Route(fight);
                ExpectStart(fight, 13, "successful detour starts, mode=" + mode);
                fight.Calls.Clear();
                fight.Controller.currentEnemy = new Enemy { enemyType = enemyType.boss };
                Route(fight);
                fight.Expect("idle-only gold fight observes boss without manual attacks");
                ExpectValue("gold detour preserves idle", true, fight.Character.adventure.autoattacking);
                fight.Calls.Clear();
                fight.Controller.currentEnemy = null;
                Route(fight);
                fight.Expect("real CombatManager restores gear on enemy disappearance", "restore:prior-gear", "release:Gold", "zone:-1");
                ExpectValue("kill restores exact prior gear token", "prior-gear", LoadoutManager.Equipped);
                ExpectValue("kill releases Gold", LockType.None, LoadoutManager.CurrentLock);
                fight.Calls.Clear();
                Route(fight);
                fight.Expect("next route resumes selected farm, mode=" + mode, "zone:" + SelectedZone(mode));
                ExpectGoldState(fight, "successful attempt recorded", false, false, -1, 13, LockType.None, "prior-gear");
                ExpectSelectedGoal(fight, mode, "successful return");
                fight.Calls.Clear();
                Route(fight);
                Route(fight);
                fight.Expect("success never re-equips same target");
            }
        }

        private static void CheckGoldPushDeath()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var observedEnemy in new[] { false, true })
            foreach (var safeZone in new[] { false, true })
            {
                var name = $"death mode={mode}, observedEnemy={observedEnemy}, safeZone={safeZone}";
                var fight = NewAdventure(mode);
                Route(fight);
                if (observedEnemy)
                {
                    fight.Controller.currentEnemy = new Enemy { enemyType = enemyType.boss };
                    fight.Calls.Clear();
                    Route(fight);
                    fight.Expect(name + ": observed boss does not cast manual attacks");
                    ExpectValue(name + ": observation preserves idle", true, fight.Character.adventure.autoattacking);
                }
                fight.Calls.Clear();
                fight.Controller.currentEnemy = null;
                if (safeZone) fight.Character.adventure.zone = -1;
                else fight.Character.adventure.curHP = 0;
                Route(fight);
                fight.Expect(name + ": cleanup before farm", safeZone
                    ? new[] { "restore:prior-gear", "release:Gold", "settle", "zone:" + SelectedZone(mode) }
                    : new[] { "restore:prior-gear", "release:Gold", "settle", "zone:-1", "zone:" + SelectedZone(mode) });
                ExpectGoldState(fight, name, false, false, -1, 13, LockType.None, "prior-gear");
                ExpectSelectedGoal(fight, mode, name);
                fight.Calls.Clear();
                Route(fight);
                Route(fight);
                fight.Expect(name + ": failed target is not retried");
            }
        }

        private static void CheckGoldPushNewTarget()
        {
            var fight = NewAdventure(boss: 89);
            Route(fight);
            ExpectStart(fight, 10, "first unlocked target");
            fight.Character.adventure.curHP = 0;
            Route(fight);
            fight.Character.adventure.curHP = 100;
            fight.Calls.Clear();
            fight.Character.bossID = fight.Character.EffectiveBossId = 90;
            Route(fight);
            fight.Expect("new higher non-titan zone gets its own attempt", "save:prior-gear", "acquire:Gold",
                "optimize:Gold:PowerTimesToughness:True", "zone:-1", "zone:12");
            ExpectGoldState(fight, "higher target started", true, true, 12, 10, LockType.Gold, "gold-gear");
        }

        private static void CheckGoldPushHealingAndRerolls()
        {
            foreach (var hp in new[] { 0f, 25f })
            {
                var fight = NewAdventure();
                fight.Character.adventure.curHP = hp;
                Route(fight);
                fight.Expect("initial safe-zone recovery equips but does not enter, HP=" + hp,
                    "save:prior-gear", "acquire:Gold", "optimize:Gold:PowerTimesToughness:True");
                ExpectGoldState(fight, "healing before entry", true, false, 13, -1, LockType.Gold, "gold-gear");
                fight.Calls.Clear();
                Route(fight);
                Route(fight);
                fight.Expect("safe-zone healing is not a failed attempt");
                ExpectGoldState(fight, "still healing", true, false, 13, -1, LockType.Gold, "gold-gear");
                fight.Character.adventure.curHP = 100;
                Route(fight);
                fight.Expect("healed character enters existing detour without re-equipping", "zone:13");
                ExpectGoldState(fight, "healed entry recorded", true, true, 13, -1, LockType.Gold, "gold-gear");
            }

            var idle = NewAdventure();
            idle.Character.adventure.autoattacking = false;
            Route(idle);
            idle.Expect("first tick enables idle before entry", "save:prior-gear", "acquire:Gold",
                "optimize:Gold:PowerTimesToughness:True", "idle:on");
            ExpectGoldState(idle, "idle preparation not entry", true, false, 13, -1, LockType.Gold, "gold-gear");
            idle.Calls.Clear();
            Route(idle);
            idle.Expect("idle preparation resumes entry on next tick", "zone:13");

            var trash = NewAdventure();
            Route(trash);
            trash.Controller.currentEnemy = new Enemy { enemyType = enemyType.normal };
            for (var tick = 0; tick < 3; tick++)
            {
                trash.Calls.Clear();
                Route(trash);
                trash.Expect("boss-only trash reroll returns safe-to-target in same call", "zone:-1", "zone:13");
                ExpectGoldState(trash, "trash reroll is not death", true, true, 13, -1, LockType.Gold, "gold-gear");
                ExpectValue("trash reroll preserves idle", true, trash.Character.adventure.autoattacking);
            }
            trash.Calls.Clear();
            trash.Controller.currentEnemy = new Enemy { enemyType = enemyType.boss };
            Route(trash);
            trash.Expect("boss after trash is tracked without a manual attack");
            ExpectValue("boss after trash preserves idle", true, trash.Character.adventure.autoattacking);
            trash.Controller.currentEnemy = null;
            Route(trash);
            trash.Expect("idle-only boss after rerolls completes through enemy lifecycle",
                "restore:prior-gear", "release:Gold", "zone:-1");
        }

        private static void CheckGoldPushCancellation()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var entered in new[] { false, true })
            {
                var fight = NewAdventure(mode, seconds: 3599.99);
                if (!entered) fight.Character.adventure.curHP = 25;
                Route(fight);
                fight.Calls.Clear();
                fight.Character.rebirthTime.totalseconds = 3600;
                Route(fight);
                fight.Expect("active detour expires, mode=" + mode + ", entered=" + entered, entered
                    ? new[] { "restore:prior-gear", "release:Gold", "settle", "zone:-1", "zone:" + SelectedZone(mode) }
                    : new[] { "restore:prior-gear", "release:Gold", "settle", "zone:" + SelectedZone(mode) });
                ExpectValue("expiry clears entry flag", false, AdventureState<bool>(fight, "_customAdventureGoldPushEnteredZone"));
                ExpectValue("expiry clears active flag", false, AdventureState<bool>(fight, "_customAdventureGoldPushActive"));
                ExpectValue("expiry clears target", -1, AdventureState<int>(fight, "_customAdventureGoldPushZone"));
                ExpectValue("expiry restores prior gear", "prior-gear", LoadoutManager.Equipped);
                ExpectValue("expiry unlocks", LockType.None, LoadoutManager.CurrentLock);
                ExpectSelectedGoal(fight, mode, "expiry return");
                fight.Calls.Clear();
                Route(fight);
                fight.Expect("expired detour stays cancelled");
            }

            var blocked = NewAdventure(1);
            Route(blocked);
            blocked.Calls.Clear();
            blocked.RuntimeInstance.BlockedReason = "fixture priority goal";
            Route(blocked);
            blocked.Expect("priority boundary cancels entered detour before configured farm",
                "restore:prior-gear", "release:Gold", "settle", "zone:-1", "zone:2");
            ExpectValue("priority cancellation clears entry", false,
                AdventureState<bool>(blocked, "_customAdventureGoldPushEnteredZone"));
        }

        private static void CheckGoldPushGearBoundaries()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                var fight = NewAdventure(mode);
                GearOptimizer.FailEquip = true;
                Route(fight);
                fight.Expect("failed equip releases lock and resumes configured farm, mode=" + mode,
                    "save:prior-gear", "acquire:Gold", "optimize:Gold:PowerTimesToughness:True", "release:Gold",
                    "zone:" + SelectedZone(mode));
                ExpectGoldState(fight, "failed equip", false, false, -1, -1, LockType.None, "prior-gear");
                ExpectSelectedGoal(fight, mode, "failed equip fallback");
            }

            var busy = NewAdventure();
            LoadoutManager.CurrentLock = LockType.MoneyPit;
            Route(busy);
            busy.Expect("busy loadout lock waits without snapshot, equip, restore, or movement");
            ExpectGoldState(busy, "busy lock preserved", false, false, -1, -1, LockType.MoneyPit, "prior-gear");
            LoadoutManager.CurrentLock = LockType.None;
            Route(busy);
            ExpectStart(busy, 13, "detour starts after other lock releases");

            var lost = NewAdventure(1);
            Route(lost);
            // Another owner has already restored/released Gold before the route observes it.
            LoadoutManager.RestoreGear();
            LoadoutManager.ReleaseLock();
            LoadoutManager.CurrentLock = LockType.MoneyPit;
            lost.Calls.Clear();
            Route(lost);
            lost.Expect("lost Gold completes attempt without touching another owner's lock", "zone:-1", "zone:2");
            ExpectGoldState(lost, "lost Gold recorded", false, false, -1, 13, LockType.MoneyPit, "prior-gear");
            LoadoutManager.CurrentLock = LockType.None;
            lost.Calls.Clear();
            Route(lost);
            lost.Expect("lost Gold does not retry same target after lock becomes free");
        }

        private static void CheckGoldPushInterruptions()
        {
            foreach (var interruption in new[] { "priority", "titan" })
            foreach (var phase in new[] { "entered", "after kill", "after death", "before entry", "lost before entry" })
            {
                var name = $"{interruption} interruption {phase}";
                var fight = NewAdventure(1);
                if (phase == "before entry" || phase == "lost before entry") fight.Character.adventure.curHP = 25;
                Route(fight);
                if (phase == "after kill")
                {
                    fight.Controller.currentEnemy = new Enemy { enemyType = enemyType.boss };
                    fight.Calls.Clear();
                    Route(fight);
                    fight.Expect(name + ": idle-only enemy observation");
                    fight.Controller.currentEnemy = null;
                    Route(fight);
                    fight.Expect(name + ": CombatManager restores before route bookkeeping",
                        "restore:prior-gear", "release:Gold", "zone:-1");
                    ExpectGoldState(fight, name + ": completion not yet recorded", true, true, 13, -1, LockType.None, "prior-gear");
                }
                if (phase == "after death")
                {
                    fight.Character.adventure.zone = -1;
                    fight.Character.adventure.curHP = 0;
                    ExpectGoldState(fight, name + ": failure not yet recorded", true, true, 13, -1, LockType.Gold, "gold-gear");
                }
                if (phase == "lost before entry")
                {
                    LoadoutManager.RestoreGear();
                    LoadoutManager.ReleaseLock();
                    LoadoutManager.CurrentLock = LockType.MoneyPit;
                }

                fight.Calls.Clear();
                var expected = new List<string>();
                if (phase != "after kill" && phase != "lost before entry")
                    expected.AddRange(new[] { "restore:prior-gear", "release:Gold", "settle" });
                if (interruption == "priority")
                {
                    fight.RuntimeInstance.BlockedReason = "fixture priority interruption";
                    Route(fight);
                    if (phase == "entered") expected.Add("zone:-1");
                    expected.Add("zone:2");
                }
                else
                {
                    // Titan routing is a boundary stub; execute its real adventure-cancel handoff.
                    AdventureCancel.Invoke(fight.RuntimeInstance, new object[] { "fixture titan interruption" });
                }
                fight.Expect(name + ": only owned Gold is restored/released", expected.ToArray());
                var attempted = phase == "before entry" ? -1 : 13;
                ExpectGoldState(fight, name, false, false, -1, attempted,
                    phase == "lost before entry" ? LockType.MoneyPit : LockType.None, "prior-gear");

                fight.RuntimeInstance.BlockedReason = null;
                fight.Character.adventure.curHP = 100;
                if (phase == "lost before entry") LoadoutManager.CurrentLock = LockType.None;
                fight.Calls.Clear();
                Route(fight);
                if (phase == "before entry")
                {
                    fight.Expect(name + ": retry permitted without a prior entry", interruption == "priority"
                        ? new[] { "save:prior-gear", "acquire:Gold", "optimize:Gold:PowerTimesToughness:True", "zone:-1", "zone:13" }
                        : new[] { "save:prior-gear", "acquire:Gold", "optimize:Gold:PowerTimesToughness:True", "zone:13" });
                    ExpectGoldState(fight, name + ": retried", true, true, 13, -1, LockType.Gold, "gold-gear");
                }
                else
                {
                    fight.Expect(name + ": same-zone detour not retried", interruption == "priority"
                        ? Array.Empty<string>()
                        : phase == "entered" ? new[] { "zone:-1", "zone:2" } : new[] { "zone:2" });
                    ExpectSelectedGoal(fight, 1, name + ": selected farm resumes");
                    ExpectGoldState(fight, name + ": attempt retained", false, false, -1, 13, LockType.None, "prior-gear");
                    fight.Calls.Clear();
                    Route(fight);
                    fight.Expect(name + ": repeated tick does not retry");
                }
            }
        }

        private static void CheckAdventureRuntimeReset()
        {
            foreach (var mode in new[] { 0, 1, 2 })
            foreach (var phase in new[] { "entered", "healing", "post-kill", "completed", "failed" })
            {
                var name = $"rebirth reset mode={mode}, phase={phase}";
                var fight = NewAdventure(mode);
                if (phase == "healing") fight.Character.adventure.curHP = 25;
                Route(fight);
                if (phase == "post-kill" || phase == "completed")
                {
                    fight.Controller.currentEnemy = new Enemy { enemyType = enemyType.boss };
                    fight.Calls.Clear();
                    Route(fight);
                    fight.Expect(name + ": idle-only enemy observation");
                    fight.Controller.currentEnemy = null;
                    Route(fight);
                    if (phase == "completed") Route(fight);
                }
                if (phase == "failed")
                {
                    fight.Character.adventure.curHP = 0;
                    Route(fight);
                }
                ExpectGoldState(fight, name + ": pre-reset state", phase != "completed" && phase != "failed",
                    phase == "entered" || phase == "post-kill", phase == "completed" || phase == "failed" ? -1 : 13,
                    phase == "completed" || phase == "failed" ? 13 : -1,
                    phase == "entered" || phase == "healing" ? LockType.Gold : LockType.None,
                    phase == "entered" || phase == "healing" ? "gold-gear" : "prior-gear");

                fight.Calls.Clear();
                AdventureReset.Invoke(fight.RuntimeInstance, null);
                fight.Expect(name + ": active Gold cleanup only", phase == "entered" || phase == "healing"
                    ? new[] { "restore:prior-gear", "release:Gold", "settle" } : Array.Empty<string>());
                ExpectGoldState(fight, name + ": all adventure state cleared", false, false, -1, -1, LockType.None, "prior-gear");
                fight.Calls.Clear();
                AdventureReset.Invoke(fight.RuntimeInstance, null);
                fight.Expect(name + ": repeated reset is side-effect free");

                fight.Character.rebirthTime.totalseconds = 0;
                fight.Character.adventure.curHP = 100;
                fight.Character.adventure.zone = -1;
                fight.Controller.currentEnemy = null;
                Route(fight);
                ExpectStart(fight, 13, name + ": same target can be attempted in new rebirth");
            }
        }

        private static void CheckHighBossNonTitanTargets()
        {
            ExpectValue("Tippi classified as titan", true, ZoneHelpers.ZoneIsTitan(44));
            ExpectValue("Traitor classified as titan", true, ZoneHelpers.ZoneIsTitan(45));
            ExpectValue("zone 43 remains a normal farm target", false, ZoneHelpers.ZoneIsTitan(43));
            ExpectValue("supported autokill roster remains twelve targets", 12, ZoneHelpers.TitanZones.Length);
            ExpectValue("Tippi not added to unsupported autokill roster", -1, Array.IndexOf(ZoneHelpers.TitanZones, 44));
            ExpectValue("Traitor not added to unsupported autokill roster", -1, Array.IndexOf(ZoneHelpers.TitanZones, 45));
            foreach (var progress in new[] { (Boss: 870, Normal: 41, IncludingTitans: 42), (Boss: 871, Normal: 43, IncludingTitans: 43),
                (Boss: 897, Normal: 43, IncludingTitans: 44), (Boss: 902, Normal: 43, IncludingTitans: 45) })
            {
                var name = "high effective boss " + progress.Boss;
                var fight = NewAdventure(boss: progress.Boss);
                ExpectValue(name + ": highest unlocked including titans", progress.IncludingTitans, ZoneHelpers.GetMaxReachableZone(true));
                ExpectValue(name + ": highest unlocked non-titan", progress.Normal, ZoneHelpers.GetMaxReachableZone(false));
                Route(fight);
                ExpectStart(fight, progress.Normal, name + ": real route excludes late titans");
            }
        }
    }
}
