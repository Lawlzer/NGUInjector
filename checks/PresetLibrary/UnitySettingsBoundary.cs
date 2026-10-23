using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace System.Windows.Forms
{
    internal static class NamespaceStub { }
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    // Only Unity serialization is substituted. Missing fields are zeroed, as in the game.
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions {IncludeFields = true};
        public static string ToJson(object value, bool pretty = false)
        {
            return JsonSerializer.Serialize(value.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(field => Attribute.IsDefined(field, typeof(SerializeField)))
                .ToDictionary(field => field.Name, field => field.GetValue(value)), Options);
        }

        public static T FromJson<T>(string text)
        {
            using (var document = JsonDocument.Parse(text))
            {
                var instance = Activator.CreateInstance(typeof(T), new object[] {null});
                foreach (var field in typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(field => Attribute.IsDefined(field, typeof(SerializeField))))
                    field.SetValue(instance, document.RootElement.TryGetProperty(field.Name, out var value)
                        ? value.Deserialize(field.FieldType, Options)
                        : field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null);
                return (T) instance;
            }
        }
    }
}
