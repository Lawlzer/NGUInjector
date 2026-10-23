# Execution Writer Inventory

Source inventory for the desired-state cutover. Inventory complete does not mean
conversion complete. Keep this distinction when updating `../../wip.md`.

## Ingress

| Entry | Current mutation reach |
| --- | --- |
| `Main.Update` | External commands, watcher reloads, resource reconciliation, F8 gear/digger operations, F7 save loading |
| `QuickStuff` | Recovery, automatic-stage start, beard rotation, blood toggles/NUMBER flush, resource refresh |
| `AutomationRoutine` | Boosting, harvest, pit, EXP purchases, blood casts, fruit activation, focus support, rebirth |
| Adventure/boss callbacks | Titan/ITOPOD/adventure routing and combat, Fu Manchu, native nuke/fight calls |
| External UI/WinForms/probes | Preset/focus/gear/allocation commands, stage changes, fight/rebirth, harvest, mutable probe verbs |
| Save loading/reset/unload | Authority invalidation plus several feature-local native cleanup/return operations |

## Current Conversion State

The current task/evidence source is `../../wip.md`. Chronological source-only
notes below predate the directly implemented inline cutover and are retained as
investigation history, not current blockers or proof of release completion.

| Domain | Current Authority And Remaining Cutover |
| --- | --- |
| Preset/focus/inline/combat resources | One ResourceSetupExecutor and per-model/lane evidence; healthy authorized lanes can continue while affected lanes/dependents remain unresolved. Combat exit resolves the current successor before release; unchanged TM handoff has zero extra accounting writes. Special unmanaged W/AT-entry cleanup remains separate. |
| Selected gear/support | ApplySelectedSetup owns exact gear then coupled support, current boundary authority, route retirement and cross-domain callback checks, including allocated-model/gear changes during retirement before gear actuation. Native-model-scoped gear/support/route conflicts survive ordinary resets. Reviewed stage actions bind entry and conflict revision; later foreign events invalidate review. No full old workflow is wrapped. Unselected producers below remain open. |
| Inline stages | Main.InlineStageSetup retains entry-bound selected instances/support independently of application validity and bounded ownership observations. Ordinary/quick paths, independent support-minute events, material revisions and fruit-cap returns use shared actuation and current intent. Full current setup plus resource proof admits credit. TryEquipStageGear, RestoreCustomStageGear and _customStageGearDone are deleted from production/C# fixtures. |
| Inline temporary claims | Policy-independent baseline composition resolves manual gear/support over current B before mutation; release goes directly to B. Keep gear and uncontrolled domains do not create historical return targets. Verified focus handoffs can update bounded observations while the original stage is paused; they do not renew its entry or budget. |
| Explicit optimizeGear command | Selection stays separate from mutation; exact selected application now enters the same typed actuator, preserving command acceptance/deduplication and rendering-error reporting. F8/WinForms/probes are NOT yet all converted. |
| Observation versus authority | Generic invalidation clears application validity, not foreign evidence. Native gear/support/model/bar changes across callbacks stop dependent writes. Idle-pool evolution is reobserved for funding, not mistaken for ownership of changed bars. Known partial planned support can choose an affordable replacement only after verifying mutation ownership. |
| Combat handoff/titan/ITOPOD/finite actions/persistence/lifecycle | Combat active mutation is written/native-tested, but its concrete handoff blockers remain OPEN, as do the other coordinated cutovers, identified finite histories, positive policy rebind and final writer deletion gate. No completed release/deployment is implied. |

Focused native inline acceptance is in `checks/PresetIntegration/InlineStageSetupChecks.cs`;
the full preset/ownership/BlackBar/source suites are rerun against the final DLL.
Exact commands, MVIDs and outcomes are in WIP. Do not use earlier pass totals as
evidence for later source changes.

## Historical Conversion Notes

