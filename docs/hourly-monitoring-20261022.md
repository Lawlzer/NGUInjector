# Hourly Monitoring

## Captured Deployment: October 23

**Captured candidate is actually deployed, but live acceptance is NOT green.**
The old game crashed during an allowed read-only diagnostic attempt before any
graceful game close. The exact captured candidate recovered in a new process; one
tokenized same-entry rebind succeeded. After one explicit native route handover,
application hit a null-reference/UnknownOutcome boundary and did not commit its
preset receipt. Native NGUs are funded, but stage credit is frozen. Walderp is
spawn-ready but its new-build gear-capacity preflight rejects admission. These are
real new-build live blockers, not prior-build kill or offline acceptance evidence.

### Identity And UI

- Captured distribution:
  `temp/checkins/verified-candidate-3b6016de5360463ea3ce4ac991dbb589/injector`.
- DLL SHA256 `5FEFCA5E10572707FC19A3E99AA9F1372A02640551F7E131CFD3774277AB881A`;
  captured-file MVID `3c01e825-035c-4ab7-98b1-3c2d50d1f2c9`.
- The exact path/hash was checked immediately before normal Loader.Init injection,
  while holding `Local\NGUInjector-Executor-Offline-Verification` across the launch.
  No C# injector rebuild or mutable dist was used. Original capture/manifest remain
  untouched, including `liveVerified:false`.
- New game PID27048, start03:46:49.639; injector epoch03:47:08.7377887; new desktop
  PID63320, start03:47:09.545. Old game/UI PIDs were47484/2852.
- Desktop UI2852 accepted graceful close. `npm run tauri -- build --no-bundle`
  rebuilt the release UI successfully at03:46:11, SHA256
  `D1FFD40EA3AF41A6F1C7A8AC5E2D8050DF1C641F5591904EBEF8AD28A14D49CC`.
- Loaded assembly MVID was NOT directly observed. The metadata attempt failed and
  was not repeated in the new game; captured-file identity and exact injection path
  are evidence of what was injected, not a fabricated runtime metadata reading.

### Crash And Recovery

Read-only preflight backed up runtime root files, settings/library/plans/profiles,
all existing execution/OS/scheduler commitments and logs; verified fresh native
quicksave acknowledgements, nonempty TXT/JSON, timestamps and hashes. Safe diagnostics
confirmed no native boss fight/nuke or active temporary titan/preparation actor.

The standalone preflight initially tried reconstructing Equipment from JSON. Its
all-slot mismatches came from the runtime-only `path` field: JSON omits it, the
Equipment constructor defaults to`NoItem`, and the saved selected material has
empty path. This was a diagnostic limitation, not proof all saved equipment differed.
Do not reuse that synthetic mismatch as a migration finding.

The small separate `NguReadOnlyDiagnostics.dll` used reflection reads and file
output only, with no Unity/injector actor calls or native setters. Its SMI invocation
failed with `mono_object_get_class() returned NULL`; no diagnostic output appeared.
Windows Application Error1000 at03:38:57 confirms NGUIdle PID47484 crashed in
`mono-2.0-bdwgc.dll`, exception0xc0000005, offset0xb91a0. The attempted read-only
injection is the immediate incident context; it is NOT described as a graceful
close. A V2 invocation only found the game already gone; it did not run. Both
diagnostic binaries/source are retained as incident artifacts and must not be
injected again. No repeat diagnostic was used in the recovered process.

Steam `remotecache.vdf` records native cloud save time1792744730, **03:38:50 local**,
six seconds before the crash. The03:36:51 quicksave and the newer local cloud save,
native Steam backups, cache metadata and crash events were all copied before
relaunch. The supported cloud-load startup workflow used the latest native save.
NUMBER and clock continuity passed against the last pre-crash runtime; original
entry/generation/enteredAt/86400deadline/applied credit were retained. The final
unsaved six-second crash window is NOT proven lossless or exactly-once continuity.

### Explicit Review And Actual Blockers

The new runtime correctly withheld imported authority (`NeedsUser`) until review.
Exactly ONE Preview token and ONE Apply request were used. The reviewed original
definition, all12 exact native material matches, owner, acquisition
`3c2748bd93c74f578553dcee8f3b09a2`, entry4, frozen support/scoring choices and
horizon49822 matched the retained prelaunch policy. Apply acknowledged:
`Acquired selection approved for this session; entry timing and history preserved.`
No stage restart, new acquisition, weight/AT/death-budget edit, settings-CAS bypass
or forced rebirth was used. No new-build settings patch was issued.

After approval, inherited native ITOPOD blocked setup retirement. At03:53:52 the
native dropdown explicitly selected Safe Zone once. Normal setup then applied
matching gear/support/resources and resumed ITOPOD196, but failed before committing
the preset receipt with:

`gear/support/native-targets/route requires acknowledgement: Object reference not set to an instance of an object Restart the stage or explicitly Equip to acquire new authority.`

