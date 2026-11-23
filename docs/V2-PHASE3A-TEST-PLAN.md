# Phase 3A acceptance plan

**Status — The owner provisionally accepted Stage 3A-II. Under the revised development policy, Stage 3A-III acceptance is limited to current-format identity, conflict handling, Gu custody and fresh-process save/reload correctness. Historical V1 migration and recovery are deferred, unsupported or unverified for development builds. C0 equivalence and C4 benchmarking remain separately gated.** [Phase 3A proposal](V2-PHASE3A-PROPOSAL.md) · [Implementation ledger](IMPLEMENTATION-V2.md#phase-3a-iii--legacy-migration-conflict-handling-and-recovery)

## C0 — Equivalent benchmark inputs

Use the pinned RimWorld 1.7.5014 rev1195 build and the accepted isolated runner. Keep the benchmark alone and sequential. Record engine and fixture hashes, ordered mods and DLC, machine and instrumentation, game settings and each executable revision. Keep source saves read-only.

1. Freeze one common Phase 2 source save. Give every test aperture its durable ID before branching and record the exact Pawn and Hediff identities. Freeze the simulation tick. Build the empty and populated variants from disposable copies with identical hosts, Hediffs, IDs, world and native objects. The populated variant has the declared record-only Gu fixture count, with fixed fixture Gu IDs and zero extra native projections.
2. Prepare the fixture pair twice in independent fresh processes from clones of the same hashed source. The corresponding variants must have equal canonical fingerprints. This tests fixture preparation reproducibility. If future use of a seed regenerates the source world, first require the same canonical fingerprint from two independently generated sources with pinned world seed, tile, settings and RNG inputs. Counts and save bytes alone cannot validate equivalence.
3. Compare the empty and populated pair against a narrow allowlist for the exact intended Gu fixture additions. Run the Phase 2 and 3A comparisons on the same verified variants. A comparison of their resulting saves may allow only the declared cultivator schema markers, migrated records and their integrity evidence. Any other difference invalidates the comparison until traced and corrected. Preassigned native aperture IDs and Gu custody revisions must match.

### Fingerprint contract

Use a small fixture-specific canonical digest with per-section diffs. Sort genuinely unordered collections by their stable keys and preserve order where it affects simulation. Include meaningful values and reference topology, including serialized native state held in compressed map data. A hash of the immutable source file separately verifies exact input bytes. Exclude only verified volatile metadata with a written reason.

| Section | State included |
| --- | --- |
| World and scenario | Seed, world dimensions and generation settings, relevant geography and layer/tile identity, faction and world-object state, scenario, storyteller and difficulty. |
| Map | Parent, tile, dimensions, terrain, roofs, zones and meaningful map component state, plus Things by definition, identity, position, holder, count and relevant state. |
| Pawns and health | Exact native memberships and associations, Pawn IDs, kind, race, faction, age, traits and relevant skills, needs, hediffs, life state and apparel or equipment. Include both native Hediff IDs and durable aperture IDs. |
| Holders | Map, inventory, carry, corpse, caravan, transporter and world-Pawn references present in the fixture. Preserve each exact item’s ownership chain, quantity and position where applicable. |
| Simulation | Tick, weather and other relevant manager state, work and job state, pending incidents, deterministic RNG inputs and counters where observable. Document any relevant state that requires a separate equality assertion. |

**Pass** requires equal fingerprints across the two independent preparations for each variant, exactly the intended delta between variants, and unchanged source hashes after all passes. Measure save bytes and per-record growth, save/load duration, managed allocations, cultivator index rebuild and existing Gu binding rebuild separately. Measure lifecycle event capture and reconciliation for a single event and a representative burst. Include the 600-tick discovery path explicitly. Record fixture counts for cultivators, apertures, Gu, native Pawns and retained evidence. Report repeated fresh-process measurements and sample dispersion. Counts are measurement points. Capacity and deletion targets remain provisional.

## C1 — Managed and structural checks

Use the existing managed verification infrastructure and a game-linked compilation after implementation approval. Managed checks validate data rules and the proposed transaction contract. The native fixtures at C2 and C3 validate the engine behavior.

| Test | Measurable pass criterion |
| --- | --- |
| Lossless input and reference classification | Pass fixtures for resolved references, a failed reference with a unique exact raw token, a token with no matching Gu, missing node, literal null, malformed aptitude or awakening state, normalized raw fields, duplicate rows and contradictory anchors. Every input row and malformed value is accounted for. Only the exact, supported case binds a Pawn. Unique historical evidence yields an unresolved association with zero current ownership. Missing and conflicting evidence survives without inferred lifecycle facts. |
| Readiness and schema authority | Test both component callback orders, repeated callbacks and upstream exceptions. Publish exactly once after successful readiness. Test absent, committed empty, committed populated, incomplete, unsupported and invalid markers, plus conflicting dual stores. Only absent new authority is eligible for import. A committed empty input reopens empty. Failure leaves the guard closed and preserves source evidence. |
| Apertures and association keys | Test a clean legacy aperture-only Pawn, a replacement Hediff, duplicate cultivator IDs, duplicate Pawn claims and duplicate aperture IDs on the same and different Pawns, including an unindexed owner. Preserve all conflict evidence. Zero arbitrary winners, ownership grants, remaps or custody revision changes. A unique, existing aperture owns its original ID. |
| Failure boundaries | Inject failure during prepare, after validation, after the first native aperture write, before owner/store publication, at save preflight and during rollback. Require either exact verified rollback with unchanged source evidence or a guarded, reported recovery state. There are zero saves with mixed authority, partial owner links or a falsely committed marker. |
| Awakening and discovery | The same Pawn reached through both routes has one association and one cultivator ID. Exercise failed native insertion, invalidation after insertion, refused debit and a save request during publication. The successful case debits once and publishes one owner link. Failed cases leave no committed aperture link, no extra aptitude roll and the prior Hope Gu quantity. |
| Lifecycle and bounded retention | Replay duplicate, nested and reordered callbacks. Only a confirmed discard transition creates one fact per association revision. Confirm that body evidence survives GC and resurrection, and missing references leave life evidence intact. Dependency fixtures keep significant identities and unresolved evidence. An eligible incidental discard stays removable and an incomplete dependency index prevents removal. |

## C2 — Native identity, lifecycle and save boundaries

Use disposable profiles and native saves on the pin. Record exact Pawn, cultivator, Hediff and Gu identifiers, actions, quantities, pre/post native flags, callbacks, ticks, selected patches and resolved events. Repeat each durable test state through **two successive reloads in separate fresh processes**. Compare the same identity IDs, association revisions, explicit owner links and event keys across both.

| Case | Measurable pass criterion |
| --- | --- |
| Awakening and in-world continuity | One committed identity and at most one existing-policy aperture link. The same IDs survive faction change, imprisonment, downing, map travel and both reloads. Gu custody revisions, owner chains, holder access rules and Hope Gu accounting match the declared Phase 2 behavior. |
| Death with corpse, then resurrection | Both a corpse-held Pawn and a natively saved dead world Pawn retain their valid associations through save/load. Resurrection restores a live view with the same cultivator ID and surviving aperture link, and records no body loss from shell cleanup. Gu extracted or destroyed before revival stays in its committed state. |
| Actual corpse destruction and proven corpse-less death | Separate fixtures yield one correctly classified body fact each. If positive corpse-less evidence cannot be captured, the test fails the proposed coverage gate and no unproven fact is written. Where the Pawn remains engine-owned, the association survives. Compare Gu release or preservation with the Phase 2 baseline. |
| Nonlethal destruction, explicit discard and GC | A nonlethally destroyed Pawn that remains world-owned keeps its association. Test live and dead discards, explicit `PassToWorld(Discard)`, and real GC. Each confirmed discard yields one fact with the correct cause and observed life. A dead Pawn’s prior body evidence survives GC. Missing references alone create no dead or discarded fact. No hook records the same transition twice. |
| Re-entry, errors and saving | Verify nested hooks, original exceptions and partial teardown. The marker clears at scope exit and never contaminates the next Pawn. A save during an active scope is blocked. A save after a real completed transition and before the next tick includes both the confirmed identity event and the Phase 2 custody result. Reload produces zero additional events or physical moves. |
| Native retention and selective persistence | From matched Phase 2 and 3A sources, compare the exact retained Pawn IDs and reasons, including a vanilla-retained Pawn, an Individual-policy Gu holder or host and a genuinely collectible NPC. A significant identity remains queryable after native discard. A dependency-free incidental record can be removed. A Pawn kept only by existing `ProjectGuCustody` retains that reason and exact custody. |

## C3 — Existing-save migration and recovery

The original C3 migration matrix remains the comprehensive compatibility plan, but old experimental V1 and historical saves are outside the current development-build acceptance gate. Do not claim those saves are compatible without new evidence. For current development builds, use a disposable current-format Phase 2 source and test existing identity/aperture associations, conflicting and malformed claims, committed-empty authority where applicable, transactional failure safety, Gu custody conservation and two independent fresh-process reloads. Record source provenance and hashes. Preserve unresolved conflicts as evidence; do not select arbitrary winners.

**Current-format development pass** requires unchanged source hashes, one authoritative cultivator store, stable identity and conflict evidence across two independent fresh-process reloads, unchanged Gu custody revisions, and exact physical conservation. The broader legacy pass additionally requires the original migration criteria above; it is deferred and must be reported as unverified until performed.

For each rollback trial compare the original representation, authority marker, aperture IDs, Gu revisions, Hope Gu balance and custody graph before and after. Keep the original source read-only. Verify that an unsafe completion leaves no writable half-migrated game. Test a repaired retry from a pristine copy and require that the next committed save contains one authoritative cultivator store.

## C4 — Regression and measured costs

Run the approved Gu fixture and regression set after C2 and C3, including the existing underground checks with no architecture changes. Compare the verified C0 inputs across Phase 2 and 3A. Retain raw measurements and instrumentation metadata. Report an explicit pass, fail or unverified result for each check. Investigate regressions at the narrow seam that caused them before raising scope. No new population, scheduler or deletion policy is implied by acceptance.

## Approval and evidence gates

| Review blocker | Revised contract | Evidence after authorization |
| --- | --- | --- |
| 1. Legacy evidence and ordering | Capture raw rows before Scribe resolves or normalizes them. Gate import on explicit Phase 2 registry success. | C1 classification and callback-order fixtures. C3 source accounting. |
| 2. Identity and lifecycle state | Separate association, observed life and bounded confirmed facts. Preserve existing identity through resurrection. | C1 state round-trips. C2 death, body loss, disappearance and resurrection. |
| 3. Commit, rollback and conflicts | Stage Hediff and owner changes under the existing guard. Publish one completed authority. Preserve conflicting claims without a winner. | C1 and C3 failure injection, source hashes, conservation and two fresh reloads. |
| 4. Overlapping native observations | Correlate the exact GC invocation, commit after confirmed discard, and flush before saving. Keep Phase 2 policy. | C2 event counts, scope exits, retained Pawn sets and pre-tick save evidence. |
| 5. Benchmark equivalence | Clone one hashed source with fixed aperture identities. Check canonical fingerprints and measured cost components. | C0 independent preparations and Phase 2 baseline. C4 measured comparison. |

| Gate | Required evidence |
| --- | --- |
| Stage 3A-I | Approved design and managed scope. Game-linked compilation and Stage 3A-I checks are in the ledger. |
| Stage 3A-II | Owner authorized the focused native lifecycle, retention, Scribe, save authority and Phase 2 regression cases. See the [implementation ledger](IMPLEMENTATION-V2.md) for results. |
| Stage 3A-III development gate | Verify current-format identity, conflicts, custody conservation and fresh-process save/reload. Historical V1 migration, the original 31×31 and 39×39 saves, and the full legacy recovery matrix are deferred and remain unsupported or unverified. See the [implementation ledger](IMPLEMENTATION-V2.md#phase-3a-iii--legacy-migration-conflict-handling-and-recovery). |
| Separate Stage 3A-IV approval | Establish C0 equivalence and run C4 benchmarking once approved. Confirm measurable budgets and population bounds with the owner after evidence is available. |