Iteration-16 fresh scheduled admission correction (source only, UNRUN): the
explicit, marked current regular manual route can authorize setup admission and
the SAME selected-setup route retirement without requiring the old baseline
receipt to belong to the successor stage. The bounded predicate captures the new
owner/entry/generation, save epoch, exact observed gear/material, settings and
native attack preference; foreign/unmarked/titan/ITOPOD routes gain no permission.
Baseline selection/composition and actual-gear/support funding still use the
existing executor. No baseline is installed beneath a Keep-gear claim. The native
fresh-stage assertion is retained, with before/after resource/owner/scheduler/
prerequisite diagnostics and exact no-flash/AT-target/budget/credit/search checks.
Existing Compile entries cover both edited files; no new source or fixture file
was added or deleted. This does NOT convert independently acquired inline
gear/support, fruit restoration, legacy support cadence or `_customStageGearDone`.

Iteration-14 delegated route removal (source only, UNRUN): a completed regular-zone
manual delegation under a matching acquired preset now retains a typed current-route
lease independently of the reset activity flags. The lease binds epoch, receipt and
acquisition, stage/generation/entry/cycle, settings/managed-lane permissions, exact
observed gear/material/support, native autoattack and floor bounds. It supplies only
safe retirement through `ApplySelectedSetup` with no recall/support mutation, not a
historical return or permission to install a farming route. Claim reconciliation
must verify retirement before successor capture/application; ordinary cached preset
reconciliation then supplies the pinned current successor. Foreign route, epoch,
entry, preferences or native values block dependent work. Native retirement failure
retains the same claim for retry; verified retirement consumes it. Existing production
Compile entries cover both changed Main partials; no manifest addition is needed.
PresetIntegration's existing Program Compile entry includes new native entry-order,
failure and authority-negative assertions. SustainedFocus does NOT link these Main
partials, so its pass alone cannot validate this route change. Full compiled preset
and overlay gates against a fresh DLL remain required. Inline gear/support bodies,
fruit restoration, support cadence and `_customStageGearDone` are still unconverted.

Iteration-13 resource producer cutover (source only, UNRUN): inline setup and cheap
refresh now resolve `StageResourceRequirements` before application. The resolver
retains the existing ordered cap/share/AT/blood mathematics and acquired activity
pins but performs no native allocation writes. Application sends its exact map to
Main's same `ResourceSetupExecutor` as preset/focus; the stage-local ordinary
`ResourceApplication` is deleted. Stage reset cannot replace that shared application
or forget its pending rendering/unresolved native-lane evidence. A superseded
requirements object cannot apply, including another resolution in the same entry.
The old writable success flag is removed; its compatibility observation derives
from the verification receipt issued only after the shared outcome verifies.
`ApplyStage` remains a thin fixture/caller adapter over resolve/apply, not the old
full allocation body or a fallback writer. The special unmanaged-W/AT-entry safety
cleanup still has its separate `_atEntryResourceApplication`; it is NOT migrated.

Native resource regressions now check actual shared instance/outcome identity,
conflict retention across stage decision reset, healthy-Energy continuation with
foreign Magic preserved, exact write counts and no full-stage credit. Source checks
exercise read-only resolution, superseded same-entry rejection, basic-training/R3
exclusion and no repeated accounting after decision reset. All additions are UNRUN.
This does not convert inline gear/support or establish the requested complete
scheduled A/overlay/B/exit AT/W-history timeline. The exact remaining composition
seam is `ManualAdventureBaselineAuthorized` / `ComposeManualAdventureClaims` /
`ReconcileManualAdventureClaims`, which currently require a `GlobalPresetPolicy`
(Candidate/ExecutionPreset/Items/Material/Acquisition). Inline stages have no such
accepted selection record. `TryEquipStageGear`, fruit `RestoreCustomStageGear`,
legacy support cadence and `_customStageGearDone` must be replaced together with
an acquired stage selection and shared gear/support verification, not adapted by
fabricating a preset policy or treating resources-only verification as full setup.

