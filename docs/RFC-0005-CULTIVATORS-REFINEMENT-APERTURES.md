# RFC-0005 — Cultivators, refinement and apertures

**Status** — Proposed. Awaiting owner approval. Production refinement is not authorized.
**Target** — RimWorld 1.7 Waylith / .NET 10
**Depends on** — [RFC-0004 Gu Framework V2](RFC-0004-GU-FRAMEWORK-V2.md)
**Evidence** — [Engine investigation](ENGINE-1.7-GU-V2-INVESTIGATION.md)

## 1. Owner-approved requirements

- Physical possession does not imply refinement ownership.
- Dormancy does not imply refinement.
- Ordinary mortal-aperture storage requires appropriately refined Gu.
- An unrefined Gu can enter a mortal aperture through a distinct refinement process.
- Refinement may involve an ongoing contest, costs, interruption and outcomes.
- Mortal apertures hold many individual Gu without physical Pawn projections.
- Refinement allegiance does not depend only on the current physical holder or aperture.
- NPC cultivators, prisoners, enemies and other non-colonist actors must eventually be supported.
- Mortal-to-immortal ascension needs an explicit migration and lifecycle model.

## 2. Current implementation

| Element | Today | Limitation |
|---|---|---|
| `GuPawnRecord` in `GuWorldComponent` | Keyed by a Pawn reference. Saves latent aptitude, awakening state and physique. Humanlike only. A 600-tick scan creates records for spawned Pawns. | No durable ID. The record disappears from the save when the Pawn reference goes null. The scan creates a record for every humanlike Pawn it sees. |
| `Hediff_Aperture` | Owns aptitude, rank, essence and physique under `guAptitude`, `guRank`, `guEssence` and `guExtremePhysiqueId`. | Aperture identity is the Hediff load ID. Replacing the Hediff loses identity. |
| V1 aperture custody | One slot per Hediff. The host must be a free colonist. Death quarantines permanently. | [Verified](ENGINE-1.7-GU-V2-INVESTIGATION.md#f5-hediffs-survive-death-and-resurrection) that resurrection keeps the same Hediff and V1 still refuses to recover. |

## 3. Cultivator identity

```csharp
public sealed class CultivatorRecord : IExposable      // saved under guCultivatorRecordsV1
{
    string cultivatorId;            // GUID, durable
    Pawn pawn;                      // Scribe reference; null once the Pawn is discarded
    string pawnLoadIdAtLastSave;    // diagnostic link that survives a null reference
    string cachedName;              // for tombstones, inheritance and UI
    CultivatorStatus status;        // Mortal, AwakeningFailed, Awakened, Dead, Ascended, Lost
    int latentAptitude; string extremePhysiqueId;
    List<string> apertureIds;       // durable aperture records owned by this cultivator
    int stateRevision;
}
```

- **Durable ID.** Bonds, apertures, environments and inheritance refer to `cultivatorId`, never to a Pawn reference or load ID.
- **Lazy creation.** A record is created when it is needed: at awakening, when a Pawn gains an aperture, when a refinement bond names the Pawn, or when an NPC is generated as a cultivator. The 600-tick discovery scan remains only for the 0.2.x mortal status markers until V2 migration replaces it.
- **Faction-agnostic.** Colonists, prisoners, slaves, guests, enemies and world Pawns use the same record. Policies such as who may give orders are gameplay rules, not record validity.
- **Retention.** A Harmony postfix on `WorldPawnGC.GetCriticalPawnReason` keeps world Pawns whose cultivator record is *significant*. Examples are an aperture with Gu, an environment, refinement bonds or a quest role. Insignificant mortal records do not keep a Pawn alive. [Finding F4](ENGINE-1.7-GU-V2-INVESTIGATION.md#f4-world-pawn-gc-ignores-mod-references)
- **Migration.** `guPawnRecords` load as before. Each becomes a `CultivatorRecord` with a new ID, the same Pawn reference and the same fields. Mortal-status Hediffs are unchanged.

## 4. Aperture identity

```csharp
public sealed class ApertureRecord : IExposable        // saved under guApertureRecordsV1
{
    string apertureId;              // GUID, durable
    string ownerCultivatorId;
    ApertureTier tier;              // Mortal, Immortal
    string nativeHediffLoadId;      // last bound Hediff, for diagnostics and migration
    string environmentId;           // Immortal tier: the environment record (RFC-0006)
    ApertureState state;            // Open, HostDead, Sealed, Destroyed, Ascended
    int capacity;                   // Mortal tier: derived from rank and aptitude by policy
}
```

- `Hediff_Aperture` gains one new saved key, `guApertureId`. All existing keys remain. A Hediff without the key is matched once during migration, using the V1 record's saved Hediff load ID or the host's only aperture.
- **Resolving the native anchor.** Find the owner cultivator's Pawn, then the `Hediff_Aperture` whose `guApertureId` matches. Rank advancement may replace or edit the Hediff and keep the ID. A same-Def replacement made by unrelated code without the ID starts a new, empty aperture, as in V1.
- **Capacity.** A mortal aperture holds many Gu. The default capacity policy uses rank and aptitude, and refinement or species effects may consume capacity. The numbers are a balance decision.
- **Essence and rank stay on the Hediff.** Their save keys are unchanged. A later slice may move them into `ApertureRecord` if the immortal tier needs them off-Pawn.

## 5. Refinement allegiance

Refinement is a relationship between one Gu and one cultivator, saved on the Gu record (RFC-0004 §3).

```csharp
public sealed class GuRefinement : IExposable
{
    string cultivatorId;            // null while unrefined
    RefinementState state;          // Unrefined, InProgress, Refined, Contested, Severed
    float hold;                     // 0..1 strength of the refiner's will imprint
    int sinceTick;
    string processId;               // active refinement or contest process
    List<RefinementClaim> claims;   // competing claimants during a contest
}
```

- **Independent of custody.** A refined Gu can lie on the ground, sit in another person's inventory, live in an environment or be stolen. It stays bound to its refiner until a severing outcome. A Gu in someone's aperture is always either refined by that cultivator or in an active refinement process with them.
- **Independent of biology.** A dormant Gu can be unrefined. An active Gu can be refined.
- **Effects follow the bond.** Passive effects and ability access require both the bond and an access route. The default access route is the refiner's own aperture. A species may allow use from inventory or at range. That is a capability policy.

### 5.1 The refinement process

Refinement is a `GuProcess` (RFC-0004 §10.3).

1. **Start.** The cultivator chooses an unrefined or contested Gu in reach. Preflight checks condition, rank versus cultivator rank, essence or material costs, and competing claims. The Gu is reserved exclusively.
2. **Entry.** For mortal-aperture refinement, the Gu enters the aperture under the process. `Unmanifest` to `Contained(MortalAperture)` with the process ID. This is the "distinct action" the owner approved.
3. **Contest steps.** Each due step spends cost and moves `hold`. The species' will, rank difference, condition and wild resistance shape the result. Steps are bounded elapsed-time updates.
4. **Interruption.** Host downed, killed or out of essence, a competing claim, or the Gu becoming unavailable triggers the process's declared outcome. Spent costs stay spent. Unspent costs return.
5. **Outcomes.** `Refined` sets the bond and ends the reservation. `Failed` follows species policy, which might eject the Gu, damage it, harm the cultivator or destroy it. `Abandoned` returns the Gu to its prior placement where possible.

Outcome probabilities, costs and backlash are owner and balance decisions.

## 6. Host loss and inheritance

The framework provides the states and hooks. The lore defaults require owner approval.

| Event | Framework state | Proposed default for owner review |
|---|---|---|
| Host killed | Aperture `HostDead`. Contained Gu become `Unresolved(HostDead)`. | Wait while the corpse exists. Resurrection restores the aperture and its occupants. |
| Host corpse destroyed or the deadline passes | `HostCorpseDestroyed` | Apply each Gu's `HostLoss` capability: `Die`, `Escape` (manifest as an active wild Gu near the corpse or last tile), or `Remain` (dormant in a sealed remains container). |
| Host Pawn discarded or absent on load | `HostAbsent` | Same as corpse destroyed, at the last known tile. |
| Refiner dies, Gu outside the aperture | Bond becomes `Severed` or persists as an inheritance claim. | Owner decision. |
| Inheritance | A designated heir or clan claims bonds or containers. | Re-refinement at reduced difficulty, or direct transfer for specific species. Owner decision. |

## 7. Non-colonist cultivators

- NPC generation can add an aperture and contained Gu to raiders, traders or clan members through a `CultivatorKindDef` extension. Contained Gu are records, so an NPC with ten Gu costs ten records and no Pawns.
- Capturing an NPC keeps its cultivator record, aperture and bonds. Imprisonment drops physical inventory natively. That is harmless in V2.
- An NPC who leaves the map becomes a world Pawn. Retention follows the significance rule. If GC discards an insignificant NPC, its Gu follow the `HostAbsent` path.
- Who may command, use or extract an NPC's Gu is a gameplay policy for later slices.

## 8. Ascension

Ascension converts a mortal aperture into an immortal aperture, which owns a persistent environment (RFC-0006).

1. A process with explicit preconditions and costs reserves the cultivator, the aperture and its occupants.
2. On commit, the `ApertureRecord` tier becomes `Immortal`, an `EnvironmentRecord` is created with `ownerCultivatorId`, and each occupant moves from `Contained(MortalAperture)` to `Contained(EnvironmentRegion)` in one coordinated operation. Bonds are unchanged.
3. Failure follows a declared outcome, such as death of the cultivator and host-loss handling.
4. The cultivator status becomes `Ascended`. Lore details such as tribulation, aperture expansion and the initial environment are owner decisions.

## 9. Lore and gameplay decisions for the owner

1. Host-death outcomes and whether they vary by species, rank or refinement hold.
2. Refinement costs, contest model, failure consequences and interruption rules.
3. Whether bonds survive the refiner's death and how inheritance works.
4. Mortal aperture capacity rules.
5. How far species may customize host loss, refinement resistance and access routes.
6. When NPC cultivators are generated and how many Gu they carry.
7. Ascension preconditions, failure and the initial environment.
8. Whether physical inventory Gu should stay with a downed Pawn instead of dropping. The engine drops them by default.
