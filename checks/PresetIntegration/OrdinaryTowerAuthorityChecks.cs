using System;

public static partial class PresetIntegrationChecks
{
    private static void CheckOrdinaryTowerAuthority()
    {
        CheckOrdinaryTowerAfterSetupRetirement();
        CheckOrdinaryTowerRetirementDoesNotAdoptSettings();
        CheckOrdinaryTowerDeniedAdmission();
        CheckOrdinaryTowerCallbackRevocation();
    }

    private static int PrepareOrdinaryTowerAuthority()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(settings, "_customAdventureGoldPush", false); Set(settings, "_customAdventureMode", 0);
        Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
        Set(settings, "_manualAdventureEnabled", false); Set(settings, "_beastMode", false);
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        Set(controller, "regAttackMulti", 1f);
        Set(adventure, "autoattacking", true); Set(adventure, "beastModeOn", false);
        Set(adventure, "highestItopodLevel", 10001); Set(adventure, "itopodStart", 3); Set(adventure, "itopodEnd", 4);
        Set(Child(controller, "itopodStartInput"), "m_Text", "3");
        Set(Child(controller, "itopodEndInput"), "m_Text", "4");
        Set(Get(Get(Character, "inventory"), "head"), "curAttack", 1000000f);
        Invoke(typeof(Program), "RefreshBonuses");
        var optimal = (int)Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "CalculateBestItopodLevel");
        Check(optimal > 4 && optimal < (int)Get(adventure, "highestItopodLevel"), "tower fixture has a positive native-verifiable optimal floor different from both old bounds");
        Equal(true, CallMain("ValidateExecutionGrant", CallMain("ObserveRouteGrant", 1000), Domain("Route, NativeSettings")), "ordinary tower starts with current native authority");
        return optimal;
    }

    private static void CheckOrdinaryTowerAfterSetupRetirement()
    {
        var optimal = PrepareOrdinaryTowerAuthority();
        var adventure = Get(Character, "adventure"); var manager = Get(MainInstance, "_combManager");
        Equal(true, Invoke(manager, "MoveToZone", 0), "ordinary route enters a real native predecessor zone");
        var oldGrant = CallMain("ObserveRouteGrant", 1000);
        var setup = ExactClaimSetup(Get(Get(Character, "inventory"), "head"), "ordinary-retirement-regression");
        Set(setup, "ControlsGear", false); Set(setup, "RetireRoute", true);
        Set(setup, "RouteRetirementAuthorized", (Func<bool>)(() => (bool)CallMain("ValidateExecutionGrant", oldGrant, Domain("Route"))));
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var routes = AdventureZoneEntries;
        var resources = CallMain("GlobalPresetAllocationState"); var support = Property(CallMain("CaptureSupportState"), "StableKey");
        CallMain("ApplySelectedSetup", setup);
        Equal(-1, Get(adventure, "zone"), "selected setup actually retires native predecessor combat");
        Equal(routes + 1, AdventureZoneEntries, "selected setup performs exactly one native Safe Zone retirement");
        Equal(-1, Get(MainInstance, "_ordinaryRouteZone"), "verified selected-setup retirement publishes its Safe Zone effect");
        Equal(true, CallMain("ValidateExecutionGrant", CallMain("ObserveRouteGrant", 1000), Domain("Route, NativeSettings")), "fresh ordinary tower grant survives our own retirement");
        CallMain("RouteConfiguredAdventureGoal", "selected setup retired");
        Equal(optimal, Get(adventure, "itopodStart"), "ordinary tower writes the selected optimal start through native verification");
        Equal(optimal, Get(adventure, "itopodEnd"), "ordinary tower writes the selected optimal end through native verification");
        Equal(1000, Get(adventure, "zone"), "ordinary tower resumes native ITOPOD after selected-setup retirement");
        Equal(routes + 2, AdventureZoneEntries, "resumption adds only one native ITOPOD entry");
        CallMain("RouteConfiguredAdventureGoal", "repeat");
        Equal(routes + 2, AdventureZoneEntries, "verified optimal route does not repeat native entry");
        Equal(swaps, Get(typeof(Program), "Swaps"), "route-only retirement/resumption swaps no equipment");
        Equal(writes, ResourceTransfers, "route-only retirement/resumption transfers no resources");
        Equal(resources, CallMain("GlobalPresetAllocationState"), "route-only retirement preserves exact native allocations");
        Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), "route-only retirement preserves exact coupled support");
    }

    private static void CheckOrdinaryTowerRetirementDoesNotAdoptSettings()
    {
        PrepareOrdinaryTowerAuthority();
        var adventure = Get(Character, "adventure");
        Equal(true, Invoke(Get(MainInstance, "_combManager"), "MoveToZone", 0), "settings negative control enters ordinary native combat");
        var oldGrant = CallMain("ObserveRouteGrant", 1000);
        var setup = ExactClaimSetup(Get(Get(Character, "inventory"), "head"), "ordinary-retirement-settings");
        Set(setup, "ControlsGear", false); Set(setup, "RetireRoute", true);
        Set(setup, "RouteRetirementAuthorized", (Func<bool>)(() => (bool)CallMain("ValidateExecutionGrant", oldGrant, Domain("Route"))));
        AdventureZoneEntryCompletedObserver = zone => { if (zone == -1) Set(adventure, "autoattacking", false); };
        try { CallMain("ApplySelectedSetup", setup); }
        finally { AdventureZoneEntryCompletedObserver = null; }
        Equal(-1, Get(adventure, "zone"), "retirement observes the actual Safe Zone effect");
        Equal(false, Get(adventure, "autoattacking"), "retirement preserves the callback's foreign attack setting");
        Equal(true, Get(MainInstance, "_ordinaryRouteIdle"), "retirement does not adopt a foreign setting into ordinary authority");
        Equal(false, CallMain("ValidateExecutionGrant", CallMain("ObserveRouteGrant", 1000), Domain("Route, NativeSettings")), "publishing Safe Zone cannot launder a changed attack setting");
        var routes = AdventureZoneEntries;
        CallMain("RouteConfiguredAdventureGoal", "foreign setting retained");
        Equal(routes, AdventureZoneEntries, "foreign attack setting prevents ordinary native re-entry");
        Equal(3, Get(adventure, "itopodStart"), "foreign attack setting prevents tower start writes");
        Equal(4, Get(adventure, "itopodEnd"), "foreign attack setting prevents tower end writes");
    }

    private static void CheckOrdinaryTowerDeniedAdmission()
    {
        foreach (var kind in new[] { "claim", "foreign-route", "review" })
        {
            PrepareOrdinaryTowerAuthority();
            var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
            object claim = null;
            if (kind == "claim") claim = CallMain("BeginExecutionClaim", "tower-priority", "original-acquisition", Domain("Route, NativeSettings"), (Func<bool>)(() => true));
            if (kind == "foreign-route") Set(adventure, "zone", 4);
            if (kind == "review") CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "route", "fixture unresolved route review");
            var zone = Get(adventure, "zone"); var routes = AdventureZoneEntries; var writes = ResourceTransfers;
            var drafts = 0; InlineObservedError = null;
            InputInterference = (input, text) => drafts++;
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    CallMain("RouteConfiguredAdventureGoal", kind);
                    CallMain("CustomAdventureTick");
                }
            }
            finally { InputInterference = null; }
            Equal(null, InlineObservedError, kind + " repeated blocked tower ticks report no injector exception");
            Equal(0, drafts, kind + " denied authority writes neither native input draft");
            Equal(3, Get(adventure, "itopodStart"), kind + " denied authority preserves native start");
            Equal(4, Get(adventure, "itopodEnd"), kind + " denied authority preserves native end");
            Equal("3", Property(Get(controller, "itopodStartInput"), "text"), kind + " preserves start input");
            Equal("4", Property(Get(controller, "itopodEndInput"), "text"), kind + " preserves end input");
            Equal(zone, Get(adventure, "zone"), kind + " denied authority preserves native route");
            Equal(routes, AdventureZoneEntries, kind + " denied authority performs no native zone entry");
            Equal(writes, ResourceTransfers, kind + " denied authority transfers no resources");
            if (claim != null)
            {
                Equal(true, CallMain("EndExecutionClaim", claim, false), "priority owner explicitly releases its original claim");
                CallMain("RouteConfiguredAdventureGoal", "priority owner released");
                Equal(1000, Get(adventure, "zone"), "ordinary tower resumes only after priority claim release");
            }
        }
    }

    private static void CheckOrdinaryTowerCallbackRevocation()
    {
        foreach (var kind in new[] { "revision", "route", "floor", "draft", "boss", "model" })
        {
            PrepareOrdinaryTowerAuthority();
            var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
            var zone = Get(adventure, "zone"); var routes = AdventureZoneEntries; var writes = ResourceTransfers;
            var drafts = 0; object foreign = null;
            InputInterference = (input, text) => {
                drafts++;
                if (drafts != 1) return;
                if (kind == "revision") CallMain("InvalidateExecutionObservations", Domain("NativeSettings"));
                if (kind == "route") Set(adventure, "zone", 4);
                if (kind == "floor") Set(adventure, "itopodEnd", 123);
                if (kind == "draft") Set(Get(controller, "itopodEndInput"), "m_Text", "foreign draft");
                if (kind == "boss") Set(Get(Character, "bossController"), "nukeBoss", true);
                if (kind == "model")
                {
                    foreign = Activator.CreateInstance(adventure.GetType());
                    Set(foreign, "zone", 4); Set(foreign, "itopodStart", 123); Set(foreign, "itopodEnd", 124);
                    Set(Character, "adventure", foreign);
                }
            };
            try { Throws(() => CallMain("RouteConfiguredAdventureGoal", kind), "Optimal tower settings authority changed", kind + " callback revocation still fails closed after admission"); }
            finally { InputInterference = null; }
            Equal(1, drafts, kind + " callback stops before the second input write or native verification");
            Equal(3, Get(adventure, "itopodStart"), kind + " callback never submits the changed draft to the native model");
            Equal(kind == "floor" ? 123 : 4, Get(adventure, "itopodEnd"), kind + " callback does not overwrite native end");
            Equal(kind == "route" ? 4 : zone, Get(adventure, "zone"), kind + " callback preserves native route");
            Equal(kind == "draft" ? "foreign draft" : "4", Property(Get(controller, "itopodEndInput"), "text"), kind + " callback never overwrites the second draft");
            Equal(routes, AdventureZoneEntries, kind + " callback makes no native zone entry");
            Equal(writes, ResourceTransfers, kind + " callback transfers no resources");
            if (foreign != null)
            {
                Check(ReferenceEquals(foreign, Get(Character, "adventure")), "foreign model is not replaced by cleanup");
                Equal(123, Get(foreign, "itopodStart"), "foreign model start remains untouched");
                Equal(124, Get(foreign, "itopodEnd"), "foreign model end remains untouched");
            }
        }
    }
}
