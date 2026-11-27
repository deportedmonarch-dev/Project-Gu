# Kindred research → Project GU: feasibility and opportunity assessment

Claude Opus 5.5, 2026-11-27. This is a companion to `CLAUDE-ARCHITECTURE-REVIEW.md`. It is not an implementation authorization and makes no repository changes.

**Evidence labels**
- **[V]** Verified by the static Kindred research or by the 1.7 native engine sample (N17). "Verified" means the code exists and does what the trace says. It does *not* mean the code was runtime-tested: no Kindred mechanism was run, profiled or tested against other mods.
- **[I]** Engineering inference: my judgement, based on the native API, Project GU's code and general RimWorld modding knowledge.
- **[S]** Speculative: plausible gameplay that would need its own design and feasibility work.

## Headline conclusions

1. **Kindred shows that RimWorld can host a high-spectacle supernatural combat game without replacing the engine.** Every Kindred effect still resolves through native damage, health, jobs, flyers, maps and the pawn renderer [V]. Project GU can reach Kindred's level of combat spectacle by the same means. The main cost is artwork and careful edge-case handling, not engine limits [I].
2. **Kindred's weak points line up with Project GU's existing strengths.**
   - Kindred loses pending attacks on load (Already Dead, Sun Lance, Head Burst flight, Walker salvos) [V/I].
   - It commits scars and states before its side effects succeed [V/I].
   - It spreads death interception across many independently prioritized Harmony hooks [V].
   - It repairs state with periodic scans over all pawns [V].

   Project GU's cast IDs, once-only resolution, transactional custody and event-driven style address exactly these problems. "Go beyond Kindred" therefore mostly means *the same spectacle, but persistent, correct and in-world*.
3. **Kindred has almost no investigation or concealment, no multi-Gu composition, no ecology-driven creatures and no information-limited AI.** These are where Project GU can be distinctive rather than derivative [V for the absence within traced families].
4. **There are hard ceilings.** Each needs a design that works around it, not a fight with the engine:
   - True multi-cell pawn footprints
   - True airborne combat on a z-axis
   - Hundreds of simultaneously simulated beasts
   - Many simultaneously active maps

   Kindred stays under all four: giant forms are a one-cell pawn drawn large; flight uses a temporary flyer; packs are small; realms are short-lived [V].
