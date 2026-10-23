# Adventure Automation

Manual Adventure controls combat, routing, and an optional quick combat loadout.
The default, **Keep equipped gear**, preserves existing behavior. The Adventure
tab also offers `sqrt(Power * Toughness) * Move Speed`,
`Power * Toughness * Move Speed`, and `(Power + Toughness) * Move Speed`.
Move Speed means the optimizer's existing move-cooldown bonus, not zone movement.
These optimize owned gear, not named global resource presets or arbitrary weights.
They apply once per manual ownership entry or selection change, including manual
farm rules and manual max-ITOPOD pushes, rather than searching every combat tick.
Failed application pauses combat in Safe Zone and retries no faster than five
seconds. Other equipment locks are respected. Gear swaps recall energy, magic,
and R3 through the existing capacity-safe swap path; normal allocation then resumes.
Disabling ordinary Manual Adventure leaves the selected gear equipped until another
owner applies its loadout. Temporary titan fights instead restore their prior gear.

Turning off explicit Manual Adventure enables the game's adventure idle attack on
the next combat tick, even when Adventure goal is disabled or manual setup was
blocked. This is a one-time transition, not a permanent idle override: manual farm
rules, titan fights, and other active manual owners can still select manual combat.

The Manual Adventure card keeps its zone picker editable during max-ITOPOD pushes.
The separate **Push ITOPOD** toggle, death limit, and push status live in that card,
not in the normal Adventure mode dropdown. Push remains persisted as mode `3` and
starts with either Manual Adventure or Adventure goal enabled. Disabling both,
turning push off, or selecting another Adventure mode cancels it. Completion resets
the fallback to Optimal ITOPOD and resumes the latest selected manual zone if
Manual Adventure remains enabled. Editing that zone never interrupts a push.

Manual Adventure still manages its existing beard/digger support. The separately
labeled Adventure gold push and titan automation retain their dedicated temporary
gear-swap behavior. Dashboard focus/prep and explicit loadout controls are unchanged.

## Global Stat Rules

The Adventure tab supports up to 32 ordered rules in global `settings.json`, not
in a rebirth plan. Enable Adventure goal to use them. The first enabled rule
whose minimum Power, Toughness, and HP Regen are all met selects Optimal ITOPOD,
dynamic boost farming, idle zone farming, or manual zone farming. Minimums are
inclusive; zero means no minimum. Locked idle/manual zone targets are skipped,
allowing a later rule to match. If none match, the existing Adventure mode and
Farm zone remain the fallback. An empty list preserves existing behavior.

Put the highest-stat tier first. For example:

| Priority | Minimums | Activity |
| --- | --- | --- |
| 1 | Power >= your zone target; Toughness/Regen as needed | Manual farm selected zone |
| 2 | Power >= your boost threshold; other minimums as needed | Dynamic boosts |
| 3 | All zero | Optimal ITOPOD |

Rules reevaluate native currently equipped stats every adventure tick, including
after stat loss or rebirth. They are not one-time milestones, do not predict
future gear, and do not change the rebirth plan or its resource allocations.
Thresholds are user choices, not a guarantee of combat survival. Support, buffs,
and equipped gear can change the measured stats; set minimums with headroom.

Manual rules reuse the Manual Adventure combat options (boss-only, healing,
precast, fast combat, Beast Mode) without enabling the explicit manual toggle.
They manage the same beard/digger support and optional manual combat loadout. Explicit
Manual Adventure overrides the rules; combat preparation, priority titans,
quests, fight/rebirth blockers, max-ITOPOD pushes, preset-owned activities, and
the existing temporary gold detour keep their priority. The runtime rule preview
reports a matching rule, not a claim that it currently owns combat.

The serialized `_customAdventureRules` array contains records with exactly
`Enabled`, `MinPower`, `MinToughness`, `MinRegen`, `Mode`, and `Zone`. Modes are
`0` ITOPOD, `1` idle zone, `2` boosts, and `4` manual zone. Max-ITOPOD pushes are
deliberately not repeatable rules; use the existing bounded-push control.

## Dynamic Adventure Goal

The Dynamic adventure goal follows the saved Adventure mode and ordered stat
rules. Add it through **Add goal** on any preset and assign a relative weight,
alongside NGU, Blood, or other goals. Its full weight values the selected farming
output, without an independent power or drop-chance priority:

| Selected Activity | Main Output | Route |
| --- | --- | --- |
| Optimal ITOPOD | PP per second | Planner-selected repeatable ITOPOD floor |
| Dynamic boosts | Expected boost value per second | Planner compares ITOPOD and unlocked regular zones for each gear/support candidate |
| Idle zone | Drop/kill index | Selected unlocked regular zone |

Boost and zone output already includes drop chance, power-dependent kill speed,
and respawn. Automatic diggers use the resolved goals, including PP, Adventure,
and Drop where relevant; explicit digger overrides remain fixed. A dynamic goal
controls the shared farming route even when old fixed farming goals are present.
Locked configured idle zones fall back to ITOPOD; locked rule targets are skipped
as usual. Farming remains unavailable in Beast Mode or without its native unlocks.

Dashboard Equip still pauses the plan and auto-rebirth and disables the separate
Adventure/Manual Adventure toggles. The dynamic preset itself opts into the saved
Adventure mode/rules, so those toggles need not remain enabled. Scheduled presets
preserve their plan owner. Preview reports the resolved task and effective weights;
Apply rejects a changed Adventure configuration or matched task before mutation.
Saving a preset edit alone does not alter its acquired execution policy.

With Auto-update enabled, a later task/configuration change causes one deliberate
re-acquisition, not a new search on every refresh. Allocations otherwise replay
the frozen gear, support, route, and effective weights. With Auto-update disabled,
an incompatible task requires Preview + Apply; there is no hidden optimization.
Gear/support applied by the preset can itself cross a stat-rule threshold. The
post-application observation is latched to prevent that feedback from repeatedly
switching tasks; later observations outside that acquisition's task/baseline pair
can trigger another acquisition. Owned minute beard rotations refresh this baseline.

Manual-zone tasks defer to the existing manual combat loadout/support and options,
without allowing the old farming receipt to earn stage credit. Boss/rebirth prep,
quests, combat prep, bounded pushes, and titan owners retain priority. Returning
from manual combat records a restoration event even with Keep equipped gear.
Manual/push/titan tasks are not simulated as idle weighted-farming forecasts;
Preview/Equip wait for a supported idle task rather than silently applying PP gear.

Fresh libraries have one `adventure-dynamic` seed instead of separate PP, Boosts,
and Zone Farming seeds. This is an ordinary preset containing the Dynamic adventure
goal. Existing libraries and plan references are preserved. Add **Dynamic adventure**
to an existing preset and remove its old fixed farming goals to follow the current
Adventure activity instead of retaining those fixed priorities.

## Adventure Gold Push

During the first hour of a rebirth, Adventure gold push starts with the highest
unlocked non-titan zone's boss. Its temporary loadout balances 90% gold with 10%
combat stats, split equally between power and toughness. The scale-independent
score is `GoldMultiplier^0.9 * Power^0.05 * Toughness^0.05`: gold remains the main
priority, while spare slots prefer strong, balanced combat stats. Titan gold swaps
retain their separate gold-only scoring.

After a death, it keeps the temporary gear, recovers in Safe Zone, and tries the
next lower unlocked non-titan zone. This repeats until a boss dies or no lower
zone remains. Completion restores the original loadout and selected adventure
goal without restarting at the failed highest zone. A newly unlocked higher zone
can start a new push; rebirth resets attempt history. The one-hour cutoff and
priority interruptions still cancel the push and restore its gear, including
during recovery.

## Bounded Manual Titans

The Adventure tab's independent **Manual Titan Fights** rule defaults to disabled,
Walderp (native zone 16), the Power-times-Toughness combat score, three deaths,
and requiring completed AT targets. Select one supported titan and enable it.
It starts when the native countdown is due, even if the titan spawned before
the rule was enabled, provided manual progression/unlock conditions are met.
It does not require autokill eligibility or three prior Walderp kills.

With **Require AT targets reached** enabled, both native Advanced Training
Adventure Toughness (lane 0) and Power (lane 1) targets must be positive, and
their actual levels must meet those targets. Zero/unlimited or disabled targets
do not qualify. This is an entry gate, not a maximum-speed funding check; a
resumed attempt retains the readiness receipt and its original death budget.

