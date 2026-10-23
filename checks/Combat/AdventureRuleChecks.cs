using System;
using System.Reflection;
using SimpleJSON;
using Runtime = NGUInjector.Main;

namespace NGUInjector
{
    internal static partial class CombatChecks
    {
        private static void ExpectValue<T>(string name, T expected, T actual)
        {
            _assertions++;
            if (Equals(expected, actual)) return;
            _failures++;
            Console.Error.WriteLine("FAIL: " + name + "; expected " + expected + ", got " + actual);
        }
        private static object RuleRead(Fight fight, string name, params object[] args) => typeof(Runtime)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).Invoke(fight.RuntimeInstance, args);
        private static void CheckAdventureRules()
        {
            foreach (var missing in new[] { "power", "toughness", "regen", "none" })
            {
                var fight = new Fight();
                Runtime.Settings.CustomAdventureRules = new[] { new AdventureRule { Enabled = true, Mode = 1, Zone = 4,
                    MinPower = 100, MinToughness = 200, MinRegen = 300 } };
                fight.Character.AdventureAttack = missing == "power" ? 99 : 100;
                fight.Character.AdventureDefense = missing == "toughness" ? 199 : 200;
                fight.Character.AdventureRegen = missing == "regen" ? 299 : 300;
                var state = (JSONObject)RuleRead(fight, "GetAdventureRulesRuntime");
                ExpectValue("inclusive AND thresholds " + missing, missing == "none" ? 0 : -1, state["matchedRuleIndex"].AsInt);
                ExpectValue("native power read", (double)fight.Character.AdventureAttack, state["power"].AsDouble);
                ExpectValue("native toughness read", (double)fight.Character.AdventureDefense, state["toughness"].AsDouble);
                ExpectValue("native regen read", (double)fight.Character.AdventureRegen, state["regen"].AsDouble);
            }
            foreach (var mode in new[] { 0, 1, 2, 4 })
            {
                var fight = new Fight();
                fight.Character.EffectiveBossId = 101;
                Runtime.Settings.CustomAdventureMode = 1; Runtime.Settings.CustomAdventureZone = 2;
                Runtime.Settings.CustomAdventureRules = new[] {
                    new AdventureRule { Enabled = false, Mode = 1, Zone = 5 },
                    new AdventureRule { Enabled = true, Mode = 4, Zone = 43 },
                    new AdventureRule { Enabled = true, Mode = mode, Zone = 4 },
                    new AdventureRule { Enabled = true, Mode = 1, Zone = 3 } };
                var args = new object[] { 0, 0, 0 };
                RuleRead(fight, "ResolveConfiguredAdventureGoal", args);
                ExpectValue("first enabled unlocked match", 2, (int)args[2]);
                ExpectValue("rule mode read", mode, (int)args[0]);
                ExpectValue("rule zone read", 4, (int)args[1]);
                ExpectValue("fallback mode preserved", 1, Runtime.Settings.CustomAdventureMode);
                ExpectValue("fallback zone preserved", 2, Runtime.Settings.CustomAdventureZone);
                Runtime.Settings.CustomAdventureMode = 3;
                RuleRead(fight, "ResolveConfiguredAdventureGoal", args);
                ExpectValue("bounded tower ignores rule override", 3, (int)args[0]);
                fight.Character.AdventureAttack = float.NaN;
                ExpectValue("nonfinite native telemetry fails closed", "{}", RuleRead(fight, "GetAdventureRulesRuntime").ToString());
            }
        }
    }
}
