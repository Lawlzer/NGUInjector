# Adventure Zone Reference

`NGUInjector/Data/AdventureZones.json` is a sourced, offline reference snapshot retrieved **2026-10-16**. It is not loaded by the injector, does not replace `defaultZoneStats.json` or `zoneOverride.json`, and is not a combat safety oracle. No runtime code, UI, project configuration or existing data was changed for this reference.

## Coverage

- All **32 regular combat zones**, from Tutorial Zone (game ID `0`) through The Aethereal Sea (`43`).
- The noncombat Safe Zone (`-1`) is separate.
- All **14 titans** are indexed separately by ID, name, difficulty and unlock boss. Their combat, autokill, version, quest and loot rules are deliberately excluded.
- Boss encounter fractions, normal/boss enemy rosters, source-listed manual/idle/one-hit recommendations, boost sizes/base probabilities/caps, main equipment groups and selected other drops are included for regular zones.
- Eight actual enemy stat records: all four Tutorial enemies, Brown Slime, Gorgon, Sundae and Doctor Wahwee. These are **Adventure-tab** values, not the vastly larger Boss Fight stats shown on the same pages.
- Enemy stats for the remaining roster are **not collected**. Most zone pages provide rosters, not numeric enemy stats. The Aethereal Sea's roster names are not linked to individual enemy pages; a direct `THE_CAPTAIN` lookup returned 404. A direct `Chad` lookup also returned 404.
- Gold, a complete bestiary, exact damage mechanics, full special-drop rules and ITOPOD floors are outside this snapshot. Do not interpret omissions as absence of a drop or enemy ability.

There are 47 indexed locations in total: 32 regular combat zones + one safe zone + 14 titans. In this index `wikiOrdinal = zoneId + 2`. The wiki starts its numbering with the Safe Zone, whereas injector ID `0` is Tutorial. Do not feed wiki ordinals into zone selectors.

## Sources

