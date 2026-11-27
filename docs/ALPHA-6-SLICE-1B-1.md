# Alpha 6 Slice 1B-1 · White Boar Gu

White Boar Gu and Black Boar Gu share the Rank 1 rules. Each has its own wild population, exact registered Gu identities, cultivation technique, and bodily attainment. Both have a reference value of 600 primeval stones.

## Shared rules

The surface forest rules give each species up to three initial adults, rolled from the same biome suitability table using a seed that includes the species. At one or fewer residents plus unmanifested adults, the cohort starts a recovery cycle. Each replacement takes 3–9 eligible days, continuing until the population reaches four. A locality can have one active resident of each species. Proximity and **Search for wild Gu** use the same placement and reachability rules for both.

The shared diet is 100 raw pork or one whole pig or wild boar corpse per five days. After nourishment runs out, withering begins at two days and starvation becomes terminal at ten. Refinement, aperture storage, feeding and the bodily channel use the existing workers.

Each new cultivation session commits a one-day cooldown and costs no essence upfront. Each of its ten pulses takes 500 ticks and pays one essence on success. Each attainment grows by one step out of 150 and caps independently. Full development of either strength gives +40% melee damage, +50% carrying capacity, +20% mining speed and +15% construction speed. The Cultivation page shows each attained strength with its own progress and current bonuses below it.

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

1. In an eligible forest settlement, explore with a colonist or right-click reachable natural ground and choose **Search for wild Gu**. Capture a White Boar, pick it up, enforce its will and store it in a Gu Master's aperture.
2. Feed it once its nourishment runs low. Use the Cultivation page to start **White Boar cultivation**, draft mid-session, then undraft and resume it. Compare essence and progress before the first pulse and after it.
3. On a pawn with Black Boar strength, check that **Bodily attributes** lists both strengths with their own progress and bonuses. Save, quit to desktop and reopen. Withdraw White Boar and verify that both entries and bonuses remain.
