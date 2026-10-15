using System;
using System.IO;
using System.Linq;
using System.Text;
using NGUInjector.AllocationProfiles.BreakpointTypes;
using NGUInjector.AllocationProfiles.RebirthStuff;
using SimpleJSON;

namespace NGUInjector.AllocationProfiles
{
    internal partial class CustomAllocation
    {
        internal void ReloadAllocation()
        {
            if (File.Exists(_allocationPath))
            {
                try
                {
                    var text = File.ReadAllText(_allocationPath);
                    var parsed = JSON.Parse(text);
                    var breakpoints = parsed["Breakpoints"];
                    _wrapper = new BreakpointWrapper {Breakpoints = new Breakpoints()};
                    var rb = breakpoints["Rebirth"];
                    var rbtime = breakpoints["RebirthTime"];
                    if (rb == null)
                    {
                        _wrapper.Breakpoints.Rebirth = rbtime == null ? new NoRebirth() : BaseRebirth.CreateRebirth(ParseTime(rbtime), "time", new string[0]);
                    }
                    else
                    {
                        if (rb["Type"] == null || rb["Target"] == null)
                            _wrapper.Breakpoints.Rebirth = new NoRebirth();

                        var type = rb["Type"].Value.ToUpper();
                        var target = type == "TIME" ? ParseTime(rb["Target"]) : rb["Target"].AsDouble;
                        _wrapper.Breakpoints.Rebirth = BaseRebirth.CreateRebirth(target, type, rb["Challenges"].AsArray.Children.Select(x => x.Value.ToUpper()).ToArray());
                    }

                    _wrapper.Breakpoints.Magic = breakpoints["Magic"].Children.Select(bp => new AllocationBreakPoint
                    {
                        Time = ParseTime(bp["Time"]),
                        Priorities = BaseBreakpoint.ParseBreakpointArray(bp["Priorities"].AsArray.Children.Select(x => x.Value.ToUpper())
                            .ToArray(), ResourceType.Magic, rbtime).Where(x => x != null).ToArray()
                    }).OrderByDescending(x => x.Time).ToArray();

                    _wrapper.Breakpoints.Energy = breakpoints["Energy"].Children.Select(bp => new AllocationBreakPoint
                    {
                        Time = ParseTime(bp["Time"]),
                        Priorities = BaseBreakpoint.ParseBreakpointArray(bp["Priorities"].AsArray.Children.Select(x => x.Value.ToUpper())
                            .ToArray(), ResourceType.Energy, rbtime).Where(x => x != null).ToArray()
                    }).OrderByDescending(x => x.Time).ToArray();

                    _wrapper.Breakpoints.R3 = breakpoints["R3"].Children.Select(bp => new AllocationBreakPoint
                    {
                        Time = ParseTime(bp["Time"]),
                        Priorities = BaseBreakpoint.ParseBreakpointArray(bp["Priorities"].AsArray.Children.Select(x => x.Value.ToUpper())
                            .ToArray(), ResourceType.R3, rbtime).Where(x => x != null).ToArray()
                    }).OrderByDescending(x => x.Time).ToArray();

                    _wrapper.Breakpoints.Gear = breakpoints["Gear"].Children
                        .Select(bp => new GearBreakpoint
                        {
                            Time = ParseTime(bp["Time"]),
                            Gear = bp["ID"].AsArray.Children.Select(x => x.AsInt).ToArray()
                        })
                        .OrderByDescending(x => x.Time).ToArray();

                    _wrapper.Breakpoints.Diggers = breakpoints["Diggers"].Children
                        .Select(bp => new DiggerBreakpoint
                        {
                            Time = ParseTime(bp["Time"]),
                            Diggers = bp["List"].AsArray.Children.Select(x => x.AsInt).ToArray()
                        }).OrderByDescending(x => x.Time).ToArray();

                    _wrapper.Breakpoints.Wandoos = breakpoints["Wandoos"].Children
                        .Select(bp => new WandoosBreakpoint {Time = ParseTime(bp["Time"]), OS = bp["OS"].AsInt})
                        .OrderByDescending(x => x.Time).ToArray();

                    _wrapper.Breakpoints.NGUBreakpoints = breakpoints["NGUDiff"].Children
                        .Select(bp => new NGUDiffBreakpoint {Time = ParseTime(bp["Time"]), Diff = bp["Diff"].AsInt})
                        .Where(x => x.Diff <= 2).OrderByDescending(x => x.Time).ToArray();


                    Main.Log(BuildAllocationString());

                    _currentDiggerBreakpoint = null;
                    _currentEnergyBreakpoint = null;
                    _currentGearBreakpoint = null;
                    _currentWandoosBreakpoint = null;
                    _currentMagicBreakpoint = null;
                    _currentR3Breakpoint = null;
                    _currentNguBreakpoint = null;

                    this.DoAllocations();
                }
                catch (Exception e)
                {
                    Main.Log("Failed to load allocation file. Resave to reload");
                    Main.Log(e.Message);
                    Main.Log(e.StackTrace);
                    _wrapper = new BreakpointWrapper
                    {
                        Breakpoints =
                        {
                            Rebirth = new NoRebirth(), R3 = new AllocationBreakPoint[0],
                            Diggers = new DiggerBreakpoint[0], Energy = new AllocationBreakPoint[0],
                            Gear = new GearBreakpoint[0], Magic = new AllocationBreakPoint[0],
                            NGUBreakpoints = new NGUDiffBreakpoint[0], Wandoos = new WandoosBreakpoint[0]
                        }
                    };

                    _currentDiggerBreakpoint = null;
                    _currentEnergyBreakpoint = null;
                    _currentGearBreakpoint = null;
                    _currentWandoosBreakpoint = null;
                    _currentMagicBreakpoint = null;
                    _currentR3Breakpoint = null;
                    _currentNguBreakpoint = null;
                }
            }
            else
            {
                var emptyAllocation = @"{
    ""Breakpoints"": {
      ""Magic"": [
        {
          ""Time"": 0,
          ""Priorities"": []
        }
      ],
      ""Energy"": [
        {
          ""Time"": 0,
          ""Priorities"": []
        }
      ],
    ""R3"": [
        {
          ""Time"": 0,
          ""Priorities"": []
        }
      ],
      ""Gear"": [
        {
          ""Time"": 0,
          ""ID"": []
        }
      ],
      ""Wandoos"": [
        {
          ""Time"": 0,
          ""OS"": 0
        }
      ],
      ""Diggers"": [
        {
          ""Time"": 0,
          ""List"": []
        }
      ],
      ""NGUDiff"": [
        {
          ""Time"": 0,
          ""Diff"": 0
        }
      ],
      ""RebirthTime"": -1
    }
  }
        ";

                Main.Log("Created empty allocation profile. Please update allocation.json");
                using (var writer = new StreamWriter(File.Open(_allocationPath, FileMode.CreateNew)))
                {
                    writer.WriteLine(emptyAllocation);
                    writer.Flush();
                }
            }
        }

