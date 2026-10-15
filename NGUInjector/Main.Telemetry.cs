using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NGUInjector
{
    internal partial class Main
    {
        public void MonitorLog()
        {
            try
            {
                var bLog = Character.adventureController.log;
                var type = bLog.GetType().GetField("Eventlog",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var val = type?.GetValue(bLog);
                if (val == null)
                    return;

                var log = (List<string>) val;
                for (var i = log.Count - 1; i >= 0; i--)
                {
                    var line = log[i];
                    if (!line.Contains("dropped")) continue;
                    if (line.Contains("gold")) continue;
                    if (line.ToLower().Contains("special boost")) continue;
                    if (line.ToLower().Contains("toughness boost")) continue;
                    if (line.ToLower().Contains("power boost")) continue;
                    if (line.Contains("EXP")) continue;
                    if (line.EndsWith("<b></b>")) continue;
                    var result = line;
                    if (result.Contains("\n"))
                    {
                        result = result.Split('\n').Last();
                    }

                    var sb = new StringBuilder(result);
                    sb.Replace("<color=blue>", "");
                    sb.Replace("<b>", "");
                    sb.Replace("</color>", "");
                    sb.Replace("</b>", "");

                    LogLoot(sb.ToString());
                    log[i] = $"{line}<b></b>";
                }
            }
            catch (Exception e)
            {
                LogError("MonitorLog", e);
            }
        }

        public void ShowBoostProgress()
        {
            try
            {
                var boostSlots = _invManager.GetBoostSlots(Character.inventory.GetConvertedInventory().ToArray());
                _invManager.ShowBoostProgress(boostSlots);
            }
            catch (Exception e)
            {
                LogError("ShowBoostProgress", e);
            }
        }

        public void ResetBoostProgress()
        {

        }
    }
}
