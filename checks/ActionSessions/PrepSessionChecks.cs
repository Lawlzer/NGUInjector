using System;
using NGUInjector;
using NGUInjector.CustomPlans;

// Source-unit checks for the production prep/fruit/rebirth session code.
// Native controllers, route admission and selected-setup authority are boundaries.
internal static class PrepSessionChecks
{
    private static int assertions;
    private static Main runtime;
    private static CustomPlan plan;
    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
    private static void Setup()
    {
        Main.Character = new Character(); Main.Settings = new Settings();
        runtime = Main.reference = new Main(); plan = new CustomPlan();
    }
    private static void BusyNativeFightDoesNotApplySetup()
    {
        foreach (var nuke in new[] { false, true })
        {
            Setup(); Main.Character.bossController.isFighting = !nuke; Main.Character.bossController.nukeBoss = nuke;
            runtime.Prep(plan, true);
            Equal(0, runtime.SetupApplications, "busy native fight admits no gear/support/recall boundary");
            Equal(0, runtime.Selections, "busy native fight admits no replacement selection");
            Equal(null, runtime.FightPreparationIdentity, "busy native fight acquires no attempt");
            Equal(false, runtime.PrepareFinalRebirthAction(), "busy native fight blocks final NUMBER setup");
            Equal(false, runtime.PrepareHarvestAction(), "busy native fight blocks harvest setup");
            Main.Character.bossController.isFighting = Main.Character.bossController.nukeBoss = false;
            runtime.Prep(plan, true);
            Equal(true, runtime.BossPushRunning, "native readiness recovery permits current session preparation");
        }
    }
    private static void FruitStepsSurviveBossAdmissionWait()
    {
        Setup(); runtime.BossAdmissionReady = false;
        runtime.Prep(plan, true);
        Equal(1, Main.Character.yggdrasil.fruits[1].harvests, "first prep consumes alpha once");
        Equal(1, Main.Character.yggdrasil.fruits[6].harvests, "first prep consumes beta once");
        Equal(false, runtime.BossPushRunning, "route preflight may deny boss admission");
        for (var i = 0; i < 3; i++)
        {
            Main.Character.yggdrasil.fruits[1].seconds = Main.Character.yggdrasil.fruits[6].seconds = 3600;
            runtime.Prep(plan, true);
        }
        Equal(1, Main.Character.yggdrasil.fruits[1].harvests, "waiting for boss cannot consume a later alpha cycle");
        Equal(1, Main.Character.yggdrasil.fruits[6].harvests, "waiting for boss cannot consume a later beta cycle");
        runtime.BossAdmissionReady = true; runtime.Prep(plan, true);
        Equal(true, runtime.BossPushRunning, "same session admits boss when route is ready");
        Equal(1, runtime.Selections, "wait retains original selected gear");
        runtime.StopBossPush(); runtime.Prep(plan, true);
        Equal(false, runtime.BossPushRunning, "completed session cannot implicitly start another boss process");
    }
    private static void BusyNativeFightAppearingDuringSelectionDoesNotApplySetup()
    {
        Setup(); runtime.SelectionHook = () => Main.Character.bossController.isFighting = true;
        runtime.Prep(plan, true);
        Equal(0, runtime.SetupApplications, "native fight appearing during selection still blocks disruptive setup boundary");
        Equal(0, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "late native busy state also withholds fruit leaf");
        Equal(false, runtime.BossPushRunning, "late native busy state cannot start a second process");
    }
    private static void FruitPreflightDenialDoesNotConsumeStep()
    {
        Setup(); runtime.GrantReady = false; runtime.Prep(plan, true);
        Equal(0, Main.Character.yggdrasil.fruits[1].harvests, "denied leaf enters no native fruit method");
        Equal(true, runtime.FightPreparationCanRetry(runtime.FightPreparationIdentity), "uninvoked prep remains retryable");
        runtime.GrantReady = true; runtime.Prep(plan, true);
        Equal(1, Main.Character.yggdrasil.fruits[1].harvests, "readiness recovery executes the original fruit step");
        Equal(true, runtime.BossPushRunning, "verified recovered fruit allows boss process");
    }
    private static void AmbiguousFruitCancelsOnlyAttempt()
    {
        Setup(); Main.Character.yggdrasilController.fruits[0].RejectConsume = true;
        runtime.Prep(plan, true);
        Equal(1, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "ambiguous fruit actually entered native leaf");
        Equal(false, runtime.BossPushRunning, "ambiguous fruit withholds boss admission");
        Equal(false, runtime.FightPreparationCanRetry(runtime.FightPreparationIdentity), "ambiguous bounded prep is cancelled");
        var originalIdentity = runtime.FightPreparationIdentity;
        Main.Character.yggdrasilController.fruits[0].RejectConsume = false; runtime.Prep(plan, true);
        Equal(1, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "cancelled attempt never retries its ambiguous step");
        runtime.Prep(plan, true, "new-explicit-request");
        Equal(false, runtime.BossPushRunning, "new request ID cannot admit a boss over the unchanged ambiguous opportunity");
        Equal(1, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "new request ID cannot replay the unchanged ambiguous leaf");
        Equal(originalIdentity, runtime.FightPreparationIdentity, "unchanged ambiguity retains the original attempt evidence");
        Equal(0, Main.Character.yggdrasil.fruits[1].harvests, "unverified original fruit receives no fabricated harvest credit");
        Equal(0, Main.Character.rebirth.NormalCalls, "unchanged ambiguous opportunity admits no normal rebirth");
        Equal(0, Main.Character.rebirth.ChallengeCalls, "unchanged ambiguous opportunity admits no challenge rebirth");
        Equal(7L, Main.Character.stats.rebirthNumber, "blocked new request leaves the native rebirth unchanged");
        Main.Character.yggdrasil.fruits[1].harvests++;
        runtime.Prep(plan, true, "fresh-after-counter-change");
        Equal(true, runtime.BossPushRunning, "changed native opportunity permits fresh identified preparation");
        Equal(false, originalIdentity == runtime.FightPreparationIdentity, "changed opportunity acquires a fresh bounded attempt");
        Equal(2, Main.Character.yggdrasil.fruits[1].harvests, "fresh request uses the current native fruit counter");
        Equal(1, Main.Character.yggdrasil.fruits[6].harvests, "fresh request consumes the remaining current beta opportunity once");
        Equal(1, Main.Character.yggdrasil.fruits[0].harvests, "fresh request does not replay an already harvested original step");
        Equal(3, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "fresh request invokes only current alpha and beta opportunities after the one ambiguous invocation");
        runtime.Prep(plan, true, "fresh-after-counter-change");
        Equal(3, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "continuing the fresh attempt cannot replay consumed steps");
        Equal(2, Main.Character.yggdrasil.fruits[1].harvests, "fresh attempt continuation preserves exact alpha harvest accounting");
    }
    private static void AutomaticAmbiguousPrepYieldsCurrentPass()
    {
        Setup(); Main.Character.yggdrasilController.fruits[0].RejectConsume = true;
        Equal(true, runtime.RunCustomPreRebirthPrepIfNeeded(plan), "cancelled auto preparation stops the current rebirth pass");
        var originalIdentity = runtime.FightPreparationIdentity;
        var completed = typeof(Main).GetField("_customPreRebirthPrepCompleted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Main.Character.yggdrasilController.fruits[0].RejectConsume = false;
        for (var i = 0; i < 3; i++)
        {
            Equal(true, runtime.RunCustomPreRebirthPrepIfNeeded(plan), "cancelled auto preparation continues yielding later rebirth passes");
            Equal(originalIdentity, runtime.FightPreparationIdentity, "later automatic pass retains the cancelled attempt instead of replaying it");
            Equal(false, (bool)completed.GetValue(runtime), "cancelled automatic preparation is never marked complete");
            Equal(false, runtime.BossPushRunning, "later automatic pass admits no boss after ambiguous preparation");
            Equal(false, runtime.RebirthAttemptedThisPass, "later automatic pass admits no final rebirth attempt");
            Equal(0, Main.Character.rebirth.NormalCalls, "cancelled preparation never invokes normal rebirth");
            Equal(0, Main.Character.rebirth.ChallengeCalls, "cancelled preparation never invokes challenge rebirth");
            Equal(7L, Main.Character.stats.rebirthNumber, "cancelled preparation never completes a native rebirth");
            Equal(1, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "each later automatic pass preserves the one-shot native invocation count");
        }
        Equal(1, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "later pass cannot repeat the cancelled bounded fruit step");
    }
    private static void EpochReplacementCancelsSelectedAttempt()
    {
        Setup(); runtime.BossAdmissionReady = false; runtime.Prep(plan, false);
        var selections = runtime.Selections; Main.Character = new Character();
        runtime.Prep(plan, false);
        Equal(false, runtime.BossPushRunning, "foreign model cannot inherit selected preparation");
        Equal(selections, runtime.Selections, "foreign model does not trigger replacement selection in old request");
    }
    private static void UncertainFruitRejectsUnadmittedManualRequest()
    {
        Setup(); runtime.RouteRetirementReady = false;
        var controller = Main.Character.yggdrasilController.fruits[0]; controller.RejectConsume = true;
        runtime.Prep(plan, true);
        var identity = runtime.FightPreparationIdentity; var claim = runtime.FightPreparationClaim;
        Equal(false, claim == null, "uncertain prep retains its unresolved setup claim");
        runtime.CurrentExecutionCommandId = "manual-after-uncertain-fruit";
        Equal("Manual fight prep: waiting for previous preparation retirement.", runtime.RunManualFightPrep(),
            "unadmitted manual request waits for actual preparation retirement");
        Equal(true, runtime.ManualFightPending, "unresolved retirement retains the pending handoff");
        Equal(true, runtime.ProcessPending(), "unresolved handoff still stops the current ordinary pass");
        Equal(true, ReferenceEquals(claim, runtime.FightPreparationClaim), "failed retirement preserves the exact original claim");
        Equal(false, claim.Ended, "failed retirement does not drop partial ownership");
        runtime.RouteRetirementReady = true;
        Equal("Manual fight prep: previous fruit outcome requires review.", runtime.RunManualFightPrep(),
            "safe retirement still rejects preparation over the unchanged uncertain fruit");
        Equal(true, claim.Ended, "original ownership retires before rejecting the unadmitted request");
        Equal(false, runtime.ManualFightPending, "fruit review rejection does not remain queued");
        Equal(false, runtime.ProcessPending(), "rejected manual request no longer blocks ordinary dispatch");
        Equal(identity, runtime.FightPreparationIdentity, "manual rejection retains original uncertain attempt evidence");
        Equal(false, runtime.HarvestFruitAction(1, false, true), "manual rejection does not clear the uncertain fruit guard");
        Equal(1, controller.ConsumeCalls, "manual rejection invokes no replacement fruit leaf");
        Equal(false, runtime.BossPushRunning, "rejected manual request starts no boss process");
        Equal(0, Main.Character.rebirth.NormalCalls, "rejected manual request allows no final rebirth");
    }
    private static void CancelledPrepReleasesOnlyBlanketHarvestReservation()
    {
        Setup(); plan.RebirthPrepStartSeconds = 1; runtime.RouteRetirementReady = false;
        var controller = Main.Character.yggdrasilController.fruits[0]; controller.RejectConsume = true;
        Equal(true, runtime.HarvestReserved(plan), "due timed preparation initially reserves ordinary harvesting");
        Equal(true, runtime.RunCustomPreRebirthPrepIfNeeded(plan), "uncertain automatic prep stops the rebirth pass");
        var identity = runtime.FightPreparationIdentity; var claim = runtime.FightPreparationClaim;
        Equal(false, claim == null, "cancelled prep retains its unretired setup claim");
        Equal(true, runtime.HarvestReserved(plan), "cancelled prep keeps blanket reservation while ownership is unresolved");
        runtime.RouteRetirementReady = true; controller.RejectConsume = false;
        foreach (var nuke in new[] { false, true })
        {
            Main.Character.bossController.isFighting = !nuke; Main.Character.bossController.nukeBoss = nuke;
            Equal(true, runtime.RunCustomPreRebirthPrepIfNeeded(plan), "native busy state still withholds automatic continuation");
            Equal(true, runtime.HarvestReserved(plan), "native busy state keeps the harvest reservation");
            Equal(true, ReferenceEquals(claim, runtime.FightPreparationClaim), "native busy state retains original preparation ownership");
            Equal(false, claim.Ended, "native busy state cannot retire the setup claim");
            Main.Character.bossController.isFighting = Main.Character.bossController.nukeBoss = false;
        }
        Equal(true, runtime.RunCustomPreRebirthPrepIfNeeded(plan), "safe cancellation retirement still withholds final rebirth");
        Equal(true, claim.Ended, "cancelled prep safely retires its original setup claim");
        Equal(null, runtime.FightPreparationClaim, "safe retirement releases preparation ownership");
        foreach (var nuke in new[] { false, true })
        {
            Main.Character.bossController.isFighting = !nuke; Main.Character.bossController.nukeBoss = nuke;
            Equal(true, runtime.HarvestReserved(plan), "a new native fight still reserves harvesting after prep ownership retires");
            Main.Character.bossController.isFighting = Main.Character.bossController.nukeBoss = false;
        }
        Equal(false, runtime.HarvestReserved(plan), "retired cancelled prep no longer reserves every healthy fruit");
        Equal(true, runtime.HarvestFruitAction(2, false), "healthy fruit can use the production harvest leaf after cancellation");
        Equal(1, Main.Character.yggdrasil.fruits[2].harvests, "healthy fruit receives exactly one harvest");
        Equal(false, runtime.HarvestFruitAction(1, false, true), "unchanged ambiguous fruit remains protected");
        Equal(0, Main.Character.yggdrasil.fruits[1].harvests, "unknown fruit receives no fabricated harvest credit");
        Equal(1, controller.ConsumeCalls, "ordinary harvesting never repeats the unknown consume invocation");
        Equal(identity, runtime.FightPreparationIdentity, "healthy harvest preserves original uncertainty evidence");
        Equal(true, runtime.RunCustomPreRebirthPrepIfNeeded(plan), "healthy harvesting does not restart cancelled preparation or allow final rebirth");
        Equal(0, Main.Character.yggdrasil.fruits[6].harvests, "automatic continuation does not resume the remaining prep fruit steps");
        Equal(false, runtime.BossPushRunning, "healthy harvesting admits no boss process");
        Equal(0, Main.Character.rebirth.NormalCalls, "healthy harvesting admits no normal rebirth");
        Equal(0, Main.Character.rebirth.ChallengeCalls, "healthy harvesting admits no challenge rebirth");
    }
    private static void ForeignFruitOpportunityCancelsOriginalRequest()
    {
        Setup(); runtime.GrantReady = false; runtime.Prep(plan, true);
        Main.Character.yggdrasil.fruits[1].harvests++;
        runtime.GrantReady = true; runtime.Prep(plan, true);
        Equal(false, runtime.FightPreparationCanRetry(runtime.FightPreparationIdentity), "foreign harvest counter cancels rather than waiting forever on stale counter");
        Equal(0, Main.Character.yggdrasilController.fruits[0].ConsumeCalls, "changed fruit opportunity invokes no replacement harvest");
        Equal(false, runtime.BossPushRunning, "changed fruit opportunity admits no boss process");
    }
    private static void RebirthCommittedProofSurvivesRetiredAuthority()
    {
        Setup(); runtime.BeginRebirthAutomationPass(); Equal(true, runtime.PrepareFinalRebirthAction(), "final setup is ready");
        Main.Character.rebirth.ThrowAfterCommit = true;
        Equal(true, runtime.InvokeFinalRebirthAction("engage"), "native committed rebirth succeeds even after old authority retires and render throws");
        Equal(true, runtime.RebirthAttemptedThisPass, "native rebirth invocation consumes pass attempt");
        Equal(8L, Main.Character.stats.rebirthNumber, "rebirth increments actual native counter once");
        Equal(false, runtime.InvokeFinalRebirthAction("engage"), "same pass cannot engage a second native rebirth");
    }
    private static void ChallengeDoesNotFallThroughToNormal()
    {
        Setup(); runtime.PrepareFinalRebirthAction(); Main.Character.rebirth.Reject = true;
        runtime.BeginRebirthAutomationPass();
        Equal(false, runtime.InvokeFinalRebirthAction("engageBasicChallenge"), "unverified challenge attempt has no native committed proof");
        Equal(false, runtime.InvokeFinalRebirthAction("engage"), "unverified challenge cannot fall through to normal same pass");
        Equal(1, Main.Character.rebirth.ChallengeCalls, "challenge native method entered once");
        Equal(0, Main.Character.rebirth.NormalCalls, "normal native method never entered after challenge");
        runtime.BeginRebirthAutomationPass(); Main.Character.rebirth.Reject = false;
        Equal(true, runtime.InvokeFinalRebirthAction("engage"), "later ordinary pass is not quarantined by previous ambiguity");
    }
    private static void RebirthPreflightDenialIsNotAttempted()
    {
        Setup(); runtime.PrepareFinalRebirthAction(); runtime.BeginRebirthAutomationPass(); runtime.GrantReady = false;
        Equal(false, runtime.InvokeFinalRebirthAction("engage"), "denied rebirth is not committed");
        Equal(false, runtime.RebirthAttemptedThisPass, "preflight denial does not consume one-shot attempt");
        runtime.GrantReady = true;
        Equal(true, runtime.InvokeFinalRebirthAction("engage"), "same pass can retry genuinely uninvoked rebirth");
    }
    private static void PendingManualRebirthStopsPass()
    {
        Setup(); runtime.SetPendingManualRebirth(); runtime.BeginRebirthAutomationPass();
        Equal(true, runtime.ProcessPending(), "manual continuation stops ordinary coordinator pass even after request retires");
        Equal(1, Main.Character.rebirth.NormalCalls, "manual continuation engages one native rebirth");
        Equal(false, runtime.ManualPending, "successful manual request retires in memory");
        Equal(true, runtime.RebirthAttemptedThisPass, "manual continuation exposes stop signal after epoch change");
    }
    private static void ManualFightDenialRetainsSessionContinuation()
    {
        Setup(); runtime.CurrentExecutionCommandId = "manual-request"; runtime.BossAdmissionReady = false;
        runtime.RunManualFightPrep();
        Equal(true, runtime.ManualFightPending, "manual boss admission denial retains pending session request");
        var identity = runtime.FightPreparationIdentity;
        Equal(true, runtime.ProcessPending(), "pending manual fight stops ordinary pass while retrying readiness");
        Equal(identity, runtime.FightPreparationIdentity, "manual continuation retains exact original attempt");
        runtime.BossAdmissionReady = true; runtime.ProcessPending();
        Equal(true, runtime.BossPushRunning, "manual continuation eventually admits original boss process");
        Equal(false, runtime.ManualFightPending, "successful boss admission retires pending request");
        Equal(1, runtime.Selections, "manual continuation never repeats acquired selection");
    }
    private static void RestartCannotRestorePendingManualRequest()
    {
        Setup(); runtime.SetPendingManualRebirth();
        runtime = Main.reference = new Main();
        Equal(false, runtime.ProcessPending(), "new session imports no pending manual rebirth request");
        Equal(0, Main.Character.rebirth.NormalCalls, "session restart does not engage a native rebirth");
    }
    internal static void RunChecks()
    {
        BusyNativeFightDoesNotApplySetup(); BusyNativeFightAppearingDuringSelectionDoesNotApplySetup();
        FruitStepsSurviveBossAdmissionWait(); FruitPreflightDenialDoesNotConsumeStep();
        AmbiguousFruitCancelsOnlyAttempt(); AutomaticAmbiguousPrepYieldsCurrentPass();
        UncertainFruitRejectsUnadmittedManualRequest(); CancelledPrepReleasesOnlyBlanketHarvestReservation();
        EpochReplacementCancelsSelectedAttempt(); ForeignFruitOpportunityCancelsOriginalRequest();
        RebirthCommittedProofSurvivesRetiredAuthority();
        ChallengeDoesNotFallThroughToNormal(); RebirthPreflightDenialIsNotAttempted(); PendingManualRebirthStopsPass();
        ManualFightDenialRetainsSessionContinuation(); RestartCannotRestorePendingManualRequest();
        Console.WriteLine("Prep/rebirth session source-unit checks PASS: " + assertions + " assertions; native/setup/route boundaries supplied.");
    }
}

internal static class PrepSessionEntry
{
    private static void Main() { PrepSessionChecks.RunChecks(); }
}
