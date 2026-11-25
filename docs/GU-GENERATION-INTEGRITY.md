# Wild Gu generation integrity

On RimWorld 1.7.5014 rev1195, `ScenPart_StartingAnimal.RandomPets` selects from animal PawnKinds whose race has positive `petness`. It uses handling skill to form the first pool and falls back to a pool without that filter. The Southern Border scenario delegates both pets to this selector. Hope Gu, Moonlight Gu, Liquor Worm and the developer race all inherited `petness = 0.08` from Core's `BaseHare`. Their zero ecosystem weight, empty trade tags and disabled manhunter flag had no bearing on the starting-pet roll.

All four pairs of race and PawnKind Defs now inherit the shared `GU_WildProjectionRace` and `GU_WildProjectionKind` parents. Use these parents for new wild Gu across ranks. They keep the native animal body and AI. Species Def validation reports an exposed kind and the registry factory rejects it.

## Native generation audit

| Surface | RimWorld 1.7 selector and Project GU restriction |
| --- | --- |
| Random starting animals | The native pet selector requires positive `petness`. Every Gu projection race sets it to zero. Scenario randomization and the editor also use a named-animal list that ignores petness. A targeted filter excludes Gu from that list. A previously saved or directly edited scenario naming a Gu kind can still reach the native factory. |
| Biome wildlife | `WildAnimalSpawner` obtains candidates from the biome's wildlife pools. Gu declare no wild biome membership. Zero ecosystem weight controls how a live animal counts toward the map's animal budget. The biome audit also checks the runtime list used for coastal and pollution variants when those expansions are active. |
| Trader stock and carriers | `StockGenerator_Animals` requires a matching sale tag and a race the trader may sell. Wild Gu clear trade tags and set tradeability to `None`. Native caravan carriers use the faction's named pack-animal kinds. Gu have pack-animal eligibility disabled. |
| Quest animals and rewards | `QuestNode_GetAnimalKindByPoints` requires an animal under the points budget whose race allows quest inclusion. All Gu races set `neverIncludeInQuests`. The hunt selector uses existing wildlife from the biome's animal list. A reward or quest configured with a specific kind can call the factory directly. |
| Manhunter incidents and ambushes | `AggressiveAnimalIncidentUtility` filters ordinary candidates by `canArriveManhunter`, including the fallback across loaded kinds. The shared kind disables this flag. Manually supplied incident kinds can reach the factory directly. |
| Migration and farm animals | The shared race disables herd migration. The farm-animal wander-in selector requires the `AnimalFarm` trade tag, low wildness and quest eligibility. The shared race clears trade tags and disables quest eligibility. |
| Generic fresh Pawn creation | `PawnGenerator` calls `ThingMaker.MakeThing`, which invokes the Gu Pawn's sealed `PostMake` override. The override requires a one-use, thread-local permit for the exact race. A direct attempt raises an error before the native factory returns a Pawn. The override affects only the Gu projection class. |

## Creation, custody and old saves

The registry acquires the permit only for a new, factionless wild projection. Spirit-spring searches and lures use the existing cohort transaction, which validates the source and revision, places the staged Pawn, commits one adult debit and binds one record. Release and recovery recreate the physical representation of an existing record. Future habitats and controlled scenarios must enter through a registry transaction with an explicit source and accounting before placing a Gu. Retain that rule for future ranks.

The explicit Project GU test action requires Development Mode and a species tagged `development_test`. It records development provenance and labels the specimen in the inspector. Native debug spawn tools that explicitly request a Gu kind use the same fresh-creation gate.

Scribe restores old bodies without invoking the fresh-creation hook. An old unbound starting pet is retained as a rogue projection and labeled in the inspector. The registry requires a matching canonical record, native ID, kind and revision before capture or ownership. Loading a rogue neither creates a record nor changes a cohort.

## Verification

On Windows with .NET SDK 10.0.401 and installed RimWorld 1.7.5014 rev1195 assemblies, `scripts/verify.ps1` passed the Def checks, game-linked compilation and managed suite. The release build had zero warnings and errors.

Native campaign `1e40813706b24721adcfeeeb8a67e67b` passed `GuGeneration`, `ClanStart`, `Alpha1`, `Alpha2`, `Alpha3`, `Slice3B3B`, `Slice3B5A` and `Phase2Lifecycle` on Core with Harmony 3.0.4. The Alpha and custody profiles verified spring searches, luring, capture, refinement, inventory and aperture transfers, exact cohort debits and save/load. The focused profile queried both starting-animal selectors, every loaded biome's wildlife list, 11 animal stock generators for the colony tile and the native manhunter predicate for all four kinds. It also sampled 64 native quest draws at two point budgets. Natural selection logged zero errors. Deliberate fresh-generation attempts produced the expected guard error. A fresh process loaded the same developer record and flagged an unbound legacy pet, with no change to the Moonlight cohort.

The same `GuGeneration` profile passed new-game and reload stages with Biotech and Odyssey active in campaign `cbc6eae48a9f44438099110dcd51aaa4`, exercising the loaded coastal and pollution wildlife lists.

`Slice3B4` initially stopped at the aperture-storage check because its older fixture had skipped the refinement bond. With the same fixture setup used by the other custody profiles, all three stages passed in campaign `fe568d43c9ef4feea580e8e9ddf599ed`, including active release, interrupted placement and a fresh-process reload of the sole wild Pawn.

## Coverage limits

Explicit third-party scenario, biome, faction, quest and incident integrations should use a Project GU registry transaction. A caller supplying a named kind to native fresh generation receives the guard error and may need its own error handling. Native redressing can reuse an existing world Pawn without invoking `PostMake`. The ordinary selection exclusions keep Gu kinds out of the audited pools, but a custom integration that directly requests Gu redressing needs a separate safeguard. Engine snapshot cloning and third-party Def edits that replace the Gu Pawn class also need separate safeguards. The audit does not exhaust every DLC-specific or modded quest flow. Existing animal interactions such as faction assignment concern native custody and confer no canonical Gu ownership.