Runtime reports `UnknownOutcome`; resource desired/observed maps match but their
grant verification is false, no current applied preset receipt exists, and actual
NGU stage credit remains15191.6798434s. This scoped uncertainty is preserved. The
instruction to restart/equip in that generic message was NOT followed to obtain a
green status. No repeated Safe Zone/re-equip/review loop was used.

Native Walderp tooltip shows SPAWN READY. Its automatic actor remains inactive:
`Gear swap energy capacity is below retained allocations in an intermediate or final slot map; no gear or resources were changed.`
Native AT levels/targets remain3000/3000 and configured death limit3; no new fight
or kill is claimed. The earlier22:12 kill cannot validate this new build.

Initial03:55 native save: Energy164801893, idle0, Energy NGUs0/1/3/5 total164801881;
Magic12901697 fully on NGUs0/1, excluded targets0; gear IDs unchanged, frozen support
matches, ITOPOD196/196 autoattack active. Fruit7 had naturally harvested13times but
was inactive with0seconds; activation/funding remained a gap under blocked authority.
Zero idle alone is not acceptance.

### Fifteen-Minute Result: FAILED

The bounded follow-up ran900.24seconds, with six fresh native samples at03:59:42,
04:02:42,04:05:42,04:08:42,04:11:42 and04:14:42. **All six failed scheduled-owner
acceptance**, despite native idle Energy/Magic0 throughout:

- Energy NGUs0/1/3/5 remained41200471/41200470/41200470/41200470, total164801881;
  current Energy164801893 includes12 basic training. Magic NGUs0/1 remained
  6450849/6450848, total12901697. All configured zero exclusions remained0.
- Actual desired/observed gear slots matched and support verification wastrue,
  including the original digger IDs/levels and saved acquired beard choice.
  Resource desired and observed numeric maps also matched, but their **authority
  verification remainedfalse**. There was no committed applied-preset receipt.
- Execution remainedWaiting with setup scope`gear/support/native-targets/route`
  and`UnknownOutcome`. Actual NGU applied credit stayed15191.679843401602s; only
  elapsed time advanced. No completed training credit was invented.
- Fruit7 remained inactive, seconds0, harvests13 in the04:15 native save. A natural
  harvest occurred, but new-build reactivation/recall/refill was NOT accepted.
- Walderp remained spawn-ready and inactive with the same retained-Energy capacity
  preflight rejection. Configured positive AT3000/3000 and death limit3 remained
  unchanged. The actor never acquired a new fight/death budget; no new-build kill
  was observed. Waiting longer would not turn this admission failure into a spawn
  wait, so no sixty-minute kill claim or forced native workaround was attempted.
- Native adventure itself continued: PP progress737733 -> 759048 between04:15:40
  and04:18:50, with different actual defeated enemies and435-progress rewards.
  Native bounds remained196/196. The tooltip's optimal-floor197 is a forecast,
  not evidence that this acquired floor was replaced.
- Recurring post-application capacity-growth service is NOT validated under this
  failure. Earlier03:47/03:48 EXP/capacity changes are logged and were funded during
  the single partial application; static zero-idle funding is not a cap-growth test.

No further review token, native route repair, manual resource allocation, explicit
Equip or stage reset was used after this failure. The new game remains running on
the exact capture with affected authority scopedblocked. This is **not** a safe
automatic rollback point to an unproven old dist, and is not a source root-cause fix.

### Final Continuity And Snapshot

`20261023-034637-captured-relaunch/live-evidence.json` retains the final runtime,
native comparisons, full setup error, titan admission reason and acceptancefalse.
Its`post-live/` snapshot includes all root execution/lifecycle/OS/scheduler files,
settings, library/plans/profiles/logs, and the entire`finite-actions-v1/` directory.
The latest verified fresh save is`20261023-041515-667-bounded-save*`.

The continuity audit found:

- Plan and preset-library hashes unchanged; **no changed existing settings**.
- Same generation/rebirth230, acquisition, original definition and horizon49822.
- No changed scheduler entry/visits/enteredAt/expectedEnd/durations/completion/
  override history. Prior applied credit was retained exactly, not reset or renewed.
- Native origin differed only8.512754e-6seconds after native reload, within the
  existing provenance check; NUMBER unchanged and native clock advanced.
- 22 native NGU skill records compared against the verified pre-crash native save;
  no observed level regressions. Native AT levels/targets remain3000/3000.
- Captured DLL hash still exactly
  `5FEFCA5E10572707FC19A3E99AA9F1372A02640551F7E131CFD3774277AB881A`.

These comparisons do not erase the unobserved final-six-second crash window or
prove universal exactly-once continuity. They show what could actually be checked.
Original capture manifest remains`liveVerified:false`; the separate live evidence
also explicitly recordsfalse. Subsequent+2h/+4h/+6h/daily acceptance is unfinished
and no unattended check process was left active.

### Read-Only Failure Boundary

The emitted scope/error and partial native effects narrow the failure to prepared
preset application, not a missing user review: `ApplyPreparedLoadoutPreset` catches
setup/result-generation errors under`gear/support/native-targets/route` after native
setup/resource/route effects can already have occurred (`Main.LoadoutPresets.cs`,
prepared apply and its catch). The exact null-dereference line/stack is not exposed
by the current public runtime and was NOT guessed or obtained by another unsafe
diagnostic injection. Review owners need the retained imported policy and real
post-rebind application timeline, including receipt publication/result projection.

