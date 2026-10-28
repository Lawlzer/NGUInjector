# Automation Error Reporting

## Implemented

- `Main.LogError` records bounded session exception history as well as the original full log output. Runtime publishes context, reason, diagnostic details, first/last UTC timestamps and repeat count.
- History retains at most 16 recently occurring distinct context/message pairs within 32 KiB of serialized UTF-8, including JSON escaping. It cannot consume the entire command-result envelope. Full exceptions remain in `logs/inject.log`.
- Runtime sections are isolated: a throwing getter reports unavailable/error with its actual reason while unrelated observations and the heartbeat continue. NGU/execution observation catches and preset reconciliation report their exceptions instead of silently returning an ordinary wait.
- Explicit allocation refusals are distinguished from ordinary waiting. The read-only top-of-app panel combines these faults with selection recovery, startup, command, save and read errors on every tab. Stack traces remain in closed Details disclosures. The existing Save Safety warning retains priority.
- Ordinary cooldown/spawn waits and unverified observations alone are not errors. Failed command receipts are history, not proof of an active automation fault; unrelated success does not erase them. A new injector session clears its exception history, not command deduplication.
- Explicitly failed Combat Prep observations are unavailable, never `Planner ready`, and cannot authorize a retained Start preview in the UI/controller.

## Verification

- UI: 2,201 unit tests; Svelte check with zero errors/warnings; frontend and native Tauri release builds passed.
- Browser: 884 passed, four unchanged existing skips, zero failures; desktop/mobile error panel, keyboard Details, all-tab visibility, stale runtime, failure retention and no automatic command replay included.
- Rust: 182 tests and format/compile checks passed.
- Source bridge: 9,864 assertions, including thread-safe repeat counting, detached snapshots, byte-budget/escaping and isolated getter failure/recovery. All source projects in the verification wrapper passed after stale fixture boundaries were migrated.
- Compiled production logger-to-runtime regression is included in the full native preset suite. All 13 native suites passed the captured error-reporting candidate `c1d7d6be-d73a-494a-9b08-08d94f57e62b` before subsequent concurrent live-repair fixes.
- The later deployed combined candidate and its additional native route/capacity fixes are documented in `live-injector-repair-20261024.md`: MVID `9e14378a-60f3-49bc-b86d-b7effeacb7f9`, SHA256 `A6ED76B7CF8F2A31BAE412CBE6BC1713C8879B2424E6975B6F5944400FF2A1FF`, all 13 full native suites passed.

## Independent Live Checks

After waiting for the other repair sessions to finish, sampled the actual restarted game/injector rather than restarting over its verification. Injector session `2026-10-24T06:26:10.1984563-05:00`; original `24hr` plan, auto-rebirth, Energy/Magic and adventure automation enabled.

Three observations separated by 30 seconds passed all required diagnostics with no runtime exceptions. Evidence: `%LOCALAPPDATA%/Temp/opencode/incident-independent-live-baseline-20261024.json`. Optional independent NGU-distribution checks remain inconclusive because authoritative current caps/budgets are not published; these are not claimed as verified ratios.

Actual Time Machine allocation: Energy `167997167` plus basic training `12`, Magic `27125014`, idle Energy/Magic `0`. Native ITOPOD screenshot `ngu-game-20261024-064756-184.png` shows automatic attack, floor 173, defeated enemies and actual PP progress. The UI screenshot `ngu-ui-20261024-064906-858.png` verifies the prominent error panel above navigation, showing an earlier rejected startup `readPlan` receipt while the plan is running. That historical command error is distinct from the empty current-session `automationErrors` array.

The actual one-hour transition follow-up FAILED: `incident-independent-live-plus1h-20261024.json` records the fruit cap-gear cross-lane capacity exception and a retained fight-preparation claim with `Selected route release authority changed`. The initially passing baseline is not sustained acceptance. Native fruit regression reproduced the exception; optional unsafe cap swaps now retain current gear and activate currently affordable fruit, preserving the capacity guard and unmanaged lanes. Focused native fruit checks passed 551 assertions; combined release/deployment remains pending. Another session restarted at `07:54:25` during this work, so this session resumed the user-requested one-hour wait rather than overlapping recovery.

A full daily cycle, later mandatory AT, or final-build titan kill is not proven by these observations. No stage/deadline/AT/death-budget workaround or manual rebirth was used in this session.
