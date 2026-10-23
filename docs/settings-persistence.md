# Settings Persistence

## Contract

- `get_ngui_state` returns `settings_json` (a JSON string) and `settings_revision`
  derived from the same file bytes. The revision is lowercase SHA-256. Only actual
  absence produces `{}` with revision `""`; malformed, locked, oversized and
  unreadable files are errors, not fresh settings.
- `save_settings_patch_json` takes `runtimeDir`, `patchJson` and a required
  `expectedRevision`. Connected transport forwards that exact token in
  `applySettingsPatch { patch, expectedRevision }`. There is no production
  revision-less save entry point. The old Rust helper name exists only in tests,
  where it explicitly reads a fresh revision before submitting a single edit.
- Connected writes require actual `runtime.settingsCasVersion == 1` before
  queueing. An older injector cannot silently ignore the new revision argument
  and perform an unguarded write; absence is not a compatibility fallback.
- Revisions are transport metadata, not serialized settings or execution tokens.
  Settings CAS does not acquire execution ownership, rebind an acquired selection,
  or renew injector/process identity. Existing readiness, command expiry, claims,
  execution admission and injector-lifetime ownership remain authoritative.
- Success returns the committed settings object and revision in `data` (offline)
  or `result.data` (connected). The Tauri save result is a JSON string so unknown
  numeric tokens do not cross JavaScript object IPC. A heartbeat, completion time,
  or generic success flag is not a settings commit receipt.

## Writer Boundaries

Offline comparison, merge and atomic write hold the existing UI writer lock and
`.injector.lock` ownership. Connected comparison and commit run on the Unity
command thread under `PresetLibraryStore.SyncRoot`, with the injector's existing
lifetime lease. C# requires both the submitted revision and its loaded/committed
baseline to match current disk bytes. It never reloads changed disk as part of
accepting a patch. Native/internal writes do not require an external token, but
must match that same disk baseline. NGU migration also checks the original disk
revision before writing.

Persistent property setters construct a candidate and persist before publishing
live fields or scheduler invalidation. Persistence failure retains their prior
live value, permitting a same-value retry. Iron Pill history follows the same
commit path without refreshing the native form; retained history rows preserve
unknown source fields/numeric tokens. Exact Int32 token checks reject fractions
and underflow that a double would round to a valid integer.

This is cooperating-writer CAS, not an OS transaction against arbitrary editors
that ignore the ownership locks. Such editors can still race the final atomic
replacement. The single Unity thread and lifetime lease are part of the guarantee.
Plan and settings files are not one atomic transaction: the existing explicit
"plan saved, settings synchronization failed" result and non-replaying retry
behavior remain in place.

## Native Audit

The failure/retry audit covers all **114 public persistent setters**. The three
intentional memory-only setters are `QuickLoadout`, `QuickDiggers`, and
`DebugAllocation`. Existing startup and WinForms `SetSaveDisabled` batches remain
intentional unsaved state; they are not retrofitted with guessed rollback of their
side effects. `MassUpdate` outside settings validation/loading is used by startup.

External patches refuse any serialized live state differing from the committed
baseline, including exposed-array in-place edits and a failed native batch. The
native caller must explicitly commit its pending values, or explicitly reload to
discard them. Even an unchanged-byte explicit reload discards dirty live values.
Existing array callers were checked: titan completion and native form edits copy
arrays before assigning the persistent property. Public array getter semantics
were not changed. No execution, route, native-boundary, or browser fixtures were
modified for this audit.

## Frontend

Bulk, inline and plan-control settings writes carry a fresh revision. Useful
field-level conflict checks remain for editor drafts; backend CAS closes the
cooperating-writer gap after that read. Folder/session changes and stale connected
reads retain drafts. Only the committed snapshot advances saved baselines, while
newer per-field drafts survive in-flight writes. Inline completion preserves newer
same-session runtime telemetry. Failed/unknown writes do not automatically replay,
including when another draft arrived in flight. Existing claimed command handling
prevents replay if a connected result is lost.

## Shared Fixtures

`checks/settings-contract.json` is consumed directly by Rust and as an embedded
resource by the C# ExternalUi harness. Its 13 patch cases cover acceptance,
rejection, backend-owned history, shared missing-field defaults, and exact unknown
integers/decimals/exponents at the root and inside retained history. Expected
values are not separately re-specified in the two test implementations.

Regression coverage additionally exercises stale connected/offline edits, a native
change after a read, stale loaded state despite a fresh disk token, failed writes
and same-value retry, absence/corruption/unreadability, concurrent creation, exact
transport tokens, session changes, newer drafts, and lost results without replay.

Verification commands (temporary files and stubbed game boundaries only):

```powershell
npm test -- --no-color
npm run check
cargo fmt --check
cargo test
cargo check
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj -c Release
dotnet run --project checks/ExternalUi/ExternalUiChecks.csproj -c Release -- --settings-cas
dotnet run --project checks/PresetLibrary/PresetLibraryChecks.csproj -c Release
```

The stale connected and offline overwrite regressions were observed failing before
the implementation and pass with revision enforcement. Final scoped verification:
2,072 frontend tests, typecheck with zero errors/warnings, 179 Rust tests, 9,824
ExternalUi assertions, 825 settings-CAS/setter assertions, and 4,749 PresetLibrary
assertions. C# checks substitute Unity/game boundaries; no live game action was run.
Full-tree `cargo fmt --check` still reports the separately owned execution
rebind/restart handlers in `main.rs` and `execution-rebind-tests.rs`. Those regions
are deliberately untouched; settings/persistence test files were formatted.
