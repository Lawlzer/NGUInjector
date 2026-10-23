# Activity Policy Checks

These checks embed the actual `NGUInjector/Data/ActivityPolicies.json` under
`NGUInjector.ActivityPolicies.json`. They link production `ActivityPolicy`,
`SimpleJson`, and `JsonFiles` sources, with no package dependencies or game DLLs.
The pure executable has no plan or game types. The integration executable uses a
`ProjectReference` to SustainedFocus's source-linked check assembly, with test-only
friend access. It reuses real `Main.Execution`, selected setup, beard coordination,
setup metadata, shared projection and resource sources, rather than compiling a
second executor or the deleted manager writers. `SetupBoundary.cs` was removed.

Run from the repository root:

```powershell
dotnet run --project checks/ActivityPolicy/ActivityPolicyChecks.csproj
dotnet run --project checks/ActivityPolicy/IntegrationChecks.csproj
dotnet run --project checks/ActivityPolicy/ActivityPolicyChecks.csproj -p:OmitPolicyResource=true -- --missing-resource
```

The last command intentionally builds without the embedded resource.
`Directory.Build.props` isolates each project and resource variant's build
outputs to prevent shared apphost/intermediate artifacts or incremental resource
omission from running the wrong executable.

Coverage: old default orders, exact quick-focus mappings, all executable group
and indexed-token boundaries, canonical/invalid tokens, normalized primary
intent, resource/gear separation, explicit manual adventure, support request
IDs/weights/names, defensive copies, exact selected support, foreign-support yield,
malformed JSON, unsupported handlers, missing metadata, and
missing resources. Manifest edits exercise existing executable tokens and weights;
unknown synthetic handlers are rejected, not treated as implemented features.

The UI tests independently import the same JSON file. Existing stage-preset
tests retain the exact legacy arrays and tuple-only index/label semantics.

## Executor Migration

The integration runner preserves every focus's gear/resource tuple, full primary
priority sequences, support IDs/weights/names, matching standard-stage metadata,
defensive copies and explicit manual-adventure intent. New application cases check
exact native active sets/levels, real configured budgets, retained retry and accepted
minute times, read-only replay, imminent-titan overlays without modifying base
requests, unavailable hard-support rejection, and settings replacement inside an
actual gear callback stopping the later support write.

The shared boundary supplies selected beard data and isolated native effects;
the grant, selected actuator and policy code are production sources. Its native
preflight uses the real `SupportProjection` and observes disabled/menu/controller/
unlock/slot state rather than asserting unconditional availability. It is NOT a
test of Unity or the compiled native `Main.Support` pod/input/partial-receipt code.

`SupportPrerequisiteChecks` runs nineteen timelines in this executable and in BOTH
full and explicit `--execution-core` SustainedFocus modes. Cached, invalidated-
retained and pending optional Fu must promote to a real hard selection. CombatPrep
and support-claim freezes verify present native Fu but reject missing Fu, a stale
active-list entry without its flag, locked/disabled beards, a missing controller,
locked Gold despite its active flag, and zero available slots.
Frozen verification must not draw another selection, write, recall or consume a
minute. All failed matrix cases are reported before the group throws; none is skipped.
Promotion cases additionally retain exact active/inactive digger levels, verify
that repeating the hard request performs no selection/write, and preserve the
original accepted minute even when replacing a pending optional attempt.

| Retired integration expectation | Current coverage |
| --- | --- |
| `RotateBeards` slot/Gold loop | Actual `SelectBeards` exhaustive caps/core/unlock/hard-set oracles in `checks/Beards` and the Support BeardPolicy wrapper. No selection policy is recreated in this fixture. |
| `EquipBeards` followed by automatic foreign repair | `ForeignSupportRequiresReview` requires rejection, preserved native support and no writes or minute renewal. |
| Legacy GPS receipt strings and weighted manager calls | `ExactSelectedSupport` and `ImminentTitanPolicy` inspect actual exact support, native levels, configured fraction and projected whole-support drain. |
| Independent fixture callbacks/authority stand-ins | `CallbackRevokesSupportAuthority` invokes the existing gear leaf callback and observes the real grant stop later support mutation. |
| Optional cached Fu treated as a hard prerequisite | Shared nineteen-case `SupportPrerequisiteChecks`, with real native flag/availability assertions and positive frozen controls. |

