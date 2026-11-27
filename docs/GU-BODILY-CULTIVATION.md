# S0/S1 · Channel contract and permanent bodily cultivation

Branch `feature/gu-bodily-cultivation`, based on `b9612e5`. This assignment implements slices S0 and S1 of the [architecture review](reviews/CLAUDE-ARCHITECTURE-REVIEW.md#6-smallest-playable-sequence). The [feasibility assessment](reviews/KINDRED-PROJECT-GU-FEASIBILITY.md) is preserved beside it. Both are owner-accepted.

## S0 · Channel contract

The existing ability architecture is extended. No new execution layer is added.

- `GuChannelProperties` is no longer sealed. An ability's XML can select a subclass with `<channel Class="…">`, and the subclass's `ConfigErrors` adds its own validation.
- `GuChannelWorker.CanChannel`, `BindingFor` and `ApplyPulse` now receive the live `GuAbilityGrant` (caster, exact aperture, Gu record and ability). `GuChannelService` already builds this grant when it authorizes a channel. `StillAuthorized` now returns it for every tick.
- Workers now supply their own interface text: per-pulse effect, session progress, what a stop keeps, the completion message, and whether a session counts as essence refinement in the orb display. The command, the Techniques reference and the Cultivation page previously hard-coded Liquor Worm wording. They now read these properties.
- There are two new pulse results. `Finished` means the pulse applied and the session completes now, which bodily cultivation uses at full development. `Suspended` means nothing applied, nothing was paid, and the session stays resumable.
- A channel ability may have `essenceCost` 0, for channels that charge only per pulse. Non-channel abilities still require a positive cost.
- `GuChannelSession` has a new saved field, `pulseEssencePaid`. `Converted` is documented as the worker-defined amount applied in the session.

Liquor Worm and Moonblade behave as before. Their Defs are unchanged. The Liquor Worm worker was ported line for line.

## S1 · Black Boar bodily cultivation

| Item | Value (provisional) |
| --- | --- |
| Ability | `GU_BlackBoarCultivation`, category Cultivation |
| Activation cost | None |
| Cooldown | 1 in-game day (60,000 ticks), starting when a new session commits |
| Session | 10 pulses, one every 500 ticks (about 2 in-game hours in all) |
| Per pulse | 1 primeval essence for 1/150 of full development |
| Full development | 150 pulses, or 15 daily sessions |
| Attainment | `GU_BodilyStrength_BlackBoar`, severity 0–1 |
| Fully developed bonuses | Melee damage ×1.4, carrying capacity ×1.5, mining speed ×1.2, construction speed ×1.15 |

**Payment and progress are atomic.** Each pulse runs one `Hediff_Aperture.TrySpendEssenceForCast` commitment, and the progress step happens inside it. If the body doesn't take the step, or the step throws, the aperture restores the exact essence mixture. The pulse count, the payment record and the severity change in the same tick, and saves happen only between ticks. So no save can show unpaid progress, a double debit or a double advance.

**Progress belongs to the body.** The attainment is a native whole-body hediff, `Hediff_GuBodilyAttainment`. The Gu, the session and the registry keep no copy of it. Withdrawing, transferring or losing the Gu, or ending a session early, leaves every completed step and its stats in place. Each strength has exactly one HediffDef. A fully developed attainment refuses every further session, including from a second Gu of the same species. Different strengths are different HediffDefs and coexist.

**Bonuses grow continuously.** Every stage carries the full modifiers with the native `multiplyStatChangesBySeverity`, so a pawn at 40% development has 40% of each bonus. The native stat explanation lists the Black Boar strength. The five stage labels name the condition in the Health tab: stirring, developing, hardening, formidable and fully developed. The tab also shows a percentage, and the condition stays visible after full development.

**Medicine leaves it alone.** The HediffDef is not bad, untendable, not curable by items and not chronic. It sets `keepOnBodyPartRestoration`. `ConfigErrors` refuses an attainment Def that breaks any of these rules or omits severity scaling. Native paths checked against the RimWorld 1.7.5021 assembly:

| Native path | Why the attainment is kept |
| --- | --- |
| `HealthUtility.FixWorstHealthCondition`, used by healer serum and similar cures | Every candidate search requires `everCurableByItem`. The native fixture confirms this. |
| Biosculpter healing cycle | Skips `!everCurableByItem` and non-chronic hediffs. Confirmed from source only, because Ideology wasn't loaded. |
| Doctors' tending | The hediff isn't tendable. `HasHediffsNeedingTend` stays false. |
| `Pawn_HealthTracker.Notify_Resurrected` | Removes only curable lethal, immunizable or injury hediffs and `forceRemoveOnResurrection`. Its fallback removal for a pawn that still should be dead keeps `keepOnBodyPartRestoration`. Confirmed by the native fixture. |
| Death | Hediffs stay on the corpse's pawn. Confirmed by the native fixture. |
| Body-part loss or restoration | The hediff has no part, so part removal and restoration don't touch it. |

**Interface.** The Cultivation page lists Black Boar cultivation beside Liquor Worm refinement, with the current development. A new **Bodily attributes** block reads every attainment straight from the pawn's hediffs, whether or not the Gu is still held. It shows each attainment's development bar and its current bonuses.

**Data-driven reuse.** White Boar is planned as one HediffDef (parent `GU_BodilyStrengthBase`) plus one `GuAbilityDef` that names it in `channel.attainment`, with its species listing the ability. It needs no code.

## Development fixture and integration

The real `GU_BlackBoarGu` species exists only on `feature/alpha6-wild-gu-ecology` (Codex, `67c1a30`). Until integration, cultivation is tested with an isolated stand-in species, `GU_DevBlackBoarStandIn`, in `tests/Fixtures/BodilyCultivationDev/`. The stand-in is a separate development mod. The native runner copies its Def into the disposable test mod, and production `Defs/` never references it. It reuses the existing developer projections and is exempt from feeding.

Integration takes one change on the ecology side: add `<abilities><li>GU_BlackBoarCultivation</li></abilities>` to `GU_BlackBoarGu`. Then retire the fixture folder and its runner line. Both branches edit two shared test files, so expect two trivial text conflicts: the `-Profile` list in `scripts/verify-priority0-live.ps1` (keep both `Bodily` and `Slice1A`) and the stage dispatch in `tests/ProjectGu.LiveVerification/LiveLifecycleChecks.cs` (keep both). Slice 1A also changed `LiveAlpha3.cs`, which the `Bodily` profile builds on, so rerun `Bodily` after the merge. This branch doesn't edit `docs/ROADMAP.md`. In code, the ecology branch doesn't touch `Combat/`, the Cultivation page or the aperture. The ecology branch's docs still cite RimWorld 1.7.5014 rev1195 in several places; Slice 1A and this branch were verified on 1.7.5021 rev1365.

## Remaining limitations

- **Values are provisional.** The bonuses, cost, cadence and cooldown await balance review.
- **Cultivation page visibility.** The Bodily attributes block is on the Aperture tab, which appears only for aperture hosts. A pawn whose aperture is later lost still has the Health tab condition and the stats, but no Cultivation page.
- **Untested native paths.** Caravans, world pawns, Anomaly duplication and mechanitor or mutant transformations rely on native hediff persistence and weren't exercised. Duplication copies hediffs by default (`duplicationAllowed`), so a duplicate would carry the attainment. That is an owner policy question for later.
- **Explicit removal still works.** Developer tools, other mods' removal code and explicit `HealthUtility.Cure` calls can still remove the hediff. Project GU never reduces its severity.
- **Out of scope for this assignment.** The species' native wildlife eligibility (hunting, taming) belongs to the ecology branch. Combat Extended is untouched and has no dependency.
- **No manifestation.** Bodily cultivation has no visual manifestation, so "effects off" has nothing to compare.

## Verification

Environment: Windows 11, .NET SDK 10, RimWorld 1.7.5021 rev1365 (Waylith), Core and Harmony (workshop 2009463077).

Automated evidence types are listed separately. No rendered or player test has been run yet.

- **Real compilation.** `scripts/build.ps1` and the live-verification assembly built against the actual 1.7.5021 assemblies with zero warnings and zero errors.
- **Structural and managed checks.** `scripts/verify.ps1` passed in both focused and full modes, 166 managed checks in the full run (`tests/obj/bodily-verify-full.log`).
  - The Liquor Worm managed checks were ported to the grant context and pass unchanged.
  - The new `BodilyCultivationManagedChecks` cover:
    - Class-selected properties and the worker they select by default.
    - The zero-cost rule: allowed for channels only.
    - Refusal of every unsafe attainment Def: curable, bad, tendable, removed on resurrection, lost on part restoration, unscaled, uncapped or a plain hediff class.
    - Exact essence restoration when progress is refused or throws.
    - Scribe persistence of a suspended session's pulses, partial pulse, per-pulse payment and cooldown.
- **Native game tests.** Final campaign `13920b7ee58e4719b203cf460ac38f52` (disposable game copy, Steam `Mods` excluded, read-only enforced, no isolation problems, user data folder unchanged) passed `Bodily`, `Alpha3`, `Alpha2` and `Alpha4A`.
  - `Bodily` stage **bodily**, on the Alpha 3 save:
    - Acquiring the stand-in takes the ordinary route: capture, pick up, will-enforcement, and withdrawing Moonlight to make room in the two-Gu aperture.
    - The first commit makes one session and starts the cooldown with no upfront debit.
    - Each pulse pays exactly 1 essence for exactly 1/150 development, with natural recovery accounted tick by tick.
    - The scaled stat factors are checked for all four stats, and the native melee explanation names the strength.
    - Drafting suspends the session with nothing gained or charged, and resuming is free despite the cooldown.
    - Withdrawing the Gu keeps the development and the melee stat.
    - With essence at zero at the pulse tick, the session pauses with no payment and no progress, and resumes once essence is back.
    - The game saves 200 ticks into the seventh pulse.
  - `Bodily` stage **reload**, in a fresh process:
    - The same job, session, partial pulse, payment record and severity are restored.
    - The session completes at exactly 10 essence for 10/150.
    - The daily cooldown blocks a new session until it elapses.
    - The last pulse reaches exactly 1.0, pays once and ends the session at once. The Health tab shows "fully developed".
    - A second Black Boar Gu, stored after withdrawing the first, is refused and charges nothing.
    - The suspended Liquor Worm session resumes and refines exactly its remaining 5 units with no new commitment.
    - A mortal at 50% development keeps it through healer-serum curing of every condition, through death and through `ResurrectionUtility.TryResurrect`.
  - `Bodily` stage **final**, in another fresh process: full development, the refusals, the commitment count and the resurrected mortal's development all persist. The Cultivation page reads the attainment from the body. No red errors appeared in any stage.
  - `Alpha3`, `Alpha2` and `Alpha4A` pass unchanged as the Liquor Worm, Moonblade and Aperture-tab regressions.
- **Fixture corrections during development.** All of these were harness issues, not gameplay defects:
  - The fixture didn't make room in the two-Gu aperture.
  - The fixture's pulse arithmetic was wrong.
  - The fixture's day-long wait crossed the native autosave, which blocks custody inside a single frame. The fix is the existing `DisableFixtureAutosave` lesson from Alpha 5A.
  - Severity comparisons failed because `System.Text.Json` doesn't round-trip floats exactly. The game save (G9) does.
- **Not run.** Rendered inspection needs a game window and no other RimWorld instance, and the owner's RimWorld was running throughout. No competing performance test was run.

## Owner playtest

Codex's payload is installed in Steam's `Mods\ProjectGU-LivingCultivation` for owner acceptance. Installing this branch replaces it, so do this only when that acceptance session is finished or paused. To go back, run `scripts\install.ps1` from the ecology worktree.

1. Close RimWorld. In PowerShell, from `C:\Users\monve\Downloads\Project-Gu-bodily-cultivation`, run `.\scripts\install.ps1`.
2. Copy `tests\Fixtures\BodilyCultivationDev` to `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\ProjectGU-BodilyCultivationDev`.
3. Enable Harmony, Core, Project GU and the bodily cultivation development fixture, in that order. Turn on Development Mode.
4. Start the Southern Border Clan scenario and let the disciple awaken an aperture, or load a recent save with a Gu Master.
5. Open the debug actions menu. Under Project GU, choose **Spawn registered wild Gu… → Black Boar stand-in (developer only)**, and click an open cell near the Gu Master.
6. Right-click the stand-in with the Gu Master and choose **Capture … (unrefined)**. Then pick up the dormant Gu.
7. Open the **Aperture** tab and run **Enforce will over** it. When it's refined, **Store** it. If the aperture already holds two Gu, withdraw one first.
8. On the **Cultivation** page, press **Black Boar cultivation**. Each pulse (500 ticks) spends 1 essence. Check these:
   - The Health tab shows **Black Boar strength (stirring, N%)**.
   - The **Bodily attributes** block shows the bar and the current bonuses.
   - Hovering the melee damage, carrying capacity, mining speed or construction speed stat lists the strength.
9. Draft the pawn mid-session, then undraft and resume. Development is kept and resuming costs no wait. Withdraw the Gu: the condition and bonuses remain.
10. Full development takes 15 daily sessions. At full development the Health tab reads **fully developed**, and the technique refuses further sessions, including from a second stand-in.

Please judge the provisional numbers, the session length and daily cooldown, the stage labels and the Bodily attributes presentation. Remove the fixture mod after testing.
