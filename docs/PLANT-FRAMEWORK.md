# Project GU plant framework

Project GU plants are ordinary RimWorld plants. Growing zones, sowing, growth conditions, Harvest designations, grower and plant-cutting work, stockpiles and work priorities are all native. The framework adds only what native plant definitions cannot express. Moon orchids are its first user.

Introduced in Alpha 5B. Code: [`Source/ProjectGu/Botany`](../Source/ProjectGu/Botany).

## What the framework adds

| Feature | How it works | Scope |
| --- | --- | --- |
| Seed-gated sowing | A native grower sow job for a seed-gated plant becomes `GU_SowWithMaterial`. The colonist fetches the planting material, carries it, sows exactly as vanilla does, and spends the material in the tick the sowing completes. With no usable material the job is refused, with a reason that shows in the right-click menu. | Only plants whose spec sets `sowingMaterial` |
| Seed collection | **Gather seeds** works three ways: an Orders-menu designator, a command on the selected plant, and a right-click option. A grower collects seeds from a flowering plant without uprooting it. Gathering spends the same flowering as a harvest. | Only plants with `seedCollection` |
| Right-click harvest | With a colonist selected, right-click a plant to harvest it, even with no designation and no growing zone. Unavailable options stay visible with a reason: immature, regrowing, reserved, forbidden, no path, incapable or unwilling. Vanilla's own "Prioritize harvesting" is left to vanilla. | Plants with the spec |
| Double-click selection | Double-clicking a framework plant selects the same-def plants on screen, even when a pawn stands near the cursor. Vanilla would pick the pawn instead. | Plants with the spec |
| Extra products | `byproducts` are added through the native `GetAdditionalHarvestYield` hook, which runs after a successful harvest. | Plants with the spec |
| Player summary | The description and inspect pane show category, habitat, cultivation, seeds and harvest rules. They also show which Gu eat the products, derived automatically from every `GuSpeciesDef` feeding list. | Plants with the spec |

Every hook returns immediately for a plant without `CompProperties_GuPlant`, so vanilla and third-party crops are never affected. A managed check guards this.

## Adding a plant (XML only)

1. **Define the plant natively.** Write an ordinary `ThingDef` with a `plant` block. Set `sowTags` to make it sowable, and `wildTerrainTags` for natural habitat. Use `harvestedThingDef`, `harvestYield`, `harvestTag` set to `Standard`, and `harvestWork`. For a repeat harvest, set `harvestMinGrowth` and `harvestAfterGrowth` with `harvestAfterGrowth` lower. Give it a real `sowWork`: vanilla's default of 10 makes sowing instant.
2. **Define the products natively.** Petals, herbs, fruit and seeds are ordinary item `ThingDef`s with `thingCategories`, so stockpiles, hauling and trade work for them.
3. **Add the spec:**

```xml
<comps>
  <li Class="ProjectGu.Botany.CompProperties_GuPlant">
    <category>Herb</category>                     <!-- Flower, Herb, Crop, Tree, Grass, Fungus, Vine, Other -->
    <naturalHabitat>shaded qi-rich riverbanks</naturalHabitat>
    <cultivable>true</cultivable>                 <!-- false: wild only, sowing refused -->
    <sowingMaterial>GU_ExampleSeeds</sowingMaterial>   <!-- omit to sow freely like a vanilla crop -->
    <sowingMaterialCount>1</sowingMaterialCount>
    <seedCollection>                               <!-- optional -->
      <yield>2</yield>
      <work>300</work>
      <!-- minGrowth / growthAfter default to the native harvestMinGrowth / harvestAfterGrowth -->
    </seedCollection>
    <harvestRoles><li>Medicine</li><li>Trade</li></harvestRoles>   <!-- GuFood, Refinement, HumanFood, Medicine, Trade, Planting, Other -->
    <byproducts>
      <li><thingDef>GU_ExampleResin</thingDef><count>1</count><chance>0.25</chance><roles><li>Refinement</li></roles></li>
    </byproducts>
    <usesNote>Reserved for future refinement recipes.</usesNote>
  </li>
</comps>
```

4. **Feed Gu with it, optionally.** List the product in a species' `feeding/foods`. The plant's summary then names that Gu, and automatic feeding can use the product from stockpiles. No C# is needed.

### Validation (`ConfigErrors`)

These configurations are rejected at load:
- a harvest with no product;
- a repeat harvest that keeps more growth than it needs, which would allow unlimited harvesting;
- seed gathering that would kill the plant or never need regrowth;
- planting material on an unsowable plant;
- planting material on a wild-only plant;
- a byproduct chance outside (0, 1].

## Conservation rules

- **Sowing:** the material is consumed only in the completing tick (`TryConsumeMaterial`), and only then does the plant leave the sowing stage.
  - **Interrupted:** the half-sown plant is removed exactly as vanilla removes it, and the carried material is dropped by the native job cleanup.
  - **Save and load:** the carried material and the sowing progress persist, and the job resumes. The material is spent once.
- **Seed gathering:** validated again in the completing tick. A failed gathering, which depends on Plants skill like a native harvest, still spends the flowering, as a failed native harvest does.
- **Harvesting and gathering both:** non-destructive, and both need the plant to regrow before either can happen again.

## Compatibility with external seed systems

- **SeedsPlease:** compatibility with the actual mod, including Lite and successors, remains unverified on RimWorld 1.7.
- **Detection:** the framework checks known package IDs and IDs containing `seedsplease`. When a match is active, Project GU bypasses its own seed gate and the plant description tells the player the external system manages sowing. External seed eligibility and consumption still need an end-to-end test with the real mod.

## Old saves

The spec is a stateless component, and harvest and gathering eligibility is the plant's native growth. Existing orchids, wild or sown, gain the component on load, with nothing migrated or destroyed. Growing zones already set to moon orchids now need seeds for new sowing. Plants that are already established keep growing and can be harvested.