Walderp's separate rejection corresponds to the explicit retained-current-pool
preflight before claim admission in`Main.ManualTitan.cs:151-154`. Actual live status
and SPAWN READY prove that boundary; no source patch or weakened retention rule was
made in this frozen deployment pass.

### Deployment Evidence

- `temp/checkins/20261023-032234-505-bounded-save*`: initial verified saves.
- `20261023-033313-deployment-readonly-initial/`: fresh backup/diagnostic attempt.
- `20261023-033432-deployment-native-equipment-check/` and
  `20261023-033651-deployment-material-diagnostics/`: retained synthetic path mismatch
  evidence plus full native/config/commitment backups and fresh saves.
- `20261023-034539-desktop-build.{json,log}`: graceful UI closure and actual release build.
- `20261023-034637-captured-relaunch/`: exact launch transcript, prelaunch native/cloud
  backups, crash events and deployment.json; normal captured DLL injection succeeded.
- `20261023-034637-captured-relaunch/same-entry-rebind.json`: original commitments,
  full Preview result/token/revision, single Apply result and before/after native state.
- `20261023-035148-hourly-captured-post-rebind-initial.json`,
  `20261023-035424-hourly-captured-after-native-route-handover.json` and
  `20261023-035500-454-bounded-save*`: actual blocked postlaunch state and native saves.
- `20261023-041443-continuity-captured-new-build-15-minute-followup.json`: six actual
  failed owner checks over15minutes, not a future scheduled check.
- `20261023-041515-667-bounded-save*`: final fresh native save/config/log backups.
- `20261023-034637-captured-relaunch/live-evidence.json` and`post-live/`: final
  continuity/failed acceptance and all execution/action commitment evidence.
- Native screenshots`ngu-game-20261023-035306-969.png` (Walderp spawn ready),
  `...-035352-311.png` (single explicit Safe Zone handover),`...-041540-502.png`
  and`...-041850-072.png` (actual continued native ITOPOD progress).

## Regression And Repair: 23:22-23:59

**The 22:16 healthy checkpoint did NOT remain sustained.** Supervisor samples at
23:10 and fresh live samples at 23:22-23:25 confirm the original NGU entry waiting,
all Energy idle, and a displaced/historical preset. Magic remained fully funded.
The earlier Walderp kill remains valid; it does not validate later allocation health.

Initial fresh state: Energy161563947, idleEnergy161563935, Energy NGUs all0;
Magic12901697 fully on native NGUs0/1. Same plan, generation, entry4, gear IDs and
AT3000 targets. Runtime reported `ObservationsChanged`, blank execution reason and
stale `activated 1 fruit` status. Logs show Energy recall and successful Fruit of
Arbitrariness activation at22:50. Native Yggdrasil showed that fruit already active,
not still waiting to acquire Energy. Last successful rotation timestamp22:22:46
and earned stage time588.65s precede that fruit event; they do not prove the fruit
was the initiating cause. `rotation.observed=false` mirrors inactive receipt state,
not proof of an exposed pending rotation claim. Loaded MVID is still unknown.

**Operational recovery was observed after a supported same-entry re-equip, including
the next real fruit activation. This is NOT a source/root-cause fix or indefinite
health claim.** The detailed verification below retains transient failed samples.

### Supported Repair

- 23:23:06: fresh quicksave and backups before any repair, command
  `dac30464a7d84bb3a82e208f2de611e4`. Artifact prefix
  `temp/checkins/20261022-232306-830-bounded-save*`.
- 23:25:30: temporarily paused CustomAdventure with supported settings patch,
  command `d2c9539ca8334897b5923282f880556d`.
- 23:26:06: native Safe Zone selected. This **did not repair spending**;
  three fresh samples ended23:26:33 with all Energy idle and an explicit
  `Preset allocation blocked: Blocked` reason. No repeated Safe Zone success claim.
- 23:27:13: restored CustomAdventure, command `0bbb13068ce54bfb9868c1da10038d4b`.
- 23:28:12: supported `previewGear EnergyNgu` recorded its current recommendation;
  it differs from the scheduled mixed NGU preset and was not treated as that preset.
- 23:28:38: supported `optimizeGear EnergyNgu`, command
  `fd7f594b0ddc4f4fb94401cf155ed57b`, explicitly re-equipped gear. Normal scheduled
  reconciliation then reacquired its setup on the **same entry4**, retaining the
  NGU preset's109 accessory rather than temporary manual candidate439. By23:28:50,
  both idle counters were0, current scheduled owner active and both lanes verified.
  Stage/plan settings, original deadlines, AT/death limits and NGU weights were not
  changed. The policy acquisition changed to `66ff0905a2d84e3fa489b6d14eb07043`;
  the scheduler entry, original commitment and prior earned credit did not reset.
