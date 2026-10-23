using System;

namespace NGUInjector.Managers
{
    // The callback returns actual useful funding, not a requested share. Only that amount consumes budget.
    internal static class ResourceLanePriority
    {
        internal static long Resolve(long budget, string[] priorities, Func<string, long, long> fund)
        {
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            if (priorities == null) throw new ArgumentNullException(nameof(priorities));
            if (fund == null) throw new ArgumentNullException(nameof(fund));
            var remaining = budget;
            foreach (var activity in priorities)
            {
                if (remaining == 0) break;
                var amount = fund(activity, remaining);
                if (amount < 0 || amount > remaining) throw new InvalidOperationException("Lane funding exceeded its remaining budget.");
                remaining -= amount;
            }
            return remaining;
        }
    }
}
