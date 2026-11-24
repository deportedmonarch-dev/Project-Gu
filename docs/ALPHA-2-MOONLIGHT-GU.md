# Playable Alpha 2 — Moonlight Gu combat slice

Alpha 2 adds one finished Gu combat ability on top of the accepted Alpha 1 integration `376741e` (`feature/alpha1-clan-gu-loop`). A Gu Master can obtain a wild Moonlight Gu, refine it, store it in their aperture and fire **Moonblade**: a visible crescent projectile that deals native damage. Gameplay does not depend on the visual effects.

## Play it

1. Start a new **Southern Border Clan** game (Core, Harmony, Project GU). Awaken a disciple at the spirit spring as in Alpha 1. Every newly generated spring holds **two mature Moonlight Gu** next to its Hope Gu.
2. Select the spring and choose **Search for Moonlight Gu**. Right-click the wild Moonlight Gu with the awakened disciple and choose **Capture Moonlight Gu (unrefined)**, then **Pick up dormant Moonlight Gu**.
3. Choose **Manage aperture Gu → Enforce will over Moonlight Gu**. Rank 1 costs 10 essence. The duration depends on aptitude: about 4.4 in-game hours at D-grade aptitude 22 and 1.3 hours at B-grade aptitude 73.
4. When the Gu recognizes the disciple, choose **Manage aperture Gu → Store Moonlight Gu**. **Inspect aperture Gu** lists the Gu's abilities.
5. A **Moonblade** command now appears for that Gu Master, drafted or not. Click it, then click a pawn or a ground location within the white range ring (22 cells, line of sight required). The Gu Master aims for about 0.6 seconds (35 ticks), then looses the blade.
6. Each launch costs **3 primeval essence**, paid when the blade leaves. The Gu then recovers for 2.5 seconds (150 ticks). The command shows the reason when it is unavailable (recovering, too little essence, Gu not stored, caster downed).
7. **Options → Mod settings → Project GU → Gu combat visual effects** turns the launch, trail and impact effects off. Projectiles, damage, costs and cooldowns stay the same.

Withdrawing the Gu from the aperture, giving it to someone else, losing it, or leaving it unrefined removes the command immediately. A Gu bonded to another cultivator never grants it.

## Gu Framework integration

- **Species:** `GU_MoonlightGu` is now an ordinary wild spring species with its own wild Pawn and dormant Thing projections (`Defs/ThingDefs/GU_MoonlightGu.xml`) and `abilities: GU_Moonblade`. Species declare abilities as data; no code names Moonlight Gu.
- **Acquisition:** `CompProperties_GuSpiritSpring.initialCohorts` seeds extra species cohorts once, only into a newly generated spring. The existing generic encounter command, cohort debit, capture, pickup, will-enforcement and storage paths handle Moonlight Gu unchanged. Existing Alpha 1 springs are not migrated: they have no Moonlight cohort, so only new games offer the acquisition route.
- **Authorization:** `GuIndividualRegistry.Abilities.cs` grants a species' abilities only while the exact individual is living, resolved, not quarantined, not under refinement, contained in the live mortal aperture of an awakened, conscious, spawned cultivator, and bonded to that same cultivator. Every check runs against the registry's live bindings, with no cached ability state. Every other placement grants nothing.
- **Essence:** casting uses the aperture's existing `TrySpendEssence`. The one addition, `RestoreUncommittedEssence`, returns a debit only when the launch fails within the same commit tick.

## Combat architecture

| Concern | Owner |
| --- | --- |
| Eligibility and targeting | `GuAbilityService.CanCast` (registry authorization, cooldown, essence) and `GuAbilityWorker.ValidateTarget` (range, minimum range, line of sight, pawn or location, never self) |
| Pending order | `JobDriver_GuCastAbility` (native job, aim toil) plus the `GuCastIntent` the ledger stores by job ID |
| Commitment | `GuAbilityService.TryCommit`: one tick, revalidate everything, debit once, launch, start the per-Gu cooldown, record the cast |
| Projectile and damage | `Projectile_GuAbility`, a native `Bullet`. The engine moves it, saves it with the map, chooses the hit and applies `DamageInfo`. A saved `resolved` flag plus the ledger's claim allow exactly one impact. |
| Facts | `GuCombatLedger` (GameComponent): cooldowns per Gu ID, intents, in-flight casts, the last 32 resolved outcomes |
| Presentation | `GuFxMapComponent` (MapComponent), `GuFxProfileDef`, `GuFxAssets`. Cosmetic presentation only. |
| Cleanup | Job finish removes the intent. Projectile removal without contact records `EndedWithoutImpact`. Effects expire by ticks or when their anchor despawns. The ledger records a cast as `Lost` only after 2,500 ticks with no live projectile. |