- 23:29:58: fresh quicksave proves native AT levels/targets3000/3000, no AT allocation,
  ITOPOD native bounds196/196, and active fruit7 with2377seconds/harvest count8.
  Its next natural activation was due around23:50; no timer was reset.

### Longer Actual Verification

`20261022-234552-continuity-repair-13-minute-window.json` contains seven fresh samples
over780.25seconds. Six healthy checkpoints span12.5minutes, meeting the requested
five-sample minimum; the seventh is a real transient failure, not a green window:

| Native timestamp | Energy NGU total | Idle E/M | Preset/state | Actual earned NGU time |
| --- | --- | --- | --- | --- |
| 23:32:51 | 161609918 | 0/0 | Active / Applied | 832.71s |
| 23:35:21 | 162104231 | 0/0 | Active / Applied | 979.64s |
| 23:37:51 | 162104231 | 0/0 | Active / Applied | 1126.23s |
| 23:40:21 | 162104231 | 0/0 | Active / Applied | 1273.96s |
| 23:42:51 | 162104231 | 0/0 | Active / Applied | 1419.20s |
| 23:45:22 | 162104231 | 0/0 | Active / Applied | 1567.79s |
| 23:45:52 | 162104231 | 0/0 | Historical / Waiting, ObservationsChanged | 1595.66s |

Magic NGU total stayed12901697, all excluded targets stayed0, and positive targets
within each lane had equal allocations to within one integer throughout these
checkpoints. Native Energy growth after the23:34 EXP purchase was automatically
funded, not manually assigned. The23:45:52 sample caught a beard/support observation
mismatch while allocations remained funded. By23:46:26 it had recovered WITHOUT
another re-equip/settings/route repair. This transient is not evidence that the
original long idle was harmless, nor proof of a persistent pending claim.

### Natural Fruit Event

`20261022-235254-continuity-natural-fruit-event.json` contains358 passive samples
over360.02seconds, requested one second apart. Native heartbeats can repeat; count
these as polling samples, not358 distinct game callbacks. No live mutation was
issued during this observation window.

- 23:46:26 native save: fruit7 active,3364seconds, harvests8; native AT and targets
  3000/3000. Native Adventure/Knowledge/Luck/Power-beta fruits had3454seconds and
  harvests8. The earlier23:23 save separately proves fruit7 already active then.
- Around23:48:51, the permanent one-hour fruits naturally harvested; no manual
  harvest or timer change was issued. Subsequent native saves show harvests9.
- 23:49:02: idle Energy67058 after a real265-EXP capacity purchase; automatically
  funded by23:49:04. Magic stayed fully funded.
- 23:50:20: Energy NGUs162171289, idle0, active preset.
- 23:50:22: native automatic fruit7 recall/activation; Energy NGUs0,
  idleEnergy142643201, applicationIssueObservationsChanged. Logs explicitly record
  `Removing energy for Fruit of Arbitrariness (7)` and `Activated ...` at32489seconds.
- 23:50:24: automatic NGU funding resumed, NGU total143856689, idle471912, current
  applicationIssueNone. Available Energy then refilled after the20million cost.
- 23:50:46: Energy NGUs162171289, idle0. No second repair was used. The recorded
  event/refill is NOT counted as continuous zero-idle time.
- 23:51:02: another15328 Energy capacity increment briefly idle; automatically funded
  by23:51:04, NGU total162186617.
- 23:54:08 native save: fruit7 active,227seconds, harvests9, confirming the actual
  new growth cycle. Other one-hour fruit harvest counters are also9.

The strict sampler reports28 unhealthy polling samples, including fruit recall,
refill and the two capacity events. It intentionally did not turn these into an
all-green result; the state timeline distinguishes bounded convergence from the
earlier20+minute all-Energy idle failure.

### Final State And Continuity Audit

23:54:08 save plus three fresh samples ending23:54:28:

- Energy162201956; idle0; Energy NGUs0/1/3/5 each40550486, total162201944. Remaining
  12 Energy is basic training. Magic12901697; idle0; NGUs0/1=6450849/6450848.
- All excluded Energy NGUs2/4/6/7/8 and Magic NGUs2/3/4/5/6 remain0.
- Scheduled NGU owner is active on original entry4, both lanes AppliedVerified.
  Earned NGU time2097.55s at23:54:28 is distinct from elapsed6146.66s.
- Current acquired gear IDs match:
  `150,151,124,153,159,116,139,138,440,109,148,196`.
  Same gear set as before repair; accessory positions were reacquired. Diggers
  remain `0:40,3:40,4:18,5:15,6:3,7:3`; core available beards1/3/5 remain selected,
  with allowed optional0/2/4 rotation. The saved initial beard subset need not equal
  a later legitimate rotation; that mismatch alone is not an exact setup failure.
- Actual native AT levels and targets remain3000/3000; no AT allocation after
  completion. Manual Walderp remains prior `target killed`, deaths0, budget3,
  awaitingNextSpawn. No new kill is claimed or required for this Energy repair.
- Adventure enabled, actual native ITOPOD zone1000 and bounds196/196 with autoattack.
  Native screenshots show continued defeated enemies and435-progress rewards.
