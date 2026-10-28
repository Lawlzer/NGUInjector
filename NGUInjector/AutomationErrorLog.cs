using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SimpleJSON;

namespace NGUInjector
{
    internal static class AutomationErrorLog
    {
        private sealed class Entry
        {
            internal string Context, Message, Details;
            internal DateTime FirstSeen, LastSeen;
            internal int Count;
        }

        private static readonly object Gate = new object();
        private static readonly List<Entry> Entries = new List<Entry>();

        internal static void Clear()
        {
            lock (Gate) Entries.Clear();
        }

        internal static void Record(string context, Exception error)
        {
            context = Limit(context ?? "Automation", 160);
            var message = Limit(error.Message, 1024);
            var details = Limit(error.ToString(), 4096);
            var now = DateTime.UtcNow;
            lock (Gate)
            {
                var entry = Entries.Find(x => x.Context == context && x.Message == message);
                if (entry == null)
                {
                    if (Entries.Count == 16) Entries.RemoveAt(0);
                    entry = new Entry { Context = context, Message = message, FirstSeen = now };
                }
                else Entries.Remove(entry);
                entry.LastSeen = now;
                entry.Details = details;
                if (entry.Count < int.MaxValue) entry.Count++;
                Entries.Add(entry);
            }
        }

        internal static JSONArray Snapshot()
        {
            lock (Gate)
            {
                var result = new JSONArray();
                var bytes = 2;
                for (var i = Entries.Count - 1; i >= 0; i--)
                {
                    var entry = Entries[i];
                    var row = new JSONObject();
                    row["context"] = entry.Context; row["message"] = entry.Message;
                    row["details"] = entry.Details; row["count"] = entry.Count;
                    row["firstSeenAt"] = entry.FirstSeen.ToString("o", CultureInfo.InvariantCulture);
                    row["lastSeenAt"] = entry.LastSeen.ToString("o", CultureInfo.InvariantCulture);
                    // Error history shares the bounded command-result envelope with runtime state.
                    var rowBytes = Encoding.UTF8.GetByteCount(row.ToString()) + (result.Count == 0 ? 0 : 1);
                    if (bytes + rowBytes > 32 * 1024) break;
                    bytes += rowBytes;
                    result.Add(row);
                }
                return result;
            }
        }

        private static string Limit(string value, int length) => value.Length <= length ? value : value.Substring(0, length);
    }
}