Iteration-12 source transition (UNRUN): recurring unoverridden support rotation
may claim only the observed, marked current regular manual route. The existing
shared selected-setup actuator retires it before support writes; exact current
gear, support and funded resources must verify before native reentry to that same
zone. The session-local claim survives a known-partial support retry and does not
grant policy farming-route takeover, preference changes or ITOPOD floor authority.
Boss/nuke, foreign owners, changed gear/claim and unresolved resources still block
dependent work. Setup acquisition (not independent allocation refresh) now rejects
known missing route controllers/ownership before broad capture. PrepareSupport
rebuilds time-machine/blood/ITOPOD fixture models; the exact previous native getter
NRE remains unlocalized, not repaired by null defaults. Native barrier/count,
receipt projection, retry/cadence and original scheduler-budget assertions are
added but unrun. Complete scheduled AT/W history and global cutover remain open.

Iteration-11 source isolation (UNRUN): candidate copies now own their support object,
arrays and digger rows. Post-composition observations must not mutate acquired B;
cached planned/pinned replay still starts from policy support, not current overlay
support. Keep-gear manual claims supply exact observed gear while retaining B's
separate release intent. Active-zone capture rejects missing route controllers
before native capture getters; safe-zone observation adds no selector requirement.
Verbose preset admission probes/checkpoint payloads require
`NGU_PRESET_ADMISSION_DIAGNOSTICS=1`; concise root context remains unconditional.
These changes do not complete scheduled overlay/credit or global writer cutover.

Preset regression isolation: the linked fresh-Character fixture now uses a unique
checkpoint directory per Setup. Same-model reload tests retain their directory and
still exercise provenance quarantine; identical fixture rebirth clocks are not save
continuity. Stage telemetry projects owner-scoped execution waiting/blocked reasons
even when an application receipt exists. This projection grants no native authority
or stage credit. Iteration-10 edits and new assertions are unrun.