**Cost and cancellation policy.** Nothing is spent before launch. Drafting, a new order, death, downing, the Gu being withdrawn or transferred, a target dying or becoming invalid, a cooldown, or too little essence all cancel the cast with no debit and no cooldown. The player sees "cancelled: … No primeval essence was spent". At launch the essence is spent once and the cooldown starts. Nothing that happens to the caster, the Gu or the target afterwards refunds it. A blade whose target vanishes in flight flies on to the target cell and strikes whatever native rules choose there, or the ground.

**Save and load in flight.** The projectile is a native map Thing and keeps its cast ID and resolved flag. The debit is already in the saved aperture essence, and the ledger saves the in-flight record. After loading, the projectile resumes from the same tick count and strikes once. Nothing is debited again. Historical effects are not replayed: only the live projectile re-attaches its travel accent.

**Reuse.** The minimum shared interface is `GuAbilityDef` (data), `GuAbilityWorker` (`ValidateTarget`, all-or-nothing `TryLaunch`), the service and the ledger. There is no general ability engine.

## Visual presentation

- **Projectile:** the native projectile graphic, a 128 px crescent drawn at 1.5 cells with the `MoteGlow` shader. It is rotated to the projectile heading by the engine and is always drawn.
- **Launch flash, travel trail, impact burst:** `GuFxProfileDef` layers drawn by the map component in `MapComponentDraw` with per-frame UV quads and native `MaterialPool`/`FadedMaterialPool` materials. Frames advance by simulation ticks, so pause and game speed apply.
- **Bounds:** at most 64 instances per map, with the oldest evicted first. Instances are culled to the camera view rectangle (expanded by 3 cells) before drawing and expire by ticks on every map. They are cleared when the map is removed or effects are switched off. Nothing is saved.
- **Placeholder art:** original, generated by `scripts/generate-moonlight-placeholder-art.ps1`. Replace any PNG with hand-drawn art of the same size and grid. No code or XML change is needed.

### Asset specifications

All PNGs are 32-bit RGBA with straight (non-premultiplied) alpha and transparent padding. They use the `MoteGlow` shader unless noted, which brightens: author the core near white with a colored falloff. Art points **up** (toward the top of the image) in the direction of travel. Each frame's pivot is its cell center. Atlases read left to right, then top to bottom, and frame 0 is the top-left cell.

| File (`Textures/ProjectGU/…`) | Size | Grid / frames | Timing | World size | Notes |
| --- | --- | --- | --- | --- | --- |
| `Effects/Moonblade/Projectile.png` | 128×128 | 1 | — | 1.5×1.5 cells | Crescent with convex leading edge up, horns trailing down |
| `Effects/Moonblade/Launch.png` | 512×128 | 4×1, 4 frames | 3 ticks/frame, 12-tick life, fade out 4 | 2.2 cells, 0.7→1.2 scale, 0.6 cells ahead | Opening flash at the hand |
| `Effects/Moonblade/Trail.png` | 512×128 | 4×1, 4 frames, looping | 3 ticks/frame | 1.1×2.2 cells, 1.0 cell behind | Streak; the top of each cell touches the blade |
| `Effects/Moonblade/Impact.png` | 512×256 | 4×2, 8 frames | 3 ticks/frame, 24-tick life, fade out 6 | 3.2 cells, 0.8→1.15 scale | Frames 0–2 carry a bright core flash |
| `UI/Abilities/Moonblade.png` | 64×64 | 1 | — | command icon | Standard transparent-shader icon |
| `UI/ManageAperture.png` | 64×64 | 1 | — | command icon | Also fixes Alpha 1's missing icon |
| `Gu/MoonlightGu/Wild_north/_east/_south.png` | 64×64 each | `Graphic_Multi` | — | 0.85 cells | West is mirrored from east |
| `Gu/MoonlightGu/Dormant.png` | 64×64 | 1 | — | item | |