        private double ParseTime(JSONNode timeNode)
        {
            var time = 0;

            if (timeNode.IsObject)
            {
                foreach (var N in timeNode)
                {
                    if (N.Value.IsNumber)
                    {
                        switch (N.Key.ToLower())
                        {
                            case "h":
                                time += 60 * 60 * N.Value.AsInt;
                                break;
                            case "m":
                                time += 60 * N.Value.AsInt;
                                break;
                            case "s":
                            default:
                                time += N.Value.AsInt;
                                break;
                        }
                    }
                }
            }

            if (timeNode.IsNumber)
            {
                time = timeNode.AsInt;
            }

            return time;
        }

        private string BuildAllocationString()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Loaded Custom Allocation from profile '{_profileName}'");
            builder.AppendLine($"{_wrapper.Breakpoints.Energy.Length} Energy Breakpoints");
            builder.AppendLine($"{_wrapper.Breakpoints.Magic.Length} Magic Breakpoints");
            builder.AppendLine($"{_wrapper.Breakpoints.R3.Length} R3 Breakpoints");
            builder.AppendLine($"{_wrapper.Breakpoints.Gear.Length} Gear Breakpoints");
            builder.AppendLine($"{_wrapper.Breakpoints.Diggers.Length} Digger Breakpoints");
            builder.AppendLine($"{_wrapper.Breakpoints.Wandoos.Length} Wandoos Breakpoints");
            builder.AppendLine($"{_wrapper.Breakpoints.NGUBreakpoints.Length} NGU Difficulty Breakpoints");
            var rb = _wrapper.Breakpoints.Rebirth;
            if (rb is NoRebirth)
            {
                builder.AppendLine($"Rebirth Disabled.");
            }else if (rb is NumberRebirth nrb)
            {
                builder.AppendLine($"Rebirthing when number bonus is {nrb.MultTarget}x previous number");
            }else if (rb is TimeRebirth trb)
            {
                builder.AppendLine($"Rebirthing at {trb.RebirthTime} seconds");
            }else if (rb is BossNumRebirth brb)
            {
                builder.AppendLine($"Rebirthing when number allows you +{brb.NumBosses} bosses");
            }

            if (rb.ChallengeTargets.Length > 0)
            {
                builder.AppendLine(
                    $"Challenge targets: {string.Join(",", rb.ChallengeTargets.Select(x => x.ToString()).ToArray())}");
            }

            return builder.ToString();
        }
    }
}
