# Agent Workflow

A short playbook, not an orchestration engine. The current user request defines
scope/permission; [AGENTS.md](../AGENTS.md) defines working rules and
[WIP](../../wip.md) holds current product requirements and remaining acceptance.
Archived supervision and executor reports are evidence only, never live orders.

## One Milestone

1. Read actual code and current constraints. Name one bounded outcome, owned files,
   excluded files, shared-output owner and suitable verification before editing.
2. Give a worker all relevant request/context, acceptance/safety rules, concurrent
   ownership and available tools. Read/build/test/shell commands are allowed within
   scope; task restrictions and actual runtime security still win. Report an actual
   tool denial explicitly, without inventing permission or a blanket no-shell rule.
3. Make focused changes and verify the relevant scope. A known owner/conflict means
   no overlap, waiting/polling or recursive escalation/redelegation. Work elsewhere
   within ownership or hand off once. The same blocker twice without new evidence
   ends the invocation; do not start nested review loops or demand mega-completion.
4. Record the real result at a practical checkpoint. Only one owner coordinates
   shared outputs and handoff appends; direct builds must not race verification.
   Never use queued work, a future check or an interrupted wait as completed proof.

## Runner Contract

| Entry | Scope |
| --- | --- |
| `./verify.ps1` | Offline `-Scope All` by default, or `UI`, `Source`, `Native`, `Harness`. Native supports `-Suite`/`-Selector` and full native artifact capture without deployment. Installed references use `NGU_IDLE_DIR` or `-GameFolder`. |
| `./test.ps1` | Game-facing `-Mode Diagnostics` by default (read-only); Inspect decodes existing fixtures, Prepare captures/scans, confirmed Live runs fixtures, Restore recovers the existing session. No offline gate, `-SkipOfflineChecks` or `-GameFolder`. |

Example offline invocation: `./verify.ps1 -Scope Native -Suite PresetIntegration -Selector '--execution-core'`.
It proves only that selector, not full native or all-scopes acceptance.

`verify.ps1` writes a JSON report and phase logs. Contention, explicit
`-DeferReason '<reason>'`, or source/artifact drift exits 2 and automatically appends
to the SAME `artifacts/agent-handoff.md`; `-HandoffNote '<next steps>'` adds context.
Fail fast, never queue for 30 minutes or build directly over an active verifier.
Quiet source ownership is a prerequisite throughout verification: matching endpoint
fingerprints mean no detected endpoint drift, NOT a coherent snapshot or protection
against uncoordinated edit/revert/build activity. Revalidate actual affected scopes.

Native compiles production plus selected fixture dependencies in one incremental
MSBuild invocation, then runs separate test programs, not one merged assembly.
`checks/verification/Native.ps1` and `Ui.ps1` are internal helpers only. Keep
`checks/Combat/VerifyRegressions.ps1` separate: its behavioral mutation assertions
are distinct coverage, not another wrapper to remove. See [testing](testing.md).

Offline verification does not authorize deployment, restart or live mutation.
Prepare's longer-playtime load and Live require `-ConfirmLiveSaveLoad`; Live also
requires existing fixtures and a compatible deployed backend. Preserve SaveSafety/
Steam/native restore guards and current choices; test PASS covers its selected mode
only. No actual live transaction is certified by the runner/documentation change.
Long soak is separately scheduled by the user, not a blocking coding-invocation
sleep. No commit or release/live acceptance follows from generated evidence.

## One Handoff File

Append to `A:\misc\NGU-Automation\NGUInjector\artifacts\agent-handoff.md` under your
own UTC/session/task section. Include all worker findings needed to continue,
including failed/partial/deferred evidence, not merely links to ephemeral chats.
The shared owner serializes appends; preserve existing sections. If appending is
outside ownership or denied by tools, give that owner the complete entry and
restriction rather than writing another handoff file or pretending it succeeded.

Template/example only; placeholders are not execution evidence:

```markdown
## <UTC timestamp> | <session> | <bounded task>

- Request/scope: <requested outcome, acceptance, owned/excluded files>.
- Changed files/owners: <paths, this worker, shared-output/other owners>.
- Decisions/findings: <result, tradeoffs and all relevant worker/review outputs>.
- Commands: `<exact command>`; exit <code or not run>; scope <scope>.
- First failure: <first actionable failure, or none in the completed scope>.
- Logs/reports: <absolute paths, or none produced>.
- Identity: <source revision plus dirty-file/fingerprint context, or tested artifact hash/MVID>.
- Pending checks: <not run/deferred/inconclusive; never queued-as-PASS>.
- Blocker/next steps: <specific owner/action, or none; no scheduled-work completion claim>.
- Safety: <live permissions/prohibitions, current state, data to preserve, no commit if applicable>.
```

After a real entry is written, say:
`Have the next agent continue from A:\misc\NGU-Automation\NGUInjector\artifacts\agent-handoff.md`.
