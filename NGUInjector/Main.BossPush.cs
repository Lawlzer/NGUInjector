namespace NGUInjector
{
    internal partial class Main
    {
        private const float BossPushFightWindow = 3.0f;
        private const float BossPushTickSeconds = 0.1f;
        private bool _bossPushRunning;
        private bool _bossPushNukeStarted;
        private float _bossPushFightSecondsRemaining;

        internal bool BossPushRunning => _bossPushRunning;

        internal void StartBossPush()
        {
            ResetCustomIronPillPrep();
            _bossPushRunning = true;
            _bossPushNukeStarted = false;
            _bossPushFightSecondsRemaining = BossPushFightWindow;
            Log("Starting boss push");
        }

        internal void StopBossPush()
        {
            _bossPushRunning = false;
            _bossPushNukeStarted = false;
            _bossPushFightSecondsRemaining = 0;
        }

        private void BossPushTick()
        {
            try
            {
                if (!_bossPushRunning)
                    return;

                var bc = Character.bossController;
                if (!_bossPushNukeStarted)
                {
                    if (!bc.isFighting && !bc.nukeBoss)
                    {
                        bc.startNuke();
                        _bossPushNukeStarted = true;
                    }

                    return;
                }

                if (bc.nukeBoss)
                    return;

                if (Character.curHP <= 0 || _bossPushFightSecondsRemaining <= 0)
                {
                    Log("Boss push finished");
                    StopBossPush();
                    ProcessPendingManualRebirth();
                    return;
                }

                _bossPushFightSecondsRemaining -= BossPushTickSeconds;
                if (bc.isFighting)
                    return;

                bc.beginFight();
                bc.stopButton.gameObject.SetActive(true);
            }
            catch (System.Exception e)
            {
                LogError("BossPushTick", e);
                StopBossPush();
                ProcessPendingManualRebirth();
            }
        }
    }
}
