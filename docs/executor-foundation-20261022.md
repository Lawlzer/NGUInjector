# Executor Foundation Handoff

## Parallel Core Contract

Persistence continuation hook: `string GetExecutionDomainReviewReason(string owner)`
reads current native-domain yield evidence without stopping healthy lanes or
creating a checkpoint-restoration latch. D can offer same-entry review from the
already durable selection when this reason is non-null, including an in-session
foreign AT/gear/support edit. Applying a reviewed `_globalPresetApplyingPolicy`
now takes priority over an existing application receipt and always preserves the
reviewed original/resolved definitions, horizon, normalization plan and acquisition.

Current additive hooks for C/D (implemented, no caller edit needed in core):
`Func<bool> CaptureExecutionBaselineRouteRetirement(ExecutionClaim claim, bool endingAppliedDashboard = false)` supplies
an exact current preset regular/ITOPOD route retirement witness after BeginClaim,
before any setup mutation. Assign it to RouteRetirementAuthorized. It captures
verified current receipt gear/material/support/funding/settings/epoch and permits
only that route -> Safe Zone. Unknown/unowned routes return a rejecting predicate.
The optional second argument is only for retiring the still-owned applied dashboard
route after an explicit normal-plan resume; it does not weaken the physical witness.
`void SetExecutionReturnBaseline(ExecutionClaim claim, SelectedSetupRequirements baseline)`
admits an EXPLICIT acquired return policy only for owner `quick:F8`. D supplies and
persists its selected descriptors; core does NOT capture an implicit return setup.
EndExecutionClaim always prefers an authorized CURRENT normal baseline, using this
explicit return policy only if no current goal owns a baseline. Other claims reject it.
`bool ExecutionClaimReservesEquipment(Equipment item)` is the read-only inventory
reservation hook for both active claim selections AND explicit F8 return selections,
plus an authorized retained baseline. C should include it in its reserved-instance
predicate; protecting only claim.Setup misses F8's acquired return items.

Same-session paused presets are retained by execution owner independently of the
per-preset recipe cache. Resuming the same plan/entry uses its original acquisition,
even when an explicit dashboard Preview+Apply changed the SAME preset ID meanwhile.
Recipe-cache updates do not silently replace that paused choice.
For C's Main partial, `RetainedExecutionPresetPolicies()` returns retained live-epoch
policy objects plus recipe-cache objects; include it when protecting/acquiring
material observers for paused selections. After a verified exact item leaf updates
an approved policy material, `ApproveExecutionPolicyMaterialRevision(policy)` marks
pending, attempts the existing persistence hook using policy.Owner, and withholds
credit on failure. D's persistence hook still needs owner-aware handling when a
retained policy belongs to a DIFFERENT paused plan than the currently loaded scheduler;
an unsupported owner stays pending, never falsely durable.

Core continuation keeps Begin/Apply/EndExecutionClaim signatures stable. The following
additive APIs are implemented in `Main.Execution.cs`; finite/route streams may use
them without calling Recall or retaining old allocations:

```csharp
void SetExecutionResourceReservation(ExecutionClaim claim, long energy, long magic, long res3 = 0);
void SetExecutionResourcePolicy(ExecutionClaim claim, ResourceSetupIntent policy);
ResourceMutationResult ReconcileExecutionResourceClaim(ExecutionClaim claim);
bool ExecutionResourceReservationReady(ExecutionClaim claim);
bool ExecutionOwnsDomain(ExecutionDomain domains);
bool ExecutionSetupVerified();
ResourceMutationResult ApplyExecutionSetting(ExecutionGrant grant, Action write, Func<bool> verify);
bool ReauthorizeExecutionDomains(ExecutionGrant grant, ExecutionDomain domains, Func<bool> reviewed);
ExecutionGrant ResolveExecutionRetirementGrant(ExecutionClaim claim, ExecutionDomain domains, Func<bool> stillOwned);
bool ServiceExecutionRetirement();
```

