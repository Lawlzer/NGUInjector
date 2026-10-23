using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class PresetIntegrationChecks
{
    private static bool RetainedFarmingActive;
    private static Action RetainedProjectionObserver;
    private static Action RetainedPublicationObserver;
    private static object RetainedProjectedResult;
    private static int RetainedNullStrings;
    private static string RetainedParentTemp;
    private static readonly Dictionary<MethodDefinition, Mono.Cecil.Cil.MethodBody> RetainedNativeAdventureMethods = new Dictionary<MethodDefinition, Mono.Cecil.Cil.MethodBody>();
    private static string RetainedFarmingFixturePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "CapturedFarmingRebind20261023.json");

    public static bool RetainedFarmingFixtureActive() => RetainedFarmingActive;
    public static void ObserveRetainedProjection() { if (RetainedFarmingActive) RetainedProjectionObserver?.Invoke(); }
    public static void ObserveRetainedResult(object node) { if (RetainedFarmingActive) { RetainedProjectedResult = node; PrintRetainedNullStrings(node, "$result"); } }
    public static void ObserveRetainedPublication() { if (RetainedFarmingActive) RetainedPublicationObserver?.Invoke(); }

    private static void PrintRetainedNullStrings(object node, string path)
    {
        if ((bool)Property(node, "IsString") && Property(node, "Value") == null) { RetainedNullStrings++; Console.WriteLine("Retained null JSON string: " + path); }
        if ((bool)Property(node, "IsObject"))
            foreach (DictionaryEntry pair in (IDictionary)Get(node, "m_Dict")) PrintRetainedNullStrings(pair.Value, path + "." + pair.Key);
        if ((bool)Property(node, "IsArray"))
        {
            var index = 0;
            foreach (var child in (IEnumerable)GetChildren(node)) PrintRetainedNullStrings(child, path + "[" + index++ + "]");
        }
    }

    private static void CaptureRetainedFarmingGameMethods(AssemblyDefinition game)
    {
        foreach (var type in game.MainModule.Types.Where(x => x.Name == "AllAdvancedTraining"))
        foreach (var method in type.Methods.Where(x => x.Name == "adventurePowerBonus" || x.Name == "adventureToughnessBonus"))
            RetainedNativeAdventureMethods[method] = method.Body;
    }

    private static void ConfigureRetainedFarmingGameFixture(AssemblyDefinition game)
    {
        foreach (var entry in RetainedNativeAdventureMethods)
        {
            var fallback = entry.Key.Body.Instructions.ToArray(); entry.Key.Body = entry.Value;
            var first = entry.Value.Instructions[0]; var il = entry.Value.GetILProcessor();
            il.InsertBefore(first, il.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(RetainedFarmingFixtureActive), All))));
            il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
            foreach (var instruction in fallback) il.InsertBefore(first, instruction);
        }
        foreach (var entry in new[] { new { Type = "InventoryController", Method = "accessorySpaces", Count = 7 },
            new { Type = "AllBeardsController", Method = "capBeards", Count = 5 },
            new { Type = "AllGoldDiggerController", Method = "maxDiggerSlots", Count = 6 } })
        {
            var method = game.MainModule.Types.Single(x => x.Name == entry.Type).Methods.Single(x => x.Name == entry.Method);
            var first = method.Body.Instructions[0]; var il = method.Body.GetILProcessor();
            il.InsertBefore(first, il.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(RetainedFarmingFixtureActive), All))));
            il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, entry.Count)); il.InsertBefore(first, il.Create(OpCodes.Ret));
        }
    }

    private static void ConfigureRetainedFarmingPluginFixture(AssemblyDefinition plugin)
    {
        var projection = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Main").Methods.Single(x => x.Name == "LoadoutPresetJson");
        projection.Body.GetILProcessor().InsertBefore(projection.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveRetainedProjection), All))));
        foreach (var call in projection.Body.Instructions.Where(x => x.Operand is MethodReference method && method.Name == "ToString" && method.Parameters.Count == 0).ToArray())
        {
            projection.Body.GetILProcessor().InsertBefore(call, Instruction.Create(OpCodes.Dup));
            projection.Body.GetILProcessor().InsertBefore(call, Instruction.Create(OpCodes.Call,
                plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveRetainedResult), All))));
        }
        var publish = projection.DeclaringType.Methods.Single(x => x.Name == "AcquireGlobalPreset");
        publish.Body.GetILProcessor().InsertBefore(publish.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveRetainedPublication), All))));
    }

    public static void GenerateRetainedFarmingFixture(Assembly plugin)
    {
        Plugin = plugin;
        var source = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "temp", "checkins", "20261023-034637-captured-relaunch", "post-live"));
        var checkpointPath = Path.Combine(source, "stage-progress-be6762c5194b46c089468f46df8ee220a34749e8f70a69c187181786e4917897.json");
        var checkpoint = JsonParse(File.ReadAllText(checkpointPath)); var save = JsonParse(File.ReadAllText(Path.Combine(source, "NGUSave.json")));
        var fixture = JsonParse("{}"); SetJson(fixture, "checkpoint", checkpoint);
        SetJson(fixture, "plan", JsonParse(File.ReadAllText(Path.Combine(source, "custom-plans", "24hr.json"))));
        SetJson(fixture, "presetLibrary", JsonParse(File.ReadAllText(Path.Combine(source, "presets.json"))));
        var settings = JsonParse(File.ReadAllText(Path.Combine(source, "settings.json"))); var preferences = JsonParse("{}");
        foreach (var field in new[] { "_customAdventureMode", "_customAdventureZone", "_customAdventureEnabled", "_customAdventureRules" })
            SetJson(preferences, field, Json(settings, field));
        SetJson(fixture, "settings", preferences);
        using (var hash = SHA256.Create()) SetJson(fixture, "checkpointSha256", Json(JsonParse("{\"v\":\"" + BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(checkpointPath))).Replace("-", "") + "\"}"), "v"));
        var native = JsonParse("{}"); var inv = JsonParse("{}");
        foreach (var field in new[] { "head", "chest", "legs", "boots", "weapon", "weapon2", "accs", "cubePower", "cubeToughness" })
            SetJson(inv, field, Json(Json(save, "inventory"), field));
        SetJson(native, "inventory", inv);
        foreach (var field in new[] { "beards", "diggers", "NGU", "magic", "training", "advancedTraining", "wandoos98", "rebirthTime", "totalPlaytime" })
            SetJson(native, field, Json(save, field));
        foreach (var field in new[] { "energyPower", "capEnergy", "curEnergy", "idleEnergy", "realGold", "attackMulti", "defenseMulti", "bossID", "highestBoss" })
            SetJson(native, field, Json(save, field));
        var adventure = JsonParse("{}");
        foreach (var field in new[] { "attack", "defense", "regen", "attackSpeed", "highestItopodLevel", "zone", "autoattacking" }) SetJson(adventure, field, Json(Json(save, "adventure"), field));
        SetJson(native, "adventure", adventure); SetJson(fixture, "native", native);
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "checks", "PresetIntegration", "Fixtures", "CapturedFarmingRebind20261023.json");
        File.WriteAllText(outputPath, Invoke(fixture, "ToString", 2).ToString());
        Console.WriteLine("Generated retained failed-live fixture: " + outputPath);
    }

    private static void SetRetainedNativeFields(object target, object json)
    {
        var codec = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var values = codec.Deserialize<Dictionary<string, object>>(json.ToString());
        foreach (var field in target.GetType().GetFields(All).Where(x => !x.IsStatic))
        {
            if (!values.TryGetValue(field.Name, out var value) || value == null) continue;
            field.SetValue(target, codec.Deserialize(codec.Serialize(value), field.FieldType));
        }
    }

    private static object SetupRetainedFarmingRebind()
    {
        var fixture = JsonParse(File.ReadAllText(RetainedFarmingFixturePath)); var checkpoint = Json(fixture, "checkpoint"); var native = Json(fixture, "native");
        RetainedParentTemp = (string)Get(typeof(Program), "Temp");
        Set(typeof(Program), "Temp", Path.Combine(RetainedParentTemp, "retained-farming-" + Guid.NewGuid().ToString("N")));
        SetupAdventureDynamic("retained-rebind-bootstrap", "[1,1,0,1,0,0]");
        RetainedFarmingActive = true;
        Set(MainType, "_dir", (string)Get(typeof(Program), "Temp")); // Avoid a second GUID nesting layer on desktop CLR MAX_PATH.
        ListField(Child(Character, "hacks"), "hacks", 16);
        var settings = Get(MainType, "Settings");
        Set(settings, "_customAdventureMode", 0); Set(settings, "_customAdventureEnabled", false);
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
        SetRetainedNativeFields(settings, Json(fixture, "settings"));
        var saved = Json(checkpoint, "selectedPolicy");
        Set(settings, "_energyNguWeights", ((IEnumerable)GetChildren(Json(saved, "energyNguWeights"))).Cast<object>().Select(x => (int)Property(x, "AsInt")).ToArray());
        Set(settings, "_magicNguWeights", ((IEnumerable)GetChildren(Json(saved, "magicNguWeights"))).Cast<object>().Select(x => (int)Property(x, "AsInt")).ToArray());
        foreach (var field in new[] { "energyPower", "capEnergy", "curEnergy", "idleEnergy", "realGold", "attackMulti", "defenseMulti", "bossID", "highestBoss" })
        {
            var value = new JavaScriptSerializer().Deserialize(Json(native, field).ToString(), Field(Character, field).FieldType);
            Set(Character, field, value);
        }
        SetRetainedNativeFields(Get(Character, "magic"), Json(native, "magic"));
        SetRetainedNativeFields(Get(Character, "NGU"), Json(native, "NGU"));
        SetRetainedNativeFields(Get(Character, "training"), Json(native, "training"));
        SetRetainedNativeFields(Get(Character, "advancedTraining"), Json(native, "advancedTraining"));
        SetRetainedNativeFields(Get(Character, "beards"), Json(native, "beards"));
        SetRetainedNativeFields(Get(Character, "diggers"), Json(native, "diggers"));
        SetRetainedNativeFields(Get(Character, "wandoos98"), Json(native, "wandoos98"));
        SetRetainedNativeFields(Get(Character, "adventure"), Json(native, "adventure"));
        SetRetainedNativeFields(Get(Character, "inventory"), Json(native, "inventory"));
        Set(Get(Character, "settings"), "wandoos98On", true);
        var descriptors = ((IEnumerable)GetChildren(Json(Json(saved, "equipment"), "equipment"))).Cast<object>().ToArray();
        var instances = (IDictionary)CallMain("LoadoutPresetInstances");
        foreach (var descriptor in descriptors)
        {
            var slot = (int)Property(Json(descriptor, "targetSlot"), "AsInt"); var item = instances[slot];
            var material = (string)Property(Json(descriptor, "material"), "Value");
            Set(item, "path", material.Split(';').Single(x => x.StartsWith("path=", StringComparison.Ordinal)).Substring(5));
            Set(item, "unique", bool.Parse(material.Split(';').Single(x => x.StartsWith("unique=", StringComparison.Ordinal)).Substring(7)));
            Equal(material, CallMain("LoadoutPresetPrimitiveKey", item), "retained native save independently matches complete selected material/" + slot);
        }
        Set(Get(Character, "adventure"), "zone", -1); // Explicit observed safe handover, not imported route authority.
        Set(Get(Character, "adventure"), "itopodStart", 196); Set(Get(Character, "adventure"), "itopodEnd", 196);
        Set(Get(Get(Character, "adventureController"), "itopod"), "character", Character);
        Set(Get(Character, "stats"), "rebirthNumber", 230L);
        Set(Get(Character, "rebirthTime"), "totalseconds", (double)Property(Json(checkpoint, "elapsed"), "AsDouble"));
        Set(Get(Character, "totalPlaytime"), "totalseconds", (double)Property(Json(checkpoint, "total"), "AsDouble"));
        // Enemy scene data is not part of a save. Keep the retained floor/policy and
        // real positive safety checks with a finite template under neutral fixture bonuses.
        var enemies = (IList)Get(Get(Character, "adventureController"), "itopodEnemyList");
        foreach (var enemy in enemies) { Set(enemy, "maxHP", 40f); Set(enemy, "defense", 10f); }
        Invoke(typeof(Program), "RefreshBonuses");
        var store = Get(MainType, "CustomPlanStore"); var planPath = Path.Combine((string)Get(store, "_plansDir"), "24hr.json");
        var library = Get(store, "<PresetLibrary>k__BackingField");
        File.WriteAllText(planPath, Json(fixture, "plan").ToString()); File.WriteAllText((string)Get(library, "_path"), Json(fixture, "presetLibrary").ToString());
        Set(settings, "_customPlanFile", "24hr"); Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "24hr");
        CallMain("InitializeProtectedStageProgress", plan);
        var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); File.WriteAllText(path, checkpoint.ToString());
        CallMain("InitializeProtectedStageProgress", plan);
        Equal(null, Get(MainInstance, "_protectedOuterCheckpointIssue"), "real retained outer checkpoint restores without a new stage entry");
        return plan;
    }

    private static void CheckExecutionRetainedFarmingRebind()
    {
        var before = Assertions;
        foreach (var fault in new[] { "none", "projection", "projection-route", "projection-resource", "projection-model", "publication-route" })
        try
        {
            var plan = SetupRetainedFarmingRebind();
            var scheduler = Get(MainInstance, "_protectedScheduler"); var commitment = Invoke(scheduler, "ExportState");
            var fightEntry = Get(MainInstance, "_customStageFightKey");
            var saved = Json(JsonParse(File.ReadAllText(RetainedFarmingFixturePath)), "checkpoint");
            var original = Json(saved, "selectedPolicy");
            var native = Get(Character, "wandoos98"); var wEnergy = Get(native, "energyLevel"); var wMagic = Get(native, "magicLevel");
            var atTargets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
            Equal(3000L, atTargets[0], fault + " retained native toughness target is positive3000");
            Equal(3000L, atTargets[1], fault + " retained native power target is positive3000");
            var basic = ExecutionStopProtectedState();
            // A conserved native deficit is observed by the tokenized review; the
            // actual production writer must repair it, not claim a retained receipt.
            var ngu = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
            Set(ngu, "energy", (long)Get(ngu, "energy") - 1); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
            var preview = CallMain("PreviewExecutionRebind", JsonParse("{}"));
            Equal("3c2748bd93c74f578553dcee8f3b09a2", Property(Json(preview, "acquisition"), "Value"), "actual retained acquisition survives real preview");
            Equal(49822, Property(Json(preview, "horizon"), "AsInt"), "actual retained horizon survives review");
            var reviewedArgs = ExecutionReviewApplyArgs(preview);
            CallMain("ApplyExecutionRebind", reviewedArgs);
            var policy = Get(MainInstance, "_globalPresetApplyingPolicy");
            Check(((Array)Get(Get(Get(policy, "Candidate"), "Support"), "Diggers")).Cast<object>().All(x => Get(x, "Name") == null),
                "actual semantic import has no persisted digger display names; the test does not seed all-nonnull policy metadata");
            var writes = ResourceTransfers; var searches = PresetSearches; var projections = 0;
            RetainedProjectedResult = null; RetainedNullStrings = 0;
            if (fault == "publication-route") RetainedPublicationObserver = () => {
                RetainedPublicationObserver = null; CallMain("InvalidateExecutionObservations", Domain("Route"));
            };
            RetainedProjectionObserver = () => {
                projections++;
                Equal(1000, Get(Get(Character, "adventure"), "zone"), fault + " projection fault boundary follows real native farming entry");
                Equal(196, Get(Get(Character, "adventure"), "itopodStart"), fault + " native model already holds retained floor196");
                Equal(true, CallMain("ExecutionBaselineResourceMapMatches", Get(MainInstance, "_executionBaselineResourceIntent")), fault + " projection begins only after actual phase funding matches");
                RetainedProjectionObserver = null;
                if (fault == "projection-route") CallMain("InvalidateExecutionObservations", Domain("Route"));
                if (fault == "projection-resource")
                { Set(ngu, "energy", (long)Get(ngu, "energy") - 1); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1); }
                if (fault == "projection-model") Set(Character, "magic", Invoke(Get(Character, "magic"), "MemberwiseClone"));
                if (fault != "none" && fault != "publication-route") throw new InvalidOperationException("retained offline result projection failure");
            };
            CallMain("ReconcileCustomResources", plan, true);
            var error = Get(MainInstance, "_globalPresetReconcileException"); if (error != null) Console.WriteLine("Retained prepared application stack: " + error);
            Equal(1, projections, fault + " really reaches the post-native result projection");
            if (fault.StartsWith("projection-", StringComparison.Ordinal) || fault == "publication-route")
            {
                Equal(null, Get(MainInstance, "_globalPresetReceipt"), fault + " lost native/grant witness cannot fabricate committed application proof");
                Check(Get(MainInstance, "_protectedSelectedPolicyIssue") != null, fault + " uncertain continuation requires a NEW verified review token");
                var uncertainWrites = ResourceTransfers; var routes = AdventureZoneEntries;
                CallMain("ReconcileCustomResources", plan, true);
                Equal(uncertainWrites, ResourceTransfers, fault + " ordinary service cannot replay uncertain native work without review");
                Equal(routes, AdventureZoneEntries, fault + " ordinary service cannot reacquire an uncertain route");
                Throws(() => CallMain("ApplyExecutionRebind", reviewedArgs), "expired", fault + " the spent original token cannot acknowledge new uncertainty");
                preview = CallMain("PreviewExecutionRebind", JsonParse("{}")); CallMain("ApplyExecutionRebind", ExecutionReviewApplyArgs(preview));
                Invoke(Get(Get(Character, "adventureController"), "zoneSelector"), "changeZone", -1);
                System.Threading.Thread.Sleep(1100); // Existing retry backoff is not renewed authority or a new stage budget.
                CallMain("ReconcileCustomResources", plan, true);
            }
            Check(Get(MainInstance, "_globalPresetReceipt") != null, fault + " actual retained farming import publishes verified receipt after native continuation; "
                + Get(MainInstance, "_globalPresetReconcileException") + "; " + CallMain("GetResourceCoordinationRuntime"));
            Equal(1000, Get(Get(Character, "adventure"), "zone"), "retained farming policy performs real native ITOPOD entry");
            Equal(196, Get(Get(Character, "adventure"), "itopodStart"), "retained farming policy preserves acquired floor196");
            Equal(196, Get(Get(Character, "adventure"), "itopodEnd"), fault + " native route keeps the retained end floor");
            Equal(196, Get(Get(Character, "adventureController"), "itopodLevel"), fault + " actual native entry uses the retained floor");
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var resumed = Get(receipt, "Policy");
            Equal("3c2748bd93c74f578553dcee8f3b09a2", Get(resumed, "Acquisition"), fault + " preserves original acquisition");
            Equal(49822, Get(resumed, "Horizon"), fault + " preserves original horizon");
            Equal(Property(Json(original, "definition"), "Value"), Get(resumed, "Definition"), fault + " preserves actual original definition");
            Check(((double[])Get(Get(resumed, "Plan"), "Normalization")).SequenceEqual(((IEnumerable)GetChildren(Json(original, "normalization"))).Cast<object>().Select(x => (double)Property(x, "AsDouble"))), fault + " preserves actual frozen scoring denominators");
            Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), fault + " caller publication has genuinely current application authority");
            CallMain("VerifyLoadoutPresetAllocations", Get(receipt, "Execution"));
            Check(ResourceTransfers > writes, fault + " continuation really repairs actual native funding");
            Equal(searches, PresetSearches, fault + " rebind continuation never searches replacement equipment/support");
            if (fault == "projection") Equal("ModelAppliedRenderFailed", Property(Json(Get(MainInstance, "_loadoutPresetSnapshot"), "resultDisposition"), "Value"), "result-only failure has a typed known-committed outcome, not UnknownOutcome");
            if (fault == "none")
            {
                Check(RetainedProjectedResult != null, "positive retained payload executes the full real result serializer");
                Equal(0, RetainedNullStrings, "current snapshot labels eliminate all imported null-name JSON strings without changing semantic decisions");
            }
            var committedWrites = ResourceTransfers; var committedRoutes = AdventureZoneEntries;
            CallMain("ReconcileCustomResources", plan, true);
            Equal(committedWrites, ResourceTransfers, fault + " published receipt prevents native accounting replay");
            Equal(committedRoutes, AdventureZoneEntries, fault + " published receipt prevents route replay");
            var telemetry = CallMain("GetExecutionStateRuntime");
            var resourceRows = ((IEnumerable)GetChildren(Json(telemetry, "resources"))).Cast<object>().ToArray();
            Check(resourceRows.Length > 0 && resourceRows.All(x => (bool)Property(Json(x, "verified"), "AsBool")), fault + " ordinary current-owner service verifies actual resource authority, not just numeric equality");
            Equal(true, Property(Json(Json(telemetry, "support"), "verified"), "AsBool"), fault + " actual support authority remains verified after publication");
            foreach (var lane in new[] { "energy", "magic" })
            {
                var weights = ((IEnumerable)GetChildren(Json(original, lane == "energy" ? "energyNguWeights" : "magicNguWeights"))).Cast<object>().Select(x => (int)Property(x, "AsInt")).ToArray();
                var skills = (IList)Get(Get(Character, "NGU"), lane == "energy" ? "skills" : "magicSkills");
                for (var i = 0; i < skills.Count; i++)
                    if (i >= weights.Length || weights[i] == 0) Equal(0L, Get(skills[i], lane), fault + " preserves actual global zero exclusion/" + lane + "/" + i);
            }
            Equal(fightEntry, Get(MainInstance, "_customStageFightKey"), fault + " does not repeat or replace the actual entry-fight receipt");
            Equal(false, Property(MainInstance, "BossPushRunning"), fault + " does not acquire another fight process");
            Equal(basic, ExecutionStopProtectedState(), fault + " preserves basic training and R3");
            Equal(wEnergy, Get(native, "energyLevel"), fault + " preserves actual Wandoos Energy history");
            Equal(wMagic, Get(native, "magicLevel"), fault + " preserves actual Wandoos Magic history");
            Check(atTargets.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "levelTarget")), fault + " preserves complete positive native AT configuration");
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied", "visits" })
                Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), fault + " preserves actual entry/credit/budget/" + field);
            Set(Get(MainType, "Settings"), "_manageEnergy", false);
            CallMain("ReconcileCustomResources", plan, true);
            Equal(false, CallMain("GlobalPresetStageReceiptValid", plan), fault + " imported semantic defaults cannot hide the observed execution's controlled Energy permission");
            Equal(committedWrites, ResourceTransfers, fault + " withdrawn Energy permission never authorizes resource replay");
        }
        finally
        {
            RetainedFarmingActive = false; RetainedProjectionObserver = RetainedPublicationObserver = null; RetainedProjectedResult = null; PresetEvaluationObserver = null;
            if (RetainedParentTemp != null) Set(typeof(Program), "Temp", RetainedParentTemp);
            RetainedParentTemp = null;
        }
        Console.WriteLine("Retained farming rebind: " + (Assertions - before) + " native assertions.");
    }
}

public static partial class Program
{
    private static void CheckCaptureRetainedFarmingFixture() => PresetIntegrationChecks.GenerateRetainedFarmingFixture(Plugin);
}
