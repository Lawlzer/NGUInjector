using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NGUInjector.AllocationProfiles.BreakpointTypes
{
    internal class HackBP : BaseBreakpoint
    {
        protected override bool Unlocked()
        {
            return Index <= 14 && Character.buttons.hacks.interactable;
        }

        protected override bool TargetMet()
        {
            return Character.hacksController.hitTarget(Index);
        }

        protected override bool CorrectResourceType()
        {
            return Type == ResourceType.R3;
        }
    }
}
