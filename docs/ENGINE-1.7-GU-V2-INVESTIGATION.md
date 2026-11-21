# RimWorld 1.7 engine investigation for Gu Framework V2

**Status** — Complete for the V2 planning gate. Evidence was collected on 2026-11-21.
**Environment** — Windows 11 build 26200, RimWorld 1.7.5014 rev1195 (Steam), Harmony 3.0.4 (Workshop 2009463077), .NET SDK 10.0.401, Core only for native runs.
**Method** — The installed `Assembly-CSharp.dll` was decompiled with ILSpy 11.2 into a session scratch directory outside the repository. No decompiled source is committed. Native behavior was exercised with the `V2EngineProbe` live profile.

## Evidence labels

| Label | Meaning |
|---|---|
| **Native-verified** | Observed in the actual 1.7 game process by a disposable fixture in this repository. |
| **Source-derived** | Read from the installed 1.7 assembly's method bodies. Not exercised in a running game. |
| **Prior evidence** | Verified by earlier fixtures recorded in the [0.2.5 journal](IMPLEMENTATION-0.2.5.md). |
| **Hypothesis** | A reasoned expectation that needs a probe before design depends on it. |

## The V2EngineProbe fixture

[`LiveV2EngineProbe.cs`](../tests/ProjectGu.LiveVerification/LiveV2EngineProbe.cs) runs ten ordinary-gameplay scenarios against V1 production code, saves, and reloads in a fresh process. It asserts only that each scenario was set up and reports what happened. A test-only comp is added to the two developer Gu Defs at startup to observe destruction and map removal callbacks and to test whether a component can read V1 save keys.

Run `scripts/verify-priority0-live.ps1 -Profile V2EngineProbe`. The final evidence is under `tests/obj/Priority0-661854abadff4dfb8f28ff6486c83723/V2EngineProbe`. `scripts/verify.ps1 -Focused` passed before the run.

| Scenario | Native behavior | V1 registry response |
|---|---|---|
| Holder downed by damage | The dormant Gu Thing was dropped to the map ground. The colonist stayed alive. | The record still claimed inventory custody. **One `GetInspectString` call quarantined it.** |
| Holder killed | The corpse spawned and the Gu Thing was dropped to the ground. | Quarantined on the next resolve. |
| Holder made a prisoner | The Gu Thing was dropped to the ground. | Quarantined on the next resolve. |
| Holder stripped | The Gu Thing was dropped to the ground. | Quarantined at the next fresh-process load. |
| Holder changed faction | The Gu Thing stayed in the same inventory. The holder was no longer a free colonist. | Quarantined on the next resolve. |
| Aperture host killed | The aperture Hediff stayed on the dead Pawn. | Quarantined immediately by the Hediff death callback. |
| Aperture host resurrected | `ResurrectionUtility.TryResurrect` succeeded. The same Hediff instance and load ID remained. | **Stayed quarantined.** The occupant did not resolve. |
| Dormant Gu destroyed on the ground | One comp `PostDestroy` call. | The record was unaware until the next resolve, then quarantined. |
| Wild Gu killed | A corpse spawned. | Quarantined on the next resolve. |
| Wild Gu passed to the world | `WorldPawnGC.RunGC` discarded it. | Quarantined on the next resolve. |
| Unprotected map removed | The dormant Thing became `Discarded` with zero `PostDestroy` calls and one comp `Notify_MapRemoved`. The active Gu Pawn became a world pawn and was not yet discarded. | Both quarantined on the next resolve. |
| Fresh-process reload | 11 records loaded. | All 11 were quarantined. |
| Comp schema probe | The comp attached to 7 of 7 saved V1 projections. | The comp read `guIndividualId` and `guBindingRevision` from the same Thing node on all 7. |

## Findings

### F1. Ordinary Pawn events move inventory through native paths

- **Native-verified.** Downing, death, imprisonment and stripping move every inventory Thing out of the Pawn without any Project GU callback.
- **Source-derived.** `Pawn_HealthTracker.MakeDowned` calls `Pawn.DropAndForbidEverything(keepInventoryAndEquipmentIfInBed: true)` for a spawned Pawn. A Pawn downed in bed keeps its inventory. `Pawn.Kill` calls `DropAndForbidEverything` after placing the corpse, then notifies Hediffs. `Pawn_GuestTracker.SetGuestStatus(Prisoner)` calls `DropAndForbidEverything`. `Pawn.Strip` drops in place, or moves inventory to other caravan members when the Pawn is in a caravan. When the Pawn is inside an enclosed container, `DropAndForbidEverything` transfers inventory into that container instead.
- **Source-derived.** `Pawn.Destroy` with a mode other than `KillFinalize` calls `inventory.DestroyAll()`. `Pawn.Discard` and corpse destruction call `Corpse.PostCorpseDestroy`, which destroys equipment, inventory and apparel and calls `Hediff.Notify_PawnCorpseDestroyed`.

