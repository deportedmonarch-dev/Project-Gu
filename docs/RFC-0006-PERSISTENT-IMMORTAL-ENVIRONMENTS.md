# RFC-0006 — Persistent immortal environments

**Status** — Requirements and foundation proposal. Implementation is not authorized. Only the interfaces the Gu Framework needs now are proposed for early work. Sections 8–11 record the owner's hybrid-environment direction of 2026-11-22.
**Target** — RimWorld 1.7 Waylith / .NET 10
**Depends on** — [RFC-0004](RFC-0004-GU-FRAMEWORK-V2.md) · [RFC-0005](RFC-0005-CULTIVATORS-REFINEMENT-APERTURES.md)
**Evidence** — [Engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md) · [Underground lifecycle](UNDERGROUND-INDEPENDENT-LEVELS.md)

Immortal apertures, blessed lands and grotto-heavens are persistent worlds inside the world. They are a foundational system, not an enlarged inventory.

## 1. Requirements

| Area | Requirement |
|---|---|
| Identity | Each environment has a durable ID independent of any map, Pawn or world object. |
| Ownership | An environment can belong to a living immortal, survive its owner, pass by succession or inheritance, or govern itself. |
| Space | It can be a real, explorable native map with buildings, settlements, inhabitants, plants, beasts and Gu. |
| Gu | Refined and unrefined Gu live inside, active or dormant, manifested on the map or abstract. |
| Economy and ecology | Internal production, consumption, growth, decline and population change. |
| Dao and path | Path affinity and dao-mark properties that shape ecology, resources and Gu. |
| Time | Independent internal time flow. |
| Hazards | Calamities, tribulations and environmental damage. |
| Exchange | Transfers of resources, individuals and populations in and out. |
| Change | Expansion, multiple regions, annexation, merging and splitting. |
| Scale | Environments that need no continuous native map simulation are simulated abstractly. |
| Integrity | Materialization and dematerialization never lose or duplicate resources, individuals, buildings or meaningful history. |

## 2. Engine facts that constrain the design