- Game PID47484 and injector epoch10:31:29 remain unchanged. Loaded MVID unknown;
  no restart, reinjection, accepted candidate or live rollback is claimed.

Final handoff recheck ended23:59:13: three fresh healthy samples, Energy
NGUs162232600 (four equal40558150 allocations), native current162232612, idle0;
Magic12901697/idle0; stage actual applied2374.74s, original entry4. Native ITOPOD
PP progress increased49803 -> 65463 between23:56:26 and23:58:54, with different
defeated enemies and435-progress rewards. This is actual current farming progress,
not only the enabled routing preference or a forecasted rate.

`20261022-regression-repair-continuity-audit.json` compares original23:23 backups
with final23:54 backups/fresh saves. **PASS:** plan and preset library are byte/hash
unchanged; settings diff is empty; epoch fields and ALL scheduler commitments
(entry/visits/enteredAt/expectedEnd/durations/completion/override history) unchanged;
native clock advanced and NUMBER preserved; AT targets unchanged; applied credit
not reset. Prior NGU applied588.652s was retained and advanced to2076.146s at the
saved checkpoint. Expected scheduler end remains86400. No manual NGU filling,
stage override, new plan, target/weight edit or rebirth was used.

### Diagnosis And Remaining Risk

**High confidence in the observed failure boundary and operational recovery:** the
installed scheduled receipt was displaced by changed observations after its Energy
lane had been recalled, despite enabled management and an already activated fruit.
Neither a current combat actor, capped NGU sinks nor a still-unfunded fruit explained
the original all-Energy idle. Safe Zone alone did not recover it; a supported
explicit gear event followed by normal SAME-entry acquisition did.

**The precise initiating internal claim/writer remains unproven.** This loaded
build does not expose claim identities or its MVID. Current source's
`RefreshGlobalPresetExecution` has an early exact-observation guard for
`resourceOnly || receipt.CurrentGearContinuation`, and its receipt validator
classifies support/allocation differences asObservationsChanged. Those are concrete
read-only investigation leads, NOT proof they are the exact loaded instructions.
Gear IDs also cannot rule out native material growth; the earlier persisted policy
had stale material descriptors while native items had been merged/boosted.

The captured23:45 transient shows native beard selection differing from the
receipt's selected rotation, then self-converging before the fruit event. The
subsequent actual fruit event recovered, unlike the22:50 event. Review owners need
the combined timeline: scheduled NGU + owned gear growth + optional beard rotation
+ native fruit recall/activation/refill, retaining same-entry decisions/credit.
No production/shared implementation was edited to guess at that cause.

Remaining release gate is the separate reviewer's full combined acceptance and an
exact tested frozen artifact. No competing build was run and no failing candidate
was deployed in this pass. A repaired installed application receipt is not acceptance
of the in-progress source cutover or proof that another long idle cannot recur.

### Regression Artifacts

All paths below are under workspace `temp/checkins`:

- Supervisor failure: `20261022-231054-hourly-supervisor-2300-continuity.json`.
- Initial backups/save: `20261022-232306-830-bounded-save*`.
- Negative Safe Zone check: `20261022-232634-hourly-regression-safe-zone-test.json`.
- Preview and actual repair: `20261022-232812-282-bounded-previewngugear.json`,
  `20261022-232838-086-bounded-reequipngugear.json`.
- Post-repair native saves: `20261022-232958-393-bounded-save*`,
  `20261022-234625-955-bounded-save*`.
- Longer observations: `20261022-233018-hourly-regression-repaired-sample-1.json`,
  `20261022-234552-continuity-repair-13-minute-window.json`,
  `20261022-235254-continuity-natural-fruit-event.json`.
- Final save/backups and frozen inject/allocation/combat logs:
  `20261022-235408-559-bounded-save*`; quicksave command
  `8a6aa3521c51494693e733625467c135`.
- Final three samples: `20261022-235429-hourly-regression-final-post-fruit.json`.
- Handoff three samples: `20261022-235913-hourly-regression-handoff-final.json`.
- Configuration/native continuity: `20261022-regression-repair-continuity-audit.json`.

Final fresh save hashes: TXT
`09AEA5A0A3C60281426306B8A055C9E4FBC656A273AE937F863FB1391DE2462B`, JSON
`3412856D04B02E6EDF7C450AB31857D50E4D277613FA54CE3ED4D6DEC593B429`.
Native screenshots remain in the approved temporary directory, named
`ngu-game-20261022-232231-978.png` (initial idle), `...-232309-062.png` (active fruit),
`...-234627-812.png` (pre-event fruit), `...-235513-199.png` (new fruit cycle) and
`...-235626-521.png` and `...-235854-756.png` (current ITOPOD kills/PP). Helpers are operational scripts only;
all bounded waits have finished and no unattended/hourly process was installed.

Operational report schema correction: new `observe-hourly-ngu.ps1` summaries now
distinguish `stageElapsedSeconds` from actual `stages[index].appliedSeconds`.
Older summary fields named `stageAppliedSeconds` held the elapsed clock; the full
saved stage rows preserve the actual earned value and remain the authoritative data.

