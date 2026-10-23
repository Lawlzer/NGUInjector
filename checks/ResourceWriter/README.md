# Resource Writer Checks

Run `dotnet run --project checks/ResourceWriter/ResourceWriterChecks.csproj`.
The project compiles the real production `Managers/ResourceWriter.cs` against a data-only game boundary.
It never loads, injects into, or mutates a running game or save.

Coverage includes every destination and lane, exact integers above 2^53, checked destination/recall
overflow, invalid and negative inputs, insufficient idle, unchanged unrelated lanes, render suppression,
accounting observed inside rendering, propagated rendering failures, exact ownership returns,
serialized/reentrant concurrent batches, native recall dispatch, basic retention, fruit page selection,
native blood cap dispatch, and exact observed native recall postconditions. Recall checks cover
nonthrowing rejection, partial returns that conserve totals, omitted wish allocations, unowned
destination/pool interference, lost or minted idle, changed collection shapes/character context,
pre-call overflow/negative rejection, and stopping a gear recall at the first failed lane.
Native operation bodies here are fixture behavior, not a verification
of native formulas, Unity rendering, or live scheduling. Preset/BlackBar integration is covered by the
repository's separately maintained allocation suites.

`ResourceWriter.Execute` serializes a batch but is not a rollback transaction. A rendering exception
may leave completed transfers applied; each completed transfer has already committed both accounting
sides. Callers must report failure and must not blindly retry the same transfer. All production calls
must remain on the game thread. The writer does not read management settings or acquire ownership:
callers authorize each affected lane. `RecallForGearSwap` deliberately affects energy, magic, and R3;
fruit activation and blood caps never implicitly recall resources. Augment/upgrade indices are tier
indices (not BlackBar's interleaved lane tokens); singleton TM/Wandoos indices must be zero.

Native recalls validate a detached expected allocation map against the observed result. They never
zero native fields to repair a rejected/partial recall. `Recall(true)` retains both basic training
arrays; `RecallAllEnergy()` recalls them too. Energy, magic, and R3 recalls include every wish entry
in that lane, matching the installed native `Character`/`WishesController` recall methods. Other
lanes must retain their exact allocations and idle pools. Conservation uses checked actual idle plus
recalled amounts, not `curEnergy`, `curMagic`, `curRes3`, or capacity getters, whose values may be
stale or clamped. Missing unowned models must stay absent; a missing recalled pool is rejected.

The source-linked SustainedFocus boundary already models native wish recall and passes these stricter
checks. The separately maintained `CombatPrepIntegration/Program.cs` recall substitutes (also reused
by `PresetIntegration/Program.cs`) omit wishes. A fixture with nonzero wish allocations must implement
the real native return semantics; it must not bypass writer validation to manufacture success.
