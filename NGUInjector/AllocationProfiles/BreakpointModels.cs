using System;
using NGUInjector.AllocationProfiles.BreakpointTypes;
using NGUInjector.AllocationProfiles.RebirthStuff;
using UnityEngine;

namespace NGUInjector.AllocationProfiles
{
    [Serializable]
    internal class BreakpointWrapper
    {
        [SerializeField] public Breakpoints Breakpoints;
    }

    [Serializable]
    internal class Breakpoints
    {
        [SerializeField] public AllocationBreakPoint[] Magic;
        [SerializeField] public AllocationBreakPoint[] Energy;
        [SerializeField] public AllocationBreakPoint[] R3;
        [SerializeField] public GearBreakpoint[] Gear;
        [SerializeField] public DiggerBreakpoint[] Diggers;
        [SerializeField] public WandoosBreakpoint[] Wandoos;
        [SerializeField] public BaseRebirth Rebirth;
        [SerializeField] public NGUDiffBreakpoint[] NGUBreakpoints;

    }

    [Serializable]
    internal class AllocationBreakPoint
    {
        [SerializeField] public double Time;
        [SerializeField] public BaseBreakpoint[] Priorities;
    }

    [Serializable]
    public class GearBreakpoint
    {
        public double Time;
        public int[] Gear;
    }

    [Serializable]
    public class DiggerBreakpoint
    {
        public double Time;
        public int[] Diggers;
    }

    [Serializable]
    public class WandoosBreakpoint
    {
        public double Time;
        public int OS;
    }

    [Serializable]
    public class NGUDiffBreakpoint
    {
        public double Time;
        public int Diff;
    }
}