Reservations are exact minimum required IDLE amounts, not amounts to restore. Productive
baseline work receives the remaining current budget; already-idle surplus with no goal
is preserved rather than invented into a fill target. The claim must
declare each positive reserved lane; amounts are nonnegative and require current
management permission. Baseline priority calculations use current native budgets
minus reservations. `SetExecutionResourcePolicy` supplies an exact temporary
whole-lane map for declared lanes, excluding basic training. It replaces only those
lanes; missing targets within a declared controlled lane become explicit zero.
Keep the admitted claim/selection on retry. `ReconcileExecutionResourceClaim`
services CURRENT baseline requirements plus reservations/policy, never old allocations.
With no baseline goal, an idle reservation releases only its deficit from CURRENT
allocations. An explicit temporary whole-lane policy may clear/replace targets; ending
that policy without a successor releases only its still-owned allocation and retains
the typed verified ReleaseOutcome to prevent repeated cleanup accounting.
Check its typed result AND `ExecutionResourceReservationReady` before consuming a
fruit/pit effect. Ending the claim resolves today's baseline and current pools.
Reservations are not finite-action deduplication or an irreversible-effect receipt.

`ExecutionOwnsDomain` reports a retained temporary claim, not verified readiness.
`ExecutionSetupVerified` observes the effective selected setup; dependent actions
still validate their grant and their complete native preconditions at each boundary.
Production/native validation of the continuation is parent-owned and pending.

SelectedSetupRequirements additionally has `RequiredBeards` (hard native
prerequisites, never truncated) and `ControlsSupport`. The latter distinguishes a
retained pinned/current baseline support selection from gear-only observational
support. `ChangeSupport=true` already denotes controlled support. A temporary
support claim returns to the CURRENT controlled baseline support; an uncontrolled
keep-only domain is observed/preserved, never used as an historical exit target.

`ApplyExecutionSetting` is a leaf, not a workflow: it requires NativeSettings in
the supplied current grant, rechecks after the synchronous write, and returns the
existing typed mutation disposition from exact verification. Native callback chains
must split into leaves with checks between them. No automatic retry of an unknown
outcome. Preset target release/invalidation now REQUESTS service; the resource
coordinator services authorized target returns. Lifecycle callers must not depend
on `ReleaseGlobalPresetNativeTargets` synchronously clearing native fields.
`_presetExecutionBlock` is temporarily retained as a last-result diagnostic field
for the lifecycle stream's existing clearing assignment; its class is now
ExecutionDomainIssue and it NO LONGER admits/blocks execution. D may remove that
obsolete clearing assignment; core uses actual domain evidence for authority.

`ReauthorizeExecutionDomains` is the explicit reviewed boundary: the caller must
bind its review token/current native observation in `reviewed`. It clears ONLY the
specified resource/setup conflict evidence, revises that scope, and revokes the
review grant. Obtain a fresh grant afterward. It does not import selections,
renew time, or clear another owner's process history. `ResolveExecutionRetirementGrant`
admits only an existing current claim's declared non-action footprint, with an exact
still-owned native predicate. It works in Running/Quiescing, never LoadingSave or
Unloaded, and retains generation/epoch/permission/revision checks. Retire the owned
route/borrowed fields, then EndExecutionClaim(claim, false); it is not ordinary
admission or permission to start a finite effect during unload.
`ServiceExecutionRetirement` is the quiescing game-thread leaf service for existing
preset borrowed AT records. It validates their current-session native model/field
ownership and routes narrow returns through the SAME resource/settings actuators;
it cannot start ordinary work, select gear, fund a successor, or create a new entry.
Lifecycle may call it after SetExecutionLifecycle(Quiescing), before final unload.

## Status

Implemented an offline authority foundation and mechanical file-ownership split,
not the remaining whole-application cutover. No live changes, deployment, restart,
soak, commit, parent WIP edit or nested agent. Existing unrelated work is preserved.

Final production MVID: `e19fd397-6745-4b54-aa5f-d4c22e395bde`.