| Area | Native footprint / conversion seam |
| --- | --- |
| ResourceWriter | Exact E/M/R3 accounting; transfers, target returns and whole-lane recalls. Preset, focus and inline-stage ordinary resource maps now enter the same concrete ResourceSetupExecutor, retaining current detached intent and one application instance. Foreign/unknown callback evidence is native-model/lane scoped and survives replacement application instances; healthy lanes may reconcile without verifying the whole setup. This is resource-domain cutover only; special AT-entry cleanup and old explicit recall callers remain. |
| BlackBarAllocator/CustomStageAllocator | Ordered caps/shares and temporary native targets. Normal allocation now computes desired maps; admission and native-setting return lifecycle remain feature-specific. |
| Presets | Initial selected gear/support now enters ApplySelectedSetup with typed requirements and callback authorization; the corresponding direct application body is removed. Resource application uses shared ResourceSetupExecutor; resource-only conflicts retain acquired intent/leases and continue healthy lanes, withholding credit and coupled support changes. Global refresh/rotation still has separate support application. ApplyPreparedLoadoutPreset still owns capture, native targets, allocation verification, activation and receipt lifecycle; it is NOT a completely migrated shared executor. Whole-envelope import/reviewed rebind, event coalescing and overlay handoff remain. |
| Focus | Initial gear/support and recurring support enter the same typed ApplySelectedSetup ingress as presets; selection is separate and recoverable activation retains selected requirements. Magic-only gear uses an explicit Magic recall footprint and retains Energy/R3 capacity checks. The old PrepareFocusGear recall/equip body and initial legacy support call are deleted. Resource requirements enter the same ResourceSetupExecutor as presets. Stop, combat/adventure activation and lifecycle cleanup remain feature-specific; native boss/nuke retirement and complete successor-claim composition are not converted. |
| Combat Prep (offline slice accepted) | Active phase requirements enter shared ApplySelectedSetup/ResourceSetupExecutor; local ResourceApplication/release-instance, gear rollback and historical support restore bodies are deleted. Current-successor resource handoff and independent paused borrowed-target/counter returns pass native gates. Focus retains producer-owned gear/support/material/epoch independently of application evidence; verified focus admits public Preview/Start and cancellation/completion reconciles that CURRENT intent without search. Incomplete/foreign focus rejects before mutation/token consumption; focus replacement/stop remain idle-only. Route/model/callback, causal material and OS-v2 invocation behavior is native-tested. FULL CombatPrep11,985 and native inline279 PASS on MVID59d239d5; overall release/live gate remains separate. See combat-executor-verification-20261022.md. |
| GearOptimizer/LoadoutManager | Equipped/inventory instance swaps, selection inputs, capacity/pool effects, optional whole-lane recalls. Typed per-swap evidence and explicit E/M/R3 footprint exist; saved/temp snapshots and feature locks remain. |
| Main.Support | Coupled beard/digger state, exact levels and pod drafts. Selected transitions attach a setup authorization callback to each support mutation, checked before native writes, borrowed controller/input boundaries and still-owned draft cleanup. Existing non-selected callers remain separate. Valid overspend observation and exact mutation evidence exist; long-lived feature ownership is not yet centralized. |
| BeardManager/DiggerManager | Gold-beard removal clears diggers; affordable resizing, saved/temp restoration and digger purchases. Preserve calculation; separate configuration from purchases. |
| Manual Adventure | Preset acquisition resolves accepted baseline B plus retained selected gear/additive support BEFORE ApplySelectedSetup's first shared mutation, with no recall footprint under the claim. Current baseline requirements/policy are independent of the receipt. Removal supplies that baseline directly; uncontrolled removal writes nothing. Historical support returns, BaseGear, OverlayExitPending, receipt adoption and full-capture exit replay are deleted. Ordinary acquired-policy refresh verifies funding before reactivating its route. Source iteration 9 is UNRUN; scheduled B-transition credit and broader failure/cutover evidence remain incomplete. |
| Manual Titan | Spawn/death/time-bounded process, setup, native combat, safe-zone retirement, original-gear return. Preserve attempt history; centralize configuration. |
| Max ITOPOD push | Borrowed floors/shifter, route, death budget, optional gear/support. Exact borrowed-setting authority still needs conversion. |
| Gold/titan routing | Temporary loadouts/support, routes and historical returns. `CombatManager.ManualZone` also restores Gold gear internally after kills. |
| Yggdrasil | Optional gear/digger setup, ripe/forced harvest, activation and cap-gear detours. Split setup claims from per-fruit identified actions; current workflow retains cleanup. |
| Money Pit | Digger purchases, preview/prepare/feed, gear and Blood digger, Magic recall/ritual capping, returns. Full workflow must be split; wrapping it is not cutover. |
| EXP/digger purchases | Currency and permanent base/purchased progression, temporary input fields. Finite actions, not repeatable desired setters. |
| Blood operations | Auto-spell flags are desired configuration; NUMBER/Iron Pill/MacGuffin casts are finite effects. Preserve action history and uncertainty. |
| Inventory maintenance | Transform/filter settings plus destructive merge/boost/consume/turn-in operations. Protect reserved instances; emit owned material-change events. |
| Rebirth prep | Fruit consumption, purchases, fight support/nuke, final harvest/spells/NUMBER beard/rebirth. Prerequisites and finite-action identities remain to centralize. |

## Hidden Side Effects

### Iteration 6 support-successor slice (source only; validation pending)

`GlobalPresetReceipt.BaselineSupport` is the current acquired intent, replaced on
verified successor acquisition and ordinary unoverridden support commits. It is
not captured when an overlay starts. Additive overlay support resolves against that
intent, or current focus policy. The standalone uncontrolled additive basis is never
an exit write target. Both modifier removal and disable use the current successor;
the pre-acquisition historical support write is deleted entirely.

Actual support application now uses selected setup requirements with no resource
recall, coupled support budget enforcement, per-boundary authority and safe native
route/boss retirement. Native support partial/render evidence remains with the
existing support actuator; dependent manual combat returns without fighting on a
support wait. An observed completed manual route records its zone so disable can
retire only that still-matching route before removing prerequisites.

Overlay support conflicts are native-Character-keyed weak-table evidence, surviving
overlay reset and replacement Main instances. Scheduled acquisition cannot clear
them. Explicit Equip/Preview+Apply clears only at the fresh post-preflight reviewed
acquisition boundary, without restoring native state. This is scoped overlay support
yield, NOT a complete shared support authority migration for all other producers.
Read-only overlay telemetry exposes the retained support waiting reason.