**Consequence.** Exact-holder custody cannot be maintained by intercepting individual UI actions. V1 patched the Gear drop button, but every other native path still produces a mismatch. V2 must treat the engine as authoritative for the physical location of a manifested Gu.

### F2. Contained Things receive no transfer notification

- **Source-derived.** `ThingOwner.NotifyAdded` and `NotifyRemoved` are `protected virtual`. They notify hard-coded owners (inventory, apparel, equipment trackers, caravans, transporters) and any owner implementing `IThingHolderEvents<T>`. The contained Thing is not notified. `Thing` and `ThingComp` expose no holder-changed hook.
- **Source-derived.** `ThingWithComps.Destroy` calls every comp's `PostDestroy(mode, previousMap)` after the base destruction. `Thing.Kill` is `Destroy(KillFinalize)`. A destroyed Thing inside a holder calls `holdingOwner.Notify_ContainedItemDestroyed`.

**Consequence.** Destruction is observable from a comp. Movement between holders is not, unless Harmony patches the protected ThingOwner notifications, which run for every item transfer in the game. V2 should not need to observe movement at all for manifested Gu.

### F3. Map removal is not destruction

- **Native-verified.** Removing an unprotected pocket map left the dormant Gu Thing `Discarded` with no `PostDestroy`. The comp received `Notify_MapRemoved` only because the probe set `ThingDef.notifyMapRemoved`.
- **Source-derived.** `Game.DeinitAndRemoveMap` runs `MapDeiniter.Deinit`, which despawns every map Pawn and passes it to the world. Hostile-map colonists are kidnapped. Things are notified through `Notify_MyMapRemoved`, which marks them discarded unless a Pawn holds them, and the map's spawned-thing lists are cleared. Items inside a Pawn's inventory travel with the Pawn into the world.
- **Prior evidence.** The underground lifecycle guards block supported removal paths for protected levels and their last surface access. They do not cover arbitrary maps such as temporary sites, enemy bases or unregistered pocket maps.

**Consequence.** V2 needs an explicit map-removal outcome that is neither destruction nor quarantine. Gu Defs need `notifyMapRemoved` set to `true`.

### F4. World-pawn GC ignores mod references

- **Native-verified.** An unowned wild Gu Pawn passed to the world was discarded by one GC pass.
- **Source-derived.** `WorldPawnGC.GetCriticalPawnReason` keeps colonists, bonded animals, faction leaders, kidnapped Pawns, caravan and transport pod members, Pawns for sale, forcefully kept Pawns, spawned Pawns, Pawns with corpses, play-log or tale subjects, quest-reserved Pawns, and Pawn sources. It does not check mod-held references. It also randomly keeps up to ten humanlike Pawns. `WorldPawns.ForcefullyKeptPawns` is saved by reference but cleared by `RemovePawn` whenever a Pawn leaves the world.

**Consequence.** NPC cultivators, prisoners who left, and world-resident active Gu need a retention rule. A Harmony postfix on the private `GetCriticalPawnReason` that returns a Project GU reason for Pawns with significant records is more robust than re-pinning after each world entry. A retained world Pawn still costs memory and save size, so retention must be selective.

### F5. Hediffs survive death and resurrection

- **Native-verified.** The aperture Hediff remained on the dead host. After resurrection, the same instance and load ID were present.
- **Source-derived.** Hediffs receive `Notify_PawnDied`, `Notify_Resurrected` and `Notify_PawnCorpseDestroyed`. `Pawn.Kill` notifies Hediffs after the corpse is created and inventory is dropped.
- **Prior evidence.** Saved Pawn and Hediff references resolved across passage travel and fresh-process reload. A same-Def replacement receives a new load ID.

**Consequence.** Host death is a recoverable state while the corpse exists. Aperture identity should not depend on a Hediff load ID when a deliberate replacement, such as rank advancement or ascension, must keep its contents.

