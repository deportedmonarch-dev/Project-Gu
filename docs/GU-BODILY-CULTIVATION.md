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

## Independent review and integration

`integration/alpha6-black-boar` combines the accepted ecology baseline `93a562b` and bodily cultivation implementation `40b6a99`. `GU_BlackBoarGu` now grants `GU_BlackBoarCultivation`. The historical S0/S1 campaign below used an isolated development stand-in. The integrated Bodily profile acquires the real species through ordinary surface discovery and capture, then refines and stores the same individual. The development fixture and runner dependency were retired. Bodily keeps its four stages and Slice1A keeps its original stages.

The review found no architectural blockers. The registered Gu authorizes a channel, the aperture owns payment, and the pawn's native whole-body hediff owns permanent development. Review corrections add rollback on failure after native health mutation, require the active caster and species ability to match the session, and derive each configured pulse's severity from its exact fraction. The latter prevents slow techniques from reaching the cap early due to the former completion tolerance. The Health tab condition is explicitly visible throughout development. Bodily health work is local to the caster and runs per pulse or when the interface reads it. Active authorization now makes one caster validation per tick. The existing channel ledger lookup and maintenance behavior were retained. No separate performance benchmark was run.

### Integrated verification

`scripts/verify.ps1` passed structural checks, game-linked compilation and the full managed suite. The native tests used RimWorld 1.7.5021 rev1365, .NET 10, Core and Harmony. Both production and native test assemblies compiled with zero warnings and errors.

- **Bodily** passed `new`, `bodily`, `reload` and `final` in campaign `3663293b6ba444ac80dbec054aea0133`. The native fixture discovered a registered wild Black Boar through surface proximity, preserved its cohort debit and identity through capture, and verified that it granted cultivation only after the right pawn refined and stored it. It exercised drafting, withdrawal, essence shortage, resume at the saved progress, cooldown, same-strength caps and independent development for a second attainment Def. Injected failures after native hediff addition and update rolled back the body and the exact mixed-quality essence stock.
- The first fresh process recovered the same Gu and receipt, aperture occupants and custody revision, active job, partial pulse, essence stock, cooldown and severity. It completed the first ten pulses and exercised the last pulse after advancing the fixture to one step short of full development. The second fresh process confirmed both Black Boar identities, retained development, full stats, the duplicate cap and persistence after native cure, death and resurrection.
- **Slice1A**, **Alpha2** (Moonblade), **Alpha3** (Liquor Worm), **Alpha4A** (Aperture interface), **Slice3B5B** (cohort and aperture custody), **Slice0Wildlife** and **GuGeneration** passed in campaign `75744f3b133940e299a5523e54e461e6`. That campaign's Bodily run exposed a mixed-quality fixture setup error, corrected before the passing rerun.
- **Alpha5B** passed `new`, `integration` and `reload` in campaign `275738620b064a9cb48c1cf09737caa1`. Its spring ambience fixture now counts non-surface records and both kinds of cavern projection so a surface discovery cannot trip the spring assertion. The optional historical Alpha 5A stage was skipped. All three campaigns staged the same 101 production files by SHA-256.
### Owner acceptance of integrated Black Boar

The owner accepted integrated Black Boar Gu at baseline `cf79ee1` within the scope of the completed gameplay session. No blocking defects were reported. The owner found the cultivation activity appropriate and advancement well communicated. The Bodily attributes interface is suitable for future additions provided it lists each attainment independently with its effects directly underneath. Refinement presentation improvements are deferred.

The gameplay session is separate from the automated campaigns above. Manual completion of all 15 sessions and manual execution of the fresh-process and medical cases were not confirmed. Balance beyond the observed session remains open.

## Remaining limitations

- **Values are provisional.** The bonuses, cost, cadence and cooldown await balance review.
- **Cultivation page visibility.** The Bodily attributes block is on the Aperture tab, which appears only for aperture hosts. A pawn whose aperture is later lost still has the Health tab condition and the stats, but no Cultivation page.
- **Untested native paths.** Caravans, world pawns, Anomaly duplication and mechanitor or mutant transformations rely on native hediff persistence and weren't exercised. Duplication copies hediffs by default (`duplicationAllowed`), so a duplicate would carry the attainment. That is an owner policy question for later.
- **Explicit removal still works.** Developer tools, other mods' removal code and explicit `HealthUtility.Cure` calls can still remove the hediff. Project GU never reduces its severity.
- **Out of scope for this assignment.** The species' native wildlife eligibility (hunting, taming) belongs to the ecology branch. Combat Extended is untouched and has no dependency.
- **No manifestation.** Bodily cultivation has no visual manifestation, so "effects off" has nothing to compare.

## Original S0/S1 verification

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

Enable Harmony, Core and Project GU. Use a Gu Master at an eligible forest settlement, such as one started with the Southern Border Clan scenario. A current [Slice 1A](ALPHA-6-SLICE-1A.md) save with a stored Black Boar Gu can start at step 3.

1. Discover a wild Black Boar through proximity or **Search for wild Gu**. Capture it and pick up the dormant Gu.
2. In **Aperture**, choose **Enforce will over**, then **Store** when refinement ends. Withdraw one Gu first if both slots are occupied.
3. On the **Cultivation** page, start **Black Boar cultivation**. Look for **Black Boar strength** in **Health**, the progress and bonuses in **Bodily attributes**, and the strength in a native stat explanation.
4. Draft mid-session, then undraft and resume. Save partway through another pulse and reopen the save from a fresh launch. Withdraw the Gu and check that the health condition and bonuses stay.
5. With the same Gu, 150 pulses fill the attainment across 15 daily sessions. At full development any Black Boar Gu should refuse further pulses.
