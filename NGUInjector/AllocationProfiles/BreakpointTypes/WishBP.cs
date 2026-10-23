using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NGUInjector.AllocationProfiles.BreakpointTypes
{
    internal class WishBP : BaseBreakpoint
    {
        protected override bool Unlocked()
        {
            return Index <= Character.wishesController.curWishSlots() - 1 && Character.buttons.wishes.interactable;
        }

        protected override bool TargetMet()
        {
            return false;
        }

        protected override bool CorrectResourceType()
        {
            return true;
        }
    }
}
