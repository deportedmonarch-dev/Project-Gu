# Alpha 6 Slice 1B-1 · White Boar Gu

White Boar Gu and Black Boar Gu share the Rank 1 rules. Each has its own wild population, exact registered Gu identities, cultivation technique, and bodily attainment. Both have a reference value of 600 primeval stones.

## Shared rules

The surface forest rules give each species up to three initial adults, rolled from the same biome suitability table using a seed that includes the species. At one or fewer residents plus unmanifested adults, the cohort starts a recovery cycle. Each replacement takes 3–9 eligible days, continuing until that species' population reaches four.

The shared diet is 100 raw pork or one whole pig or wild boar corpse per five days. After nourishment runs out, withering begins at two days and starvation becomes terminal at ten. Refinement, aperture storage, feeding and the bodily channel use the existing workers.

Each new cultivation session commits a one-day cooldown and costs no essence upfront. Each of its ten pulses takes 500 ticks and pays one essence on success. Each attainment grows by one step out of 150 and caps independently. Full development of either strength gives +40% melee damage, +50% carrying capacity, +20% mining speed and +15% construction speed. The Cultivation page shows each attained strength with its own progress and current bonuses below it.

## Ambient surface ecology

Total wild population is the sum of unmanifested cohort adults and living registered residents. Each eligible loaded locality can have one physical resident per species, so Black Boar and White Boar can wander at the same time while additional adults remain in their separate cohorts. A living Gu retained at its world site after map removal still counts. On map regeneration it can resume wandering under the same ID and debit. Capture or death removes it from wild occupancy.

The ecology runs on the loaded game's tick after native map generation has set up terrain and fog. It makes one bounded placement attempt every 2,500 ticks when a species has stock and a vacant resident slot or has a world-site resident to restore. It samples up to 128 cells across the settlement for unfogged outdoor natural terrain in a suitable biome. Registry placement also requires an unreserved, standable cell without a building, item or other pawn. Manifestation needs no colonist, produces no discovery announcement and has no camera effect. New source receipts use `surface-ambient`. Prior receipts retain their original provenance, event keys and debit history. The registry developer spawn action remains available.

**Search for wild Gu** checks for a reachable, unfogged, registered wild Pawn within six cells of the chosen ground. Success in a player-ordered job centers the RimWorld camera on the Pawn's actual cell and highlights the moving target for three seconds. Unsuccessful player orders report that no signs were found. Detection only reads the live map, so repeated and autonomous searches leave the habitat and resident unchanged. Autonomous searches have no camera or highlight effect.

Slice 3 still needs a policy for how long common residents stay physical and when they can return to the cohort. The current cap leaves surplus adults unavailable to search while the slot is occupied. Placement uses RimWorld's unfogged-cell safety check and retries later when its sample finds no suitable space. Searching is a short-radius detection job. Concealment, ecological clues and more advanced investigation are later work.

### Ambient correction verification

`scripts/verify.ps1` passed structural checks, game-linked compilation and the managed suite. The native campaigns ran in isolated read-only RimWorld 1.7.5021 rev1365 copies with Core and Harmony. Their 111 production file hashes match.

| Native coverage | Passing campaign |
| --- | --- |
| `Slice1A` autonomous placement, search, Black Boar capture, recruitment and fresh-process reload | `b4a5f15e8d224224b018353d8f82f3ae` |
| `WhiteBoar` independent stock and debits, capture, cultivation, attainment and fresh-process reload | `12654bb19abc46d4b964c0e62ce6a6b2` |
| `Bodily` Black Boar channel, interrupted-session persistence and bodily attainment | `f7730b3fc8cf4532938aac6695346658` |
| `Slice3B5B` source faults and custody, `Slice0Wildlife` matched native wildlife, `Alpha3` Liquor Worm and `Alpha4A` aperture | `b515e973f0274da6b75c99b4bb528bbe` |
| `Alpha2` Moonblade and in-flight fresh-process reload | `3cf02a0a85814603877ebd5d3dbbf6bb` |

The surface fixture let both Boar species appear and wander on a generated settlement with no colonists, checked their placement cells and separate debits, and verified that the camera and discovery messages stayed quiet. The search job found the existing Black Boar at its exact position, submitted its native highlight, and preserved its position, registry count and cohort revision. An out-of-radius search reported failure. An autonomous job left the camera where it was. The fixture also checked depletion while White Boar remained, and restoration of the original resident after map regeneration. Recovery checks shortened an already scheduled interval in the disposable fixture, then used the native tick path to verify one replacement at a time through capacity four. Camera position and highlight submission were checked headlessly.

