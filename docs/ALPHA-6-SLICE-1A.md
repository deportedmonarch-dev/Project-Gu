# Alpha 6 Slice 1A · Black Boar Gu

Black Boar Gu is a natural Rank 1 Strength Path Gu with a reference value of 600 primeval stones. The reference value has no vanilla silver conversion or trade price. On the integration branch, the refined and stored individual grants permanent Black Boar bodily cultivation. Beast phantom combat remains future work. [Bodily cultivation](GU-BODILY-CULTIVATION.md) · [Roadmap](ROADMAP.md#beast-strength-and-phantom-framework)

## Surface ecology

One population is stored per eligible surface settlement locality, keyed by world layer and tile. Temperate and boreal forests have the highest starting occupancy, tropical rainforest has moderate occupancy, and temperate swamp, tropical swamp, cold bog and arid shrubland have low occupancy. Each locality rolls 0–3 initial adults once. Deserts, tundra and biomes outside the initial compatibility list have no surface source.

An anonymous adult becomes a single identifiable wandering Gu when a colonist comes near eligible ground or orders an environmental search. The same population debit and placement rules apply to both triggers. At most one resident can manifest per locality. A living resident present when its map is removed retains its identity and can reappear on a replacement map with no second debit. At one or zero living wild residents plus cohort adults, natural recruitment activates and adds one mature unit per 3–9 eligible in-game days until the combined count reaches four. Unloaded and unsuitable maps pause recruitment.

Right-click suitable outdoor ground with an undrafted colonist to **Search for wild Gu**. Right-click an exposed Gu to **Capture Black Boar Gu (unrefined)**. Mortals and Gu Masters can capture it. It can evade pursuit and the colonist gives up after a sustained unsuccessful chase. At a base chance of 0.1% per in-game hour, a wild Gu may linger near one colonist for up to four hours, making it easier for that colonist to capture it.

## Feeding

Once in care, Black Boar Gu uses a full ration of 100 raw pork or one pig or wild boar carcass every five days. Either manual feeding or the cultivator's existing automatic provisioning can provide a meal. RimWorld Core uses the same raw pork Def for pigs and wild boar. Corpse meals leave native desiccated remains, with a saved marker that prevents a second meal or butcher products.

The default diet uses the specific pork and carcass Defs. If a meat merger declares non-pig species as sources of the pork Def, that pork option is unavailable. The original carcass Defs remain a feeding option. Mods that rewrite butcher output at runtime without declaring the new sources may hide provenance from that check.

## Artwork

The wild body uses RimWorld's Megascarab art with a dark tint. Dormancy uses the existing Liquor Worm sprite with a dark tint. The owner can supply these transparent PNGs in `Textures/ProjectGU/Gu/BlackBoarGu/`.

| File | Orientation |
| --- | --- |
| `Wild_south.png` | Facing the viewer |
| `Wild_north.png` | Facing away |
| `Wild_east.png` | Facing screen-right, mirrored for west |
| `Dormant.png` | Single still sprite |

The wild and dormant texture paths are declared in `Defs/ThingDefs/GU_BlackBoarGu.xml`. Switch them to `ProjectGU/Gu/BlackBoarGu/Wild` and `ProjectGU/Gu/BlackBoarGu/Dormant` when the art is added, and remove the placeholder tints. The stock corpse graphic may remain until there is dedicated corpse art.

## Verification

Windows, .NET 10 and RimWorld 1.7.5021 rev1365 with Core and Harmony. The native runner uses disposable game copies and historical save copies. `scripts/verify.ps1` passed XML and structural checks, game-linked compilation and the isolated managed suite. Installer checks covered clean install, Alpha 5A upgrade, manifest-aware retirement and repeated install.

- `Slice1A` passed fresh and reload stages in campaign `e6b12c0e337d475e8f552e89c543c3fc`. The fixture exercised natural proximity and deliberate search receipts, both captor types, native wandering, evasion and abandonment, a forced curiosity roll, direct lethal damage, refinement, and the pig and wild boar diets. The manual pork job saved after collecting its first stack and resumed with exactly 100 units consumed. Both carcasses left skeletons and yielded zero butcher products. The matched desert settlement had no surface population, and a regenerated eligible map recovered the same resident without a second debit.
- The recovery fixture checked each production 3–9-day roll, shortened the remaining wait to its final sample, and drove the real scheduler from the threshold to four. The fresh process preserved the ledger, recovery state, receipt history and resident ID.
- `Slice0Wildlife` passed the final-payload controls and fresh-process reload in campaign `4e7cd7c8cd574ed18035b81415528369`. Sixteen registered Gu, including Black Boar Gu, left the native wildlife weight, density, full gate and matched-seed replenishment unchanged.
- `GuGeneration` passed with five protected Gu kinds in campaign `d1c730bd47d542cb9b2e2207e16a4842`. `Alpha5A` passed feeding, mid-job reload, starvation and a legacy-save upgrade in campaign `d06cd55901104609aaf6b5d9dbd1b6f8`.
- `Alpha5B` passed fresh, integration, reload and Alpha 5A save stages on the final payload in campaign `7fcf60f894d54e518f554428d9dd1e31`. The regression exercised spring search and lures, awakening, custody, background sustenance, and underground travel and hauling.

## Owner acceptance

**Accepted with deferred manual checks. No blocking defects.** The owner completed gameplay testing of `67c1a30e12def485002911390f6a476a92211ec8` on RimWorld 1.7.5021 rev1365. The automated verification above remains separate evidence.

### Owner-confirmed mechanics

- Natural Black Boar Gu manifestation, environmental search availability, the existing-locality restriction, and independent wild wandering.
- Successful capture, physical inventory pickup, refinement, and storage of the refined Gu in the aperture.
- Recognition of raw pork and pig corpses, with correct ration descriptions.
- Continued Gu existence after save/load.

### Deferred manual checks

- Successful deliberate discovery and evasive pursuit.
- Inventory drop, repeated pickup, and aperture withdrawal.
- Actual pork and carcass consumption, wild boar corpse recognition, and automatic provisioning.
- Natural population recovery and biome restrictions.
- Exact identity and state comparison after reload.

### Gameplay feedback

**GP-001 · Capture difficulty.** The owner found capture easy and observed no noticeable evasion. This is a nonblocking balance observation. Further capture tuning awaits separate authorization.
