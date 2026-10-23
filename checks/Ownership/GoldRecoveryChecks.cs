using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public static partial class OwnershipChecks
{
    static object BeginGold(Array gear)
    {
        S(MainObject, "_combManager", Activator.CreateInstance(Manager("CombatManager"), true));
        var process = M("BeginRouteProcess", "adventure-gold", gear, true, Domain("Gear, Support, Route, NativeSettings"), null);
        S(MainObject, "_customAdventureGoldProcess", process);
        return process;
    }

    static void CheckGoldRecoveryRegressions()
    {
        foreach (var successor in new[] { false, true }) Case(() => {
            Setup(); var inv = G(Character, "inventory"); var a = CapturePitGear(); Publish(a, "gold-A");
            var temp = NewGear(60, "Head"); var b = NewGear(61, "Head"); var bag = (IList)G(inv, "inventory"); bag[0] = temp; bag[1] = b;
            var process = BeginGold(WithGear(Helper(temp, -1)));
            Equal(true, M("ApplyRouteProcess", process), "real adventure-gold route setup applies");
            var acquired = G(G(process, "Claim"), "Acquisition");
            if (successor)
            {
                Publish(WithGear(Helper(b, -1))); M("ApplySelectedSetup", G(MainObject, "_executionBaseline"));
                Check(ReferenceEquals(temp, G(inv, "head")), "CURRENT B remains covered, not equipped early");
            }
            else M("PublishExecutionBaseline", (object)null);
            var seen = new List<int>(); Program.NativeGearSwapped = item => seen.Add((int)G(item, "id"));
            var recalls = G(typeof(Program), "Recalls");
            try { Equal(true, M("EndRouteProcess", process), "gold retirement resolves only current intent"); }
            finally { Program.NativeGearSwapped = null; }
            Check(ReferenceEquals(successor ? b : temp, G(inv, "head")), "gold exit selects current B or preserves native setup without successor");
            Check(!seen.Contains(40), "gold exit never flashes historical A"); Equal(recalls, G(typeof(Program), "Recalls"), "preserving gold handoff never recalls resources");
            Equal(acquired, G(G(process, "Claim"), "Acquisition"), "retirement never renews acquired process identity");
            var swaps = G(typeof(Program), "Swaps"); M("EndRouteProcess", process); Equal(swaps, G(typeof(Program), "Swaps"), "gold retirement is idempotent");
        });
        foreach (var fault in new[] { "partial", "unknown", "permission", "epoch", "foreign" }) Case(() => {
            Setup(); var inv = G(Character, "inventory"); var bag = (IList)G(inv, "inventory");
            var head = NewGear(60, "Head"); var chest = NewGear(61, "Chest"); bag[0] = head; bag[1] = chest;
            var process = BeginGold(WithGear(Helper(head, -1), Helper(chest, -2))); var setup = G(process, "Setup");
            var swaps = (int)G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
            if (fault == "partial") S(typeof(Program), "FailRefreshAtSwap", swaps + 1);
            else Program.NativeGearSwapped = _ => {
                Program.NativeGearSwapped = null;
                if (fault == "unknown") S(inv, "inventory", null);
                if (fault == "permission") Setting("CustomAdventureEnabled", !(bool)G(Settings, "CustomAdventureEnabled"));
                if (fault == "epoch") S(G(Character, "stats"), "rebirthNumber", 8L);
                if (fault == "foreign") S(inv, "head", NewGear(72, "Head"));
            };
            Exception failure;
            try { failure = Failure(() => M("ApplyRouteProcess", process)); }
            finally { Program.NativeGearSwapped = null; S(inv, "inventory", bag); }
            Check(failure != null, fault + " stops real gold application"); Equal(swaps + 1, G(typeof(Program), "Swaps"), "only first native gear boundary executed");
            Equal(false, G(process, "Applied"), "failed setup grants no applied route receipt"); Equal(-1, G(G(Character, "adventure"), "zone"), "failed setup admits no dependent fight");
            Equal(recalls, G(typeof(Program), "Recalls"), "failed preserving setup does not recall");
            var outcome = G(setup, "GearOutcome"); Equal(fault == "partial" ? "KnownPartial" : fault == "unknown" ? "UnknownOutcome" : "ForeignConflict", G(outcome, "Disposition").ToString(), "native actuator retains typed evidence for " + fault);
            var observed = CapturePitGear();
            if (fault == "partial")
            {
                Check(ReferenceEquals(head, G(inv, "head")), "known partial retains bounded committed head");
                Equal(true, M("ApplyRouteProcess", process), "same retained selection completes known partial without historical rollback");
                Equal(true, Invoke(Manager("LoadoutManager"), "ExactLoadoutMatches", G(setup, "Gear")), "known partial finishes exact temporary setup");
            }
            else
            {
                Failure(() => M("ApplyRouteProcess", process)); Equal(swaps + 1, G(typeof(Program), "Swaps"), "foreign/unknown outcome is not retried as success");
                SamePitMap(observed, CapturePitGear(), "foreign/unknown state preserved");
                var healthy = M("ResolveExecutionGrant", "healthy-energy", "current", Domain("Energy"), (Func<bool>)(() => true), null, null);
                Equal(true, M("ValidateExecutionGrant", healthy, Domain("Energy")), "gear uncertainty does not fabricate a global Energy lock");
            }
        });
    }
}
