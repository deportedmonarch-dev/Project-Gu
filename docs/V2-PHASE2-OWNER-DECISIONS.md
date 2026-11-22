# Gu Framework V2 — Phase 2 owner decisions

**Status** — Recorded 2026-11-22 from the owner's consolidated Phase 2 authorization, given after review of Phase 1 at `f0d4683`. Phase 2 implementation is authorized within the boundaries below. Items marked *future* record intended gameplay. They do not authorize implementation.
**Related** — [Reconstruction plan](V2-RECONSTRUCTION-PLAN.md) · [RFC-0004](RFC-0004-GU-FRAMEWORK-V2.md) · [RFC-0005](RFC-0005-CULTIVATORS-REFINEMENT-APERTURES.md) · [RFC-0006](RFC-0006-PERSISTENT-IMMORTAL-ENVIRONMENTS.md) · [Phase journal](IMPLEMENTATION-V2.md)

## 1. Terminal Gu lifecycle — approved for Phase 2

- Verified biological death, physical destruction, deliberate destruction by a cultivator, consumption and other demonstrated terminal events produce explicit lifecycle outcomes.
- These are not terminal: a missing species Def, an unavailable projection, a missing host, map removal, world-pawn garbage collection and contradictory save evidence. Absence of evidence never establishes death.
- A terminal record keeps its canonical identity, relevant provenance, cause and idempotent event key.
- Intentional retirement of a projection during a valid transaction is not destruction.
- Species may later define residue, materials or other effects of destruction. Not every Gu leaves a product.

## 2. Gu Master death and resource denial

**Physical inventory Gu — approved.** Gu carried in a native inventory survive their holder's death unless they are independently killed or destroyed. This covers dormant Gu, gambling rocks and future species that function outside an aperture. Physical movement never changes refinement allegiance.

**Contained Gu — approved.** A mortal cultivator's death does not automatically destroy the Gu in their aperture. Instant death, or death without a meaningful chance to act, generally leaves surviving aperture Gu recoverable from the deceased.

**Clarified 2026-11-22.** A mortal cultivator's aperture (rank 5 and below) is never an item and never drops. Only physical Gu fall from a mortal body. Aperture Gu stay in the body until they are deliberately extracted. No species slips out of a dead host on its own. Only an immortal leaves anything aperture-related: the Blessed Land Root (RFC-0006 §9).

**Phase 2 boundary.** Implement the lifecycle operations, recoverable deceased-host custody and durable event evidence that the future behavior needs. Keep resurrection recovery. Extraction or destruction before resurrection is never reversed by resurrection and never duplicates occupants. Investigate corpse destruction, disappearance of the original host and recovery anchors. Recovered Gu are not refined by whoever recovers them.

**Future — not in Phase 2.** A dying hostile cultivator may deliberately destroy some or all of their Gu to deny them to enemies.

- The chance rises with perceived certainty of death. Prolonged bleeding out contributes strongly.
- Illustrative preliminary curve: 15% after four hours bleeding out, 35% after eight, 80% after ten. These are not final values.
- Other factors may include consciousness, medical prognosis, potential rescue, personality, loyalty, captivity, escape routes and strategic Gu value.
- No independent repeated per-tick rolls.
- The cultivator prioritizes the most valuable or strategically important Gu. Outcomes range from the single most important Gu to a quarter, half or all of the collection.
- Combat should offer meaningful choices: kill outright, capture and rescue, treat, imprison, negotiate, recover surviving Gu.

The autonomous decision system, strategic valuation, prisoner negotiation and final extraction and refinement gameplay are deferred.

## 3. Dynamic inheritances — future

A dying cultivator with suitable capabilities may escape to another world tile and establish an inheritance instead of destroying their Gu. The inheritance comes from actual world history: the cultivator's identity, their real surviving Gu, resources, cultivation knowledge, relationships and environmental assets. Immortals may found inheritances in their own blessed lands.

Phase 2 supports this only through durable identity, provenance and transferable placement. Escape, inheritance quests and generation are deferred.

## 4. Gu on abandoned maps and world locations — approved foundation

- Ordinary native maps are not preserved merely because Gu were left behind.
- Common wild Gu may return to regional populations or retire from individual simulation under a declared accounting policy. Ordinary Gu need not keep a permanent individual record.
- Exceptionally rare mortal Gu and Immortal Gu keep their individual identities. Important refined Gu may need individual preservation.
- Location and lifecycle evidence is captured before native map teardown.
- Map removal and world-pawn GC are not biological destruction.
- Rare and unique Gu may persist as abstract world-location records without their native map.
- Preservation is a configurable policy, not behavior hardcoded around the developer species.
- World state is distinct from player knowledge.
- Where safe recovery or conservation cannot yet be established, the record stays unresolved with its evidence rather than receiving an invented outcome.

*Future:* notifications of valuable Gu at known locations, recovery expeditions, autonomous faction interest, rival cultivators claiming Gu first, and regional conflict compatible with faction simulation.

## 5. Historical retention and save optimization — approved

- About one in-game year is an acceptable initial retention period for ordinary historical information.
- Narrative history is separate from essential simulation and integrity information.
- Ordinary past ownership, descriptions and minor events may later be retired or summarized.
- Always preserve what is needed for creation-event replay protection, cohort conservation, unique Gu accounting, current ownership and refinement, outstanding transactions and reservations, significant unresolved evidence, and gameplay-relevant inheritance provenance.
- Compaction is implemented only where the invariants are demonstrated, and its save-size benefit is measured. Retention periods never override integrity.

## 6. Dormant Gu hauling, storage and property — approved

- Ordinary hauling of dormant Gu to stockpiles and storage is approved and must be verified natively.
- Moving a Gu keeps the same authorized manifestation and does not advance its revisions.
- Gu stay unstackable unless a future representation preserves identity.
- Ordinary right-click pickup stays.
- Hauling never refines, activates, feeds, consumes or changes allegiance.
- Forbidden items, designated storage and ordinary access rules apply.
- Physical access does not imply ownership or permission to use a Gu.

*Future:* clan and sect treasuries, Gu storage facilities, gambling houses, commerce, specimen collections and recovery operations. Theft, betrayal and unauthorized refinement must come from deliberate social decisions with motive, opportunity and risk, never from accidental hauling. The social property system is deferred.

## 7. Immortal environments, Blessed Land Roots and annexation — future design

Recorded in [RFC-0006 §8–§11](RFC-0006-PERSISTENT-IMMORTAL-ENVIRONMENTS.md#8-hybrid-simulation-owner-direction). No environment, root, annexation, terraforming or map-expansion implementation is authorized in Phase 2.

## 8. Recovery and investigations — approved

- Structured lifecycle evidence, operator inspection of unresolved and quarantined records, and evidence-based recovery operations.
- Recovery never blindly authorizes rogue projections, invents historical facts or duplicates individuals.
- Investigations: component-only Pawn manifestation and staging, world-pawn retention and abstract placement, deceased-host recovery and resurrection, native map removal, Royalty, Biotech, Anomaly and relevant Odyssey lifecycle differences, compatibility with staged retirement and save-time reconciliation.
- The current Anomaly restrictions are removed only where replacement behavior is demonstrated safe.
- Preserving missing species state as opaque data remains an explicit architectural requirement.

## 9. Boundaries

No Phase 3, production refinement, cultivator AI, immortal environments, annexation or ascension. Proposals that cross into a later phase, or that are drastic redesigns, go to the owner first. Do not merge into `main`.
