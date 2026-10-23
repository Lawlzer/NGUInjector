# Reconciliation Follow-Up Audit

Read-only source review after automatic combat recovery and preset commit-budget
fixes. No new production behavior, tests or live settings were changed. Findings
below describe code paths and inspected regression contracts, not new live reproductions.

## Priority Work

1. **Classify mutation outcomes before choosing a retry policy.**
   `ResourceWriter.Transfer` commits both accounting fields before rendering, which
   may throw (`Managers/ResourceWriter.cs:32-49`). Preset refresh treats every
   exception after `mutationStarted` as an unknown partial write, removes its receipt
   and permanently latches `NeedsUser` (`Main.GlobalPresets.cs:965-978`). Preserve
   acquired intent and separate verified accounting/render-only failure from foreign
   changes and genuinely unknown writes. Recovery should verify first, then repair
   only still-owned state with backoff; it must not blindly repeat additive transfers.

2. **Finalize ownership invalidation even when target cleanup throws.**
   `InvalidateCustomEquipmentApplication` calls target release before suspending
   progress or setting invalidation (`Main.CustomAutomation.cs:525-533`). Refresh's
   catch has the same ordering before receipt retirement. Target release can finish
   native accounting/target restoration and then propagate a rendering failure.
   Record transition intent first and finalize disposition in `finally`, retaining
   original and cleanup errors. Owned lease release is not a foreign target edit.

3. **Observe overspending support without rejecting an affordable replacement.**
   `CaptureSupportContext` rejects current drain above income
   (`Main.Support.cs:122-126`), and exact-support preflight captures before evaluating
   the desired selection (`:157-166`). Thus an income drop can reject even an empty
   or cheaper replacement. Keep numeric/identity/parity validation, but enforce
   affordability on the desired selection rather than using current overspending
   as a universal observation failure.

4. **Do not consume preparation on a generic acquisition exception.**
   `Main.AutoCombatPrep.cs:277-299` can complete a durable, untouched automatic entry
   after preflight failure when no continuation exists. Management-disabled terminal
   row consumption is explicitly asserted by existing scheduled integration tests.
   A behavior change should classify retryable acquisition failures and preserve
   the original deadline; completion should be reserved for expiry, explicit cancel
   or a deliberately defined terminal outcome.

## Additional Cases

- Manual Adventure overlay exit updates its support observation only after the
  support operation returns. A still-owned partial write can make its own next
  ownership check fail (`Main.GlobalPresets.cs:104-138,898-900`). Keep the exact
  attempt journal, never adopt arbitrary current support.
- A rejected mutation-free definition is latched by owner/preset ID rather than
  the rejected input fingerprint (`Main.ResourceCoordination.cs:145-148,196-207`).
  Permit revalidation when relevant rejected inputs change, without applying
  saved edits over an already-acquired immutable definition.

## Design Direction

Use the existing coordinator, not another universal solver. Separate desired intent,
observed application and failure disposition. Distinguish retryable unavailability,
changed invalid input, proven-owned partial work, foreign conflict and unknown
mutation. All retries must revalidate owner/entry/epoch and current targets/support.
Stage credit requires verified application; manual external command uncertainty
must not be converted into automatic command replay.

Keep hard checks for conservation, native bounds, managed lanes, save/entry identity,
user edits and paid-AT/W sequencing. Replace blanket permanent-stop policy, not those
checks. Consolidate repeated lifecycle dispatch/status decisions only after the
outcome classification and focused failure-path tests are in place.

## Proposed Executor Boundary

The user's follow-up asks whether these fixes should become a simpler desired-state
execution model rather than additional independent recovery paths. Recommended
scope: rewrite composition/application/handoff across existing executors, while
retaining gear/resource/support planners and their mathematical contracts. This is
a design recommendation, not an implemented migration.

`ReconcileCustomResources` currently dispatches among separate executors. Build one
effective intent from the selected stage/preset and compatible temporary demands,
then observe, project, apply necessary differences and verify. Existing
`ComposeResourceDemands` already demonstrates baseline-plus-temporary-reservation
composition without stacking duplicate blood funding.

Keep goals and execution commitments separate from disposable application evidence.
Stage entry, earned work, original deadline, acquired AT gates, fight identity and
non-updating selected policies must survive an executor reset. Levels, currency
spent and finite-action outcomes are native facts, not rollback state. Application
flags, observed amounts and compatible caches can be recaptured; losing a cache
must not grant permission for an otherwise forbidden optimizer search.

When a temporary claim ends, a current goal claiming that domain directly supplies
the next desired setup; do not restore a historical setup first. Unclaimed domains
preserve manual/native state. Narrow owned returns remain necessary for borrowed
native configuration and standalone temporary actions without a successor goal.
Ongoing dangerous activities need a safe stop/handoff before their supporting gear
is removed. Purchases, spells, rebirths and entry fights remain explicit finite
operations, not repeatable desired-state setters.

Migrate baseline application first, then temporary claims and handoff, then combat
phase application. Cut each path over atomically; old and new writers must not
compete. Initial validation should prove convergence after each partial-write
boundary, preservation of foreign edits and unclaimed lanes, no duplicate finite
actions, no extra stage credit/budget and no new search with AutoUpdate disabled.