See [Support's verification and migration map](../Support/README.md) for current
combined results and the separate native-fixture/finite-action ownership limits.

Current post-fix Release results: integration **264 assertions**, including the
shared **19 timelines / 104 prerequisite assertions**; pure registry **876**;
missing-resource variant **1**. Full SustainedFocus passes **2,328**, with its
exclusive `--execution-core` selector passing **159**. The supervisor-transferred
production fix in `Main.CustomBeards.cs` is implemented, not pending coordinator
work. The original red evidence and narrow native-acceptance limits are retained
in the linked Support report.

## Production Integration

The normal production project compiles `Managers/ActivityPolicy.cs`, the shared
resource/digger helpers, and includes:

```xml
<EmbeddedResource Include="Data\ActivityPolicies.json">
  <LogicalName>NGUInjector.ActivityPolicies.json</LogicalName>
</EmbeddedResource>
```

`SimpleJson.cs` and `JsonFiles.cs` are existing production dependencies. Any
source-linked check that includes `BeardManager`, `Main.CustomSetup`, or
`BlackBarAllocator` must also link `ActivityPolicy`, `SimpleJson`, and `JsonFiles`,
and embed the actual JSON with the same logical name. Test enum boundaries must
match production numeric values, especially `GearOptimizationGoal`.

The API entry point is `ActivityPolicy.Current`. Its internal methods are:

- `DefaultPriorities(int focus, bool energy)` and `DefaultPriorities(string focus, bool energy)`.
- `IsValidPriority(string token, bool energy)`.
- `PrimaryPriority(string[] configured, int focus, bool energy)`.
- `FocusDefinition(string goal)` returning the immutable declared numeric gear/resource tuple.
- `StageDefinition FocusStageDefinition(string goal)` returning that tuple plus defensive copies of `EnergyPriorities` and `MagicPriorities`. It uses the unique `stagePresets` match on `(GearGoal, EnergyFocus, MagicFocus)`, never display labels or a second Quick-to-preset mapping. Duplicate preset tuples fail metadata validation, even if their orders are identical or no Quick currently matches them.
- `BuildSupport(string primaryEnergy, string primaryMagic, bool bossFight, bool manualTitans, bool manualAdventure)` returning beard IDs and immutable primitive digger requests.
- `CoreBeards()` and `DefaultBeardPriority()` returning defensive copies; both preset planner default-beard paths consume the latter.
- `PreparationPriorities(bool energy)` for dynamic implicit AT orders and `ImminentTitanDiggers()` for opportunistic support requests.
- `DiggerBudgetFraction` for the ordinary weighted GPS budget.
- `Parse(string json)` for independently validating edited manifests without replacing the embedded singleton.

Stage allocation and support use normalized primary intent. The JSON's 18 unique
priority sets are referenced by backend defaults, preparation and UI profiles;
existing explicit saved-plan arrays remain overrides. Dynamic AT decisions and
Blood overlays remain executable runtime policy, not registry logic. Data edits
require backend/UI rebuild, backend reinjection and rebuilt UI reload, not runtime hot reload.

Quick means a manual, indefinite rebirth override until the user changes or stops
it. It is not timed Combat Prep, an automatic Augments/Wandoos search, or a new
planner. Existing gear optimizers and allocation target selectors still run normally.
`FocusSetupStage(CustomFocusGoal goal)` constructs one hydrated stage for support
and allocation; initial application passes that same instance through
`AllocateCustomFocusResources(CustomFocusGoal goal, bool settle = true, CustomPlanStage stage = null)`.
Refreshes use the same metadata-backed construction. Saved standard stages are
not hydrated or rewritten; their explicit arrays remain user overrides.

Unmatched tuples, including Adventure (Fight gear with AT/Blood), Fight and
Manual Titans, receive explicit copies of their declared focus defaults and keep
their existing special support behavior. Magic NGU keeps `EnergyFocus=None` and
does not acquire energy/R3 allocations. A custom unmatched Magic gear tuple still
drives the actual gear optimizer, retaining the existing read-only capacity and
owner-handoff preflight behavior.

The checks compare every matching Quick against the actual embedded standard
preset, including effective allocation orders, gear/support requests and actual
allocator transfers with AUG overflow. They also exercise caller-array isolation,
renamed labels/edited tuples, duplicate-tuple rejection, unmatched fallbacks,
persisted user overrides and repeated manual passes far beyond ordinary stage
durations without automatic transitions. The SustainedFocus metadata-gear test
uses the same temporary private-definition replacement as the ownership R31
regression and restores the immutable original in `finally`.
See [Activity Policies](../../docs/activity-policies.md) for editing rules and the
[completed verification](../../docs/AUTOMATION_IMPROVEMENTS.md#completed-verification) for recorded full-run results.
