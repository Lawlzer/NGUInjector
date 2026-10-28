using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

// Data and command-file boundaries only. Production selection, claims, grants,
// preparation attempts, command admission and native boss leaves remain real.
public static class NativeFightFixture
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static string LastCommandMessage;

    public static void ObserveCommandMessage(string message) { LastCommandMessage = message; }

    internal static void InitializeMain(object main)
    {
        // Uninitialized Unity fixtures skip these managed field initializers.
        foreach (var name in new[] { "_knownSaves", "_saveScanErrors", "_saveScanStamps", "_saveScanPending", "_saveHighWaterHashes" })
        {
            var field = Field(main, name);
            field.SetValue(main, Activator.CreateInstance(field.FieldType));
        }
    }

    public static void ObserveFiniteResult(object result)
    {
        if ((bool)Get(result, "Committed") && (bool)Get(result, "AuthorityCurrent")) return;
        Console.WriteLine("Native finite result: " + Get(result, "Disposition") + "; invoked=" + Get(result, "Invoked")
            + "; committed=" + Get(result, "Committed") + "; authorityCurrent=" + Get(result, "AuthorityCurrent")
            + "; reason=" + Get(result, "Reason") + "; error=" + Get(result, "Error"));
        var character = Get(result.GetType().DeclaringType, "Character"); var adventure = Get(character, "adventure");
        Console.WriteLine("Native pill post-state: blood=" + Get(Get(character, "bloodMagic"), "bloodPoints") + "; "
            + string.Join("; ", new[] { "attack", "defense", "maxHP", "regen" }.Select(name => name + "=" + ((float)Get(adventure, name)).ToString("R")
                + "/0x" + BitConverter.ToInt32(BitConverter.GetBytes((float)Get(adventure, name)), 0).ToString("x8"))));
    }

    public static void ObservePillProof(object action)
    {
        var character = Get(action.GetType().DeclaringType, "Character"); var adventure = Get(character, "adventure");
        var worth = (float)Get(action, "Worth");
        var expected = new[] { (float)Get(action, "Power") + worth, (float)Get(action, "Toughness") + worth,
            (float)Get(action, "Health") + worth * 3f, (float)Get(action, "Regen") + worth * .03f };
        var actual = new[] { "attack", "defense", "maxHP", "regen" }.Select(name => (float)Get(adventure, name)).ToArray();
        Console.WriteLine("Native pill binary32 stored proof: expected=" + string.Join(",", expected.Select(value => BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("x8")))
            + "; actual=" + string.Join(",", actual.Select(value => BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("x8")))
            + "; exact=" + expected.SequenceEqual(actual));
    }

    internal static void Prepare(Assembly assembly, object character)
    {
        IronPillSetupBoundary.Enabled = true;
        IronPillReservationChecks.PrepareSetupObservation(assembly, character);
        Set(Get(character, "allDiggers"), "baseGoldCost", Enumerable.Repeat(1e200, 12).ToList());
        Need(character, "machine"); Need(character, "magic"); Need(character, "settings");
        foreach (var name in new[] { "timeMachineController", "wandoos98Controller", "NGUController", "wishesController", "bloodMagicController", "bossController" })
            Set(Need(character, name), "character", character);
        var ngu = Get(character, "NGUController");
        foreach (var name in new[] { "NGU", "NGUMagic" })
            if (Get(ngu, name) == null) Set(ngu, name, Array.CreateInstance(Field(ngu, name).FieldType.GetElementType(), 0));
        var training = Need(character, "training");
        Set(training, "_attackEnergy", new long[6]); Set(training, "_defenseEnergy", new long[6]);
        var at = Need(character, "advancedTraining");
        foreach (var name in new[] { "level", "energy", "levelTarget" }) if (Get(at, name) == null) Set(at, name, new long[5]);
        Need(character, "bloodMagic");
        Need(character, "wandoos98");
        Set(character, "bossID", 100); Set(character, "highestBoss", 100);
        Set(character, "curHP", 1f);
        var inventory = Get(character, "inventory");
        var slots = new[] { "head", "chest", "legs", "boots", "weapon" };
        var parts = new[] { "Head", "Chest", "Legs", "Boots", "Weapon" };
        for (var i = 0; i < slots.Length; i++)
        {
            var item = Get(inventory, slots[i]); Set(item, "id", 40 + i); Set(item, "bossRequired", 1); Set(item, "removable", true);
            Set(item, "type", Enum.Parse(item.GetType().GetField("type", All).FieldType, parts[i]));
        }
        Set(Get(Get(character, "inventoryController"), "itemInfo"), "itemName", Enumerable.Range(0, 45).Select(i => "Fixture " + i).ToArray());
        var challenges = Get(character, "challenges");
        Child(challenges, "noEquipmentChallenge");
        Child(Get(character, "wandoos98"), "bootupTime");
        Set(Get(Get(character, "buttons"), "beards"), "m_Interactable", true);
        var adventure = Get(character, "adventure"); Set(adventure, "zone", -1);
        var controller = Child(character, "adventureController"); Set(controller, "character", character);
        var player = Child(controller, "playerController"); var enemy = Child(controller, "enemyAI"); Set(enemy, "player", player);
        Child(controller, "log"); Child(controller, "itopodStartInput"); Child(controller, "itopodEndInput");
        var selector = Child(controller, "zoneSelector"); Set(selector, "character", character); Set(selector, "ac", controller);
        Set(inventory, "macguffinBonuses", Enumerable.Repeat(1f, 30).ToList());
        Rows(Need(adventure, "itopod"), "perkLevel", 512);
        var perks = Child(controller, "itopod"); Set(perks, "character", character);
        Rows(perks, "effectPerLevel", 512); Rows(perks, "perkDifficultyReq", 512);
        var quirks = Need(character, "beastQuestPerkController"); Set(quirks, "character", character);
        Rows(quirks, "effectPerLevel", 512); Rows(quirks, "quirkDifficultyReq", 512);
        Rows(Get(character, "wishesController"), "properties", 512);
        Rows(Need(character, "wishes"), "wishes", 512);
        var arbitrary = Need(character, "arbitrary");
        Need(arbitrary, "energyPotion1Time"); Need(arbitrary, "magicPotion1Time");
        Need(character, "allArbitrary");
        var fruits = Get(character, "yggdrasil").GetType().GetField("fruits", All);
        var rows = (IList)Activator.CreateInstance(fruits.FieldType);
        for (var i = 0; i < 18; i++) rows.Add(New(fruits.FieldType.GetGenericArguments()[0]));
        fruits.SetValue(Get(character, "yggdrasil"), rows);
        var ygg = Child(character, "yggdrasilController"); Set(ygg, "character", character);
        var fruitType = ygg.GetType().GetField("fruits", All).FieldType.GetElementType();
        var controllers = Array.CreateInstance(fruitType, 1); var fruit = New(fruitType); Set(fruit, "character", character);
        controllers.SetValue(fruit, 0); Set(ygg, "fruits", controllers);
        Rows(Need(character, "beastQuest"), "quirkLevel", 512);
        var main = assembly.GetType("NGUInjector.Main", true);
        var equipped = main.GetMethod("CombatPrepEquipped", All).Invoke(null, null);
        Set(Get(character, "inventoryController"), "bonuses", main.GetMethod("CombatPrepGearBonuses", All).Invoke(null, new[] { equipped }));
    }

    internal static string Request(object main, string directory, string command, string opportunity)
    {
        var type = main.GetType();
        var names = new[] { "_externalUiCommandsDir", "_externalUiCommandResultsDir", "_externalUiClaimsDir" };
        var before = names.Select(name => Get(type, name)).ToArray();
        var started = Get(main, "_started"); var originalLease = Get(main, "_injectorLease");
        var commands = Path.Combine(directory, "commands"); var results = Path.Combine(directory, "command-results"); var claims = Path.Combine(commands, "claimed");
        Directory.CreateDirectory(commands); Directory.CreateDirectory(results); Directory.CreateDirectory(claims);
        var lease = originalLease as FileStream;
        try
        {
            if (lease == null) lease = (FileStream)type.Assembly.GetType("NGUInjector.JsonFiles", true).GetMethod("AcquireInjectorLease", All)
                .Invoke(null, new object[] { directory, 0 });
            Set(main, "_injectorLease", lease); Set(main, "_started", true);
            Set(type, names[0], commands); Set(type, names[1], results); Set(type, names[2], claims);
            var path = Path.Combine(commands, opportunity + ".json");
            File.WriteAllText(path, "{\"id\":\"" + opportunity + "\",\"command\":\"" + command + "\",\"args\":{}}");
            LastCommandMessage = null;
            type.GetMethod("ProcessExternalUiCommand", All).Invoke(main, new object[] { path });
            var writer = Get(type, "OutputWriter") as StreamWriter;
            var log = writer?.BaseStream as MemoryStream;
            var diagnostic = "; actual callback: " + LastCommandMessage
                + (log == null ? "" : "; native log: " + System.Text.Encoding.UTF8.GetString(log.ToArray()));
            var resultPath = Path.Combine(results, opportunity + ".json");
            if (!File.Exists(resultPath)) throw new InvalidOperationException("No durable native command result was published" + diagnostic);
            var result = type.Assembly.GetType("SimpleJSON.JSON", true).GetMethod("Parse", All)
                .Invoke(null, new object[] { File.ReadAllText(resultPath) });
            var indexer = result.GetType().GetProperty("Item", new[] { typeof(string) });
            var success = indexer.GetValue(result, new object[] { "success" });
            var message = indexer.GetValue(result, new object[] { "message" }).GetType().GetProperty("Value", All)
                .GetValue(indexer.GetValue(result, new object[] { "message" })).ToString();
            if (!(bool)success.GetType().GetProperty("AsBool", All).GetValue(success))
                throw new InvalidOperationException(message + diagnostic);
            if (Get(main, "_currentExecutionCommandId") != null || !File.Exists(Path.Combine(claims, opportunity + ".claimed")) || File.Exists(path))
                throw new InvalidOperationException("Native request did not retain its command-ID claim or clear its dispatch scope.");
            var resultBytes = File.ReadAllText(resultPath);
            var attempt = Get(main, "_fightPreparationAttempt"); var process = Get(main, "_bossPushProcess");
            File.WriteAllText(path, "{\"id\":\"" + opportunity + "\",\"command\":\"" + command + "\",\"args\":{}}");
            LastCommandMessage = null;
            type.GetMethod("ProcessExternalUiCommand", All).Invoke(main, new object[] { path });
            if (File.Exists(path) || LastCommandMessage != null || File.ReadAllText(resultPath) != resultBytes
                || !ReferenceEquals(attempt, Get(main, "_fightPreparationAttempt")) || !ReferenceEquals(process, Get(main, "_bossPushProcess")))
                throw new InvalidOperationException("A duplicate command ID replayed native preparation or rewrote its result.");
            return message;
        }
        finally
        {
            Set(main, "_started", started); Set(main, "_injectorLease", originalLease);
            for (var i = 0; i < names.Length; i++) Set(type, names[i], before[i]);
            if (originalLease == null) lease?.Dispose();
        }
    }

    private static object New(Type type)
    {
        var value = FormatterServices.GetUninitializedObject(type);
        for (var current = type; current != null; current = current.BaseType)
        {
            var pointer = current.GetField("m_CachedPtr", All);
            if (pointer != null) { pointer.SetValue(value, new IntPtr(1)); break; }
        }
        return value;
    }
    private static object Get(object owner, string name) => Field(owner, name).GetValue(owner is Type ? null : owner);
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner is Type ? null : owner, value);
    private static FieldInfo Field(object owner, string name)
    {
        for (var type = owner as Type ?? owner.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, All); if (field != null) return field; }
        throw new MissingFieldException(name);
    }
    private static object Child(object owner, string name)
    { var child = New(Field(owner, name).FieldType); Set(owner, name, child); return child; }
    private static object Need(object owner, string name) => Get(owner, name) ?? Child(owner, name);
    private static void Rows(object owner, string name, int count)
    {
        var type = Field(owner, name).FieldType; var element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
        var existing = Get(owner, name) as IList;
        if (existing != null && existing.Count >= count) return;
        var values = type.IsArray ? (IList)Array.CreateInstance(element, count) : existing ?? (IList)Activator.CreateInstance(type);
        if (type.IsArray && existing != null) for (var i = 0; i < existing.Count; i++) values[i] = existing[i];
        for (var i = type.IsArray ? existing?.Count ?? 0 : values.Count; i < count; i++)
        {
            var value = element.IsValueType ? Activator.CreateInstance(element) : New(element);
            if (type.IsArray) values[i] = value; else values.Add(value);
        }
        Set(owner, name, values);
    }
}
