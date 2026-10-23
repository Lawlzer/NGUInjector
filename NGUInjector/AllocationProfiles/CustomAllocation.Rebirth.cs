using NGUInjector.AllocationProfiles.RebirthStuff;

namespace NGUInjector.AllocationProfiles
{
    internal partial class CustomAllocation
    {
        public bool DoRebirth() => DoRebirth(_wrapper?.Breakpoints?.Rebirth);

        internal bool DoRebirth(BaseRebirth rebirth)
        {
            if (rebirth == null || !rebirth.RebirthAvailable()) return false;
            if (_character.bossController.isFighting || _character.bossController.nukeBoss)
            {
                Main.Log("Delaying rebirth while boss fight is in progress");
                return false;
            }
            if (!rebirth.DoRebirth()) return false;
            ResetRebirthState();
            return true;
        }

        private void ResetRebirthState()
        {
            _currentEnergyBreakpoint = null;
            _currentWandoosBreakpoint = null;
            _currentMagicBreakpoint = null;
            _currentR3Breakpoint = null;
            _currentNguBreakpoint = null;
        }

        public void CastBloodSpells() => CastBloodSpells(false);
        public void CastBloodSpells(bool rebirth)
        {
            if (_wrapper.Breakpoints.Rebirth is TimeRebirth timed && Main.Settings.AutoRebirth
                && timed.RebirthTime - _character.rebirthTime.totalseconds < 30 * 60 && !rebirth) return;
            Main.reference?.RequestThresholdBloodSpells(rebirth);
        }
    }
}
