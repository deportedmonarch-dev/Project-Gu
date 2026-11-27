# Gu ability architecture: independent review

Claude Opus 5.5, 2026-11-27. This is a design review only. No code, branches or installs were touched.
Baseline: Project GU `b9612e5`, Society Core `1355b14`. I read every file under `Source/ProjectGu/Combat/`, `GuIndividualRegistry.Abilities.cs`, the aperture essence API, `GuCultivatorRecord`, `GuSpeciesDef`, the two ability Defs, the Kindred briefing and encyclopedia chapters 02, 04, 05, 09 and 10, plus proposals 00 to 03.

## 1. Recommendation

**Option A, applied with discipline: extend the existing framework where a real feature needs it, and fix the few contracts that are too narrow at the moment that feature arrives.** Don't build an "execution core" (Option C as framed), and don't build a universal engine (Option B).

The main finding is that **the existing code already is Option C.** The responsibilities that Option C proposes to extract are already separate:

| Option C responsibility | Already implemented by |
| --- | --- |
| Read-only exact-individual authorization | `GuIndividualRegistry.IsAbilityGrantedBy` / `TryGetAbilityCaster` (pure, no cached ownership) |
| Revalidate at commit; one irreversible step | `GuAbilityService.TryCommit`, `GuChannelService.TryBeginChannel` |
| All-or-nothing payment | `Hediff_Aperture.TrySpendEssenceForCast` (restores the exact quality mixture on a failed launch) |
| Execution identity and once-only outcomes | `GuCombatLedger` cast IDs, `Projectile_GuAbility.resolved`, channel `SessionId` |
| Domain-owned effects | Worker classes; the native `Bullet` owns impact and damage; the aperture owns essence conversion |
| Native jobs schedule the activity | `JobDriver_GuCastAbility`, `JobDriver_GuChannelAbility` |
| Presentation observes and never drives | `GuAbilityPresentation`, `GuFxMapComponent`, FX profiles are never read on the gameplay path |

The only real shared-execution duplication is about 25 lines: `CanCast` and `CanChannel` repeat the caster, grant, starvation-reason, cooldown and essence checks. That doesn't justify a new layer. Extracting a better-named core now would be a refactor with no new gameplay. The owner asked to avoid exactly that.

**Option C's proof claim also doesn't hold.** It says Black Boar body cultivation "proves a third lifecycle". It doesn't. Black Boar body cultivation is a **channel**: a seated, essence-consuming, resumable, cooldown-gated session made of pulses. That's the Liquor Worm lifecycle with a different domain effect. Building it as a new lifecycle would create the duplicate system that Option C warns against. The lifecycles that really are new are:
- **leased states** (temporary forms, phantoms, buffs)
- **reactive hooks** (defense, interception)
- **housed passives** (effects that exist while a Gu sits in the aperture)
- **multi-source activations** (killer moves)

Build each one only when a playable feature needs it.

## 2. Constraints: what is global and what is local

**Truly global. Every Gu effect obeys these, and they're already enforced:**
1. Possession never grants anything. Only the exact, living, refined, resolved, non-withering individual in the user's live aperture authorizes use, revalidated at every irreversible step.
2. One irreversible commit per activation. Before commit, cancelling is free. After commit, the cost is spent. Payment is atomic and never partial.
3. Every committed activation has a durable identity (cast or session ID), and its outcome applies at most once, including across save and load.
4. Native jobs own scheduling, interruption, reservations and player or emergency authority. Native health and damage own bodily outcomes.
5. Presentation never decides gameplay. Gameplay is correct with FX disabled.
6. Committed facts persist in the current save format. Old-save migration is not required.
7. **New, but already implied:** an effect that changes the body belongs to the body (native health on the pawn), not to the source Gu. Losing the Gu removes the *capability*, not the *result*.

**Correctly local to a specialized worker. Keep these where they are and don't promote them:**
- Projectile flight, range 1 to 60, `minRange`, line of sight, pawn-or-cell target, same map, no self-target, warmup aim (projectile worker only)
- Pulse count, interval, undrafted requirement, binding to rank 1 stage (channel or Liquor Worm only)
- A positive scalar mortal-essence price (true for every current Def. The immortal-essence payer is a future adapter at the single `TrySpend*` call.)
- One Gu source per grant (true until killer moves)
- Player-only order entry (`IsColonistPlayerControlled` sits in `TryOrder*`, *not* in authorization, which is the right place)
- A single mortal aperture (`TryGetAbilityCaster`: the one place that changes for immortal apertures)

**Misplaced or too narrow. Change these only when the first feature that needs them arrives:**