New native overlay runner timelines cover real A -> overlay -> edited/reacquired B
-> overlay exit, no pre-acquisition A write, no redundant verified support write,
no support-exit optimizer and actual AutoUpdate-false cached B reacquisition, native callback partial/render failure, foreign support,
permission revocation, reset retention, explicit reviewed reacquisition and no-goal
removal. These checks are UNRUN, not accepted evidence.

Iteration 8 source slice (UNRUN): `TryApplyManualAdventureOverlay` retains exact
selected destinations/instances independently from its applied observation. Same-epoch
application reset or successor acquisition does not discard that decision. Explicit
loadout/weight edits select again; actuator errors do not try another candidate.
Application uses `ApplySelectedSetup` with `GearRecallFootprint.None`, native route
retirement and boundary authorization. Observed foreign/unknown gear outcomes create
native-Character-scoped review evidence, cleared only by the existing freshly
preflighted reviewed acquisition boundary, not reset. This does not yet detect all
foreign gear edits between passes or persist selected claims across sessions.
The unused `GearOptimizer.EquipGearPreservingResources` compatibility wrapper is
deleted after its final production caller was converted; `EquipGearObserved` remains
the actuator, not a parallel overlay writer.

Iteration 9 supersedes the before-B/exit gaps above (source only, UNRUN):
`ComposeManualAdventureClaims` resolves retained exact gear and pure additive support
over B at ApplyPreparedLoadoutPreset, before ApplySelectedSetup. Applied funding is
evaluated with actual composed support, not B's uncovered support. The accepted
baseline/policy is retained separately from the receipt; receipt support is now only
a projection. The four named historical adoption/exit symbols are deleted, including
their production and reflection-test callers. Claim removal uses no full planning
capture or optimizer, preserves resources, and retires only its observed route through
the selected actuator; ordinary cheap acquired-route refresh owns reactivation.
Selection keys include baseline definition changes when AutoUpdate authorizes a new
selection; false retains the acquired selection without replacement search. Cross-pass
gear observations survive application reset, while foreign/unknown actuator outcomes
remain Character-scoped. Known-partial observations advance only on bounded evidence.

The native successor case now records actual head swaps: A -> combat overlay -> real
NGU-head B acquisition -> cached AutoUpdate-false B -> exit. It asserts no B flash/A
detour, direct B on exit, retained selected instances and native unmanaged R3 work.
This is added source, NOT a passing result. Scheduled stage entry/budget/AT/W-credit
across actual B acquisition and expanded native gear partial/foreign/permission cases
remain unproven. Inline/focus baselines, other support producers' shared conflict
authority, persistence and broader lifecycle cutover remain subsequent work.

### Iteration 5 prerequisite-credit correction (source only; validation pending)

Rotation admission and allocation continuation share an execution prerequisite gate.
Rejected prerequisites suspend only the matching current preset stage's credit;
stale/manual owners do not suspend another stage. Read-only receipt predicates stay
pure for telemetry. Healthy resource-lane continuation is not globally disabled by
this credit gate. Missing native boss observation is not safe retirement evidence.

Unsafe due rotations remain unaccepted (no pending choice/draw/cadence consumption).
The native timeline now explicitly admits when safe, fails route retirement, loses
native authority on that accepted event, then interrupts support behind the observed
safe-zone barrier before retry. An owned retired route can enter continuation while
safe, but cannot validate stage credit until exact support/funding and route resume
verify. Assertions retain entry, entered-at, deadline and original selected time/draw.
Permission/gear-lock/support-lock and stale-owner credit assertions are included.
All new executable assertions remain UNRUN by this worker.

### Iteration 4 selected-refresh update (source only; validation pending)

Superseding the refresh/boss gaps in the conversion rows above: global recurring
refresh/rotation now supplies selected requirements to `ApplySelectedSetup`, rather
than directly calling `ApplyPresetDesiredSupport`. Only changed support takes the
coupling gate. Gear is the current equipped map with no recall; broad inventory,
route alternatives and optimizer searches are not introduced. Pending acquired
rotation decisions survive retirement waits and known-partial support continuation.
Changed selected prerequisites require available, inactive native boss/nuke
observation throughout native mutation boundaries; `StopBossPush` is not retirement.
No native boss/nuke cancellation is introduced.

