# Phase 3A proposal — durable cultivator identity

**Status** — DRAFT architecture proposal. Owner decisions D3A.1–D3A.4 and benchmark seed control recorded 2026-11-22. Awaiting independent Codex review. Implementation is not authorized until after that review.
**Depends on** — [RFC-0005 §3–4](RFC-0005-CULTIVATORS-REFINEMENT-APERTURES.md#3-cultivator-identity), accepted Phases 1–2, accepted [test infrastructure](TEST-INFRASTRUCTURE.md).
**Goal** — The identity foundation for one fully playable cultivation clan. Clan membership, lineage, refinement bonds, apertures and inheritance will refer to a cultivator by a durable ID, never by a Pawn reference or load ID. Continent-scale simulation is out of scope.

## 1. Scope

| Slice | Contents | Authorization |
|---|---|---|
| **3A (this proposal)** | `CultivatorRecord` with a durable ID, one-time migration from `guPawnRecords`, the record's association with its native Pawn, aperture-to-cultivator links through the existing `guApertureId`, evidenced lifecycle outcomes, significance and safe removal, and benchmark seed control. | Approved in direction (D3A.4). Implementation after design review. |
| 3B | `ApertureRecord`, the multi-occupant mortal aperture with a capacity policy, resurrection restoring occupants. | Separate. |
| 3C | NPC cultivators (D2.5), capture, the 120-hosts-with-10-Gu budget. | Separate. |

3A changes no gameplay. Awakening, aptitude generation, mortal status Hediffs and the development aperture fixture behave as today.

## 2. Owner decisions

| # | Decision (2026-11-22) |
|---|---|
| D3A.1 | **Approved.** `guCultivatorRecordsV1` is the sole authoritative store. `guPawnRecords` is read for one-time migration only. Never reroll existing aptitude, never create duplicate records, never keep two authoritative stores. |
| D3A.2 | **Approved for 3A.** Keep today's aptitude generation and mortal-status behavior for every humanlike Pawn, visitors and raiders included. Do not narrow it in 3A. An ordinary record never forces native world-Pawn retention. |
| D3A.3 | **Conditionally approved.** Significant identities survive native discard with their references. Death, disappearance and world-Pawn GC are distinct, evidenced outcomes. An insignificant record may be removed only with no outstanding references, transactions, conservation requirements or historical-integrity dependencies. |
| D3A.4 | **Approved.** 3A is implemented on its own after final design review. 3B and 3C need separate authorization. |
| Benchmark | **Approved.** Make the benchmark reproducible with a narrow runner change, keep empty and populated variants on matched worlds, and set a new controlled baseline. |

## 3. Current state

- `GuWorldComponent` saves `List<GuPawnRecord>` under `guPawnRecords`: a Pawn reference, latent aptitude, extreme physique, awakening state and a 0.1.x migration flag. No durable ID.
- On load, a record whose Pawn reference resolved to null is dropped silently.
- Every 600 ticks, each spawned, living humanlike Pawn on every map gets a record (one dictionary lookup per Pawn once it exists).
- `Hediff_Aperture` carries a durable `guApertureId` since Phase 1. Contained Gu name it as `containerId` and the host's load ID as `anchorHint`.
- `GuRefinement.cultivatorId` exists on Gu records and is always empty.

## 4. Existing lifecycle handling, reviewed

Read from the mod source and from the RimWorld 1.7.5014 assembly (decompiled locally, not committed).

| Native event | Native path | Existing Project GU handling |
|---|---|---|
| Death of an aperture host | `Hediff.Notify_PawnDied` | `Hediff_Aperture` enqueues `HostDead`. Reconciliation keeps the Gu as deceased-host custody. |
| Aperture Hediff removed | `Hediff.PostRemoved` | `Hediff_Aperture` enqueues `ApertureMissing`. |
| Corpse destroyed | `Corpse.Destroy` → `Corpse.PostCorpseDestroy(pawn, discarded: false)` → `health.Notify_PawnCorpseDestroyed` | Patches on `Corpse.Destroy` (prefix and finalizer) and `PostCorpseDestroy` (prefix, `BeforeHolderTeardown`). `Hediff_Aperture` enqueues `HostCorpseDestroyed`. |
| Death of a world Pawn or a held Pawn, outside a caravan | `Pawn.Kill` makes no corpse and calls `Corpse.PostCorpseDestroy(this)` | The same `PostCorpseDestroy` prefix and Hediff notification. |
| Discard, alive or dead | `Pawn.Discard` → `Corpse.PostCorpseDestroy(this, discarded: true)` | The same prefix with `discarded = true`. `Hediff_Aperture` enqueues `HostCorpseDestroyed` with "discarded" detail when the host is alive. |
| Non-lethal destruction | `Pawn.Destroy(mode != KillFinalize)` | `Pawn.Destroy` prefix (`BeforeHolderVanish`) preserves held Gu. |
| World-Pawn GC | `WorldPawnGC` → `WorldPawns.RemoveAndDiscardPawnViaGC` → `DiscardPawn(p, silentlyRemoveReferences: true)` → `Pawn.Destroy` / `Pawn.Discard` | `GetCriticalPawnReason` postfix (`ProjectGuCustody`) retains hosts and holders of `Individual`-policy Gu. No outcome is recorded for the GC itself. |
| `PassToWorld(…, Discard)` | `WorldPawns.DiscardPawn(p)` with `silentlyRemoveReferences: false` | Same as discard. |
| Resurrection | `ResurrectionUtility.TryResurrect` → `health.Notify_Resurrected` | No hook. The aperture Hediff survives (engine finding F5) and Phase 2 reconciliation derives the result from `pawn.Dead`. |
| Map removal | `MapDeiniter.Deinit` | Prefix and finalizer bracket map teardown. |

**Consequence for the design.** 3A adds no death or resurrection patch. Living and dead are read from the Pawn while the reference exists. Every path that ends a Pawn's existence already passes through the `Corpse.PostCorpseDestroy` prefix, so 3A extends that handler instead of adding a parallel one. The only new patch is a prefix and finalizer on `WorldPawns.RemoveAndDiscardPawnViaGC` that marks the Pawn being collected, because no existing hook distinguishes GC from other discards. `Find.WorldPawns.IsBeingDiscarded` is true for both GC and `PassToWorld(Discard)`.

## 5. Design

### 5.1 Record

```csharp
public sealed class CultivatorRecord : IExposable      // GuWorldComponent, key guCultivatorRecordsV1
{
    string cultivatorId;          // GUID "N", durable, never reused
    Pawn pawn;                    // Scribe reference while the native Pawn exists; cleared at discard
    string pawnLoadId;            // last known load ID, for diagnostics and Gu anchorHint matching
    string cachedName;            // captured at creation and refreshed on save while the Pawn exists
    AwakeningState awakening;     // unchanged semantics: Unattempted, Failed, Awakened
    int latentAptitude; string extremePhysiqueId; bool legacyMigrated;   // unchanged semantics
    List<string> apertureIds;     // guApertureId values this cultivator owns
    CultivatorOutcome outcome;    // None while the Pawn exists; otherwise the evidenced end (5.3)
    int outcomeTick; string outcomeDetail;
    string creationEventKey;      // "legacy:<pawnLoadId>" for migration, "scan:<pawnLoadId>" or "awaken:<pawnLoadId>"
    int stateRevision;
}
```

Living and dead are not stored. They are read from `pawn.Dead` while the reference exists, so resurrection needs no handling in the record. `Ascended` and environment fields wait for Phase 8.

### 5.2 Pawn association

- **Forward.** A Scribe reference to the Pawn, as today.
- **Reverse.** `Dictionary<Pawn, CultivatorRecord>`, `Dictionary<string, CultivatorRecord>` by cultivator ID and by aperture ID, rebuilt once at load (§7).
- **No new Pawn components.** A comp on the Human race needs an XML patch with compatibility risk. The aperture Hediff's `guApertureId` is a second, Pawn-side link for every awakened cultivator, enough to detect a broken or duplicated association.
- **Aperture links.** `CreateAperture` and migration add the aperture ID to the owner's `apertureIds`. A load-time check reports an aperture ID claimed by two records or found on a Pawn other than its owner. It never merges or reassigns automatically.
- **Faction-agnostic.** Colonists, prisoners, slaves, guests, enemies and world Pawns use the same record. Faction change, imprisonment, downing and map travel never change identity.

### 5.3 Evidenced lifecycle outcomes

The extended `PostCorpseDestroy` handler and the GC marker record the outcome when a Pawn's existence ends. Earlier observations are kept, so a dead Pawn later collected by GC shows both.

| Outcome | Evidence |
|---|---|
| `None` | The Pawn exists. Alive or dead is read from it. |
| `BodyDestroyed` | `PostCorpseDestroy` with `discarded = false` on a dead Pawn: corpse destroyed, or death with no corpse. The Pawn object may still exist as a dead world Pawn. |
| `DiscardedDead` | Discard of a dead Pawn outside GC. |
| `Disappeared` | Discard of a living Pawn outside GC. Not a death. |
| `CollectedByGc` | Discard inside `RemoveAndDiscardPawnViaGC`, with alive or dead noted in the detail. |
| `ReferenceLost` | The Pawn reference loaded as null with no recorded outcome. A path this design does not observe. Reported, never treated as death. |

### 5.4 Significance, retention and removal

Two separate questions.

**Identity significance** decides whether a record must survive. A record is significant when any of these holds:

1. A living or tombstoned Gu record names one of its aperture IDs as `containerId`, or its `pawnLoadId` as the `anchorHint` of a contained Gu.
2. A Gu record's `refinement.cultivatorId` names it. None exist until bonds land.
3. An active transaction or awakening involves its Pawn.
4. A later slice marks it, for example clan membership, an obligation, provenance or inheritance. 3A provides the flag and the check, and no 3A code sets it.

A significant record keeps its ID, name, aptitude, apertures and outcome when its Pawn is discarded. It becomes an identity tombstone.

**Native Pawn retention** decides whether the engine keeps the Pawn object. 3A does not extend it. The existing `ProjectGuCustody` rule already keeps hosts and holders of `Individual`-policy Gu. Ordinary records, and significant records whose Gu do not need the Pawn, never keep a Pawn alive (D3A.2). Bonds, provenance and inheritance refer to the cultivator ID and do not need the Pawn.

**Removal of an insignificant record** happens only after its Pawn is discarded, when none of conditions 1–4 hold, no Gu history entry or tombstone needs it for integrity, and no conservation count depends on it. Today's insignificant records carry no such dependencies. The check runs at discard and again at load, and a refused removal is reported.

### 5.5 Existing-save migration

Runs once, after both `GuWorldComponent` and `GuIndividualRegistry` have loaded, when `guCultivatorRecordsV1` is absent. A second load finds the new key and does nothing.

1. Each valid `GuPawnRecord` becomes a `CultivatorRecord` with a new ID, the same Pawn reference and identical aptitude, physique, awakening state and migration flag.
2. Duplicate records for one Pawn keep the first, as today, and report the rest. No duplicate cultivator record is ever created.
3. A record whose Pawn resolved to null is checked against contained Gu `anchorHint` values. With no match it is dropped, as today, and reported. With a match it becomes a `ReferenceLost` tombstone, since its aptitude cannot be tied to a Pawn but its identity is needed.
4. Every `Hediff_Aperture` on a recorded Pawn gets `guApertureId` if missing, and the ID is added to `apertureIds`.
5. A Pawn with an aperture but no record (0.1.x saves) uses today's `GetOrCreate` legacy path, which reads aptitude and physique from the Hediff. This needs one pass over `PawnsFinder.All_AliveOrDead` during migration only.
6. Migration never rerolls aptitude and never adds or removes Hediffs.

The migrated data reaches disk on the next save. Original saves are never modified. Every migration source is copied to a disposable root with hashes checked before and after.

## 6. Risks

### 6.1 Native Pawn retention

- **Retaining too much.** Each retained world Pawn costs memory and save size and is never collected. 3A adds no retention reason, so the retained set stays exactly as today. A test checks that an insignificant world Pawn with a record is still collected.
- **Retaining too little.** A significant identity whose Pawn is collected keeps only the tombstone. That is intended for bonds, provenance and inheritance. Gu custody that needs the Pawn keeps the existing retention.
- **GC timing.** GC runs in passes. The unreproduced N=6 V2EngineProbe case may involve GC finishing near a save. The new GC marker records outcomes inside `RemoveAndDiscardPawnViaGC` only, so a save between passes sees either the live Pawn or the recorded outcome.
- **Unobserved removal.** A mod or engine path that drops a Pawn without `Pawn.Discard` leaves a null reference on load. The record becomes `ReferenceLost` instead of vanishing, and the report names it.
- **Discard of our own projections.** Project GU discards wild Gu projection Pawns itself. They are not humanlike and never have cultivator records. The handler ignores Pawns without a record at the cost of one dictionary lookup.

### 6.2 Save migration

- **Failure during migration.** If migration throws, the sole new store could be empty and the next save would drop `guPawnRecords`, losing every aptitude. Mitigation: migration builds the new list off to the side and swaps it in only on success. On failure the legacy list stays the in-memory store and keeps saving under `guPawnRecords`, and the failure is reported. Only one store is authoritative at any time.
- **Downgrade to a pre-3A V2 build.** That build ignores `guCultivatorRecordsV1`, finds no `guPawnRecords`, and its scan would reroll aptitude for every Pawn. V2 saves are already documented as not loading in V1. 3A release notes must also state that 3A saves are not supported by earlier V2 builds.
- **Component load order.** Linking apertures needs the Gu registry loaded. Migration and linking run from `LoadedGame`, after every component's `PostLoadInit`, not from `GuWorldComponent.ExposeData`.
- **Unsaved migration.** IDs are random. If the game exits before saving, the next load migrates again with new IDs. Nothing persisted refers to the unsaved IDs, so this is harmless.
- **Conflicting aperture IDs.** A mod copying Hediffs could put one `guApertureId` on two Pawns. Migration reports it and links the first.
- **Missing or removed races.** A Pawn of a removed race loads as null and follows step 3.

## 7. Per-load indexing cost

3A adds load work. The expected cost, to be confirmed at C4:

| Work | Complexity | Expected cost |
|---|---|---|
| Scribe load of cultivator records | One record per humanlike Pawn ever registered, replacing the current `GuPawnRecord` list | Each record grows by about 100–150 B of XML for the ID, name, apertures and keys. Load time scales with that size, roughly as `guPawnRecords` does now. |
| Reverse indexes | O(C + A) dictionary inserts for C records and A aperture IDs | Low hundreds of microseconds per thousand records. The Phase 2 registry rebuild of 120 Gu records measured 584–708 µs. |
| Association checks | One Hediff lookup per awakened cultivator | Proportional to awakened cultivators, not to all Pawns. |
| Significance index | One pass over Gu records to collect referenced aperture IDs, anchors and bonds | O(G) for G Gu records, done with the Gu registry's existing load pass where possible. |
| Migration only | One pass over all Pawns alive or dead, plus the above | Once per save, at its first 3A load. |

No world scan runs on ordinary loads. The 600-tick scan is unchanged. No per-tick work is added. The benchmark's unresolved load-time difference stays a separate investigation, and 3A must not add a scan that scales with records times world size.

## 8. Benchmark seed control

Approved as a narrow runner and fixture change, done first as checkpoint C0.

- The runner passes a fixed seed to the benchmark's `new` stage only, for example `-guperfseed=<seed>`. The fixture applies it to world generation and the starting tile.
- Empty and populated variants are already written from that one generated world in the same process, so they stay matched. C0 checks it by comparing the non-Gu parts of both saves.
- Whether quicktest world generation and tile selection are fully determined by the seed string must be verified first. Two runs with the same seed must produce the same thing count and save size before the change is accepted.
- A new controlled baseline is recorded on the Phase 2 code and the seeded world. Earlier random-world results are not compared directly.
- Other profiles keep random worlds.

## 9. Checkpoint-based verification

Each checkpoint is committed separately with an evidence folder and stops on failure. Native campaigns use `-Parallel 4`, the benchmark runs alone. Independent Codex review is requested at C2 and C4.

| Checkpoint | Evidence | Labels |
|---|---|---|
| **C0 Seeded benchmark** | Two seeded runs on the Phase 2 code produce identical worlds. Controlled baseline recorded. | Native benchmark |
| **C1 Schema and migration logic** | Managed tests: round trip of every field; migration of `GuPawnRecord` fragments with null Pawn (with and without a Gu anchor), duplicate Pawn, out-of-range aptitude, missing physique, the 0.1.x aperture-only case; migration failure keeps the legacy store; idempotent second load; significance and removal rules. `scripts/verify.ps1`. | Stub-based tests, actual-assembly compilation |
| **C2 Native identity fixture** | New `CultivatorIdentity` profile in fresh processes. Awakening keeps one ID across two fresh-process reloads. Imprisonment, faction change, downing and world travel keep it. Death reads as dead, and resurrection reads as alive with the same aperture link and no hook. Corpse destruction, discard of a living Pawn, `PassToWorld(Discard)` and world-Pawn GC each record their own outcome. An insignificant world Pawn is collected and its record removed. A significant identity survives as a tombstone across reload. The retained Pawn set matches the Phase 2 rule exactly. | Native fixture |
| **C3 Existing-save migration** | The seven V1 saves, the Phase 2 fixture saves, both historical copies, and the owner's acceptance colony if provided. Records match the legacy fields one to one, with no rerolled aptitude. Gu migration counts (17/12/12) unchanged. A second load is a no-op. Every source hash unchanged. | Native fixture, migration |
| **C4 Regression and cost** | The 12 Gu fixtures plus the regression set at N=4. Seeded benchmark with cultivator records against the C0 baseline, including load time and the per-load index cost of §7. | Native fixtures, benchmark |

## 10. Items to verify before or during implementation

- The exact 1.7 signatures of `WorldPawns.RemoveAndDiscardPawnViaGC` and `Corpse.PostCorpseDestroy`, and whether any other engine path discards Pawns without `Pawn.Discard`.
- That `GameComponent.LoadedGame` runs after every component's `PostLoadInit` in Waylith.
- That the quicktest world and starting tile are reproducible from a seed (C0).