| Item | Problem | Change when |
| --- | --- | --- |
| `GuChannelWorker.CanChannel/BindingFor/ApplyPulse(Hediff_Aperture …)` | The worker sees only the aperture, not the pawn, Gu record or species. Body cultivation needs the pawn. | Slice S0 |
| `GuChannelProperties` is `sealed` | Worker-specific configuration (for example, which hediff to grow) has nowhere to go except polluting the shared class | Slice S0 |
| `GuAbilityDef` holds projectile fields and `ConfigErrors` checks range for every Def | Harmless today. It becomes noise once self or non-targeted activations exist. | When the first non-projectile cast ships (phantom or lease slice) |
| `GuAbilityWorker.ValidateTarget` base forbids self and requires a map cell | Should belong to the projectile worker. A self-lease worker needs to override it or use a `targetSelf` flag. | Same slice as above |
| `GuCombatLedger.IsDriven` scans every spawned pawn on every map, for each active session, every 250 ticks | Bounded today. Becomes noticeable if many NPCs body-cultivate. | When autonomous NPC cultivation exists. Index sessions by pawn then. |
| Cooldown is keyed per Gu, not per (Gu, ability) | A Gu with two abilities would share one cooldown. Possibly correct in-world, but undecided. | Owner decision when the first multi-ability Gu appears |

## 3. Ownership boundaries

| Concern | Owner | Notes |
| --- | --- | --- |
| Gu identity, custody, refinement, grant eligibility | `GuIndividualRegistry` | Unchanged. No ability state cached here. |
| Activation admission, payment, cooldown, cast or session identity, once-only bookkeeping | `GuAbilityService` / `GuChannelService` + `GuCombatLedger` | Keep both services. Optionally share one private authorization helper. A rename of the ledger isn't worth the churn. |
| Mortal essence stock and quality | `Hediff_Aperture` | Future immortal essence is a separate payer. Don't generalize now. |
| What a committed activation does | Worker class per effect family (projectile, essence refinement, bodily cultivation, then later lease, investigation and so on) | Workers never debit, set cooldowns or authorize. |
| **Permanent bodily attainment (Black Boar and similar)** | **A native `Hediff` on the pawn, one HediffDef per strength, severity = progress 0 to 1** | Native stat modifiers, Health tab, death, resurrection, caravans and world pawns all come for free. Not in `GuCultivatorRecord`, whose strict save whitelist and pawn-rebinding role make it the wrong home. Not in the registry. |
| Temporary forms, phantoms, buffs | A lease hediff on the same pawn, created by a committed activation and ended by expiry or interruption | Kindred AN-001 and AN-002. Use a proxy pawn only with a written identity and custody policy. |
| Projectile impact and damage | Native `Bullet` and `DamageInfo` | Defense resolves at one native boundary with a typed result (Kindred CB-003). |
| Evidence from investigative Gu | Project GU (a committed evidence fact) | Society Core interprets and propagates it. Gu never read omniscient social data. |
| Why and whom an NPC fights or serves | Society Core (optional, independent) | Project GU exposes a read-only "can this pawn use X now" offer through the existing `CanCast`. No Society Core changes now. |
| Visuals and sound | Presentation components | Unchanged |

## 4. How the future cases fit (seams only, nothing to build)

- **Permanent body changes (Bronze Skin, boar strengths):** the same pattern as Black Boar: a channel worker grows a pawn hediff, or a one-shot cast adds one.
- **Beast phantoms:** a self-target leased activation through the *existing* cast service. The phantom's magnitude reads the pawn's bodily-strength hediffs, which keeps it separate from strength by construction. Eligibility after transfer is an owner rule, checked in that worker.
- **Investigative Gu:** a cast worker whose result is a saved evidence record (location, time, confidence, concealment) instead of a `Thing`. `TryLaunch` already tolerates a null `launched`.
- **Temporary transformations:** a lease hediff plus temporary grants recomputed from the lease. Same pawn first.
- **Permanent conversion, zombies, soul or identity:** each needs its own survival, identity and custody policy (Kindred AN-005). Out of scope until designed. No placeholder classes.
- **Autonomous use:** add a second order entry (a cadenced JobGiver that calls `CanCast` and `ValidateTarget`, then orders an unforced job). Authorization is already pawn-agnostic.
- **Multi-Gu killer moves:** a future `GuKillerMoveDef` listing component species. Its admission calls `IsAbilityGrantedBy` for each component, pays once, starts every component cooldown and commits one cast record that lists every component Gu ID. Nothing in today's design blocks this addition.
- **Rank 6 and above:** the changes land at `TryGetAbilityCaster` (immortal aperture or blessed land) and at the payer call. Dao marks and blessed-land scope get their own owners when designed (RFC-0006).
- **Passives while housed:** recompute a hediff from aperture-occupancy change events and on load. Build this only when the first such Gu is designed.

## 5. Rejected alternatives

