using System;
using System.IO;

namespace NGUInjector
{
    internal partial class Main
    {
        internal static void Log(string msg)
        {
            WriteLog(OutputWriter, msg);
        }

        internal static void LogLoot(string msg)
        {
            WriteLog(LootWriter, msg);
        }

        internal static void LogCombat(string msg)
        {
            WriteLog(CombatWriter, msg);
        }

        internal static void LogPitSpin(string msg)
        {
            WriteLog(PitSpinWriter, msg);
        }

        internal static void LogAllocation(string msg)
        {
            if (Settings == null || !Settings.DebugAllocation) return;
            WriteLog(AllocationWriter, msg);
        }

        internal static string LogError(string context, Exception e)
        {
            try { AutomationErrorLog.Record(context, e); }
            catch { /* Error reporting must not interrupt automation or recurse. */ }
            var message = $"ERROR in {context}: {e.Message}";
            Log(message);
            Log(e.ToString());

            try
            {
                settingsForm?.ReportCustomError(message);
            }
            catch
            {
                // Avoid recursive error reporting if the UI itself is failing.
            }

            return message;
        }

        private static void WriteLog(StreamWriter writer, string msg)
        {
            try
            {
                writer?.WriteLine($"{Timestamp()}: {msg}");
            }
            catch
            {
                // Logging should never be able to crash automation.
            }
        }

        private static string Timestamp()
        {
            var rebirthSeconds = 0.0;
            try
            {
                if (Character != null)
                {
                    rebirthSeconds = Character.rebirthTime.totalseconds;
                }
            }
            catch
            {
                rebirthSeconds = 0.0;
            }

            return $"{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToShortTimeString()} ({Math.Floor(rebirthSeconds)}s)";
        }
    }
}
