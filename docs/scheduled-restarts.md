# Scheduled Restarts

The October 20, 2026 restart jobs use `../temp/restart-ngu-24hr.ps1` from the
NGU-Automation workspace. The four one-time Task Scheduler entries are named
`NGU-Restart-20261020-114431-{30,60,90,120}m`. They run in Kevin's interactive,
non-elevated session, so Windows must remain awake with that user logged in.
Missed jobs are not replayed on login or wake, avoiding clustered restarts.

Each job archives existing quicksaves, requests and validates a fresh quicksave,
then uses the existing Release launcher without building or force-killing NGU.
The launcher reloads Steam Cloud after Quick Save requests cloud saving. Local
recovery copies remain beside each report; the verification checks NUMBER and
rebirth time for apparent rollback, but does not independently confirm remote
Steam synchronization.

Verification requires a new game process, a fresh external injector heartbeat,
the running `24hr` plan with its 86400-second trigger, preserved auto-rebirth
permission, and an active preset receipt for weighted stages. It rejects manual
overrides, invalid policies and cleanup-recovery blockers. Failed checks produce
a nonzero task result, not an automatic force restart or stage reset.

Reports and transcripts are in `../temp/restart-24hr-*.json` and `.log` relative
to the injector repository. The runtime JSON can contain both `autoUpdate` and
`AutoUpdate`; PowerShell readers must use PowerShell 7's
`ConvertFrom-Json -AsHashtable` to preserve these case-sensitive keys.