| Verification | Result |
| --- | --- |
| Production Release rebuild | PASS |
| Framework Preset/Combat/Ownership/BlackBar fixture rebuilds | PASS |
| Full native Preset, no selector | PASS 14,086 |
| Foundation native checks, focused and full Preset | PASS 42 |
| Preset commit budget, included in full Preset | PASS 624 / 20 cases |
| Inline stage setup, included in full Preset | PASS 462 |
| Full native Combat | PASS 11,985 |
| Full native Ownership | PASS 12,285 |
| Full native BlackBar | PASS 24,388 |
| `git diff --check` | PASS |

All full native runs and the standalone foundation selector used the final DLL.
BlackBar initially exposed rejection of legitimate no-domain resource observation;
the final version accepts an identity with `None` and grants no mutation domains.
Source-linked SDK fixtures, frontend and live acceptance were not run in this slice.
The existing native fixture warning about unassigned `Program.NativeGearSwapped`
remains; no production build warning was emitted.

## APIs

All following types/methods are nested in the existing `Main` partial class, with
`internal` access unless noted. They run on the game thread; they are not a queue,
workflow DSL, durable action store or native-thread marshalling service.

```csharp
[Flags] enum ExecutionDomain {
    None = 0, Energy = 1, Magic = 2, Res3 = 4, Gear = 8, Support = 16,
    NativeSettings = 32, Route = 64, FiniteAction = 128, Inventory = 256
}
enum ExecutionLifecycle { Running, Quiescing, LoadingSave, Unloaded }

ExecutionClaim BeginExecutionClaim(string owner, string acquisition,
    ExecutionDomain domains, Func<bool> current);
void ApplyExecutionClaim(ExecutionClaim claim, SelectedSetupRequirements setup);
bool EndExecutionClaim(ExecutionClaim claim, bool reconcile = true);

ExecutionGrant ResolveExecutionGrant(string owner, string acquisition,
    ExecutionDomain domains, Func<bool> current, ExecutionClaim claim = null,
    ExecutionClaim[] resolvedClaims = null);
bool ValidateExecutionGrant(ExecutionGrant grant, ExecutionDomain footprint);
bool VerifyExecutionPrerequisites(ExecutionGrant grant, ExecutionDomain footprint,
    Func<bool> observe);
void InvalidateExecutionObservations(ExecutionDomain domains);
void SetExecutionLifecycle(ExecutionLifecycle lifecycle);

// Private, available to other Main partials:
void PublishExecutionBaseline(SelectedSetupRequirements baseline);
SelectedSetupRequirements ResolveExecutionSetup(SelectedSetupRequirements baseline);
void ApplySelectedSetup(SelectedSetupRequirements wanted);

// Existing shared resource ingress; signature unchanged:
ResourceMutationResult ReconcileStageResourceSetup(ResourceSetupIntent intent);
```

`BeginExecutionClaim` admits one stable owner/acquisition and explicit footprint.
Overlapping claims reject; the process router must retire an earlier exclusive
owner rather than relying on numeric importance. Its predicate must bind the real
process/entry/attempt and relevant permission/preferences. Gear/support claims
retain bounded equipped/material/support observations, never an original return
target. Foreign edits reject application/release and preserve native state.

`ApplyExecutionClaim` accepts already-selected exact requirements, not an optimizer
request. `ControlsGear=false` is uncontrolled gear. `ChangeSupport=false` is keep
observed support; an empty `SupportSelection` with `ChangeSupport=true` is an
explicit clear. Gear maps must include explicit empty slots. Support acquisition
is established internally. Keep the SAME acquired selection on retries and provide
`MaterialValid` for the selected instances. Native gear/support/resource outcomes
remain the existing typed outcomes, including model-committed/render-failed and
bounded partial progress. The exact gear actuator receives the grant callback;
global locks are no longer its authority for an explicit claim.

Use `Recall=None` for a preserving transition. Nonzero recall still needs lane
permission and the exact native actuator's capacity/accounting proof. Gear/support
application still waits on unresolved resource lanes: an unrelated healthy lane may
allocate, but cannot authorize a coupled setup change over yielded evidence.
A non-safe route requires an explicit Route claim plus the exact current
`RouteRetirementAuthorized` predicate. A grant alone does not prove native route or
boss retirement, and never cancels unknown/native boss activity.

