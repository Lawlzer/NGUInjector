# Agent Guidelines

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
