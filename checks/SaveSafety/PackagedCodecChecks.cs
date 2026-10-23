using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

internal static class PackagedCodecChecks
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] Dependencies = {
        "System.Formats.Nrbf", "Microsoft.Bcl.HashCode", "System.Reflection.Metadata", "System.Collections.Immutable",
        "System.Memory", "System.Buffers", "System.Runtime.CompilerServices.Unsafe", "System.Numerics.Vectors", "System.ValueTuple"
    };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Pass the built plugin and a native-format fixture. No game process is used.");
            var bytes = File.ReadAllBytes(args[0]);
            var save = File.ReadAllBytes(args[1]);
            string before;
            using (var hash = SHA256.Create()) before = BitConverter.ToString(hash.ComputeHash(save)).Replace("-", "").ToLowerInvariant();
            // Match SMI's important property: the plugin has no sibling probing path.
            var plugin = Assembly.Load(bytes);
            Require(string.IsNullOrEmpty(plugin.Location), "Plugin must be loaded from bytes.");
            using (var hash = SHA256.Create())
                Console.WriteLine("Plugin MVID=" + plugin.ManifestModule.ModuleVersionId + "; SHA256=" + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            var resources = plugin.GetManifestResourceNames();
            foreach (var name in Dependencies)
                Require(resources.Contains("NGUInjector.Dependencies." + name + ".dll"), "Missing embedded dependency: " + name);
            var resolver = plugin.GetType("NGUInjector.SaveSafetyDependencies", true);
            resolver.GetMethod("Register", All).Invoke(null, null);
            // Tiny fixtures need not exercise every parser branch. Probe the full
            // packaged closure as well, using identities from its own metadata.
            foreach (var name in Dependencies)
                using (var resource = plugin.GetManifestResourceStream("NGUInjector.Dependencies." + name + ".dll"))
                using (var payload = new MemoryStream())
                {
                    resource.CopyTo(payload);
                    var embeddedName = Assembly.ReflectionOnlyLoad(payload.ToArray()).FullName;
                    var loaded = (Assembly)resolver.GetMethod("Resolve", All).Invoke(null, new object[] { null, new ResolveEventArgs(embeddedName, plugin) });
                    Require(loaded != null, "Packaged dependency did not resolve: " + name);
                    var repeated = resolver.GetMethod("Resolve", All).Invoke(null, new object[] { null, new ResolveEventArgs(embeddedName, plugin) });
                    Require(ReferenceEquals(loaded, repeated), "Repeated resolution changed assembly identity: " + name);
                }
            var codec = plugin.GetType("NGUInjector.SaveSafetyCatalog", true);
            var identity = codec.GetMethod("Read", All).Invoke(null, new object[] { Path.GetFullPath(args[1]), 1260 });
            Require((string)identity.GetType().GetProperty("Sha256", All).GetValue(identity) == before, "Read-back fixture hash differs.");
            foreach (var name in Dependencies.Where(x => x != "System.ValueTuple"))
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.ReflectionOnly && x.GetName().Name == name).ToArray();
                Require(loaded.Length > 0 && loaded.All(x => string.IsNullOrEmpty(x.Location) && !x.GlobalAssemblyCache), "Dependency escaped embedded loading: " + name);
            }
            var tupleName = "System.ValueTuple, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
            var tuple = (Assembly)resolver.GetMethod("Resolve", All).Invoke(null, new object[] { null, new ResolveEventArgs(tupleName, plugin) });
            Require(tuple != null && tuple.GetName().Name == "System.ValueTuple", "Tuple facade did not resolve.");
            Require(BitConverter.ToString(tuple.GetName().GetPublicKeyToken()).Replace("-", "").ToLowerInvariant() == "cc7b13ffcd2ddd51", "Unexpected tuple signing identity.");
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "Assembly-CSharp" || x.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal)), "Codec unexpectedly loaded game or Unity assemblies.");
            using (var hash = SHA256.Create()) Require(before == BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(args[1]))).Replace("-", "").ToLowerInvariant(), "Fixture was modified.");
            Console.WriteLine("PASS: byte-loaded net48 packaged codec, 9 embedded dependencies, 8 memory-loaded libraries, native checksum/hash read-back. No game actions; Unity Mono remains unverified.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