The coordinator equips its own combat score, uses the shared Manual Adventure
healing/precast/fast/Beast Mode options, and counts actual native death-log
events, including deaths that move to Safe Zone. Recovery is mandatory after a
death. The run ends after the configured deaths, a confirmed native kill/timer
reset, Walderp phase completion, or ten minutes of active-attempt time. Defeating
an early Walderp form reports phase completion: find him in-game before the next
phase can spawn. It never claims that an enemy disappearing alone proves a kill.

Prior gear is captured exactly and restored under the same snapshot token and
save/rebirth epoch. Failed restoration retains cleanup ownership, blocks combat,
reports the recovery error, and retries once per second. Lost/foreign ownership
or an old save cannot restore over a newer loadout. Native in-game gear changes
also invalidate the equipped-instance receipt: the rule relinquishes without
moving the player or restoring over manually selected gear. Unload is refused
while exact gear recovery is pending, retaining the cleanup component/timers;
retry unload after recovery succeeds. Quest/preparation/rebirth
interruptions release the temporary lease without replenishing deaths. Config
edits, target changes, or toggling off/on do not replenish a spawn's budget;
raising the limit mid-spawn cannot increase it. A disarmed target only rearms
after an observed native timer reset and the following due spawn. State is
runtime-only; restarting/reloading the injector or changing save/rebirth epoch
resets attempt history.

The selected target is reserved from ordinary manual adventure/rules and custom
autokill preparation while this rule is enabled, including AT waits and exhausted
attempts. Other normal adventure goals resume after the temporary run. Native
game autokills remain enabled/disabled by the game's own setting and can still
resolve a titan globally. This rule does not toggle native autokills.

Runtime `manualTitan` exposes active/recovering state, target zone, status,
remaining deaths, and disarmed/next-spawn state. Offline Combat checks cover
gates, all Walderp phases, Safe Zone and synchronous deaths, interruptions,
rearming, equipment failures/recovery, foreign owners, and integrated routing.
The successful bounded-run lifecycle is now live-verified below. Death-budget
exhaustion and restoration-failure recovery remain offline-tested, not live-tested.

Verification for this change: the injector Release build, Svelte/TypeScript check,
frontend production build, 1,676 frontend tests, 137 Rust tests, 54 desktop/mobile
Adventure browser cases, source-linked Combat and ExternalUi checks, and the
compiled TitanGold regression suite passed. Mutation checks reject all eight
introduced regressions. Broader compiled Ownership and CombatPrepIntegration
runners reach an existing shared-fixture failure: their uninitialized
`CustomPlanStore` has no `PresetLibrary`. The fixture dependency was diagnosed
separately; no production null guard or unrelated fixture change was added here.

### Live Bounded Walderp Verification

On 2026-10-18, after restarting NGU with the rebuilt injector and desktop UI,
the user requested enabling and running the rule permanently. The saved rule was
enabled for Walderp, AT gating, `Power * Toughness * Move Speed`, and three deaths;
shared recovery/precast were on, Fast Combat and Beast Mode off. The active `24hr`
plan remained on its Energy NGU stage, without changing its auto-rebirth setting.

Native inspection found final-form Walderp already due, both AT levels 3002,
and both native targets zero. The existing completed plan targets were 3000 each.
Only those two zero native target fields were set to the existing 3000 values;
no levels, resources, spawn times, kills, or progression were manufactured. The
enabled rule then acquired the Titan lease, generated/equipped combat gear, healed
and precast in Safe Zone, and entered native zone 16 with idle attack disabled.

At 21:45:53 local time, native `PlayerLog` reported `You have defeated WALDERP!`,
650 EXP, 81 AP, and `1.035e+009 damage over 1:32 (1.117e+007 DPS)`. This was the
final `bigBoss5` form, not an intermediate finding phase. `titan5Kills` rose 1 to 2,
`boss5Kills` rose 5 to 6, and its 9900-second native spawn timer reset. There were
zero deaths. The coordinator reported `target killed`, disarmed this spawn, and
released its lock without recovery errors. All twelve original gear destinations
matched the same equipment references as before the attempt. Normal ITOPOD and
the Energy NGU stage resumed while the rule stayed enabled for the next spawn.