Owned active preset routes retire before changed support. Recurring route resume
requires verified exact support/funding and current receipt/owner/boss authority,
rechecked at floor/input/zone boundaries. It refuses to enable native autoattack.
Bounded single-floor observations can validate that exact floor without full capture.
The new `--shared-support-transition` native selector covers real regular-route
rotation, boss/nuke waits, injected native retirement failure, safe support callbacks,
verified reactivation and steady-state write/search/capture counters. It is UNRUN.

Iteration-4 remaining list, partially superseded by iteration 6 above: inline-stage
support, complete gear/current-successor composition, shared cross-producer support
yield/reauthorization, combat/adventure/titan/gold/ITOPOD temporary setup/returns and
finite-action/lifecycle cleanup remain. Iteration 6 deletes the manual-overlay
historical support returns, not the complete restoration/receipt pipeline.

- Gear may recall every globally managed lane, not merely the lane that requested gear. Typed callers must authorize the entire footprint and protect basic/unmanaged allocations through intermediate capacity.
- Deactivating Gold beard clears diggers. A beard-only change is not isolated from support.
- `ObserveCustomRebirthEpoch`, `InvalidateCustomEquipmentApplication`, runtime-plan invalidation, setup resets, protected-stage reset and unload can invoke native cleanup. Their names do not make them observation-only.
- F8, WinForms harvest/equip and startup probe verbs bypass ordinary recurring dispatch.
- Harvest/boost work occurs before the Combat Prep early return. Pre-rebirth MacGuffin actions remain reachable even while ordinary legacy allocation is disabled.
- `StopBossPush` clears injector flags, not native fighting/nuking. Stop/handoff must observe the native activity before removing required support.

## Disabled Legacy Paths

`LegacyAutomationEnabled` is false, but compiled allocation breakpoints, equipment/
OS switching, quest operations and broad inventory maintenance still exist. Keep
them explicitly unreachable or remove them. Do not use disabled complete workflows
as new actuator adapters or accidentally reactivate them during extraction.

## Fixture And Manifest Boundaries

Selected setup retirement now distinguishes observed native safe zone `-1` from an
unknown active route. Changed inputs require available selector/player/enemy
controllers before policy persistence, then fresh owned-route authorization or an
explicit takeover before native retirement. A matching current farming receipt is
checked against its epoch, current owner selection and actual zone/floors; disabled
automation alone is not ownership. The safe-zone barrier remains checked through
gear/support callbacks. This is not native boss/nuke retirement or a complete
successor-claim lifecycle. Resource coordination fixtures initialize route controllers
and enter safe mode through the fixture's existing native zone-entry slice, rather
than treating default zone 0 as idle. Source-linked focus uses its actual character
inventory-controller boundary for `midDrag`; exact support recordings represent
projected levels, not legacy weighted-manager requests. Executable validation of
these integration repairs is still required.

Production has explicit source includes in `NGUInjector.csproj`. Pure Preset and
NumericIntegration manifests both include planner helpers. Source-linked writer,
focus, ordinary-policy, support, combat-routing, bridge and purchase fixtures need
matching compilation boundaries when APIs change.

`CombatPrepIntegration/Program.cs` is shared by Ownership, PresetIntegration,
ManualAdventureOverlay and TitanGold. PresetIntegration's fixture is also linked by
combat/overlay checks. New fixture-only mutation observers must preserve those
consumers and observe actual committed writes, not obsolete transfer/recall calls.

## Deletion Gate

Every resource transfer/return, native swap/support setter, native target assignment,
route/tower toggle, cast/purchase/consumption and reset/unload write must terminate
in an authorized actuator or identified action, or be deliberately unreachable.
No old workflow may run after the new executor has started changing a shared domain.
The currently passing subset does not satisfy this full deletion gate yet.
