# Desired-State Executor Plan

> Historical, non-operative design record. Implementation statuses, durable replay
> assumptions and old commands below are not current tasks or authorization.
> [Current WIP](../../wip.md) and the newer [action/session contract](action-state-simplification-20261023.md)
> override stale requirements/statuses. Use [Testing And Save Safety](testing.md)
> and [agent workflow](agent-workflow.md) for current entrypoints and ownership.
> Do not resume historical work or live operations from this document.

## Status And Goal

Proposed end-state for a coordinated execution-layer refactor. This is a design,
not an implemented migration or permission to change the live game. The user has
explicitly allowed a large refactor provided its end-goal and behavioral changes
are clear. Product defaults below are the accepted baseline; the useful-cap,
active-weight and explicit-review decisions in `../../wip.md` take precedence.

**One execution authority derives the present setup from active goals and durable
commitments, observes native state, applies authorized differences, and verifies
the result. Features stop owning independent apply/restore/recovery pipelines.**

Replace execution ownership, not the mathematical planners. Ordinary priorities,
weighted presets and combat-preparation forecasting retain their distinct policy
semantics behind a common execution result.

## Current Problem

`Main.ResourceCoordination` dispatches to independent preset, inline-stage, focus
and combat paths. Those paths still manage their own native writes, observations,
target leases, snapshots, restoration, retry decisions and failure flags.

The result is that a feature can forget the desired goal while remembering a
historical obligation, or interpret a completed write followed by a rendering
exception as an unknowable application. Another loop cannot safely repair it
because ownership information was discarded.

Existing useful foundations include `ResourceProjection`, `ResourceAllocation`,
`SupportProjection`, exact gear operations, `ResourceWriter`, the pure planners,
`ProtectedStageScheduler`, and baseline-plus-blood `ComposeResourceDemands`.

## End-State Components

Use four concrete responsibilities rather than a generic workflow framework:

| Component | Responsibility |
| --- | --- |
| Intent and commitments | Selected baseline, active temporary requests, permissions, stage/phase history and acquired decisions |
| Requirement resolver | Compose compatible requests and invoke existing selection/allocation calculations |
| Reconciler and domain actuators | Observe, plan the native transition, apply authorized changes, verify and publish per-domain outcomes |
| Finite-action runner | Execute identified purchases, casts, harvests, fights and rebirth operations only after their prerequisites are verified |

These can use ordinary data records and explicit code. No runtime policy language,
arbitrary constraint solver, event-sourcing platform or generic transaction graph
is required.

## State Categories

| Category | Examples | Reset/reload rule |
| --- | --- | --- |
| User intent | Selected plan/preset, preferences, management permissions | Preserve until user changes them |
| Execution commitments | Stage entry, credited work, original deadline, acquired AT gate, W/OS history, action identity | Preserve and persist where required; never recreate a budget through recovery |
| Acquired decisions | Selected equipment/support/activity policy and acquired definition | Separate from application evidence; reset must not authorize a replacement search |
| Application evidence | Observed amounts, verified setup, bounded intermediate mutation evidence | Recapture; retain unresolved operation evidence until its disposition is established |
| Native progression | Levels, spent currency, consumed fruits, fight/death outcomes | Observe; not rollback state |

An application reset means that nothing is assumed applied and affected state is
reobserved. It does not reset goals, native progression, deadlines or finite-action
deduplication.

## Effective Intent

Each feature supplies requirements for only the domains it actually needs:

- E/M/R3 allocation policy and authorized target set.
- Gear requirements or selection preferences.
- Coupled beard/digger requirements and budget policy.
- Borrowed native targets, timer or tower settings.
- Adventure route and safe-entry requirements.

Every domain distinguishes **uncontrolled**, **controlled empty**, and **controlled
with a desired value**. Missing intent must not be interpreted as a request to
clear manual allocations, remove support or equip empty slots.

Composition order is explicit: permissions/epoch/manual restrictions, safety and
existing commitments, compatible exclusive phase/action requirements, temporary
reservations, then baseline work and eligible remainder. Resolve conflicts per
domain and dependency, not with one globally winning owner.

Objective weights and sharing ratios remain soft preferences. Resource
conservation, purchased limits, finite targets, acquired AT gates, safe routes,
original deadlines and OS restrictions are hard constraints. Ordered priorities
remain ordered; they are not approximated with enormous weights.