## Resumed Bounded Checkpoint: 21:56-22:16

**Existing installed build is currently running the original `24hr` plan at NGU
Energy, spending both lanes, farming ITOPOD, and has completed an observed automatic
Walderp kill with zero deaths. Candidate deployment/restart is BLOCKED by the current
full offline gate.** No source release or completed daily rebirth is claimed.

This resumed pass followed `agent-supervision-20261022.md`. It did not start another
one-hour sleep, mutate review-owned production files, perform git operations, or
ask a parent task to handle live actions. Other streams retain offline ownership.

### Actual Native Results

| Observation | Actual result |
| --- | --- |
| 21:56:30-21:56:50 | Original plan running at AT Adventure; native idle Energy/Magic both 0 across three fresh samples. |
| 21:57:03 native AT screen | Actual Toughness/Power 2738/2738; configured native targets 3000/3000. |
| 22:03:45 native AT screen | Actual levels 2858/2858; targets unchanged. |
| 22:08:17 native AT screen | Actual levels 2937/2937; targets unchanged. |
| 22:11:58 | Natural transition to NGU stage index 3; automatic actor `Fighting WALDERP (16)`, three deaths remaining. No stage override or lowered gate. |
| 22:12:20 native combat screen | Actual WALDERP enemy, native attack cooldowns, damaged enemy HP 3.542e8/1e9, and combat log showing attacks against WALDERP. |
| 22:12:40 | Observed active actor -> inactive `target killed`; deaths 0, remaining deaths 3, disarmed true, awaitingNextSpawn true. |
| 22:12:52 native Adventure screen | Walderp native spawn countdown reset to 2:44:45; ordinary ITOPOD farming resumed. This corroborates the actor outcome, not a fixture-supplied kill. |
| 22:13:45 native AT screen | Actual Toughness/Power 3000/3000, native targets still 3000/3000, allocations 0 after completion. No target-field repair was needed in this pass. |
| 22:14:06-22:14:26 | Three fresh NGU samples: idle Energy 0, idle Magic 0, both resource lanes `AppliedVerified`. |
| 22:15:27-22:15:47 | Three additional fresh zero-idle NGU samples; stage elapsed/applied ownership continued advancing. |
| 22:16:08 | Fresh quicksave and readback: zero idle, correct acquired gear/support, adventure enabled, original plan and epoch retained. |

Final actual allocations:

- Current Energy 161245900; NGUs 0/1/3/5 each 40311472, total 161245888.
  The other 12 Energy is basic training, not idle. Excluded Energy targets
  2/4/6/7/8 are all 0.
- Current Magic 12901697; NGUs 0/1 hold 6450849/6450848, respectively. Excluded
  Magic targets 2/3/4/5/6 are all 0.
- Actual NGU gear IDs match the same-entry acquired checkpoint exactly:
  `150,151,124,153,159,116,138,139,148,196,440,109`.
- Final support matches acquired support:
  `1,2,3,4,5|0:40,3:40,4:18,5:15,6:3,7:3`.
  A 22:15:11 snapshot recorded different optional beard slots; these are allowed
  minute rotations from saved `BeardRotation=[2,4,0]`, not a gear mismatch.
  Core available priorities 1/3/5 remained selected; beard 6 is unavailable here.
  ID/support comparison is not an independent native equipment-material audit.
- During the actual fight and post-fight gear/capacity refill, Energy was temporarily
  idle. For example 301068 at 22:13:10 and 1137368 at 22:13:29. These were recorded,
  not counted as zero-idle samples. The subsequent repeated zero-idle observations
  establish that this specific refill converged; they do not excuse unrelated idle.

Actual adventure progress was observed independently of enabled flags:

- During AT, native PP progress rose 336139 -> 342874 between 22:06:56 and 22:07:53,
  with different defeated enemies and native 449-progress reward messages.
- After NGU and preference restoration, native PP progress rose 387298 -> 394693
  between 22:15:01 and 22:16:09, with native 435-progress rewards and different
  defeated enemies. Optimal floor was 196. No long-run throughput claim is inferred
  from the UI's forecasted kills/hour.

### Identity And Continuity

- Game PID **47484**, started `2026-10-22T10:31:16.0179883-05:00`; injector epoch
  `2026-10-22T10:31:29.0758554-05:00`; desktop UI PID **2852**. These were not
  restarted during this pass.
- The loaded build does **not** publish its assembly MVID in available runtime or
  current startup diagnostics. Loaded MVID remains **unverified**. Earlier morning
  MVID observations predate this 10:31 process and are not substituted. Mutable
  `bin`/`dist` artifacts are not accepted as identity evidence for loaded memory.
- `24hr`, automatic rebirth, automatic Energy/Magic management and 86400 trigger
  remain enabled. The existing NUMBER condition and All semantics are preserved.
- Captured rebirth counter remains **230**, origin `2216314.5006405637`, generation
  `3605c9de98b44e6a9deba19817cf2ef8`; natural scheduler entry progressed 3 -> 4.
  No new rebirth, forced stage entry, reset clock, checkpoint rewrite or extended
  preparation/rebirth deadline was used.
