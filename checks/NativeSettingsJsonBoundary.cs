using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using Mono.Cecil;

// Unity's serializer is unavailable in the desktop CLR. Only its field codec is
// substituted; production validation, atomic IO and publication remain untouched.
public static class NativeSettingsJsonBoundary
{
    public static int Reads, Writes;

    internal static void Configure(AssemblyDefinition plugin)
    {
        foreach (var method in plugin.MainModule.GetType("NGUInjector.SavedSettings").Methods.Where(method => method.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            var called = instruction.Operand as MethodReference;
            if (called?.DeclaringType.FullName != "UnityEngine.JsonUtility") continue;
            if (called.Name == "ToJson" && called.Parameters.Count >= 1 && called.Parameters.Count <= 2)
                instruction.Operand = plugin.MainModule.ImportReference(typeof(NativeSettingsJsonBoundary).GetMethods()
                    .Single(boundary => boundary.Name == nameof(ToJson) && boundary.GetParameters().Length == called.Parameters.Count));
            else if (called.Name == "FromJson" && called is GenericInstanceMethod generic && generic.GenericArguments.Count == 1)
            {
                var replacement = new GenericInstanceMethod(plugin.MainModule.ImportReference(typeof(NativeSettingsJsonBoundary).GetMethod(nameof(FromJson))));
                replacement.GenericArguments.Add(generic.GenericArguments[0]);
                instruction.Operand = replacement;
            }
            else throw new InvalidOperationException("Unsupported Unity settings JSON boundary: " + called.FullName);
        }
    }

    private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(field => !field.IsNotSerialized && (field.IsPublic || field.GetCustomAttributesData()
            .Any(attribute => attribute.AttributeType.FullName == "UnityEngine.SerializeField"))).ToArray();

    public static string ToJson(object value) => ToJson(value, false);
    public static string ToJson(object value, bool pretty)
    {
        Writes++;
        return new JavaScriptSerializer().Serialize(Fields(value.GetType()).ToDictionary(field => field.Name, field => field.GetValue(value)));
    }

    public static T FromJson<T>(string text)
    {
        Reads++;
        var serializer = new JavaScriptSerializer();
        var values = serializer.Deserialize<Dictionary<string, object>>(text);
        // Unity zeroes missing serialized fields instead of running field initializers.
        var instance = FormatterServices.GetUninitializedObject(typeof(T));
        foreach (var field in Fields(typeof(T)))
            if (values.TryGetValue(field.Name, out var value))
                field.SetValue(instance, value == null ? null : serializer.Deserialize(serializer.Serialize(value), field.FieldType));
        return (T)instance;
    }
}
