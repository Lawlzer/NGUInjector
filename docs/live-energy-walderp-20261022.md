# Live Energy And Walderp Verification

## Outcome

The existing installed `24hr` plan is running with its 86400-second rebirth trigger,
plan auto-rebirth permission, live auto-rebirth, Energy management and Magic
management enabled. Custom adventure routing is enabled. Final native observations
at 10:06:34, 10:06:50 and 10:07:05 showed **idle Energy 0 and idle Magic 0**, after
an actual automatic Walderp kill and return to scheduled allocation.

The current refactor DLL was NOT deployed or injected, and the game was NOT
restarted. Live observations concern the previously loaded assembly
`3b1ffb80-0e37-48b1-9d67-6432a56fafe1`. Source acceptance concerns a different DLL,
`c5602156-049f-49f6-9b4a-93946b24cb90`. Do not conflate the two.

## Live Repair

The user explicitly prioritized actual live Energy spending and the 24-hour plan
before any further combat or remaining refactor work.

Initial live state was enabled but unhealthy: the restored current NGU stage had
no selected-policy record, reported `NeedsUser`, and retained 137674600 Energy on
the now-excluded Magic NGU target while 3859115 Energy was idle.

Supported bridge operations backed up settings, preset library, plan and stage
checkpoint, then saved the current NGU preset's unambiguous NGU-only Energy and
Magic lane orders. All objective weights, support policy and Misc NGU exclusions
were preserved. An explicit flexible-stage restart granted new acquisition
authority, changing entry 4 to 5 without replaying earlier completed stages or
extending the daily horizon. The configured entry boss push ran once and finished.

The unowned pre-existing ITOPOD route prevented safe setup admission. Adventure
routing was temporarily paused, the native Adventure dropdown selected Safe Zone,
and the scheduled preset then acquired and verified its setup. Adventure routing
was restored. No checkpoint was forged or deleted and no missing policy was
silently rebound.

07:50:55 through 07:52:11 verified zero idle in both lanes. Energy capacity growth
from 164661780 to 164676376 was automatically funded: NGU targets 0/1/3/5 grew from
41165442 each to 41169091 each; excluded targets remained zero. Magic funded the
active Iron Pill reservation, then returned to native Yggdrasil/EXP NGUs after it.

Independent zero-idle observations at 07:52, 08:26, 08:50 and 09:09 were spaced at
least 15 minutes apart. These are current-installed-build observations, not the
post-deployment final release gate for the whole refactor.

## Automatic Walderp

After offline combat acceptance, native AT inspection showed actual Toughness and
Power levels 3000/3000 but native menu targets 0/0. The automatic titan rule was
already enabled for zone 16 with AT required and three deaths allowed, but its
positive-native-target gate prevented any attempt.

The native target fields were restored to the plan's existing configured
3000/3000. No level, configured threshold, spawn timer, unlock, death budget or AT
requirement was reduced or fabricated.

- 09:18:24: automatic titan actor active, `Fighting WALDERP (16)`, deaths 0,
  remaining deaths 3.
- 09:18:40: actual actor outcome `target killed`, deaths 0, inactive,
  `awaitingNextSpawn: true`.
- The daily plan and auto-rebirth remained enabled throughout.
- Resource allocation was temporarily suspended under the native fight's equipment
  lock. After the kill, allocations resumed while resource pools refilled. This
  temporary idle was observed, not represented as capped idle or zero-idle combat.
- 10:06:34 through 10:07:05: scheduled allocation `AppliedVerified`, Energy NGUs
  totaling 153275558, current Energy 153275570, idle Energy 0; Magic NGUs totaling
  12901697, idle Magic 0. The allocation CSV excludes basic training; its 12-point
  difference is not idle Energy.

One successful native spawn was observed. The enabled rule is waiting for the
next native spawn; indefinite monitoring or another completed spawn is not claimed.

## Source Regression Fixes

The completed-AT menu-return bug also has an offline fix: only exactly native
0/0 may use current enabled-plan, matching-epoch, durable completed mandatory AT
proof with positive configured targets and adequate actual native levels. Positive
native targets retain precedence. The new native acceptance uses actual training
ticks and normal target return, not completed flags alone. This source fix is not
yet loaded into the live game; the live target-field repair is in effect.

Broad same-DLL verification caught and fixed a separate focus publication ordering
regression without relaxing unmanaged Energy/R3 or foreign-owner protections.
See `walderp-at-readiness-20261022.md`,
`focus-ownership-regression-20261022.md`, and
`combat-executor-verification-20261022.md` for commands and source evidence.

Final source DLL passes full Ownership 10294, CombatPrep 11985, Preset 13528,
BlackBar 24081, and the relevant source Focus/Combat/Support/ResourceWriter/ExternalUi
suites. The overall desired-state refactor still has unchecked actor, finite-action,
persistence, lifecycle and final release/deployment work in `../../wip.md`.

## Artifacts

- `temp/live-first-energy-walderp-20261022-final.json` relative to the workspace
  root contains final live runtime, saved plan and settings readback.
- Workspace `temp/repair-live-energy-20261022.ps1` and `temp/ngu-bridge.ps1` contain
  the supported operational commands; command results remain in the installed
  runtime's `command-results` directory.
- Workspace `temp/live-energy-20261022-0745*-before-*` contains pre-repair backups.
- Approved temporary screenshots `ngu-game-20261022-091448-677.png` and
  `ngu-game-20261022-091744-511.png` show native AT levels and before/after targets.

No commit, final deployment or background monitoring job was created.
