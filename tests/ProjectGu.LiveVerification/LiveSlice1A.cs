using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using ProjectGu.Presentation;
using ProjectGu.Sustenance;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private static bool Slice1A => GenCommandLine.CommandLineArgPassed("guslice1a");
    private static string SurfaceStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-slice1a.json");
    private static ThingDef Pork => ThingDef.Named("Meat_Pig");
    private record SurfaceState(string Gu, string Resident, string Carcass, string Cultivator, int Count, long Revision,
        int Replacement, int Ordinal, int Meals);

    private static List<GuRecord> SurfaceGu() => IndividualRegistry.Records
        .Where(record => record.SpeciesDefName == "GU_BlackBoarGu" && record.CreationEvidence?.OwnerKind == GuSurfaceEcology.SourceKind).ToList();

    private static void ExerciseSlice1A(Pawn elder, Pawn mortal, Map home, Map cave)
    {
        DisableFixtureAutosave();
        DebugSettings.enableStoryteller = false;
        Current.Game.CurrentMap = home;
        Check(!Prefs.DevMode && Apertures(mortal).Count == 0 && home.Biome.defName == "TemperateForest",
            "The surface fixture starts in temperate forest with a mortal and a Gu Master, with Development Mode off");
        Check(GuSurfaceEcology.Instance.LocalityAt(cave, "GU_BlackBoarGu") == null, "The established spring cavern has no surface population");
        TickUntil(() => GuSurfaceEcology.Instance.LocalityAt(home, "GU_BlackBoarGu") != null, 250);
        var habitat = GuSurfaceEcology.Instance.LocalityAt(home, "GU_BlackBoarGu");
        int initial = habitat.Cohort.MatureCount + SurfaceGu().Count(record => record.IsLiving);
        Check(initial is >= 0 and <= 3 && habitat.Cohort.JuvenileCount == 0, $"Initial forest occupancy is {initial} of 0–3 adults");
        if (initial == 0)
        {
            CheckReplacementRoll(habitat);
            habitat.ReplacementTicks = 250;
        }
        TickUntil(() => SurfaceGu().Count > 0, 5200, "Surface ecology manifested a Black Boar through the native tick");
        var caught = SurfaceGu().Single();
        var wild = (Pawn_GuWildProjection)IndividualRegistry.ProjectionOf(caught);
        var receipt = caught.CreationEvidence;
        Check(receipt.Trigger == GuSurfaceEcology.AmbientTrigger && receipt.Rank == 1 && caught.CohortDebitCommitted
            && receipt.RevisionAfter == receipt.RevisionBefore + 1 && habitat.Cohort.MatureCount + 1 == Math.Max(1, initial),
            "Ambient manifestation exchanged one mature unit for one registered individual");
        var start = wild.Position;
        long age = wild.ageTracker.AgeBiologicalTicks;
        Check(wild.needs.food == null && wild.RaceProps.disableMating && !wild.RaceProps.hasGenders && !wild.RaceProps.canFlyIntoMap
            && !wild.RaceProps.canLeaveMapFlying && Snap(caught).Mode == GuSustenanceMode.Wild,
            "The wild insect is grounded, has no food need or mating route, and uses abstract sustenance");
        var controlFood = SpawnItemAt(home, Pork, 10, EmptyCargoCell(home, start, mortal, 3));
        TickUntil(() => wild.Position != start, 1600, "The discovered Gu wanders through normal Pawn AI");
        Check(wild.ageTracker.AgeBiologicalTicks == age && controlFood.stackCount == 10 && wild.inventory.innerContainer.Count == 0
            && !wild.health.hediffSet.HasHediff(HediffDefOf.Malnutrition), "Wild wandering changed neither biological age nor food resources");
        controlFood.Destroy();
        ExerciseBlackBoarNativeGuards(home, mortal, wild);
        Report("Ambient manifestation, wandering, abstract feeding, fixed age and native exclusion controls passed.");

        PositionBeside(mortal, wild);
        Order(mortal, wild, "Capture Black Boar Gu (unrefined)");
        TickUntil(() => IndividualRegistry.ProjectionOf(caught) is Thing_GuDormantProjection, 1200,
            "The mortal captured the same individual through the ordinary float menu");
        var dormant = (Thing_GuDormantProjection)IndividualRegistry.ProjectionOf(caught);
        Check(caught.Refinement == null && caught.SurfaceResidencyEnded && dormant.GuId == caught.Id && habitat.Cohort.MatureCount == Math.Max(1, initial) - 1,
            "Capture preserved identity, ended residence and left the cohort debit intact");

        // For initial stock above the threshold, remove the remaining unit needed to trigger recovery via direct damage.
        if (habitat.Cohort.MatureCount > 1)
        {
            Check(habitat.ReplacementTicks < 0, "Stock above the minimum has not latched recovery");
            TickUntil(() => SurfaceGu().Count == 2, 3100, "A second encounter consumes another unit after the capture");
            var victim = SurfaceGu().Single(record => record.Id != caught.Id);
            var victimPawn = (Pawn_GuWildProjection)IndividualRegistry.ProjectionOf(victim);
            victimPawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 400, 20, instigator: mortal));
            TickUntil(() => !victim.IsLiving, 500, "A direct attack can kill a wild Gu without duplicating its unit");
        }
        TickUntil(() => habitat.ReplacementTicks >= 0, 500, "Removal to the threshold latched recovery");
        while (habitat.Cohort.MatureCount + SurfaceGu().Count(record => record.IsLiving && !record.SurfaceResidencyEnded) < 4)
        {
            CheckReplacementRoll(habitat);
            int ordinal = habitat.ReplacementOrdinal;
            long revision = habitat.Cohort.Revision;
            int before = habitat.Cohort.MatureCount + SurfaceGu().Count(record => record.IsLiving && !record.SurfaceResidencyEnded);
            habitat.ReplacementTicks = 250; // Advance the existing interval to its last native sample.
            TickUntil(() => habitat.Cohort.Revision > revision
                && habitat.Cohort.MatureCount + SurfaceGu().Count(record => record.IsLiving && !record.SurfaceResidencyEnded) > before,
                500, "A due production sample added exactly one adult");
            Check(habitat.Cohort.MatureCount + SurfaceGu().Count(record => record.IsLiving && !record.SurfaceResidencyEnded) == before + 1
                && (habitat.ReplacementOrdinal == ordinal + 1 || habitat.ReplacementTicks == -1),
                "The latched interval continues above one and closes at four");
        }
        Check(habitat.ReplacementTicks == -1 && habitat.Cohort.EvidenceIntact && IndividualRegistry.SurfaceEvidenceIntact(receipt.SourceId, habitat.Cohort),
            "Capacity closed recovery with all committed debit evidence intact");

        ExerciseSurfaceReplacement(mortal, home);
        Report($"Reached capacity four from an opening of {initial}. Desert rejection, deliberate search and map replacement passed.");
        var resident = SurfaceGu().Single(record => record.IsLiving && !record.SurfaceResidencyEnded
            && IndividualRegistry.ProjectionOf(record)?.Map == home);
        PositionBeside(elder, dormant);
        Order(elder, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == elder.inventory.innerContainer, 1800, "The Gu Master picked up the captured Gu");
        SetEssence(Apertures(elder).Single(), Apertures(elder).Single().MaxEssence);
        RunAlpha1ApertureOption(elder, "Enforce will over", caught.Id, "essence");
        TickUntil(() => caught.Refinement != null, 40000, "Real will-enforcement refined Black Boar Gu");
        RunAlpha1ApertureOption(elder, "Store ", caught.Id);
        Check(caught.ContainerKind == GuContainerKind.MortalAperture && caught.Refinement.CultivatorId == Cultivators.GetOrCreate(elder).CultivatorId,
            "The same refined individual is stored in its Gu Master's aperture");
        foreach (var colonist in home.mapPawns.FreeColonistsSpawned) SustenanceComponent.SetAutoTend(colonist, false);
        Check(Snap(caught).NourishmentTicks > 4 * GuSurfaceEcology.TicksPerDay
            && GuNourishmentRules.MealsWanted(caught.Species.feeding, caught.Species.feeding.foods[0], 1) == 0,
            "The initial meal lasts five days and no replacement ration is used while nourishment remains");
        Drain(caught, Snap(caught).NourishmentTicks);
        var carcass = SpawnCarcass(home, elder, "WildBoar");
        Check(GuSustenance.TryOrderFeed(elder, caught, out var reason), reason ?? "The native manual corpse feeding job started");
        var state = caught.GetState<GuNourishment>();
        TickUntil(() => state.MealsEaten == 1, 2000, "One boar corpse fed the Gu via the manual job");
        Check(carcass.GetRotStage() == RotStage.Dessicated && !carcass.Destroyed && !carcass.ButcherProducts(elder, 1f).Any(),
            "Corpse feeding leaves a native skeleton with zero butcher products");
        Drain(caught, Snap(caught).NourishmentTicks);
        Check(!GuSustenance.TryMakeFeedJob(elder, caught, false, out _, out _, out _), "The spent corpse cannot feed the Gu again");
        var a = SpawnItemAt(home, Pork, 60, EmptyCargoCell(home, elder.Position, elder, 3));
        var b = SpawnItemAt(home, Pork, 55, EmptyCargoCell(home, elder.Position, elder, 7));
        a.SetForbidden(false, false);
        b.SetForbidden(false, false);
        Check(caught.Species.feeding.foods[0].Accepts(a) && caught.Species.feeding.foods[0].Accepts(b),
            "The diet accepts Core pork from pigs and wild boar");
        Check(GuSustenance.TryOrderFeed(elder, caught, out reason), reason ?? "The manual pork ration ordered two physical stacks");
        TickUntil(() => elder.inventory.innerContainer.Any(t => t.def == Pork), 1800, "The feeder collected the first pork portion");
        Check(state.MealsEaten == 1 && TotalItems(home, Pork) == 115 && elder.CurJobDef == GuSustenance.FeedJob,
            "Save checkpoint has a partially gathered, unconsumed pork ration");
        File.WriteAllText(SurfaceStatePath, JsonSerializer.Serialize(new SurfaceState(caught.Id, resident.Id, carcass.GetUniqueLoadID(),
            elder.ThingID, habitat.Cohort.MatureCount, habitat.Cohort.Revision, habitat.ReplacementTicks, habitat.ReplacementOrdinal, state.MealsEaten)));
        AssertNoErrors("slice1a new");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifySlice1AReload()
    {
        var saved = JsonSerializer.Deserialize<SurfaceState>(File.ReadAllText(SurfaceStatePath));
        var home = Find.Maps.Single(map => map.IsPlayerHome);
        var habitat = GuSurfaceEcology.Instance.LocalityAt(home, "GU_BlackBoarGu");
        var elder = home.mapPawns.FreeColonistsSpawned.Single(pawn => pawn.ThingID == saved.Cultivator);
        var record = IndividualRegistry.Resolve(saved.Gu);
        var resident = IndividualRegistry.Resolve(saved.Resident);
        var state = record.GetState<GuNourishment>();
        var carcass = home.listerThings.ThingsOfDef(ThingDef.Named("Corpse_WildBoar")).OfType<Corpse>()
            .Concat(elder.inventory.innerContainer.OfType<Corpse>())
            .Concat(elder.carryTracker.innerContainer.OfType<Corpse>()).Single(corpse => corpse.GetUniqueLoadID() == saved.Carcass);
        Check(habitat.Cohort.MatureCount == saved.Count && habitat.Cohort.Revision == saved.Revision
            && habitat.ReplacementTicks == saved.Replacement && habitat.ReplacementOrdinal == saved.Ordinal
            && IndividualRegistry.SurfaceEvidenceIntact(resident.OriginId, habitat.Cohort)
            && IndividualRegistry.ProjectionOf(resident) is Pawn_GuWildProjection wild && wild.GuId == saved.Resident && wild.needs.food == null,
            "Fresh-process load preserved surface stock, timing, evidence and the exact living resident");
        Check(carcass.GetRotStage() == RotStage.Dessicated && !carcass.ButcherProducts(elder, 1f).Any(),
            "The spent carcass still yields zero on a fresh process");
        Check(record.Refinement != null && record.ContainerKind == GuContainerKind.MortalAperture && elder.CurJobDef == GuSustenance.FeedJob,
            "The refined individual and partially collected ration both survived reload");
        TickUntil(() => state.MealsEaten > saved.Meals, 3200, "The resumed native job completed the 100-unit ration");
        Check(state.MealsEaten == saved.Meals + 1 && state.ItemsEaten == 101 && TotalItems(home, Pork) == 15
            && Snap(record).NourishmentTicks > 299000, "The meat meal consumed precisely 100 units and supplied five days of nourishment");
        Drain(record, Snap(record).NourishmentTicks);
        var pig = SpawnCarcass(home, elder, "Pig");
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        stockpile.AddCell(pig.Position);
        stockpile.GetStoreSettings().filter.SetAllow(pig.def, true);
        SustenanceComponent.SetAutoTend(elder, true);
        TickUntil(() => state.MealsEaten == saved.Meals + 2, GuSustenanceComponent.SweepPeriod + 50,
            "The established automatic provisioning scheduler fed a pig corpse from the colony stockpile");
        Check(pig.GetRotStage() == RotStage.Dessicated && !pig.ButcherProducts(elder, 1f).Any()
            && TotalItems(home, Pork) == 15 && state.AutomaticMeals == 1, "Automatic corpse feeding recorded one meal with no duplicate pork");
        var remainingBoar = (Pawn_GuWildProjection)IndividualRegistry.ProjectionOf(resident);
        ExerciseBlackBoarPursuit(home, elder, remainingBoar, habitat);
        PositionBeside(elder, remainingBoar);
        Order(elder, remainingBoar, "Capture Black Boar Gu (unrefined)");
        TickUntil(() => IndividualRegistry.ProjectionOf(resident) is Thing_GuDormantProjection, 1200,
            "The Gu Master could also capture the same reloaded individual");
        Report("Fresh reload, 100-unit manual pork meal, automatic pig corpse meal, evasive pursuit, curiosity and Gu Master capture passed.");
        AssertNoErrors("slice1a reload");
    }

    private static void ExerciseBlackBoarNativeGuards(Map home, Pawn mortal, Pawn_GuWildProjection wild)
    {
        var checkbox = AccessTools.Method(typeof(PawnColumnWorker_Hunt), "HasCheckbox");
        var candidates = AccessTools.Method(typeof(IncidentWorker_SelfTame), "Candidates");
        var ordinary = (Pawn)GenSpawn.Spawn(PawnGenerator.GeneratePawn(PawnKindDef.Named("WildBoar")), EmptyCargoCell(home, mortal.Position, mortal, 6), home);
        var normal = new Designator_Hunt();
        Check(!normal.CanDesignateThing(wild).Accepted && normal.CanDesignateThing(ordinary).Accepted
            && !(bool)checkbox.Invoke(new PawnColumnWorker_Hunt(), new object[] { wild })
            && (bool)checkbox.Invoke(new PawnColumnWorker_Hunt(), new object[] { ordinary }),
            "Designator and Wildlife tab hide the hunt route only for Gu");
        Check(!TameUtility.CanTame(wild) && TameUtility.CanTame(ordinary)
            && !IncidentWorker_AnimalInsanityMass.AnimalUsable(wild) && IncidentWorker_AnimalInsanityMass.AnimalUsable(ordinary),
            "Taming and insanity reject Gu while accepting an ordinary boar");
        Check(!Pawn_TrainingTracker.CanAssignToTrain(TrainableDefOf.Tameness, wild.def, out _, wild).Accepted
            && Pawn_TrainingTracker.CanAssignToTrain(TrainableDefOf.Tameness, ordinary.def, out _, ordinary).Accepted,
            "The native training query refuses a Gu and accepts an ordinary boar");
        var selfTame = new IncidentWorker_SelfTame();
        var eligible = ((IEnumerable<Pawn>)candidates.Invoke(selfTame, new object[] { home })).ToList();
        Check(!eligible.Contains(wild) && eligible.Contains(ordinary) && wild.Faction == null,
            "The actual self-tame candidate set excludes the Gu and keeps the ordinary boar");
        ordinary.Destroy();
    }

    private static Pawn heldPursuer;
    private static bool MoveUnheldPawns(Pawn ___pawn) => ___pawn != heldPursuer;

    private static void ExerciseBlackBoarPursuit(Map map, Pawn mortal, Pawn_GuWildProjection wild, GuSurfaceEcology.Locality habitat)
    {
        mortal.jobs.EndCurrentJob(JobCondition.InterruptForced);
        mortal.Position = GenRadial.RadialCellsAround(wild.Position, 6, false).First(cell => cell.InBounds(map)
            && cell.DistanceToSquared(wild.Position) >= 25 && cell.Standable(map) && cell.GetFirstPawn(map) == null
            && map.reachability.CanReach(cell, wild, PathEndMode.Touch, TraverseMode.PassDoors));
        mortal.Notify_Teleported();
        wild.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 500), JobCondition.InterruptForced);
        var fixture = new Harmony("projectgu.priority0test.pursuit");
        var movement = AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick));
        fixture.Patch(movement, prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(MoveUnheldPawns)));
        try
        {
            heldPursuer = mortal;
            int started = Now;
            Order(mortal, wild, "Capture Black Boar Gu (unrefined)");
            int pursuit = mortal.CurJob.loadID;
            bool fled = false;
            TickUntil(() =>
            {
                fled |= wild.CurJobDef == JobDefOf.Flee;
                return mortal.CurJob?.loadID != pursuit;
            }, 1280, "The active native pursuit reached its abandon deadline");
            Check(fled && wild.Spawned && wild.Map == map && Now >= started + 1199,
                "The Gu fled an actual captor, stayed on the map and the unsuccessful pursuer gave up");
        }
        finally
        {
            heldPursuer = null;
            fixture.Unpatch(movement, HarmonyPatchType.Prefix, fixture.Id);
        }

        PositionBeside(mortal, wild);
        var species = DefDatabase<GuSpeciesDef>.GetNamed("GU_BlackBoarGu");
        var target = AccessTools.Field(typeof(Pawn_GuWildProjection), "curiosityTarget");
        var revision = habitat.Cohort.Revision;
        float normalChance = species.wildCuriosityChancePerHour;
        bool wasDrafted = mortal.Drafted;
        try
        {
            Check(normalChance == 0.001f, "The production curiosity chance is rare and configurable");
            species.wildCuriosityChancePerHour = 1f;
            mortal.drafter.Drafted = true;
            mortal.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 5300), JobCondition.InterruptForced);
            wild.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 5300), JobCondition.InterruptForced);
            Check(wild.CanReach(mortal, PathEndMode.Touch, Danger.Some), "The stationary curiosity fixture has a reachable colonist");
            TickUntil(() => target.GetValue(wild) is Pawn, 5200, "The wild Gu picked a particular colonist through its native interval");
            Check(habitat.Cohort.Revision == revision && IndividualRegistry.Resolve(wild) != null,
                "Curiosity kept the same Gu and changed no population");
        }
        finally
        {
            species.wildCuriosityChancePerHour = normalChance;
            mortal.drafter.Drafted = wasDrafted;
        }
    }

    private static void ExerciseSurfaceReplacement(Pawn scout, Map original)
    {
        var tile = Find.WorldGrid.Surface.Tiles.First(candidate => candidate.PrimaryBiome.defName == "Desert"
            && candidate.hilliness < Hilliness.Mountainous && TileFinder.IsValidTileForNewSettlement(candidate.tile));
        var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        settlement.Tile = tile.tile;
        settlement.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(settlement);
        GenMapParent(parent: settlement);
        Map map = settlement.Map;
        Check(map != null && map.IsPlayerHome && map.Biome.defName == "Desert", "Generated a real hard-ineligible desert settlement");
        IntVec3 previous = scout.Position;
        scout.jobs.EndCurrentJob(JobCondition.InterruptForced);
        scout.DeSpawn();
        GenSpawn.Spawn(scout, map.Center, map);
        TickUntil(() => false, 2600);
        Check(GuSurfaceEcology.Instance.LocalityAt(map, "GU_BlackBoarGu") == null && !GuSurfaceEcology.SearchGround(map, scout.Position)
            && !map.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>().Any(pawn => pawn.kindDef.defName == "GU_WildBlackBoarGu")
            && GuSurfaceEcology.Suitability(BiomeDef.Named("Tundra")) == 0, "Desert rejects initialization, ambient encounters and searching. Tundra is also hard-ineligible");

        // Change the disposable tile's fixture biome, then generate a fresh forest map at the same locality.
        scout.DeSpawn();
        GenSpawn.Spawn(scout, previous, original);
        Current.Game.DeinitAndRemoveMap(map, false);
        tile.PrimaryBiome = BiomeDef.Named("TemperateForest");
        GenMapParent(settlement);
        map = settlement.Map;
        Current.Game.CurrentMap = original;
        var cameraBefore = SurfaceCameraTarget();
        int startTick = Now;
        TickUntil(() => GuSurfaceEcology.Instance.LocalityAt(map, "GU_BlackBoarGu") != null, 250);
        var habitat = GuSurfaceEcology.Instance.LocalityAt(map, "GU_BlackBoarGu");
        var whiteHabitat = GuSurfaceEcology.Instance.LocalityAt(map, "GU_WhiteBoarGu");
        Pawn_GuWildProjection Wild(string species) => map.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>()
            .SingleOrDefault(pawn => IndividualRegistry.Resolve(pawn)?.SpeciesDefName == species);
        foreach (var locality in new[] { habitat, whiteHabitat })
        {
            if (locality.Cohort.MatureCount == 0 && Wild(locality.Cohort.SpeciesDefName) == null)
            {
                CheckReplacementRoll(locality);
                locality.ReplacementTicks = 250;
            }
        }
        var placements = new Dictionary<Pawn, IntVec3>();
        bool BothManifested()
        {
            foreach (var species in new[] { BlackSpecies, WhiteSpecies })
                if (Wild(species) is { } resident) placements.TryAdd(resident, resident.Position);
            return placements.Count == 2;
        }
        TickUntil(BothManifested, 5200,
            "Both species manifested on a newly initialized settlement without any colonists or jobs on the map");
        var before = Wild(BlackSpecies);
        var white = Wild(WhiteSpecies);
        var record = IndividualRegistry.Resolve(before);
        Check(map.mapPawns.FreeColonistsSpawned.Count == 0 && Find.CurrentMap == original && SurfaceCameraTarget() == cameraBefore
            && !LiveMessages().Any(message => message.startingTick >= startTick && message.text.Contains("discovered a wild")),
            "Witness-free manifestation left the player's map and camera alone and issued no discovery message");
        Check(new[] { before, white }.All(pawn => GuSurfaceEcology.AmbientHabitat(map, placements[pawn])
            && IndividualRegistry.Resolve(pawn).CreationEvidence.Trigger == GuSurfaceEcology.AmbientTrigger)
            && habitat.Cohort.ReceiptDebits == 1 && whiteHabitat.Cohort.ReceiptDebits == 1,
            "Both initial placements used open natural habitat and each species paid its own debit");
        var departure = before.Position;
        TickUntil(() => before.Position != departure, 1600, "The ambient resident wandered before any colonist visited");

        scout.DeSpawn();
        GenSpawn.Spawn(scout, map.Center, map);
        ExerciseSurfaceSearch(scout, before, white, habitat);
        long revision = habitat.Cohort.Revision;
        int count = habitat.Cohort.MatureCount;
        scout.DeSpawn();
        GenSpawn.Spawn(scout, previous, original);
        Current.Game.CurrentMap = original;
        Current.Game.DeinitAndRemoveMap(map, false);
        TickUntil(() => record.ContainerKind == GuContainerKind.WorldSite, 250, "Native teardown kept a living resident at its world locality");
        int remaining = habitat.ReplacementTicks;
        TickUntil(() => false, 600);
        Check(habitat.ReplacementTicks == remaining, "The unloaded locality did not bank any replacement time");
        GenMapParent(settlement);
        map = settlement.Map;
        TickUntil(() => IndividualRegistry.ProjectionOf(record) is Pawn_GuWildProjection { Spawned: true }, 5200,
            "The regenerated map remanifested the same resident without any colonist arriving");
        Check(IndividualRegistry.ProjectionOf(record) != before && habitat == GuSurfaceEcology.Instance.LocalityAt(map, "GU_BlackBoarGu")
            && habitat.Cohort.MatureCount == count && habitat.Cohort.Revision == revision && record.CohortDebitCommitted,
            "Map replacement preserved the ID and charged no second debit");
        while (true)
        {
            var victim = Wild(BlackSpecies);
            var victimRecord = IndividualRegistry.Resolve(victim);
            victim.TakeDamage(new DamageInfo(DamageDefOf.Cut, 1000, 20));
            TickUntil(() => !victimRecord.IsLiving, 250, "Direct lethal damage ended the resident");
            if (habitat.Cohort.MatureCount == 0) break;
            TickUntil(() => Wild(BlackSpecies) != null, 5200, "Remaining stock supplied the vacant resident slot");
        }
        long depletedRevision = habitat.Cohort.Revision;
        TickUntil(() => false, 2600);
        Check(Wild(BlackSpecies) == null && habitat.Cohort.MatureCount == 0 && habitat.Cohort.Revision == depletedRevision
            && habitat.ReplacementTicks > 0 && Wild(WhiteSpecies) != null,
            "A depleted species stayed absent across an ambient attempt while recovery waited and White Boar remained");
        Current.Game.DeinitAndRemoveMap(map, false);
        settlement.Destroy();
    }

    private static List<Message> LiveMessages() =>
        (List<Message>)AccessTools.Field(typeof(Messages), "liveMessages").GetValue(null);

    private static IntVec3 SurfaceCameraTarget()
    {
        var target = ((UnityEngine.Vector3)AccessTools.Field(typeof(CameraDriver), "rootPos").GetValue(Find.CameraDriver)).ToIntVec3();
        return new IntVec3(target.x, 0, target.z);
    }

    private static void ExerciseSurfaceSearch(Pawn scout, Pawn_GuWildProjection wild, Pawn_GuWildProjection other,
        GuSurfaceEcology.Locality habitat)
    {
        Map map = scout.Map;
        Current.Game.CurrentMap = map;
        foreach (var resident in new[] { wild, other })
            resident.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 2200), JobCondition.InterruptForced);
        var position = wild.Position;
        var center = GenRadial.RadialCellsAround(position, JobDriver_GuSearchSurface.SearchRadius, false)
            .First(cell => GuSurfaceEcology.SearchGround(map, cell) && cell.GetFirstPawn(map) == null
                && cell.DistanceToSquared(position) >= 4 && cell.DistanceToSquared(position) < cell.DistanceToSquared(other.Position)
                && map.reachability.CanReach(cell, wild, PathEndMode.Touch, TraverseMode.PassDoors));
        var distant = map.AllCells.First(cell => GuSurfaceEcology.SearchGround(map, cell) && cell.GetFirstPawn(map) == null
            && cell.DistanceToSquared(position) > 100 && cell.DistanceToSquared(other.Position) > 100);
        var jobDef = DefDatabase<JobDef>.GetNamed("GU_SearchSurface");
        void SearchAt(IntVec3 cell, bool ordered)
        {
            scout.jobs.EndCurrentJob(JobCondition.InterruptForced);
            scout.Position = cell;
            scout.Notify_Teleported();
            var option = new FloatMenuOptionProvider_GuSearchSurface().GetOptions(new FloatMenuContext(new List<Pawn> { scout },
                cell.ToVector3Shifted(), map)).SingleOrDefault(entry => entry.Label == "Search for wild Gu");
            Check(option != null, "An ordinary terrain right-click exposes Search for wild Gu");
            if (ordered) option.action();
            else scout.jobs.StartJob(JobMaker.MakeJob(jobDef, cell), JobCondition.InterruptForced);
            TickUntil(() => scout.CurJobDef == jobDef, 20, "The surface search job started");
            TickUntil(() => scout.CurJobDef != jobDef, 400, "The surface search job finished");
        }

        Find.CameraDriver.JumpToCurrentMapLoc(distant);
        long revision = habitat.Cohort.Revision;
        int recordCount = IndividualRegistry.Records.Count;
        Check(GuSurfaceEcology.FindWildGu(scout, distant, JobDriver_GuSearchSurface.SearchRadius) == null,
            "Detection excludes living Gu outside the search radius");
        SearchAt(distant, ordered: true);
        Check(SurfaceCameraTarget() == distant && LiveMessages().Any(message => message.text == "You found no signs of wild Gu here."),
            "An unsuccessful player search reported the result without moving the camera");
        SearchAt(center, ordered: false);
        Check(SurfaceCameraTarget() == distant && GuSurfaceEcology.FindWildGu(scout, center, JobDriver_GuSearchSurface.SearchRadius) == wild,
            "The same detection is available to autonomous searches without changing the player's camera");

        SearchAt(center, ordered: true);
        var arrows = (List<UnityEngine.Vector3>)AccessTools.Field(typeof(TargetHighlighter), "arrowPositions").GetValue(null);
        map.GetComponent<GuFxMapComponent>().MapComponentUpdate();
        Check(SurfaceCameraTarget() == position && arrows.Contains(new LocalTargetInfo(wild).CenterVector3)
            && LiveMessages().Any(message => message.text.Contains($"discovered a wild {wild.LabelShort}")),
            "The player order moved the native camera to the actual Gu and queued its native target highlight");
        Check(wild.Position == position && IndividualRegistry.Records.Count == recordCount && habitat.Cohort.Revision == revision,
            "Search preserved the exact Pawn, its position, registry count and cohort revision");
    }

    private static void GenMapParent(Settlement parent) =>
        MapGenerator.GenerateMap(new IntVec3(71, 1, 71), parent, MapGeneratorDefOf.Base_Player, null, null, false, false);

    private static void CheckReplacementRoll(GuSurfaceEcology.Locality habitat) =>
        Check(habitat.ReplacementTicks is >= (3 * GuSurfaceEcology.TicksPerDay - 250) and <= (9 * GuSurfaceEcology.TicksPerDay),
            $"Replacement interval is within 3–9 days ({habitat.ReplacementTicks} eligible ticks)");

    private static void PositionBeside(Pawn actor, Thing target)
    {
        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
        actor.Position = GenAdj.CellsAdjacent8Way(target).First(cell => cell.InBounds(actor.Map) && cell.Standable(actor.Map)
            && cell.GetFirstPawn(actor.Map) == null);
        actor.Notify_Teleported();
    }

    private static Corpse SpawnCarcass(Map map, Pawn feeder, string kind)
    {
        var animal = (Pawn)GenSpawn.Spawn(PawnGenerator.GeneratePawn(PawnKindDef.Named(kind)), EmptyCargoCell(map, feeder.Position, feeder, 4), map);
        animal.Kill(null);
        var corpse = animal.Corpse;
        corpse.SetForbidden(false, false);
        return corpse;
    }
}
