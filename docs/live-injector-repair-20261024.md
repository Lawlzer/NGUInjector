# Injector Repair Loop, October 24

Historical baseline only. The later natural stage transition failed and the final
follow-up still has a live AT-to-NGU route-handoff block. The clean observations
below are not sustained acceptance. Current implementation, exact verification
failures, live state and continuation are in `../artifacts/agent-handoff.md`, under
the `2026-10-24T18:38:24Z | stage-allocation-parent` entry.

## Outcome

The requested initial sleep completed in full: `7200.0237201` seconds. Subsequent
work used actual game/injector restarts, 30-second post-launch waits, live
diagnostics, confirmed-cause fixes, and fresh native verification gates. No commit
was requested or made. Existing worktree changes were preserved.

Final repeated live diagnostics passed with zero required issues and an empty
`automationErrors` array. The game and external UI were left running on the existing
`24hr` plan, with automatic rebirth, Energy/Magic management, and adventure automation
enabled. No plan identity, stage entry, duration, trigger, AT target, or NGU zero
exclusion was changed to manufacture completion.

An actual automatic rebirth occurred at **03:50:39 local**, advancing **230 -> 231**.
The saved plan history records NUMBER `3.555042239453107E+109` ->
`6.084803984640611E+110`, multiplier `17.115982243790928`, after
`133264.18859918334` rebirth seconds. No `manualRebirth` command was issued.
This occurred on intermediate candidate `a00204d3-a32b-47d7-906e-ac686c562046`;
it is not a claim that the final candidate completed another full daily cycle.

## Confirmed Fixes

| Boundary | Cause And Fix |
| --- | --- |
| Startup NGU telemetry | Telemetry ran before settings and the native NGU model were initialized. Return explicit unavailable metadata until those dependencies exist; initialized-state corruption still reports an error. |
| Ordinary tower routing | A verified selected-setup Safe Zone transition left the ordinary route observation stale. Publish that exact zone effect. Reject unavailable tower admission without writes or exception flooding, while retaining callback-boundary authority checks. |
| Save publication | A newly captured mixed-slash path and the scanner's canonical path became different dictionary keys. Canonicalize the generated capture path before writing/indexing; real missing-save detection and high-water evidence remain enforced. |
| External UI launch | A running transport heartbeat could still describe `LoadingSave`. Require execution lifecycle `Running` before opening the UI, with enough time for startup cloud loading to settle. |
| Post-rebirth handoff/Pit | Ordinary owned routes lacked bounded retirement evidence, and expected rejected Pit/successor admission could throw from cleanup and abort the ordinary pass. Add current-session, exact-state retirement evidence and nonthrowing rejected admission; preserve foreign routes. Keep application-scoped evidence on the producer's actual setup object. |
| Delayed native cap debit | An owned inline gear change could fund the old pool before the native generator reduced total and idle, creating negative idle. Mark the existing capacity-budget limiter after a verified authorized gear change, only for its Energy/Magic recall footprint. Stable/no-swap/unmanaged paths retain their existing behavior. |
| Diagnostic completeness | Allocation success did not inspect injector error history. Require a present empty `automationErrors` array; retain original exceptions as failing evidence. |

## Native Recovery

The old session had persisted Magic total `13,451,923`, TM allocation `13,810,097`,
and idle `-358,174`. This predated the restart. Native `MagicDisplay` applies a
delayed cap delta to total and idle, not allocated bars. The exact intra-frame
historical ordering was not recorded, but the saved arithmetic and native method
establish the failure mechanism.

After a verified protective local capture, the native Time Machine allocation input
was set to `358174` and Gold Multiplier Magic minus was clicked once. Native total
remained unchanged, allocation became `13,451,923`, and idle became `0`. The previous
numeric allocation input was restored. No levels, progress, or targets were lowered.

The inherited unowned adventure route was explicitly released through the native
Safe Zone dropdown, not adopted as injector cleanup authority. On the final restart,
adventure automation was temporarily paused through the supported CAS patch;
after the native handover and verified inline application it was restored to `true`.
Foreign-route refusal remains intentional, not a generic automatic takeover feature.

## Verification

The final scoped-capacity candidate passed all 13 full native suites through
`checks/VerifyExecutorOffline.ps1`, under its shared verification mutex:

| Suite | Assertions |
| --- | ---: |
| PresetIntegration | 21,915 |
| CombatPrepIntegration | 12,587 |
| ManualAdventureOverlay | 297 |
| BlackBar | 24,758 |
| Ownership | 10,501 |
| TitanGold | 1,497 |
| PresetEquipment | 94 |
| GearEquip | 20,805 |
| OrdinaryPolicy | 167 |
| WinFormsBlood | 174 |
| StageTargetsNative | 48 |
| ResourceAggregation | 4,646 |
| AutoExp | 5,169 |

Additional checks: 168 diagnostics cases, 83 save-safety bridge cases / 1,444
assertions, launcher syntax/readiness-predicate checks, and `git diff --check` passed.
The final ordinary-capacity focused selector passed 1,167 assertions; its integrated
group contributes 1,165 to the full suite. Earlier failed gates and the timed-out gate
were not deployed or substituted for this final pass.

Captured artifact: `../temp/checkins/sleep-loop-scoped-capacity-candidate-20261024`.
MVID: `9e14378a-60f3-49bc-b86d-b7effeacb7f9`.
SHA256: `A6ED76B7CF8F2A31BAE412CBE6BC1713C8879B2424E6975B6F5944400FF2A1FF`.
The launcher verified that immutable artifact immediately before normal injection.
No diagnostic assembly injection, force-kill, or manual conflict-history clearing
was used. Native verification logs and the manifest remain beside the captured DLL.

## Final Live Evidence

Game PID `22904`, started `06:25:47`; injector session
`2026-10-24T06:26:10.1984563-05:00`; UI PID `43328`.
Graceful restart/save/backups: `../temp/checkins/sleep-loop-live-20261024-062517-f138da28`.
Final progressing snapshots and three 30-second-separated samples:
`../temp/checkins/sleep-loop-final-20261024-063558`.

- `diagnostics.success=true`, required issues `0`, injector errors `0`.
- Three samples: 137 PASS, 0 FAIL, 0 WAITING. Six optional independent NGU
  distribution checks remain INCONCLUSIVE because authoritative current caps/budgets
  are not published. These are not suppressed errors or completed ratio verification.
- Energy total `167,997,179`: Time Machine `167,997,167`, basic training `12`, idle `0`.
- Magic total `27,125,014`: Time Machine `27,125,014`, idle `0`.
- Gear/native support matched desired state and were verified; support was feasible.
- ITOPOD zone `1000`, automatic attack on; no pending execution claims or review.
- Original stage entry `e490cc5e0d5f4819b8dac4d018422f1a:1` retained. Time Machine
  applied time advanced `496.20249770018665` -> `555.8058779029911` seconds across
  the final interval, including an actual beard rotation without new errors.
- A final supported quicksave succeeded: command `fe2fa6709dea4b9eb7314c2ce69fccf1`.

These are completed observations, not an unattended-monitoring or permanent-zero-bugs
guarantee. A full daily cycle and later preparation/AT/titan transitions on the final
artifact remain long-run coverage limits. The broader executor WIP is not completed
or deleted by this bounded runtime repair.