- Prior automatic Augments + Wandoos runtime reports `cancelled` with
  `Deadline reached before Augments targets; later Wandoos was not started.`
  The scheduler's elapsed row completion is not proof that both training phases
  succeeded. Native Wandoos AT dump levels are 1459/1459. This is a retained
  preparation coverage/problem report, not a newly fixed source claim.

### Candidate Gate And Restart Blocker

Executed the complete default wrapper, holding the shared verification mutex
across rebuild and all native runs:

```powershell
& "A:\misc\NGU-Automation\NGUInjector\checks\VerifyExecutorOffline.ps1" -GameFolder "C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE" -LogPath "A:\misc\NGU-Automation\temp\checkins\bounded-candidate-20261022-2158.log"
```

Production rebuilt successfully: **MVID `28eb4976-3d94-4949-999f-958d2c124b6c`**.
Full gate FAILED, with these concrete boundaries:

- PresetIntegration: explicit `manual-overlay-True` fresh-stage acquisition rejected
  while native zone 1000 remained; `Program.cs:894`, after 9140 assertions, not a pass.
- Persistence diagnostic: null native res3-perk data during
  `SetupScopedExecutionReview`, `ExecutionPersistenceChecks.cs:135/193`.
- CombatPrepIntegration: actual new-entry fight did not start;
  `AutomaticEntryRegressions.cs:69`, after 7263 assertions, not a pass.
- BlackBar: fight-prep/entry dispatch, scheduling preparation and Iron Pill
  reservation failures; detailed aggregate and missing command-result evidence in log.
- Ownership: native pit leaf lacked current scoped authority/durable result;
  `MoneyPitRecoveryChecks.cs:118`.
- OrdinaryPolicy did not compile: removed `OptimizeAndAllocateIdle/Target` plus
  missing `RequestAugmentAllocation`, `ExecutionResourceBudget` and
  `ExecutionBaselineResourceMapMatches` fixture boundaries.

Same-DLL subsets passed: foundation42, core937, route719, finite172, overlay297,
TitanGold1497, equipment94, gear20805, WinForms174, native AT48, aggregation4646.
They do not override the failed full gate. No candidate was frozen/deployed and no
new game/UI PID, reinjection or loaded-MVID verification is claimed. A safe restart
of an old artifact was also withheld because an exact proven known-good artifact
matching the current loaded assembly was not established.

### Mutations And Backups

Only these new game-state actions occurred in the resumed bounded pass:

1. `quickSave`, command `1d5fb1467697432993b76e7529acdf89`, at 22:06:17.
2. `applySettingsPatch {_customAdventureEnabled:true}`, command
   `a202ea28f4c14303a1234695fb74116b`, at 22:13:26. This restores the preference
   temporarily paused by the earlier live repair; no other setting was requested.
3. `quickSave`, command `12dcce77e14e45209f5013446f7dae3c`, at 22:16:08.

Each helper action backed up settings, presets, `24hr`, and stage checkpoint before
mutation. Save actions also archived existing quicksaves and copied fresh nonempty
`NGUSave.txt`/`.json` after validating write timestamps. Final fresh-copy SHA-256:

- `NGUSave.txt`: `997CFF563D023AB39BAF60E3A608AE68234C5556EE187FA7C3C4D7076C2CB949`
- `NGUSave.json`: `68CF78ABC011880ED40CA3DC77D62881107B728610F7D30A7AC7FD5C2CD42676`

Native UI interaction in this pass only navigated AT/Adventure and displayed native
tooltips. No manual attack, direct native-value write, Safe Zone repair, spawn reset,
death-budget edit, target reduction, stage reset, or rebirth was issued.

### Read-Only Route Diagnosis

Earlier Safe Zone clicks were **operational repairs, not root-cause fixes**. Current
source still demonstrates the relevant missing-ownership boundary:

- `Main.InlineStageSetup.cs:198-199` bases inline retirement permission on
  `ManualAdventureSupportRouteAuthorized(Settings.ManualAdventureEnabled, receipt)`.
- `Main.ManualAdventureOverlay.cs:517-529` accepts Safe Zone, an explicitly marked
  overlay route, or a verified manual takeover. With standalone manual disabled,
  ordinary non-safe ITOPOD is not thereby authorized for inline setup changes.
- `Main.CustomFocus.cs:276-294` rejects coupled gear/support changes before route
  retirement if that predicate is absent/false. Disabling adventure flags alone
  does not prove native retirement.
- `Main.GlobalPresets.cs:948-962` likewise rejects a new scheduled acquisition at
  non-safe zone unless current receipt/manual-route ownership matches. A previous
  stage's receipt can cease to satisfy the new stage owner.

This source path is consistent with the earlier captured Time Machine and AT
handoff waits. Loaded MVID uncertainty prevents claiming exact current-source/live
instruction equivalence. Review/core owners must implement a correct predecessor
witness/retirement handoff without treating an arbitrary inherited or foreign route
as owned. No overlapping production edit was made in this resumed pass.

