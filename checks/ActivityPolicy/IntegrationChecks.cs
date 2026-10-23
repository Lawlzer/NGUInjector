using System;
using System.IO;
using System.Linq;
using NGUInjector;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using Game = NGUInjector.Main;

internal static class IntegrationChecks
{
    private static int assertions;
    private static readonly DateTime Entry = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message)
    { Check(Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
    private static void Main()
    {
        Metadata();
        ExactSelectedSupport();
        ForeignSupportRequiresReview();
        RetainedNativeRetry();
        ImminentTitanPolicy();
        CallbackRevokesSupportAuthority();
        UnavailableHardSupport();
        Console.WriteLine("PASS migrated activity policy executor: " + assertions + " assertions.");
        assertions += SupportPrerequisiteChecks.Run();
        Console.WriteLine("PASS activity policy integration: " + assertions + " assertions; shared SustainedFocus source executor, projection and native boundary, no legacy writers.");
    }

    private static void Metadata()
    {
        SimpleJSON.JSONObject root;
        using (var stream = typeof(ActivityPolicy).Assembly.GetManifestResourceStream(ActivityPolicy.ResourceName))
        using (var reader = new StreamReader(stream)) root = JsonFiles.ParseObject(reader.ReadToEnd());
        foreach (CustomFocusGoal goal in Enum.GetValues(typeof(CustomFocusGoal)))
        {
            var setup = Game.SetupForFocus(goal);
            var definition = ActivityPolicy.Current.FocusDefinition(goal.ToString());
            Equal(definition.GearGoal, setup.Stage.GearGoal, "Quick focus gear mapped by enum value");
            Equal(definition.EnergyFocus, setup.Stage.EnergyFocus, "Quick focus energy mapped by enum value");
            Equal(definition.MagicFocus, setup.Stage.MagicFocus, "Quick focus magic mapped by enum value");
            var policy = ActivityPolicy.Current;
            var preset = root["stagePresets"].Children.SingleOrDefault(row => row["GearGoal"].AsInt == definition.GearGoal
                && row["EnergyFocus"].AsInt == definition.EnergyFocus && row["MagicFocus"].AsInt == definition.MagicFocus);
            var energy = preset == null ? policy.DefaultPriorities(definition.EnergyFocus, true)
                : root["prioritySets"][preset["EnergyPriorities"].Value].Children.Select(row => row.Value).ToArray();
            var magic = preset == null ? policy.DefaultPriorities(definition.MagicFocus, false)
                : root["prioritySets"][preset["MagicPriorities"].Value].Children.Select(row => row.Value).ToArray();
            Equal(string.Join(",", energy), string.Join(",", setup.Stage.EnergyPriorities), goal + " full explicit standard/fallback energy order");
            Equal(string.Join(",", magic), string.Join(",", setup.Stage.MagicPriorities), goal + " full explicit standard/fallback magic order");
            var expected = policy.BuildSupport(policy.PrimaryPriority(energy, definition.EnergyFocus, true),
                policy.PrimaryPriority(magic, definition.MagicFocus, false), goal == CustomFocusGoal.Fight,
                goal == CustomFocusGoal.ManualTitans, false);
            Equal(string.Join(",", expected.Beards), string.Join(",", setup.Beards), "Quick focus support beard mapping");
            Equal(string.Join(",", expected.Diggers.Select(x => x.Id + ":" + x.Weight + ":" + x.Name)),
                string.Join(",", setup.Diggers.Select(x => x.Id + ":" + x.Weight + ":" + x.Name)), "Quick focus primitive digger DTO mapped unchanged");
            if (preset != null)
            {
                var normal = Game.SetupForStage(new CustomPlanStage {GearGoal = preset["GearGoal"].AsInt,
                    EnergyFocus = preset["EnergyFocus"].AsInt, MagicFocus = preset["MagicFocus"].AsInt,
                    EnergyPriorities = energy, MagicPriorities = magic});
                Equal(string.Join(",", normal.Beards), string.Join(",", setup.Beards), goal + " normal preset beard requests match");
                Equal(string.Join(",", normal.Diggers.Select(x => x.Id + ":" + x.Weight + ":" + x.Name)),
                    string.Join(",", setup.Diggers.Select(x => x.Id + ":" + x.Weight + ":" + x.Name)), goal + " normal preset digger requests match");
            }
            if (setup.Stage.EnergyPriorities.Length > 0) setup.Stage.EnergyPriorities[0] = "MUTATED";
            if (setup.Stage.MagicPriorities.Length > 0) setup.Stage.MagicPriorities[0] = "MUTATED";
            Equal(string.Join(",", energy), string.Join(",", Game.SetupForFocus(goal).Stage.EnergyPriorities), "Stage mutations cannot affect next Quick energy setup");
            Equal(string.Join(",", magic), string.Join(",", Game.SetupForFocus(goal).Stage.MagicPriorities), "Stage mutations cannot affect next Quick magic setup");
        }
        var stage = new CustomPlanStage { GearGoal = 9, EnergyPriorities = new[] {"invalid", " wan "}, MagicPriorities = new[] {"RIT-0"} };
        var fightGear = Game.SetupForStage(stage);
        Equal("4,1,3,5", string.Join(",", fightGear.Beards), "Fight gear does not replace resource intent with boss support");
        Check(fightGear.Diggers.Any(x => x.Id == 1) && fightGear.Diggers.Any(x => x.Id == 10) && fightGear.Diggers.All(x => x.Id != 2), "Wandoos/blood diggers independent of Fight gear");
        stage.GearGoal = 13;
        var manualGear = Game.SetupForStage(stage);
        Check(manualGear.Diggers.Any(x => x.Id == 1) && manualGear.Diggers.Any(x => x.Id == 10), "ManualAdventure gear alone is not explicit manual-adventure intent");
        var explicitManual = Game.SetupForManualAdventure();
        Equal("3:800,0:400", string.Join(",", explicitManual.Diggers.Select(x => x.Id + ":" + x.Weight)), "Explicit manual adventure owns digger intent");
        Equal("0,1,3,5", string.Join(",", Game.SetupForStage(new CustomPlanStage {GearGoal = 9}).Beards), "Resource-free Fight gear retains existing implicit boss setup");
        var adventure = Game.SetupForFocus(CustomFocusGoal.Adventure);
        Check(adventure.Diggers.Any(x => x.Id == 10) && adventure.Diggers.All(x => x.Id != 2), "Adventure quick focus retains AT/blood resource intent rather than boss gear intent");

    }

    private static Game Reset()
    {
        EquipmentRecording.Reset(true); Game.Settings = new FocusSettings();
        Game.Character = new FocusCharacter(); Game.reference = new Game();
        EquipmentRecording.BeardMessage = "beards:1,3,4,5,6";
        return Game.reference;
    }

    private static SupportSelection ExpectedSupport(Game.CustomEquipmentSetup setup, bool imminent = false)
    {
        var context = Game.CaptureSupportContextForChecks();
        var requests = setup.Diggers.Select(d => new DiggerBudgetRequest { Id = d.Id, Weight = d.Weight,
            Maximum = context.Diggers.Single(row => row.Id == d.Id).Maximum }).ToList();
        if (imminent) requests.AddRange(ActivityPolicy.Current.ImminentTitanDiggers().Select(d => new DiggerBudgetRequest {
            Id = d.Id, Weight = d.Weight, Maximum = context.Diggers.Single(row => row.Id == d.Id).Maximum }));
        var result = SupportProjection.Plan(context, new[] { 1, 3, 4, 5, 6 }, requests.ToArray(), ActivityPolicy.Current.DiggerBudgetFraction);
        Check(result.Feasible, "actual shared support projection must be feasible");
        return result.Selection;
    }

    private static void ExactSelectedSupport()
    {
        var owner = Reset();
        var setup = Game.SetupForFocus(CustomFocusGoal.Wandoos);
        var expected = ExpectedSupport(setup);
        var message = owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry);
        Equal(expected.StableKey, Game.CaptureSupportForChecks().StableKey, "selected coordinator installs exact registry-driven native support");
        Check(Game.Character.beards.beards[4].active && Game.Character.diggers.activeDiggers.Contains(1), "Wandoos support is actually active, not just a request receipt");
        Equal("support: " + expected.StableKey, message, "receipt identifies exact current support instead of a legacy GPS string");
        Equal(ActivityPolicy.Current.DiggerBudgetFraction, EquipmentRecording.Budgets.Single(), "exact actuator receives actual configured budget fraction");
        var writes = EquipmentRecording.ExactSupport.Count; var selections = EquipmentRecording.BeardRandomize.Count;
        owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry.AddSeconds(59));
        Check(EquipmentRecording.ExactSupport.Count == writes && EquipmentRecording.BeardRandomize.Count == selections, "unchanged support replays without selection or native mutation");
        owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry.AddSeconds(60));
        Check(EquipmentRecording.BeardRandomize.Count == selections + 1 && EquipmentRecording.ExactSupport.Count == writes, "minute event reselects without redundant identical native writes");
        Equal(Entry.AddSeconds(60), owner.LastBeardRotationForChecks, "minute receipt uses independently accepted event time");
    }

    private static void ForeignSupportRequiresReview()
    {
        var owner = Reset(); var setup = Game.SetupForFocus(CustomFocusGoal.Wandoos);
        owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry);
        var c = Game.Character;
        c.beards.activeBeards.Remove(4); c.beards.beards[4].active = false;
        c.beards.activeBeards.Add(0); c.beards.beards[0].active = true;
        var foreign = Game.CaptureSupportForChecks().StableKey; var writes = EquipmentRecording.ExactSupport.Count;
        Reject(() => owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry.AddSeconds(1)), "foreign support requires review");
        Equal(foreign, Game.CaptureSupportForChecks().StableKey, "manual support remains intact instead of blind manager repair");
        Equal(writes, EquipmentRecording.ExactSupport.Count, "foreign support receives no replacement write");
        Equal(Entry, owner.LastBeardRotationForChecks, "foreign rejection never renews the minute anchor");
    }

    private static void RetainedNativeRetry()
    {
        var owner = Reset(); var setup = Game.SetupForFocus(CustomFocusGoal.Wandoos);
        var expected = ExpectedSupport(setup);
        EquipmentRecording.ThrowNextBeards = true;
        Reject(() => owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry), "native support boundary interruption propagates");
        Equal("|", Game.CaptureSupportForChecks().StableKey, "failed initial native write earns no applied support");
        Equal(DateTime.MinValue, owner.LastBeardRotationForChecks, "failed support application consumes no minute");
        var selections = EquipmentRecording.BeardRandomize.Count;
        owner.ApplyEquipmentForChecks(setup, "activity-policy", Entry.AddSeconds(1));
        Equal(expected.StableKey, Game.CaptureSupportForChecks().StableKey, "retry installs the original selected native set and levels");
        Equal(selections, EquipmentRecording.BeardRandomize.Count, "retry retains the same acquired selection");
        Equal(Entry, owner.LastBeardRotationForChecks, "retry preserves original accepted time rather than extending cadence");
    }

    private static void ImminentTitanPolicy()
    {
        foreach (var imminent in new[] {false, true})
        {
            var owner = Reset(); EquipmentRecording.TitanSoon = imminent;
            var support = Game.SetupForStage(new CustomPlanStage());
            var original = string.Join(",", support.Diggers.Select(d => d.Id + ":" + d.Weight));
            var expected = ExpectedSupport(support, imminent);
            owner.ApplyEquipmentForChecks(support, "activity-policy", Entry);
            Equal(expected.StableKey, Game.CaptureSupportForChecks().StableKey, "imminent overlay applies exact production-planned native levels/" + imminent);
            Equal(imminent, Game.Character.diggers.activeDiggers.Contains(11), "EXP support follows actual imminent timing/" + imminent);
            var evaluation = SupportProjection.Evaluate(Game.CaptureSupportContextForChecks(), Game.CaptureSupportForChecks(), ActivityPolicy.Current.DiggerBudgetFraction, true);
            Check(evaluation.Feasible && evaluation.Drain <= evaluation.GrossGold * ActivityPolicy.Current.DiggerBudgetFraction, "entire native support respects configured income budget");
            Equal(original, string.Join(",", support.Diggers.Select(d => d.Id + ":" + d.Weight)), "opportunistic overlay never mutates base support requests");
        }
    }

    private static void CallbackRevokesSupportAuthority()
    {
        var owner = Reset(); var c = Game.Character;
        var head = new ih { slot = -1, id = 1, equipment = new Equipment { type = part.Head } };
        var next = new ih { slot = -1, id = 2, equipment = new Equipment { type = part.Head } };
        EquipmentRecording.ExactLoadout = new[] { head };
        var claim = owner.BeginExecutionClaim("activity-policy", "callback", Game.ExecutionDomain.Gear | Game.ExecutionDomain.Support,
            () => ReferenceEquals(Game.reference, owner) && ReferenceEquals(Game.Character, c));
        var callbacks = 0;
        EquipmentRecording.AfterExactApplication = () => { callbacks++; Game.Settings = new FocusSettings(); };
        Reject(() => owner.ApplyExecutionClaim(claim, new Game.SelectedSetupRequirements { Gear = new[] { next }, ChangeSupport = true,
            Budget = 1, Support = new SupportSelection(new[] { 0 }, Array.Empty<SupportDiggerLevel>()) }), "settings replacement revokes actual callback authority");
        Equal(1, callbacks, "native gear callback was actually reached");
        Check(ReferenceEquals(EquipmentRecording.ExactLoadout.Single().equipment, next.equipment), "committed native gear progress is not falsely rolled back");
        Equal("|", Game.CaptureSupportForChecks().StableKey, "revoked callback cannot enter support mutation");
        Equal(0, EquipmentRecording.ExactSupport.Count, "no support writer is invoked after settings identity changes");
    }

    private static void UnavailableHardSupport()
    {
        foreach (var unavailable in new Action<FocusCharacter>[] {
            c => c.beards.disabled = true, c => c.buttons.beards.interactable = false,
            c => c.buttons.beards = null, c => c.allBeards = null })
        {
            var owner = Reset(); var c = Game.Character;
            EquipmentRecording.BeardMessage = "beards:0,1,3";
            unavailable(c);
            Reject(() => owner.EnsureBeardForChecks(0), "unavailable hard support must reject, not verify an input receipt");
            Equal("|", Game.CaptureSupportForChecks().StableKey, "unavailable hard support preserves native empty state");
            Equal(0, EquipmentRecording.ExactSupport.Count, "unavailable hard support rejects before native mutation");
            Equal(DateTime.MinValue, owner.LastBeardRotationForChecks, "unavailable hard support cannot consume minute cadence");
        }
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { Check(true, message); return; }
        throw new InvalidOperationException(message + ": expected rejection");
    }
}