`ResolveExecutionSetup` overlays current exact gear/support claims on the CURRENT
retained baseline without equipping a covered baseline first. Existing manual
adventure composition publishes baseline intent, as does focus acquisition.
`EndExecutionClaim` revokes the claim before resolving the current baseline; it
never restores its entry observations. No authorized successor means no native
setup write and no implicit NGU fallback. Release can be retried against today's
baseline after an actuator wait, without reviving the ended claim. Foreign evidence
continues to block replacement until reviewed. Old-epoch release cannot write.

`ResolveExecutionGrant` binds coordinator instance, lifecycle generation, native
epoch, settings instance, relevant managed-lane permissions, per-domain observation
revisions, admitted owner identities and caller predicate. A claim parameter must
match owner/acquisition and cover the complete requested domains. A different owner
cannot borrow its domains. The final `resolvedClaims` parameter is for the internal
composed-setup resolver, not a way for producers to acknowledge foreign ownership.
No-domain grants retain bounded observation identity without granting writes.

`VerifyExecutionPrerequisites` checks authority before AND after the supplied cheap
native observation and rejects unresolved requested resource lanes. It does NOT
manufacture a setup receipt or action proof. Route/finite-action callers must observe
their exact required gear/support/settings/funding and retain action-specific
invocation history. Call `ValidateExecutionGrant` immediately before and after EVERY
native callback, including field/input/selection boundaries. Do not serialize grants,
claims, delegates, native references, or cleanup rights.

`InvalidateExecutionObservations` revokes affected domain revisions and coalesces a
resource-service request. It does not clear native conflicts, acquired choices,
budgets, action history, native targets or allocations. Reentrant resource service
requests are retained for the next normal service instead of recursively mutating;
a dirty request bypasses cadence, not selection policy. No inventory/route search
was added to recurring observation. Existing AutoUpdate=false tests remain green.

`SetExecutionLifecycle` revokes all old grants by generation. Returning to Running
does not resurrect them. This slice exposes the hook; lifecycle ingress wiring and
bounded quiescing retirement authorization remain persistence-workstream work.

## Extraction Ownership

Moved source sections unchanged, retaining original method/field names and no
duplicated bodies. The mechanical extraction checked source-length conservation.
The original `Main.CustomAutomation.cs` remains core-owned.

| New file | New owner | Exact extraction bounds in the original source |
| --- | --- | --- |
| `Main.TitanAutomation.cs` | Route/titan | Fields `_customTitanGoldActive` through `_customTitanAutokillCooldownReason`; `CustomTitanAutokillFailureCooldownSeconds`; methods `TryRouteCustomTitanGoldKill` through `CancelCustomTitanFightKill`, inclusive. Includes autokill selection/projection/cooldown/restoration helpers and `StopAdventureAutoattack`. |
| `Main.FruitBloodActions.cs` | Finite actions | Iron Pill fields `_customIronPillPrepStartedAt` through `_customIronPillPlanName`; fruit retry fields/constant; methods `TryActivateCustomFruits` through `CustomFruitActivationAutomationActive`; `RunCustomBloodSpellAutomation` through `TryFlushCustomBloodToNumber`; `RunCustomBloodSpellToggles` through `ResetCustomIronPillPrep`, including `IronPillBloodReserved`. `CustomRebirthReady` remains in the original file. |
| `Main.ExecutionLifecycle.cs` | Persistence/lifecycle | Epoch fields `_customLastObservedRebirthSeconds`, `_customObservedEpoch`; `InvalidateCustomEquipmentApplication`, `ObserveCustomRebirthEpoch`, `InvalidateCustomRuntimePlanCache`, `ResetCustomAutomationRuntime`. Native cleanup bodies here are unchanged, NOT converted. |

Added all four production Compile entries, including `Main.Execution.cs`.
Added `ExecutionFoundationChecks.cs` to PresetIntegration and both consumers of its
shared partial fixture: CombatPrepIntegration and ManualAdventureOverlay. No edit
to shared `checks/CombatPrepIntegration/Program.cs`. Production and shared fixture
manifest ownership now returns to the parent/persistence workstream.