Primary requested page: [Adventure Mode](https://ngu-idle.fandom.com/wiki/Adventure_Mode). Fandom was accessible; no mirror was required. Its `?action=render` view removes navigation and was used for focused transcription of the overview and zone pages. Every regular-zone record includes its canonical `sourceUrl`; append `?action=render` to reproduce the fetched view. The top-level retrieval date applies to all source records, including local source inspection. Revision IDs were not captured.

Supporting pages: [Titans](https://ngu-idle.fandom.com/wiki/Titans), [Skills](https://ngu-idle.fandom.com/wiki/Skills), and [Safe Zone: Awakening Site](https://ngu-idle.fandom.com/wiki/Safe_Zone:_Awakening_Site). Every regular-zone subpage was fetched individually, including late-game stub pages. These are **community-maintained sources**, not an official game-data dump or independent game testing.

Actual enemy stats were read from the Adventure section of [A Small Piece of Fluff](https://ngu-idle.fandom.com/wiki/A_Small_Piece_of_Fluff), [Floating Sewage](https://ngu-idle.fandom.com/wiki/Floating_Sewage), [A Stick?](https://ngu-idle.fandom.com/wiki/A_Stick%3F), [A Small Mouse](https://ngu-idle.fandom.com/wiki/A_SMALL_MOUSE_(BOSS)), [Brown Slime](https://ngu-idle.fandom.com/wiki/Brown_Slime), [Gorgon](https://ngu-idle.fandom.com/wiki/Gorgon), [Sundae](https://ngu-idle.fandom.com/wiki/Sundae) and [Doctor Wahwee](https://ngu-idle.fandom.com/wiki/Doctor_Wahwee). A bestiary Type of `normal` is an ability classification; it does not imply the enemy is not a zone boss.

Local files inspected, not changed:

- `NGUInjector/Managers/ZoneStatHelper.cs`: ID/name mapping and existing manual/idle/one-hit recommendations.
- `NGUInjector/Managers/BoostFarmHelper.cs`: regular-zone boost table, encounter ratios and a **different model** of one-hit power. Its comment attributes the table to Farmer Sanc's Boost Almanac, NGU 1.220; the original spreadsheet was not retrieved or independently verified.
- `NGUInjector/Managers/ZoneHelpers.cs`: safe/titan ID mapping, including endgame titans `44` and `45`. Its effective boss numbers are not the difficulty-relative wiki boss numbers stored here.

Local constants were reused only where their meaning was verified: IDs were cross-checked against wiki ordering, boost values/odds against individual zone loot sections, and encounter ratios against wiki boss fractions. Similar-looking constants are not treated as independent confirmation. No values were inferred from compiled game assemblies or live game state.

## Reading The JSON

- `schemaVersion` is `1`. `zones`, `safeZones` and `titans` are separate collections. Regular-zone records use the same key structure.
- Numbers are unscaled stat points; `1e27` means one octillion points, not a display suffix. Probabilities are **fractions**, so `0.15` means 15%, and `1e-8` means 0.000001%.
- `recommendations` contains **player recommendations**, not enemy maxima or verified survival thresholds. The individual zone page's Fight table is the chosen source. Overview/local conflicts are retained in `notes` rather than silently merged.
- `recommendations.idle[].beastMode` distinguishes `false` (OFF), `true` (ON), and `null` (source does not distinguish). West World has two OFF recommendations, preserving the page's alternate recommendation.
- Player `hp` is `null` throughout because the retrieved regular-zone recommendation tables do not specify it. One-hit toughness is also unspecified. Unknown does not mean zero or irrelevant.
- `enemyStats` contains only the eight separately sourced numeric enemy records. An empty array means not collected, **not** no enemies. `enemies` is the roster and does not imply the corresponding stats are known.
- `bossChance` is the source's encounter fraction, not a ratio inferred from distinct enemy names. Sundae, Doctor Wahwee and The Elusive C.S have double encounter weight.
- `drops.boosts` lists chances **conditional on killing a normal enemy**. Caps not stated on early zone pages are `null`; the local helper's assumed 100% cap was not promoted to a wiki fact. Tutorial boosts additionally require the whole Training set unlocked.
- Late zones list two separate Boost 10000 entries. Both are retained. Do not deduplicate or assume a combined single-roll probability. Independence is not verified; sums can describe expected count, not probability of at least one success.
- `drops.equipment` describes a selected group. Its probability is for the group, not every item. `selection: "one_of"` does not establish equal item weights. `baseLevelExceptions` handles mixed levels in shared rolls. `normalChance: null` is not proof that normal enemies cannot drop gear.
- `drops.other` preserves selected source descriptions, conditions and percentage text. It is not an executable loot model. MacGuffin unlocks, quest status, special ingredients, guaranteed first drops and completed sets must not be ignored.
- Sources and field interpretation are also recorded in the JSON itself. Fields without a separate source inherit the zone's `sourceUrl`; ID mapping is attributed to the local source registry. All enemy records have their own source URL.

## Formula Interpretation

The [overview](https://ngu-idle.fandom.com/wiki/Adventure_Mode#Equipment_and_Loot) states that Chocolate World and later use the cube root of the player's loot bonus. Let `D = displayed total drop-chance percent / 100`, with baseline 100% giving `D = 1`. For a listed boost row with known cap:

```text
Before Chocolate World (zoneId < 20): p = min(cap, base * D)
Chocolate World onward (zoneId >= 20): p = min(cap, base * D^(1/3))
```

This is an interpretation cross-checked against `BoostFarmHelper`, not an assertion that every special drop obeys it. Do **not** take the cube root of the listed base probability. Additive pendant chances in Chocolate/Evilverse/Princess pages were retained as source text rather than forced into this model. Additional charm effects, caps, filters, quest gates and player state are not simulated here.

For eligible boost rows, expected boosts per arbitrary zone kill can be expressed as `(1 - bossFraction) * sum(p)`. Linearity of expectation does not require independent rolls. To estimate time per drop, one also needs actual eligible encounter weights, kill time, respawn, and the particular drop's rules; a zone recommendation alone is insufficient.

The [Skills page](https://ngu-idle.fandom.com/wiki/Skills) lists idle damage multipliers of `1.2`, `1.5` with the Spoopy set, and `1.8` with the additional Sadistic No Equipment challenge reward. Beast Mode increases power by 40% (50% with the Purple Liquid set) while making received damage 300%. The wiki does not consistently annotate its one-hit recommendations with all these conditions. No exact damage formula, enemy HP, full-zone maxima or survival guarantee is derived from them. Ancient Battlefield explicitly lists separate one-hit recommendations with and without the Spoopy bonus.

## Conflicts And Gaps

The reference intentionally prefers the **individual zone page**, not a presumed newest or safest number. This is a transcription policy, not evidence that the selected recommendation is more accurate. Representative conflicts below are also recorded in zone `notes`.

| Zone | Individual Zone Page | Overview / Local Difference |
| --- | --- | --- |
| The Sky | Manual toughness 400 is marked unconfirmed | Local value has no uncertainty flag |
| Clock Dimension | Idle 3850 / 3600 | Overview and local: 4500 / 3000 |
| The 2D Universe | Idle 7500 / 6000; one-hit 168233 | Overview/local: 8000 / 6000; 168223 |
| Ancient Battlefield | One-hit 422550 without, 282966 with Spoopy bonus | Local ZoneStatHelper keeps only 282966 without condition; BoostFarmHelper model uses 353000 |
| A Very Strange Place | One-hit 842490; fragment called SEXY | Overview/local one-hit 842483; overview fragment called Energy Beard |
| Mega Lands | Idle power 190000; one-hit 3.53e6 | Overview idle power 265000, one-hit 3.54e6; local one-hit 3.5e6 |
| The Beardverse | Manual 1.3e6 / 850000; idle 2.5e6 / 1.7e6 | Overview manual toughness 550000 and conflicting idle power text; local idle 3e6 / 2.2e6 |
| Badly Drawn World | Manual 1.8e7 / 1.1e7; idle toughness 3.5e7 | Local manual 2.5e7 / 1.5e7; overview's parenthetical idle toughness says 3.7e7 |
| Chocolate World | Idle OFF 1.4e11 / 8e10 | Overview/local 1.5e11 / 9e10 |
| Pretty Pink Princess Land | Idle OFF 1.15e14 / 7e13 | Overview/local 1.3e14 / 9.7e13 |
| Meta Land | Manual 2.5e16 / 1.5e16; idle OFF toughness 3.5e16 | Overview/local manual 2.6e16 / 1.2e16; idle toughness 3.1e16 |
| Interdimensional Party | Manual toughness 1.5e17; idle OFF 5e17 / 3.5e17; ON power 1.1e18 | Overview/local manual toughness 1.1e17 and idle OFF 4.8e17 / 3.1e17; overview ON power 1.2e18 |
| Typo Zonw | Idle OFF power 3.7e20; boost caps 8% | Overview/local idle power 2.7e20; local boost caps 9% |
| The Fad-lands | Boost caps 8% | Local boost caps 10% |
| JRPGVille | One-hit 1.8858e23 | Local ZoneStatHelper one-hit 7.2e23; manual/idle also differ |
| The Rad-Lands | One-hit 1.8857e26 | Local ZoneStatHelper one-hit 1.04e25; idle also differs |
| Back To School | One-hit 5.239e28 | Local ZoneStatHelper one-hit 1.76e27; idle also differs |
| The West World | Two OFF recommendations; one-hit 2.2216e29 | Local ZoneStatHelper one-hit 9.2e27; manual/idle also differ |
| The Breadverse | One-hit listed as `-` | Kept null; local one-hit 4.88e29 is not wiki-verified |
| That 70's Zone | One-hit 1.36e31 | Local ZoneStatHelper 1.518e30; manual/idle also differ |
| The Halloweenies | One-hit 4.2e31 | Local ZoneStatHelper 4.66e30; manual/idle also differ |
| Construction Zone | `1.42 Dec (1.42E+32)` | Unit/exponent conflict left unresolved as null; local 1.6e32 |
| DUCK DUCK ZONE | One-hit `?` | Kept null; local 1e33 is not wiki-verified; manual/idle also differ |
| The Nether Regions | One-hit `?` | Kept null; local 1.12e33 is not wiki-verified; idle also differs |
| The Aethereal Sea | One-hit 5.75e35 | No ZoneStatHelper default; BoostFarmHelper model uses 8.77e35 |

Where a headline and parenthetical scientific notation differ only by rounding, the more precise headline is retained, such as `889.08 M = 8.8908e8`, `10.47 Qi = 1.047e19`, or `17.2 Dc = 1.72e34`. Construction's unit/exponent disagreement is not merely rounding, so it is not silently repaired.

`BoostFarmHelper.OneHitPower` and `ZoneStatHelper.OPower` are different datasets with different attack-modifier contexts. Even Tutorial uses `109` in the former and `129.5` in the latter. Neither is an enemy HP field. They were **not merged** into `enemyStats`. The local boost encounter ratios are rounded in several places; this reference retains exact wiki fractions (`7/9`, `13/16`, `10/13`, `17/21`, etc. after complementing the boss fraction) instead.

All recommended player HP fields are unknown. Enemy stats are partial and no strongest-enemy aggregate is provided. Four zones have missing/unresolved one-hit power. Early boost caps, individual gear-selection weights, duplicate roll independence, exact damage mechanics and several Aethereal Sea special-drop probabilities remain unknown. Some late-game pages explicitly identify themselves as stubs. The JSON's `missingFields` and `excluded` lists are part of the reference contract, not implementation TODOs.

## Validation

Validation passed using read-only Node assertions: JSON parsing, consistent regular-zone keys, unique IDs, the complete `-1..45` 47-location partition, wiki ordinal mapping, source URLs, eight enemy-stat records, positive known stats, probability bounds, base/cap ordering, equipment level exceptions, duplicate endgame boost rows, and retained null HP/one-hit gaps. Coverage is 15 normal, eight evil and nine sadistic regular zones, with 283 roster entries and 62 listed boost rows. Ten early-zone boost rows have unspecified caps.

The local-source cross-check passed for all 32 BoostFarmHelper zone IDs, all 62 boost values/base probabilities, normal-enemy encounter fractions within the helper's four-decimal rounding, all 31 ZoneStatHelper zone IDs, and all 14 titan IDs. Exactly four cap comparisons differ, the two boost rows each in Typo Zonw and The Fad-lands, as documented above. `git diff --no-index --check` against the two new files found no whitespace errors. Existing files were not edited.

No build is needed for a standalone reference file; the C# project was not modified to embed or consume it. No live-game automation, combat verification, probability simulation, commits, or independent validation of the original Almanac spreadsheet was performed. Any future runtime consumer must explicitly handle nulls, uncertainty, conditions and source conflicts instead of silently applying these recommendations as hard requirements.
