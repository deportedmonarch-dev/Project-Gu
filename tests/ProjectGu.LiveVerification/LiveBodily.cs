using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using RimWorld;
using Verse;

/// <summary>
/// S0/S1 native verification on the Alpha 3 save. Stage "bodily" acquires a registered surface Black Boar, commits a bodily
/// cultivation session with no upfront debit, and checks per-pulse payment and progress through drafting, Gu withdrawal and
/// an essence shortage before saving part way into a pulse. Stage "reload" resumes that exact pulse in a fresh process,
/// completes the session, reaches full development, refuses a second Black Boar Gu, resumes the suspended Liquor Worm session
/// unchanged, and puts a mortal's attainment through native medicine, death and resurrection. Stage "final" rechecks it all
/// after another fresh process.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static GuAbilityDef BoarCultivation => DefDatabase<GuAbilityDef>.GetNamed("GU_BlackBoarCultivation");
    private static HediffDef BoarStrength => DefDatabase<HediffDef>.GetNamed("GU_BodilyStrength_BlackBoar");
    private static string BodilyStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "bodily-state.json");
    private const float PulseEssence = 1f;
    private const float Step = 1f / 150f;

    private sealed record BodilyState(string BoarId, string SecondBoarId, string LiquorId, string CultivatorId, string ApertureId,
        string SessionId, int PulsesDone, int TicksIntoPulse, float Severity, double PulseEssencePaid, int Committed,
        string MortalId, float MortalSeverity, int LiquorPulses, BoarOrigin FirstOrigin, BoarOrigin SecondOrigin,
        long StoredRevision, double[] EssenceStock, int CooldownUntil);

    private sealed record BoarOrigin(string Source, string Trigger, long AfterDebit)
    {
        public static BoarOrigin Of(GuRecord boar) =>
            new(boar.CreationEvidence.SourceId, boar.CreationEvidence.Trigger, boar.CreationEvidence.RevisionAfter);
    }

    private static double[] BodilyEssenceStock(Hediff_Aperture aperture) =>
        Enum.GetValues<RankOneStage>().Select(aperture.EssenceOfQuality).ToArray();

    // Stage "bodily".
    private static void ExerciseBodily()
    {
        var (_, disciple, aperture, liquor) = LoadAlpha3State();
        DisableFixtureAutosave();
        var registry = IndividualRegistry;
        var liquorSession = Ledger.ChannelFor(liquor.Id);
        Check(Hediff_GuBodilyAttainment.Of(disciple, BoarStrength) == null && !Hediff_GuBodilyAttainment.AllOf(disciple).Any()
            && liquorSession is { Complete: false } && BoarCultivation.channel is GuChannelProperties_BodilyCultivation,
            "The disciple starts with no bodily attainment and a Liquor Worm session in progress");

        var moon = registry.ApertureOccupants(aperture).Single(record => record != liquor);
        TravelTo(disciple, Home);
        Current.Game.CurrentMap = Home;
        var boar = AcquireSurfaceGu(disciple, aperture, moon);
        Check(liquorSession is { Active: false } && Ledger.ChannelFor(liquor.Id) == liquorSession,
            "Travelling to the surface suspended the Liquor Worm session without ending it");
        Check(GuAbilityService.GrantsFor(disciple).Any(grant => grant.Gu == boar && grant.Ability == BoarCultivation)
            && AbilityGizmos(disciple).Any(command => command.Ability == BoarCultivation && !command.Disabled)
            && !GuChannelService.CanChannel(Clan(GuClanRole.Elder).Single(), boar.Id, BoarCultivation, out _, out _, out _)
            && !GuChannelService.CanChannel(disciple, liquor.Id, BoarCultivation, out _, out _, out _)
            && !GuChannelService.CanChannel(disciple, boar.Id, LiquorRefinement, out _, out _, out _),
            "The stored Black Boar grants its own technique to its own cultivator");
        VerifyBodilyRollbackAndCoexistence(disciple, aperture, boar);
        var view = new ApertureView();
        view.Refresh(disciple);
        var action = view.ChannelActions.SingleOrDefault(candidate => candidate.GuId == boar.Id);
        Check(action is { Available: true } && action.Ability == BoarCultivation && view.ChannelActions.Any(candidate => candidate.Ability == LiquorRefinement),
            "The Cultivation page offers Black Boar cultivation beside Liquor Worm refinement");
        Check(Pawn_ApertureNumbers(BoarCultivation).Any(row => row.Item1 == "Activation cost" && row.Item2.Contains("each successful pulse")),
            "The Techniques reference states that essence is paid per pulse with nothing to begin");

        // 1. Commit: a session and the cooldown, but no upfront essence.
        SetEssence(aperture, Math.Min(aperture.MaxEssence * 0.55f, 14f));
        float expected = aperture.Essence;
        int committed = Ledger.CommittedTotal;
        Check(GuChannelService.TryOrderChannel(disciple, boar.Id, BoarCultivation, out string reason), reason ?? "ordered");
        BodilyTick(disciple, aperture, null, ref expected);
        var session = Ledger.ChannelFor(boar.Id);
        Check(session is { Active: true, PulsesDone: 0, EssencePaid: 0f, PulseEssencePaid: 0d } && Ledger.CommittedTotal == committed + 1
            && disciple.CurJobDef?.defName == GuChannelService.ChannelJobDefName && Ledger.CooldownRemaining(boar.Id, Find.TickManager.TicksGame) > 0
            && Math.Abs(aperture.Essence - expected) < 0.001f,
            "Starting Black Boar cultivation committed one session and the cooldown with no upfront essence debit");

        // 2. Each pulse pays one essence and adds exactly one step, atomically.
        RunBodilyPulses(disciple, aperture, session, 3, ref expected);
        var strength = Hediff_GuBodilyAttainment.Of(disciple, BoarStrength);
        Check(strength != null && strength.Part == null && strength.Visible && Math.Abs(strength.Severity - 3 * Step) < 1e-5f
            && session.PulseEssencePaid == 3 && Math.Abs(session.Converted - 3 * Step) < 1e-5,
            $"Three pulses paid three essence for exactly three steps of whole-body development ({strength?.Severity:0.#####})");
        AssertBoarStats(disciple, strength);
        Check(strength.LabelCap.Contains("Black Boar strength") && strength.LabelInBrackets.Contains("stirring") && strength.LabelInBrackets.Contains("%")
            && !strength.TendableNow() && !strength.def.isBad,
            $"The Health tab shows the developing condition, untendable and not bad ({strength.LabelCap} ({strength.LabelInBrackets}))");
        string explanation = DefDatabase<StatDef>.GetNamed("MeleeDamageFactor").Worker.GetExplanationUnfinalized(StatRequest.For(disciple), ToStringNumberSense.Factor);
        Check(explanation.Contains(strength.LabelBaseCap) || explanation.Contains(strength.def.label, StringComparison.OrdinalIgnoreCase),
            "The native melee damage explanation names the Black Boar strength");

        // 3. Drafting suspends: progress kept, nothing more charged; resuming is free despite the cooldown.
        BodilyStop(disciple, aperture, session, strength, ref expected, "drafting", () => disciple.drafter.Drafted = true);
        disciple.drafter.Drafted = false;
        BodilyResume(disciple, aperture, session, committed + 1, ref expected);
        RunBodilyPulses(disciple, aperture, session, 1, ref expected);

        // 4. Withdrawing the Gu keeps all development and stats; the Gu then grants nothing until stored again.
        float beforeWithdraw = strength.Severity;
        float meleeBefore = disciple.GetStatValue(DefDatabase<StatDef>.GetNamed("MeleeDamageFactor"));
        Thing_GuDormantProjection withdrawn = null;
        BodilyStop(disciple, aperture, session, strength, ref expected, "withdrawal", () =>
            Check(registry.TryWithdrawDormant(boar.Id, boar.CustodyRevision, disciple, aperture, out withdrawn, out string why), why ?? "withdrawn"));
        Check(Hediff_GuBodilyAttainment.Of(disciple, BoarStrength) == strength && strength.Severity == beforeWithdraw
            && disciple.GetStatValue(DefDatabase<StatDef>.GetNamed("MeleeDamageFactor")) == meleeBefore
            && !GuChannelService.CanChannel(disciple, boar.Id, BoarCultivation, out _, out _, out _),
            "With the Gu withdrawn the development and its stats remain, and the technique is unavailable");
        Check(registry.TryStoreDormant(withdrawn, withdrawn.GuRevision, disciple, aperture, out string restored), restored ?? "restored");
        expected = aperture.Essence;
        BodilyResume(disciple, aperture, session, committed + 1, ref expected);

        // 5. Too little essence at a pulse: nothing paid, nothing gained, the session pauses and stays resumable.
        TickUntil(() => session.TicksIntoPulse == BoarCultivation.channel.pulseIntervalTicks - 1, 600);
        int pulses = session.PulsesDone;
        float severity = strength.Severity;
        SetEssence(aperture, 0f);
        Find.TickManager.DoSingleTick();
        TickUntil(() => false, 2);
        Check(!session.Active && Ledger.ChannelFor(boar.Id) == session && session.PulsesDone == pulses && strength.Severity == severity
            && session.PulseEssencePaid == pulses && aperture.Essence < PulseEssence && disciple.CurJobDef?.defName != GuChannelService.ChannelJobDefName,
            "An unaffordable pulse paused the session with no payment and no progress");
        Check(!GuChannelService.CanChannel(disciple, boar.Id, BoarCultivation, out _, out _, out string poor) && poor.Contains("primeval essence"),
            $"Resuming waits for essence ({poor})");
        SetEssence(aperture, Math.Min(aperture.MaxEssence * 0.55f, 14f));
        expected = aperture.Essence;
        BodilyResume(disciple, aperture, session, committed + 1, ref expected);
        RunBodilyPulses(disciple, aperture, session, 2, ref expected);

        // 6. Save during the active session, part way into a pulse.
        TickUntil(() => session.TicksIntoPulse >= 200, 520);
        Check(session.Active && session.PulsesDone == 6 && Math.Abs(strength.Severity - 6 * Step) < 1e-5f && session.PulseEssencePaid == 6,
            "Six pulses are complete and a seventh is in progress at the save");
        var state = new BodilyState(boar.Id, null, liquor.Id, Cultivators.FindCultivator(disciple).CultivatorId, aperture.ApertureId, session.SessionId,
            session.PulsesDone, session.TicksIntoPulse, strength.Severity, session.PulseEssencePaid, Ledger.CommittedTotal,
            null, 0f, liquorSession.PulsesDone, BoarOrigin.Of(boar), null, boar.CustodyRevision, BodilyEssenceStock(aperture),
            session.StartedTick + BoarCultivation.cooldownTicks);
        File.WriteAllText(BodilyStatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved during bodily session {session.SessionId}: {session.PulsesDone} pulses + {session.TicksIntoPulse} ticks, severity {strength.Severity:0.######}.");
        AssertNoErrors("bodily");
    }

    // Stage "reload": fresh process during the active session.
    private static void VerifyBodilyReload()
    {
        var (state, disciple, aperture, boar) = LoadBodilyState();
        var registry = IndividualRegistry;
        var session = Ledger.ChannelFor(boar.Id);
        var strength = Hediff_GuBodilyAttainment.Of(disciple, BoarStrength);
        Check(session is { Active: true } && session.SessionId == state.SessionId && session.PulsesDone == state.PulsesDone
            && session.TicksIntoPulse == state.TicksIntoPulse,
            $"Reload restored the session at its exact partial pulse (pulses {session?.PulsesDone}/{state.PulsesDone}, ticks {session?.TicksIntoPulse}/{state.TicksIntoPulse}, active {session?.Active})");
        Check(session.PulseEssencePaid == state.PulseEssencePaid && Ledger.CommittedTotal == state.Committed
            && BodilyEssenceStock(aperture).SequenceEqual(state.EssenceStock) && boar.CustodyRevision == state.StoredRevision
            && Ledger.CooldownRemaining(boar.Id, Find.TickManager.TicksGame) == state.CooldownUntil - Find.TickManager.TicksGame,
            "Reload restored the exact essence mixture, payment, cooldown and custody revision");
        Check((disciple.jobs.curDriver as JobDriver_GuChannelAbility)?.SessionId == state.SessionId,
            $"Reload restored the channel job driving the session (job {disciple.CurJobDef?.defName})");
        Check(strength != null && Math.Abs(strength.Severity - state.Severity) < 1e-6f,
            $"Reload restored the development ({strength?.Severity:R} vs {state.Severity:R})");
        AssertBoarStats(disciple, strength);

        // Complete the session: four more pulses, one essence each, no new commitment.
        float expected = aperture.Essence;
        RunBodilyPulses(disciple, aperture, session, 4, ref expected);
        TickUntil(() => false, 2);
        Check(session.PulsesDone == 10 && Ledger.ChannelFor(boar.Id) == null && session.PulseEssencePaid == 10
            && Math.Abs(strength.Severity - 10 * Step) < 1e-5f && Ledger.CommittedTotal == state.Committed
            && disciple.CurJobDef?.defName != GuChannelService.ChannelJobDefName,
            "The reloaded session completed: ten pulses, ten essence, exactly one fifteenth of full development");
        Check(!GuChannelService.CanChannel(disciple, boar.Id, BoarCultivation, out _, out _, out string cooldown) && cooldown.Contains("recovering"),
            $"A new session waits for the daily cooldown ({cooldown})");

        // Full development. The fixture moves the body to one step short instead of running fourteen more days.
        strength.Severity = 149f / 150f;
        TickUntil(() =>
        {
            if (Find.TickManager.TicksGame % 2500 == 0) SustainColonists();
            return Ledger.CooldownRemaining(boar.Id, Find.TickManager.TicksGame) == 0;
        }, 61_000, "The daily cooldown elapsed");
        SustainColonists();
        SetEssence(aperture, Math.Min(aperture.MaxEssence * 0.55f, 14f));
        expected = aperture.Essence;
        Check(GuChannelService.TryOrderChannel(disciple, boar.Id, BoarCultivation, out string reason), reason ?? "ordered");
        BodilyTick(disciple, aperture, null, ref expected);
        var last = Ledger.ChannelFor(boar.Id);
        Check(last is { Active: true, PulsesDone: 0 } && last != session, "A new daily session began");
        RunBodilyPulses(disciple, aperture, last, 1, ref expected);
        TickUntil(() => false, 2);
        Check(strength.Severity == 1f && strength.FullyDeveloped && last.PulsesDone == 1 && last.PulseEssencePaid == 1 && Ledger.ChannelFor(boar.Id) == null
            && disciple.CurJobDef?.defName != GuChannelService.ChannelJobDefName,
            "The final pulse reached full development, paid once and completed the session at once");
        Check(strength.LabelInBrackets == "fully developed" && strength.Visible && Hediff_GuBodilyAttainment.Of(disciple, BoarStrength) == strength,
            $"The Health tab still shows the condition at full development ({strength.LabelCap} ({strength.LabelInBrackets}))");
        AssertBoarStats(disciple, strength);
        Check(!GuChannelService.CanChannel(disciple, boar.Id, BoarCultivation, out _, out _, out string full) && full.Contains("fully developed"),
            $"A fully developed strength refuses further sessions ({full})");

        // A second Black Boar Gu adds nothing past full.
        var second = AcquireSurfaceGu(disciple, aperture, boar);
        float essence = aperture.Essence;
        bool refused = !GuChannelService.CanChannel(disciple, second.Id, BoarCultivation, out _, out _, out string twice);
        Check(second != boar && refused && twice.Contains("fully developed")
            && !GuChannelService.TryOrderChannel(disciple, second.Id, BoarCultivation, out _) && aperture.Essence == essence && strength.Severity == 1f
            && Hediff_GuBodilyAttainment.AllOf(disciple).Count() == 1,
            $"A second Black Boar Gu cannot add a second or larger attainment and charges nothing ({twice})");

        // S0 regression: the Liquor Worm session suspended in stage "bodily" resumes and completes unchanged.
        var liquor = registry.Resolve(state.LiquorId);
        var liquorSession = Ledger.ChannelFor(liquor.Id);
        SetEssence(aperture, aperture.MaxEssence);
        double middle = aperture.EssenceOfQuality(RankOneStage.Middle);
        int remaining = liquorSession.PulseCount - liquorSession.PulsesDone;
        int liquorCommitted = Ledger.CommittedTotal;
        Check(liquorSession is { Complete: false, Active: false } && liquorSession.PulsesDone == state.LiquorPulses,
            "The Liquor Worm session is still suspended at its saved pulse count");
        Check(GuChannelService.TryOrderChannel(disciple, liquor.Id, LiquorRefinement, out string resumeLiquor), resumeLiquor ?? "The Liquor Worm session resumes");
        TickUntil(() => Ledger.ChannelFor(liquor.Id) == null, remaining * 260 + 20, "The Liquor Worm session completed");
        Check(liquorSession.PulsesDone == 10 && Math.Abs(aperture.EssenceOfQuality(RankOneStage.Middle) - (middle + remaining)) < 0.0001
            && Ledger.CommittedTotal == liquorCommitted && strength.Severity == 1f,
            $"Liquor Worm refined exactly its {remaining} remaining unit(s) with no new charge and no effect on bodily development");

        // Native medicine, death and resurrection on a mortal with a half-developed attainment.
        Pawn mortal = Clan(GuClanRole.MortalWorker).FirstOrDefault(pawn => Apertures(pawn).Count == 0 && !pawn.Dead)
            ?? Find.Maps.SelectMany(map => map.mapPawns.FreeColonistsSpawned).First(pawn => Apertures(pawn).Count == 0);
        for (int i = 0; i < 75; i++) Check(Hediff_GuBodilyAttainment.TryAdvance(mortal, BoarStrength, 150, out _, out _), "Fixture development step");
        var mortalStrength = Hediff_GuBodilyAttainment.Of(mortal, BoarStrength);
        float half = mortalStrength.Severity;
        Check(Math.Abs(half - 0.5f) < 1e-4f, $"The mortal fixture holds half development ({half:0.#####})");
        var torso = mortal.health.hediffSet.GetNotMissingParts().First(part => part.def == BodyPartDefOf.Torso);
        mortal.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.Cut, mortal, torso), torso);
        int cures = 0;
        for (; cures < 40 && HealthUtility.TryGetWorstHealthCondition(mortal, out var worst, out _); cures++)
        {
            Check(worst != mortalStrength, "Healer-serum curing never selects the attainment");
            HealthUtility.FixWorstHealthCondition(mortal);
        }
        Check(cures > 0 && !mortal.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(hediff => hediff.def == HediffDefOf.Cut)
            && Hediff_GuBodilyAttainment.Of(mortal, BoarStrength) == mortalStrength && mortalStrength.Severity == half
            && !HealthUtility.TryGetWorstHealthCondition(mortal, out _, out _),
            $"Healer-serum curing cured every condition ({cures}) including the wound, and left the attainment untouched");
        mortal.Kill(null);
        Check(mortal.Dead && Hediff_GuBodilyAttainment.Of(mortal, BoarStrength) == mortalStrength && mortalStrength.Severity == half,
            "Death keeps the attainment on the corpse");
        Check(ResurrectionUtility.TryResurrect(mortal) && !mortal.Dead && Hediff_GuBodilyAttainment.Of(mortal, BoarStrength) is { } revived
            && revived.Severity == half, "Native resurrection keeps the attainment and its development");

        File.WriteAllText(BodilyStatePath, JsonSerializer.Serialize(state with
        {
            SecondBoarId = second.Id, SecondOrigin = BoarOrigin.Of(second), StoredRevision = second.CustodyRevision,
            Severity = strength.Severity, MortalId = mortal.ThingID, MortalSeverity = half, Committed = Ledger.CommittedTotal,
            EssenceStock = BodilyEssenceStock(aperture)
        }));
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("bodily-reload");
    }

    // Stage "final": another fresh process.
    private static void VerifyBodilyFinal()
    {
        var (state, disciple, aperture, boar) = LoadBodilyState();
        var strength = Hediff_GuBodilyAttainment.Of(disciple, BoarStrength);
        var second = IndividualRegistry.Resolve(state.SecondBoarId);
        Check(BodilyEssenceStock(aperture).SequenceEqual(state.EssenceStock)
            && second.CustodyRevision == state.StoredRevision && BoarOrigin.Of(second) == state.SecondOrigin
            && IndividualRegistry.ProjectionOf(boar) is Thing_GuDormantProjection dormant
            && dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(),
            "The second registered Black Boar remains in the aperture and the original remains in the disciple's inventory");
        var mortal = Find.Maps.SelectMany(map => map.mapPawns.AllPawns).Single(pawn => pawn.ThingID == state.MortalId);
        Check(strength is { FullyDeveloped: true } && strength.Severity == 1f && Hediff_GuBodilyAttainment.AllOf(disciple).Count() == 1
            && Ledger.ChannelFor(boar.Id) == null && Ledger.ChannelFor(state.SecondBoarId) == null && Ledger.CommittedTotal == state.Committed
            && !GuChannelService.CanChannel(disciple, boar.Id, BoarCultivation, out _, out _, out _)
            && !GuChannelService.CanChannel(disciple, state.SecondBoarId, BoarCultivation, out _, out _, out _),
            "Full development, the finished sessions and the refusal of both Black Boar Gu persist");
        AssertBoarStats(disciple, strength);
        // The JSON sidecar does not round-trip floats exactly; the game save does (G9). Compare within one float step.
        Check(!mortal.Dead && Hediff_GuBodilyAttainment.Of(mortal, BoarStrength) is { } kept && Math.Abs(kept.Severity - state.MortalSeverity) < 1e-6f,
            $"The resurrected mortal's half development persists ({Hediff_GuBodilyAttainment.Of(mortal, BoarStrength)?.Severity:R})");
        var view = new ApertureView();
        view.Refresh(disciple);
        Check(view.Valid && Hediff_GuBodilyAttainment.AllOf(view.Pawn).Single() == strength, "The Cultivation page reads the attainment from the body");
        Report($"Final: disciple Black Boar strength {strength.Severity:0.###}, mortal {state.MortalSeverity:0.###}.");
        AssertNoErrors("bodily-final");
    }

    // ---------------------------------------------------------------------------------------------------------------
    private static GuRecord AcquireSurfaceGu(Pawn disciple, Hediff_Aperture aperture, GuRecord makeRoom, GuAbilityDef ability = null, Action<GuRecord> afterCapture = null)
    {
        var registry = IndividualRegistry;
        ability ??= BoarCultivation;
        var species = DefDatabase<GuSpeciesDef>.AllDefsListForReading.Single(def => def.abilities.Contains(ability));
        Map map = disciple.Map;
        var ecology = GuSurfaceEcology.Instance;
        Check(!Prefs.DevMode && GuSurfaceEcology.IsForest(map.Biome), "The disciple is in the real surface habitat with Development Mode off");
        TickUntil(() => ecology.LocalityAt(map, species.defName) != null, 500, "The surface locality is initialized");
        var locality = ecology.LocalityAt(map, species.defName);
        GuRecord Resident() => registry.Records.SingleOrDefault(record => record.SpeciesDefName == species.defName
            && record.CreationEvidence?.OwnerKind == GuSurfaceEcology.SourceKind && record.IsLiving && !record.SurfaceResidencyEnded
            && registry.ProjectionOf(record) is Pawn_GuWildProjection projection && projection.Map == map);
        if (Resident() == null && locality.Cohort.MatureCount == 0)
        {
            TickUntil(() => locality.ReplacementTicks >= 0, 500, "The empty surface cohort schedules native recruitment");
            locality.ReplacementTicks = 250;
        }
        TickUntil(() => Resident() != null, 7500, $"Ambient ecology manifested a registered {species.label}");
        var gu = Resident();
        var receipt = gu.CreationEvidence;
        int remaining = locality.Cohort.MatureCount;
        Check(gu.SpeciesDefName == species.defName && receipt.Trigger == GuSurfaceEcology.AmbientTrigger && gu.CohortDebitCommitted
            && receipt.RevisionAfter == receipt.RevisionBefore + 1 && registry.SurfaceEvidenceIntact(receipt.SourceId, locality.Cohort),
            "The ambient resident has a committed, conserved surface cohort debit");
        var wild = (Pawn_GuWildProjection)registry.ProjectionOf(gu);
        PositionBeside(disciple, wild);
        Order(disciple, wild, $"Capture {species.label} (unrefined)");
        TickUntil(() => registry.ProjectionOf(gu) is Thing_GuDormantProjection, 2400, $"Captured the registered {species.label}");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(gu);
        Check(dormant.GuId == gu.Id && gu.Refinement == null && gu.SurfaceResidencyEnded
            && locality.Cohort.MatureCount == remaining && !GuAbilityService.GrantsFor(disciple).Any(grant => grant.Gu == gu),
            "Capture preserved identity and ended residence without a second debit or an ability grant");
        afterCapture?.Invoke(gu);
        Order(disciple, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400, $"Picked up the captured {species.label}");
        SetEssence(aperture, aperture.MaxEssence);
        var view = new ApertureView();
        view.Refresh(disciple);
        var entry = view.Gu.SingleOrDefault(candidate => candidate.Record == gu);
        var enforce = entry?.Actions.SingleOrDefault(candidate => candidate.Kind == GuApertureActionKind.EnforceWill);
        Check(enforce is { Available: true }, $"Will-enforcement is offered for the captured {species.label} (entry {entry != null}; actions: "
            + string.Join(" | ", entry?.Actions.Select(candidate => $"{candidate.Kind} {candidate.Label} [{candidate.DisabledReason}]") ?? Enumerable.Empty<string>()) + ")");
        Check(enforce.Execute(out string failure), failure ?? "Will-enforcement started");
        TickUntil(() =>
        {
            if (aperture.Essence < aperture.MaxEssence * 0.5f) SetEssence(aperture, aperture.MaxEssence);
            if (Find.TickManager.TicksGame % 500 == 0) SustainColonists();
            if (gu.Refinement == null && disciple.CurJobDef?.defName != "GU_RefineMortalGu" && disciple.Awake() && !disciple.Downed)
            {
                view.Refresh(disciple);
                view.Gu.SingleOrDefault(candidate => candidate.Record == gu)?.Actions
                    .SingleOrDefault(candidate => candidate.Kind == GuApertureActionKind.ResumeWill)?.Execute(out _);
            }
            return gu.Refinement != null;
        }, 60_000);
        Check(gu.Refinement?.CultivatorId == Cultivators.FindCultivator(disciple).CultivatorId
            && !GuAbilityService.GrantsFor(disciple).Any(grant => grant.Gu == gu),
            $"Refined the {species.label} in inventory (job {disciple.CurJobDef?.defName}, downed {disciple.Downed}, awake {disciple.Awake()}, map {disciple.Map}, process "
            + $"{gu.RefinementProcess?.CompletedWorkTicks}/{gu.RefinementProcess?.TotalWorkTicks}, condition {gu.Condition}, placement {gu.Placement}, essence {aperture.Essence:0.#})");
        // A Rank 1 aperture holds two Gu: the player withdraws one to make room.
        if (makeRoom != null) Check(IndividualRegistry.TryWithdrawDormant(makeRoom.Id, makeRoom.CustodyRevision, disciple, aperture, out _, out string roomFailure),
            roomFailure ?? $"Withdrew {makeRoom.Species?.label} to make room");
        view.Refresh(disciple);
        var store = view.Gu.Single(candidate => candidate.Record == gu).Actions.SingleOrDefault(candidate => candidate.Kind == GuApertureActionKind.Store);
        Check(store != null && store.Execute(out _), $"Stored the {species.label} ({store?.DisabledReason})");
        Check(registry.ApertureOccupants(aperture).Contains(gu) && gu.ContainerId == aperture.ApertureId
            && dormant.Destroyed && registry.ProjectionOf(gu) == null && gu.CreationEvidence == receipt,
            "The same refined individual is stored in the disciple's aperture and the dormant projection is retired");
        return gu;
    }

    private static (BodilyState, Pawn, Hediff_Aperture, GuRecord) LoadBodilyState(string otherStoredId = null)
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        DisableFixtureAutosave(); // the fixture ticks a whole day inside one frame
        var state = JsonSerializer.Deserialize<BodilyState>(File.ReadAllText(BodilyStatePath));
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var aperture = Apertures(disciple).Single();
        var boar = IndividualRegistry.Resolve(state.BoarId);
        string storedId = state.SecondBoarId ?? state.BoarId;
        Check(aperture.ApertureId == state.ApertureId && boar?.SpeciesDefName == "GU_BlackBoarGu" && boar.IsActionable
            && BoarOrigin.Of(boar) == state.FirstOrigin && boar.CohortDebitCommitted && boar.SurfaceResidencyEnded
            && IndividualRegistry.ApertureOccupants(aperture).Select(record => record.Id).ToHashSet().SetEquals(new[] { otherStoredId ?? state.LiquorId, storedId }),
            "Reload kept the original registered Black Boar receipt and the exact two aperture occupants");
        return (state, disciple, aperture, boar);
    }

    private static IEnumerable<(string, string)> Pawn_ApertureNumbers(GuAbilityDef ability) => ITab_Pawn_Aperture.Numbers(ability);

    private static void VerifyBodilyRollbackAndCoexistence(Pawn pawn, Hediff_Aperture aperture, GuRecord boar)
    {
        SetEssence(aperture, 0f);
        SetEssence(aperture, 2f);
        Check(aperture.TryUpgradeEssence(RankOneStage.Initial, 1.5d), "Prepared a mixed-quality pulse");
        var before = BodilyEssenceStock(aperture);
        var grant = new GuAbilityGrant(pawn, aperture, boar, BoarCultivation);
        var worker = BoarCultivation.channel.Worker(BoarCultivation);
        var session = new GuChannelSession { Ability = BoarCultivation };
        var fault = new Harmony("projectgu.priority0test.bodily.rollback");
        var postfix = new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ThrowAfterBodilyHealthMutation));
        var add = AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });
        var notify = AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged));

        void CheckRollback(System.Reflection.MethodBase method, float? expectedSeverity)
        {
            fault.Patch(method, postfix: postfix);
            bool failed = false;
            try { worker.ApplyPulse(grant, session, out _); }
            catch (InvalidOperationException e) when (e.Message == "Injected bodily health callback failure") { failed = true; }
            finally { fault.Unpatch(method, HarmonyPatchType.Postfix, fault.Id); }
            Check(failed && BodilyEssenceStock(aperture).SequenceEqual(before)
                && Hediff_GuBodilyAttainment.Of(pawn, BoarStrength)?.Severity == expectedSeverity
                && session.Converted == 0d && session.PulseEssencePaid == 0d,
                "A failure after native health mutation rolls back both development and the exact mixed-quality payment");
        }

        CheckRollback(add, null);
        Check(Hediff_GuBodilyAttainment.TryAdvance(pawn, BoarStrength, 150, out _, out _), "Prepared an existing attainment for update rollback");
        var primary = Hediff_GuBodilyAttainment.Of(pawn, BoarStrength);
        CheckRollback(notify, primary.Severity);

        var distinct = new HediffDef
        {
            defName = "BodilyCoexistenceProbe", label = "coexistence probe", hediffClass = typeof(Hediff_GuBodilyAttainment),
            minSeverity = 0f, maxSeverity = 1f, isBad = false, tendable = false, everCurableByItem = false,
            keepOnBodyPartRestoration = true, stages = new List<HediffStage> { new() { multiplyStatChangesBySeverity = true } }
        };
        Check(Hediff_GuBodilyAttainment.TryAdvance(pawn, distinct, 100_000, out _, out _),
            "A distinct attainment Def develops beside Black Boar through the same implementation");
        var other = Hediff_GuBodilyAttainment.Of(pawn, distinct);
        other.Severity = 99_989f / 100_000f;
        for (int i = 0; i < 11; i++)
            Check(Hediff_GuBodilyAttainment.TryAdvance(pawn, distinct, 100_000, out _, out bool full) && full == (i == 10),
                "A slow attainment reaches full development on its exact last pulse");
        Check(primary.Severity == Step && other.Severity == 1f && Hediff_GuBodilyAttainment.AllOf(pawn).Count() == 2,
            "Different attainment Defs coexist and retain independent development");
        pawn.health.RemoveHediff(other);
        pawn.health.RemoveHediff(primary);
    }

    private static void ThrowAfterBodilyHealthMutation(Hediff hediff)
    {
        if (hediff.def == BoarStrength) throw new InvalidOperationException("Injected bodily health callback failure");
    }

    private static void AssertBoarStats(Pawn pawn, Hediff_GuBodilyAttainment strength)
    {
        var stage = strength.CurStage;
        foreach (var (stat, full) in new[] { ("MeleeDamageFactor", 1.4f), ("CarryingCapacity", 1.5f), ("MiningSpeed", 1.2f), ("ConstructionSpeed", 1.15f) })
        {
            var def = DefDatabase<StatDef>.GetNamed(stat);
            float factor = HediffStatsUtility.GetStatFactorForSeverity(def, stage, pawn, strength.Severity);
            Check(Math.Abs(factor - (1f + (full - 1f) * strength.Severity)) < 1e-5f, $"{stat} factor {factor:0.#####} matches development {strength.Severity:0.#####}");
        }
    }

    private static void BodilyTick(Pawn pawn, Hediff_Aperture aperture, GuChannelSession session, ref float expected)
    {
        int before = session?.PulsesDone ?? 0;
        Find.TickManager.DoSingleTick();
        if (pawn.IsHashIntervalTick(250)) expected = Math.Min(aperture.MaxEssence, expected + aperture.RegenerationPerHour / 10f);
        if (session != null) expected -= (session.PulsesDone - before) * PulseEssence;
    }

    private static void RunBodilyPulses(Pawn pawn, Hediff_Aperture aperture, GuChannelSession session, int pulses, ref float expected, HediffDef attainment = null)
    {
        attainment ??= BoarStrength;
        int target = session.PulsesDone + pulses;
        float severity = Hediff_GuBodilyAttainment.Of(pawn, attainment)?.Severity ?? 0f;
        double paid = session.PulseEssencePaid;
        for (int tick = 0; tick < pulses * 510 + 5 && session.PulsesDone < target; tick++) BodilyTick(pawn, aperture, session, ref expected);
        float after = Hediff_GuBodilyAttainment.Of(pawn, attainment)?.Severity ?? 0f;
        Check(session.PulsesDone == target && Math.Abs(aperture.Essence - expected) < 0.001f && session.PulseEssencePaid == paid + pulses
            && (after == 1f || Math.Abs(after - severity - pulses * Step) < 1e-5f),
            $"{pulses} pulse(s) paid exactly {pulses} essence and added exactly {pulses} step(s) (essence {aperture.Essence:0.###}, expected {expected:0.###}; severity {severity:0.#####} → {after:0.#####})");
    }

    private static void BodilyStop(Pawn pawn, Hediff_Aperture aperture, GuChannelSession session, Hediff_GuBodilyAttainment strength,
        ref float expected, string label, Action stop)
    {
        while (session.TicksIntoPulse < 60) BodilyTick(pawn, aperture, session, ref expected);
        int pulses = session.PulsesDone;
        float severity = strength.Severity;
        stop();
        for (int i = 0; i < 2; i++) BodilyTick(pawn, aperture, session, ref expected);
        Check(!session.Active && Ledger.ChannelFor(session.GuId) == session && pawn.CurJobDef?.defName != GuChannelService.ChannelJobDefName,
            $"{label} stopped the channel at once and suspended the session");
        for (int i = 0; i < 600; i++) BodilyTick(pawn, aperture, session, ref expected);
        Check(session.PulsesDone == pulses && strength.Severity == severity && Math.Abs(aperture.Essence - expected) < 0.001f,
            $"After {label}, development is kept and nothing more is gained or charged");
    }

    private static void BodilyResume(Pawn pawn, Hediff_Aperture aperture, GuChannelSession session, int expectedCommitted, ref float expected, GuAbilityDef ability = null)
    {
        ability ??= BoarCultivation;
        var command = AbilityGizmos(pawn).Single(gizmo => gizmo.Ability == ability);
        Check(!command.Disabled && command.defaultLabel.StartsWith("Resume", StringComparison.Ordinal)
            && GuChannelService.CanChannel(pawn, session.GuId, ability, out _, out var resumable, out _) && resumable == session,
            $"The command offers to resume the same session ({command.defaultLabel}) despite the cooldown");
        int partial = session.TicksIntoPulse;
        Check(GuChannelService.TryOrderChannel(pawn, session.GuId, ability, out string reason), reason ?? "resumed");
        BodilyTick(pawn, aperture, session, ref expected);
        Check(session.Active && Ledger.ChannelFor(session.GuId) == session && Ledger.CommittedTotal == expectedCommitted
            && Math.Abs(aperture.Essence - expected) < 0.001f && session.TicksIntoPulse == partial + 1,
            "Resuming continued the same session from its partial pulse without a new charge or commitment");
    }
}
