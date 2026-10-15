namespace NGUInjector.AllocationProfiles.RebirthStuff
{
    internal class ManualRebirth : BaseRebirth
    {
        internal ManualRebirth()
        {
            ChallengeTargets = new RCTarget[0];
        }

        internal override bool RebirthAvailable()
        {
            return BaseRebirthChecks();
        }
    }
}