To add layers or change timing, edit the `GuFxProfileDef` entries in `Defs/GuAbilityDefs/GU_Moonblade.xml` (columns, rows, frameCount, ticksPerFrame, lifetimeTicks, drawSize, start and end scale, fades, color, shaderType, altitudeLayer, forwardOffset, loop).

## Verification

Environment: Windows 11, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195 (Waylith), Core and Harmony.

| Kind | Result |
| --- | --- |
| Static and structural | `verify-gu-framework.ps1` checks the Moonlight species, projections, ability Def and every texture. `verify.ps1 -Focused` passes. |
| Real compilation | Release build against the installed 1.7 assemblies: 0 warnings, 0 errors. |
| Managed (real assemblies, no game process) | 18 combat checks: exact debit and restore bounds, once-only commitment, cooldown arithmetic, Scribe round trips of intents, in-flight and resolved casts, refused second impact after reload, bounded history, frame, fade and scale timing, atlas UV order and profile validation. |
| Native headless, `Alpha2` profile | See results below. |
| Native rendered, `Alpha2Visual` profile | See results below. |

Commands:

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile Alpha2 -StageTimeoutSeconds 600
.\scripts\verify-priority0-live.ps1 -Profile Alpha2Visual -Parallel 1 -StageTimeoutSeconds 900   # opens a game window
```

### Native acceptance (`Alpha2`)

Stage **new**: real Southern Border start with Development Mode off, ordinary search, capture, pickup, will-enforcement and storage, then:

1. The unrefined Gu, in inventory, grants nothing and cannot be stored.
2. The stored, refined Gu offers an enabled Moonblade command to its disciple and nothing to the elder.
3. Each cast debits exactly 3.000 essence. An independent per-tick account of regeneration and debits matches the aperture to within 0.001 after every phase.
4. Drafting during aim, withdrawing the Gu during aim and destroying the target during aim each cancel with no debit, projectile or cooldown. A withdrawn Gu stops granting immediately and grants again once restored.
5. One projectile per cast and one impact. Native damage is applied and no new injury appears afterwards.
6. Identity, bond and eligibility survive reload (stage **reload**).
7. A save with a blade in flight reloads the same unresolved projectile at the same tick count. It strikes once after reload with no new debit or commitment.
8. With effects disabled, a cast pays the same 3.000 essence and the same 11 damage and schedules no effects.
9. Self, out-of-range and unaffordable targets are refused before any job starts. A target that vanishes mid-flight resolves safely.
10. All launch, trail and impact instances expire, and no projectile lingers.

Stage **final**: after another fresh process, a withdrawn Gu keeps its identity and bond but grants nothing, and storing it again restores Moonblade.

Observed damage on muffalo targets ranges from 11 to about 19.6 per 11-damage cast. This is vanilla `Cut` cleave behavior (`DamageWorker_Cut` splits a cut across extra parts with a cleave bonus), not repeated impacts. The ledger records the native `DamageInfo` amount separately.

### Results

| Campaign | Outcome |
| --- | --- |
| `8197e3f3bb504d8ba8676aa3da780357` | `Alpha2` passed new, reload and final with zero red errors. |
| 14 repeated `Alpha2` campaigns | 12 passed cleanly. 1 passed, but the runner marked its timing invalid because the laptop briefly slept. 1 (run 8) failed on a harness assumption: the location case expected a ground hit, but native `ImpactSomething` legitimately struck something in the cell. The case now picks an empty cell and accepts either native outcome, and runs 9–14 passed with it. |
| `c7d9529454cd4de8ae85a21e137ffd82` | Regression with `-Parallel 3`. `Alpha2`, `Alpha1`, `Slice3B3B`, `Slice3B3C`, `Slice3B5A`, `ClanStart` and `ClanSession` all passed. This covers Hope Gu awakening, acquisition, capture, refinement (including active-refinement reload), aperture storage, multi-cohort persistence and the clan economy with a Moonlight cohort present. |
| `c03e24cad46843319d0776e7621d1bdc` | One unexplained failure, in the headless `new` stage that precedes the rendered stage. The check that the refined Gu is the sole aperture occupant with no physical projection failed. It has not recurred in 14 later runs. The assertion now records the occupant count, whether the dormant Thing was destroyed and every projected Thing, so a recurrence will identify the cause. It is treated as an open harness-or-framework observation, not an accepted defect. |

### Rendered inspection (`Alpha2Visual`)

The final rendered campaign is `4c9379fc3d7a45d0a7fb505a7ad51d42`, after the visual fixes; the earlier one was `dfcbcd07f8394504afd02adc97ba745a`. Both are Vulkan windowed runs at 2560×1600 with 88 screenshots each, in `tests/obj/Priority0-<campaign>/Alpha2Visual/alpha2-screenshots/`. The disciple walked to the surface through the ordinary passage. The run then cast in eight directions, at Normal, Fast and Superfast, at close and far zoom, with the camera off-screen, and with effects off.

Inspected by eye:

- **Command bar:** the Moonblade command shows its icon. The native targeter shows the range ring.
- **Projectile:** the crescent's convex edge leads in all eight directions, and the trail streams behind toward the caster.
- **Launch flash:** appears at the hand.
- **Impact:** the 8 frames play in order over 24 ticks, with a core flash, a slash, a ring and a fade.
- **Effects off:** the projectile is still visible, with no trail or burst.

Measured in the run: every cast paid exactly 3.000 essence and hit; the off-screen cast drew 0 effect submissions and still resolved; 42 effect instances were spawned and 42 retired.

Defects found and fixed after the first rendered run:

- The impact read too small and faint against the native red damage flash. It now has a 3.2-cell draw size, a thicker slash and a core flash in frames 0–2.
- The launch flash was enlarged to 2.2 cells.
- Alpha 1's **Manage aperture Gu** command drew the engine's missing-texture red X. It now has an original icon.
- Harness fixes: the caster is drafted so it no longer wanders off-camera, the camera re-centres before each cast, and the runner's log-isolation check now reads past Vulkan's startup output.

Known renderer limitation: the minimum camera zoom clamps the "close" shots, so they are only slightly closer than the default view.

## Remaining manual checks

The rendered run plays scripted casts and captures screenshots, but it cannot judge feel. In a normal game, check by hand:

1. **Targeting:** the cursor icon and white range ring follow the mouse. Out-of-range or blocked cells cannot be confirmed. Right-click cancels.
2. **Timing:** a few casts at speeds 1, 2 and 3 to confirm the aim time reads as intentional and the trail keeps up with the blade.
3. **Night and caverns:** cast at night and underground. The glow shader should stay legible, and the trail should not look harsh in darkness.
4. **Interaction:** cast while drafted in a real fight, with several colonists selected (each Gu Master keeps its own command), and with two Moonlight Gu in one aperture (the commands show a short ID suffix).
5. **Settings:** toggle effects off mid-flight. The current effects vanish and the blade continues.

## Performance

Simulation work runs per cast: a few registry lookups when ordering and committing, one native projectile, and ledger maintenance every 250 ticks only while records exist. Effects tick only on maps with live instances. Drawing is culled to the camera view, measured as 0 draw submissions with the camera away from the action. With the camera on the action, each cast peaks at 2 effect instances in flight plus the impact. Command gizmos query the registry whenever the pawn's gizmos are built. Frame time and GPU cost were **not measured**, and no performance claim is made.

## Next extension point

A second projectile Gu needs only XML: a `GuAbilityDef` with its costs, timing, range and FX profiles, a projectile ThingDef whose `thingClass` is `ProjectGu.Combat.Projectile_GuAbility` (or a subclass), and the ability added to the species' `abilities` list. A non-projectile Gu, such as a self-buff or a melee strike, adds a `GuAbilityWorker` subclass and implements `ValidateTarget` and an all-or-nothing `TryLaunch`. The service, ledger, cancellation policy and presentation component are shared. Defensive interception (White Jade Gu) would hook `Projectile_GuAbility.Impact` or native `Bullet.Impact` at one resolver, per Kindred CB-003.