### Artifacts And Hourly State

All machine-readable reports/backups are under workspace `temp/checkins`:

- `20261022-215650-hourly-resumed-bounded-start.json`
- `bounded-candidate-20261022-2158.log`
- `20261022-220617-359-bounded-save*`
- `20261022-220726-hourly-bounded-at-and-adventure-progress.json`
- `20261022-221209-bounded-walderp.json` and `20261022-221240-bounded-walderp.json`
- `20261022-221311-hourly-bounded-after-walderp-kill.json` records transient idle.
- `20261022-221326-892-bounded-restoreadventure*`
- `20261022-221428-hourly-bounded-post-refill.json`
- `20261022-221511-803-bounded-snapshot*` records the optional beard rotation.
- `20261022-221548-hourly-bounded-final-native.json`
- `20261022-221608-157-bounded-save*`

Native screenshots are in the approved `C:\Users\Kevin\AppData\Local\Temp\opencode`
directory, named `ngu-game-20261022-<time>-<suffix>.png`, with the times above.
The operational helpers are workspace `temp/capture-bounded-live-20261022.ps1` and
`temp/watch-bounded-walderp-20261022.ps1`; all their waits have finished.

The interrupted one-hour sleep is NOT a completed hourly checkpoint. No hourly
background process or new scheduled task is active. Existing NGU tasks from October
15/20 have no next run. Continuation is explicitly handed back to supervision;
no automatic resumption or unattended 24-hour monitoring is claimed. Next daily
threshold remains approximately October 23 at 14:48 local, subject to NUMBER and
native/protected-work conditions. That rebirth has not yet occurred.

## Earlier Hourly History

The October 22 request is an actual one-hour sleep followed by rebuild and live
allocation/loadout verification, repeated until the next automatic 24-hour rebirth.
No scheduled future observation counts as a completed check. No new scheduled task
or unattended monitoring process has been installed.

## First Check

- Actual initial `Start-Sleep -Seconds 3600` completed at 18:16 local time.
- Release rebuild succeeded, MVID `bad5dd09-cbec-41f6-9ab5-3635ba57a741`.
- Full native PresetIntegration failed in `CheckAdventureDynamicManualPriority`:
  selected setup was waiting for the current adventure owner. No new DLL was
  injected and the game was not restarted.
- Existing live run was also stalled at Time Machine, with 96,896,425 idle Energy
  and 12,828,496 idle Magic. Both resource-management switches alone were insufficient.
- At 18:27, temporarily paused CustomAdventure through the supported settings
  bridge. At 18:28, explicitly selected the native Adventure dropdown's Safe Zone.
  At 18:29, restored CustomAdventure. No stage override, completed-work rewrite,
  plan/NGU-weight change, or rebirth-deadline extension was performed.
- Native observations at 18:28:47, 18:29:02, 18:29:22, and 18:29:37 show idle Energy
  and Magic both zero. Time Machine holds 96,896,425 Energy and 12,828,496 Magic;
  the remaining 12 Energy is basic training, not idle. Required preparation
  initially earned applied time; NGU had not yet been reached at that observation.
- At 18:34, the stronger follow-up found that resumed adventure caused inline
  setup to wait again. Existing Time Machine allocations remained funded, but
  applied time stopped at 60.62 seconds. Zero idle alone is NOT a healthy-stage
  proof. The observer now rejects waiting execution and non-advancing fixed-stage
  time. Adventure must remain temporarily paused until preparation can advance;
  restoring the preference is still required before claiming final healthy state.

## Offline Follow-Up

`Main.CombatRouting.cs` now captures the exact predecessor route-retirement witness
before applying an incoming claim. It no longer circularly requires the incoming
support to be applied before authorizing retirement of the predecessor. Retained
unapplied delegated processes retry the same acquisition, and Keep-current does not
record a generated loadout marker. Foreign route/support/floor/material/resource
regressions were added without weakening the shared safety gate.

The route selector passed 430 native assertions on MVID
`2369e40a-15d8-4de9-b7d1-504ef993b3cf`. This is not full release acceptance.
The subsequent focused adventure-dynamic run on MVID
`e034ce77-077d-43d4-8c8c-880bf1f7e771` failed earlier in
`CheckAdventureDynamicLifecycle`: native NGU allocation expected 2471, observed 0.
The Keep-current assertion was not reached on that build. Deployment remains blocked.

## Evidence

`../temp/observe-hourly-ngu.ps1`, relative to the repository, passively reads installed
runtime/configuration and takes three native-allocation samples ten seconds apart.
It records actual gear/support and available desired-state telemetry; missing exact
setup telemetry is not treated as proof of an exact match. Reports and its baseline
are under workspace `temp/checkins`. A successful rebirth requires a newer captured
counter, a new >=86400-second history record, and a reset live clock together.

The installed `24hr` plan last auto-rebirthed October 22 at 14:48:04. Its next
86400-second threshold is approximately October 23 at 14:48 local time, subject to
native/save timing, NUMBER protection, and protected-work gates. A configured
threshold is not evidence of a completed future rebirth.