Example: a blood request borrows an authorized magic budget without claiming gear
or energy. When it ends, resolve current baseline magic using current resources.
A fight can claim gear/support without unnecessarily stopping baseline resource
allocation. Incompatible requests defer according to declared rules.

## Reconciliation Pass

1. Validate current coordinator generation and native save/rebirth context.
2. Capture cheap relevant observations and process identified events.
3. Advance scheduler and phase controllers from observed completion.
4. Resolve effective intent and its domain dependencies.
5. Reuse selected decisions, or select again only at an authorized event.
6. Validate the entire mutation footprint and construct an explicit transition.
7. Persist new commitments before dependent native actions/funding.
8. Safely retire an owned route before removing gear/support it depends on.
9. Apply required gear/support/borrowed-setting changes and verify outcomes.
10. Reobserve affected capacities, income, rates and actual available resources.
11. Calculate exact desired allocations; release excess before funding deficits.
12. Verify required conditions before admitting actions or crediting work.

There is one game-thread mutation authority. Frame, quick, ordinary, command and
unload entry points request/service that authority instead of calling independent
writers. Read-only preview and telemetry do not advance or apply execution.

## Mutation Footprints And Outcomes

Permissions cover side effects, not just requested output. Gear swaps can affect
E/M/R3 and intermediate capacity. Removing Gold beard can clear diggers. These
operations need authorization for the complete footprint before starting, or a
resource-preserving alternative. A globally enabled management setting alone is
not proof that a yielded domain can be touched.

Preserve exact accounting, checked integers, basic training reservations, unmanaged
resources and intermediate gear-capacity preflight.

Domain actuators return a small structured result:

| Result | Response |
| --- | --- |
| Not attempted / rejected unchanged | Wait for the relevant condition or input change |
| Applied and verified | Proceed and record verified progress |
| Known partial | Retain bounded mutation evidence; observe and finish the remaining difference |
| Model applied, rendering failed | Do not replay the model write; retry presentation separately |
| Foreign conflict | Preserve the unexpected change; yield affected authority and dependent unsafe operations |
| Unknown native outcome | Observe if resolvable; otherwise report uncertainty for that operation, not a blanket failure of unrelated work |

The evidence records pre-state, authorized footprint, permitted partial outcomes,
observed post-state, epoch and acquisition identity. A catch-time snapshot is not
sufficient proof of ownership. Additive transfers are never blindly replayed.

Transient errors use bounded, condition-sensitive backoff. Changed rejected inputs
can reopen preflight. Old generations cannot write, and a failed return cannot
resurrect an ended process. Invalid configurations remain actionable blockers;
they do not justify pretending work succeeded or reoptimizing without permission.

## Handoff And Stop

If a successor currently claims a domain, apply its desired setup directly. Do not
restore a historical setup only to replace it immediately.

If no goal claims that domain, preserve native/manual state. Narrow return records
remain for explicitly borrowed native configuration or an explicit return baseline.
They restore only still-owned values in the same epoch and never overwrite a
successor or user edit. Dangerous running activities must stop/handoff safely before
their supporting configuration is withdrawn.

Stop, pause, baseline replacement, stage restart, rebirth/save load and unload are
distinct transitions. Only explicit stage restart creates a new entry/budget.
Unload retires write authority and performs only authorized release; it does not
promise to undo arbitrary native progression.

## Scheduling And Irreversible Actions

Preserve the existing stage-kind distinctions:

- Fixed protected stages complete from verified applied time.
- Mandatory AT stages complete from actual required targets.
- Flexible and automatic-preparation stages use their applicable elapsed deadline.
- Waiting/retry/reload never manufactures credit, resets the entry or renews time.
- Temporary preflight failure does not itself consume a preparation row.

Combat phase controllers retain acquired unfinished targets, completed phase
history and no-paid-AT-after-W restrictions. The coordinator applies phase intent;
the controller decides legal phase transitions.

Purchases, spells, fruits, pit feeds, boss entry and rebirth remain finite actions,
not recurring desired-state setters. Each has a stable operation identity,
preconditions, observed outcome and action-specific uncertainty/retry policy.
Execution revalidates permissions/epoch/revision/entry, even after command acceptance.
Existing external-command at-most-once claims must survive asynchronous admission.

Exactly-once effects across arbitrary game-save/checkpoint rollback cannot be
guaranteed. If evidence is ambiguous, do not blindly repeat an irreversible action.

## Selection, Performance And Persistence

### Implemented observation boundary (partial cutover)

