# Preset Commit Budget

## Confirmed Failure

At 23:18 local time on October 20, 2026, live `24hr` was enabled at AT Adventure,
with 72,534,807 of 72,534,819 Energy idle. Most Magic remained allocated.
Combat-prep recovery was clear. Resource execution instead reported `NeedsUser`:
`Preset was partially applied; automatic retries stopped. Preset operation
exceeded its time budget; preview again.`

Read-only capture/search/preflight and native application shared one stopwatch.
The three-second preparation limit was checked after gear/support mutation,
during post-recall capture and after funding. Thus elapsed planning time could
abandon a valid prepared application, release its AT target lease and latch the
stage against future retries. The live diagnostic did not identify the exact
deadline check that fired; controlled offline tests reproduce the mechanism at
multiple boundaries.

## Fix

The final preparation deadline remains before committing a preflighted policy.
Once native application begins, the preparation stopwatch continues for duration
reporting but no longer aborts that commit. Post-recall captures in prepared Apply
and resource refresh explicitly disable cooperative deadline enforcement. They
still perform native parity, validity, feasibility, ownership/target and allocation
checks. Exact-policy evaluation does not run a replacement optimizer there.

Preparation failures remain read-only retryable waits. Unknown native exceptions,
foreign target changes and genuinely partial native failures retain their safety
handling. Manual Preview + Apply tokens remain at-most-once. No deadline was simply
increased, no unknown command is automatically replayed, and no weights were changed.
This does not provide a hard native wall-clock bound or change surrounding manual
Adventure overlay-handoff behavior.

## Verification

The new integration selector forces the real Framework stopwatch to 4,000 ms at
capture, search completion, gear, support, recall, post-recall capture (including
native getters/ITOPOD capture), exact evaluation and allocation rendering boundaries.
It does not sleep, synthesize timeout exceptions or replace deadline bodies.

- Old DLL `bdd17352-8607-428d-a35f-703714438c69`: 14 expected regression failures;
  seven controls passed.
- Fixed DLL `35a4947a-a38e-4d32-87ab-e20ee84b594e`: all 21 cases/638 assertions passed.
- Full compiled preset integration: 11,370 assertions passed.
- Full compiled combat integration: 9,417 assertions passed.
- Independent focused source review found no blocking issues.

The prepared-commit checks assert actual resource accounting, native AT funding,
verified receipts, unchanged search counts, protected-credit eligibility and manual
token consumption. Preparation-expiry and arbitrary native-error negative controls
retain the intended distinct behavior.

## Live Deployment

A fresh save/backup preceded a graceful restart and normal reinjection. The new
session started at `2026-10-20T23:46:59.8889512-05:00`, retaining the same plan, AT
Adventure stage index 2, generation/entry and configured targets. No manual stage
restart/set/skip or preset Equip was used. Settings, plans and preset library
retained their pre-deployment hashes; plan enabled and auto-rebirth enabled were
preserved. The injected dist DLL hash matched the tested Release DLL.

Six samples over 100 seconds, from 23:47:17 to 23:48:57 local time, all reported:

- Resource execution `Applied`, unchanged AT Adventure owner/entry.
- Energy 72,534,819, idle Energy 0.
- Magic 8,038,421, idle Magic 0.
- No combat recovery marker or preset failure latch in reported state.

The samples retained the same verified preset application timestamp; they prove
sustained funding across normal refreshes, not that a live re-equip exceeded three
seconds. Deterministic offline tests cover that boundary.

Backup and evidence are machine-local under
`C:\Users\Kevin\AppData\Local\Temp\opencode\preset-commit-budget-live-20261020-234622\`.
`verification.json` and `verified-runtime.json` contain the final observations.
