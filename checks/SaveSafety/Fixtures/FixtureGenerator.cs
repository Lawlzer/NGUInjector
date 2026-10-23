// Offline test-only serializer. Input is a destination directory, never a save or an object graph.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;

internal static class FixtureGenerator
{
    private const string GameAssembly = "Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
    private static string directory;
    private static int count;

    [Serializable]
    private sealed class Record : ISerializable
    {
        internal string Name;
        internal string Assembly = GameAssembly;
        internal readonly List<KeyValuePair<string, object>> Fields = new List<KeyValuePair<string, object>>();

        internal Record(string name) { Name = name; }
        private Record(SerializationInfo info, StreamingContext context) { throw new InvalidOperationException("Must never deserialize fixtures."); }

        internal Record Set(string name, object value)
        {
            Remove(name);
            Fields.Add(new KeyValuePair<string, object>(name, value));
            return this;
        }

        internal Record Remove(string name)
        {
            Fields.RemoveAll(field => field.Key == name);
            return this;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.FullTypeName = Name;
            info.AssemblyName = Assembly;
            foreach (var field in Fields) info.AddValue(field.Key, field.Value);
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Supply a new temporary fixture directory.");
        directory = Path.GetFullPath(args[0]);
        if (Directory.Exists(directory)) throw new IOException("Refusing to overwrite an existing fixture directory.");
        Directory.CreateDirectory(directory);

        PlayerCase("valid", Player(), true);
        PlayerCase("minimum-version", Player().Set("version", 361), true);
        PlayerCase("zero-clocks", Player().Set("totalPlaytime", Clock(0d)).Set("rebirthTime", Clock(0d))
            .Set("stats", Stats(0L)).Set("lastTime", 0).Set("playerName", ""), true);
        PlayerCase("large-finite-clocks", Player().Set("totalPlaytime", Clock(double.MaxValue))
            .Set("rebirthTime", Clock(double.MaxValue)).Set("stats", Stats(long.MaxValue)), true);
        var modData = new Record("UnknownMod.WithCallbacks") { Assembly = "DeliberatelyMissing.ModAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" };
        modData.Set("nested", new object[] { null, "test", new int[] { 1, 2, 3 } });
        modData.Set("cycle", modData);
        PlayerCase("extra-mod-data", Player().Set("Data", modData), true);

        foreach (int version in new[] { int.MinValue, -1, 0, 360, 1261, int.MaxValue })
            PlayerCase("version-" + version, Player().Set("version", version), false);
        foreach (string field in new[] { "version", "lastTime", "totalPlaytime", "rebirthTime", "stats", "playerName" })
        {
            PlayerCase("missing-" + field, Player().Remove(field), false);
            PlayerCase("null-" + field, Player().Set(field, null), false);
            PlayerCase("wrong-type-" + field, Player().Set(field, field == "playerName" ? (object)123 : "not-the-required-type"), false);
        }
        PlayerCase("version-int64", Player().Set("version", 1260L), false);
        PlayerCase("lastTime-int64", Player().Set("lastTime", 1720000000L), false);
        PlayerCase("lastTime-negative", Player().Set("lastTime", -1), false);
        PlayerCase("rebirth-negative", Player().Set("stats", Stats(-1L)), false);
        PlayerCase("rebirth-int32", Player().Set("stats", Stats(7)), false);
        PlayerCase("rebirth-null", Player().Set("stats", Stats(null)), false);
        PlayerCase("rebirth-missing", Player().Set("stats", new Record("Stats")), false);
        PlayerCase("rebirth-exceeds-total", Player().Set("rebirthTime", Clock(1000.251)), false);
        foreach (string field in new[] { "totalPlaytime", "rebirthTime" })
        {
            PlayerCase("missing-seconds-" + field, Player().Set(field, new Record("PlayerTime")), false);
            PlayerCase("null-seconds-" + field, Player().Set(field, Clock(null)), false);
            PlayerCase("int-seconds-" + field, Player().Set(field, Clock(1)), false);
            PlayerCase("single-seconds-" + field, Player().Set(field, Clock(1f)), false);
            int index = 0;
            foreach (double seconds in new[] { -0.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                PlayerCase("invalid-clock-" + field + "-" + index++, Player().Set(field, Clock(seconds)), false);
            PlayerCase("unknown-clock-type-" + field, Player().Set(field, new Record("Other.PlayerTime").Set("totalseconds", 1d)), false);
        }

        var wrongRoot = Player();
        wrongRoot.Name = "Other.PlayerData";
        PlayerCase("unknown-player-root", wrongRoot, false);
        wrongRoot = Player();
        wrongRoot.Assembly = "UnknownGame, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        PlayerCase("unknown-player-assembly", wrongRoot, false);
        PlayerCase("unknown-stats-type", Player().Set("stats", new Record("Other.Stats").Set("rebirthNumber", 7L)), false);
        var wrongClockAssembly = Clock(1d);
        wrongClockAssembly.Assembly = "UnknownGame, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        PlayerCase("unknown-clock-assembly", Player().Set("totalPlaytime", wrongClockAssembly), false);
        PlayerCase("primitive-player-root", "PlayerData", false);
        PlayerCase("array-player-root", new int[] { 1, 2 }, false);

        byte[] player = Serialize(Player());
        foreach (string field in new[] { "playerData", "checksum" })
        {
            Emit("missing-wrapper-" + field, Wrap(player).Remove(field), false);
            Emit("null-wrapper-" + field, Wrap(player).Set(field, null), false);
            Emit("wrong-wrapper-" + field, Wrap(player).Set(field, 123), false);
        }
        var wrapper = Wrap(player);
        wrapper.Name = "Other.SaveData";
        Emit("unknown-wrapper", wrapper, false);
        wrapper = Wrap(player);
        wrapper.Name = "jshepler.ngu.mods.ModSave.SaveData";
        Emit("unverified-wrapper-subclass", wrapper, false);
        wrapper = Wrap(player);
        wrapper.Assembly = "UnknownGame, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        Emit("unknown-wrapper-assembly", wrapper, false);
        Emit("primitive-wrapper-root", "SaveData", false);
        Emit("array-wrapper-root", new int[] { 1 }, false);
        Emit("extra-wrapper-fields", Wrap(player).Set("extra", modData), true);
        Emit("wrong-checksum", Wrap(player).Set("checksum", Convert.ToBase64String(new byte[16])), false);
        Emit("short-checksum", Wrap(player).Set("checksum", Convert.ToBase64String(new byte[15])), false);
        Emit("long-checksum", Wrap(player).Set("checksum", Convert.ToBase64String(new byte[17])), false);
        Emit("malformed-checksum", Wrap(player).Set("checksum", new string('!', 24)), false);
        Emit("malformed-inner-base64", Wrap(player).Set("playerData", "!invalid!"), false);
        Emit("empty-inner", Wrap(new byte[0]), false);
        Emit("malformed-inner-nrbf", Wrap(new byte[] { 0, 1, 2, 3 }), false);
        Emit("truncated-inner-nrbf", Wrap(Slice(player, player.Length - 1)), false);
        Emit("trailing-inner-nrbf", Wrap(Append(player, new byte[] { 0 })), false);
        byte[] changed = (byte[])player.Clone();
        changed[changed.Length - 1] ^= 1;
        Emit("tampered-inner-checksum", Wrap(player).Set("playerData", Convert.ToBase64String(changed)), false);
        byte[] outer = Serialize(Wrap(player));
        EmitBytes("truncated-outer-nrbf", Encoding.ASCII.GetBytes(Convert.ToBase64String(Slice(outer, outer.Length - 1))), false);
        EmitBytes("trailing-outer-nrbf", Encoding.ASCII.GetBytes(Convert.ToBase64String(Append(outer, new byte[] { 0 }))), false);
        EmitBytes("malformed-outer-base64", Encoding.ASCII.GetBytes("not a save!"), false);
        EmitBytes("empty-file", new byte[0], false);
        EmitBytes("invalid-utf8", new byte[] { 0xc0, 0xaf }, false);
        EmitBytes("whitespace", Encoding.ASCII.GetBytes(" \r\n" + Convert.ToBase64String(outer) + "\t\n"), true);

        // Bridge scenarios stay outside the codec suite's fixed root fixture counts.
        string originalDirectory = directory;
        directory = Path.Combine(directory, "bridge");
        Directory.CreateDirectory(directory);
        PlayerCase("earlier", Player().Set("totalPlaytime", Clock(100d)).Set("rebirthTime", Clock(10d)).Set("stats", Stats(6L)), true);
        PlayerCase("tied-branch", Player().Set("rebirthTime", Clock(20d)).Set("stats", Stats(8L)), true);
        PlayerCase("other-player", Player().Set("playerName", "Other Synthetic Player"), true);
        directory = originalDirectory;

        Console.WriteLine("Generated " + count + " trusted synthetic fixtures in " + directory);
        return 0;
    }

    private static Record Player()
    {
        return new Record("PlayerData").Set("version", 1260).Set("lastTime", 1720000000)
            .Set("totalPlaytime", Clock(1000.25)).Set("rebirthTime", Clock(60.5)).Set("stats", Stats(7L)).Set("playerName", "Synthetic Player");
    }

    private static Record Clock(object seconds) { return new Record("PlayerTime").Set("totalseconds", seconds); }
    private static Record Stats(object rebirth) { return new Record("Stats").Set("rebirthNumber", rebirth); }

    private static Record Wrap(byte[] player)
    {
        using (var md5 = MD5.Create())
            return new Record("SaveData").Set("playerData", Convert.ToBase64String(player)).Set("checksum", Convert.ToBase64String(md5.ComputeHash(player)));
    }

    private static byte[] Serialize(object graph)
    {
        using (var stream = new MemoryStream())
        {
            new BinaryFormatter().Serialize(stream, graph);
            return stream.ToArray();
        }
    }

    private static void PlayerCase(string name, object player, bool valid) { Emit(name, Wrap(Serialize(player)), valid); }
    private static void Emit(string name, object wrapper, bool valid) { EmitBytes(name, Encoding.ASCII.GetBytes(Convert.ToBase64String(Serialize(wrapper))), valid); }

    private static void EmitBytes(string name, byte[] bytes, bool valid)
    {
        File.WriteAllBytes(Path.Combine(directory, name + (valid ? ".ok.txt" : ".bad.txt")), bytes);
        count++;
    }

    private static byte[] Slice(byte[] bytes, int count)
    {
        var result = new byte[count];
        Buffer.BlockCopy(bytes, 0, result, 0, count);
        return result;
    }

    private static byte[] Append(byte[] bytes, byte[] suffix)
    {
        var result = new byte[bytes.Length + suffix.Length];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        Buffer.BlockCopy(suffix, 0, result, bytes.Length, suffix.Length);
        return result;
    }
}
