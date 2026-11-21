# Project GU vision

Project GU brings a living cultivation society inspired by *Reverend Insanity* to RimWorld 1.7. The goal is a living cultivation simulation, not a set of cultivation-themed items and abilities. Biological, ecological, economic, social, political and cultivation systems should interconnect and develop organically over time. Scarcity, cultivation, clan relationships, and competing ambitions should create choices and consequences. The project owner approves gameplay scope and acceptance.

## Development philosophy

- **The vision outranks any implementation.** Existing systems may be removed, rewritten, replaced or substantially redesigned when that produces better gameplay, deeper simulation, a better player experience, stronger extensibility or a more convincing living world.
- **Keep the ideas, question the code.** Preserve the worthwhile ideas and intended gameplay experiences. Do not assume their current implementation is the best expression of them.
- **Past effort informs but does not bind.** Development cost already spent is a factor in decisions. It is not a reason to keep inadequate architecture.
- **Ask the owner first** before drastic changes, substantial redesigns, major feature removals or consequential gameplay changes. Explain the reasoning, alternatives, benefits, migration implications and costs. Routine fixes, diagnostics, refactoring within approved boundaries and verified preparatory work proceed without separate approval.

## Approved design principles

- **Gu are living individuals.** Species may move in the wild, interact, feed, become dormant, be captured or refined, enter apertures, and participate in abilities. Species drive which behaviors apply.
- **Identity survives representation and custody changes.** Support Pawn-like active Gu, selectable dormant Things, physical inventory, and aperture storage. Keep one persistent identity and one authoritative placement. Dormancy is a biological condition. Refinement allegiance is represented independently of biology and custody. Physical possession and refinement ownership are separate, and unrefined dormant Gu can be picked up and dropped through ordinary physical inventory.
- **Ordinary events have ordinary outcomes.** A Gu can die, be destroyed, be consumed, transform, rejoin a population or be lost. These are recorded lifecycle outcomes. Quarantine is reserved for contradictory save evidence.
- **Mortal and immortal apertures have different rules.** Ordinary mortal-aperture storage requires appropriately refined Gu. Introducing an unrefined Gu into a mortal aperture for refinement is a distinct action and process. Immortal apertures and blessed lands are persistent internal physical environments. Refined and unrefined Gu can inhabit blessed lands as dormant objects or active living creatures. The current Development Mode aperture transfer is an infrastructure fixture for validating identity, custody and persistence.
- **Containment scales.** A cultivator can hold many Gu, and an ability can involve one or many existing Gu. Containment and ability use should avoid the cost of simulating an active Pawn per participant.
- **Ecology supports multiple origins.** Gu may arise through reproduction, natural refinement, regional ecology, biological relationships, or special events. Habitats can improve suitability without being mandatory for every species. Track origins and enforce conservation and applicable uniqueness across these pathways.
- **Reuse underground infrastructure.** The spirit spring cavern established reusable map infrastructure for mines, inheritances, shelters, deep Earth Abyss-style sites and, potentially, immortal environments. Avoid needless rework. Improvements with consequences for saves or gameplay go to the owner first.
- **Validated gameplay is a regression baseline.** The spring ecology, production, awakening, and migration behavior keep their tests. Changing them is allowed under the development philosophy when the owner approves.

## Design space

A cultivation interface, feeding jobs and facilities, region-scale ecology, exceptional Gu, formations, autonomous cultivator societies, clan politics, and resource markets are future directions. Their rules, balance, and delivery scope need design and acceptance decisions. [RFC-0004](RFC-0004-GU-FRAMEWORK-V2.md) proposes the reconstructed individual Gu framework. [RFC-0005](RFC-0005-CULTIVATORS-REFINEMENT-APERTURES.md) covers cultivators, refinement and apertures. [RFC-0006](RFC-0006-PERSISTENT-IMMORTAL-ENVIRONMENTS.md) covers immortal environments. The [roadmap](ROADMAP.md) tracks implementation and evidence.
