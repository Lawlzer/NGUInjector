# Agent Guidelines

## Working Contract

- Follow the current request's scope and file ownership. Read [current WIP](../wip.md) for product/safety requirements and [agent workflow](docs/agent-workflow.md) for checkpoints; historical reports are evidence, not incorporated instructions or mutation permission.
- Workers may use read, shell, build and test commands relevant to their assignment, unless that assignment restricts them. Do not impose an arbitrary no-shell rule. Actual global/tool security permissions still apply; report the specific denied tool/command rather than claiming this document overrides runtime restrictions.
- Delegate one bounded milestone with the complete relevant request, acceptance criteria, owned/excluded files, concurrent owners, code/evidence context, safety constraints and actual tool permissions. Do not assume workers inherit the conversation or frontend guidance.
- A known shared owner/conflict is a checkpoint, not a queue: do not overlap, wait/poll, recursively ask the parent or redelegate the same blocker. Do independent owned work if available, otherwise report the blocker and next step once.
- One owner coordinates shared build outputs and serializes handoff appends. Do not build directly while verification owns those outputs. Use fail-fast contention handling, not long queues or 30-minute ownership waits.
- Finish a practical milestone, not the whole mega-refactor per invocation. Avoid nested implementation/review loops. After two attempts at the same blocker without new evidence or progress, stop and hand off; do not weaken safety or assertions to continue.

## Commands And Evidence

- `verify.ps1` is the canonical offline runner: all scopes by default, or `-Scope UI|Source|Native|Harness`; Native accepts `-Suite` and `-Selector`. `test.ps1` is the game-facing runner: Diagnostics by default, or Inspect, Prepare, Live and Restore. Offline gates never authorize live actions; retain the game-facing runner's confirmation, backup and restore safeguards.
- Use `verify.ps1 -DeferReason` and `-HandoffNote` to append context to `artifacts/agent-handoff.md` when shared ownership blocks verification. Contention and source drift also produce a handoff automatically; they never authorize a conflicting direct build.
- Report exact scope, commands, exit codes, first failure, source/artifact identity and log/report paths. Scoped PASS is not a full gate; queued, deferred, skipped, timed-out or future work is not a pass. Long soak is a separate deliverable with user-controlled waits, never a mandatory sleep in every coding invocation.

## Handoff

- At completion or a stop, append one complete, self-contained entry to `artifacts/agent-handoff.md`, under your own UTC/session/task heading. Include the request/scope, changed files and owners, decisions, exact commands/exits/first failure, logs/reports and source or artifact identity, pending checks, blocker/next steps and safety limits. Put every agent's decision-relevant output there; do not scatter continuation instructions across new handoff files.
- Coordinate appends through the shared owner; never overwrite another entry. If ownership/tool restrictions prevent the append, report the complete entry and the actual restriction to that owner. Do not claim an unwritten handoff exists.
- After the entry is written, tell the user: `Have the next agent continue from A:\misc\NGU-Automation\NGUInjector\artifacts\agent-handoff.md`.

## UI Content

- Default to the minimum copy needed to complete the user's task. Adding visible text is a product decision, not a finishing step.
- Include necessary labels, actions, results, meaningful current state, and actionable blockers. Use the user's task vocabulary.
- Do not add decorative subtitles, helper paragraphs/cards, tooltips, promotional copy, or instructions that repeat an obvious control. Empty space does not need copy; visual polish does not require more text.
- Do not explain saving mechanics, storage, caching, polling, refresh cadence, or other implementation details in the UI unless the user explicitly requests that UI content. Do not turn technical documentation or developer reports into interface copy, or replace explanations with unnecessary badges.
- Preserve accessibility labels, actionable errors/recovery, and concise guidance or warnings needed for non-obvious decisions or consequences. A save failure can need a Retry action; normal saving does not need an explanation.
- Follow the relevant frontend guidance in `NGUInjector.UI/README.md` and `docs/frontend-global-presets.md` without copying their implementation explanations into the interface.

| Avoid | Prefer |
|---|---|
| "Manage your automation settings below. Click Save to save your changes." | Necessary controls and labels only; no introductory or obvious-action helper text. |
| "Changes are saved automatically on this device after a short delay." | Omit the explanation, rather than shortening it to a storage or autosave badge. |
| "This dashboard polls in the background and updates every 30 seconds." | Omit the explanation and update-cadence text. |

## Delegation And Review

- Pass these rules and applicable project frontend guidance to delegated implementers and reviewers; do not assume they inherit them.
- Treat UI-copy requirements as product requirements, not optional style preferences or formatting nits. Reviewers should flag violations as unmet requirements.
- Before finishing a UI change, review each new text element in the diff: what user need justifies it? If removing it does not harm task completion, understanding, accessibility, or safety, remove it.
- Confirm necessary labels, warnings, and recovery actions remain. Apply this check to new or touched UI, not an unsolicited rewrite of unrelated screens.

## Agent Reports

- Lead with the outcome, relevant verification, and unresolved blockers. Skip filler, repeated requests, and unsolicited under-the-hood walkthroughs.
- Explain internals when asked or when needed to communicate a material risk, decision, or limitation. Keep engineering evidence in the developer report or documentation, not the UI.