Resumed iteration 1 adds a **resource-domain cutover, not the complete setup
executor**. Preset and focus requirement maps enter `ResourceSetupExecutor` and
retain detached current intent plus one shared application instance. Admission
rechecks coordinator, stage/focus, epoch and permission context. Native-model-scoped
lane evidence survives application replacement; a Magic callback conflict can leave
Energy verified while the complete setup remains unverified. Acquired preset intent
and target leases survive resource-only conflicts. New coupled acquisitions/support
changes are withheld while resource uncertainty remains. The old preset/focus
gear/support/route/stop pipelines still exist; wrapping or renaming those is not the
remaining migration. This change has not yet passed independent validation.

The evidence currently records callback-detected uncertainty, not all manual edits
between passes. Reviewed lane reauthorization, comprehensive dependency footprints,
safe native route retirement and the coupled real-adapter failure timeline remain
required before release. No permanent-quarantine completion is claimed.

`RefreshGlobalPresetExecution` uses equipped-only observation before and after
support application for acquired presets, including farming weights (indices 3–5).
It captures live pools, caps, coefficients, progress, targets and coupled support,
retaining native forward-parity and desired-affordability checks. It does not
convert inventory alternatives, fingerprint the inventory, enumerate zone tables,
or build ITOPOD floor forecasts. `_presetPlanningCaptures` and
`_presetExecutionObservations` count entry to the actual capture modes. They are
diagnostics, not policy or write authority. Selection remains outside this pass.

A full planning capture can record an incidental route even with zero farming
weights. Resource-only observation omits that unrequested forecast prerequisite
while preserving the acquired route/floor identity; it grants no route mutation
permission. Positive farming weights observe only the acquired regular-zone row or
one acquired ITOPOD floor. Current PP, conservative native enemy HP/defense, boost
recycling/filter values, drop/respawn and safety recommendations remain live; native
Lazy ITOPOD and missing-enemy one-hit constraints retain the real repeatable maximum,
not the size of the one-row observation. No inventory alternatives, route-table
enumeration or floor forecasts are captured. Diagnostic route-table and forecast-row
counters cover the broad boundaries as regression controls.

Route identity is independently bound in the snapshot even during the unpinned
funding pass used by reviewed priorities. Recomputing funding must not enumerate
routes or accidentally access an absent floor table on a regular-zone observation.
Acquired candidates carry direct regular/boost eligibility witnesses (prefer the
selected zone when applicable). These preserve availability/zero-yield metrics for
mixed farming goals without scanning alternatives. A lost witness waits for a new
authorized acquisition rather than searching another witness; failed live boost
economics remain unavailable even when the acquired eligibility witness is valid.
These are session-local descriptors, not durable save/reload identity or write authority.

Acquisition/Preview/Apply continue to use full planning capture. Dynamic-task changes
still pass through the existing authorized acquisition gate; ordinary refresh reuses
the acquired resolved intent. Event coalescing, durable descriptors and other recurring
paths remain unfinished. This is not the full executor cutover.

### Accepted execution decisions

- Energy and Magic have independent reviewed orderings. Each eligible activity
  receives its productive/sustainable useful cap or remaining completion demand
  before surplus reaches the next activity; an uncapped activity may consume its
  lane. Within-activity NGU ratios and zero exclusions are retained. Mandatory
  eligibility/permission gates cannot be bypassed as low utility.
- Shared setup uses existing active importance weights, with each activity's
  utility gated by prospective legal funding under that candidate setup. Neither
  lane has unconditional dominance. Recall-induced zero funding is not a goal
  removal; recompute after support/capacity changes.
- Temporary suppression retains dormant fallback definitions. Ending a request
  resolves current baseline intent, not saved allocations. Uncontrolled domains
  stay untouched; controlled-empty is explicit. Manual changes yield affected
  authority and unsafe dependents until explicit resume/acquisition.
- Pause/stop/reset do not create a new stage entry, deadline, AT gate or finite
  action identity. Only explicit restart creates a fresh budget. Application
  evidence is rebuildable; acquired intent and progression are not rollback data.
- Existing definitions are preserved. Proposed lane mappings require explicit
  review before activation; saved edits do not replace the acquired definition.
  AutoUpdate false never authorizes replacement search, including after reset.
  Missing or ambiguous durable selected-instance descriptors remain unresolved.

These rules specify target behavior; the migration, persistence, action and writer
deletion gates below remain implementation requirements, not completed features.

