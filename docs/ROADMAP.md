# Living Society Roadmap — Project GU (RimWorld 1.7)

## Target: RimWorld 1.7 only
Beta-to-stable compatibility is managed by tests, not guaranteed. See COMPATIBILITY-1.7.md.

## North star
A world of autonomous cultivators with ambitions, scarcity, loyalties, institutions and consequences. Every Gu system should create a social or economic choice, not merely another combat stat.

## Core gameplay loop
Work / negotiate / steal resources -> cultivate/refine -> compete for status -> interact with clan rivals -> alter relations -> world reacts.

## Prototype milestones

### 0.1 — Aptitude / Aperture (this repository)
- Persistent per-pawn aptitude, rank and essence.
- Humanlike pawns on loaded maps initialized periodically.
- Developer validation via health tooltip.
- Not yet in-game tested; compilation and smoke-test required.

### 0.2 — Autonomous cultivation
- New needs: breakthrough ambition, aptitude-limited advancement.
- Scheduled NPC cultivation (bounded work, no full-map scan every tick).
- Cultivation journal to record decisions and consequences.
- World NPCs and caravans coverage.

### 0.3 — Scarcity and the clan treasury
- Primeval stone economy, expenditure tracking, reserve budgets.
- Contribution points and clan reward tiers.
- Competition among NPC disciples for limited resources.
- Tests: if supply falls, behavior *changes*.

### 0.4 — Social hierarchy and politics
- Ranks: initiate, disciple, elder, clan head (independent of cultivation rank).
- Faction loyalty, grievances, patronage, succession and reputation.
- Events: denied promotion, stolen resources, faction split.
- Tests: NPCs initiate politics without player input.

### 0.5 — Gu ecology / refinement
- Five functional Gu with feeding and decay pressures.
- Recipe-driven refinement, failure and resource markets.
- NPC valuation and trading based on needs.

### 0.6 — Emergent story playtest
- One settlement, 15–30 cultivators, a rival faction and a resource shortage.
- Observe a simulated season without directly intervening.
- Success when NPCs cultivate, compete, ally, betray and change status plausibly.

## Architecture
- Data layer: XML Defs for Gu, cultivation paths, clan rules.
- Persistence: per-pawn aperture Hediff; separate faction/world state components as needed.
- Decision layer: bounded action scoring for NPC goals; avoid language-model calls at runtime.
- Events: append-only social events (resource requests, disputes, promotion, theft).
- UI: pawn cultivation tab + clan treasury panel (later).
- Testing: deterministic pure logic tests, in-game smoke tests, save/load, performance.

## Avoid premature scope
No Immortal Gu, Dao marks, hundreds of Gu, gigantic maps, elaborate graphics or online multiplayer until the NPC society loop is compelling.

## First learning sequence
1. Read `About/About.xml` and `Defs/HediffDefs/GU_Aperture.xml`.
2. Trace `GuWorldComponent.GameComponentTick()`.
3. Trace `Hediff_Aperture.Initialize()`, `Tick()`, and `ExposeData()`.
4. Build, observe a pawn, save/reload, compare aptitude.
5. Change probability weights, rebuild and test on a *new* pawn.
