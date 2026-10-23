# Money Pit Preparation

Automation previews the next reward using the native tier boundaries and Unity RNG seeded from `character.pit.pitState`. It restores the caller's Unity RNG state in `finally` and never advances the saved Pit state during preview. Preview runs synchronously on the game thread, after digger upgrades have spent gold, and is rechecked immediately before feeding. The preview is written to the Pit/spin log.

Only an equipped-item leveling reward triggers temporary equipment preparation. Random-item rewards preview the exact target slot(s), including the cosmetic RNG draw between the two hits in tier 4. Native random rewards do not reach the second weapon or accessories beyond the first five; out-of-range accessory rolls target the head. All-equipped rewards include all unlocked equipment slots.

Eligible items are owned, equippable instances below level 100, including previously maxxed item IDs. Selection is by slot type, with distinct IDs:

1. AP-shop hearts (explicit IDs, not any item with "heart" in its name).
2. Forest Pendant and Looty transformation families.
3. Highest source adventure zone, then minimum titan version within that zone.

Within equal priority, higher-level copies come first to finish them; equipped copies win remaining ties to avoid unnecessary swaps. Progression uses a native-source table, not raw item IDs or the timed rebirth-plan stage. Daycare gear is not moved. Other reward types leave equipment alone.

Equipment preparation honors inventory dragging and loadout locks. A failed preparation defers feeding. Exact original equipment instances and slots are restored in `finally`, including when feeding throws; snapshot epoch/token checks avoid restoring into a different save or replacing another owner's snapshot. Blood/digger preparation happens after the equipment swap so gear resource recalls cannot undo it.

When the cumulative Looty milestone is pending, preparation reserves the extra inventory staging space needed to restore same-ID copies or equipment permutations after that loot arrives. If restoration still fails, its snapshot and lock are retained and restoration is retried before the next readiness check or feed, rather than silently losing the original loadout. The main automation loop services recovery even if Auto Money Pit is switched off; unload waits for owned gear recovery instead of losing the snapshot.

Offline checks exercise deterministic RNG boundaries rather than Unity's native PRNG implementation. Successful live Unity swaps and engine RNG behavior still require in-game verification.
