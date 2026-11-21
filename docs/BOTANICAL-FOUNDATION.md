# 0.2.5 botanical foundation decision

## Native plant behavior

Use Core's `PlantBaseNonEdible` for new Spirit Grass and Moon Orchid ThingDefs. RimWorld 1.7.5014 rev1195's `Plant` derives from `ThingWithComps`. It owns growth, age, sowing state and light stress through native Scribe, and uses the ordinary health, cutting and harvesting paths. Species XML supplies growth days, temperature and light thresholds, fertility sensitivity, and optional harvest yield. The first species can mature without sunlight on suitable ground. Their initial definitions omit harvest products. The native wild spawner can use biome commonalities, terrain tags, cluster weights and fertility for future regional flora. The cavern uses bounded seeded placement and regrowth to produce a reliable local baseline.

## Saved flora

Retain `GU_SpiritGrassHabitat` and `GU_MoonOrchidHabitat` as Building Defs for existing saves. Native Scribe resolves the serialized object class before the Thing reads its Def and loads its comps. Replacing those Defs with Plant versions could leave loaded Buildings with Plant configuration, while future spawns use a different class. New Plant Def names coexist with the legacy versions. A saved spring setting selects the new planting profile only on fresh generation. Existing caverns resume their saved terrain, physical patches and prior regrowth policy. Surface spring habitat remains compatible with its established fixtures.

## Render density

Royalty's installed Anima Grass Def uses `Graphic_Random`, a 3×3 mesh budget and a small visual size range. The installed `Plant.Print` scales mesh count with growth and places jittered planes in the static map section. Project GU uses that Core rendering path with original grass artwork, a bounded population along connected banks, and lower-density orchid colonies among the grass. Each grass Plant can show up to nine small tufts and has no added pathing cost. The revised spring lights the field through a focal light, soft shore and approach lighting, and a bounded set of mature orchid accents. [Rendered and native verification](UNDERGROUND-SPIRIT-SPRING.md#verification-and-release-limits).

## Qi-rich soil and sowing

Introduce a natural soil TerrainDef with the standard growing affordance, rich-soil fertility, blended edges, and a Project GU terrain tag. A native ground-sown plant can require that tag using `wildTerrainTags`. In this build, the ordinary growing-zone work giver reaches `PlantUtility.CanNowPlantAt`, which checks ground fertility and terrain tags. `Plant` also checks terrain tags when determining dying conditions. Give Moon Orchid the `Ground` sow tag and the Qi-rich terrain requirement, so the growing job can use any eligible soil cell, even on another map or without a spring. The milestone introduces the terrain through new cavern generation. Native soil suitability also covers later sources of this terrain, including other site profiles.

## Habitat support

Reuse the flora comp and map-local nearest-spring attribution for both Plant and Building Things. A botanical species has its own physical count and contribution. Scale the larger grass population to a 24-unit contribution at the seeded target. Keep three units per orchid at the five-orchid baseline, five units per effective support point and a cap of seven. A full field should yield 39 raw units, seven support points and Hope Gu capacity 17. Test this over seeded layouts and in the native game. The spring continues to own its reserve, schedule and Gu cohorts. Sown plants use their native growth and can exist without an enrolled spring.
