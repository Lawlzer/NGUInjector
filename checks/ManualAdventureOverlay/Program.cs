using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;

// Reuse the existing compiled/native fixture, not a second implementation of overlay policy.
public static class ManualAdventureOverlayChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Assembly Game, Plugin;
    private static Type MainType;
    private static object Runtime, Character, Settings, BasePreset, Library;
    private static string PresetId, SavedBase, ExecutionBase, HistoricalBase, ResourceBaseline, CounterBaseline;
    private static int Assertions, Failures, FixtureNumber;

    public static int Main(string[] args)
    {
        string temp = null;
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass a freshly built NGUInjector.dll and the game's Managed directory.");
            var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                var name = new AssemblyName(request.Name).Name;
                if (name == "Assembly-CSharp") return Game;
                if (name == "UnityEngine.UI") return (Assembly)Fixture("LoadUi", Path.Combine(managed, name + ".dll"));
                var path = Path.Combine(managed, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Game = (Assembly)Fixture("LoadGame", Path.Combine(managed, "Assembly-CSharp.dll"));
            Plugin = (Assembly)Fixture("LoadPlugin", Path.GetFullPath(args[0]));
            MainType = Plugin.GetType("NGUInjector.Main", true);
            temp = Path.Combine(Path.GetTempPath(), "opencode", "ManualAdventureOverlay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            foreach (var fixture in new[] { typeof(Program), typeof(PresetIntegrationChecks) })
            {
                Set(fixture, "Game", Game); Set(fixture, "Plugin", Plugin); Set(fixture, "MainType", MainType);
            }
            Set(typeof(Program), "Temp", temp);
            Run("Offline settings persistence isolation", () => Fixture("CheckAdventureSettingsIsolation"));
            Run("Keep gear / zero bonuses", CheckZeroBonuses);
            Run("Selected loadout / preserving swaps", CheckSelectedLoadout);
            Run("Additive support / no compounding", CheckAdditiveSupport);
            Run("Zeroing modifiers", () => CheckRemoval(false));
            Run("Disabling overlay", () => CheckRemoval(true));
            Run("Reset ownership guards", CheckResetGuards);
            Run("Current successor without historical support detour", CheckCurrentSuccessor);
            Run("Native support partial and render continuation", CheckSupportContinuation);
            Run("Native support callback revokes permission", () => CheckPermissionLoss(false));
            Run("Native foreign support callback survives reset", () => CheckPermissionLoss(true));
            Run("Uncontrolled support removal does not imply restore", CheckUncontrolledRemoval);
            Console.WriteLine((Failures == 0 ? "PASS: " : "FAIL: ") + Assertions + " compiled overlay assertions; " + Failures + " failed groups; no live game/save access.");
            return Failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (temp != null && Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    private static void Setup(bool preset, bool selectedLoadout = false)
    {
        Runtime = Character = Settings = BasePreset = Library = null;
        PresetId = "overlay-support-" + ++FixtureNumber;
        var args = Fixture("SetupAdventureDynamic", PresetId, "[1,0,0,0,0,0,0,0,0,0,0,0]", true);
        Runtime = Get(typeof(PresetIntegrationChecks), "MainInstance");
        Character = Get(typeof(PresetIntegrationChecks), "Character"); Settings = Get(MainType, "Settings");
        Fixture("SetActiveBeards", new int[] { 1, 3, 5, 6 });
        var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(diggers[4], "maxLevel", 4L); Set(diggers[5], "maxLevel", 4L);
        Set(diggers[4], "curLevel", 2L); Set(diggers[4], "active", true);
        ((IList)Get(Get(Character, "diggers"), "activeDiggers")).Add(4);
        Library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        // Every successor preflight retains unmanaged R3, even with Keep gear.
        // Bind the real getter dependencies before acquisition; do not replace the getter.
        Set(Get(Character, "wishesController"), "character", Character);
        Fixture("ListField", Get(Character, "wishesController"), "properties", 512);
        Fixture("ListField", Get(Character, "wishes"), "wishes", 512);
        if (preset)
        {
            var root = Call(Library, "ReadExternalLibrary");
            var row = Call(typeof(PresetIntegrationChecks), "Json", root, "Presets");
            row = ((IEnumerable)Fixture("GetChildren", row)).Cast<object>()
                .Single(x => Equals(PresetId, Property(Fixture("Json", x, "Id"), "Value")));
            foreach (var field in new[] {
                new[] { "SupportMode", "\"planned\"" }, new[] { "AdventureDynamicWeight", "0" },
                new[] { "BeardPriority", "[1,3,5,6]" }, new[] { "DiggerWeights", "[0,0,0,0,4,3,0,0,0,0,0,0]" }
            }) Fixture("SetJson", row, field[0], Fixture("Json", Fixture("JsonParse", "{\"value\":" + field[1] + "}"), "value"));
            var revision = Call(Library, "SaveExternalLibrary", root, Property(Fixture("Json", root, "__externalUiRevision"), "Value"));
            args = Fixture("JsonParse", "{\"presetId\":\"" + PresetId + "\",\"libraryRevision\":\"" + revision + "\"}");
            Call(Runtime, "EquipGlobalPreset", args, null);
            var receipt = Get(Runtime, "_globalPresetReceipt");
            Equal(true, receipt != null, "real base preset acquisition succeeds");
            BasePreset = Get(Get(receipt, "Policy"), "ExecutionPreset");
            SavedBase = Call(Call(Library, "Get", PresetId), "ToJson").ToString();
            ExecutionBase = Call(BasePreset, "ToJson").ToString();
            HistoricalBase = Get(Runtime, "_loadoutPresetSnapshot").ToString();
        }
        Set(Settings, "_manualAdventureEnabled", true); Set(Settings, "_manualAdventureLoadout", -1);
        Set(Settings, "_manualAdventureGearWeights", new double[5]);
        Bonuses(0, 0);
        Set(Runtime, "_manualAdventureSetupActive", true);
        var res3 = Call(typeof(Program), "Child", Character, "res3");
        Set(res3, "capRes3", 1000L);
        Set(res3, "curRes3", 731L); Set(res3, "idleRes3", 431L);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3", 300L);
        Equal(true, (long)Call(Character, "totalCapRes3") >= 731L, "overlay fixture retains funded unmanaged R3 through native wishes cap getter");
        ResourceBaseline = Resources(); CounterBaseline = Counters();
        Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "Keep gear enters real overlay");
    }

    private static void CheckZeroBonuses()
    {
        foreach (var preset in new[] { false, true })
        {
            Setup(preset);
            var support = Property(Call(MainType, "CaptureSupportState"), "StableKey");
            var mutation = Get(Runtime, "_supportMutation");
            for (var tick = 0; tick < 3; tick++)
            {
                Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "Keep gear repeat succeeds");
                Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "zero support is a no-op");
            }
            Equal(support, Property(Call(MainType, "CaptureSupportState"), "StableKey"), "zero bonuses preserve active support, flags and every inactive level");
            Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "zero bonuses never invoke exact support actuator");
            Unchanged(preset);
        }
    }

    private static void CheckAdditiveSupport()
    {
        Setup(true); Bonuses(1.5, 2);
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "additive support applies");
        Equal("0,1,3,5|5:4", Selection(), "base priorities survive; 3+2 beats base digger 4 (bonus 2 alone would lose)");
        var basis = BaselineSupport();
        var applied = Get(Runtime, "_supportMutation");
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "cached repeated application succeeds");
        Equal(true, ReferenceEquals(applied, Get(Runtime, "_supportMutation")), "cached tick does not reapply support");
        for (var tick = 0; tick < 3; tick++)
        {
            Set(Runtime, "_manualAdventureSupportRefreshAt", DateTime.MinValue);
            Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "minute refresh succeeds");
            Equal("0,1,3,5|5:4", Selection(), "minute refresh does not compound bonuses");
        }
        Bonuses(.5, .5);
        Set(Runtime, "_manualAdventureSupportRefreshAt", DateTime.MinValue);
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "smaller bonuses apply at refresh");
        Equal("1,3,5,6|4:4", Selection(), "smaller modifiers recompute from original base, not last overlay");
        Equal(true, ReferenceEquals(basis, BaselineSupport()), "current acquired support intent is never rebased to overlay");
        Unchanged(true);
        var previousKey = Get(Runtime, "_manualAdventureSupportKey");
        Bonuses(1.5, 2);
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "changed bonuses apply before minute deadline");
        Unchanged(true);
        Equal(false, Equals(previousKey, Get(Runtime, "_manualAdventureSupportKey")), "different modifier arrays must have distinct support cache keys");
        Equal("0,1,3,5|5:4", Selection(), "edits bypass minute cache immediately");
    }

    private static void CheckSelectedLoadout()
    {
        Setup(true, true);
        var inventory = Get(Character, "inventory"); var originalHead = Get(inventory, "head");
        var items = (IList)Get(inventory, "inventory"); var combatHead = items[0]; var lootHead = items[1];
        foreach (var head in new[] { combatHead, lootHead })
        {
            Set(head, "id", ReferenceEquals(head, combatHead) ? 550 : 551);
            Set(head, "type", Get(originalHead, "type")); Set(head, "bossRequired", 1); Set(head, "removable", true);
            Set(head, "curAttack", ReferenceEquals(head, combatHead) ? 10000f : 1f);
            Set(head, "curDefense", ReferenceEquals(head, combatHead) ? 10000f : 1f);
        }
        Set(lootHead, "spec1Type", Enum.Parse(Get(lootHead, "spec1Type").GetType(), "Looting"));
        Set(lootHead, "spec1Cur", 100000f);
        Set(Get(Character, "res3"), "capRes3", 1000L);
        var wish = ((IList)Get(Get(Character, "wishes"), "wishes"))[0]; Set(wish, "res3", 300L);
        Program.RefreshBonuses();
        Equal(true, (long)Call(Character, "totalCapRes3") >= 731L, "selected-loadout fixture funds retained R3 with native capacity");
        ResourceBaseline = Resources(); CounterBaseline = Counters();
        var receipt = Get(Runtime, "_globalPresetReceipt"); var policy = Get(receipt, "Policy");
        Equal(true, Call(Runtime, "GlobalPresetReceiptValid", Get(receipt, "Owner")), "selected-loadout fixture begins with a valid acquired base receipt");
        var baseIdentity = Property(Runtime, "ManualAdventureBaseIdentity");
        var support = Property(Call(MainType, "CaptureSupportState"), "StableKey");
        var supportMutation = Get(Runtime, "_supportMutation");
        var searches = (int)Get(typeof(PresetIntegrationChecks), "PresetSearches");
        var observed = 0; double[] observedWeights = null; var observedMode = -1; var plannedSupport = true;
        var observer = Get(typeof(PresetIntegrationChecks), "PresetEvaluationObserver");
        Set(typeof(PresetIntegrationChecks), "PresetEvaluationObserver", (Action<object, object>)((candidate, request) => {
            var weights = (double[])Get(request, "GearWeights");
            if (!weights.Any(x => x > 0)) return;
            observed++; observedWeights = weights.ToArray(); observedMode = (int)Get(request, "GearFightMode");
            plannedSupport = (bool)Get(request, "PlannedSupport");
        }));
        try
        {
            Set(Settings, "_manualAdventureLoadout", 1);
            Set(Settings, "_manualAdventureGearWeights", new double[] { 100, 0, 0, 1, 0 });
            Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "selection 1 equips through real planner/actuator: " + Call(MainType, "GetCustomAutomationStatus"));
            Equal(searches + 1, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "selected Push/drop overlay invokes production planner once");
            Equal(true, observed > 0, "production planner evaluates gear bonus requests");
            Equal(1, observedMode, "selection 1 reaches the production fight-score request");
            Equal(false, plannedSupport, "selected overlay planner observes support instead of replanning it");
            Equal("200,0,0,1,0", string.Join(",", observedWeights), "planner receives additive Push preference and nonzero Drop bonus");
            Equal(true, ReferenceEquals(combatHead, Get(inventory, "head")), "preserving actuator really equips the combat head");
            Equal(true, ReferenceEquals(originalHead, items[0]), "native Inventory swap preserves displaced base head");
            Equal(1, Get(Runtime, "_manualAdventureAppliedLoadout"), "successful real application commits selection 1");
            Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "first selected swap with zero support bonuses preserves support");
            Equal(support, Property(Call(MainType, "CaptureSupportState"), "StableKey"), "first selected swap leaves exact support untouched");
            Unchanged(true, 1);
            Equal(300L, Get(wish, "res3"), "first selected swap preserves an actual R3 wish allocation");
            Equal(true, Call(Runtime, "GlobalPresetReceiptValid", Get(receipt, "Owner")), "first selected swap preserves valid base ownership");
            var firstKey = Get(Runtime, "_manualAdventureOverlayKey");
            Equal(DateTime.MinValue, Get(Runtime, "_manualAdventureGearRetryAt"), "successful selection clears retry deadline");
            Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "unchanged selected overlay is cached");
            Equal(searches + 1, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "cached selected gear never replans");
            var selectedInstances = Get(Runtime, "_manualAdventureSelectedGear");
            var selectedMutation = Get(typeof(Program), "Swaps");
            Call(Runtime, "ResetManualAdventureOverlay");
            Equal(null, Get(Runtime, "_manualAdventureOverlayGear"), "application reset discards only the applied overlay observation");
            Equal(true, ReferenceEquals(selectedInstances, Get(Runtime, "_manualAdventureSelectedGear")), "application reset retains acquired selected instances");
            Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "application reset reconverges using the retained selected requirements");
            Equal(searches + 1, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "application reset is not an authorized replacement search");
            Equal(selectedMutation, Get(typeof(Program), "Swaps"), "verified retained gear is not swapped again after reset");

            Set(Settings, "_manualAdventureGearWeights", new double[] { 1, 0, 0, 1000000, 0 });
            Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "immediate weight edit succeeds without expiring a retry timer");
            Equal(false, Equals(firstKey, Get(Runtime, "_manualAdventureOverlayKey")), "gear-weight-only edit changes the actual overlay key");
            Equal(searches + 2, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "gear-weight edit triggers exactly one new production search");
            Equal("101,0,0,1000000,0", string.Join(",", observedWeights), "new request uses edited weights without compounding Push");
            Equal(true, ReferenceEquals(lootHead, Get(inventory, "head")), "Drop-weight edit really swaps to the competing loot head");
            Equal(true, ReferenceEquals(combatHead, items[1]), "second native swap retains displaced combat head");
            Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "selected gear with zero support bonuses does not override support");
            Equal(support, Property(Call(MainType, "CaptureSupportState"), "StableKey"), "both selected swaps preserve all support flags and levels");
            Equal(true, ReferenceEquals(supportMutation, Get(Runtime, "_supportMutation")), "selected gear never invokes exact support actuator");
            Equal(true, ReferenceEquals(receipt, Get(Runtime, "_globalPresetReceipt")), "selected overlay retains base receipt");
            Equal(true, ReferenceEquals(policy, Get(receipt, "Policy")), "selected overlay retains acquired base policy");
            Equal(true, ReferenceEquals(BasePreset, Property(Runtime, "ManualAdventureBasePreset")), "selected overlay retains the same acquired base definition");
            Equal(baseIdentity, Property(Runtime, "ManualAdventureBaseIdentity"), "selected overlay retains base identity");
            Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "edited selected overlay is cached after success");
            Equal(searches + 2, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "successful weight edit cannot cause repeated searches");
            Unchanged(true, 2);
            Equal(300L, Get(wish, "res3"), "edited selected swap preserves the actual R3 wish allocation");
            Equal(true, Call(Runtime, "GlobalPresetReceiptValid", Get(receipt, "Owner")), "edited selected swap preserves valid base ownership");
        }
        finally { Set(typeof(PresetIntegrationChecks), "PresetEvaluationObserver", observer); }
    }

    private static void CheckRemoval(bool disable)
    {
        Setup(true); var baseline = Selection(); Bonuses(1.5, 2);
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "nonzero modifiers apply before removal");
        Equal(false, baseline == Selection(), "removal has a genuinely different overlay to undo");
        if (disable)
        {
            Set(Settings, "_manualAdventureEnabled", false);
            Call(Runtime, "ResetManualAdventureOverlay");
            Equal(null, Get(Runtime, "_manualAdventureSupportApplied"), "disable removes the support claim");
            Call(Runtime, "ResetManualAdventureOverlay");
        }
        else
        {
            Bonuses(0, 0);
            Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "zeroing modifiers restores immediately");
        }
        Unchanged(true);
        Equal(baseline, Selection(), "removing modifiers restores original exact support");
    }

    private static void CheckResetGuards()
    {
        foreach (var interference in new[] { "epoch", "gear claim", "support claim", "user support" })
        {
            Setup(true); var baseline = Selection(); Bonuses(1.5, 2);
            Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "overlay applies before ownership change");
            Equal(false, baseline == Selection(), "ownership guard has changed support to protect");
            var basis = BaselineSupport();
            var applied = Get(Runtime, "_manualAdventureSupportApplied");
            var epoch = Get(Runtime, "_manualAdventureOverlayEpoch");
            var mutation = Get(Runtime, "_supportMutation");
            object foreignClaim = null;
            if (interference == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            if (interference.EndsWith(" claim", StringComparison.Ordinal))
            {
                var domain = Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), interference == "gear claim" ? "Gear" : "Support");
                foreignClaim = Call(Runtime, "BeginExecutionClaim", "overlay-interference", interference, domain, (Func<bool>)(() => true));
            }
            if (interference == "user support") Fixture("SetActiveBeards", new int[] { 3 });
            var observed = Property(Call(MainType, "CaptureSupportState"), "StableKey");
            if (interference == "user support")
            {
                for (var tick = 0; tick < 2; tick++)
                {
                    // Exercise both changed-modifier and expired-refresh paths while still active.
                    Bonuses(tick == 0 ? .5 : 1.5, tick == 0 ? .5 : 2);
                    Set(Runtime, "_manualAdventureSupportRefreshAt", DateTime.MinValue);
                    var message = (string)Call(Runtime, "ApplyManualAdventureOverlaySupport");
                    Equal(true, message != null && message.Contains("support was changed by another owner"), "active foreign support reports a pause");
                    Equal(observed, Property(Call(MainType, "CaptureSupportState"), "StableKey"), "active overlay never overwrites foreign support");
                    Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "foreign support does not invoke native support application");
                    Equal(true, ReferenceEquals(basis, BaselineSupport()), "foreign support is not adopted as baseline intent");
                    Equal(true, ReferenceEquals(applied, Get(Runtime, "_manualAdventureSupportApplied")), "foreign support is not adopted as the applied overlay");
                }
            }
            Set(Settings, "_manualAdventureEnabled", false); Call(Runtime, "ResetManualAdventureOverlay");
            Equal(observed, Property(Call(MainType, "CaptureSupportState"), "StableKey"), interference + " prevents stale support restoration");
            if (foreignClaim != null)
            {
                Call(Runtime, "ResetManualAdventureOverlay");
                Equal(observed, Property(Call(MainType, "CaptureSupportState"), "StableKey"), "repeated claimed disable leaves support unchanged");
                Equal(false, Get(foreignClaim, "Ended"), "pending disable never ends the foreign claim");
                Equal(true, ReferenceEquals(basis, BaselineSupport()), "claimed disable retains current successor intent for retry");
                Equal(true, ReferenceEquals(applied, Get(Runtime, "_manualAdventureSupportApplied")), "claimed disable retains applied support ownership");
                Equal(true, ReferenceEquals(epoch, Get(Runtime, "_manualAdventureOverlayEpoch")), "claimed disable retains the epoch guard");
                Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "claimed disable never attempts native restoration");
                Call(Runtime, "EndExecutionClaim", foreignClaim, false);
                Call(Runtime, "ResetManualAdventureOverlay");
                Equal(baseline, Selection(), "pending disable resolves exact current base support once the foreign claim ends");
                Equal(null, Get(Runtime, "_manualAdventureSupportApplied"), "successful disable retry removes the claim");
                var restored = Get(Runtime, "_supportMutation");
                Call(Runtime, "ResetManualAdventureOverlay");
                Equal(true, ReferenceEquals(restored, Get(Runtime, "_supportMutation")), "successful cleanup retry is idempotent");
            }
            Unchanged(true);
        }
    }

    private static object BaselineSupport() => Get(Get(Runtime, "_globalPresetReceipt"), "BaselineSupport");

    private static void CheckUncontrolledRemoval()
    {
        Setup(false); Bonuses(1.5, 2);
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "uncontrolled additive support verifies");
        var current = Selection(); var mutation = Get(Runtime, "_supportMutation");
        Set(Settings, "_manualAdventureEnabled", false); Call(Runtime, "ResetManualAdventureOverlay");
        Equal(current, Selection(), "no successor does not authorize historical support restoration");
        Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "uncontrolled removal performs zero native support writes");
        Unchanged(false);
    }

    private static void CheckCurrentSuccessor()
    {
        Setup(true); var original = Selection();
        var inventory = Get(Character, "inventory"); var a = Get(inventory, "head");
        var items = (IList)Get(inventory, "inventory"); var combat = items[0]; var b = items[1];
        foreach (var head in new[] { combat, b })
        {
            Set(head, "id", ReferenceEquals(head, combat) ? 550 : 551);
            Set(head, "type", Get(a, "type")); Set(head, "bossRequired", 1); Set(head, "removable", true);
            Set(head, "curAttack", ReferenceEquals(head, combat) ? 10000f : 1f);
            Set(head, "curDefense", ReferenceEquals(head, combat) ? 10000f : 1f);
        }
        Set(b, "spec1Type", Enum.Parse(Get(b, "spec1Type").GetType(), "NGU")); Set(b, "spec1Cur", 100000f);
        Program.RefreshBonuses();
        Set(Settings, "_manualAdventureLoadout", 1);
        Set(Settings, "_manualAdventureGearWeights", new double[] { 1000000, 0, 0, 0, 0 });
        Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "native A -> selected combat overlay verifies");
        Equal(true, ReferenceEquals(combat, Get(inventory, "head")), "overlay really owns a different native head from A");
        var selectedGear = Get(Runtime, "_manualAdventureSelectedGear");
        var swaps = Get(typeof(Program), "Swaps");
        var timeline = new System.Collections.Generic.List<object>();
        Set(typeof(Program), "NativeGearSwapped", (Action<object>)(head => timeline.Add(head)));
        try
        {
        Bonuses(1.5, 2);
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "A overlay is verified");
        var overlay = Selection(); var mutation = Get(Runtime, "_supportMutation");
        Call(Runtime, "ReleaseManualAdventureOverlayForPresetAcquisition", false);
        Equal(overlay, Selection(), "releasing A never installs historical support before B");
        Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "acquisition handoff performs zero native support writes");

        // An actual definition edit and native acquisition, not a fabricated receipt.
        var root = Call(Library, "ReadExternalLibrary");
        var rows = Fixture("Json", root, "Presets");
        var row = ((IEnumerable)Fixture("GetChildren", rows)).Cast<object>()
            .Single(x => Equals(PresetId, Property(Fixture("Json", x, "Id"), "Value")));
        Fixture("SetJson", row, "BeardPriority", Fixture("Json", Fixture("JsonParse", "{\"value\":[6]}"), "value"));
        Fixture("SetJson", row, "DiggerWeights", Fixture("Json", Fixture("JsonParse", "{\"value\":[0,0,0,0,4,0,0,0,0,0,0,0]}"), "value"));
        var revision = Call(Library, "SaveExternalLibrary", root, Property(Fixture("Json", root, "__externalUiRevision"), "Value"));
        var args = Fixture("JsonParse", "{\"presetId\":\"" + PresetId + "\",\"libraryRevision\":\"" + revision + "\"}");
        Call(Runtime, "EquipGlobalPreset", args, null);
        var receipt = Get(Runtime, "_globalPresetReceipt");
        Equal(true, receipt != null, "real B acquisition succeeds while manual intent remains enabled");
        Equal(true, Get(Settings, "_manualAdventureEnabled"), "B does not erase the manual goal");
        var baseline = Get(Runtime, "_manualAdventureBaseline");
        var successor = (string)Property(Get(baseline, "Support"), "StableKey");
        Equal("6|4:4", successor, "acquired B support excludes temporary overlay additions");
        var policySupport = Get(Get(Get(receipt, "Policy"), "Candidate"), "Support");
        Equal(false, ReferenceEquals(policySupport, Get(Get(receipt, "Execution"), "Support")),
            "raw acquired support and observed composed support have independent storage");
        Equal("6", string.Join(",", (int[])Get(policySupport, "Beards")),
            "post-composition funding does not mutate B's acquired beard selection");
        Equal(true, ReferenceEquals(b, Property(((Array)Get(baseline, "Gear")).GetValue(0), "equipment")), "B acquires the genuinely different native NGU head as current baseline intent");
        Equal(true, ReferenceEquals(combat, Get(inventory, "head")), "B is composed under the retained overlay before any gear mutation");
        Equal(swaps, Get(typeof(Program), "Swaps"), "B gear never flashes under the acquired claim");
        Equal(0, timeline.Count, "native swap timeline has no B or historical A detour during acquisition");
        Equal(true, ReferenceEquals(selectedGear, Get(Runtime, "_manualAdventureSelectedGear")), "B does not replace selected overlay instances");
        Equal(false, original == successor, "B has a genuinely different native support selection from A");
        var searches = Get(typeof(PresetIntegrationChecks), "PresetSearches");
        root = Call(Library, "ReadExternalLibrary"); rows = Fixture("Json", root, "Presets");
        row = ((IEnumerable)Fixture("GetChildren", rows)).Cast<object>()
            .Single(x => Equals(PresetId, Property(Fixture("Json", x, "Id"), "Value")));
        Fixture("SetJson", row, "AutoUpdate", Fixture("Json", Fixture("JsonParse", "{\"value\":false}"), "value"));
        revision = Call(Library, "SaveExternalLibrary", root, Property(Fixture("Json", root, "__externalUiRevision"), "Value"));
        args = Fixture("JsonParse", "{\"presetId\":\"" + PresetId + "\",\"libraryRevision\":\"" + revision + "\"}");
        Call(Runtime, "EquipGlobalPreset", args, null);
        Equal(searches, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "AutoUpdate false reacquires B from its existing selected policy without search");
        Equal(false, Get(Property(Runtime, "ManualAdventureBasePreset"), "AutoUpdate"), "timeline actually holds an acquired AutoUpdate false definition");
        receipt = Get(Runtime, "_globalPresetReceipt");
        Equal(successor, Property(Get(Get(Runtime, "_manualAdventureBaseline"), "Support"), "StableKey"),
            "cached reacquisition retains raw B support independently of composed observations");
        Equal("0,5,6|4:4", Selection(), "cached B reacquisition keeps temporary support applied until removal");
        Equal("6", string.Join(",", (int[])Get(policySupport, "Beards")),
            "cached replay leaves the preceding acquired B selection unchanged");
        var acquiredB = Property(Runtime, "ManualAdventureBasePreset");
        Set(Runtime, "_globalPresetReceipt", null);
        try
        {
            Equal(true, ReferenceEquals(acquiredB, Property(Runtime, "ManualAdventureBasePreset")), "current acquired policy survives application receipt invalidation");
            Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "receipt invalidation reuses current acquired claim without replacement search");
            Equal(searches, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "AutoUpdate false receipt reset performs zero optimizer searches");
        }
        finally { Set(Runtime, "_globalPresetReceipt", receipt); }
        Equal(true, Call(Runtime, "TryApplyManualAdventureOverlay"), "manual goal composes again over B");
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "B plus overlay verifies");
        Set(Settings, "_manualAdventureEnabled", false);
        Call(Runtime, "ResetManualAdventureOverlay");
        Equal(successor, Selection(), "claim removal resolves B directly, never saved A");
        Equal(true, ReferenceEquals(b, Get(inventory, "head")), "native exit installs today's B, never A");
        Equal(1, timeline.Count, "exit needs exactly one native head swap");
        Equal(true, ReferenceEquals(b, timeline[0]), "the only post-claim swap is directly to current B");
        Equal(300L, Get(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3"), "successor composition and exit preserve unmanaged native R3 work");
        Equal(searches, Get(typeof(PresetIntegrationChecks), "PresetSearches"), "support composition and exit perform no optimizer search");
        mutation = Get(Runtime, "_supportMutation");
        Call(Runtime, "ResetManualAdventureOverlay");
        Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "verified B exit is idempotent");
        }
        finally { Set(typeof(Program), "NativeGearSwapped", null); }
    }

    private static void CheckSupportContinuation()
    {
        Setup(true); Bonuses(1.5, 2);
        var interrupted = false;
        Set(typeof(PresetIntegrationChecks), "PodInterference", (Action<object>)(pod => {
            Set(typeof(PresetIntegrationChecks), "PodInterference", null); interrupted = true;
            Equal(-1, Get(Get(Character, "adventure"), "zone"), "native support callback is behind safe retirement");
            throw new InvalidOperationException("overlay fixture partial support");
        }));
        object result;
        try { result = Call(Runtime, "ApplyManualAdventureOverlaySupport"); }
        finally { Set(typeof(PresetIntegrationChecks), "PodInterference", null); }
        Equal(true, interrupted, "timeline crosses actual native support callback");
        Equal(true, result != null, "partial support reports waiting instead of admitting combat");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "partial support leaves route retired");
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "same selected intent resumes owned partial");
        Equal("0,1,3,5|5:4", Selection(), "native partial converges to exact desired support");
        var mutation = Get(Runtime, "_supportMutation");
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "verified repeat succeeds");
        Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "verified repeat makes no committed support writes");

        Bonuses(.5, .5); Set(typeof(PresetIntegrationChecks), "FailBeardRefresh", true);
        try { result = Call(Runtime, "ApplyManualAdventureOverlaySupport"); }
        finally { Set(typeof(PresetIntegrationChecks), "FailBeardRefresh", false); }
        // The model may be partial when rendering interrupts a multi-domain change;
        // either way retry must use the retained native evidence, not undo it.
        Equal(null, Call(Runtime, "ApplyManualAdventureOverlaySupport"), "render failure converges without a restore pass");
        Equal("1,3,5,6|4:4", Selection(), "render continuation verifies exact current intent");
        Unchanged(true);
    }

    private static void CheckPermissionLoss(bool foreign)
    {
        Setup(true); Bonuses(1.5, 2); var fired = false;
        Set(typeof(PresetIntegrationChecks), "PodInterference", (Action<object>)(pod => {
            Set(typeof(PresetIntegrationChecks), "PodInterference", null); fired = true;
            if (foreign) Fixture("SetActiveBeards", new int[] { 3 });
            else Set(Runtime, "_manualAdventureSetupActive", false);
        }));
        object result;
        try { result = Call(Runtime, "ApplyManualAdventureOverlaySupport"); }
        finally { Set(typeof(PresetIntegrationChecks), "PodInterference", null); }
        Equal(true, fired, "permission timeline reaches actual native callback");
        Equal(true, result != null, "callback revocation does not verify the claim");
        var support = Selection(); var mutation = Get(Runtime, "_supportMutation");
        Set(Settings, "_manualAdventureEnabled", false); Call(Runtime, "ResetManualAdventureOverlay");
        Set(Settings, "_manageMagic", true); Set(Settings, "_manualAdventureEnabled", true);
        Set(Runtime, "_manualAdventureSetupActive", true);
        Equal(true, Call(Runtime, "ApplyManualAdventureOverlaySupport") != null, "reset and permission toggle cannot erase unresolved support evidence");
        Equal(support, Selection(), "unresolved callback preserves observed native support");
        Equal(true, ReferenceEquals(mutation, Get(Runtime, "_supportMutation")), "unresolved callback retry performs no new support writes");
        Unchanged(true);
        var root = Call(Library, "ReadExternalLibrary");
        var args = Fixture("JsonParse", "{\"presetId\":\"" + PresetId + "\",\"libraryRevision\":\""
            + Property(Fixture("Json", root, "__externalUiRevision"), "Value") + "\"}");
        Call(Runtime, "EquipGlobalPreset", args, null);
        Equal(null, Property(Runtime, "ManualAdventureSupportConflictReason"), "explicit freshly preflighted Equip grants reviewed current support authority");
        Equal(true, Get(Runtime, "_globalPresetReceipt") != null, "reviewed successor actually verifies native acquisition");
    }

    private static void Bonuses(double beard, double digger)
    {
        var beards = new double[7]; beards[0] = beard; beards[5] = beard > 0 ? 2.5 : 0;
        var diggers = new double[12]; diggers[5] = digger;
        Set(Settings, "_manualAdventureBeardWeights", beards); Set(Settings, "_manualAdventureDiggerWeights", diggers);
    }
    private static string Selection() => (string)Property(Property(Call(MainType, "CaptureSupportState"), "Selection"), "StableKey");
    private static string Resources() => Call(MainType, "GlobalPresetAllocationState") + "|" + Get(Character, "curEnergy") + "|" + Get(Character, "idleEnergy")
        + "|" + Get(Get(Character, "magic"), "curMagic") + "|" + Get(Get(Character, "magic"), "idleMagic")
        + "|" + Get(Get(Character, "res3"), "curRes3") + "|" + Get(Get(Character, "res3"), "idleRes3");
    private static string Counters() => Get(typeof(Program), "Recalls") + ":" + Get(typeof(Program), "MagicRecalls") + ":" + Get(typeof(Program), "Swaps");
    private static void Unchanged(bool preset, int expectedSwaps = 0)
    {
        Equal(ResourceBaseline, Resources(), "overlay leaves allocations, E/M/R3 totals and idle untouched");
        var counters = CounterBaseline.Split(':');
        counters[2] = (int.Parse(counters[2]) + expectedSwaps).ToString();
        Equal(string.Join(":", counters), Counters(), "overlay never recalls and executes only the expected gear swaps");
        if (!preset) return;
        Equal(SavedBase, Call(Call(Library, "Get", PresetId), "ToJson").ToString(), "saved base preset is unchanged");
        Equal(ExecutionBase, Call(BasePreset, "ToJson").ToString(), "acquired base preset is unchanged");
        Equal(HistoricalBase, Get(Runtime, "_loadoutPresetSnapshot").ToString(), "applied preset history is unchanged");
    }
    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error)
        {
            Failures++;
            Console.Error.WriteLine("FAIL: " + name + "\n" + error);
            Diagnostics();
        }
    }
    private static void Diagnostics()
    {
        // Independent read-only probes: a missing native dependency must not hide
        // the original named failure or prevent the remaining useful observations.
        Probe("fixture", () => PresetId);
        Probe("zone", () => Get(Get(Character, "adventure"), "zone"));
        Probe("support", () => Selection());
        Probe("supportOutcome", () => Get(Get(Runtime, "_presetSupportOutcome"), "Disposition"));
        Probe("supportContinuation", () => Get(Runtime, "_presetSupportContinuation") != null);
        Probe("supportConflict", () => Property(Runtime, "ManualAdventureSupportConflictReason"));
        Probe("receiptOwner", () => Get(Get(Runtime, "_globalPresetReceipt"), "Owner"));
        Probe("baselineSupport", () => {
            var basis = BaselineSupport();
            return string.Join(",", (int[])Get(basis, "Beards")) + "|" + string.Join(",",
                ((IEnumerable)Get(basis, "Diggers")).Cast<object>().Select(x => Get(x, "Id") + ":" + Get(x, "Level")));
        });
        Probe("baseIdentity", () => Property(Runtime, "ManualAdventureBaseIdentity"));
        Probe("overlayKey", () => Get(Runtime, "_manualAdventureOverlayKey"));
        Probe("supportKey", () => Get(Runtime, "_manualAdventureSupportKey"));
        Probe("resources", () => Resources());
        Probe("recalls:magicRecalls:swaps", () => Counters());
        foreach (var field in new[] { "PresetSearches", "PresetCaptures", "ResourceTransfers", "PodRefreshCalls", "AdventureZoneEntries" })
            Probe(field, () => Get(typeof(PresetIntegrationChecks), field));
        Probe("coordination", () => Call(Runtime, "GetResourceCoordinationRuntime"));
        Probe("stageEntry", () => Property(Get(Runtime, "_protectedScheduler"), "Entry"));
        Probe("stageDeadline", () => Property(Get(Runtime, "_protectedScheduler"), "ExpectedEndSeconds"));
        Probe("stageArmed", () => Get(Get(Runtime, "_protectedScheduler"), "_armed"));
    }
    private static void Probe(string name, Func<object> read)
    {
        try { Console.Error.WriteLine("  " + name + "=" + (read()?.ToString() ?? "<null>")); }
        catch (Exception error) { Console.Error.WriteLine("  " + name + "=<unavailable: " + error.GetBaseException().Message + ">"); }
    }
    private static object Fixture(string name, params object[] args) => Call(typeof(PresetIntegrationChecks), name, args);
    private static object Call(object target, string name, params object[] args)
    {
        try { return typeof(PresetIntegrationChecks).GetMethod("Invoke", All).Invoke(null, new object[] { target, name, args }); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static object Get(object target, string name) => Fixture("Get", target, name);
    private static void Set(object target, string name, object value) => Fixture("Set", target, name, value);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, All).GetValue(target);
    private static void Equal(object expected, object actual, string message)
    {
        Assertions++;
        if (!Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
}