- **All maps share one clock.** There is no native per-map time rate. [F7](ENGINE-1.7-GU-V2-INVESTIGATION.md#f7-all-maps-share-one-clock)
- **Loaded maps are expensive and limited.** Every loaded map ticks every tick. The independent provider caps underground levels at eight and checks the signed-byte map index limit before generation.
- **Map removal discards contents.** Things are marked discarded without destruction callbacks. Pawns pass to the world and may be collected by GC. [F3](ENGINE-1.7-GU-V2-INVESTIGATION.md#f3-map-removal-is-not-destruction), [F4](ENGINE-1.7-GU-V2-INVESTIGATION.md#f4-world-pawn-gc-ignores-mod-references)
- **There is no proven Core way to unload a map and restore it later.** Odyssey's gravship capture is a DLC precedent for lifting structures. [F9](ENGINE-1.7-GU-V2-INVESTIGATION.md#f9-native-precedent-for-lifting-map-content)
- **Proven infrastructure exists.** A hidden planet layer supplies native map addresses. An engine-owned `MapParent` subclass owns each level. A saved site, level, endpoint and route graph links levels. Lifecycle guards block destructive removal. Emergency return recovers stranded Pawns. Rendered and fresh-process native tests passed in Core and with Ideology and Odyssey. [Underground lifecycle](UNDERGROUND-INDEPENDENT-LEVELS.md)

## 3. Reuse of the underground system

| Underground component | Reuse for environments | Gap |
|---|---|---|
| `GuUndergroundAddressLayer` (hidden planet layer, 32 addresses) | **Retain and generalize.** Any non-geographic map needs a native address. | Rename or add a second Def for environments to keep address pools separate. Raise the address count after measurement. |
| `GuUndergroundLevelParent` (engine-owned `MapParent`) | **Extend.** An `EnvironmentRegionParent` subclass with the environment and region IDs. | Entry is by cultivator or portal, not by a surface building. |
| `GuIndependentSite` level, endpoint and route graph | **Adapt.** Becomes the region and portal graph of an environment. | Endpoints may be a cultivator's body, a portal Thing, or an abstract gate. |
| `GuUndergroundLifecycleGuards` | **Retain.** The same prefixes protect environment parents and maps. | Guard predicates must ask the environment registry. |
| Emergency return and passage travel jobs | **Adapt.** Same Pawn movement and recovery mechanics. | Destination selection goes through the environment's entry policy. |
| Eight-level cap | **Investigate.** | Shared budget across underground levels and environments. Must be measured. |
| Map dormancy and snapshot | Not implemented. | New infrastructure (section 5). |

Environments do not need to be underground sites. They share the address, parent, route and guard infrastructure, and add their own identity, ownership, abstract simulation and materialization.

## 4. Environment model

```csharp
public sealed class EnvironmentRecord : IExposable      // saved under guEnvironmentRecordsV1
{
    string environmentId;
    EnvironmentKindDef kind;            // ImmortalAperture, BlessedLand, GrottoHeaven, ...
    EnvironmentOwnership ownership;     // owner cultivator, succession rules, governance mode
    List<EnvironmentRegion> regions;    // each region is abstract or materialized
    EnvironmentClock clock;             // internal elapsed time, time-flow ratio, last sync tick
    DaoProfile dao;                     // path affinities and dao marks
    EnvironmentLedger ledger;           // abstract resources, cohorts, structures, history
    List<string> portalIds;             // links in the shared route graph
    int revision;
}

public sealed class EnvironmentRegion : IExposable
{
    string regionId;
    RegionMode mode;                    // Abstract | Materializing | Materialized | Dematerializing
    EnvironmentRegionParent parent;     // non-null only while materialized
    RegionSnapshot snapshot;            // the authoritative content while abstract
}
```

### 4.1 Single authority per region

At any moment a region's contents have exactly one authority.

- **Materialized.** The native map is authoritative. The abstract snapshot is frozen and holds only metadata.
- **Abstract.** The snapshot is authoritative. No native map exists.
- **Transitional.** `Materializing` and `Dematerializing` are durable states with a transition record, like RFC-0004's `Transit`. A save during transition resumes or rolls back by ID.

Gu inside a materialized region are ordinary manifested Gu, or contained Gu in apertures of Pawns present. Gu inside an abstract region are `Contained(EnvironmentRegion)` records with their condition and last cell.

### 4.2 Time flow

- While abstract, the region advances its own `EnvironmentClock` at its time-flow ratio. The scheduler converts outside elapsed ticks to inside elapsed time with a bound per update. Contained Gu processes use the container `TimeScale`.
- While materialized, the native map runs at the shared rate. Options for owner decision:
  1. **Shared rate while visited.** Simple and safe. The time-flow ratio applies only while abstract.
  2. **Catch-up on exit.** Apply the extra elapsed time abstractly when the region dematerializes. Visits feel normal and long-term growth still reflects the ratio.
  3. **Engine-level dilation.** Patching map ticks per map is high risk and is not recommended without a dedicated probe.

The owner's 2026-11-22 direction favors normal native rates while visited and acceleration expressed while the player is outside, close to option 2. See section 8.1.

The owner's 2026-11-22 direction favors normal native rates while visited and acceleration expressed while the player is outside, close to option 2. See section 8.1.

### 4.3 Ownership and succession

`EnvironmentOwnership` references cultivator IDs only. Owner death triggers the environment's succession rule. The environment can pass to an heir, become ownerless and self-governing, decay, or collapse into a sealed blessed land. The rules are owner decisions. The environment record survives in every case until an explicit terminal outcome.

### 4.4 Exchange and conservation

Every movement across the boundary is an operation with an idempotent event key. Examples are resources leaving as stacks, Gu moving as records, and populations moving as cohort transfers. Resource ledgers use the same debit and credit discipline as Gu cohorts.

## 5. Materialization strategies

| Strategy | Fidelity | Engine risk | Use |
|---|---|---|---|
| **A. Always loaded while owned and active** | Full | Low. Proven by the underground provider. | First implementation of a player's own immortal aperture. |
| **B. Generate from abstract state** | Summary. Exact buildings and items need a full structure ledger. | Medium. Generation is proven. Restoring player-built structure exactly is not. | NPC environments, unvisited blessed lands, first visits. |
| **C. Native snapshot and restore** | Full | High and unproven. Cross-references to Pawns, factions and world objects must survive outside a whole-game load. | Only after a feasibility probe. |
| **D. Partial capture** | Structures and items | Medium. Odyssey precedent only. | Investigate as a Core-compatible variant. |

**Recommendation.** Design the abstract model and the transition protocol first. Implement strategy A for the first owned environment, use strategy B for environments the player has never shaped, and fund a strategy C feasibility probe before promising full dematerialization of player-built content. Dematerialization of a region the player has built in is unavailable until a high-fidelity strategy passes.

## 6. Gu Framework interfaces needed now

Only these pieces are proposed alongside V2. They keep the Gu record compatible with environments without building environments.

1. `GuContainerRef` kinds `ImmortalAperture` and `EnvironmentRegion`, with `TimeScale` (RFC-0004 §6).
2. `Unresolved(MapRemoved)` and `Contained(TileAbstract)`, which reuse the same abstract-placement path an environment will use.
3. A `ManifestationBoundary` contract. Dematerializing a map turns each manifested Gu on it into `Contained(region)` with its condition and cell. Materializing reverses the operation. Both are batch operations under one transition record.
4. Cultivator and aperture IDs that an environment owner can reference (RFC-0005).

## 7. Decisions for the owner

1. Is the first immortal environment a loaded map from day one, or abstract first?
2. Which time-flow option applies while the player is inside?
3. Succession and collapse rules when the owner dies.
4. Whether player-built content may ever be dematerialized before a high-fidelity strategy passes.
5. The shared budget of loaded underground levels and environments.
6. Scope of calamities and tribulations in the first environment slice.

## 8. Hybrid simulation (owner direction)

Recorded 2026-11-22 from the owner's Phase 2 authorization. Sections 8–11 are design requirements and directions under consideration. They do not authorize environment implementation.

The preferred direction combines abstract and manifested environments.

- NPC immortals and non-appointed colony immortals generally have persistent **abstract** environments.
- The player may appoint particular colony immortals whose blessed lands are fully **manifested** native maps.
- Discovered inheritances and expeditions may materialize an environment for direct interaction.
- Both forms support meaningful management.
- Environments the player has developed should eventually be preserved and restored at high fidelity, if the engine permits it safely (strategy C, section 5).

An abstract environment communicates its stability, time-flow ratio, upcoming tribulations, dao marks and path affinity, territory and biome composition, resources and production, Gu populations, wildlife, mortal and variant-human populations, and internal settlements and social development. Abstract gameplay includes expeditions, resource development, Gu searches, diplomacy, subjugation and population management. Abstract populations are persistent world entities, not disposable menu events.

A manifested environment supports native interaction with inhabitants, settlements, Rockmen, wildlife, Gu and resources.

### 8.1 Efficient time dilation

- Prefer mathematical elapsed time and scheduled processes over multiplying the native tick loop.
- While the player visits a manifested environment, movement, combat and jobs run at the normal native rate.
- Dilation applies to ecological, biological and economic development. The owner favors expressing acceleration while the player is outside.
- Acceleration carries costs, resource consumption and ecological consequences.
- Native and abstract simulation never both produce the same output.
- A loaded but unvisited map keeps ticking. The design must not assume otherwise.
- Hibernation and high-fidelity snapshot restoration are investigated in the environment phase.

### 8.2 Performance requirements

Permanent limits on concurrent manifested blessed lands wait for dedicated benchmarks: the surface colony baseline, one, two and four extra manifested environments, several map sizes and ecological workloads, several Pawn and animal populations, and 100 and 1,000 abstract environments. Each measures tick time, TPS, memory, save size, load time and scheduling overhead. The benchmark belongs to the environment phase. The design never requires hundreds of continuously loaded native maps.

### 8.3 Cultivation path and ascension

An environment reflects its immortal's cultivation history. A healing-path cultivator who ascends may develop medicinal resources, healing Gu and matching production. Rank 5 Gu, ascension outcomes, heaven and earth qi and dao marks may influence the result. Specific ascension mechanics are later decisions.

## 9. Blessed Land Roots (future)

- An eligible deceased immortal has a proposed initial 50% chance of leaving a recoverable Blessed Land Root.
- Eligibility depends on actual environment state: a living or dead aperture, prior rooting, destruction by calamity, annexation and other lifecycle conditions.
- A root refers to the deceased immortal's actual environment, never a newly randomized map.
- A root may be planted to create an entrance to that environment, or annexed by another eligible environment.
- An immortal escaping to found an inheritance may deliberately root their existing blessed land.
- The outcome is generated once and persists across reloads.
- Environment identities, Gu, inhabitants and resources are never duplicated.

## 10. Annexation and territorial assimilation (future)

Annexation is selective and stat-based, not literal stitching of native maps.

An annexation report exposes the donor's territorial size and capacity, terrain and biomes, dao marks and path affinities, climate and ecology, time flow and stability, Gu, wildlife, mortal and variant-human populations, and resources and production. The player selects eligible assets and characteristics. For example, annexing a desert immortal's blessed land may transfer a chosen amount of desert territory, transplant suitable Gu and populations, and convert the remaining territorial potential into approved benefits.

- Selected territory replaces designated territory or expands the environment.
- Remaining potential may feed environmental properties, dao marks, time flow or expansion capacity under balanced rules.
- Conflicting characteristics can bring disadvantages as well as benefits.
- Every donor asset is transferred, transformed, retired or accounted for exactly once. Nothing is duplicated.

### 10.1 Environmental specialization

Rare resources may need specific combinations of substrate, climate, biome, dao marks, qi, symbiotic plants, beasts or Gu, stability and internal maturation time. Example: a desert-dependent seed that needs centuries to mature. A cultivator without desert territory may acquire or annex it and use accelerated internal time to raise the seed. Biome requirements are mechanical, not cosmetic.

## 11. Physical map expansion (future investigation)

Investigate, in order of expected safety:

1. Unlocking more usable territory inside a preallocated native map.
2. Adding independently materializable regions.
3. Safely resizing an existing native map.

Abstract territory capacity is tracked independently of its native representation. Territorial conversion protects player buildings, inhabitants, valuable resources and meaningful terrain. The project makes no commitment to native map resizing before engine verification.