Evidence is retained in the approved temporary directory as
`manual-titan-run-events.log` and `manual-titan-run-state.txt`. The bounded temporary
observer sampled native state and event references; the coordinator alone chose
gear and combat moves. Its only requested configuration write was the AT-target
sync described above. The observer was shut down after verification, and a game
quicksave/Steam Cloud save was requested after the kill.

## Titan Gold Preparation

Configured titan gold targets equip generated Gold gear within the three seconds
before spawn, using the 100 ms combat tick rather than the slow automation loop.
Native titan autokill must be enabled, with the relevant progression, unlock,
prerequisite item/kills, and selected difficulty available before any swap.

The actual equipped Gold loadout is checked immediately against the game's
autokill requirements, including power, toughness, HP regeneration, and native
kill-count shortcuts. Stored difficulties 0-3 map to V1-V4. If Gold gear falls
short, both Gold-target and ordinary titan routes search owned gear for the
required power, toughness, and regeneration. The fallback caps credit at each
requirement: excess power or move cooldown cannot compensate for missing toughness.
It forward-projects base stats, cube softcaps, and current multipliers, then
requires the actual native post-equip check to pass. Unsupported projections or
unverified loadouts restore the original gear and use the existing one-hour
failure cooldown. A native rounding rejection gets one retry with eight binary32
ULPs of headroom.

Requirement search is uncached and stops at a complete feasible loadout, with a
cooperative 750 ms / 100,000-work budget per search. Budget exhaustion reports
incomplete feasibility, not proof that owned gear cannot meet the requirements.
Capture, candidate preprocessing, and native equipment calls are not hard
real-time bounded; native verification remains authoritative at float boundaries.

Passing gear waits in Safe Zone because native autokills run globally. Imminent
Gold preparation preempts a temporary Fight wait after restoring its snapshot.
Co-due and overlapping eligible targets share their temporary gear and original
snapshot. A harder nearby target can upgrade an active batch to required-stat gear
while preserving its already tracked targets; a rejected upgrade rolls back to
the batch's previous gear without replacing the original snapshot. Successful
completion requires the target's own timer reset while ownership and save epoch
still match. Only a full-Gold loadout marks its Gold goal done; a fallback kill leaves
that goal eligible for a later spawn. Gear stays equipped until prepared targets resolve.
The 25-second timeout measures stalled progress, so successful overlapping spawns
can continue without cancelling a newly joined target. Cooldown targets do not
hide other eligible targets. Lost eligibility, invalid timing, timeout, and
priority/epoch cancellation clean up owned gear without claiming a Gold kill.

## Non-Fast Combat

Non-fast manual combat favors denying enemy turns and maintaining buffs rather
than repeatedly spending the shared move cooldown on Block/Parry. The unlocked
emergency recovery remains first below 50% HP; otherwise a ready Paralyze Gaze
takes priority over defenses, ordinary healing, buffs, and attacks. Gaze remains
available against charging/rapid enemies, below 20% enemy HP, and with Block or
Parry already active. Fast Combat still bypasses this utility policy.

Block is reserved for imminent hits at or below 50% player HP, rapid bursts, or
enemies attacking at least once per second. Healthy players save Parry for a
charged heavy hit or explosion. Low HP permits Parry as a fallback when Block is
unavailable. Neither defense is recast while Block or Parry is active. The legacy
More Block/Parry toggle does not override these threat checks.
Healthy non-fast boss-only spawn waiting also reserves Parry. Explicit Safe Zone
preparation is unchanged: it can precast Parry and wait for its cooldown before
entering combat, so the skill is ready again for a later heavy hit.

Defenses are placed within one shared cooldown plus 0.25 seconds of damage.
Timing includes the ordinary enemy's 1.5x first-turn delay, the rapid burst's 0.3x
interval, and remaining enemy paralysis. Charger/Rapid warning and blank turns
do not trigger defense. Gaze goes first so a newly placed Block is not immediately
wasted by freezing its target; if Gaze becomes ready with an older Block active,
denying enemy turns still takes priority over preserving that Block.

