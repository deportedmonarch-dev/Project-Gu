# Alpha 4A — The Aperture Interface and Living Primeval Sea

Alpha 4A adds a native **Aperture** tab to the inspection pane of every pawn that carries an aperture. It brings Project GU's cultivation, Gu custody and technique information into one interface with four pages: **Cultivation**, **Gu**, **Techniques** and **Foundation**. It also adds a first visible effect for Gu will-enforcement.

Baseline: accepted `07b2f5d` (Alpha 3 with the spirit-spring encounter and Gu generation-integrity fixes). Branch: `feature/alpha4a-aperture-tab`.

No gameplay value changed. Essence costs, conversion rates, cooldowns, interruption rules, refinement time, will-enforcement authority, capacity and custody rules are exactly as accepted in Alphas 1–3.

## Play it

1. Select an awakened Gu Master and open the **Aperture** tab in the inspection pane (beside Health, Needs and the other tabs).
2. **Cultivation** shows the living aperture and the cultivator's actual values:
   - **Rank and stage:** Rank 1 and the actual minor stage, with the essence name (Green Copper Primeval Essence).
   - **Primeval sea:** quantity against the shared capacity, and an exact breakdown by quality. A quality the sea doesn't hold is dimmed, and your own stage is marked.
   - **Recovery:** natural recovery per hour, and the quality that recovered essence takes.
   - **Aperture walls:** nourishment progress toward the next minor stage, and how much each quality nourishes.
   - **Activity:** the running cultivation job or channel.
   - **Buttons:** **Nourish aperture walls**, **Advance to … Stage** and **Liquor Worm refinement**. An unavailable button explains why on hover.
3. **Gu** lists each Gu as an individual card:
   - **Shown:** the Gu stored in this aperture, the dormant Gu the cultivator carries, and dormant Gu on the ground within 8 cells that this cultivator can act on.
   - **Card:** the species' own sprite, rank, role, placement and recognition.
   - **Selected card:** the full detail: recognition, custody, condition, any will-enforcement progress, the techniques it grants, its identity and custody revision. It also shows the actions available now: **Store in aperture**, **Withdraw to inventory**, **Enforce will**, **Resume will-enforcement** or **Abandon will-enforcement**.
4. **Techniques** is a reference to what the cultivator's own refined Gu can do, with current numbers from the Defs. It has no buttons. Moonblade is still cast from the pawn's command bar, and Liquor Worm refinement is controlled on the Cultivation page.
5. **Foundation** shows aptitude grade and percentage, sea capacity, recovery rate and realm, plus special physique when one exists.

The existing **Manage aperture Gu** command remains as a fallback. It now builds its options from the same action layer as the tab, so the two always offer the same operations.

## What the player sees on the orb

- **Spherical boundary:** the aperture's shell, lightly tinted by the cultivator's actual minor stage.
- **Primeval sea:**
  - **Fill:** it fills the sphere by area, not by height, so half the capacity reaches the equator.
  - **Layers:** each quality of essence forms its own coloured layer. Higher quality is denser and settles lower: Peak at the bottom, then Upper, Middle, and Initial on top. An Initial-stage cultivator holding Liquor Worm-refined Middle essence therefore shows a jade layer over a pale layer. The sea is never shown as a uniform quality the pawn doesn't exclusively hold.
  - **Thin layers:** a trace amount keeps a minimum 1.5-pixel layer so it stays visible. The numbers stay exact.
  - **Motion:** the surface moves in a slow two-part wave, with caustic shimmer and a bright lip. The empty space above holds a faint mist.
- **Wall ring:** 24 crystalline plates just inside the boundary. The lit arc, clockwise from the top, is the nourishment gathered toward the next minor stage. At Peak the ring is complete.
- **Activity (only from a live job or saved session):**
  - **Wall nourishment:** the lit arc breathes.
  - **Liquor Worm refinement:** currents in the next quality's colour swirl through the sea.
  - **Will-enforcement:** the surface stirs harder and dim counter-currents turn.
