# Alpha 6 Slice 1B-2 · Stone Skin Gu

Stone Skin is a Rank 1 defensive Gu with a reference value of 200 primeval stones. The Defs give it independent wild and dormant projections, a grey stone-like shell, slow ground movement and the shared evasive pursuit. Natural formation and abstract recruitment account for its wild stock.

## Ecology and feeding

The shared surface ecology selects suitability per species. Small hills have higher starting odds for Stone Skin than flat forests containing natural rock. Large hills and mountains have the highest odds. Manifestation samples open natural ground near an exposed, unsmoothed natural stone face within five cells. Stone Skin and both Boar species can each have one resident in the same loaded settlement. Each rolls 0–3 initial adults. When its own cohort plus residents falls to one or fewer, recovery adds one adult every 3–9 eligible days until the combined population reaches four.

Surface search reads registered wild projections near the chosen ground. A successful player order highlights the same roaming Gu. Capture, pickup, will-enforcement and aperture storage preserve the Gu's identity and its source debit. Slice 3 owns fogged habitat, physical resident limits and abstraction of physical residents back into their cohorts.

One sandstone, granite, limestone, slate or marble chunk feeds Stone Skin for five in-game days. The existing manual feed job, automatic provisioning and starvation paths use the species Def's list of five natural chunk ThingDefs.

## Sustained effects

The sustained cast worker uses the existing grant, cast commitment and exact-stock essence transaction. It immediately adds a native whole-body condition without ordering a job. The condition saves the caster ID, Gu ID and custody revision, exact native aperture reference, ability, activation ID and paid-through deadline. It revalidates after both the registry and the cultivator store are ready on load. Its native stages supply the armor offsets. The combat ledger indexes only these conditions during play. At the start of the normal tick list, upkeep validates the original grant and pays before native pawns and projectiles can act at the new interval. Ordinary casting and channels retain their own continuation paths.

Stone Skin costs 2 primeval essence upfront for the first 250 ticks and 1 for each following interval. Each new activation pays the full initial cost. The condition applies +0.30 sharp and +0.25 blunt armor through RimWorld's native armor and penetration rules. Heat armor and movement speed are unaffected. Deactivation, insufficient upkeep, incapacitation, death and loss of the granting Gu or aperture end the effect. The effect can be toggled while drafted or undrafted. The condition appears in Health while active, and the cosmetic layer follows the mod's effects setting.

Temporary sprite assets live under `Textures/ProjectGU/Gu/StoneSkinGu`. The built-in image generator produced a transparent sheet of north, east and south views of a flattened grey stone beetle and an isolated arc of slate fragments for the protective overlay.

<details>
<summary>Sprite prompt</summary>

> Create one strictly aligned 2 by 2 game asset sheet on a genuinely transparent 1024x1024 background. There is exactly one isolated asset in each quadrant, centered in its quadrant, with ample transparent padding and no sprite crossing center axes. No labels, text, borders, scenery, ground or drop shadow. Stylized hand-drawn 2D sprites readable at a tiny scale, subtly outlined charcoal, a restrained range of slate grays. The creature is a very flat broad beetle whose carapace resembles a cracked smooth gray river stone, with six tiny articulated legs peeking out and a tiny head, no horns. Top left: the same beetle in game north view, head points to top of image, slightly overhead. Top right: the beetle in game east view, head points right, low side profile, slightly overhead. Bottom left: game south view, head points toward bottom, slightly overhead. Bottom right: a separate pale gray subtle semicircle of a few small cracked slate fragments and dust, open center, for a modest temporary protective overlay around a character's feet. No UI circle. Square canvas, transparent.

</details>

## Verification

`scripts/verify.ps1` passed structural checks, assembly-linked compilation and isolated managed tests on Windows with .NET SDK 10.0.401 and RimWorld 1.7.5021 rev1365. Native automation used disposable read-only game copies with Core and Harmony 3.0.4.

The requested regression profiles passed against the production payload in campaign `1cce4de25de441fabcdec4de1bd2b9d4`. `Slice1A` covered Black Boar, environmental search and map replacement. `WhiteBoar` covered both permanent bodily techniques, including Black Boar's interrupted-channel reload. `Alpha2` covered Moonblade and in-flight persistence, `Alpha3` covered Liquor Worm, `Alpha4A` and `Slice3B5B` covered aperture behavior and custody, and `Slice0Wildlife` covered native animal generation and occupancy. The campaign left the game copy and ordinary user data unchanged.

The focused `StoneSkin` profile passed new-game setup and acquisition through active save in campaign `d63907874ddc41dbb9457f9c0847914d`. The two independent fresh-process continuations passed in `Continuation-49a8e93a6ee2438590ec316efaa6ab3d` under that campaign, using copies of the same active save with SHA-256 `7387330C76D7638DA38F7526BFDA26571304D865F049BDE0D1E619E01A445194`. The focused and regression manifests match for all 120 production files.

The focused fixture advances the replacement or hunger clock where needed to exercise native recovery and starvation. It verifies independent residents, detection-only search, native capture and food delivery, job continuity, Moonblade coexistence, and activation rollback after a native health insertion error. It observes reduced damage on 7 of 40 matched Cut hits and 5 of 40 matched Blunt hits, with identical high-penetration controls. The fresh processes restored 0.7 Initial and 6.5 Middle essence at tick 31512 with upkeep due at tick 31665. They verified one payment at the original deadline, no unpaid protection after a shortage, and termination through withdrawal, transfer, destruction, unconsciousness, aperture loss and death. These are headless gameplay checks. Rendered sprite, command-bar and Health tab inspection remain part of the owner playtest.

The Steam installation was closed during deployment. All 120 installed production files matched the tested SHA-256 manifest. The installed `ProjectGu.dll` hash is `9C301C8F8336066A9DB6E688D4F59B95536D295575C7836E8203748E6C9897CC`.

## Owner playtest

1. In a rocky hill or mountain settlement, search outdoor ground near a natural rock face after giving wild Gu time to appear. Follow the detected Stone Skin Gu, capture it, pick it up, enforce its will, and store it in the refiner's aperture.
2. When it is hungry, feed a natural chunk manually. Try automatic feeding with a different natural chunk in the refiner's inventory or colony storage.
3. Select the Gu Master during work and toggle **Stone Skin** on. Check the **Health** tab and the pawn's armor stats. Draft the pawn, move or fight, and try another stored Gu while protected. Toggle Stone Skin off and check that the condition and armor bonuses are gone.
4. Activate again, save mid-interval, quit to desktop and reload. Check the same condition and essence balance before unpausing. Let essence run short or withdraw the source Gu and verify the effect ends. Toggle cosmetic effects off and verify the stats still change when activating.
