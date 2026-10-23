# Resource Coordination

Implemented 2026-10-20. This replaces resource handoff/restoration bookkeeping
with live composition, while keeping equipment ownership, native-target leases,
Combat Prep cleanup and task deadlines as separate safety concerns.

## Execution Model

`Main.ResourceCoordination.cs` is the recurring allocation entry point for frame,
quick and ordinary automation callbacks. It selects one baseline:

1. Active Combat Prep execution or pending cleanup, serviced before throttling.
2. The running plan's current stage, or the manually selected dashboard preset.
3. A selected legacy sustained focus.
4. No selected resource activity: stopped.

Frame/quick/ordinary allocation shares one two-second refresh cadence. Changes
to selection, management switches, global NGU ratios or Iron Pill demand bypass
the ordinary throttle. Setup and stage-entry fights remain explicit operations;
quick reconciliation can establish a newly selected stage instead of depending
on unrelated earlier work in the ten-second loop.

`ReconcileGlobalPreset` is shared by dashboard and stage adapters. Stages retain
their horizon, AT targets and verified training-credit responsibilities. Manual
preset takeover and explicit Stop retain their existing plan-pause semantics;
Stop does not silently resume an old plan.

## Live Composition

The acquired preset is an immutable baseline policy, evaluated against fresh
game inputs. Temporary requests affect only the effective allocation:

- Eligible Iron Pill preparation gives sustainable blood production priority
  over the shared magic pool.
- Eligible ordinary loot/gold blood catch-up uses the same composition path.
- Iron Pill and ordinary catch-up do not stack duplicate blood requests.
- Energy and dedicated Time Machine/Wandoos magic allocations are preserved.
- Unused useful/affordable blood capacity falls through to baseline NGU sharing.
- When a request ends, the next pass recomputes the current baseline. No old
  allocation map is restored, including if the baseline changed in the meantime.

Weighted presets compose on initial acquisition as well as later refreshes via
`LoadoutPresetPlanner.ComposeResourceDemands`. Inline stages and legacy focus
compose effective priorities through `CustomStageAllocator.EffectiveStagePriorities`.
Temporary inline blood ownership is observed solely to release an exact
still-owned ritual when demand ends with an empty baseline. It never restores
old allocations or authorizes recalling a newer manual allocation.

Global NGU ratios are live resource inputs for both dashboard and stage
presets, independent of AutoUpdate. Ratio changes invalidate previews but do
not delete the acquired gear/support policy, strand cached restoration or
launch a gear search. Explicit cached equip/restoration uses current ratios.
Saved preset-definition edits still require an admitted equip event; this does
not turn autosave into implicit execution.

Fresh composed allocations that already match the entire managed destination
map require no recalls, native writes or rendering. Wish allocations and all
unrequested activities participate in that comparison; foreign allocations are
not adopted through the no-op path.

## One Resource Writer

`Managers/ResourceWriter.cs` is the low-level mutation boundary for automation:

- Exact energy, magic and R3 transfers and owned-target returns.
- Native lane recalls, fruit activation and Money Pit ritual caps.
- Serialized batches used by the coordinator and equipment operations.

Stage, preset, focus, Combat Prep and auxiliary optimizer/gear/fruit/pit paths
use this writer instead of independently changing resource fields. Compiled
legacy profile/breakpoint mutation entry points reject execution while legacy
automation is disabled. Their historical implementations are not fallback
writers.

Transfers validate source/destination balances and checked integer arithmetic
before writing. Both accounting sides are committed before fallible native
text rendering. Rendering exceptions remain observable; they do not cause a
one-sided debit or a fictional rollback.

Native recalls require the exact post-map and idle return, including every
backing wish slot, retained basic training and unchanged unowned lanes. A
nonthrowing no-op or partial recall is not accepted as successful application.

This is one writer for injector-issued allocation changes, not an interception
of the game's own resource generation, native target advancement or capacity
clamping. Calls still belong on the Unity thread. The reentrant writer lock
serializes operations; it does not make arbitrary Unity calls thread-safe or
make a multi-step operation transactional.

## Timers And Permission

Iron Pill retains its preparation start time, timeout/backoff, one-time verified
NUMBER flush, spell suppression and confirmed native cast/history. These are
task progress and irreversible actions, not saved resource restoration state.

Iron Pill requires managed magic, a selected resource activity, the selected
plan's Iron Pill permission and the existing native/blood eligibility rules.
Auto-rebirth permission is independent and no longer disables resource-side
Iron Pill preparation. A running plan supplies its blood settings; dashboard
and legacy manual modes use global blood settings consistently for eligibility,
saving thresholds and spell execution. Ordinary blood demand uses the same
short-rebirth spell policy as actual casting, so it cannot reserve magic for a
goal whose spending spells are deliberately suppressed.