Separate cheap execution observations from expensive forecast/selection capture.
Ordinary allocation ticks must not enumerate inventory-wide alternatives or build
thousands of route-forecast rows. Instrument this rather than merely claiming that
no optimizer method was called.

Expensive selection is event-driven: explicit acquisition, new stage, relevant
inventory/unlock/slot change, accepted material/economic change, phase completion,
or changed adventure task. Coalesce events; passive gold/progress/time increments
are not automatic permission for another gear search.

AutoUpdate false retains selected decisions while recalculating current amounts.
Saved-definition edits do not silently replace acquired intent. Preview Apply still
applies the selected candidate, never a newly optimized substitute.

Persist versioned semantic commitments and acquired decisions separately from live
authority. Within a session, use exact equipment references. Across reload, validate
saved selection descriptors against the current save context, location/material and
unambiguous candidate identity. Moved unique matches may resolve; missing or
ambiguous same-ID instances remain unresolved, not silently substituted. Causally
observed material growth is distinct from an unexplained save/instance change.
The game does not currently supply a proven durable unique equipment identity;
the descriptor/migration rules must be specified and tested before claiming policy
restoration support. Never turn an application reset into a forbidden new search.

Iteration 3 adds an isolated equipment-identity serialization/resolution boundary in
`Main.GlobalPresets.cs`. Its version-1 envelope contains the acquisition token,
`equipment-primitives-v1` material schema and ordered target-slot/item-ID/approved-
material rows. Source location is only a hint. Export uses the policy's approved
material, not fresh native values; existing causally observed boost revaluation is
therefore distinct from unexplained growth. Resolution accepts a moved unique exact
match, but rejects missing, changed-material, duplicate/aliased and reused instances,
even if the old location matches. Empty is explicit. Versions, numeric identity,
shape, acquisition and target slots are validated without coercion. Returned native
references are transient resolution results, not serialized authority.

Iteration 11 uses this codec inside a detached selected-policy snapshot, durably
written before scheduled preset support/gear/resource application. Checkpoint
version 2 retains the existing scheduler, original automatic setup timing, acquired
automatic AT continuation and fight identity alongside this snapshot; version 1
progress remains readable. The selected-policy schema is independently versioned.
It records original/resolved definitions, original horizon, acquisition/owner/entry
and clock provenance, mandatory AT gates, approved equipment material, route and
eligibility witnesses, support selection, acquired AUG/OS, fixed legacy activity
decisions and frozen scoring references. Fixed amounts are semantic inputs to the
legacy replay evaluator, never proof that native accounting committed. No receipt,
native reference, historical return lease or rebuildable priority-funding result
is serialized. Subsequent runtime object edits cannot mutate the stored string.

This is a capture/quarantine boundary, NOT a completed restoration importer. A
restored active ordinary preset entry, including legacy progress without a policy,
is held before entry fights, broad capture, optimizer search and selected setup
application in both AutoUpdate modes. Envelope version, entry and necessary save
clock checks produce scoped reasons; even a match remains unresolved because these
heuristics cannot prove continuity against same-clock/same-material save branching.
Unresolved bytes survive further progress saves and unrelated owners are not gated.
Only explicit stage restart creates a fresh entry; quarantine does not renew its
budget or credit. Existing automatic-preparation recovery and separate native W/OS
history are not imported as preset authority. No live library or deployed checkpoint
was converted. Full semantic import validation, a proven continuity witness or
reviewed same-entry rebind protocol, dashboard/Quick persistence, approved-material
revision persistence and lifecycle migration remain cutover requirements. The outer
checkpoint's old mismatch-to-uncredited-progress behavior is not redesigned here.

Import existing checkpoint progress, deadlines, AT targets, W/OS history and action
identities. Do not import cleanup flags or saved snapshots as native write authority.
Game saves and sidecar checkpoints are not one atomic transaction; uncertain or
branched history must not fabricate completed work.

## Current To Target

