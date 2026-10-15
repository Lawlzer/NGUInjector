using System;

namespace NGUInjector.Managers
{
    internal static class IronPillAutomation
    {
        internal static bool Eligible(bool enabled, bool planActive, double rebirthSeconds, int bossStage)
        {
            return EligibilityBlocker(enabled, planActive, rebirthSeconds, bossStage).Length == 0;
        }

        internal static string EligibilityBlocker(bool enabled, bool planActive, double rebirthSeconds, int bossStage)
        {
            if (!enabled) return "Automatic blood spells are disabled.";
            if (!planActive) return "Custom plan resource automation is inactive.";
            if (!(rebirthSeconds >= 3600d)) return "Requires at least one hour in the current rebirth.";
            if (bossStage < 100) return "Requires boss stage 100 or later.";
            return string.Empty;
        }

        internal static bool ShouldReserve(bool eligible, bool saving, double cooldownSeconds, int saveSeconds, double minWorth)
        {
            var lookahead = saveSeconds > 0 ? saveSeconds : minWorth > 0d ? 900 : 0;
            return eligible && (saving || cooldownSeconds <= lookahead);
        }

        internal static bool TargetReached(double elapsed, double worth, int saveSeconds, double minWorth)
        {
            return (saveSeconds <= 0 && minWorth <= 0d)
                   || (saveSeconds > 0 && elapsed >= saveSeconds)
                   || (minWorth > 0d && worth >= minWorth);
        }

        internal static double PrepLimitSeconds(int saveSeconds)
        {
            return Math.Max(900d, saveSeconds) + 60d;
        }

        internal static double Worth(double blood, float bonus)
        {
            // Match the game's float rounding and 100-million per-pill cap.
            var worth = (float) Math.Floor(Math.Pow(Math.Max(0d, blood), 0.25d)) * bonus;
            return Math.Max(0f, Math.Min(1e8f, worth));
        }
    }
}
