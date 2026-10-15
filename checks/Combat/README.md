# Offline Combat Checks

Run from the repository root:

```powershell
dotnet run --project checks/Combat/CombatChecks.csproj
```

This no-framework .NET 8 console project compiles the real `Main.CustomAdventure.cs`,
`CombatManager.cs`, `CombatHelpers.cs`, and `ZoneHelpers.cs` through source links. No game assemblies,
Unity installation, injection, settings files, or NuGet test packages are needed.
Failures print expected and observed boundary actions and return exit code 1.

Coverage includes exact true-prompt attack IDs 3 through 6, every distinct
false-prompt alternative, unavailable required moves, invalid/incomplete prompts,
prompt priority over buffs and cooldown reservation, movement gates, ordinary
regular attacks, grow-count reservation boundaries, all five Walderp enemy types,
early-form boss-only routing, idle disabling, and repeated manual ticks with
regular unlocked but no strong training. Negative detection cases include all
four Beast versions, ordinary bosses, misleading names, and zone 19. Normal idle,
manual, healing, fast, preserve-idle, attack fallback, and boss-only behavior have
control checks.

Custom-adventure checks invoke the private production route through reflection.
They cover boss 101 at 1017 seconds in ITOPOD, fixed-zone, and boost-farm modes;
bosses 89/90/101 at 3599.99/3600 seconds; disabled gold push, locked Time Machine,
and its challenge; effective versus raw boss progress; and exact prior-gear
restoration on success, death (including before enemy observation), and expiry.
They verify selected-farm return, no retry of successful/failed/lost-lock targets,
a new attempt at a higher unlocked non-titan zone, safe-zone healing before entry,
same-call trash rerolls, priority cancellation, busy locks, and failed equipment.
ITOPOD return also checks the production-computed optimal floor inputs.
Interruption checks cover entered attempts and pre-bookkeeping kills/deaths,
including lost Gold before entry; only an unentered attempt still holding Gold
can retry after cancellation. The real adventure reset helper restores active
gear, clears all attempt state, is idempotent, and permits the same target in a
new rebirth. Fixtures include active entered/healing, post-kill, completed, and
failed states. These call the extracted helper, not the full automation reset.
Effective bosses 870/871/897/902 exercise the zone-43 unlock boundary and exclude
late titans 44/45 without extending the supported autokill roster.

Gold-push fixtures model the game's idle-only path: idle autoattack stays enabled
and all four manual attack buttons are non-interactable. Enemy appearance and
disappearance still drive production fight tracking, restoration, and completion;
these checks do not claim manual attacks execute alongside idle autoattack.

Stubs represent game data, buttons, moves, zone selection, settings, logging, and
gear side effects. Plan loading, priority-goal blocking, titan routing, quests,
focus settling, and boost-target selection are unrelated boundary stubs; boost
scoring and optimizer/inventory correctness are not tested here. Assertions inspect
actual `doMove`, `setToggle`, and `changeZone` calls, gear action order/ownership,
equipped tokens, and route state, not source strings or copied policy.
The `EnemyAI.GetPV` adapter performs field reflection at the game boundary.
Gear fixtures track snapshot/acquire/optimize/restore/release and reset the default
loadout lock to `None` for every fight. Ordinary combat fixtures retain explicitly
supplied button readiness, not a simulation of the game's idle/manual readiness rules;
the harness does not simulate Unity timing, animations, damage, prompt generation,
or move-induced global cooldown changes. Normal-combat controls are focused, not
exhaustive. Enum names model the game boundary; this is not a binary-compatibility
check against game DLLs.

To verify regression sensitivity without editing production, run:

```powershell
pwsh -File checks/Combat/VerifyRegressions.ps1
```

The optional runner compiles isolated temporary copies with the boss-90 cutoff
restored and the death guard disabled. Each must exit 1 with the corresponding
behavioral assertion failure, not a compile error. Source transformations only
generate mutants; assertions execute real routing/combat code. Temporary projects
are removed afterward. The scratch parent defaults to the existing `opencode`
directory under the system temp directory; override with `-ScratchRoot` if needed.
