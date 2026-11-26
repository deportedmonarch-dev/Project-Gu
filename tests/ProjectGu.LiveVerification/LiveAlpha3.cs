using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using ProjectGu.Springs;
using RimWorld;
using Verse;

/// <summary>
/// Alpha 3 native acceptance: a Southern Border disciple lures a Liquor Worm with beer, refines and stores it beside a
/// Moonlight Gu, and channels Liquor Worm refinement through interruption, withdrawal, incapacitation, effects-off and a
/// Moonblade cast. Stage "new" saves during an active channel; "reload" completes it in a fresh process and nourishes the
/// walls with the refined essence; "final" checks the outcome after another fresh process.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha3 => GenCommandLine.CommandLineArgPassed("gualpha3");
    private static GuAbilityDef LiquorRefinement => DefDatabase<GuAbilityDef>.GetNamed("GU_LiquorWormRefinement");
    private static string Alpha3StatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha3-state.json");
    private const string LiquorSpecies = "GU_LiquorWorm";

    private sealed record Alpha3State(string LiquorId, string MoonId, string CultivatorId, string ApertureId, string SessionId,
        int PulsesDone, int TicksIntoPulse, double Initial, double Middle, int Committed, int CooldownUntil);

    // ---------------------------------------------------------------------------------------------------------------
    private static void ExerciseAlpha3(Pawn disciple, GuCultivatorRecord cultivator, Hediff_Aperture aperture,
        Thing springThing, CompGuSpiritSpring spring)
    {
        DebugSettings.enableStoryteller = false;
        var registry = IndividualRegistry;
        Map cave = disciple.Map;
        Check(!Prefs.DevMode && spring.MatureCountFor(LiquorSpecies) == 2 && spring.MatureCountFor("GU_MoonlightGu") == 2 && !registry.Records.Any(),
            "The new cavern spring holds two mature Liquor Worms beside the Moonlight and Hope Gu cohorts");
        // Alpha 5B: the spring explains the lure with an informational command, but never offers a plain search.
        Check(!springThing.GetGizmos().OfType<Command_Action>().Any(command => command.defaultLabel == "Search for Liquor Worm")
            && springThing.GetGizmos().OfType<Command_Action>().Any(command => command.defaultLabel == "Lure Liquor Worm"),
            "Liquor Worm is not offered as a plain search; the spring explains its lure instead");
        Check(springThing.GetFloatMenuOptions(disciple).Any(option => option.Label.StartsWith("Lure Liquor Worm (requires", StringComparison.Ordinal) && option.Disabled),
            "Without beer the lure is shown but unavailable");

        // 1. Acquire through the lure: the ordinary vanilla pick-up puts beer in inventory, then the spring's lure option.
        var beer = SpawnItem(cave, ThingDefOf.Beer, 1, disciple.Position, 3);
        var pickUp = beer.GetFloatMenuOptions(disciple).Concat(FloatMenuMakerMap.GetOptions(new List<Pawn> { disciple }, beer.DrawPos, out _))
            .FirstOrDefault(option => option.Label.StartsWith("Pick up", StringComparison.OrdinalIgnoreCase) && option.Label.Contains(beer.LabelNoCount) && option.action != null);
        Check(pickUp != null, "Vanilla offers to pick the beer up into inventory");
        pickUp.action();
        TickUntil(() => disciple.inventory.innerContainer.Any(thing => thing.def == ThingDefOf.Beer), 1200, "The disciple carried the beer in inventory");

        // An interrupted lure keeps the beer and the cohort.
        var spare = SpawnItem(cave, ThingDefOf.Beer, 1, disciple.Position, 5);
        int beerBefore = TotalBeer(cave);
        Order(disciple, springThing, "Lure Liquor Worm with beer");
        TickUntil(() => disciple.CurJobDef?.defName == "GU_LureWildGu" && disciple.Position.AdjacentTo8WayOrInside(springThing), 1500, "The lure job reached the spring");
        TickUntil(() => false, 120);
        disciple.drafter.Drafted = true;
        TickUntil(() => false, 30);
        disciple.drafter.Drafted = false;
        Check(TotalBeer(cave) == beerBefore && spring.MatureCountFor(LiquorSpecies) == 2 && !registry.Records.Any(),
            "Interrupting the lure kept every beer and drew no Liquor Worm");

        Order(disciple, springThing, "Lure Liquor Worm with beer");
        TickUntil(() => registry.Records.Any(), 2500, "The completed lure drew a wild Liquor Worm");
        var liquor = registry.Records.Single();
        var wild = (Pawn_GuWildProjection)registry.ProjectionOf(liquor);
        Check(liquor.SpeciesDefName == LiquorSpecies && wild.kindDef.defName == "GU_WildLiquorWorm" && liquor.CohortDebitCommitted
            && spring.MatureCountFor(LiquorSpecies) == 1 && TotalBeer(cave) == beerBefore - 1 && liquor.Refinement == null,
            $"One wild Liquor Worm {liquor.Id} answered the lure, debiting one adult and exactly one beer");
        DespawnTarget(spare);

        Order(disciple, wild, "Capture Liquor Worm (unrefined)");
        TickUntil(() => registry.ProjectionOf(liquor) is Thing_GuDormantProjection, 2400, "The capture job produced a dormant Liquor Worm");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(liquor);
        Order(disciple, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400, "The disciple picked up the dormant Liquor Worm");
        Check(!GuAbilityService.GrantsFor(disciple).Any() && !AbilityGizmos(disciple).Any()
            && !GuChannelService.CanChannel(disciple, liquor.Id, LiquorRefinement, out _, out _, out _),
            "An unrefined Liquor Worm grants nothing");

        SetEssence(aperture, aperture.MaxEssence);
        RunAlpha1ApertureOption(disciple, "Enforce will over", liquor.Id, "essence");
        TickUntil(() => liquor.Refinement != null, 40_000, "Will-enforcement over the Liquor Worm completed");
        Check(!GuAbilityService.GrantsFor(disciple).Any(), "A refined Liquor Worm grants nothing until stored");
        RunAlpha1ApertureOption(disciple, "Store ", liquor.Id);
        AssertStoredOnly(registry, aperture, new[] { liquor }, dormant);

        // 2. A second Gu shares the aperture: Moonlight Gu through its ordinary route.
        var moon = AcquireMoonlight(disciple, aperture, springThing);
        AssertStoredOnly(registry, aperture, new[] { liquor, moon }, null);
        var grants = GuAbilityService.GrantsFor(disciple).ToList();
        Check(grants.Count == 2 && grants.Any(grant => grant.Gu == liquor && grant.Ability == LiquorRefinement)
            && grants.Any(grant => grant.Gu == moon && grant.Ability == Moonblade)
            && AbilityGizmos(disciple).Count(command => !command.Disabled) == 2
            && !GuChannelService.CanChannel(Clan(GuClanRole.Elder).Single(), liquor.Id, LiquorRefinement, out _, out _, out _),
            "Both Gu share one aperture and each grants its own command to the disciple only");

        // 3. A new session commits once. Headroom covers 2 activation + 10 pulses + one 3-essence Moonblade.
        float start = Math.Min(aperture.MaxEssence, 20f);
        SetEssence(aperture, start);
        Check(aperture.EssenceOfQuality(RankOneStage.Initial) == start && aperture.Stage == RankOneStage.Initial, $"Fixture holds {start} Initial essence");
        var fx = cave.GetComponent<GuFxMapComponent>();
        int committed = Ledger.CommittedTotal;
        Check(GuChannelService.TryOrderChannel(disciple, liquor.Id, LiquorRefinement, out string reason), reason ?? "ordered");
        Find.TickManager.DoSingleTick();
        double recoveredOnCommit = disciple.IsHashIntervalTick(250) ? aperture.RegenerationPerHour / 10.0 : 0d;
        var session = Ledger.ChannelFor(liquor.Id);
        Check(session is { Active: true, PulsesDone: 0, EssencePaid: 2f } && Ledger.CommittedTotal == committed + 1
            && disciple.CurJobDef?.defName == GuChannelService.ChannelJobDefName
            && Ledger.CooldownRemaining(liquor.Id, Find.TickManager.TicksGame) > 0,
            "The channel committed one session, one 2-essence debit and the Gu's cooldown");
        Check(Math.Abs(Total(aperture) - (start - 2 + recoveredOnCommit)) < 0.0001,
            "The activation cost was paid once from the aperture");
        RunPulses(disciple, aperture, session, 3, fx, effectsExpected: true);
        Check(aperture.EssenceOfQuality(RankOneStage.Middle) == 3 && aperture.Stage == RankOneStage.Initial && aperture.WallNourishment == 0,
            "Three pulses refined exactly three Initial units into Middle with stage and walls unchanged");

        // 4. Every stop keeps completed pulses, costs nothing more and removes the manifestation at once.
        StopAndCheck(disciple, aperture, session, fx, "drafting", () => disciple.drafter.Drafted = true);
        double draftedTotal = Total(aperture);
        Check(!GuChannelService.TryOrderChannel(disciple, liquor.Id, LiquorRefinement, out string drafted) && drafted.Contains("Undraft")
            && Total(aperture) == draftedTotal && Ledger.CommittedTotal == committed + 1,
            $"A drafted Gu Master cannot start or resume the channel and pays nothing ({drafted})");
        disciple.drafter.Drafted = false;
        ResumeAndCheck(disciple, aperture, session, committed + 1);
        RunPulses(disciple, aperture, session, 1, fx, effectsExpected: true);

        Thing_GuDormantProjection withdrawn = null;
        StopAndCheck(disciple, aperture, session, fx, "withdrawal", () =>
            Check(registry.TryWithdrawDormant(liquor.Id, liquor.CustodyRevision, disciple, aperture, out withdrawn, out string why), why ?? "withdrawn"));
        Check(!GuChannelService.CanChannel(disciple, liquor.Id, LiquorRefinement, out _, out _, out _)
            && !GuAbilityService.GrantsFor(disciple).Any(grant => grant.Gu == liquor),
            "A withdrawn Liquor Worm cannot be channeled or resumed");
        Check(registry.TryStoreDormant(withdrawn, withdrawn.GuRevision, disciple, aperture, out string restored), restored ?? "restored");
        ResumeAndCheck(disciple, aperture, session, committed + 1);

        Hediff anesthetic = null;
        StopAndCheck(disciple, aperture, session, fx, "incapacitation", () => anesthetic = disciple.health.AddHediff(HediffDefOf.Anesthetic));
        Check(disciple.Downed, "The anesthetic incapacitated the disciple");
        disciple.health.RemoveHediff(anesthetic);
        TickUntil(() => !disciple.Downed, 300, "The disciple woke");
        ResumeAndCheck(disciple, aperture, session, committed + 1);

        // 5. Moonblade from the shared aperture interrupts the channel; the channel then resumes.
        var target = SpawnTarget(cave, disciple.Position, 6, 10);
        Check(GuAbilityService.TryOrderCast(disciple, moon.Id, Moonblade, target, out reason), reason ?? "cast ordered");
        TickUntil(() => session.Active == false, 5, "The Moonblade order suspended the channel");
        TickUntil(() => Ledger.InFlight.Any(), Moonblade.warmupTicks + 5, "Moonblade launched from the shared aperture");
        TickUntil(() => !Ledger.InFlight.Any(), 200, "Moonblade resolved");
        DespawnTarget(target);
        Check(Ledger.ChannelFor(liquor.Id) == session && session.PulsesDone == 4, "The cast left the suspended Liquor Worm session intact");
        ResumeAndCheck(disciple, aperture, session, committed + 2);

        // 6. Effects off: identical refinement, no manifestation.
        ProjectGuMod.Settings.cosmeticEffects = false;
        try { RunPulses(disciple, aperture, session, 1, fx, effectsExpected: false); }
        finally { ProjectGuMod.Settings.cosmeticEffects = true; }
        TickUntil(() => fx.CountKeyed(session.SessionId) == 1, 40, "Re-enabling effects re-attached exactly one manifestation");

        // 7. Save during the active channel, part way into a pulse.
        TickUntil(() => session.TicksIntoPulse >= 100, 260);
        Check(session.Active && session.PulsesDone == 5 && aperture.EssenceOfQuality(RankOneStage.Middle) == 5,
            "Five pulses are complete and a sixth is in progress at the save");
        var state = new Alpha3State(liquor.Id, moon.Id, cultivator.CultivatorId, aperture.ApertureId, session.SessionId, session.PulsesDone,
            session.TicksIntoPulse, aperture.EssenceOfQuality(RankOneStage.Initial), aperture.EssenceOfQuality(RankOneStage.Middle),
            Ledger.CommittedTotal, Find.TickManager.TicksGame + Ledger.CooldownRemaining(liquor.Id, Find.TickManager.TicksGame));
        File.WriteAllText(Alpha3StatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved during channel {session.SessionId}: {session.PulsesDone} pulses + {session.TicksIntoPulse} ticks, Initial {state.Initial:0.###}, Middle {state.Middle:0.###}.");
        AssertNoErrors("alpha3-new");
    }

    // Stage "reload": fresh process during an active channel.
    private static void VerifyAlpha3Channeling()
    {
        var (state, disciple, aperture, liquor) = LoadAlpha3State();
        var session = Ledger.ChannelFor(liquor.Id);
        var driver = disciple.jobs.curDriver as JobDriver_GuChannelAbility;
        Check(session is { Active: true } && session.SessionId == state.SessionId && session.PulsesDone == state.PulsesDone
            && session.TicksIntoPulse == state.TicksIntoPulse && driver?.SessionId == state.SessionId
            && aperture.EssenceOfQuality(RankOneStage.Initial) == state.Initial && aperture.EssenceOfQuality(RankOneStage.Middle) == state.Middle
            && Ledger.CommittedTotal == state.Committed,
            "Reload restored the active job, the exact session progress and both essence balances");
        var fx = disciple.Map.GetComponent<GuFxMapComponent>();
        Check(fx.CountKeyed(state.SessionId) == 0, "No manifestation is replayed from the save");
        TickUntil(() => fx.CountKeyed(state.SessionId) == 1, 5, "The live session re-attached one manifestation after load");

        var watch = new EssenceWatch(disciple, aperture);
        double middleBefore = aperture.EssenceOfQuality(RankOneStage.Middle);
        watch.Until(() => Ledger.ChannelFor(liquor.Id) == null, 2000, "The reloaded session completed");
        Check(session.PulsesDone == 10 && aperture.EssenceOfQuality(RankOneStage.Middle) == middleBefore + 5 && watch.Commits == 0
            && Ledger.CommittedTotal == state.Committed && aperture.Stage == RankOneStage.Initial,
            $"The remaining five pulses refined exactly five more units with no new charge ({session.Converted:0.###} total)");
        TickUntil(() => false, 2);
        Check(fx.CountKeyed(state.SessionId) == 0 && disciple.CurJobDef?.defName != GuChannelService.ChannelJobDefName,
            "Completion ended the job and removed the manifestation");
        Check(!GuChannelService.CanChannel(disciple, liquor.Id, LiquorRefinement, out _, out _, out string cooldown) && cooldown.Contains("recovering"),
            $"A new session waits for the cooldown ({cooldown})");

        // Wall nourishment uses the Liquor Worm-refined Middle essence at double weight.
        double wall = aperture.WallNourishment, middle = aperture.EssenceOfQuality(RankOneStage.Middle);
        var nourish = GuApertureGizmos.BuildOptions(disciple).Single(option => option.Label.StartsWith("Nourish aperture walls", StringComparison.Ordinal));
        nourish.action();
        TickUntil(() => aperture.WallNourishment > wall, Hediff_Aperture.NourishmentPulseTicks + 5, "One nourishment pulse completed");
        Check(aperture.WallNourishment == wall + 2 && aperture.EssenceOfQuality(RankOneStage.Middle) == middle - 1,
            "Nourishment spent one refined Middle unit for two wall nourishment");
        disciple.drafter.Drafted = true;
        disciple.drafter.Drafted = false;
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha3-reload");
    }

    // Stage "final": the finished session, cooldown, refined essence and both Gu persist.
    private static void VerifyAlpha3Final()
    {
        var (state, disciple, aperture, liquor) = LoadAlpha3State();
        Check(Ledger.ChannelFor(liquor.Id) == null && Ledger.CooldownRemaining(liquor.Id, Find.TickManager.TicksGame) == state.CooldownUntil - Find.TickManager.TicksGame
            && aperture.EssenceOfQuality(RankOneStage.Middle) >= 9 && aperture.Stage == RankOneStage.Initial
            && GuAbilityService.GrantsFor(disciple).Count() == 2,
            "After another fresh process the session is finished, the cooldown is unchanged and both Gu still grant their commands");
        Report($"Final: Initial {aperture.EssenceOfQuality(RankOneStage.Initial):0.###}, Middle {aperture.EssenceOfQuality(RankOneStage.Middle):0.###}, wall {aperture.WallNourishment:0.##}.");
        AssertNoErrors("alpha3-final");
    }

    // ---------------------------------------------------------------------------------------------------------------
    private static GuRecord AcquireMoonlight(Pawn disciple, Hediff_Aperture aperture, Thing springThing)
    {
        var registry = IndividualRegistry;
        var known = registry.Records.ToHashSet();
        springThing.GetGizmos().OfType<Command_Action>().Single(command => command.defaultLabel == "Search for Moonlight Gu").action();
        var moon = registry.Records.Single(record => !known.Contains(record));
        Order(disciple, registry.ProjectionOf(moon), "Capture Moonlight Gu (unrefined)");
        TickUntil(() => registry.ProjectionOf(moon) is Thing_GuDormantProjection, 2400, "Captured the Moonlight Gu");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(moon);
        Order(disciple, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400, "Picked up the Moonlight Gu");
        SetEssence(aperture, aperture.MaxEssence);
        RunAlpha1ApertureOption(disciple, "Enforce will over", moon.Id, "essence");
        TickUntil(() => moon.Refinement != null, 40_000, "Refined the Moonlight Gu");
        RunAlpha1ApertureOption(disciple, "Store ", moon.Id);
        return moon;
    }

    private static void AssertStoredOnly(GuIndividualRegistry registry, Hediff_Aperture aperture, IReadOnlyCollection<GuRecord> expected, Thing retired)
    {
        var occupants = registry.ApertureOccupants(aperture).ToList();
        var projected = ActuallyProjectedGu().ToList();
        Check(occupants.Count == expected.Count && expected.All(occupants.Contains) && (retired == null || retired.Destroyed) && projected.Count == 0,
            $"The aperture holds exactly the expected Gu with no physical projection (occupants {occupants.Count}, retired destroyed {retired?.Destroyed}, "
            + $"projected: {string.Join(", ", projected.Select(thing => $"{thing.def.defName} {thing.GetUniqueLoadID()} spawned={thing.Spawned} destroyed={thing.Destroyed} gu={thing.GuTag()?.GuId}"))})");
    }

    private static void RunPulses(Pawn disciple, Hediff_Aperture aperture, GuChannelSession session, int pulses, GuFxMapComponent fx, bool effectsExpected)
    {
        int target = session.PulsesDone + pulses;
        double initial = aperture.EssenceOfQuality(RankOneStage.Initial), middle = aperture.EssenceOfQuality(RankOneStage.Middle);
        int recoveries = 0;
        int maxManifestations = 0;
        for (int tick = 0; tick < pulses * 260 && session.PulsesDone < target; tick++)
        {
            Find.TickManager.DoSingleTick();
            if (disciple.IsHashIntervalTick(250)) recoveries++;
            maxManifestations = Math.Max(maxManifestations, fx.CountKeyed(session.SessionId));
        }
        double recovered = recoveries * aperture.RegenerationPerHour / 10.0;
        Check(session.PulsesDone == target && Math.Abs(aperture.EssenceOfQuality(RankOneStage.Middle) - (middle + pulses)) < 0.0001
            && Math.Abs(aperture.EssenceOfQuality(RankOneStage.Initial) - (initial - pulses + recovered)) < 0.0001,
            $"{pulses} pulse(s) moved exactly {pulses} unit(s) Initial→Middle (Middle {middle} → {aperture.EssenceOfQuality(RankOneStage.Middle)})");
        Check(effectsExpected ? maxManifestations == 1 : maxManifestations == 0,
            $"Manifestations while channeling: {maxManifestations} (effects {(effectsExpected ? "on" : "off")})");
    }

    private static void StopAndCheck(Pawn disciple, Hediff_Aperture aperture, GuChannelSession session, GuFxMapComponent fx, string label, Action stop)
    {
        TickUntil(() => session.TicksIntoPulse >= 60, 260);
        int pulses = session.PulsesDone;
        double middle = aperture.EssenceOfQuality(RankOneStage.Middle), total = Total(aperture);
        stop();
        TickUntil(() => false, 2);
        Check(!session.Active && Ledger.ChannelFor(session.GuId) == session && fx.CountKeyed(session.SessionId) == 0
            && disciple.CurJobDef?.defName != GuChannelService.ChannelJobDefName,
            $"{label} stopped the channel at once, suspended the session and removed the manifestation");
        TickUntil(() => false, 400);
        Check(session.PulsesDone == pulses && aperture.EssenceOfQuality(RankOneStage.Middle) == middle && Total(aperture) >= total,
            $"After {label}, completed pulses are kept, nothing more is refined and nothing more is charged");
    }

    private static void ResumeAndCheck(Pawn disciple, Hediff_Aperture aperture, GuChannelSession session, int expectedCommitted)
    {
        var command = AbilityGizmos(disciple).Single(gizmo => gizmo.Ability == LiquorRefinement);
        Check(!command.Disabled && command.defaultLabel.StartsWith("Resume", StringComparison.Ordinal)
            && GuChannelService.CanChannel(disciple, session.GuId, LiquorRefinement, out _, out var resumable, out _) && resumable == session,
            $"The command offers to resume the same session ({command.defaultLabel}) despite the cooldown");
        double total = Total(aperture);
        int partial = session.TicksIntoPulse;
        Check(GuChannelService.TryOrderChannel(disciple, session.GuId, LiquorRefinement, out string reason), reason ?? "resumed");
        Find.TickManager.DoSingleTick();
        Check(session.Active && Ledger.ChannelFor(session.GuId) == session && Ledger.CommittedTotal == expectedCommitted
            && Total(aperture) >= total && session.TicksIntoPulse == partial + 1,
            "Resuming continued the same session from its partial pulse without a new charge or commitment");
    }

    private static (Alpha3State, Pawn, Hediff_Aperture, GuRecord) LoadAlpha3State()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        var state = JsonSerializer.Deserialize<Alpha3State>(File.ReadAllText(Alpha3StatePath));
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var aperture = Apertures(disciple).Single();
        var liquor = IndividualRegistry.Resolve(state.LiquorId);
        var occupants = IndividualRegistry.ApertureOccupants(aperture).Select(record => record.Id).ToHashSet();
        Check(aperture.ApertureId == state.ApertureId && liquor?.SpeciesDefName == LiquorSpecies && occupants.SetEquals(new[] { state.LiquorId, state.MoonId })
            && IndividualRegistry.Records.Count == 2,
            "Reload kept the disciple's aperture with exactly the Liquor Worm and the Moonlight Gu");
        return (state, disciple, aperture, liquor);
    }

    private static double Total(Hediff_Aperture aperture) => Enum.GetValues<RankOneStage>().Sum(aperture.EssenceOfQuality);

    private static int TotalBeer(Map map) => map.listerThings.ThingsOfDef(ThingDefOf.Beer).Sum(thing => thing.stackCount)
        + map.mapPawns.AllPawnsSpawned.Sum(pawn => (pawn.inventory?.innerContainer.Where(thing => thing.def == ThingDefOf.Beer).Sum(thing => thing.stackCount) ?? 0)
            + (pawn.carryTracker?.CarriedThing is { } carried && carried.def == ThingDefOf.Beer ? carried.stackCount : 0));

    private static Thing SpawnItem(Map map, ThingDef def, int count, IntVec3 near, int radius)
    {
        var thing = ThingMaker.MakeThing(def);
        thing.stackCount = count;
        IntVec3 cell = GenRadial.RadialCellsAround(near, radius, true).First(candidate => candidate.InBounds(map) && candidate.Standable(map)
            && candidate.GetThingList(map).Count == 0 && candidate != near);
        return GenSpawn.Spawn(thing, cell, map);
    }
}
