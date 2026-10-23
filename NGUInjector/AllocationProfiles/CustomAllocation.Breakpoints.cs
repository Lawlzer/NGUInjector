namespace NGUInjector.AllocationProfiles
{
    internal partial class CustomAllocation
    {
        private AllocationBreakPoint GetCurrentBreakpoint(bool energy)
        {
            var bps = energy ? _wrapper?.Breakpoints?.Energy : _wrapper?.Breakpoints?.Magic;
            if (bps == null)
                return null;

            foreach (var b in bps)
            {
                var rbTime = _character.rebirthTime.totalseconds;
                if (rbTime > b.Time)
                {
                    if (energy && _currentEnergyBreakpoint == null)
                    {
                        _currentEnergyBreakpoint = b;
                    }

                    if (!energy && _currentMagicBreakpoint == null)
                    {
                        _currentMagicBreakpoint = b;
                    }

                    return b;
                }
            }

            if (energy)
            {
                _currentEnergyBreakpoint = null;
            }
            else
            {
                _currentMagicBreakpoint = null;
            }

            return null;
        }

        private AllocationBreakPoint GetCurrentR3Breakpoint()
        {
            var bps = _wrapper?.Breakpoints?.NGUBreakpoints;
            if (bps == null)
                return null;
            foreach (var b in _wrapper.Breakpoints.R3)
            {
                var rbTime = _character.rebirthTime.totalseconds;
                if (rbTime > b.Time)
                {
                    if (_currentR3Breakpoint == null)
                    {
                        _currentR3Breakpoint = b;
                    }

                    return b;
                }
            }

            _currentR3Breakpoint = null;
            return null;
        }

        private NGUDiffBreakpoint GetCurrentNGUDiffBreakpoint()
        {
            var bps = _wrapper?.Breakpoints?.NGUBreakpoints;
            if (bps == null)
                return null;
            foreach (var b in bps)
            {
                if (_character.rebirthTime.totalseconds > b.Time)
                {
                    if (_currentNguBreakpoint == null)
                    {
                        _hasNGUSwapped = false;
                        _currentNguBreakpoint = b;
                    }

                    return b;
                }
            }

            _currentNguBreakpoint = null;
            return null;
        }

        private WandoosBreakpoint GetCurrentWandoosBreakpoint()
        {
            var bps = _wrapper?.Breakpoints?.Wandoos;
            if (bps == null)
                return null;

            foreach (var b in bps)
            {
                if (_character.rebirthTime.totalseconds > b.Time)
                {
                    if (_currentWandoosBreakpoint == null)
                    {
                        _hasWandoosSwapped = false;
                        _currentWandoosBreakpoint = b;
                    }

                    return b;
                }
            }

            _currentWandoosBreakpoint = null;
            return null;
        }
    }
}
