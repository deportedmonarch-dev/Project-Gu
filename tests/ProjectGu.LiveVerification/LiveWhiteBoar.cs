using System.Text.Json;
using ProjectGu;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Interface;
using ProjectGu.Sustenance;
using RimWorld;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private const string BlackSpecies = "GU_BlackBoarGu";
    private const string WhiteSpecies = "GU_WhiteBoarGu";
    private static GuAbilityDef WhiteCultivation => DefDatabase<GuAbilityDef>.GetNamed("GU_WhiteBoarCultivation");
    private static HediffDef WhiteStrength => HediffDef.Named("GU_BodilyStrength_WhiteBoar");
    private static string WhiteStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "white-boar-state.json");
    private sealed record WhiteState(string Original, string Duplicate, BoarOrigin OriginalOrigin, BoarOrigin DuplicateOrigin,
        long OriginalRevision, long DuplicateRevision, long BlackCohort, long WhiteCohort);

    private static void ExerciseWhiteBoar()
    {
        DisableFixtureAutosave();
        var (state, pawn, aperture, _) = LoadBodilyState();
        var registry = IndividualRegistry;
        var black = registry.Resolve(state.SecondBoarId);
        var ecology = GuSurfaceEcology.Instance;
        var blackLocality = ecology.LocalityAt(pawn.Map, BlackSpecies);
        var whiteLocality = ecology.LocalityAt(pawn.Map, WhiteSpecies);
        Check(blackLocality != null && whiteLocality != null && blackLocality.Cohort != whiteLocality.Cohort,
            "The same eligible locality holds independent Rank 1 boar cohorts");
        GuRecord Wild(string species) => registry.Records.SingleOrDefault(record => record.SpeciesDefName == species
            && !record.SurfaceResidencyEnded && registry.ProjectionOf(record) is Pawn_GuWildProjection wild && wild.Map == pawn.Map);
        int Occupied(GuSurfaceEcology.Locality locality) => locality.Cohort.MatureCount + (Wild(locality.Cohort.SpeciesDefName) == null ? 0 : 1);
        foreach (var locality in new[] { blackLocality, whiteLocality })
        {
            if (Occupied(locality) < 2 && locality.ReplacementTicks >= 0) locality.ReplacementTicks = 250;
        }
        TickUntil(() => Wild(BlackSpecies) != null && Wild(WhiteSpecies) != null, 7500, "Both species manifested in the same eligible locality");
        var blackWild = Wild(BlackSpecies);
        var blackBody = registry.ProjectionOf(blackWild);
        long blackRevision = blackLocality.Cohort.Revision;
        int whiteOccupied = Occupied(whiteLocality);
        var white = AcquireSurfaceBoar(pawn, aperture, registry.Resolve(state.LiquorId), WhiteCultivation, captured =>
        {
            Check(registry.ProjectionOf(blackWild) == blackBody && blackLocality.Cohort.Revision == blackRevision
                && Occupied(whiteLocality) == whiteOccupied - 1 && captured.CreationEvidence.SourceId == blackWild.CreationEvidence.SourceId,
                "White Boar capture removes only its own resident, within the shared locality");
        });
        // Advance the existing replacement clock to its last sample in the disposable fixture.
        if (whiteLocality.ReplacementTicks < 0)
        {
            while (Occupied(whiteLocality) > 1)
            {
                TickUntil(() => Wild(WhiteSpecies) != null, 5200);
                ((Pawn_GuWildProjection)registry.ProjectionOf(Wild(WhiteSpecies))).TakeDamage(new DamageInfo(DamageDefOf.Cut, 1000, 20));
                TickUntil(() => false, 1);
            }
            TickUntil(() => whiteLocality.ReplacementTicks >= 0, 500);
        }
        Check(whiteLocality.ReplacementTicks is > 0 and <= 9 * GuSurfaceEcology.TicksPerDay, "White Boar has an independent replacement clock");
        whiteLocality.ReplacementTicks = 250;
        int beforeRecovery = Occupied(whiteLocality);
        TickUntil(() => Occupied(whiteLocality) > beforeRecovery, 750, "White Boar recruited one unit through native surface recovery");
        Check(Occupied(whiteLocality) == beforeRecovery + 1 && registry.ProjectionOf(blackWild) == blackBody
            && blackLocality.Cohort.Revision == blackRevision,
            "White Boar recovery leaves the Black Boar resident and cohort debit unchanged");
        var nextWhite = Wild(WhiteSpecies);
        if (nextWhite == null)
        {
            long whiteDebitCount = whiteLocality.Cohort.ReceiptDebits;
            TickUntil(() => Wild(WhiteSpecies) != null, 5200, "White Boar rediscovered with the Black Boar still active");
            nextWhite = Wild(WhiteSpecies);
            Check(whiteLocality.Cohort.ReceiptDebits == whiteDebitCount + 1
                && blackLocality.Cohort.Revision == blackRevision && registry.ProjectionOf(blackWild) == blackBody,
                "The discovery debits only the White Boar cohort and does not suppress the Black Boar");
        }
        Check(nextWhite.Id != white.Id && nextWhite.CohortDebitCommitted,
            "The next White Boar is a new registered individual with its own cohort debit");

        foreach (var colonist in pawn.Map.mapPawns.FreeColonistsSpawned) SustenanceComponent.SetAutoTend(colonist, false);
        Drain(white, Snap(white).NourishmentTicks);
        var carcass = SpawnCarcass(pawn.Map, pawn, "WildBoar");
        Check(GuSustenance.TryOrderFeed(pawn, white, out string feedReason), feedReason ?? "Ordered a White Boar meal");
        TickUntil(() => white.GetState<GuNourishment>().MealsEaten == 1, 2200, "White Boar ate through the native feeding job");
        Check(carcass.GetRotStage() == RotStage.Dessicated && Snap(white).NourishmentTicks > 4 * GuSurfaceEcology.TicksPerDay,
            "White Boar used its whole-corpse meal and five-day nourishment");

        // A level-zero miner can hit Core's 10% Mining Speed floor with either one or both attainments.
        pawn.skills.GetSkill(SkillDefOf.Mining).Level = 8;
        pawn.skills.GetSkill(SkillDefOf.Construction).Level = 8;
        Check(WhiteCultivation.channel is GuChannelProperties_BodilyCultivation
            { attainment: var attainment, pulseIntervalTicks: 500, pulseCount: 10, pulsesToFullDevelopment: 150, essencePerPulse: 1f }
            && attainment == WhiteStrength && WhiteCultivation.channel.workerClass == typeof(GuChannelWorker_BodilyCultivation)
            && WhiteCultivation.essenceCost == 0 && WhiteCultivation.cooldownTicks == 60000,
            "White Boar selects the shared bodily worker with the approved cadence and price");
        SetEssence(aperture, Math.Min(aperture.MaxEssence * 0.55f, 14f));
        float expected = aperture.Essence;
        int committed = Ledger.CommittedTotal;
        Check(GuChannelService.TryOrderChannel(pawn, white.Id, WhiteCultivation, out string reason), reason ?? "Ordered White Boar cultivation");
        BodilyTick(pawn, aperture, null, ref expected);
        var session = Ledger.ChannelFor(white.Id);
        Check(session is { Active: true, PulsesDone: 0, PulseEssencePaid: 0 } && Math.Abs(aperture.Essence - expected) < 0.001f,
            "White Boar begins with zero upfront payment");
        RunBodilyPulses(pawn, aperture, session, 2, ref expected, WhiteStrength);
        var strength = Hediff_GuBodilyAttainment.Of(pawn, WhiteStrength);
        BodilyStop(pawn, aperture, session, strength, ref expected, "drafting", () => pawn.drafter.Drafted = true);
        pawn.drafter.Drafted = false;
        BodilyResume(pawn, aperture, session, committed + 1, ref expected, WhiteCultivation);
        RunBodilyPulses(pawn, aperture, session, 8, ref expected, WhiteStrength);
        Check(session.PulseEssencePaid == 10 && Math.Abs(strength.Severity - 10 * Step) < 1e-5f
            && Ledger.ChannelFor(white.Id) == null
            && !GuChannelService.CanChannel(pawn, white.Id, WhiteCultivation, out _, out _, out string cooldown) && cooldown.Contains("recovering"),
            "White Boar completes ten paid pulses and the daily cooldown blocks a new session");
        for (int pulse = 10; pulse < 149; pulse++)
            Check(Hediff_GuBodilyAttainment.TryAdvance(pawn, WhiteStrength, 150, out _, out _), "Seeded a fixture step toward White Boar's cap");
        TickUntil(() =>
        {
            if (Now % 500 == 0) SustainColonists();
            return Ledger.CooldownRemaining(white.Id, Now) == 0;
        }, 61_000);
        SustainColonists();
        SetEssence(aperture, 14f);
        expected = aperture.Essence;
        Check(GuChannelService.TryOrderChannel(pawn, white.Id, WhiteCultivation, out reason), reason ?? "Ordered White Boar's final pulse");
        BodilyTick(pawn, aperture, null, ref expected);
        RunBodilyPulses(pawn, aperture, Ledger.ChannelFor(white.Id), 1, ref expected, WhiteStrength);
        var view = new ApertureView();
        view.Refresh(pawn);
        Check(view.ChannelActions.Any(action => action.GuId == black.Id) && view.ChannelActions.Any(action => action.GuId == white.Id)
            && Hediff_GuBodilyAttainment.AllOf(view.Pawn).Select(a => a.def).ToHashSet().SetEquals(new[] { BoarStrength, WhiteStrength }),
            "The interface has both registered techniques and reads both distinct bodily attainments");
        AssertCombinedBoarStats(pawn);

        var duplicate = AcquireSurfaceBoar(pawn, aperture, white, WhiteCultivation);
        Check(duplicate.Id != white.Id && !GuChannelService.CanChannel(pawn, duplicate.Id, WhiteCultivation, out _, out _, out string capped)
            && capped.Contains("fully developed") && !GuChannelService.TryOrderChannel(pawn, duplicate.Id, WhiteCultivation, out _),
            "A second stored White Boar cannot bypass the species cap");
        Check(Hediff_GuBodilyAttainment.Of(pawn, WhiteStrength) == strength && strength.FullyDeveloped,
            "Withdrawal of the original White Boar leaves the attainment on the pawn");
        File.WriteAllText(WhiteStatePath, JsonSerializer.Serialize(new WhiteState(white.Id, duplicate.Id, BoarOrigin.Of(white), BoarOrigin.Of(duplicate),
            white.CustodyRevision, duplicate.CustodyRevision, blackLocality.Cohort.Revision, whiteLocality.Cohort.Revision)));
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("white");
    }

    private static void VerifyWhiteBoarReload()
    {
        var saved = JsonSerializer.Deserialize<WhiteState>(File.ReadAllText(WhiteStatePath));
        var (state, pawn, aperture, originalBlack) = LoadBodilyState(saved.Duplicate);
        var registry = IndividualRegistry;
        var black = registry.Resolve(state.SecondBoarId);
        var white = registry.Resolve(saved.Original);
        var duplicate = registry.Resolve(saved.Duplicate);
        var ecology = GuSurfaceEcology.Instance;
        var whiteCohort = ecology.LocalityAt(pawn.Map, WhiteSpecies).Cohort;
        var blackCohort = ecology.LocalityAt(pawn.Map, BlackSpecies).Cohort;
        Check(originalBlack.SpeciesDefName == BlackSpecies && BoarOrigin.Of(black) == state.SecondOrigin && BoarOrigin.Of(originalBlack) == state.FirstOrigin
            && BoarOrigin.Of(white) == saved.OriginalOrigin && BoarOrigin.Of(duplicate) == saved.DuplicateOrigin
            && white.CustodyRevision == saved.OriginalRevision && duplicate.CustodyRevision == saved.DuplicateRevision
            && white.SpeciesDefName == WhiteSpecies && duplicate.SpeciesDefName == WhiteSpecies
            && registry.ApertureOccupants(aperture).ToHashSet().SetEquals(new[] { black, duplicate })
            && whiteCohort.Revision == saved.WhiteCohort && blackCohort.Revision == saved.BlackCohort
            && registry.SurfaceEvidenceIntact(saved.OriginalOrigin.Source, whiteCohort)
            && registry.SurfaceEvidenceIntact(state.FirstOrigin.Source, blackCohort),
            "Fresh process restored independent cohorts, exact identities, debit receipts and both stored occupants");
        AssertCombinedBoarStats(pawn);
        Check(!GuChannelService.CanChannel(pawn, black.Id, BoarCultivation, out _, out _, out _)
            && !GuChannelService.CanChannel(pawn, duplicate.Id, WhiteCultivation, out _, out _, out _),
            "Both attainment caps persist after fresh-process load");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(white);
        SustainColonists();
        var recipient = pawn.Map.mapPawns.FreeColonistsSpawned.First(other => other != pawn && other.IsColonistPlayerControlled
            && !other.Downed && !other.Drafted && other.Awake());
        Check(pawn.inventory.innerContainer.TryDrop(dormant, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _),
            "Dropped the original White Boar from the cultivator's inventory");
        PositionBeside(recipient, dormant);
        Order(recipient, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == recipient.inventory.innerContainer, 2400);
        Check(registry.ProjectionOf(white) == dormant && dormant.GuId == white.Id
            && !Hediff_GuBodilyAttainment.AllOf(recipient).Any(held => held.def == WhiteStrength),
            "Physical transfer preserves the Gu identity and leaves the attainment with its original pawn");
        dormant.Destroy(DestroyMode.Vanish);
        TickUntil(() => !white.IsLiving, 500, "The registry records the permanent loss of the original White Boar");
        AssertCombinedBoarStats(pawn);
        var torso = pawn.health.hediffSet.GetNotMissingParts().First(part => part.def == BodyPartDefOf.Torso);
        var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, torso);
        cut.Severity = 1f;
        pawn.health.AddHediff(cut, torso);
        for (int i = 0; i < 40 && pawn.health.hediffSet.hediffs.Contains(cut)
            && HealthUtility.TryGetWorstHealthCondition(pawn, out var worst, out _); i++)
        {
            Check(worst is not Hediff_GuBodilyAttainment, "Native medicine excludes both bodily attainments");
            HealthUtility.FixWorstHealthCondition(pawn);
        }
        Check(!pawn.health.hediffSet.hediffs.Contains(cut), "Native medicine removed the injury");
        AssertCombinedBoarStats(pawn);
        AssertNoErrors("white-reload");
    }

    private static readonly string[] BoarStats = { "MeleeDamageFactor", "CarryingCapacity", "MiningSpeed", "ConstructionSpeed" };
    private static float[] BoarNativeStats(Pawn pawn) =>
        BoarStats.Select(name => StatDef.Named(name).Worker.GetValue(StatRequest.For(pawn))).ToArray();

    private static void AssertCombinedBoarStats(Pawn pawn)
    {
        var black = Hediff_GuBodilyAttainment.Of(pawn, BoarStrength);
        var white = Hediff_GuBodilyAttainment.Of(pawn, WhiteStrength);
        Check(Hediff_GuBodilyAttainment.AllOf(pawn).Count() == 2 && black is { FullyDeveloped: true } && white is { FullyDeveloped: true },
            "Both body-owned attainments are independently full");
        // Take all three measurements in the same tick so capacities, skills and needs stay fixed.
        float[] combined = BoarNativeStats(pawn);
        float[] blackOnly;
        float[] baseline;
        try
        {
            white.Severity = 0f;
            pawn.health.hediffSet.DirtyCache();
            blackOnly = BoarNativeStats(pawn);
            black.Severity = 0f;
            pawn.health.hediffSet.DirtyCache();
            baseline = BoarNativeStats(pawn);
        }
        finally
        {
            black.Severity = 1f;
            white.Severity = 1f;
            pawn.health.hediffSet.DirtyCache();
        }
        float[] factors = { 1.4f, 1.5f, 1.2f, 1.15f };
        for (int i = 0; i < BoarStats.Length; i++)
        {
            float singleFactor = blackOnly[i] / baseline[i];
            float addedFactor = combined[i] / blackOnly[i];
            string measurement = $"Native {BoarStats[i]}: baseline {baseline[i]:R}, Black only {blackOnly[i]:R}, combined {combined[i]:R} (x{combined[i] / baseline[i]:0.####})";
            Check(Math.Abs(singleFactor - factors[i]) < 0.001f && Math.Abs(addedFactor - factors[i]) < 0.001f, measurement);
            Report(measurement);
        }
    }
}
