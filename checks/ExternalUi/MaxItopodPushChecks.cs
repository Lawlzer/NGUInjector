using System.IO;
using SimpleJSON;

namespace NGUInjector
{
    internal static partial class ExternalUiChecks
    {
        private static string PushEnvelope(string id, string command, string rawArgs) => rawArgs == null
            ? Envelope(id, command)
            : "{\"id\":\"" + id + "\",\"command\":\"" + command + "\",\"args\":" + rawArgs + "}";

        private static void CheckMaxItopodPushCommands(NGUInjector.Main main)
        {
            var before = _assertions;
            var settingsPath = Path.Combine(_dir, "settings.json");
            var settingsBytes = File.ReadAllText(settingsPath);
            Assert(main.BuildRuntime()["maxItopodPush"].ToString() == "{\"active\":false,\"remainingDeaths\":0,\"floor\":0}",
                "runtime builder includes typed idle push state");

            foreach (var args in new[] {
                null, "null", "{}", "[]", "true", "1", "\"bad\"", "{\"death\":3}",
                "{\"deaths\":1,\"extra\":false}", "{\"deaths\":null}", "{\"deaths\":true}",
                "{\"deaths\":\"3\"}", "{\"deaths\":[]}", "{\"deaths\":{}}", "{\"deaths\":0}",
                "{\"deaths\":-1}", "{\"deaths\":1.5}", "{\"deaths\":1000.5}", "{\"deaths\":1001}",
                "{\"deaths\":2147483648}", "{\"deaths\":1e100}" })
            {
                var id = "push-invalid-" + _assertions;
                var starts = main.PushStartCalls;
                var cancels = main.PushCancelCalls;
                main.Process(id, PushEnvelope(id, "startMaxItopodPush", args));
                Assert(!Result(id)["success"].AsBool && main.PushStartCalls == starts && main.PushCancelCalls == cancels,
                    "start rejects malformed/range/type/extra arguments before action: " + (args ?? "missing"));
                Assert(!Result(id)["data"]["runtime"]["maxItopodPush"]["active"].AsBool,
                    "invalid start acknowledgement retains inactive push");
            }

            foreach (var token in new[] { "1", "3", "1000", "1.0", "1e3" })
            {
                var id = "push-valid-" + _assertions;
                var starts = main.PushStartCalls;
                var revision = main.RuntimeRevision;
                var args = JsonFiles.ParseObject("{\"deaths\":" + token + "}");
                main.Process(id, Envelope(id, "startMaxItopodPush", args));
                var result = Result(id);
                Assert(result["success"].AsBool && result["message"].Value == "fixture push started"
                    && main.PushStartCalls == starts + 1 && main.LastPushDeaths == args["deaths"].AsInt,
                    "valid integer limit dispatched exactly once, token=" + token);
                var ack = result["data"]["runtime"];
                Assert(ack["maxItopodPush"].ToString() == main.PushRuntime.ToString()
                    && ack["maxItopodPush"]["remainingDeaths"].IsNumber && ack["maxItopodPush"]["floor"].AsInt == 87,
                    "start acknowledgement includes actual post-action boundary runtime");
                var published = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
                Assert(ack["runtimeRevision"].AsDouble > revision && published["runtimeRevision"].AsDouble == ack["runtimeRevision"].AsDouble
                    && published["maxItopodPush"].ToString() == ack["maxItopodPush"].ToString(),
                    "start publishes matching fresh full runtime and compact acknowledgement");
                main.Process(id, Envelope(id, "startMaxItopodPush", args));
                Assert(main.PushStartCalls == starts + 1, "claimed push start is never replayed");
                var cancels = main.PushCancelCalls;
                main.Process(id + "-cancel", Envelope(id + "-cancel", "cancelMaxItopodPush", new JSONObject()));
                Assert(Result(id + "-cancel")["success"].AsBool && Result(id + "-cancel")["message"].Value == "fixture push cancelled"
                    && main.PushCancelCalls == cancels + 1, "empty object cancel dispatches exactly once");
                Assert(Result(id + "-cancel")["data"]["runtime"]["maxItopodPush"].ToString() == "{\"active\":false,\"remainingDeaths\":0,\"floor\":0}",
                    "cancel acknowledgement immediately clears push runtime");
            }

            main.Process("push-cancel-validation-start", Envelope("push-cancel-validation-start", "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":2}")));
            foreach (var args in new[] { "{\"deaths\":2}", "{\"extra\":null}", "[]", "\"bad\"", "false", "1" })
            {
                var id = "push-cancel-invalid-" + _assertions;
                var cancels = main.PushCancelCalls;
                main.Process(id, PushEnvelope(id, "cancelMaxItopodPush", args));
                Assert(!Result(id)["success"].AsBool && main.PushCancelCalls == cancels && main.PushRuntime["remainingDeaths"].AsInt == 2,
                    "cancel rejects arguments before action: " + args);
            }

            main.PushCancelFails = true;
            main.Process("push-cancel-fails", Envelope("push-cancel-fails", "cancelMaxItopodPush", new JSONObject()));
            Assert(!Result("push-cancel-fails")["success"].AsBool && Result("push-cancel-fails")["message"].Value.Contains("fixture push cancel failure")
                && Result("push-cancel-fails")["data"]["runtime"]["maxItopodPush"]["active"].AsBool,
                "cancel boundary exception reports failure and unchanged active runtime");
            main.PushCancelFails = false;
            foreach (var args in new[] { null, "null", "{}" })
            {
                var id = "push-cancel-valid-" + _assertions;
                var cancels = main.PushCancelCalls;
                main.Process(id, PushEnvelope(id, "cancelMaxItopodPush", args));
                Assert(Result(id)["success"].AsBool && main.PushCancelCalls == cancels + 1 && !main.PushRuntime["active"].AsBool,
                    "cancel accepts missing/null/empty arguments even when already idle");
            }

            main.PushStartFails = true;
            main.Process("push-start-fails", Envelope("push-start-fails", "startMaxItopodPush", JsonFiles.ParseObject("{\"deaths\":1}")));
            Assert(!Result("push-start-fails")["success"].AsBool && Result("push-start-fails")["message"].Value.Contains("fixture push start failure")
                && !Result("push-start-fails")["data"]["runtime"]["maxItopodPush"]["active"].AsBool,
                "start boundary exception reports failure with actual idle runtime");
            main.PushStartFails = false;

            // Independently supplied action state proves builders do not synthesize it from commands.
            main.PushRuntime["active"] = true;
            main.PushRuntime["remainingDeaths"] = 17;
            main.PushRuntime["floor"] = 321;
            Assert(main.BuildRuntime()["maxItopodPush"].ToString() == main.PushRuntime.ToString(), "read-only runtime builder forwards live push state");
            main.PumpRuntime();
            var heartbeat = JsonFiles.ParseObject(File.ReadAllText(Path.Combine(_dir, "runtime.json")));
            Assert(heartbeat["maxItopodPush"].ToString() == main.PushRuntime.ToString(), "full heartbeat forwards live push state without command");
            main.Process("push-status", Envelope("push-status", "status"));
            Assert(Result("push-status")["data"]["runtime"]["maxItopodPush"].ToString() == main.PushRuntime.ToString(), "status compact pull forwards live push state");
            main.Process("push-final-cancel", Envelope("push-final-cancel", "cancelMaxItopodPush", new JSONObject()));
            Assert(File.ReadAllText(settingsPath) == settingsBytes, "push commands never persist adventure settings");
            System.Console.WriteLine("Max ITOPOD push bridge: " + (_assertions - before) + " assertions.");
        }
    }
}