Mega/Ultimate and Offensive buffs retain their priority over ordinary attacks.
Defensive Buff is now proactive even at full HP, but is skipped while Ultimate
Buff or Defensive Buff is already active. The Mega Buff unlock policy is unchanged.
Below 20% enemy HP, ordinary non-Walderp combat still skips discretionary buffs,
but not Gaze, emergency recovery, healing, or threat-based defense.

## Walderp Skills

Manual Walderp combat prioritizes a ready prompt-response attack: exactly the
requested attack for "Walderp says", or a different attack when he does not say it.
If the response is unavailable, the prompt is incomplete, or its active state
cannot be read, automation waits without spending utility. All utility moves also
start the shared 1-second cooldown (0.8 seconds with Red Liquid); spending one
while waiting could delay a late-ready response past its deadline. This safety
choice defers healing too, even at low HP, until the prompt can be answered.

Before a prompt, buffs and recovery still do not wait for attack reservations,
including immediately before issuance or with missing attack-timing data. The
response window lasts another enemy turn. Attack selection and its existing
cooldown reservation/margin are unchanged. Fast Combat still skips utility.

Walderp survival does not stop when the enemy falls below 20% HP. Heal restores
15% of maximum HP, so combat uses it at or below 85% HP rather than waiting below
50%. If healing is unavailable and HP is below 85%, Hyper Regen is used proactively;
an active `PlayerController.hyperRegenTime > 0` prevents recasting it. The emergency
move remains first below 50% when unlocked and ready. Heal thresholds and the
active-regen guard also apply to ordinary non-fast combat.

Walderp uses the same low-HP/rapid-hit defensive policy rather than routinely
blocking normal turns or preparing Parry while healthy. The turn advancing
`growCount` to a value congruent to 2 modulo 6 issues a prompt instead of attacking,
so it never triggers defense. Unanswered prompts remain reserved for a response,
not an attempted Block against the following explosion. Unknown timing cannot
place defense. Long automation stalls can still miss this timing window; live
behavior must be checked separately from the offline fixture.

## Game Evidence

Read-only decompilation of the installed `Assembly-CSharp.dll` confirms:

- `EnemyAI.waldoAI`: prompts at counts 2, 8, 14, etc.; the following turn explodes
  only if unanswered, otherwise it attacks normally.
- `PlayerController`: Block expires at three seconds; Parry is consumed by damage;
  heal restores 15% maximum HP; Hyper Regen lasts five seconds; `usedMove` sets the
  shared 1/0.8-second cooldown. Move `doMove` methods call `usedMove`.
- `Block`, `Parry`, `UltimateBuff`, and `DefenseBuff` mechanically stack, but the
  automation deliberately avoids redundant Defensive Buff turns under Ultimate.
- Ordinary `EnemyAI.Update` delays the first action to 1.5x attack rate and uses
  0.3x during rapid mode. Charger counters 2/3 and Rapid counters 4/5/6 precede
  non-damaging turns; an active Gaze freezes the enemy action timer.
- `Charge.Update`: `defenseTraining[3] >= 20000`; `Parry.Update`:
  `attackTraining[2] >= 15000`; `UltimateBuff.Update`:
  `defenseTraining[4] >= 25000`; `DefenseBuff.Update`:
  `defenseTraining[0] >= 5000`. Precast unlock helpers now match those gates.

Offline verification: `dotnet run --project checks/Combat/CombatChecks.csproj`.
The source-linked harness covers prompt correctness, survival, buff priority, timing,
and utility-enabled cooldown cycles; it does not substitute for live Unity testing.

## Live Verification

Before the turn-denial policy above, on 2026-10-17, applying Manual Titans and enabling non-fast Manual Adventure
defeated `waldo4` (800 million HP). The game's battle log reported 4:21 and
3.289 million DPS. A bounded three-minute capture observed 12 Blocks, 13
in-combat Parries, 6 Heals, 4 Hyper Regens, and repeated Ultimate/Offensive buffs.
Minimum sampled player HP was 70.31%, with no observed prompt failures or deaths.
The capture covered only part of the fight; the subsequent battle log confirmed
the kill. This verifies the fourth form, not the final `bigBoss5` form.

Temporary testing settings, exact gear, beard/digger selections, targets,
allocations, and routing were restored afterward; the diagnostic component was
removed. Earned kill/progression was retained, not rolled back through an old save.