## Resource Deletions

Removed direct whole-lane `Recall` from `ReleaseOwnedCustomFocusResources` and
`StopManualGlobalPreset`. Their explicit stop requests now submit exact controlled
zero maps for still-owned managed lanes through the SAME resource executor;
basic training and unmanaged lanes remain excluded. These are deliberate releases,
not the no-goal rule, and existing successor/ownership admission remains intact.

Deleted `_atEntryResourceApplication`, including its reset replacement. Both
AT-entry safety releases now use `ReconcileStageResourceSetup`: exact AT zeroing
when unmanaged Magic W blocks entry, or exact managed Magic W zeroing before AT.
The existing AT/W safety calculation and explanatory blocker are unchanged.
Resource unresolved/render evidence now survives through the shared application.

## Remaining Integration

1. Route/titan: replace the extracted snapshot/lock setup/restore bodies with one
   admitted process claim plus exact selection; retain spawn/death/time/AT/W history.
   Retire the owned route before ending prerequisites. Use explicit Route and
   NativeSettings grants for exact route/tower/borrowed fields. Delete hidden Gold
   restoration in `CombatManager.ManualZone` only after callers migrate.
2. Finite actions: use setup claims and grants, but implement real action identities,
   durable admission, action-specific committed/unknown observation and post-effect
   invalidation. The extracted fruit/blood workflows are UNCHANGED old producers,
   not converted effects. Pit/harvest/purchases/boost/rebirth remain unfinished.
3. Persistence/lifecycle: wire the lifecycle hook into frame/command/load/unload;
   implement reviewed semantic rebind, manual/Quick durability and outer checkpoint
   recovery without importing cleanup authority. Convert mutation-capable invalidation
   in the extracted lifecycle file. Quiescing currently rejects ordinary grants;
   add only bounded retirement/return admission, not a blanket Running bypass.
4. Core follow-up: convert remaining producer-specific admission to explicit grants;
   `LegacyExecutionRouteAvailable` and the implicit selected-setup admission retain
   restrictions for still-shipped callers. They are not a completed lock deletion.
   New temporary setup resolution is native-tested with selected baseline A/B;
   full new-claim preset/inline/focus credit/funding/route timelines still need
   integration. In particular, preset resource replay must observe actual composed
   support, and route activation must yield to the Route claim, before migrating
   those producers. Do not equate a grant with protected credit or finite-action
   readiness. Existing accepted inline/manual/combat timelines still pass unchanged.
5. Core deletion pass: `ApplyCustomEquipmentSupport`, custom beard/digger writers,
   feature-local target leases/returns, `PresetExecutionBlock`, and other manager
   snapshot APIs still have live callers. Keep `GearRestoreLease`, saved/temp gear,
   `RestoreGear`/`RestoreTempLoadout`, digger snapshots and old manager APIs until
   route/action/ingress callers are migrated. No disabled legacy automation enabled.
6. Source-linked fixtures need the new authority compilation boundary. For example,
   SustainedFocus currently links `Main.CustomFocus.cs` but not `Main.Execution.cs`,
   and its exact-loadout stub lacks the existing actuator's `ignoreLock` argument.
   Parent owns manifests after this handoff; add the real grant boundary with
   appropriate native stubs rather than writable success/ownership booleans.
7. Final writer audit, all remaining WIP gates, UI/command compatibility and actual
   restart/+2h/+4h/+6h soak remain parent-owned. No release-completion claim here.

## Commands

MSBuild: `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`.

Production: `NGUInjector.sln /restore /t:Rebuild /p:Configuration=Release /p:GameFolder="C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" /v:minimal`.

Framework fixtures: their `.csproj /restore /t:Rebuild /p:Configuration=Release /v:minimal`.

Native invocation: each fixture EXE receives `NGUInjector/bin/Release/NGUInjector.dll`
and `C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed`.
Focused selector: `PresetIntegrationChecks.exe <dll> <Managed> --execution-foundation`.
Full suites use NO selector. All build/test calls used timeout 1,200,000 ms.