### Owner acceptance of the ambient correction

The owner accepted autonomous surface manifestation and detection-only environmental search at `236cb67` on RimWorld 1.7.5021 rev1365 within the scope tested. The owner did not report case-by-case results. Slice 3 still owns the fogged-terrain policy, physical resident limits and transitions between physical residents and abstract cohorts.

## Framework corrections

Surface ecology previously assumed every locality, transaction, resident and pursuit belonged to Black Boar. A species Def now opts into the surface forest rules. Each locality retains one registered source identity with separate species cohorts, recovery clocks and resident checks. The registry receipt format already scoped debits by species. Surface evidence now uses the cohort's species, and discovery, pursuit and curiosity messages use the registered species or projection label.

Black Boar's source IDs, initialization seed, receipt keys and cohort values retain their existing meanings. The loader also reads the previous single-cohort entries and adopts them by species. Current-format saves write the per-locality cohort collection.

White Boar has independent art paths under `Textures/ProjectGU/Gu/WhiteBoarGu` and its own cultivation icon. The dormant art and icon recolor existing Project GU placeholders. The temporary wild and dessicated sprites use original pale beetle artwork.

## Future refinement routes

- Black Boar Gu and Green Silk Gu lead toward Black Mane Gu, followed by Steel Mane Gu.
- White Boar Gu and Jade Skin Gu lead toward White Jade Gu, followed by Heavenly Mugwort Gu.

These routes are future progression references. Acquired bodily strength belongs to the pawn and persists through withdrawal, transfer, loss, ordinary medical treatment and eventual refinement of the original Gu into a successor.

## Native stat behavior

RimWorld multiplies the two hediff factors. Same-tick native measurements on one pawn held skills and capacities fixed while comparing zero, one and two full attainments.

| Stat | Neither | Black Boar only | Both | Combined factor |
| --- | ---: | ---: | ---: | ---: |
| Melee damage factor | 1.00 | 1.40 | 1.96 | ×1.96 |
| Carrying capacity | 75 | 112.5 | 168.75 | ×2.25 |
| Mining speed | 80% | 96% | 115.2% | ×1.44 |
| Construction speed | 80% | 92% | 105.8% | ×1.3225 |

Native stat limits still apply. A separate run observed the 10% Mining Speed floor with and without the second attainment on a level-zero miner.

## Verification

Structural checks, game-linked compilation and the managed suite passed via `scripts/verify.ps1` on Windows with .NET 10 and RimWorld 1.7.5021 rev1365. Native campaigns used disposable read-only game copies with Core and Harmony. The three campaign manifests match across 111 production files by SHA-256.

| Native coverage | Campaign |
| --- | --- |
| `WhiteBoar` and `Alpha4A` | `dff7e70da2854de0b87a500eda1e9864` |
| `Alpha2` Moonblade | `cb4833e8b5884a8eb600b20d37a054db` |
| `Slice1A` Black Boar, `Alpha3` Liquor Worm, `Slice3B5B` aperture custody and `Slice0Wildlife` | `11fd8a4c50fe4634b318eaa0780e1dfa` |

The focused fixture exercises co-residence, separate cohort debits and recovery, exact identities, native capture and pickup, refinement, aperture storage and feeding. It runs a ten-pulse session through drafting and resumption, advances the fixture to just before the cap, and runs the last paid pulse. It checks both caps, current-format fresh-process persistence and retention after transfer, loss and native medicine. Native regression assertions that count cavern Gu exclude ambient surface receipts. The dedicated wildlife profile still includes both Boar kinds.

Rendered inspection and a natural 15-session White Boar playthrough remain for manual testing. The legacy single-cohort loader path was outside this verification campaign.

## Owner playtest

The owner completed and accepted White Boar gameplay testing at baseline `e11b7d6` on RimWorld 1.7.5021 rev1365 within the scope tested. The steps below are the original playtest guide. The owner did not report case-by-case results.

1. In an eligible forest settlement, explore with a colonist or right-click reachable natural ground and choose **Search for wild Gu**. Capture a White Boar, pick it up, enforce its will and store it in a Gu Master's aperture.
2. Feed it once its nourishment runs low. Use the Cultivation page to start **White Boar cultivation**, draft mid-session, then undraft and resume it. Compare essence and progress before the first pulse and after it.
3. On a pawn with Black Boar strength, check that **Bodily attributes** lists both strengths with their own progress and bonuses. Save, quit to desktop and reopen. Withdraw White Boar and verify that both entries and bonuses remain.