- **Condition overlays:** see [Conditions](#aperture-conditions).
- **Other factions:** the orb of a pawn outside the player's faction shows only its structure. Its sea is concealed.

The tab's composition bar under the orb shows the same breakdown proportionally against capacity, with exact values in its tooltip.

## Disclosure and authorization

| Viewer | What is shown | Controls |
| --- | --- | --- |
| The player's own colonist cultivator | Everything | All actions, each revalidated when clicked |
| The player's prisoner, slave or tamed nonhuman host | Everything the game knows | None (read-only); only a colonist's own body may act |
| Anyone else (visitors, enemies, wild animals) | Rank and stage only; essence, aptitude and Gu are concealed | None |

The tab appears for any pawn race. It is added to every pawn `ThingDef` at startup and hides itself unless the pawn has exactly one aperture. It makes no human-only assumption, and the native test exercises a nonhuman host.

## Architecture

All new presentation code lives in `Source/ProjectGu/Interface/`. None of it saves anything (a managed check enforces this).

| Type | Role |
| --- | --- |
| `ITab_Pawn_Aperture` (+ `.Cultivation`, `.Gu`, `.Techniques`, `.Foundation`) | The native inspection tab. It sizes itself to the space above the inspection pane at any UI scale, and every page scrolls. |
| `ApertureView` | A read-only snapshot rebuilt from the aperture, registry, combat ledger and current job four times a second while open, and at once after any action. It holds no authority between refreshes. |
| `GuApertureAction` / `GuApertureActions` | One description per available operation (nourish, advance, channel, store, withdraw, enforce, resume, abandon), built from authoritative state. Running one calls the existing registry, aperture or channel service, which revalidates and commits. The tab and the fallback menu both use them, and the tab never calls a commit method directly (a managed check enforces this). |
| `ApertureOrbRenderer` | Composites the orb on the CPU into one 256×256 texture, at most 30 times a second while visible. Animation follows real time; there is no tick work and no world scan. |
| `PrimevalSeaLayout` | Pure geometry: area-to-row conversion and quality layering. Covered by managed tests. |
| `GuApertureRankDef` | Rank terminology, essence colours and art paths. Presentation only. |
| `GuApertureConditions` / `IGuApertureConditionSource` | The reusable vitality system. See [Conditions](#aperture-conditions). |
| `GuPortraits` | Card portraits: the wild south sprite, then the dormant sprite, then a neutral fallback. Cached per species. |
| `GuApertureFoundationSectionDef` | Extension point for future Foundation sections. |
| `ApertureTabInstaller` | Adds the tab to pawn races at startup. |
| `Presentation/GuRefinementPresentation` | Reusable refinement-presentation hook. See [Will-enforcement effect](#will-enforcement-effect). |

Engine note: RimWorld 1.7 runs on Waylith through `UnityStubs`. Inspection of the stubs confirmed the following:
- `Texture2D.GetPixels32`, `SetPixels32` and `Apply` are implemented. Loaded textures keep CPU data, and the renderer falls back to decoding the PNG from the mod folder.
- `GUI.DrawTexture` is implemented.
- `Graphics.DrawTexture` with materials and `PackTextures` are stubs.

That is why the orb is composited on the CPU instead of through custom shaders or GUI rotation.

### Data-driven descriptions

New optional fields let a future Gu describe itself without new UI code:

- `GuSpeciesDef.role`: short role on cards ("attack", "cultivation support").
- `GuSpeciesDef.effectNotes`: passive or contextual effects for Gu without listed abilities.
- `GuAbilityDef.category` (`Combat`, `Cultivation`, `Support`, `Defense`, `Movement`, `Information`, `Utility`, `Refinement`, `Transformation`). `Cultivation` channels also get a control on the Cultivation page.
- `GuAbilityDef.effectNotes` and `GuAbilityDef.limitations`: bullets in the Techniques reference.

The numbers in Techniques are read live from the `GuAbilityDef` and its projectile: range, cost, aim time, recovery, targets, line of sight, damage and armor penetration. For channels it shows activation cost, pulses, interval, total duration, amount per pulse and recovery.

## Rank terminology

`Defs/GuApertureRankDefs/GU_ApertureRanks.xml` records all nine ranks. Only Rank 1 has `implemented=true`. The others are naming conventions and are never shown as reachable.

| Rank | Essence | Kind |
| --- | --- | --- |
| 1 | Green Copper Primeval Essence | Primeval (implemented) |
| 2 | Red Steel Primeval Essence | Primeval |
| 3 | White Silver Primeval Essence | Primeval |
| 4 | Yellow Gold Primeval Essence | Primeval |
| 5 | Purple Crystal Primeval Essence | Primeval |
| 6 | Green Grape Immortal Essence | Immortal, own aperture identity |
| 7 | Red Date Immortal Essence | Immortal, own aperture identity |
| 8 | White Litchi Immortal Essence | Immortal, own aperture identity |
| 9 | Yellow Apricot Immortal Essence | Immortal, own aperture identity |

### Rank 1 stage palette (provisional)

| Stage | Colour (linear RGB) | Character |
| --- | --- | --- |
| Initial | (0.30, 0.72, 0.52) | Jade green |
| Middle | (0.68, 0.92, 0.70) | Pale green |
| Upper | (0.13, 0.55, 0.29) | Deep green |
| Peak | (0.07, 0.30, 0.18) | Black-green |

Every renderer, bar and swatch reads these from the Def. Edit the `<essenceColor>` values to refine the canonical palette; no code change is needed.

## Art pipeline

Each rank has one set of **grayscale** layers. They are recoloured at runtime from the rank's stage colours, so the four minor stages share the same images. A rank may later receive unique structure art, and a single stage may override its base image with `stages/li/baseTexPathOverride`.

| File (`Textures/ProjectGU/UI/Aperture/…`) | Placeholder | Channels used | Role |
| --- | --- | --- | --- |
| `Rank1_Base.png` | 256×256 | Luminance and alpha | The aperture boundary (shell, hollow interior, faint outer aura). Tinted by the stage colour at `structureTint` strength (0.4). |
| `Rank1_SeaMask.png` | 256×256 | Alpha bounds the liquid; luminance is depth and texture detail | The primeval sea. Alpha also defines the area used to place the surface and layers, so any sea shape works. |
| `Rank1_WallMask.png` | 256×256 | Alpha bounds the ring; luminance is plate detail | The aperture walls. The lit arc is measured clockwise from the top around the image centre. |
| `Rank1_Highlights.png` | 256×256 | Alpha (white) | Specular light, added on top. Optional. |
| `Cracks.png` | 256×256 | Alpha (white lines) | Independent crack overlay for any rank, darkened at runtime and scaled by severity. |
| `RefinementCurrents.png` | 256×256 | Alpha (white spirals) | Currents rotated inside the sea during refinement activity. |
| `GuFallback.png` | 64×64 | Colour and alpha | Neutral portrait for a species with no sprite. |

Requirements and tips:

- **Format:** 32-bit RGBA PNG with straight (non-premultiplied) alpha.
- **Size:** any square size works; layers are resampled bilinearly to 256×256 once, when first shown. 512×512 is a good source size for hand-drawn work.
- **Registration:** keep the sphere centred and the same size in every layer of a rank so they line up.
- **Colour:** paint the orb layers in grayscale. Colour painted into them is converted to luminance, since the stage colours do the tinting.
- **Missing layers:** a missing required layer is replaced by a generated shape, with one warning in the log. Missing optional layers are skipped.
- **Regenerating placeholders:** `scripts/generate-aperture-placeholder-art.ps1` regenerates every placeholder. They are original, procedurally generated images.

To replace the art, overwrite the PNG at the same path and restart the game. To add Rank 2 later, set its texture paths and `implemented`, and supply four stages.

## Aperture conditions

`GuApertureVitality` is `Healthy`, `Cracked` (with a 0–1 severity) or `Dead`.

- **Authority:** only a gameplay system that registers an `IGuApertureConditionSource` can make an aperture anything but healthy. Alpha 4A registers none, because no damaged or dead aperture state exists yet. Cracks are therefore never shown in play.
- **Cracked:** draws `Cracks.png` over any rank. Its opacity grows with severity (35% at 0, 100% at 1).
- **Dead:**
  - **Appearance:** desaturates the orb, stills the sea, dims it, and removes the shimmer, activity and lit wall arc.
  - **Behaviour:** a dead aperture shows no cultivation controls.
  - **Scope:** this is a presentation capability only. No immortal zombie mechanics exist.
- **Immortal apertures:** each immortal rank Def carries `immortalAperture=true`, so it can receive its own structure art rather than a recoloured mortal aperture.
- **Developer preview:** in Development Mode, the Cultivation page offers **Actual / Cracked / Dead** buttons to preview the overlays for art review. The preview is presentation only, never saved, and reset when the tab opens.

## Will-enforcement effect

The three refinement mechanics stay separate (`GuRefinementKind`):
1. **Will-enforcement:** subduing one Gu and establishing recognition. This is new in Alpha 4A.
2. **Essence refinement:** Liquor Worm, presented by its existing waist orbit, unchanged.
3. **Recipe refinement:** reserved; not implemented.

While a `GU_RefineMortalGu` job is genuinely driving a refinement process, `GuRefinementPresentation` attaches two sustained layers to the cultivator from `GU_Presentation_WillEnforcement`:

| Layer | File (`Textures/ProjectGU/Effects/WillEnforcement/…`) | Placeholder | In game |
| --- | --- | --- | --- |
| `GU_Fx_WillSuppressionField` | `SuppressionField.png` | 256×256, 1 frame | Ground ring of branching essence currents beneath the cultivator, 2.3 cells across. It turns 0.6°/tick, breathes ±4% over 150 ticks and is drawn below pawns (`MoteLow`) with additive glow. |
| `GU_Fx_WillSuppressionCurrents` | `Currents.png` | 512×128: 4 frames of 128×128 in one row | Swirling currents around the hands and the held Gu, 1.5 cells across. It turns −2.2°/tick, animates at 8 ticks per frame, breathes ±6% over 90 ticks and is drawn just above pawns (`MoteOverheadLow`). |

Effect profiles gained `spinDegreesPerTick`, `pulseScale` and `pulsePeriodTicks`. All follow game ticks and stop when the game is paused.

Lifetime rules:
- **Starting:** the job attaches the layers from its live process, retrying every 30 ticks.
- **Keep-alive:** a layer holds only while this exact driver is the pawn's current driver, the pawn is spawned and conscious, and the Gu's process is the same process.
- **Stopping:** the job's finish action removes the layers on every map.
- **Interruption:** drafting, another order, incapacitation, completion, death, despawning or a map change each removes them in the same tick.
- **Saving:** nothing is saved. A reloaded job attaches the layers again from its saved process.
- **Effects off:** the Project GU visual-effects setting removes them while refinement continues identically.

## Verification

Environment: Windows 11, .NET SDK 10.0.401, RimWorld 1.7.5014 rev1195 (Waylith), Core and Harmony.

```powershell
.\scripts\verify.ps1 -Focused
.\scripts\verify-priority0-live.ps1 -Profile Alpha4A -Parallel 1 -StageTimeoutSeconds 900
.\scripts\verify-priority0-live.ps1 -Profile Alpha4AVisual -Parallel 1 -StageTimeoutSeconds 900   # opens a game window
```

| Kind | Result |
| --- | --- |
| Structural | `verify.ps1` parses every Def, including the new rank, presentation and language files. |
| Real compilation | Release build against the installed 1.7 assemblies: 0 errors. The engine APIs used (texture CPU access, `GUI.DrawTexture`, `TabDrawer`, `Widgets`, `ProjectileProperties.GetDamageAmount`) were checked against the decompiled 1.7 and Waylith `UnityStubs` assemblies. |
| Managed (real assemblies, no game process) | `ApertureInterfaceChecks`, 60 assertions (details below the table). The full `verify.ps1 -Focused` suite passes. |
| Native headless `Alpha4A` | Campaigns `a33f66d826d54c62958905511de4f8f7` and `b7b56769d00346669e22fdb8ad2d2f3e` passed `new`, `aperture` and `reload` with zero red errors. |
| Native regression | Campaign `843e666970ef4c0881e8f6f7e6863d22` (`-Parallel 3`) passed all nine units: `Alpha4A`, `Alpha3`, `Alpha2`, `Alpha1`, `RankOne`, `GuGeneration` (generation integrity), `Slice3B3B` (identity and two Gu in one aperture), `Slice3B5A` (multi-cohort) and `ClanStart`. `Alpha3` includes the spirit-spring encounter follow-up. |
| Native rendered `Alpha4AVisual` | Campaign `8666abce4f614442be370c5befa83384` passed all four stages, and the screenshots were inspected (below). Earlier campaign `ea1884f5a37a48caa78773f8b661faab` also passed; its screenshots exposed the presentation defects fixed afterwards. |

`ApertureInterfaceChecks` covers:
- Area-based sea level, including half capacity reaching the equator.
- Mixed-quality layer order and thickness, trace visibility and the empty and full seas.
- An Initial-stage aperture with Middle essence keeping its stage while drawing two layers.
- The nine canonical essence names, with only Rank 1 implemented and immortal ranks flagged.
- Distinct stage colours and art file presence.
- Condition authority: healthy without a source, a cracked report, and fallback on failure.
- Visual-state rules, effect-profile validation, and the refinement presentation Def.
- Static guards that no interface type saves state and that the tab never calls a commit method directly.

What the `Alpha4A` stages verify, all with Development Mode off and every control run through the tab's own `GuApertureAction` objects:

1. **Tab visibility:**
   - The tab is installed on pawn races.
   - It appears for the disciple and the elder.
   - It is hidden for a mortal without an aperture.
2. **Accurate values:**
   - On the Alpha 3 save (an Initial-stage disciple holding 12 Initial and 5 Middle essence), the view reports Rank 1 Initial, Green Copper Primeval Essence and the exact breakdown.
   - The orb is tinted by the actual stage and draws both layers, with no invented condition.
   - The live Liquor Worm session shows as activity, and a running session cannot be started twice.
3. **Liquor Worm through the Cultivation page:**
   - Drafting suspends the session and refuses cultivation actions with their reason.
   - The page then offers **Resume Liquor Worm refinement (n/10)**. Resuming re-attaches the waist orbit, charges nothing, commits nothing and completes the session.
4. **Gu custody:**
   - Both stored Gu are listed as individuals with their own wild sprites.
   - Withdrawal moves the same individual to inventory with exactly one custody revision and no new record.
   - Re-running the stale withdrawal is refused and changes nothing.
   - The elder can neither withdraw nor store the disciple's Gu.
   - Storage returns the same individual with exactly the expected occupants and no physical projection.
5. **Moonblade:** cast from the native command bar command, launched and resolved.
6. **Cultivation:**
   - Advancing is refused until the walls are full.
   - **Nourish aperture walls** runs the existing job until the walls are full.
   - Nourishment is then refused and **Advance to Middle** advances the stage.
7. **Will-enforcement:**
   - A third Gu, freshly searched, captured and picked up, offers only **Enforce will** while unrefined.
   - Starting it shows exactly two suppression layers and will-enforcement activity on the orb.
   - Drafting removes the effect at once, and the process keeps its progress and advances no further.
   - Resuming re-attaches the effect, and ordering another job removes it at once.
   - With effects off nothing is drawn while refinement continues identically; re-enabling restores exactly two layers.
8. **Nonhuman host:**
   - A wild Muffalo given an aperture shows the tab as observed only: no essence, Gu or controls.
   - Once tamed it shows its full aperture but still offers no controls.
   - No management command appears for it.
9. **Save/load:**
   - Saved part way through will-enforcement.
   - The fresh-process reload restores exactly three Gu (two stored, one carried mid-refinement) with its exact progress and no duplicate projection, and the will-enforcement job.
   - It replays no effect, then re-attaches it from the saved process.
   - Completion removes the effect. The newly refined Gu is refused by the full aperture ("the aperture is full (2/2)"), and the refusal changes nothing.

### Rendered inspection

The rendered stage is a Vulkan windowed run at 2560×1600 with UI scale 1. It opens the real tab through the inspection pane (`InspectPaneUtility.OpenTab`) and photographs 29 frames:

- **Will-enforcement in the world:** the ground ring and currents turn around the cultivator in the spring cavern. They are modest, readable and don't hide the pawn. Default zoom is also shown.
- **Every page while refining:**
  - **Cultivation:** a mixed sea with a pale Middle layer under a jade Initial layer, the exact breakdown, the composition bar and wall progress.
  - **Gu:** the Liquor Worm and Moonlight Gu cards with their own sprites, and the detail panel with recognition, custody, condition, grants and identity.
  - **Techniques:** current numbers.
  - **Foundation.**
- **Refined but no room:** the third Gu, refined, with **Store in aperture** dimmed and "Unavailable: The aperture is full (2/2)".
- **Liquor Worm running:** next-quality currents swirl through the sea, and a deep-green Upper layer settles beneath the Middle sea. The orbit and the tab are visible together, and the suspended state is shown after drafting.
- **Wall nourishment running.**
- **Developer previews:** the crack overlay, and the desaturated, still, dead aperture.
- **UI scales 1.5 and 2:** the tab shrinks to the available height, and pages scroll without clipping their controls.
- **Nonhuman host:** the observed orb (structure only), "The primeval sea is concealed", and the concealed Gu page.

The first rendered campaign found three presentation defects, all fixed and confirmed in campaign `8666abce…`:
- Unavailable buttons were drawn like available ones. They are now dimmed, with the reason shown inline.
- The mixed-sea note assumed the separate essence was always higher quality. It is now accurate for leftover lower-quality essence too.
- Gu actions showed their unavailability reason only on hover. It is now shown inline.

A harness defect also surfaced and was fixed. The reload assertion compared the refinement's float essence total exactly against the test's own JSON copy, and the decimal text formats of the save and the JSON differ in the last digit. It now uses the 0.0001 tolerance the Alpha 1 reload test already uses. Every other field still matches exactly.

A real player session through the ordinary main menu was not performed. The rendered harness drives the real inspection pane and tab, but the owner's own playthrough remains the acceptance check for feel and readability.

## Open items for the owner

- **Placeholder art:** the orb and effect art are original placeholders, specified above for replacement with handmade Krita assets.
- **Palette:** the Rank 1 colours are provisional pending the canonical palette.
- **Fallback menu:** **Manage aperture Gu** stays as a fallback until the owner accepts the tab as a full replacement. Removing it needs no save migration, since it saves nothing.
- **Observed view:** what a pawn outside the clan may perceive (currently rank and stage only) is a provisional design choice.
- **Layer order:** higher-quality essence settling lowest is a presentation choice. It can be reversed in `PrimevalSeaLayout.Layers`.
