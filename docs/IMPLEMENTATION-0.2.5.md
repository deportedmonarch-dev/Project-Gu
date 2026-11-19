# Gu Framework 0.2.5 — implementation journal

## Slice 1: Species catalog — committed, pending RimWorld compilation

**Scope:** Adds `GuSpeciesDef : Verse.Def` and two XML species (`GU_HopeGu`, `GU_MoonlightGu`). No ecology, awakening, existing save keys, jobs, or pre-existing gameplay behavior modified. `DefDatabase<GuSpeciesDef>` is the species catalog; no manually synchronized registry.

**Intentional constraints:**
- A species definition is **not** an individual Gu or a wild population.
- `capabilityTags` are declarative only; they do not execute abilities.
- Hope Gu have an explicit `spirit_spring` affinity. Species-neutral machinery must preserve species-specific habitat rules.
- Moonlight Gu is a contrasting, non-wild, ability-oriented declaration.
- XML Def type resolution requires an actual RimWorld 1.7 smoke test.

## Tests

1. Run `./scripts/verify-gu-framework.ps1` in PowerShell for static checks.
2. Run `./scripts/verify.ps1` for static checks plus compile against installed RimWorld 1.7 assemblies.
3. Start game with Core + Project GU. Inspect error log for unknown `GuSpeciesDef`, unexpected XML fields, class/Def resolution and rank errors.
4. Verify basic spring, flora, stone production, awakening, save/reload on a backup of the validated 0.2.4 save.
5. Confirm neither Gu species spawns automatically; this is definition-only.
6. Do **not** merge into `main` until player acceptance.

## Next slices (not implemented)

- Determine canonical population store, spring/site identity and migration transaction ordering.
- Move saved spring-specific Hope Gu cohorts into habitat+species keyed records, retaining all 0.2.4 save fields.
- Add persistent individual Gu identity and count-conserving materialization/capture.
- Make awakening consumption failure-safe and atomic at a narrow coordinator.
- Verify at least one **functioning** second species before claiming the framework is extensible.
