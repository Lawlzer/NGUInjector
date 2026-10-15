using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SimpleJSON;

namespace NGUInjector
{
    internal static class JsonFiles
    {
        internal static FileStream AcquireInjectorLease(string directory, int waitMilliseconds = 5000)
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return new FileStream(Path.Combine(directory, ".injector.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException e)
                {
                    var remaining = waitMilliseconds - (int)timer.ElapsedMilliseconds;
                    if (remaining <= 0)
                        throw new IOException("Could not acquire .injector.lock within " + waitMilliseconds + "ms. The runtime directory may be in use by an injector or offline editor.", e);
                    Thread.Sleep(Math.Min(50, remaining));
                }
            }
        }

        internal static string ComputeRevision(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static readonly Regex NumberToken = new Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?");

        // Retained extension fields must not lose precision merely by passing through a double.
        private sealed class SourceNumber : JSONNumber
        {
            private string _token;
            internal SourceNumber(double number, string token) : base(number) { _token = token; }
            public override string Value
            {
                get => _token ?? base.Value;
                set { base.Value = value; _token = null; }
            }
            public override double AsDouble
            {
                get => base.AsDouble;
                set { base.AsDouble = value; _token = null; }
            }
            internal override void WriteToStringBuilder(StringBuilder builder, int indent, int indentIncrement, JSONTextMode mode)
            {
                builder.Append(Value);
            }
        }
        // SimpleJSON's legacy parser accepts truncated objects. External writes must be complete.
        internal static JSONObject ParseObject(string text)
        {
            var index = 0;
            void SkipSpace()
            {
                while (index < text.Length && " \t\r\n".IndexOf(text[index]) >= 0)
                    index++;
            }

            string ReadString()
            {
                var start = index++;
                while (index < text.Length)
                {
                    var c = text[index++];
                    if (c == '"')
                        return JSON.Parse("[" + text.Substring(start, index - start) + "]")[0].Value;
                    if (c < ' ')
                        throw new FormatException("Control character in JSON string.");
                    if (c != '\\')
                        continue;
                    if (index >= text.Length || "\"\\/bfnrtu".IndexOf(text[index]) < 0)
                        throw new FormatException("Invalid JSON escape.");
                    if (text[index++] == 'u')
                    {
                        for (var i = 0; i < 4; i++)
                        {
                            if (index >= text.Length || !Uri.IsHexDigit(text[index++]))
                                throw new FormatException("Invalid JSON unicode escape.");
                        }
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }

            JSONNode ReadValue(int depth)
            {
                SkipSpace();
                if (depth > 64 || index >= text.Length)
                    throw new FormatException("Incomplete or excessively nested JSON.");
                var c = text[index];
                if (c == '"')
                    return new JSONString(ReadString());
                if (c == '{' || c == '[')
                {
                    var isObject = c == '{';
                    JSONNode container = isObject ? (JSONNode)new JSONObject() : new JSONArray();
                    var keys = isObject ? new HashSet<string>(StringComparer.Ordinal) : null;
                    var close = isObject ? '}' : ']';
                    index++;
                    SkipSpace();
                    if (index < text.Length && text[index] == close)
                    {
                        index++;
                        return container;
                    }
                    while (true)
                    {
                        SkipSpace();
                        var key = string.Empty;
                        if (isObject)
                        {
                            if (index >= text.Length || text[index] != '"')
                                throw new FormatException("Expected a JSON property name.");
                            key = ReadString();
                            if (!keys.Add(key))
                                throw new FormatException("Duplicate JSON property: " + key);
                            SkipSpace();
                            if (index >= text.Length || text[index++] != ':')
                                throw new FormatException("Expected a JSON colon.");
                        }
                        var value = ReadValue(depth + 1);
                        // Add treats an empty object key as an anonymous value in SimpleJSON.
                        if (isObject)
                            container[key] = value;
                        else
                            container.Add(value);
                        SkipSpace();
                        if (index >= text.Length)
                            throw new FormatException("Unterminated JSON container.");
                        var separator = text[index++];
                        if (separator == close)
                            return container;
                        if (separator != ',')
                            throw new FormatException("Expected a JSON comma.");
                    }
                }
                foreach (var literal in new[] { "true", "false", "null" })
                {
                    if (string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                        continue;
                    index += literal.Length;
                    if (literal == "null")
                        return JSONNull.CreateOrGet();
                    return new JSONBool(literal == "true");
                }
                var match = NumberToken.Match(text, index);
                double number;
                if (!match.Success || !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                    || double.IsNaN(number) || double.IsInfinity(number))
                    throw new FormatException("Invalid JSON value.");
                index += match.Length;
                return new SourceNumber(number, match.Value);
            }

            var root = ReadValue(0);
            SkipSpace();
            if (index != text.Length || !root.IsObject)
                throw new FormatException("Expected one complete JSON object.");
            return root.AsObject;
        }

        internal static void WriteAtomic(string path, string text)
        {
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, text);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { /* Failed cleanup must not hide the original write error. */ }
            }
        }
    }
}
