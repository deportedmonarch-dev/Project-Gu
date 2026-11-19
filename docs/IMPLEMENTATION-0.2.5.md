# Gu Framework 0.2.5 — implementation journal

## Slice 1: Species catalog — player-validated (12/12 checks)

**Scope:** Adds `GuSpeciesDef : Verse.Def` and two XML species (`GU_HopeGu`, `GU_MoonlightGu`). No ecology, awakening, existing save keys, jobs, or pre-existing gameplay behavior modified. `DefDatabase<GuSpeciesDef>` is the species catalog; no manually synchronized registry.

**Intentional constraints:**
- A species definition is **not** an individual Gu or a wild population.
- `capabilityTags` are declarative only; they do not execute abilities.
- Hope Gu have an explicit `spirit_spring` affinity. Species-neutral machinery must preserve species-specific habitat rules.
- Moonlight Gu is a contrasting, non-wild, ability-oriented declaration.
- XML Def type resolution requires an actual RimWorld 1.7 smoke test.

## Player acceptance results — 2026-11-19

The project owner reported **12/12 in-game checks passed** after the fully-qualified GuSpeciesDef XML loading hotfix. The prior startup Def resolution error no longer appears. Regression checks covered clean loading, spring ecology, flora support, awakening, stones, save/reload, and absence of unintended physical Gu spawning. **Moonlight Gu being unspawnable is expected:** only a GuSpeciesDef exists, not a ThingDef/physical entity or ability. This is player-reported acceptance, not an independently captured automated runtime test.

## Tests

1. Run `./scripts/verify-gu-framework.ps1` in PowerShell for static checks.
2. Run `./scripts/verify.ps1` for static checks plus compile against installed RimWorld 1.7 assemblies.
3. Start game with Core + Project GU. Inspect error log for unknown `GuSpeciesDef`, unexpected XML fields, class/Def resolution and rank errors.
4. Verify basic spring, flora, stone production, awakening, save/reload on a backup of the validated 0.2.4 save.
5. Confirm neither Gu species spawns automatically; this is definition-only.
6. Do **not** merge into `main` until player acceptance.

## Slice 2: Species-keyed spring cohorts — player-reported 10/10 gameplay tests passed

The new save-backed `GuPopulationRecord` and `GuPopulationLedger` provide species-independent cohort storage and consumption. Spirit springs remain authoritative owners of their own cohorts, keyed by species Def name. The `guPopulationSchema025` marker prevents old legacy save fields from reinitializing a deliberately empty new cohort. Migration imports the mature count, juveniles and ecology tick, with old reserve/stone/habitat fields retained. Stable spring IDs are persisted and duplicate IDs are diagnosed in the map registry.

A developer-only `GU_DevTestSpringGu` can be seeded once through a new spring dev gizmo to verify two species persist independently. It is not a spawnable Gu, does not auto-reproduce and cannot be used for awakening. Wild Gu are now allowed to omit habitat tags because habitats are preferential rather than universally mandatory.

**Player acceptance (2026-11-19):** Project owner reports **10/10 tests passed** for Slice 2. This is player-reported runtime acceptance, not an independently reproduced CI run; the owner has not yet explicitly confirmed the output of `verify.ps1`/`dotnet build`, so **local compilation confirmation remains pending**. The normal `verify.ps1` invokes `build.ps1` using the user's RimWorld 1.7 assemblies. Do not merge into main until compilation is confirmed and pending review gates close. See `TEST-SLICE-0.2.5-2.md`.

**Known issue deliberately deferred:** The existing awakening service still commits pawn state before attempting Gu consumption; a later transaction slice must address rollback and failure-safe coordination.

## Slice 2 follow-up — population save integrity

A versioned save with missing cohorts, an unsupported schema, duplicate keys, or negative
cohort counts keeps the population fields that Scribe loaded and disables cohort mutations.
An existing unversioned list is authoritative, including an empty list. Only an unversioned
save without a list imports legacy scalars. Unresolved species retain their saved counts
and cannot be consumed or simulated until their definition is available.

Local verification on Debian with .NET SDK 10.0.404 passed both Python validators and
the structural stages of `scripts/verify.ps1`. An isolated harness compiled the cohort
and ledger source with Verse stubs and confirmed standard maturation and consumption,
plus no birth above capacity when the total exceeds `int.MaxValue`. The full script
stopped at the missing RimWorld 1.7 `Assembly-CSharp.dll`. Windows compilation and
gameplay remain pending, including save round-trips with explicitly empty lists, missing
versioned lists, unresolved species, and old saves with zero adults and surviving juveniles.

## Follow-up work

See the [roadmap](ROADMAP.md) for sequencing and current gates.

- Verify Slice 2 population custody, spring/site identity and migration through real game testing.
- Add persistent individual Gu identity and count-conserving materialization/capture.
- Make awakening consumption failure-safe and atomic at a narrow coordinator.
- Verify at least one **functioning** second species before claiming the framework is extensible.