Unmanaged lanes are not lower-priority requests. They are outside permission:
fruit activation skips them even with sufficient idle resources, and equipment
preflight protects their full idle-plus-funded pools in every intermediate slot
map. Magic-only focus also protects full non-owned energy/R3 pools regardless
of the global management flags.

## Outcomes And Recovery

Runtime `resourceAllocation` reports `Stopped`, `Applied`, `Waiting`,
`NeedsUser` or `Failed`, the selected owner, reason and current demand flags.
Preset metadata includes the first `applicationIssue` rather than collapsing
all failures into an unexplained historical/displaced status. Stage
`presetReason` carries the coordinator's blocker. Idle warnings do not replace
an actionable waiting/failure reason.

- Loading/readiness failures are read-only waits; preset preflight retries use
  bounded backoff.
- Same-gear material growth can continue resource evaluation from actual gear
  without an unrequested optimizer search.
- Focus failures retain requested intent and bounded retry. Pending activation
  retries verify epoch, policy switches, equipment, support and allocation
  observations before replaying setup. Newer manual choices require explicit
  acknowledgement instead of being overwritten.
- Focus Stop recalls only independently verified, still-owned managed lanes.
  Failed preflight or an uncertain partial transfer does not grant whole-lane
  cleanup authority.
- Ambiguous partial preset mutation remains `NeedsUser`, not a blind retry.
  Explicit Equip or a new stage entry acknowledges the operation.
- Unresolved writer lane evidence is bound to verified save/rebirth epoch markers,
  not the selected owner. Owner or resource-row changes in the same epoch retain
  quarantine. A verified epoch transition retires historical evidence without
  restoring allocations; missing metadata cannot clear uncertainty.
- Scheduled farming routes retire under the predecessor's verified identity before
  publishing the next stage owner. Foreign route, gear, support or unresolved lane
  evidence blocks the handoff rather than authorizing cleanup of another owner.
- Native AT target conflicts survive gear displacement/invalidation. Borrowed
  target cleanup services both fields even if native rendering throws after a
  resource return, and preserves later callback edits.
- Combat Prep's durable cleanup barrier remains authoritative. Reconciliation
  never bypasses it to spend idle resources.

Both weighted and inline stage credit require verified observed application.
Inline receipts are bound to character/epoch, the actual scheduler entry,
managed flags and destination maps. External allocation changes disarm credit;
idle generation alone does not. These observations never restore resources.

## Verification

Normal production Release rebuild passed without temporary source imports.
The user-launch artifact tested was:

```text
NGUInjector/bin/Release/NGUInjector.dll
MVID: e31d1803-2a4c-4886-9b1a-86d4c57aba18
SHA256: 6E44A1FED2BF8C533E4659EA700D1EC8D3F3EAE8D8D76363514633D1472FA322
```

Fresh offline results:

| Suite | Assertions Passed |
| --- | ---: |
| ResourceWriter | 1,307 |
| SustainedFocus | 2,580 |
| ProtectedTiming | 1,699 across 63 scenarios |
| Presets numeric/model | 2,843 |
| NumericIntegration | 2,172 |
| CombatPrep planner | 6,560 |
| PresetIntegration, compiled production | 10,732 |
| GearEquip, compiled production | 20,497 |
| Ownership, compiled production | 10,224 |
| Ownership Yggdrasil/fruit lanes, separate selector | 671 |
| CombatPrepIntegration, compiled production | 7,955 |
| BlackBar, compiled production | 23,676 |
| OrdinaryPolicy | 115 source-linked + 32 compiled |
| ExternalUi | 9,492 |

Counts overlap where integration runners reuse fixtures; they are not a unique
coverage total. Source-linked suites use substituted game boundaries. Compiled
suites exercise the production DLL with isolated native assemblies and selected
rendering/input substitutions, not a live Unity scene. Test-only recall
substitutes were corrected to include native wish/ritual/R3 semantics rather
than weakening writer verification.

Regression coverage includes live ratios with AutoUpdate off/on, cached gear
restoration, demand/baseline changes and release, empty-baseline cleanup,
idempotent allocation, failed native recalls, committed-transfer rendering
failures, per-lane Stop, target conflicts during gear changes, timer/cast
eligibility, unmanaged fruit/capacity protection and observed stage credit.

No live game restart, injection, bridge command, resource spending or save edit
was performed. The running game must load the new Release DLL before the new
behavior takes effect. No commit was requested or created; unrelated worktree
changes were preserved.
