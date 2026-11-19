# Project GU — Agent Instructions

- Target **RimWorld 1.7 Waylith/.NET 10 only**; do not substitute RimWorld 1.6 APIs or unverified `ThingComp` overrides.
- The **0.2.4 compile-hotfix** is player-validated (10/10 in-game checks). Preserve behavior and save compatibility. Never claim automated validation implies gameplay validation.
- Do **not** begin implementing the 0.2.5 Gu Framework until its architecture RFC is independently reviewed and approved.
- Separate Gu **species definitions**, **persistent individuals**, and **wild population records**, with single-source-of-truth transitions and conservation across capture, unloading, save/load, and map travel.
- Design reusable behavior extensions for extraordinary Gu rather than an ever-growing hard-coded species switch. Do not overengineer unimplemented mechanics.
- In-depth Gu World requirements: potential future immortal Gu, ownership/refinement/activation, ecology, off-map simulation, clans/sects, and optional underground systems. Spirit springs must not be hardwired to only Moon Orchid or only Hope Gu.
- Native underground maps, if introduced later, must support other sites: inheritances, refuges, mining, and abyssal depths. External mods should remain optional compatibility integrations.
- Every update requires a Windows build against actual RimWorld 1.7 assemblies, focused in-game gameplay tests, save/load and regression tests. Log blockers and fix before advancing. Speed testing with dev controls rather than reducing coverage.
- Avoid committing compiled DLLs, Steam/RimWorld game files, personal logs, saves, secrets, or unlicensed third-party assets.
- Source-of-truth test results belong in versioned docs; don't invent passes.
