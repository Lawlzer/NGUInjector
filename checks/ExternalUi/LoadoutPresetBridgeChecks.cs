using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static JSONObject LoadoutPresetBridgeArgs(bool apply = false)
        {
            var args = JsonFiles.ParseObject("{\"preset\":{\"Id\":\"bridge-preset\",\"Name\":\"Bridge preset\","
                + "\"Weights\":[1,0.25,0,0,0,0],\"SupportMode\":\"planned\",\"future\":9223372036854775807}}");
            if (apply)
            {
                args["previewId"] = NGUInjector.Main.LoadoutPresetPreviewToken;
                args["candidateKey"] = "fixture-candidate";
            }
            return args;
        }

        private static int[] LoadoutPresetBridgeEffects(NGUInjector.Main main)
        {
            return new[] {
                NGUInjector.Main.Equips, NGUInjector.Main.Saves, NGUInjector.Main.Allocations,
                NGUInjector.Main.Focuses, NGUInjector.Main.Invalidations, NGUInjector.Main.Previews,
                NGUInjector.Main.ZoneReloads, NGUInjector.Main.StageOverrides,
                Managers.AugmentOptimizer.Previews, Managers.AugmentOptimizer.Allocations,
                main.PushStartCalls, main.PushCancelCalls, NGUInjector.Main.settingsForm?.SettingsUpdates ?? 0
            };
        }

        private static void RejectLoadoutPresetBridgeCommand(NGUInjector.Main main, string command, string rawArgs)
        {
            var id = "preset-invalid-" + _assertions;
            var previews = main.LoadoutPresetPreviews;
            var applies = main.LoadoutPresetApplies;
            var effects = LoadoutPresetBridgeEffects(main);
            main.Process(id, "{\"id\":\"" + id + "\",\"command\":\"" + command + "\""
                + (rawArgs == null ? "" : ",\"args\":" + rawArgs) + "}");
            var result = Result(id);
            Assert(!result["success"].AsBool && !string.IsNullOrEmpty(result["message"].Value),
                command + " rejects malformed arguments: " + (rawArgs ?? "missing"));
            Assert(main.LoadoutPresetPreviews == previews && main.LoadoutPresetApplies == applies
                && effects.SequenceEqual(LoadoutPresetBridgeEffects(main)),
                "rejected preset command reaches no successful action boundary");
        }

        private static void CheckLoadoutPresetBridge(NGUInjector.Main main)
        {
            // Real dispatch/publication with a substituted game adapter, not proof of engine safety or forecasts.
            var before = _assertions;
            var settingsPath = Path.Combine(_dir, "settings.json");
            var settingsBytes = File.ReadAllBytes(settingsPath);
            var settingsJson = UnityEngine.JsonUtility.ToJson(NGUInjector.Main.Settings);
            var originalForm = NGUInjector.Main.settingsForm;
            var originalRuntime = main.LoadoutPresetRuntime;
            NGUInjector.Main.settingsForm = new FakeForm();
            var effects = LoadoutPresetBridgeEffects(main);
            var previews = main.LoadoutPresetPreviews;
            var applies = main.LoadoutPresetApplies;
            try
            {
                main.SetReady(false);
                foreach (var command in new[] { "previewLoadoutPreset", "applyLoadoutPreset" })
                {
                    var id = "preset-not-ready-" + command;
                    main.Process(id, Envelope(id, command, LoadoutPresetBridgeArgs(command == "applyLoadoutPreset")));
                    Assert(!Result(id)["success"].AsBool && Result(id)["message"].Value.Contains("not ready"),
                        "preset dispatch is readiness-gated: " + command);
                }
                Assert(main.LoadoutPresetPreviews == previews && main.LoadoutPresetApplies == applies,
                    "not-ready preset commands do not reach either adapter boundary");
                main.SetReady(true);

                Assert(main.BuildRuntime()["loadoutPreset"].IsObject && main.BuildRuntime()["loadoutPreset"].Count == 0,
                    "null preset runtime boundary becomes an empty object");
                var previewArgs = LoadoutPresetBridgeArgs();
                var previewText = previewArgs.ToString();
                using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    main.Process("preset-preview", Envelope("preset-preview", "previewLoadoutPreset", previewArgs));
                var preview = Result("preset-preview");
                Assert(preview["success"].AsBool && preview["command"].Value == "previewLoadoutPreset"
                    && main.LoadoutPresetPreviews == previews + 1 && main.LoadoutPresetApplies == applies,
                    "real preview branch invokes only the read adapter boundary exactly once");
                Assert(main.LoadoutPresetPreviewArgs.ToString() == previewText && previewArgs.ToString() == previewText,
                    "preview forwards the complete ratio-free preset unchanged");
                Assert(preview["data"]["result"]["version"].AsInt == 1
                    && preview["data"]["result"]["presetId"].Value == "bridge-preset"
                    && preview["data"]["result"]["previewId"].Value == NGUInjector.Main.LoadoutPresetPreviewToken,
                    "preview result survives the additive runtime envelope");
                Assert(effects.SequenceEqual(LoadoutPresetBridgeEffects(main))
                    && settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath))
                    && UnityEngine.JsonUtility.ToJson(NGUInjector.Main.Settings) == settingsJson,
                    "preview dispatch causes no equipment, save, allocation, focus, settings update or persistence effects");
                var compact = preview["data"]["runtime"];
                Assert(compact["loadoutPresetsVersion"].IsNumber && compact["loadoutPresetsVersion"].AsInt == 2
                    && compact["globalNguWeightsVersion"].AsInt == 1
                    && compact["loadoutPreset"].IsObject && !compact.Linq.Any(item => item.Key == "supportedCommands"),
                    "compact preview acknowledgement advertises preset capability and empty state");
                main.Process("preset-preview", Envelope("preset-preview", "previewLoadoutPreset", previewArgs));
                Assert(main.LoadoutPresetPreviews == previews + 1 && main.LoadoutPresetApplies == applies,
                    "claimed preset preview is not replayed or implicitly applied");

                foreach (var command in new[] { "previewLoadoutPreset", "applyLoadoutPreset" })
                {
                    var apply = command == "applyLoadoutPreset";
                    foreach (var raw in new[] { null, "null", "{}", "[]", "true", "1", "\"bad\"" })
                        RejectLoadoutPresetBridgeCommand(main, command, raw);
                    foreach (var field in new[] { "preset" })
                    {
                        var missing = LoadoutPresetBridgeArgs(apply); missing.Remove(field);
                        RejectLoadoutPresetBridgeCommand(main, command, missing.ToString());
                        foreach (var value in new[] { "null", "true", "1", "\"bad\"", "{}", "[]" })
                        {
                            var args = LoadoutPresetBridgeArgs(apply);
                            args[field] = JsonFiles.ParseObject("{\"value\":" + value + "}")["value"];
                            RejectLoadoutPresetBridgeCommand(main, command, args.ToString());
                        }
                    }
                    foreach (var field in new[] { "energyNguWeights", "magicNguWeights" })
                    {
                        foreach (var value in new[] { "null", "true", "\"1\"", "[]", "[1,0,0,0,0,0,0,0,0]", "[1,0,0,0,0,0,0]" })
                        {
                            var args = LoadoutPresetBridgeArgs(apply);
                            args[field] = JsonFiles.ParseObject("{\"value\":" + value + "}")["value"];
                            RejectLoadoutPresetBridgeCommand(main, command, args.ToString());
                        }
                    }
                    foreach (var patch in new[] { "{\"Weights\":[0,0,0,0,0,0]}", "{\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0]}", "{\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0]}", "{\"Name\":null}" })
                    {
                        var args = LoadoutPresetBridgeArgs(apply);
                        foreach (var item in JsonFiles.ParseObject(patch).Linq) args["preset"][item.Key] = item.Value;
                        RejectLoadoutPresetBridgeCommand(main, command, args.ToString());
                    }
                    foreach (var index in Enumerable.Range(10, PresetDefinitions.ObjectiveKeys.Length - 10))
                    foreach (var invalid in new[] {"-1", "1000001", "null", "true", "\"1\"", "[]", "{}", "1e100"})
                    {
                        var args = LoadoutPresetBridgeArgs(apply);
                        args["preset"]["Weights"] = JsonFiles.ParseObject("{\"value\":[1,0,0,0,0,0,0,0,0,0,0,0,0,0]}")["value"];
                        args["preset"]["Weights"][index] = JsonFiles.ParseObject("{\"value\":" + invalid + "}")["value"];
                        RejectLoadoutPresetBridgeCommand(main, command, args.ToString());
                    }
                    var unexpected = LoadoutPresetBridgeArgs(apply); unexpected["apply"] = true;
                    RejectLoadoutPresetBridgeCommand(main, command, unexpected.ToString());
                }
                foreach (var field in new[] { "previewId", "candidateKey" })
                {
                    var previewWithApplyArgs = LoadoutPresetBridgeArgs(); previewWithApplyArgs[field] = "not-an-apply-command";
                    RejectLoadoutPresetBridgeCommand(main, "previewLoadoutPreset", previewWithApplyArgs.ToString());
                    var missing = LoadoutPresetBridgeArgs(true); missing.Remove(field);
                    RejectLoadoutPresetBridgeCommand(main, "applyLoadoutPreset", missing.ToString());
                    foreach (var value in new[] { "null", "1", "true", "[]", "{}" })
                    {
                        var args = LoadoutPresetBridgeArgs(true);
                        args[field] = JsonFiles.ParseObject("{\"value\":" + value + "}")["value"];
                        RejectLoadoutPresetBridgeCommand(main, "applyLoadoutPreset", args.ToString());
                    }
                }
                foreach (var token in new[] { "", new string('a', 31), new string('a', 33), new string('A', 32), new string('g', 32), new string('a', 31) + "\n" })
                {
                    var args = LoadoutPresetBridgeArgs(true); args["previewId"] = token;
                    RejectLoadoutPresetBridgeCommand(main, "applyLoadoutPreset", args.ToString());
                }
                var longKey = LoadoutPresetBridgeArgs(true); longKey["candidateKey"] = new string('x', 65);
                RejectLoadoutPresetBridgeCommand(main, "applyLoadoutPreset", longKey.ToString());

                var applyArgs = LoadoutPresetBridgeArgs(true);
                // Deliberately not the fixture preview token: the bridge must forward, not synthesize/cache it.
                applyArgs["previewId"] = "fedcba9876543210fedcba9876543210";
                var applyText = applyArgs.ToString();
                main.Process("preset-apply", Envelope("preset-apply", "applyLoadoutPreset", applyArgs));
                var applied = Result("preset-apply");
                Assert(applied["success"].AsBool && applied["message"].Value == "loadout preset apply boundary"
                    && main.LoadoutPresetApplies == applies + 1 && main.LoadoutPresetPreviews == previews + 1,
                    "only explicit apply command invokes apply boundary, without re-previewing; actual: " + applied.ToString());
                Assert(main.LoadoutPresetApplyArgs.ToString() == applyText
                    && applied["data"]["result"]["previewId"].Value == applyArgs["previewId"].Value
                    && applied["data"]["result"]["candidateKey"].Value == applyArgs["candidateKey"].Value
                    && applied["data"]["result"]["presetId"].Value == "bridge-preset",
                    "apply forwards supplied preview identity and candidate unchanged and preserves boundary result");
                Assert(applied["data"]["runtime"]["loadoutPresetsVersion"].AsInt == 2,
                    "compact apply acknowledgement retains preset capability");
                main.Process("preset-apply", Envelope("preset-apply", "applyLoadoutPreset", applyArgs));
                Assert(main.LoadoutPresetApplies == applies + 1, "claimed preset apply is never replayed");

                main.LoadoutPresetRuntime = JsonFiles.ParseObject("{\"id\":\"independent-snapshot\",\"status\":\"applied\",\"active\":false,\"requiresReapply\":true}");
                Assert(main.BuildRuntime()["loadoutPreset"].ToString() == main.LoadoutPresetRuntime.ToString(),
                    "runtime builder forwards independently supplied boundary state");
                main.PumpRuntime();
                var full = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(full["loadoutPresetsVersion"].IsNumber && full["loadoutPresetsVersion"].AsInt == 2
                    && full["loadoutPreset"].ToString() == main.LoadoutPresetRuntime.ToString(),
                    "full heartbeat advertises preset capability and forwards actual snapshot");
                var commands = full["supportedCommands"].Children.Select(item => item.Value).ToArray();
                Assert(commands.Count(item => item == "previewLoadoutPreset") == 1 && commands.Count(item => item == "applyLoadoutPreset") == 1,
                    "full heartbeat registers both preset command names once");
                main.Process("preset-status", Envelope("preset-status", "status"));
                var status = Result("preset-status")["data"]["runtime"];
                var published = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(status["loadoutPresetsVersion"].AsInt == 2 && status["loadoutPreset"].ToString() == main.LoadoutPresetRuntime.ToString()
                    && published["loadoutPreset"].ToString() == status["loadoutPreset"].ToString()
                    && published["runtimeRevision"].AsDouble == status["runtimeRevision"].AsDouble,
                    "compact status and full publication share actual preset snapshot and revision");
                Assert(main.LoadoutPresetPreviews == previews + 1 && main.LoadoutPresetApplies == applies + 1
                    && effects.SequenceEqual(LoadoutPresetBridgeEffects(main)),
                    "status, heartbeat and malformed commands invoke no extra preset or unrelated action boundaries");
                foreach (var command in new[] {"previewLoadoutPreset", "applyLoadoutPreset"})
                foreach (var patch in new[] {"{}", "{\"Weights\":[0,0,0,0,0,0,1,2,3,4]}",
                    "{\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0,2,5]}",
                    "{\"HorizonSeconds\":3600}", "{\"HorizonSeconds\":null}", "{\"HorizonSeconds\":true}", "{\"HorizonSeconds\":\"invalid\"}",
                    "{\"HorizonSeconds\":[]}", "{\"HorizonSeconds\":{}}", "{\"HorizonSeconds\":1.5}", "{\"HorizonSeconds\":0}", "{\"HorizonSeconds\":604801}"})
                {
                    var apply = command == "applyLoadoutPreset";
                    var args = LoadoutPresetBridgeArgs(apply);
                    args["preset"]["Weights"] = JsonFiles.ParseObject("{\"value\":[0,0,0,0,0,0,0,0,0,0,0.25,1000000]}")["value"];
                    foreach (var item in JsonFiles.ParseObject(patch).Linq) args["preset"][item.Key] = item.Value;
                    var text = args.ToString();
                    var previewCount = main.LoadoutPresetPreviews; var applyCount = main.LoadoutPresetApplies;
                    var id = "preset-contract-" + _assertions;
                    main.Process(id, Envelope(id, command, args));
                    Assert(Result(id)["success"].AsBool && main.LoadoutPresetPreviews == previewCount + (apply ? 0 : 1)
                        && main.LoadoutPresetApplies == applyCount + (apply ? 1 : 0),
                        command + " accepts legacy ten/twelve and current cap objectives with missing or ignored legacy horizon: " + patch);
                    Assert((apply ? main.LoadoutPresetApplyArgs : main.LoadoutPresetPreviewArgs).ToString() == text && args.ToString() == text,
                        "bridge forwards accepted preset source unchanged: " + patch);
                }
                Assert(effects.SequenceEqual(LoadoutPresetBridgeEffects(main)), "expanded preset contract checks cause no unrelated action effects");
                Assert(settingsBytes.SequenceEqual(File.ReadAllBytes(settingsPath))
                    && UnityEngine.JsonUtility.ToJson(NGUInjector.Main.Settings) == settingsJson,
                    "bridge-only preset checks preserve live and persisted settings");
            }
            finally
            {
                main.LoadoutPresetRuntime = originalRuntime;
                NGUInjector.Main.settingsForm = originalForm;
                main.SetReady(true);
            }
            Console.WriteLine("Loadout preset bridge checks passed: " + (_assertions - before) + " assertions (adapter boundary only)");
        }
    }
}
