using System;
using System.Linq;

namespace NGUInjector
{
    internal sealed class FiniteActionIdentity
    {
        internal readonly string Kind, Owner, Acquisition, Entry, Opportunity, Key, Scope;
        internal FiniteActionIdentity(string kind, string owner, string acquisition, string entry, string opportunity)
        {
            var parts = new[] { kind, owner, acquisition, entry, opportunity };
            if (parts.Any(string.IsNullOrEmpty)) throw new ArgumentException("Finite actions require complete semantic identity.");
            Kind = kind; Owner = owner; Acquisition = acquisition; Entry = entry; Opportunity = opportunity;
            // Length prefixes avoid aliasing identities containing punctuation.
            Scope = string.Concat(parts.Take(4).Select(x => x.Length + ":" + x));
            Key = Scope + opportunity.Length + ":" + opportunity;
        }
    }
}