### F6. Saved Thing classes are bound by name

- **Source-derived.** `ScribeExtractor.SaveableFromNode` instantiates the saved `Class` attribute through `BackCompatibility.GetBackCompatibleType`, not the current `ThingDef.thingClass`. A missing class logs an error and falls back to a base type. The conversion chain is an internal list of game-version converters.
- **Source-derived and native-verified.** `ThingWithComps.ExposeData` calls `InitializeComps` from the current Def during `LoadingVars`, then each comp's `PostExposeData` in the same XML node. The probe comp read V1 keys from 7 of 7 legacy projections.

**Consequence.** `ProjectGu.Pawn_GuWildProjection` and `ProjectGu.Thing_GuDormantProjection` must remain loadable types for as long as V1 saves are supported. A V2 identity comp can migrate their data in place without a custom converter.

### F7. All maps share one clock

- **Source-derived.** `TickManager.DoSingleTick` calls `MapPreTick`, `MapPostTick` and world ticks for every loaded map once per tick. `TickRateMultiplier` is the global game speed. There is no native per-map time rate.
- **Prior evidence.** The independent underground provider caps loaded levels at eight because native map indexing uses a signed byte. Loaded levels tick continuously.

**Consequence.** Independent internal time flow for an immortal environment can be simulated abstractly while the environment is not a loaded map. A loaded native map runs at the shared rate unless a future high-risk engine patch proves otherwise.

### F8. V1 read paths mutate canonical state

- **Native-verified.** Rendering the inspect string of a mismatched dormant Gu quarantined its record.
- **Source-derived from Project GU.** `GuIndividualRegistry.Resolve`, `ResolveApertureBinding` and `ResolveApertureOccupant` quarantine and unbind records. `InspectProjection`, `CanOrderDormantPickup` and float-menu construction call them.

**Consequence.** UI drawing, float menus and inspection can permanently change saves. V2 read operations must be pure and queue discrepancies for a budgeted reconciliation pass.

### F9. Native precedent for lifting map content

- **Source-derived.** Odyssey's `Gravship` and `GravshipPlacementUtility.PlaceGravshipInMap` capture a structure from one map and place it on another. This is a DLC feature.
- **Hypothesis.** The Gravship path may inform future environment materialization. It does not establish a Core-only way to unload a map and restore it later.

## Earlier evidence that remains valid

- Explicit `WorldPawns.PassToWorld(..., Discard)` retires an empty, unowned Core Pawn without kill calls, corpses or tales. It clears relations and notifies logs, tales, quests and memories. [Native retirement limits](IMPLEMENTATION-0.2.5.md#native-retirement-limits)
- An unstackable Thing keeps its identity through inventory merges, cross-map travel and reload.
- `GameComponent.LoadedGame` runs after map and holder references resolve.
- Record-only aperture custody for 120 records added about 499 bytes per record to the save and an estimated 0.34 µs per tick. [Benchmark](GU-FRAMEWORK-0.2.5-ACCEPTANCE.md#performance-results)

## Open questions

| Question | Why it matters | Proposed probe |
|---|---|---|
| Can an opaque species-state node survive load and resave when its add-on class is missing? | Preserves add-on Gu data when a mod is removed. | Load a save whose state node names a missing class, capture the raw XML in `LoadingVars`, resave, and diff. |
| Does an unspawned staged Pawn satisfy placement preflight without the V1 tick-suppressing subclass? | Determines whether a comp-only Pawn projection can replace `Pawn_GuWildProjection`. | Stage, validate and roll back an ordinary animal-class Gu Pawn with a comp only. |
| Does a GC postfix keep a retained world Pawn's inventory and Hediffs across mothballing and reload? | NPC cultivator and absent active Gu retention. | Pass an NPC with an aperture and inventory Gu to the world, run GC, mothball, reload. |
| What do caravans, transport pods and trading do with a Gu Thing? | Common physical routes. | Load a Gu Thing into a transporter and caravan, travel, unload, reload. Tradeability stays `None` for now. |
| How do Biotech, Royalty, Anomaly and Odyssey alter death, downing and discard callbacks? | V1 disables release and dormancy under Anomaly. | Repeat `V2EngineProbe` with each DLC once V2 hooks exist. |
| Can a loaded map be serialized, disposed and restored later? | Environment dematerialization. | Separate feasibility probe after the environment RFC is approved. |