5. **Several native 1.7 systems that Kindred barely used or that postdate its patterns would remove the need for Harmony in Project GU:**
   - pawn render nodes and keyframe animation (`PawnRenderNode`, `AnimationDef`, present in N17 [V])
   - `Pawn_FlightTracker` (a field on the native `Pawn` in N17 [V]; its behavior wasn't examined)
   - native invisibility hooks (`Notify_BecameInvisible`, `IsPsychologicallyInvisible`, `HediffComp_Invisibility`, present [V])
   - the mutant framework (Kindred's Risen "initialize the Risen mutant" [V]; whether it works without the Anomaly DLC is **unverified**)
   - verb-giver hediffs and added-part hediffs (native surgery paths traced in Stage 6A [V])

   Checking these is the most valuable small research investment before content work [I].

---

## 1. Projectiles, beams, staged attacks, sustained damage, area attacks, environmental destruction

**What Kindred demonstrated**
- Native projectiles through VEF [V].
- *Already Dead*: a map queue of real later damage, per victim, with deadlines. The queue is **not saved**, so a reload keeps the teleport but can drop the wounds [V/I].
- *Sun Lance*: a contact beam. The caster is held in a native wait job; each pulse checks walls, projects targets into beam width and reach, and calls burn damage. The active lance isn't saved [V].
- *Deluge* and *Pyro*: field records (cells or a center and radius, caster, deadline) that pulse native damage. These *are* saved [V].
- Wall breaking applies real building damage [V]. A terrain write was found only at map generation (`TerrainGrid.SetTerrain`), not after a cast [V].
- Bullet Cut intercepts at `Bullet.Impact` with priority-ordered Harmony [V].
- Gore is budgeted presentation driven by real missing-part results [V].

**What Project GU can do natively with the existing framework**
- Moonblade already covers the projectile case end to end, including once-only impact.
- Area attacks: `GenExplosion.DoExplosion` (which Kindred's Walker uses [V]), or a worker that applies `TakeDamage` to cells chosen by a bounded radius or cone query [I].
- Destruction: building damage, roofs, fire and filth are native, and terrain writes use native APIs [V for terrain and buildings, I for roofs and fire].
- Each of these is a new `GuAbilityWorker` behind the unchanged cast service [I].

**Where custom engineering is needed**
- One small, **saved** map record for committed multi-stage effects (cast ID, due tick, origin, target cells or pawn, remaining pulses). This single piece covers deferred strikes, beams, lingering fields and Chainsaw Golden Centipede pulses [I].
- Beam geometry and line tests [I].
- A terrain-change transaction for *persistent* destruction (craters, scorched or flooded ground) that respects player structures [I].

**Limits and risks**
- **Combat Extended** replaces native projectiles. `Projectile_GuAbility : Bullet` won't behave under CE without an adapter [I; high compatibility risk if CE support matters].
- Area friendly-fire policy and player-structure damage will cause player-trust problems unless they're legible.
- Saving mid-stage must be tested.
- Area scans must stay bounded (Kindred's are bounded but unmeasured [V]).

**Opportunities beyond Kindred**
- **Staged attacks that survive save/load**, which Kindred does not guarantee [V/I].
- **Telegraphed counterplay:** a visible charge-up that native AI and players can step out of [I].
- **Path-material interactions** through native damage defs and fire: fire Gu ignites, water Gu extinguishes, and earth-path effects alter terrain [I].
- **[S] Destruction with ecological consequences:** burning a habitat lowers its wild Gu suitability and recovery through the ecology system. No Kindred analogue exists, and it ties combat to the living world.

## 2. Movement, flight, aerial combat, charges, displacement, knockback

**What Kindred demonstrated**
- *Flying Charge*: VEF cast, then native `PawnFlyer`, with Kindred landing damage, wall policy and a second flyer that carries the victim [V].
- Throws: the native flyer takes custody of the victim, the route is saved, and collision is decided by cells and structures regardless of the drawn arc [V].
- Grapple: an explicit, deep-saved holder [V].
- An Umbra flyer [V].
- A verified bug: reading `pawn.Map` after the flyer despawns the caster [V]. This failure class (FR-01) will recur in any flyer work.
- Heavy gait is purely visual [V].

**Native and existing framework**
- Crash Gu (vertical and horizontal), dashes, knockback and throws are all `PawnFlyer` launches from a cast worker. The flyer carries a cast ID and resolves once, exactly like `Projectile_GuAbility` [I].
- Native jump and skip-style abilities show that short-range teleports are supported [I].
- `Pawn_FlightTracker` exists on 1.7 pawns [V], but its gameplay meaning (targetability, acting while flying, pathing) wasn't researched. **Check this before promising sustained flight** [UNVERIFIED].

**Custom engineering**
- A flyer subclass with a saved route and landing policy (blocked cells, map removal, carried victim) [I].
- **Sustained flight** as a gameplay state: a lease hediff plus pathing and targeting rules plus a visual lift [I]. Pathfinding internals are a high-risk patch target in 1.7 and should be avoided [I].

**Limits and risks**
- RimWorld has no z-axis. Pawns inside a `PawnFlyer` can't act and are effectively out of play [I].
- "Aerial combat" between two flying Gu Masters can't be done as real 3D combat [I].
- Roofs, doors, map edges and the AI's ignorance of flight are the recurring edge cases [I].

**Opportunities beyond Kindred**
- **An "airborne" combat state** instead of physical altitude [S]: an elevated pawn ignores melee from grounded pawns without reach, gains ranged accuracy and ignores terrain movement costs. It reads as aerial combat within the engine's 2D rules.
- **Flight as strategic mobility:** caravan speed or world-map travel modifiers through native caravan systems [I].
- **Retreat movement the AI actually uses** (see section 7).

## 3. Beast phantoms, layered rendering, attack animations, large-scale visual effects

**What Kindred demonstrated**
- A map-scoped FX scheduler: timed atlas sprites, attached auras, afterimages, flecks, and culling to the camera view [V].
- Giant forms drawn by a Harmony prefix on `PawnRenderer.RenderPawnAt`. The rig has directional walk, attack and idle frames, overlay layers, planted feet and entry and exit transitions [V].
- A lazy texture loader with per-frame time budgets (patching `MaterialPool.MatFrom`) [V].
- Damage masks built through texture readback [V], with GPU cost unmeasured.
- Instance caps (300 for detail, 400 before eviction) that are **eviction policies, not guarantees** [V/I].
- Crowd simplification above 30 pawns in view [V].

**Native and existing framework**
- `GuFxMapComponent` and `GuFxProfileDef` already cover launch, travel, impact and orbit cues (Liquor Worm).
- Native `PawnRenderNode` / render tree and `AnimationDef` keyframes exist in N17 [V]. A **beast phantom can be a render node attached by a hediff, with no Harmony** [I], and keyframe animation can drive its lunge or roar [I].
- Large effects (phantom charges, sword waves) fit the existing map FX component with culling [I].

**Custom engineering**
- An atlas and art pipeline (frame timing, pivots, facing, alpha conventions) [I].
- Suppressing and replacing the native body for a full giant form *does* need a render override, as in Kindred [V/I].
- Bounded instance budgets with true caps [I].

**Limits and risks**
- **Art is the dominant cost**, not code [I].
- VRAM, overdraw and first-use stutter are unmeasured in Kindred [V].
- Render-node changes can clash with Humanoid Alien Races and other render mods [I].
- Avoid texture readback (stalls) unless it's essential [I].
- Culling at the screen edge can hide a giant whose logical cell is just off-camera [V/I].

**Opportunities beyond Kindred**
- **Progress you can see:** phantom scale and opacity derived from the pawn's actual bodily-strength hediffs (the S1 Black Boar work). Players *see* fifteen days of cultivation [I].
- **Readable threat:** NPC phantoms telegraph their strength, which supports the player's decision to retreat or negotiate [S].
- **Gu that look like what they are:** a species-specific aura or orbit on any active Gu, reusing the Liquor Worm orbit profile pattern [I].
- **[S] Dao-mark body markings** at rank 6 and above as render nodes.

## 4. Temporary beast transformations, giant forms, real anatomy changes, alternative bodily matter

**What Kindred demonstrated**
- Garou same-pawn forms [V].
- *Vicissitude*: a timed giant form on the **same pawn**. It uses a body hediff (melee ×1.8, pain 0, speed −0.4), a damage ward patched into `PreApplyDamage` (38% of incoming damage, capped at 13), subordinate powers scheduled by a map component, and a permanent scar [V].
- **No larger collision footprint, no new race body, no second pawn** [V].
- *Polymorph*: a proxy pawn, with the original deep-saved in a `ThingOwner` [V].
- *Rite of Stone*: a permanent stone-body hediff that changes stats and health on the original pawn, with **no BodyDef replacement** [V].
- *Medusa*: temporary damage-lookup petrification. A mortal corpse can end up inside a statue building, with an unchecked holder admission (FR-02) [V].
- Kindred commits the record and scar before its side effects, with no rollback [V/I].

**Native and existing framework**
- Temporary forms: a lease hediff (stats, capacities, damage factors) plus a render node or form rig on the same pawn [I]. This activates through the existing cast service as a self-target worker, as planned in architecture slice S3.
- Permanent bodily matter (Bronze Skin, Stone Skin, Iron Bone): the S1 pattern, with permanent hediffs and stat and damage modifiers [I].
- **Part-local Gu weapons:** a native added-part or verb-giver hediff (blade arm, bone spurs). Native surgery and added-part paths were traced in Stage 6A [V], and verb-granting hediffs are a standard native mechanism [I].

**Custom engineering**
- Rendering for a giant form (see section 3).
- A per-transformation lease with explicit end and reconnect-on-load [I].
- Material reaction policy: which damage types each body matter resists or amplifies [I].

**Limits and risks**
- **Multi-cell pawn occupancy isn't native.** Kindred never achieved it [V]. Giants should stay one-cell pawns with large art and large *attack reach*. A real footprint would mean custom pathing and occupancy: very high risk [I].
- Changing a pawn's body graph (human to quadruped) needs a proxy or race swap, with every custody risk in AN-002 [V/I]. Avoid this unless the gameplay truly needs it.

**Opportunities beyond Kindred**
- **Transactional transformations:** prepare, commit and roll back, with no orphaned scars [I].
- **Partial transformations** (one limb, the skin, the bones) as first-class Gu content. This is closer to Reverend Insanity than wholesale forms, and more native-friendly [I].
- **[S] Material tags with world interactions:** wood-path bodies burn, metal bodies conduct lightning effects, stone bodies sink in water.

## 5. Permanent cultivation, regeneration, missing-part restoration, zombie physiology, survival, resurrection

**What Kindred demonstrated**
- Head-loss survival by postfixing the native coma-instead-of-death predicate [V].
- A saved regrowth hediff with a work bank that restores real missing parts, parent before child, through `RestorePart`. Restoration is fueled by hemogen and keeps selected implants [V].
- *Life Orbs*: intercept both the health predicate and direct `Pawn.Kill`, debit on a later tick, and repair the original [V].
- *Seal of Hours*: native `TryResurrect` on a real corpse [V].
- A survival matrix ordered by Harmony priority (serum 600/900, orbs 400/800, Vital Weave 700…) with **no single global final-death switch** [V].
- Live regeneration on Fenris and the Crawler. Neither proves survival of a truly lethal head loss [V].

**Native and existing framework**
- Bodily cultivation is the S1 design (pawn hediffs).
- Regeneration Gu: a hediff with a cadenced work bank calling `RestorePart`, with no Harmony needed for live regrowth [I, mirrors V].
- Resurrection Gu: native `ResurrectionUtility.TryResurrect` [V].
- Zombies: native mutant framework if it's usable without Anomaly [UNVERIFIED]; otherwise a hediff suite that disables needs, pain and so on [I].
- The registry already reacts to `Hediff_Aperture` on death [V, Project GU source].

**Custom engineering**
- **Survival Gu that prevent death need Harmony** at `ShouldBeDead` / the coma predicate and at `Pawn.Kill` [V].
- Project GU should own **one survival arbiter**: one patch per native method, one decision per death event, one saved recovery record [I].
- **Death and Gu custody:** what happens to aperture Gu when a master dies, revives or becomes a zombie. This is a Project GU-specific problem that Kindred never faced [I].

**Limits and risks**
- Death hooks compete with Biotech Deathrest and Deathless and with other resurrection mods [V/I].
- Saving during a deferred return [V/I].
- Implant and graft state lost through regrowth [V].
- Duplicate identity if a corpse and a revived pawn coexist [I].

**Opportunities beyond Kindred**
- **The master's death changes the world** [S, high value]: aperture Gu die, scatter or go wild, feeding the ecology system as new wild individuals with history. In Reverend Insanity a master's death matters to their Gu. No other RimWorld mod can do this, because none has individual Gu identity.
- **One deterministic survival authority**, instead of Kindred's priority race [I].
- **Regeneration as a cultivation investment** (bone and flesh Gu working like S1) rather than a free power [I].

## 6. Beast kings, summoned creatures, controlled entities, pack behavior

**What Kindred demonstrated**
- Summons with an expiry hediff, guard Lords, trainer and master links, and staggered batch spawn [V].
- Risen: native resurrection plus mutant initialization, with a servant cap that evicts the oldest [V].
- Living Shadow: a separate linked pawn [V].
- Domination: a temporary faction change [V]. Puppetry: a mental state [V].
- Innate summon powers via a 20-tick staggered map scan [V].
- Bespoke bosses: the Crawler (custom HP pool, phases, underground holder) and the Walker (shield and artillery). Walker salvos aren't saved [V].
- Pack hunting exists [V], but no large-scale tide was traced.

**Native and existing framework**
- Beasts are native animal pawn kinds. Leadership and tier (Hundred, Thousand and Myriad Beast King) can be a hediff with an aura [I].
- Native Lords, LordJobs, training and master relations, and manhunter pack incidents [I].
- Project GU's wild Gu population pipeline (Slice 0 and 1A) already demonstrates *independent populations that don't displace vanilla wildlife*. This is the right foundation for beast populations [V, Project GU docs].
- Beast control Gu: a domination-style lease with an explicit binding (EA-001) [I].

**Custom engineering**
- Pack and tide group behavior: a leader-centered Lord with cohesion and morale that breaks when the king dies [I].
- Beast tier generation rules [I].
- **Gu living in beasts** (parasitic and mutualistic). The registry currently authorizes abilities only from a cultivator's aperture [V, source], so beast-hosted Gu need a separate grant path for innate powers [I].

**Limits and risks**
- **Pawn count is RimWorld's main performance cost** [I]. A wolf tide of hundreds of simulated pawns isn't realistic. Simulate only the front of the tide on the map and keep the rest as an off-map aggregate [I].
- The owner's rule against displacing native wildlife must hold for every beast system [V, owner directive].

**Opportunities beyond Kindred**
- **[S] Beast kings as persistent world entities** with territory on the world map (compact world records, EC-002 pattern). Tides become world events that move toward settlements. Killing or enslaving a king reshapes regional ecology and threat.
- Kindred's bosses are raid add-ons; Project GU's would be **ecology-driven**.
- **[S] Mutualist Gu-beast relationships** that players observe and exploit, with no player-managed feeding, as already directed.

## 7. Autonomous Gu Master combat: tactics, defensive reactions, essence conservation, retreat

**What Kindred demonstrated**
- A hash-staggered 30-tick consideration that picks **one** action per beat [V].
- Candidate scoring (buff, shield, heal, summon, area, engage…) [V].
- **Resource reserve policy**, the direct analogue of essence conservation [V].
- Shields scored by line-of-sight ranged threat [V].
- A friendly-fire veto for area effects, and claims that prevent duplicate heals [V].
- Short-lived tactical memory [V].
- Best-effort reconstruction of an interrupted player order [V].
- **No multi-step planning** [V/UNVERIFIED].
- Area scoring can become quadratic in enemy count [V].

**Native and existing framework**
- `GuAbilityService.CanCast` and `ValidateTarget` are already independent of whoever is deciding [V, source].
- A cadenced ThinkNode or JobGiver offers casts; native jobs perform them [I].
- Native AI already handles fleeing, raid breaking and self-defense [I].
- **[I, notable] Native verb route:** exposing a Gu attack as a hediff-given verb lets native combat AI use it with no custom AI. Every shot would have to route through `TryCommit` for registry authorization, payment and cooldown. It's attractive, but needs a careful spike.

**Custom engineering**
- Offer scoring by Gu path and role [I].
- Reserve thresholds against current threat [I].
- Retreat triggers (low essence, injury, outmatched) that use native flee [I].
- Reactive defense: one typed resolver at `Bullet.Impact` or `PreApplyDamage` (CB-003) [I].

**Limits and risks**
- Cost at scale: many NPC Gu Masters plus Society Core cohorts [I].
- Player expectations about control: opt-in and pause matter, as in Kindred [V].
- Friendly fire around allies [V/I].
- ThinkTree insertion order relative to Society Core's own think-tree node [I].

**Opportunities beyond Kindred**
- **[S, distinctive] AI limited by information:** NPCs know only the Gu they've seen used or learned about through investigation and Society Core. Kindred's AI sees everything.
- **[S] Fights with motives:** surrender, ransom, fleeing to protect a household, vendettas. Society Core supplies the *why*; Project GU supplies the *how* [boundary V, from the briefings].
- **[I] Essence as an economic decision:** NPCs weigh primeval-stone cost, not just a resource pool.

## 8. Investigative and concealment Gu

**What Kindred demonstrated**
- No investigative-power or concealment family was traced [V by absence].
- The closest mechanisms are deeds, witnesses, delayed leaks and decay, and masquerade heat, each with separate fact and publication records [V].

**Native and existing framework**
- Native invisibility hooks exist (`Notify_BecameInvisible`, `IsPsychologicallyInvisible`, `HediffComp_Invisibility`) [V, presence only].
- Fog of war, letters and world-site discovery are native [I].
- **Project GU's registry records exact Gu identity and custody transitions with transaction keys** [V, source]. That makes it an unusually rich, *legitimate* evidence source: a tracking Gu can follow one specific Gu individual's custody chain to a thief or a hidden inheritance [I].

**Custom engineering**
- A bounded, decaying trace store (who, where, when, confidence, concealment rank), written when facts are committed [I].
- Rank-versus-concealment contests [I].
- An evidence UI [I].
- A Society Core hand-off for interpretation [boundary from the briefings].

**Limits and risks**
- Omniscience creep (querying engine logs that "know everything") [I].
- Save growth unless traces decay [I].
- Native AI behavior against invisible pawns needs checking [UNVERIFIED].

**Opportunities beyond Kindred.** Effectively all of this area:
- **[S]** Tracking that yields *uncertain* evidence (a direction, a radius, an age).
- **[S]** Counter-investigation Gu that plant false trails.
- **[S]** Concealment rank contests.
- **[S]** Scrying inheritance sites as uncertain world-map regions.
- **[S]** Theft investigations that run through Society Core reputation.

This can become a signature Project GU system.

## 9. Multi-Gu killer moves

**What Kindred demonstrated**
- *Joint Magic*: a group ritual. At least 3 participants run native channel jobs, each pays when joining, and a fizzle after payment is possible. Beam records are saved [V].
- Apex forms schedule subordinate powers [V].
- No composition of multiple sources into one technique [V by absence].

**Native and existing framework**
- A killer-move definition lists its core and supplementary Gu. Admission calls `IsAbilityGrantedBy` for each component, pays once, starts every component's cooldown, and commits one cast record listing all of them [I]. This is consistent with the architecture review.
- The effect uses existing workers (projectile, field, lease).

**Custom engineering**
- **One bespoke worker per killer move.** Reverend Insanity killer moves are individually distinctive. Share admission and payment only. A generic composition engine is the trap here [I].
- Handling partial killer moves (a missing supplementary Gu makes a weaker form) [I].
- Cooperative killer moves with several casters, using Joint-Magic-style receipts per participant [V pattern, I application].

**Limits and risks**
- A component Gu that dies, starves or is withdrawn mid-warmup [I].
- Exploding test combinations [I].
- Balance, because killer moves are force multipliers [I].

**Opportunities beyond Kindred**
- **[S] Discovering killer moves:** a cultivator deduces a predefined killer move through study once they own the right Gu combination. Freeform generation isn't realistic; discovery of authored definitions is.
- **[S] Killer moves as transferable knowledge:** inheritance scrolls and teaching through Society Core courses (PI-002).

## 10. Rank 6+: immortal combat, dao marks, immortal essence, large-scale environment, blessed lands

**What Kindred demonstrated**
- Apex scenes (Fenris, Seraph) and endgame bosses [V].
- Pocket realms via native `PocketMapUtility`, with a parent and return record and a drain and cleanup protocol [V].
- World-level caravan relocation (Moon Bridge) [V].
- Saved environmental fields [V].
- Off-map domain yields [V].
- Nothing analogous to dao marks [V by absence].

**Native and existing framework**
- Project GU **already has** independent underground levels on a custom planet layer and pocket-map handling [V, source and docs]. That's the proven base for immortal apertures and blessed lands (RFC-0006, option A).
- Native game conditions give map-wide effects for immortal-scale weather or pressure [I].
- Species `pathTags` already exist as metadata [V, source]. A dao-mark ledger keyed by path could scale path-tagged effects [I].
- Immortal essence is a second payer behind the payment call [I, as in the architecture review].

**Custom engineering**
- A dao-mark ledger and its interference rules [I].
- Bounded large-area effect records [I].
- Off-map aggregate simulation for unloaded apertures and blessed lands (the hybrid model already in Society Core's direction and RFC-0006) [I].
- Immortal tribulations as scaled incidents [S].

**Limits and risks**
- **Every active map costs simulation time** [V, Kindred SP-001]. Many concurrent immortal apertures can't all stay loaded.
- Map size caps the physical scale of effects. A rank 8 attack can't be bigger than the map [I].
- Balance against a mortal-majority world [I].
- Save size [I].

**Opportunities beyond Kindred**
- **[S] Blessed lands as persistent living worlds** with their own resources and ecology, simulated as aggregates while unloaded and in full while visited.
- **[S] Dao marks** gathered from cultivation, environment and Gu use, with visible markings (section 3) and interference that makes build choices meaningful.
- **[S] Different time flow** in a blessed land is possible only as aggregate catch-up while unloaded. RimWorld ticks every loaded map at the same rate [I].

## 11. Performance and compatibility alongside Society Core and ordinary RimWorld

**Kindred evidence**
- Uses hash staggering, bounded queues and culling [V].
- Its caps are eviction policies with exceptions [V].
- It runs periodic all-pawn reconciliation scans [V].
- Area AI scoring can be quadratic [V].
- It uses many Harmony patches on hot methods [V].
- **No measurements exist** [V].

**Main coexistence risks** [I unless marked]

| Risk | Why | Mitigation |
| --- | --- | --- |
| Pawn count (tides, summons, NPC Gu Masters, Society cohorts) | The dominant CPU driver in RimWorld | Aggregate off-screen; spawn only the active front; cap summons |
| Two systems inserting think-tree nodes (Project GU combat, Society Core ambitions) | Double admission, order fights | A documented priority: emergency > player > Gu combat > Society ambitions. Both systems already defer to native jobs [V, Society Core README] |
| Hot-path Harmony patches (`PreApplyDamage`, `Bullet.Impact`, `RenderPawnAt`, `ShouldBeDead`, `Kill`) | Run for every pawn and every hit | One Project GU-owned patch per method, exiting early for actors Project GU doesn't own (PF-002) |
| Combat Extended | Replaces projectiles | Decide whether to support it; if so, use an adapter boundary at the projectile worker |
| HAR and render mods | Render-node and override conflicts | Prefer render nodes; test with HAR |
| Biotech Deathrest/Deathless, other resurrection mods | Death-hook ordering | A single survival arbiter; explicit compatibility tests |
| Active maps (underground, pocket, immortal) | Each loaded map simulates | Load on demand; aggregate while unloaded |
| Texture memory from giant art | Unmeasured in Kindred | Lazy-load only when the art volume demands it; measure first |

**Beyond Kindred**
- Shared dev-mode counters across Project GU and Society Core: active casts, sessions, fields, FX instances, candidates scored per tick and pawns admitted. Kindred has no such counters, and Society Core's Stage D method already shows the measurement discipline [V, Society Core docs].

---

## Ambition tiers

| Tier | What's realistic |
| --- | --- |
| **Near: existing framework, a slice each** | Bodily cultivation (S1/S2); projectile variants; area bursts; dashes and knockback via `PawnFlyer`; phantom render nodes; permanent bodily-matter hediffs; live regeneration; simple player-only defense |
| **Mid: one contained custom subsystem each** | Saved staged and sustained strike record; temporary transformation leases with form art; autonomous Gu Master offers and retreat; a typed defensive resolver; a survival arbiter; beast tiers and pack Lords; the trace/evidence store; killer-move admission with bespoke workers |
| **Long: major subsystems** | Information-limited AI with Society Core motives; ecology-driven beast kings and world tides; master-death Gu scattering; blessed lands and immortal apertures as aggregate worlds; dao-mark ledger; cooperative killer moves |
| **Avoid or work around** | Real multi-cell pawn footprints; z-axis aerial combat; body-graph swaps without a compelling need; hundreds of simultaneous beasts; many permanently loaded maps; a generic killer-move composition engine |

## Kindred patterns not to copy

- An all-owning per-pawn component (`CompKindred`). Project GU already separates its authorities.
- VEF as the ability backbone. Project GU's own cast path is stricter.
- Unsaved pending attacks and flights.
- Committing state before side effects succeed, with no rollback.
- Survival handled by many independently prioritized hooks instead of one arbiter.
- Periodic all-pawn repair scans where event-maintained indexes would do.
- Treating instance caps with exceptions as memory bounds.
- Texture readback for damage masks, and `MaterialPool` lazy loading before there's a measured need.

## Highest-value next checks before content work (small, read-only)

1. What `Pawn_FlightTracker` supports in 1.7: targeting, acting while airborne, pathing.
2. Whether the mutant framework works without Anomaly active.
3. Whether a beast phantom can be a render node and `AnimationDef` with no Harmony.
4. Whether a hediff-given verb can route through `GuAbilityService.TryCommit`.
5. A Combat Extended support decision for the owner.
