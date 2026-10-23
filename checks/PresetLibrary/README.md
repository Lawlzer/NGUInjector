# Preset Library Checks

Run from the repository root:

```powershell
dotnet run --project checks/PresetLibrary/PresetLibraryChecks.csproj -c Release
```

The standalone harness links production persistence code and embedded catalogs. It uses disposable temporary directories and stubs, not a running game, and performs no game actions.

## Catalog Addition

- The current catalog has 14 objectives and 18 seeds. Balanced retains its original 12 unit weights with two zero cap lanes. Only Energy Cap and Magic Cap use current support; other seeds use planned support. All seeds auto-update.
- Fresh libraries persist the injector-owned root boolean `CapPresetsAdded: true` with the seed write.
- Unmarked libraries containing a recognized catalog ID receive only missing `builtin_energyCap` and `builtin_magicCap` rows and the marker in one atomic write. Recognition includes retired `builtin_pp`, `builtin_boosts`, and `builtin_zone` IDs; an arbitrary `builtin_` prefix is insufficient.
- Existing rows, IDs, edits, receipts, and unknown data are preserved. Existing cap IDs are never replaced. Once marked, deleted caps are never restored, even if other built-ins remain or all presets are deleted.
- Unmarked empty and custom-only libraries are intentionally ineligible and remain read-only on initialization. This preserves the existing lossless/read-only contract rather than turning every historical custom library into a seeded catalog. The historical farming fixture deliberately now expects only the two cap additions, not replacement farming seeds.
- `SaveExternalLibrary` preserves the persisted marker, like `MigrationReceipts`, regardless of omission or replacement in an external snapshot. Clients cannot forge a marker on an ineligible library.
- Count (1024) and serialized UTF-8 size (2 MiB) limits include additions and the marker. If an addition cannot fit, initialization fails without writing partial rows or a marker. It can retry after capacity is freed. Windows replacement-failure checks also verify atomicity and retry.

## Receipt Compatibility

- Current ratio-free definitions normalize to 14 weights. Before creating an import, lookup also reconstructs historical ratio-free 12-weight hashes, ratio-bearing 12-weight hashes, and eligible 10-weight/horizon hashes.
- Actual support-mode identities take precedence. Implicit planned support can match historical current-support identities; explicit choices cannot inherit an opposite-mode identity or tombstone.
- A match preserves the exact old receipt and adds a 14-weight ratio-free alias pointing to the same ID. Deleted imported presets remain tombstones, not candidates for recreation.
- Historical fixtures are explicit JSON, independent of the current serializer. The matrix covers 6/10/12 source weights, horizon defaults and numeric spellings, implicit/explicit support choices, edited rows, deletions, alias preservation, saves, restarts, and provenance stripping. Isolation checks reject unrelated old identities and keep current validation authoritative.
- Focused ratio-free 12-lane cases cover nonzero adventure lanes, dynamic blends, malformed retired ratios, and positive cap definitions that must not alias old identities or tombstones.

The suite also covers CAS conflicts, unknown numeric-token preservation, legacy inline stage semantics, reference protection, inherited selections, dynamic adventure persistence, global NGU settings, and atomic migration rollback.
