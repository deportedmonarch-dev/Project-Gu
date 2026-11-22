# RFC-0004 — Gu Framework V2

**Status** — Phase 2 implementation notes in [§16](#16-phase-2-lifecycle-as-implemented). Accepted with the gate G1 amendments in [section 15](#15-gate-g1-amendments). Phase 1 implementation is authorized on `feature/gu-framework-v2`. Later phases and gameplay policies remain unapproved.
**Target** — RimWorld 1.7 Waylith / .NET 10
**Supersedes** — The data model and custody validation in [RFC-0002](RFC-0002-GU-INDIVIDUALS-AND-CUSTODY.md). RFC-0002's transaction discipline, conservation rules and event-key idempotency carry forward.
**Companions** — [RFC-0005 cultivators and apertures](RFC-0005-CULTIVATORS-REFINEMENT-APERTURES.md) · [RFC-0006 immortal environments](RFC-0006-PERSISTENT-IMMORTAL-ENVIRONMENTS.md) · [Engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md) · [System assessment](V2-SYSTEM-ASSESSMENT.md) · [Reconstruction plan](V2-RECONSTRUCTION-PLAN.md)

## 1. Why V1 must be reconstructed

Schema V1 proved identity, staged native transactions, exact Pawn and Hediff binding, cohort debit and save/load replay. Those results remain valuable. Its data model cannot support ordinary gameplay.

1. **Location is treated as authority.** V1 stores the exact holder of a dormant Gu and quarantines the record whenever the engine moves the Thing. The [engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md#f1-ordinary-pawn-events-move-inventory-through-native-paths) verified that downing, death, imprisonment and stripping all move inventory natively. Every one of those ordinary events ended in permanent quarantine.
2. **Quarantine is the only outcome.** A killed wild Gu, a destroyed dormant Gu, a Gu on a removed map and a world-pawn GC all become quarantined. Quarantine was meant for evidential conflict. It is now the universal answer to normal play.
3. **Reads mutate saves.** Drawing an inspect string quarantined a record.
4. **Custody and biology are fused.** `DormantGround`, `DormantInventory` and `Aperture` all require `Dormant`. An awake captured Gu cannot be represented.
5. **No refinement, terminal, transformation or species state exists.** The record has a single revision, one aperture slot and the rank of the species minimum.
6. **Policy is hard-coded as validity.** A holder must be a free colonist. A prisoner, slave or NPC cultivator invalidates custody.
7. **Projection discovery scans the world.** Load-time binding enumerates every Pawn and recursively searches each map for configured Defs. Transport pods and other world holders are missed.

Incremental patches could remove individual symptoms. They would leave location-as-authority in place, which is the root cause. V2 changes the model and reuses V1's transaction, rollback, save-guard and replay machinery.

## 2. Design principles

- **One canonical record per individual.** The record owns identity, existence, biology, refinement, provenance and record-only containment.
- **The engine owns physical location.** When a Gu has a native manifestation, its holder, map and cell are observed from the native graph. They are never validated against a stored expectation.
- **The registry owns manifestation.** Only the registry decides which native object represents a Gu. A native object claiming an ID is not authorized by that claim.
- **Ordinary gameplay ends in a living or terminal outcome.** Quarantine is reserved for evidence that the save contradicts itself.
- **Reads are pure.** Queries return immutable views. Discrepancies go to a reconciliation queue.
- **Species add behavior through optional parts.** No universal update loop and no base class with irrelevant members.
- **Containment is records.** Apertures, sealed vessels, Gu Houses and abstract environments hold many Gu with no Pawn or Thing per occupant.

## 3. Record model

The canonical record is split into independent facets. Each facet changes through explicit operations.

```csharp
public sealed class GuRecord : IExposable              // saved under guIndividualRecordsV2
{
    // Identity (immutable after creation)
    string id;                     // GUID "N"
    string speciesDefName;         // raw key, preserved when the Def is missing
    GuProvenance provenance;       // origin kind, origin ref, creation event key, parents, tick
    string uniquenessKey;          // null unless the species declares a uniqueness scope

    // Mutable facets
    int rank;
    GuExistence existence;         // Living | Terminal | Transformed | Aggregated
    GuCondition condition;         // Active | Dormant, plus optional vitality
    GuPlacement placement;         // Manifested | Contained | Unresolved
    GuRefinement refinement;       // null when unrefined; see RFC-0005
    List<GuStateComponent> state;  // optional species state, created on demand
    GuIntegrity integrity;         // null unless evidential quarantine applies

    // Revisions
    int custodyRevision;           // binding token copied to the projection
    int stateRevision;             // biology, rank, refinement, species state, existence
}
```

### 3.1 Identity, species and rank

- `id` is a GUID and never changes. Transformation creates new IDs and links them through provenance.
- `speciesDefName` stays a raw string. A missing Def is a **degraded** record, not a quarantine. It keeps all data, cannot manifest or act, and recovers when the Def returns.
- `rank` is individual state within the species range. Rank advancement is a state operation. A transformation to another species is a lifecycle operation.

### 3.2 Existence and lifecycle outcomes

| Existence | Meaning | Record retained as |
|---|---|---|
| `Living` | The individual exists and can be placed, act and change. | Full record |
| `Terminal(outcome)` | The individual no longer exists. | Compact tombstone |
| `Transformed(resultIds)` | The individual became one or more new individuals. | Tombstone with links |
| `Aggregated(cohortRef)` | The individual returned to an aggregate cohort and lost individuality. | Tombstone with the credit event |

Terminal outcomes are explicit and species-neutral.

| Outcome | Examples | Typical source |
|---|---|---|
| `Died` | Starvation, combat death, old age, failed host-loss survival | Pawn death hook, species process, host-loss policy |
| `Destroyed` | Burned, crushed or vanished as a physical object | Projection comp `PostDestroy` |
| `Consumed` | Hope Gu spent in awakening, Gu eaten, used up by a single-use ability or killer move | Capability or process commit |
| `Lost` | Left the simulation boundary irrecoverably under an owner-approved policy | Unresolved resolution policy |
| `Sacrificed` | Spent as a deliberate cost of refinement, ascension or a formation | Process commit |

A tombstone keeps the ID, species key, provenance, uniqueness key, outcome, cause, tick and the event key that ended it. Tombstones protect replay, conservation and uniqueness. Section 11 covers retention.

### 3.3 Biological condition

`GuCondition` holds `Active` or `Dormant` and an optional vitality value. It is independent of placement. An awake Gu can be held in a sealed jar or an aperture. A dormant Gu can lie on the ground or rest in an environment. Species decide which conditions apply in which placements through capability policies. Hunger, injury and age live in optional species state components, not in the core record.

### 3.4 Placement

Placement answers *where the individual is and what represents it*. Exactly one placement applies at a time.

```csharp
abstract record GuPlacement;
record Manifested(string projectionLoadId, ManifestationKind kind) : GuPlacement;   // Pawn or Thing
record Contained(GuContainerRef container, string slotOrRegion) : GuPlacement;      // record-only
record Unresolved(UnresolvedCause cause, GuLastKnown lastKnown, int sinceTick) : GuPlacement;
```

**Manifested.** The Gu exists as one native Pawn or Thing carrying a `CompGuManifestation`. The engine is authoritative for its holder, map, cell, caravan or transporter. The registry derives location from the projection's `ParentHolder` chain when asked. A native drop, strip, death drop, caravan transfer or haul **is not a custody transition**. It changes no revision and needs no coordinator.

**Contained.** The Gu has no native object. A durable container holds it: a mortal aperture, a sealed vessel, a Gu House, an abstract environment region, or a transfer in transit. Container identity is a Project GU ID resolved to a native anchor. See section 6 and RFC-0005.

**Unresolved.** The Gu's representation was disrupted by an expected event and its outcome is pending. It is recoverable by design. Each cause has a resolution policy with a deadline.

| Cause | Detected by | Default resolution (owner decisions in the [plan](V2-RECONSTRUCTION-PLAN.md#owner-decisions-and-approval-gates)) |
|---|---|---|
| `HostDead` | Aperture Hediff `Notify_PawnDied` | Recover on resurrection. Apply the species host-loss policy when the corpse is destroyed or the deadline passes. |
| `HostCorpseDestroyed` | Hediff `Notify_PawnCorpseDestroyed` | Apply the host-loss policy immediately. |
| `HostAbsent` | Host Pawn discarded or unresolved on load | Apply the host-loss policy. |
| `MapRemoved` | Projection comp `Notify_MapRemoved` | Phase 1 keeps the record unresolved with the evidence. Phase 2 policy may abstract it into tile custody. |
| `ProjectionMissingOnLoad` | No authorized comp registered for a manifested record | Stays unresolved with any rogue candidates listed. Recovery is explicit. Absence of evidence is never a terminal outcome. |
| `WorldRetired` | An active Gu Pawn became a world Pawn | Abstract into regional or tile custody, or retain by GC rule. |

Unresolved records are inspectable and cannot act. They keep their uniqueness reservation and count toward conservation. A missing species Def, host or projection never makes a record terminal by itself.

### 3.5 Quarantine as evidential integrity

`GuIntegrity` is set only for contradictions the framework cannot resolve without outside evidence.

- Two living records claim the same ID, uniqueness key or creation event.
- A record has an unsupported version or corrupt required fields.
- Two native objects claim the same ID with valid comps and revisions.
- A transaction failed in a way that leaves the conserved total uncertain.

A quarantined record keeps its raw data and blocks actions and uniqueness reuse. An operator recovery action is required. Ordinary gameplay must never set it. Each quarantine reason has an automated test that proves the contradiction.

**Rogue projections.** A native object whose comp claims an ID without matching the authorized load ID and custody revision is a **rogue projection**. The record is unaffected while its authorized placement is live. The rogue object is suppressed. It becomes inert, is excluded from interaction, and is logged. If the authorized placement is missing and exactly one rogue claims the ID, the registry still does not adopt it. The record becomes `Unresolved(ProjectionMissingOnLoad)` with the rogue listed as evidence, and only an explicit recovery operation can bind it.

## 4. Revisions

| Revision | Advances on | Used by |
|---|---|---|
| `custodyRevision` | Manifest, unmanifest, contain, release, transfer between containers, rebind after recovery | Projection binding token. Stale requests for placement operations. |
| `stateRevision` | Condition, rank, refinement, species state, existence | Processes and UI actions that depend on state. |

Observed physical movement of an authorized manifestation advances neither revision. Each operation declares the revisions it expects, and also revalidates current physical location, accessibility, holder, allegiance and every other authorization condition at commit. A valid ID and unchanged revision are never sufficient on their own. Rejection, rollback, travel and reload preserve both revisions. V1 `revision` becomes `custodyRevision`, and `stateRevision` starts at 1.

## 5. Native manifestation and reconciliation

### 5.1 Projection representation

| Option | Advantages | Costs |
|---|---|---|
| Keep subclasses (`Pawn_GuWildProjection`, `Thing_GuDormantProjection`) | Proven in V1. The Pawn subclass can suppress ticks while staged. | Every species must use Project GU classes. Existing animal, insect or building classes cannot represent a Gu. Two parallel hierarchies grow with each manifestation kind. |
| Generalize subclasses with virtual hooks | Fewer changes now. | Same lock-in, plus a larger base class. |
| **Component (`CompGuManifestation`)** | Works on any `ThingWithComps`, including Pawns of any race class, items, apparel and buildings. Lifecycle hooks (`PostDestroy`, `Notify_Killed`, `Notify_MapRemoved`, `PostSpawnSetup`) are native. Load-time self-registration replaces world scans. [Verified to read V1 keys](ENGINE-1.7-GU-V2-INVESTIGATION.md#f6-saved-thing-classes-are-bound-by-name). | The comp must be declared on each manifestation Def. Tick suppression during staging needs another mechanism. |

**Recommendation — component.** `CompGuManifestation` saves `guIndividualId` and `guBindingRevision`, the existing V1 keys, so legacy projections migrate in place. The two V1 classes remain as thin, loadable legacy types for old saves and the developer Defs. Staging uses unspawned preflight where possible. A species that needs tick suppression may keep a Pawn subclass. The staging probe in the [engine report](ENGINE-1.7-GU-V2-INVESTIGATION.md#open-questions) decides whether the Pawn subclass can retire.

### 5.2 Binding

- During `LoadingVars`, each comp registers itself with the registry's load buffer. This finds projections in maps, inventories, caravans, transporters and world Pawns without a global scan.
- In `LoadedGame`, the registry pairs each `Manifested` record with the registered comp whose ID, load ID and custody revision match. A comp with the right ID and a wrong load ID or revision is a rogue.
- At runtime, `PostSpawnSetup` and `PostExposeData` keep an in-memory index from comp to record. No load or runtime path enumerates all maps.

### 5.3 Native lifecycle hooks

| Native event | Hook | Framework response |
|---|---|---|
| Gu Pawn killed | `CompGuManifestation.Notify_Killed` | `Terminal(Died)`. The corpse becomes an ordinary item. A species may convert it into a material. |
| Gu Thing destroyed | `PostDestroy(mode, map)` | `Terminal(Destroyed)` unless a transition guard owns the destruction. |
| Map removed | `Notify_MapRemoved`, with `notifyMapRemoved` on Gu Defs | `Unresolved(MapRemoved)` with the tile, map parent and condition. |
| Gu Pawn passed to the world | `Notify_PassedToWorld` | `Unresolved(WorldRetired)` or retention under the GC rule. |
| Holder downed, killed, imprisoned, stripped | none needed | Physical movement only. No change. |
| Discard of a staged projection | transition guard | Expected. No record change. |

All hooks enqueue an event. The registry applies queued events at the end of the current tick, outside native callbacks. Transition-owned destruction is recognized by the guard, exactly as V1 already does for retirement.

### 5.4 Pure queries and reconciliation

```csharp
public readonly struct GuView { /* immutable copy of identity, existence, condition, placement, refinement summary, revisions */ }

bool TryGetView(string id, out GuView view);
bool TryGetView(Thing projection, out GuView view);   // validates the binding without side effects
GuLocation Locate(in GuView view);                     // derived from the native graph
IEnumerable<GuView> InContainer(GuContainerRef container);
```

Views report what they observe and never mutate records or enqueue work. Correctness never depends on whether the player opened a UI. Native hooks enqueue events, and a scheduled reconciliation sweep checks a bounded slice of records each interval. A budgeted pass in `GameComponentTick` applies both.

## 6. Containers

A container is a durable record-only endpoint. The framework defines the contract. Each kind supplies policy.

```csharp
public interface IGuContainer
{
    GuContainerRef Ref { get; }                 // kind + Project GU ID
    bool TryResolveAnchor(out object anchor);   // Hediff, Thing, WorldObject or environment record
    AcceptanceReport CanAdmit(in GuView gu, GuAdmissionContext context);
    int Capacity { get; }                       // a count, a capacity measure, or unbounded
    float TimeScale { get; }                    // elapsed-time factor for abstract processes, default 1
}
```

| Kind | Anchor | Notes |
|---|---|---|
| `MortalAperture` | Aperture record via `Hediff_Aperture.apertureId` | Many occupants. Admission requires refinement or an active refinement process. RFC-0005. |
| `ImmortalAperture` / `EnvironmentRegion` | Environment record | Active or dormant occupants without projections while abstract. RFC-0006. |
| `SealedVessel` | A Thing with a container comp | Discovery stones, jars, inheritance caches. |
| `GuHouse` | A Gu House record | Composite structures of many Gu. |
| `Transit` | Transfer record | A saved in-flight move between incompatible endpoints. |
| `TileAbstract` | World tile and map parent ID | Gu left behind by map removal or world retirement, recoverable on revisit. |

Indexes from container to occupants derive from records and are never saved twice.

## 7. Operations and the coordinator

V1's specialized `TryTakeDormant`, `TryStoreDormant` and `TryReleaseActive` become one coordinator with declared operations. Each operation follows the V1 protocol: preflight against expected revisions, reserve, stage the destination without letting it act, commit the record and the native change once, then expose the result.

| Operation | Placement change | Revision |
|---|---|---|
| `Create(request)` | none → Manifested or Contained | both start at 1 |
| `Manifest(id, kind, target)` | Contained or Unresolved → Manifested | custody |
| `Unmanifest(id, container)` | Manifested → Contained | custody |
| `MoveContained(id, from, to)` | Contained → Contained | custody |
| `ChangeCondition(id, condition)` | none, or a species-required re-manifestation | state, plus custody when the projection changes |
| `SetRefinement(id, bond)` | none | state |
| `Terminate(id, outcome, cause)` | any → none | state |
| `Transform(ids, recipe)` | sources terminate, results are created | both |
| `Aggregate(id, cohort)` / `Materialize(cohort, request)` | individual ↔ cohort | both, with a cohort revision |
| `Resolve(id, resolution)` | Unresolved → any | custody, plus state if terminal |

The save guard, the transaction-in-progress flag, staged placement and rollback carry over from V1. Native placement that cannot be made reversible must use a `Transit` container so that a save between steps has a durable endpoint.

**Species boundary.** Species code may request operations. It may never write record fields directly. This keeps every custody and existence change inside the coordinator's conservation checks.

## 8. Provenance, creation and conservation

- Every creation carries a `GuCreationRequest` with an origin kind (`Cohort`, `Reproduction`, `Discovery`, `Refinement`, `Transformation`, `Event`, `Development`, `Migration`), an origin reference and an idempotent event key. A repeated key returns the original result. This generalizes V1's cohort event index.
- Conservation is defined per origin. For a cohort it is the V1 invariant: adults plus juveniles plus non-aggregated individuals debited from the cohort, including unresolved and quarantined ones. Terminal outcomes and aggregation credits are recorded events, so the total changes only through recorded causes.
- Transformation records parent and child IDs on both sides. Consuming three Gu to refine one records three terminal `Sacrificed` outcomes and one creation with three parents.

## 9. Uniqueness

A species may declare a uniqueness scope such as `World`, `Region` or `Lineage`. Before genesis, the coordinator reserves the key in a ledger derived from records and in-flight processes. Living, unresolved and quarantined records hold their key. A terminal record holds it only when the species' `uniquenessReleasePolicy` requires it. An Immortal Gu might release its key on death, or only after a regeneration interval. Two competing genesis sources cannot both reserve the key, and replay after reload returns the original reservation.

## 10. Species parts, processes and interactions

### 10.1 Capabilities

`GuSpeciesDef` gains an optional `capabilities` list of polymorphic properties, in the style of `CompProperties`. Each declares a worker and optional state.

| Capability | Purpose | State | Scheduling |
|---|---|---|---|
| `ActivatedAbility` | Costs, targeting, effect worker, cooldown | Cooldown, charges | On use |
| `PassiveEffect` | Stat or Hediff effects while refined or contained | none | Recomputed on bond or container change |
| `Feeding` | Diet, interval, starvation outcome | Nourishment | Due time |
| `Dormancy` | When the Gu can sleep or wake, and where | none | On event |
| `Autonomy` | Active behavior while manifested as a Pawn | none | Native Pawn AI |
| `Reproduction` | Cohort or individual offspring | Fertility | Ecology day |
| `HostLoss` | Outcome when an aperture host dies or vanishes | none | On event |
| `Consumable` | Single-use spending | none | On use |
| `Transformation` | Recipes into other species or ranks | Progress | Process |
| `Relationship` | Persistent links to other Gu, beasts or people | Link list | On event |
| `EnvironmentAffinity` | Path and dao-mark preferences | none | On environment change |

A species with no capabilities is a valid, inert Gu. No capability requires a per-tick update. A worker that needs time registers a due tick with the scheduler.

### 10.2 Species state

`GuStateComponent` is an `IExposable` with a `Class` attribute, created only when a capability needs it, and versioned individually. When a component's class is missing, the framework retains its raw XML node and writes it back unchanged. This needs the probe listed in the [engine report](ENGINE-1.7-GU-V2-INVESTIGATION.md#open-questions).

### 10.3 Processes and reservations

Persistent multi-step activities are `GuProcess` records saved by the registry.

```csharp
public sealed class GuProcess : IExposable
{
    string id; GuProcessDef def; int startedTick; int nextDueTick;
    List<GuParticipant> participants;   // gu id, role, expected custody and state revisions
    List<GuReservation> reservations;   // exclusive or shared, per gu id
    GuStateComponent processState;      // optional
}
```

- Refinement, feeding at a facility, transformation, killer-move channelling, formation upkeep and multi-step transfers are processes.
- An exclusive reservation blocks other exclusive processes and operations that change placement, unless the process permits them.
- At each step the process revalidates participants against their expected revisions. A changed participant triggers the process's declared interruption outcome.
- A single scheduler keyed by `nextDueTick` drives processes and capability due times. Elapsed time is bounded. Contained participants use their container's `TimeScale`.

### 10.4 Multi-Gu interactions

Killer moves, formations and Gu Houses are compositions.

- A **killer move** is an ability Def with role slots. Selection resolves distinct living Gu, checks custody, refinement and condition, reserves them, and commits costs and effects once.
- A **formation** is a process anchored to a Thing or environment, with participant Gu, upkeep costs and an area effect.
- A **Gu House** is a container whose occupants contribute capabilities to a composite. The house itself may be a manifested Thing.

### 10.5 Cultivation interface

The interface reads `GuView`s and asks each capability for `GuActionDescriptor`s that contain a label, availability with a reason, and the operation or process to start. It renders shared framework state such as condition, refinement, placement, processes and costs. It never needs species-specific menus.

## 11. Performance and simulation budget

- Records are plain objects with indexes by ID, projection, container, cultivator, species and uniqueness key. Indexes rebuild from records on load.
- Placement changes are events. There is no periodic validation scan over all records. Native hooks and pure reads detect divergence.
- The scheduler processes due items under a per-tick budget and carries the remainder forward.
- Passive effects are cached per host and recomputed on bond or container change.
- Aggregation into cohorts is the scaling path for Gu that do not need individuality. Species declare which individual state forbids aggregation.
- Tombstones are compact. A retention policy keeps tombstones that guard uniqueness, unreplayed event keys or conservation audits. Others compact after a configurable period into per-origin counters.
- The 120-record benchmark becomes a scaling series of 120, 1,000 and 10,000 records, mixed between contained and manifested, with save size, load time, rebuild and scheduler cost.

## 12. Migration from V1

| V1 record | V2 record |
|---|---|
| `WildPawn`, Active | Living, Active, `Manifested(pawnLoadId, Pawn)` |
| `DormantGround` | Living, Dormant, `Manifested(thingLoadId, Thing)` |
| `DormantInventory` | Living, Dormant, `Manifested(thingLoadId, Thing)`. The holder becomes observed location. |
| `Aperture` | Living, Dormant, `Contained(MortalAperture)`. The transitional aperture identity contract in [section 15](#15-gate-g1-amendments) assigns the container ID. |
| Quarantine whose reason is an ordinary-gameplay mismatch | Re-evaluated. A live comp with the same ID and load ID rebinds. A dead host becomes `Unresolved(HostDead)`. A missing projection becomes `Unresolved(ProjectionMissingOnLoad)`. |
| Quarantine whose reason is evidential, such as duplicates or an unsupported version | Remains quarantined with the legacy reason. |
| Rank, provenance, event key and debit flag | Carried unchanged. |
| `revision` | `custodyRevision`. `stateRevision` = 1. |

- The migrator reads `guIndividualRecordsV1` once, writes only `guIndividualRecordsV2`, and logs a summary. The V1 list is never written again.
- Projection comps read the V1 keys in place. The legacy classes stay loadable.
- Saving with V2 and loading in a V1 build is unsupported and documented.
- Migration runs against disposable copies of V1 checkpoint saves in every custody state, including quarantined ones, before release. See the [regression plan](V2-RECONSTRUCTION-PLAN.md#migration-and-regression-verification-plan).

## 13. Stress tests against future species

These examples test the architecture. They do not define lore mechanics.

| Species or concept | Demands | V2 answer |
|---|---|---|
| **Hope Gu** | Cohort ecology, mass consumption at awakening, no individuality needed | Stays a cohort. Awakening is a cohort debit with a recorded consumption. Individuals materialize only when needed. |
| **Moonlight Gu** | Refined attack ability, essence cost, targeting | `ActivatedAbility` with cost and target workers. Contained in an aperture with no projection. |
| **Wisdom Gu** | Rare, possibly unique, assists thought, may refuse the host | Uniqueness scope. A `PassiveEffect` or process that grants deduction support. Autonomy and refusal via a refinement contest. |
| **All-Out Effort Gu** | Temporary large boost with a backlash afterward | `ActivatedAbility` that starts a process with a timed phase and a backlash step. |
| **Wolf Enslavement Gu** | Persistent control of many beasts | `Relationship` capability linking the Gu to native animal Pawns. Links are records validated when a beast dies or leaves. |
| **Fixed Immortal Travel Gu** | Immortal rank, unique, moves the host between places | Uniqueness. An ability that requests a host transfer through a `Transit` container and the environment route graph. |
| **Dog Shit Luck Gu** | Passive probabilistic effect, possibly negative | `PassiveEffect` that modifies event outcomes through a narrow hook. No tick. |
| **Myriad Self Immortal Gu** | Creates many temporary selves | Process that creates temporary individuals or lightweight summons with provenance and terminal cleanup on expiry. |
| **Unique Gu** | One at a time | Uniqueness ledger and release policy. |
| **Consumable Gu** | Spent on use | `Consumable` with a `Consumed` outcome. |
| **Passive Gu** | Works while refined | Cached effects keyed by the bond. |
| **Autonomous Gu** | Acts on its own | `Autonomy` while manifested. Abstract behavior via processes while contained. |
| **Ecological Gu** | Lives in a habitat, reproduces | Cohorts with habitat adapters. Materialization and aggregation. |
| **Gu with no activated ability** | Pure material or symbiote | Valid with zero capabilities. |
| **Killer move** | Several Gu at once | Role-slot ability with reservations. |
| **Formation** | Area effect sustained over time | Anchored process. |
| **Gu House** | Composite of many Gu | Container whose occupants contribute capabilities. |

## 14. Decisions requested

The [reconstruction plan](V2-RECONSTRUCTION-PLAN.md#owner-decisions-and-approval-gates) lists every owner decision. Approving this RFC means approving the record model, observed physical location, the component-based manifestation, the separation of unresolved and quarantine, and V1-to-V2 migration. Gameplay policies such as host loss, map-removal outcomes and tombstone retention remain separate decisions.

## 15. Gate G1 amendments

The owner approved Phase 1 with these requirements. They take precedence over earlier sections.

### 15.1 Native physical movement

Native movement of an authorized manifestation does not advance its custody revision. Every action and persistent process revalidates at commit the current physical location, accessibility, holder, refinement allegiance and other authorization conditions that apply to it. An unchanged revision proves only that the registry has not rebound the Gu. It does not prove the Gu is still reachable or permitted.

### 15.2 Read purity

Inspection, menu generation and read-model queries never mutate canonical records and never enqueue reconciliation work. Reconciliation runs from native lifecycle events and a scheduled sweep. Simulation results are identical whether or not the player opens a UI.

### 15.3 Gear integration

The V1 Gear guard stays installed until the V2 manifestation model passes the existing and expanded ordinary inventory fixtures. It is then removed. Ordinary right-click pickup stays available unless a better verified interaction replaces it.

### 15.4 Unresolved states

A record becomes terminal only after a demonstrated terminal event and an approved Phase 2 policy. Phase 1 records demonstrated events such as projection destruction, death or map removal as evidence on an unresolved record. A temporarily missing species Def, host or projection is absence of evidence and is never terminal. Unique Gu keep their uniqueness reservation while unresolved.

### 15.5 Transitional aperture identity

Phase 1 must migrate V1 aperture custody before Phase 3 builds cultivator and aperture records. The minimal durable contract is:

- `Hediff_Aperture` saves a new `guApertureId` GUID. It is assigned when first needed and never changes.
- A contained Gu saves the container kind `MortalAperture`, the container ID (`guApertureId`) and an anchor hint (the host Pawn load ID).
- Resolution finds the host by the anchor hint, then the aperture Hediff with the matching ID. Host status (free colonist, prisoner, NPC) is checked by actions, not by binding.
- Migration matches a V1 aperture record's saved Hediff load ID on its saved host and assigns the ID once.
- Phase 3 `ApertureRecord` and `CultivatorRecord` adopt the same `guApertureId` without migrating Gu records again.

Capacity stays one Gu per aperture in the Phase 1 developer fixture. The container data model already supports many occupants. Final capacity is a later balance decision.

## 16. Phase 2 lifecycle as implemented

The owner authorized Phase 2 on 2026-11-22 ([decisions](V2-PHASE2-OWNER-DECISIONS.md)). This section records how the implementation realizes sections 3.2, 3.4, 9 and 11. It takes precedence over earlier sections where they differ.

### 16.1 Outcomes

| Source | Result |
|---|---|
| Authorized wild Pawn killed | `Terminal(Died)` |
| Authorized dormant Thing destroyed, in any destroy mode, outside a holder or map teardown | `Terminal(Destroyed)` |
| Explicit operation by a cultivator, process or sacrifice | `Terminal(DeliberatelyDestroyed / Consumed / Sacrificed)` with an idempotent event key |
| Operator decision on an unresolved record | `Terminal(Lost)`, actor `operator` |
| Map removal, world-pawn GC, discard, holder teardown | Never terminal. The species preservation policy decides (16.3). |
| Missing species, host, aperture or projection | `Unresolved` as before |

A record is ended and unbound before its native object is retired, so the retirement is never reported as a second, physical ending. Terminal records keep identity, species, rank, provenance, creation key, cohort debit, uniqueness key, outcome, end tick, end key and actor. A species may declare a `terminalEffectWorker` for residue. It runs once after commit and cannot change the record.

### 16.2 Deceased-host custody

Host death no longer unbinds contained Gu. The binding rule is the exact aperture on its anchored host, which may be dead (a killed Pawn is natively `Destroyed` until resurrection). Actions that need the host to act still require a living host. Resurrection therefore needs no recovery step, and any extraction or destruction before resurrection is already committed and cannot be reversed. Destroying the corpse sets `Unresolved(HostCorpseDestroyed)` with the corpse's captured location until owner-approved species host-loss defaults exist. A discarded living host is `Unresolved(HostUnavailable)` with `Discarded` evidence.

### 16.3 Preservation policy and world locations

`GuSpeciesDef.abandonmentPolicy` is `Individual`, `ReturnToPopulation` or `RetireFromSimulation`. A uniqueness key, a refinement bond or a rank at or above `individualPreservationMinRank` (default 6) always forces `Individual`. A missing species also preserves the individual.

- **Individual** with a known location → `Contained(WorldSite)` keyed by tile and layer, with a structured `GuLocation`. Recoverable on a map at the same tile.
- **RetireFromSimulation** → `GuExistence.Retired`. Not a death. Accounting fields are kept.
- **ReturnToPopulation** → `Unresolved(MapRemoved)` with evidence, because no regional population can yet receive the Gu. An active Gu of this policy stays a retained world Pawn.

Locations are captured before native teardown by a `MapDeiniter.Deinit` prefix, a `Corpse.Destroy` prefix, the projection classes just before destruction, and the comp's last spawned location. Pocket maps use their source map's tile. World-pawn GC retains Pawns that are, hold or host Gu that must keep their identity, until reconciliation abstracts them.

### 16.4 Physical Gu and their holders

Gu in a native inventory follow native behavior. When native teardown would destroy a dead or vanished holder's inventory (corpse destruction, non-lethal Pawn destruction), authorized Gu are placed where the body stood if that is a live map. Otherwise their end is recorded as the holder's teardown and handled by 16.3.

### 16.5 Retention and compaction

After one in-game year an ended record is compacted. Placement fields, refinement, species state, last location, narrative detail and history are cleared. A retirement keeps its cause. Records are never deleted. Creation keys, terminal keys and cohort debits stay in the record, so replay protection, conservation and uniqueness need no separate ledger. Living records keep their facts and have only narrative detail older than a year summarized. Deleting tombstones would require a compact event ledger. That is an owner decision (Phase 2 report).

### 16.6 Operator recovery

Read-only inspection reports state, observed form, evidence history, rogue candidates and the actions the evidence supports. Repairs require Development Mode, re-check their evidence when they run, and write an audit entry: adopt the sole live rogue candidate of a record whose projection is missing (custody revision advances and the tag is rewritten), declare an unresolved record lost, or lift a quarantine whose contradiction no longer holds. A lifted record becomes unresolved, never directly actionable. Retained V1 transaction failures cannot be lifted.