- **B, universal ability and effects engine:** replaces two working, verified systems to serve imaginary ones. The no-backward-compatibility rule lowers the cost of a rewrite but doesn't create a reason for one.
- **C as framed, an extracted shared execution core:** the core already exists. Re-extracting it is churn, and its proposed proof (body cultivation as a third lifecycle) would duplicate the channel lifecycle.
- **Body progress stored in `GuCultivatorRecord` or a new registry ledger:** duplicates native health, needs custom stat application and couples body state to Gu bookkeeping.
- **A Kindred-style all-owning per-pawn component (`CompKindred`):** the registry and aperture already split those authorities cleanly.
- **A generic effect, modifier or tag DSL, or a global passive tick service:** speculative. Hediffs and `StatDef`s are already the native modifier system.
- **Temporary strength buffs as a stopgap:** explicitly ruled out by the owner.
- **Proxy-pawn transformations by default:** heavier custody risk (AN-002). Same-pawn first.

## 6. Smallest playable sequence

Each slice is separate from Codex's Slice 1A worktree. S0 touches only `Combat/` and doesn't affect the ecology work. Every slice needs compile, install, focused native checks, current-format save/load and the relevant regressions (`LiveAlpha2`, `LiveAlpha3`, `CombatManagedChecks`).

**S0. Channel contract widening (small, no gameplay change)**
- Unseal `GuChannelProperties` so XML can use `Class="…"` subclasses.
- Change the `GuChannelWorker` methods to receive a small context `{Pawn, Hediff_Aperture, GuRecord, GuAbilityDef}` (built from the grant `GuChannelService` already has) and port `GuChannelWorker_EssenceRefinement` unchanged in behavior.
- Optional: one private helper shared by `CanCast` and `CanChannel` for the caster, grant and starvation-reason checks.
- Acceptance: Liquor Worm start, interrupt, resume, stage-change stop and save mid-pulse all behave exactly as before. Moonblade is untouched.

**S1. Black Boar body cultivation (first new playable content)**
- `HediffDef GU_BodilyStrength_BlackBoar`: not bad, untendable, maxSeverity 1, about 5 stages that scale gradually toward the provisional full values (+40% `MeleeDamageFactor`, +50% `CarryingCapacity`, +20% `MiningSpeed`, +15% `ConstructionSpeed`). The stage labels show the developing condition in the Health tab.
- `GuChannelWorker_BodilyCultivation` with properties `{hediff, progressPerPulse, essencePerPulse}`. Each pulse spends essence through the aperture and raises severity. `CanChannel` is false at full development, so repeated gains from the same species are impossible by construction.
- `GuAbilityDef GU_BlackBoarCultivation` (category Cultivation, 1 in-game day cooldown, tuned so about 15 days of regular sessions reach full).
- A "Bodily attributes" block on the Cultivation page that lists the pawn's bodily-strength hediffs.
- The species `abilities` line is added once Slice 1A's Black Boar `GuSpeciesDef` exists. Until then, test against a dev-only species Def.
- Acceptance:
  - progress accrues per pulse
  - interrupting keeps progress
  - releasing, transferring or losing the Gu leaves both progress and stats in place
  - a second Black Boar Gu adds nothing past full
  - the stat explanation shows the native modifiers
  - progress survives save/load mid-session
  - results are identical with FX off

**S2. White Boar strength (data only)**
- One HediffDef plus one ability Def, after Slice 1B adds the species.
- Proves that distinct strengths coexist with no new code. If it needs code, S1's boundary was wrong. Fix it then.

**S3. Beast phantom (only after the owner fixes the phantom rules)**
- Move the self-target and no-location rules from the base `ValidateTarget` into the projectile worker (or add `targetSelf`), and relax `GuAbilityDef.ConfigErrors` for non-projectile workers.
- Add `GuAbilityWorker_SelfLease`, which applies a timed phantom hediff scaled by the bodily strengths. This reuses `TryCommit` unchanged.

**After S3, build only on demand,** in whatever order the content roadmap pulls:
- White Jade defense (one interception resolver)
- autonomous combat entry
- investigative evidence
- the first transformation lease
- killer moves

## 7. Keep, change, defer

- **Keep:** registry authorization, both services, the ledger, the projectile worker, `Projectile_GuAbility`, the aperture payment API, the FX separation, the Moonblade and Liquor Worm Defs. They're correct, verified, and already encode the global rules.
- **Change (S0 and S3, only when needed):** the channel worker signature and unsealed properties; the projectile-specific target rules and `ConfigErrors` move down to the projectile worker.
- **Defer:**
  - the ledger rename
  - the session index
  - the cooldown-key decision
  - an AI order entry
  - killer-move Defs
  - immortal payers and caster sources
  - zombie and soul policy
  - housed passives
  - investigation records

  Each has a named seam above and costs little to add when real content asks for it.
- **Preserve:** Codex's Slice 1A ecology work (no shared files until the Black Boar species Def lands) and Society Core's independence (no API, no dependency).

## Owner decisions needed before S1

1. Confirm that S1 is the next ability slice and that the provisional numbers stand for the first build.
2. Essence model: pay per pulse (like wall nourishment) or once per session (like Liquor Worm). I recommend per pulse, because "essence-consuming practice" scales with time spent.
3. Should the Health tab condition remain once development is full, or move to the Cultivation page only?