| Current area | Target role / removal |
| --- | --- |
| ResourceCoordination | Sole reconciliation/application lifecycle, not dispatcher to competing executors |
| Global/LoadoutPresets | Definition/preview/acquisition adapters and selected policies; remove blanket failure latch and private apply/restore lifecycle |
| CustomStageAllocator | Preserve priority allocation calculation; remove independent writes, overlays and application receipt |
| AutoCombatPrep/CombatPrep | Domain process and commitments plus phase requirements; remove separate resource writer and restoration/recovery stack |
| ResourceWriter | Exact accounting actuator with operation outcomes, not a rollback engine |
| Support/Loadout managers | Observe, preflight and apply exact configurations; centralize long-lived ownership and complete side-effect authorization |
| Manual Adventure | Compatible gear/support preference claims; baseline resources continue where allowed |
| Manual Titan/Boss/ITOPOD push | Bounded action/process history plus temporary setup/route claims |
| Yggdrasil/Money Pit/rebirth prep | Identified finite workflows whose setup is supplied by the shared executor |
| Inventory maintenance | Serialized finite item operations and material-change events; protect selected/reserved instances |
| Protected timing | Scheduler and durable commitments; credit from verified effective application rather than multiple feature-specific receipts |
| UI/runtime status | One authoritative projection of desired/applied/waiting/blocked state and the specific affected domain/reason |

Keep exact local mutation journals where needed; remove duplicated long-lived
feature ownership and restoration ladders. Do not reactivate disabled legacy
automation during migration. No fallback may run old writers after new-executor
mutation.

## Accepted Product Defaults

These defaults are the WIP's accepted baseline, subordinate to its explicit
useful-cap, active-weight and explicit-review choices:

| Question | Recommended default |
| --- | --- |
| No active baseline | Preserve native gear/support/route; no implicit NGU fallback |
| Temporary task ends without successor | Preserve setup unless an explicit return baseline was acquired; release owned borrowed settings/resources safely |
| Unexpected manual edit in a managed domain | Yield the affected domain and dependent operations until explicit resume/acquisition, rather than fight the edit |
| Saved active preset edit | Keep acquired definition until explicit re-equip or documented new acquisition event |
| Planned diggers become unaffordable | Rebudget within authorized policy; keep-current/fixed preview selections do not silently resize |
| Dashboard Equip while plan runs | Preserve existing explicit takeover/pause semantics, moved into intent transition before execution |
| Native command outcome uncertain | Do not replay the command; internal known-state convergence is a separate operation |

The no-successor setup rule changes some existing standalone restore behavior.
If return-to-start is desired, model it explicitly as a baseline, not a privileged
historical rollback chain.

## Delivery And Completion

One coherent refactor can have internal workstreams without many public mini-releases:

1. Settle behavioral defaults and inventory every native writer and lifecycle entry.
2. Characterize native partial outcomes and implement the actuator contract.
3. Extract intent/calculation from current execution paths; keep mathematical planners.
4. Implement resolver, shared application lifecycle and finite-action integration.
5. Define checkpoint/acquired-decision migration and command/telemetry compatibility.
6. Cut over all shared-domain writers in one runtime activation, then delete old paths.
7. Run offline fault/timeline coverage, performance checks and controlled live verification.

The writer inventory must include action internals, background timers, command
handlers, harvest/pit preparation, spell toggles, boost operations, reset and unload.
An adapter calling an old complete workflow does not count as eliminating its
hidden writers. Handover requires quiescence; no simultaneous old/new mutation.

Acceptance requirements:

- Unchanged desired state produces no redundant model writes.
- Temporary-demand removal converges to the current baseline without restoring old allocations.
- Resource accounting is conserved and every unmanaged/yielded domain is preserved or rejected before mutation.
- Every injected partial-write boundary either converges from proven progress or reports a scoped unresolved result.
- Rendering failure never duplicates transfers or finite actions.
- Acquired AT gates, stage-kind timing, deadlines, entry fights and protected credit remain correct across pause/retry/reload.
- Overspending support can be observed and replaced by an affordable authorized target.
- Foreign edits and save/entry changes invalidate dependent authority, not grant permission to adopt arbitrary state.
- Steady-state allocation performs no full setup search or full forecast capture.
- AutoUpdate false performs zero replacement searches, including after reset/reload.
- Action admission/invocation/verification/checkpoint/result-publication failures do not cause blind replay.
- Telemetry is read-only; obsolete callbacks cannot write after handover/unload.

Primary technical risk: native operations may not expose enough evidence to
attribute every partial result. Validate resource-render failures, partial swaps,
support clear/submit failures, foreign callbacks, epoch changes, route retirement
failure and persistence failure using the real adapters. Where evidence is
insufficient, narrow that operation's retry authority instead of reintroducing a
global permanent failure flag.

## Scope Exclusions

No universal optimizer, user-programmable workflow DSL, speculative progression
model expansion, automatic replacement of chosen preview candidates, unrelated UI
redesign, or guarantee of perfect rollback/exactly-once effects across arbitrary
save branches. This is a broad execution rewrite with fewer execution concepts.
