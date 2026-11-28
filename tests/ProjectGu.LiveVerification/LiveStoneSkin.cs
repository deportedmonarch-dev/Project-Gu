using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using ProjectGu.Presentation;
using ProjectGu.Sustenance;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

public sealed partial class LiveLifecycleChecks
{
    private const string StoneSpecies = "GU_StoneSkinGu";
    private static GuAbilityDef StoneSkin => DefDatabase<GuAbilityDef>.GetNamed("GU_StoneSkin");
    private static HediffDef StoneProtection => HediffDef.Named("GU_StoneSkinProtection");
    private static string StoneStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "stone-skin-state.json");

    private sealed record StoneState(string GuId, string MoonId, string PawnId, string ApertureId, int Revision,
        int Tick, int PaidUntil, double[] Stock, float[] ActiveStats, BoarOrigin Origin);

    private static void ExerciseStoneSkin()
    {
        var (state, pawn, aperture, liquor) = LoadAlpha3State();
        DisableFixtureAutosave();
        var species = DefDatabase<GuSpeciesDef>.GetNamed(StoneSpecies);
        var forest = BiomeDef.Named("TemperateForest");
        var desert = BiomeDef.Named("Desert");
        var habitat = species.surfaceHabitat;
        Check(habitat == GuSurfaceHabitat.Rocky && GuSurfaceEcology.Suitability(habitat, forest, Hilliness.Mountainous, true) == 3
            && GuSurfaceEcology.Suitability(habitat, desert, Hilliness.SmallHills, true) == 2
            && GuSurfaceEcology.Suitability(habitat, forest, Hilliness.Flat, true) == 1
            && GuSurfaceEcology.Suitability(habitat, forest, Hilliness.Flat, false) == 0
            && GuSurfaceEcology.Suitability(habitat, desert, Hilliness.Flat, true) == 0,
            "Rocky hills and mountains outrank forests with exposed rock. Flat rockless forests and flat deserts are excluded.");

        var map = StoneMountainSettlement(forest);
        var tile = Find.WorldGrid[map.Tile];
        var originalHilliness = tile.hilliness;
        tile.hilliness = Hilliness.Flat;
        Check(GuSurfaceEcology.Suitability(map, species) == 1, "Real exposed natural rock qualifies a flat forest as secondary habitat");
        tile.hilliness = originalHilliness;
        var ecology = GuSurfaceEcology.Instance;
        string[] speciesNames = { BlackSpecies, WhiteSpecies, StoneSpecies };
        TickUntil(() => speciesNames.All(name => ecology.LocalityAt(map, name) != null), 500);
        var localities = speciesNames.Select(name => ecology.LocalityAt(map, name)).ToArray();
        Check(localities.Select(locality => locality.Cohort).Distinct().Count() == 3 && localities.All(locality => locality.Cohort.MatureCount <= 3),
            "Three separate cohorts each initialize with up to three adults");
        foreach (var locality in localities.Where(locality => locality.Cohort.MatureCount == 0))
        {
            CheckReplacementRoll(locality);
            locality.ReplacementTicks = 250;
        }
        Pawn_GuWildProjection Wild(string name) => map.mapPawns.AllPawnsSpawned.OfType<Pawn_GuWildProjection>()
            .FirstOrDefault(wild => IndividualRegistry.Resolve(wild)?.SpeciesDefName == name);
        var sightings = new Dictionary<string, IntVec3>();
        TickUntil(() =>
        {
            foreach (string name in speciesNames)
                if (Wild(name) is { } found) sightings.TryAdd(name, found.Position);
            return sightings.Count == 3;
        }, 10000, "All three species manifested on the mountain settlement before any colonist arrived");
        var stoneWild = Wild(StoneSpecies);
        Check(map.mapPawns.FreeColonistsSpawned.Count == 0 && localities.All(locality => locality.Cohort.ReceiptDebits is >= 1 and <= 2)
            && GuSurfaceEcology.AmbientHabitat(map, sightings[StoneSpecies], habitat) && stoneWild.GetStatValue(StatDefOf.MoveSpeed) < 3f,
            "Stone Skin manifested beside exposed natural rock with its own debit and slow native Move Speed");
        var position = stoneWild.Position;
        TickUntil(() => stoneWild.Position != position, 2200, "Stone Skin used ordinary wandering without a witness");

        TransferStoneFixturePawn(pawn, map);
        Current.Game.CurrentMap = map;
        foreach (var wild in speciesNames.Select(Wild)) wild.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 700), JobCondition.InterruptForced);
        position = stoneWild.Position;
        var ground = GenRadial.RadialCellsAround(position, JobDriver_GuSearchSurface.SearchRadius, false).First(cell =>
            GuSurfaceEcology.SearchGround(map, cell) && GuSurfaceEcology.FindWildGu(pawn, cell, JobDriver_GuSearchSurface.SearchRadius) == stoneWild);
        pawn.Position = ground;
        pawn.Notify_Teleported();
        int recordsBefore = IndividualRegistry.Records.Count;
        long[] revisions = localities.Select(locality => locality.Cohort.Revision).ToArray();
        long[] debits = localities.Select(locality => locality.Cohort.ReceiptDebits).ToArray();
        var search = new FloatMenuOptionProvider_GuSearchSurface().GetOptions(new FloatMenuContext(new List<Pawn> { pawn },
            ground.ToVector3Shifted(), map)).Single(option => option.Label == "Search for wild Gu");
        var searchJob = DefDatabase<JobDef>.GetNamed("GU_SearchSurface");
        var ecologyTick = AccessTools.Method(typeof(GuSurfaceEcology), nameof(GuSurfaceEcology.GameComponentTick));
        var searchFixture = new Harmony("projectgu.priority0test.stone.search");
        // Isolate the search job from concurrent ambient recruitment and counting on the two loaded sites.
        searchFixture.Patch(ecologyTick, prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(SuppressAmbientInWildlifeControl)));
        try
        {
            search.action();
            TickUntil(() => pawn.CurJobDef == searchJob, 20);
            TickUntil(() => pawn.CurJobDef != searchJob, 400);
        }
        finally { searchFixture.Unpatch(ecologyTick, HarmonyPatchType.Prefix, searchFixture.Id); }
        var highlighted = AccessTools.Field(typeof(GuFxMapComponent), "searchResult").GetValue(map.GetComponent<GuFxMapComponent>());
        Check(ReferenceEquals(highlighted, stoneWild) && stoneWild.Position == position && recordsBefore == IndividualRegistry.Records.Count
            && revisions.SequenceEqual(localities.Select(locality => locality.Cohort.Revision)),
            $"Rocky ground search detects the existing Stone Skin Gu without moving it or touching any cohort. "
            + $"Highlight {highlighted}/{stoneWild}, position {stoneWild.Position}/{position}, records {IndividualRegistry.Records.Count}/{recordsBefore}, "
            + $"cohort revisions [{string.Join(", ", localities.Select(locality => locality.Cohort.Revision))}]/[{string.Join(", ", revisions)}]");

        var stone = AcquireSurfaceGu(pawn, aperture, liquor, StoneSkin, captured =>
        {
            Check(captured.Id == stoneWild.GuId && Wild(BlackSpecies) != null && Wild(WhiteSpecies) != null
                && debits.Take(2).SequenceEqual(localities.Take(2).Select(locality => locality.Cohort.ReceiptDebits)),
                "Capture retires the same Stone Skin resident without changing either Boar population");
        });
        Check(GuAbilityService.GrantsFor(pawn).Any(grant => grant.Gu == stone && grant.Ability == StoneSkin),
            "The refined and stored individual grants the exact Stone Skin technique");
        ExerciseStoneFeeding(pawn, stone);

        foreach (var apparel in pawn.apparel.WornApparel.ToArray()) pawn.apparel.Remove(apparel);
        var baseline = StoneNativeStats(pawn);
        var sharpHits = StoneDamageRolls(pawn, DamageDefOf.Cut);
        var bluntHits = StoneDamageRolls(pawn, DamageDefOf.Blunt);
        var sharpPenetrating = StoneDamageRolls(pawn, DamageDefOf.Cut, 2f);
        var bluntPenetrating = StoneDamageRolls(pawn, DamageDefOf.Blunt, 2f);
        Check(sharpHits.Any(amount => amount > 0f) && bluntHits.Any(amount => amount > 0f),
            $"The unprotected controls take real injuries (sharp {sharpHits.Min():0.##}–{sharpHits.Max():0.##}, blunt {bluntHits.Min():0.##}–{bluntHits.Max():0.##})");
        PrepareStoneEssence(aperture, 12);
        VerifyStoneActivationRollback(pawn, aperture, stone);

        pawn.drafter.Drafted = false;
        var plant = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>()
            .First(plant => plant.Spawned && !plant.Position.Fogged(map) && pawn.CanReach(plant, PathEndMode.Touch, Danger.Some));
        PositionBeside(pawn, plant);
        var work = JobMaker.MakeJob(JobDefOf.CutPlant, plant);
        pawn.jobs.StartJob(work, JobCondition.InterruptForced);
        var before = BodilyEssenceStock(aperture);
        var inactive = AbilityGizmos(pawn).Single(command => command.Ability == StoneSkin);
        Check(!inactive.Disabled && inactive.defaultLabel.Contains("inactive") && inactive.defaultDesc.Contains("250 ticks")
            && inactive.defaultDesc.Contains("Sharp armor +30"), "The inactive command exposes the price, cadence and armor benefits");
        inactive.ProcessInput(new Event());
        var effect = Hediff_GuSustainedEffect.Of(pawn, StoneProtection);
        Check(pawn.CurJob == work && effect is { Visible: true, IsProtecting: true } && effect.PaidUntil == Now + 250
            && BodilyEssenceStock(aperture).SequenceEqual(AfterStonePayment(before, 2)),
            "Undrafted activation keeps native work, shows the temporary hediff and pays exactly once for the first interval");
        Check(!Hediff_GuBodilyAttainment.AllOf(pawn).Any() && !GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out _)
            && BodilyEssenceStock(aperture).SequenceEqual(AfterStonePayment(before, 2)), "A repeated activation cannot pay again or duplicate the effect");
        AssertStoneStats(pawn, baseline, active: true);
        foreach (var (damage, control, penetratingControl) in new[]
            { (DamageDefOf.Cut, sharpHits, sharpPenetrating), (DamageDefOf.Blunt, bluntHits, bluntPenetrating) })
        {
            var protectedHits = StoneDamageRolls(pawn, damage);
            int reduced = protectedHits.Zip(control).Count(pair => pair.First < pair.Second);
            Check(reduced > 0 && protectedHits.Zip(control).All(pair => pair.First <= pair.Second)
                && StoneDamageRolls(pawn, damage, 2f).SequenceEqual(penetratingControl),
                $"Native {damage.defName} hits exercise damage reduction and penetration against the body armor ({reduced} reduced)");
            Report($"Native {damage.defName} damage decreased on {reduced}/{control.Length} matched hits. High-penetration hits were unchanged.");
        }
        Find.TickManager.DoSingleTick();
        Check(pawn.CurJob == work && effect.IsProtecting && map.GetComponent<GuFxMapComponent>().Active.Any(fx => fx.Profile == StoneSkin.sustained.manifestationFx),
            "Native work continues and the optional Stone Skin layer attaches");
        var active = AbilityGizmos(pawn).Single(command => command.Ability == StoneSkin);
        Check(!active.Disabled && active.defaultLabel.Contains("(active)"), "The active command offers deactivation");
        active.ProcessInput(new Event());
        AssertStoneStats(pawn, baseline, active: false);
        Check(!map.GetComponent<GuFxMapComponent>().Active.Any(fx => fx.Profile == StoneSkin.sustained.manifestationFx),
            "Manual deactivation retires its cosmetic layer immediately");
        var toggleStock = BodilyEssenceStock(aperture);
        Check(GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out _)
            && BodilyEssenceStock(aperture).SequenceEqual(AfterStonePayment(toggleStock, 2)),
            "Reactivation in the same tick pays the full activation cost again");
        Hediff_GuSustainedEffect.Of(pawn, StoneProtection).Stop();

        pawn.drafter.Drafted = true;
        TickUntil(() => !pawn.IsHashIntervalTick(250), 2);
        PrepareStoneEssence(aperture, 16);
        Check(GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out string activation), activation ?? "Drafted activation succeeds");
        effect = Hediff_GuSustainedEffect.Of(pawn, StoneProtection);
        var origin = pawn.Position;
        var destination = GenRadial.RadialCellsAround(origin, 5f, false).First(cell => cell.InBounds(map) && cell.DistanceToSquared(origin) >= 9
            && cell.Standable(map) && cell.GetFirstPawn(map) == null && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some));
        pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, destination), JobCondition.InterruptForced);
        TickUntil(() => pawn.Position == destination, 220, "Drafted walking reaches its destination while Stone Skin is active");
        Check(effect.IsProtecting, "The effect did not occupy the movement job");
        var target = SpawnTarget(map, pawn.Position, 5, 8);
        target.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 600), JobCondition.InterruptForced);
        int resolved = Ledger.ResolvedTotal;
        Check(GuAbilityService.TryOrderCast(pawn, state.MoonId, Moonblade, target, out string moonReason), moonReason ?? "Moonblade ordered while protected");
        TickUntil(() => Ledger.ResolvedTotal > resolved, 500, "Moonblade launched and resolved while Stone Skin stayed active");
        Check(effect.IsProtecting, "Targeted Gu casting did not end Stone Skin");
        if (!target.Dead) target.Destroy();

        var wallCell = FindTargetCell(map, pawn.Position, 3, 6, emptyCell: true);
        var wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), wallCell, map);
        int wallHealth = wall.HitPoints;
        PositionBeside(pawn, wall);
        SetEssence(aperture, aperture.MaxEssence);
        var attack = JobMaker.MakeJob(JobDefOf.AttackMelee, wall);
        attack.playerForced = true;
        attack.maxNumMeleeAttacks = 6;
        pawn.jobs.StartJob(attack, JobCondition.InterruptForced);
        TickUntil(() => wall.Destroyed || wall.HitPoints < wallHealth, 1200, "Native melee damaged a stationary target under Stone Skin");
        Check(effect.IsProtecting, "Native combat and defense ran together");
        if (!wall.Destroyed) wall.Destroy();

        // Save mid-interval with both qualities and compare each saved unit on a fresh process.
        effect.Stop();
        TickUntil(() => !pawn.IsHashIntervalTick(250), 2);
        PrepareStoneEssence(aperture, 9, initial: 2.5);
        Check(GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out _), "Started a fresh paid interval for the reload fixture");
        effect = Hediff_GuSustainedEffect.Of(pawn, StoneProtection);
        TickUntil(() => false, 97);
        File.WriteAllText(StoneStatePath, JsonSerializer.Serialize(new StoneState(stone.Id, state.MoonId, pawn.GetUniqueLoadID(), aperture.ApertureId,
            stone.CustodyRevision, Now, effect.PaidUntil, BodilyEssenceStock(aperture), StoneNativeStats(pawn).Take(3).ToArray(), BoarOrigin.Of(stone))));
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("stone");
    }

    private static void VerifyStoneSkinReload()
    {
        DisableFixtureAutosave();
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        var saved = JsonSerializer.Deserialize<StoneState>(File.ReadAllText(StoneStatePath));
        var pawn = Clan(GuClanRole.Disciple).Single(pawn => pawn.GetUniqueLoadID() == saved.PawnId);
        var aperture = Apertures(pawn).Single();
        var stone = IndividualRegistry.Resolve(saved.GuId);
        var effect = Hediff_GuSustainedEffect.Of(pawn, StoneProtection);
        var stock = BodilyEssenceStock(aperture);
        var stats = StoneNativeStats(pawn);
        Check(Now == saved.Tick && effect is { IsProtecting: true, Visible: true } && effect.PaidUntil == saved.PaidUntil
            && effect.GuId == saved.GuId && effect.Ability == StoneSkin && aperture.ApertureId == saved.ApertureId
            && stone.CustodyRevision == saved.Revision && BoarOrigin.Of(stone) == saved.Origin
            && stock.SequenceEqual(saved.Stock) && stock.Count(volume => volume > 0d) == 2 && stats.Take(3).SequenceEqual(saved.ActiveStats.Take(3)),
            $"Fresh-process load restores caster, Gu, aperture, native stats, mixed stock and original deadline. "
            + $"Tick {Now}/{saved.Tick}, active {effect?.IsProtecting}, due {effect?.PaidUntil}/{saved.PaidUntil}, "
            + $"revision {stone?.CustodyRevision}/{saved.Revision}, stock [{string.Join(", ", stock)}], stats [{string.Join(", ", stats)}]");
        Report($"Reload restored armor and exact mixed stock [{string.Join(", ", stock)}] at tick {Now}, with upkeep due at {effect.PaidUntil}.");
        TickUntil(() => Now == saved.PaidUntil - 1, 250);
        var beforeDue = BodilyEssenceStock(aperture);
        Check(effect.IsProtecting && effect.PaidUntil == saved.PaidUntil, "The remaining first interval stays paid until the original deadline");
        Find.TickManager.DoSingleTick();
        Check(effect.IsProtecting && effect.PaidUntil == saved.PaidUntil + 250
            && BodilyEssenceStock(aperture).SequenceEqual(AfterStonePayment(beforeDue, 1)), "One upkeep payment advances the original deadline by exactly 250 ticks");

        var settings = ProjectGuMod.Settings;
        settings.cosmeticEffects = false;
        try
        {
            SetEssence(aperture, 0);
            TickUntil(() => Now == effect.PaidUntil - 1, 250);
            Check(effect.IsProtecting && aperture.Essence < 1f, "Protection lasts through the already-paid final tick with effects off");
            int deadline = effect.PaidUntil;
            Find.TickManager.DoSingleTick();
            Check(Now == deadline && effect.CurStage == null && Hediff_GuSustainedEffect.Of(pawn, StoneProtection) == null,
                "Insufficient essence removes the hediff before native actions on the due tick");
        }
        finally { settings.cosmeticEffects = true; }
        var baseline = StoneNativeStats(pawn);
        Check(!GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out string poor) && poor.Contains("essence")
            && AbilityGizmos(pawn).Single(command => command.Ability == StoneSkin).Disabled, "Too little essence refuses both the command and the commit");
        pawn.drafter.Drafted = false;

        var locality = GuSurfaceEcology.Instance.LocalityAt(pawn.Map, StoneSpecies);
        var second = AcquireSurfaceGu(pawn, aperture, IndividualRegistry.Resolve(saved.MoonId), StoneSkin);
        Check(second.Id != stone.Id && second.CreationEvidence.SourceId == saved.Origin.Source && locality.Cohort.ReceiptDebits >= 2,
            "The next Stone Skin encounter preserves the first identity and debits a second individual");
        PrepareStoneEssence(aperture, 12);
        Check(GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out _), "Activated the first of two stored Stone Skin Gu");
        var after = BodilyEssenceStock(aperture);
        Check(!GuAbilityWorker_Sustained.TryActivate(pawn, second.Id, StoneSkin, out string duplicate) && duplicate.Contains("already active")
            && BodilyEssenceStock(aperture).SequenceEqual(after), "A second stored individual cannot overlap the same protection or take payment");

        Check(IndividualRegistry.TryWithdrawDormant(stone.Id, stone.CustodyRevision, pawn, aperture, out var withdrawn, out _),
            "Withdrew the exact source Gu through native aperture custody");
        Check(Hediff_GuSustainedEffect.Of(pawn, StoneProtection)?.CurStage == null, "Withdrawal immediately invalidates the native armor stage");
        Find.TickManager.DoSingleTick();
        AssertStoneStats(pawn, baseline, active: false, checkMovement: false);
        var recipient = Clan(GuClanRole.Elder).Single();
        TransferStoneFixturePawn(recipient, pawn.Map);
        Check(pawn.inventory.innerContainer.TryDrop(withdrawn, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _), "Dropped the withdrawn Gu for another pawn");
        PositionBeside(recipient, withdrawn);
        Order(recipient, withdrawn, $"Pick up {withdrawn.Label}");
        TickUntil(() => withdrawn.holdingOwner == recipient.inventory.innerContainer, 2400);
        Check(!GuAbilityWorker_Sustained.TryActivate(recipient, stone.Id, StoneSkin, out _)
            && !GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out _), "Native transfer neither preserves nor grants protection");
        withdrawn.Destroy(DestroyMode.Vanish);
        TickUntil(() => !stone.IsLiving, 500, "Native destruction retires the same withdrawn Gu identity");

        Check(GuAbilityWorker_Sustained.TryActivate(pawn, second.Id, StoneSkin, out _), "Activated the second source after the first effect ended");
        var anesthesia = pawn.health.AddHediff(HediffDefOf.Anesthetic);
        Check(pawn.Downed && Hediff_GuSustainedEffect.Of(pawn, StoneProtection)?.CurStage == null, "Native unconsciousness disables armor immediately");
        pawn.health.RemoveHediff(anesthesia);
        Find.TickManager.DoSingleTick();
        Check(!pawn.Downed && Hediff_GuSustainedEffect.Of(pawn, StoneProtection) == null, "Recovery in the same tick does not resume protection");

        PrepareStoneEssence(aperture, 8);
        Check(GuAbilityWorker_Sustained.TryActivate(pawn, second.Id, StoneSkin, out _), "Ready for active-source destruction");
        Drain(second, Snap(second).NourishmentTicks + 10 * GuSurfaceEcology.TicksPerDay);
        Check(Hediff_GuSustainedEffect.Of(pawn, StoneProtection)?.CurStage == null, "A withering or terminal Gu can no longer grant armor");
        TickUntil(() => !second.IsLiving, 3000, "The existing starvation scheduler ends the contained source Gu");
        AssertStoneStats(pawn, baseline, active: false, checkMovement: false);

        var finalGu = AcquireSurfaceGu(pawn, aperture, null, StoneSkin);
        PrepareStoneEssence(aperture, 8);
        Check(GuAbilityWorker_Sustained.TryActivate(pawn, finalGu.Id, StoneSkin, out _), "Ready for native aperture removal");
        pawn.health.RemoveHediff(aperture);
        Check(Hediff_GuSustainedEffect.Of(pawn, StoneProtection)?.CurStage == null, "Loss of the exact aperture immediately disables its effect");
        // Restore the same native aperture before queued reconciliation to prove no same-tick resumption.
        pawn.health.AddHediff(aperture);
        Find.TickManager.DoSingleTick();
        Check(Hediff_GuSustainedEffect.Of(pawn, StoneProtection) == null, "Restoring the aperture does not resume Stone Skin");
        AssertNoErrors("stone-reload");
    }

    private static void VerifyStoneSkinDeath()
    {
        DisableFixtureAutosave();
        var saved = JsonSerializer.Deserialize<StoneState>(File.ReadAllText(StoneStatePath));
        var pawn = Clan(GuClanRole.Disciple).Single(candidate => candidate.GetUniqueLoadID() == saved.PawnId);
        var effect = Hediff_GuSustainedEffect.Of(pawn, StoneProtection);
        Check(Now == saved.Tick && effect is { IsProtecting: true } && effect.GuId == saved.GuId,
            "Fresh process began the death check with the original paid protection");
        pawn.Kill(null);
        Check(pawn.Dead && Hediff_GuSustainedEffect.Of(pawn, StoneProtection)?.CurStage == null, "Native death strips the armor from the corpse pawn");
        Find.TickManager.DoSingleTick();
        Check(ResurrectionUtility.TryResurrect(pawn) && Hediff_GuSustainedEffect.Of(pawn, StoneProtection) == null,
            "Native resurrection restores no temporary protection");
        AssertNoErrors("stone-death");
    }

    private static Map StoneMountainSettlement(BiomeDef forest)
    {
        var tile = Find.WorldGrid.Surface.Tiles.First(tile => tile.hilliness == Hilliness.Mountainous
            && tile.PrimaryBiome == forest && TileFinder.IsValidTileForNewSettlement(tile.tile));
        var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        settlement.Tile = tile.tile;
        settlement.SetFaction(Faction.OfPlayer);
        Find.WorldObjects.Add(settlement);
        GenMapParent(settlement);
        return settlement.Map;
    }

    private static void TransferStoneFixturePawn(Pawn pawn, Map map)
    {
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        pawn.DeSpawn();
        var cell = GenRadial.RadialCellsAround(map.Center, 22f, true).First(cell => cell.Standable(map) && !cell.Fogged(map) && cell.GetFirstPawn(map) == null);
        GenSpawn.Spawn(pawn, cell, map);
    }

    private static void ExerciseStoneFeeding(Pawn pawn, GuRecord stone)
    {
        var feeding = stone.Species.SimulatedFeeding;
        string[] names = { "ChunkSandstone", "ChunkGranite", "ChunkLimestone", "ChunkSlate", "ChunkMarble" };
        Check(feeding.MaxTicks == 5 * GuSurfaceEcology.TicksPerDay && feeding.foods.Select(food => food.thingDef.defName).ToHashSet().SetEquals(names)
            && feeding.foods.All(food => food.mealCount == 1 && food.daysPerMeal == 5), "Only five vanilla natural chunk Defs are one-item, five-day meals");
        foreach (string defName in new[] { "ChunkSlagSteel", "BlocksGranite", "Granite", "Wall" })
            Check(feeding.Worker.OptionFor(ThingDef.Named(defName)) == null, $"The food worker rejects {defName}");
        SustenanceComponent.SetAutoTend(pawn, false);
        foreach (var food in feeding.foods)
            foreach (var chunk in pawn.Map.listerThings.ThingsOfDef(food.thingDef).ToArray()) chunk.Destroy();
        Drain(stone, Snap(stone).NourishmentTicks);
        var manual = SpawnItem(pawn.Map, ThingDef.Named("ChunkGranite"), 1, pawn.Position, 3);
        Check(GuSustenance.TryOrderFeed(pawn, stone, out string reason), reason ?? "Ordered a manual chunk meal");
        TickUntil(() => stone.GetState<GuNourishment>().MealsEaten == 1, 2200, "The native manual feeding job consumes one chunk");
        Check(manual.Destroyed && Snap(stone).NourishmentTicks > 4 * GuSurfaceEcology.TicksPerDay, "Manual provisioning consumed actual granite and reset nourishment");
        Drain(stone, Snap(stone).NourishmentTicks);
        var automatic = ThingMaker.MakeThing(ThingDef.Named("ChunkMarble"));
        Check(pawn.inventory.innerContainer.TryAdd(automatic, false), "Placed one alternate eligible chunk in the cultivator's own inventory");
        SustenanceComponent.SetAutoTend(pawn, true);
        TickUntil(() => automatic.Destroyed, 3000, "The live sustenance scheduler provisions one natural chunk automatically");
        var state = stone.GetState<GuNourishment>();
        Check(state.AutomaticMeals == 1 && state.MealsEaten == 2 && state.ItemsEaten == 2 && Snap(stone).NourishmentTicks > 4 * GuSurfaceEcology.TicksPerDay,
            "Automatic provisioning spends exactly one marble chunk for five days of nourishment");
        SustenanceComponent.SetAutoTend(pawn, false);
    }

    private static void PrepareStoneEssence(Hediff_Aperture aperture, float amount, double initial = 1.5)
    {
        SetEssence(aperture, 0);
        SetEssence(aperture, amount);
        Check(aperture.TryUpgradeEssence(RankOneStage.Initial, amount - initial), "Prepared mixed-quality essence stock");
    }

    private static double[] AfterStonePayment(double[] stock, double amount)
    {
        var remaining = stock.ToArray();
        for (int index = 0; index < remaining.Length; index++)
        {
            double paid = Math.Min(remaining[index], amount);
            remaining[index] = Math.Round(remaining[index] - paid, 9);
            amount -= paid;
        }
        return remaining;
    }

    private static void VerifyStoneActivationRollback(Pawn pawn, Hediff_Aperture aperture, GuRecord stone)
    {
        var before = BodilyEssenceStock(aperture);
        int commits = Ledger.CommittedTotal;
        var fault = new Harmony("projectgu.priority0test.stone.rollback");
        var add = AccessTools.DeclaredMethod(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });
        fault.Patch(add, postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ThrowAfterStoneHealthMutation)));
        try { Check(!GuAbilityWorker_Sustained.TryActivate(pawn, stone.Id, StoneSkin, out _), "Injected a native health callback failure after insertion"); }
        finally { fault.Unpatch(add, HarmonyPatchType.Postfix, fault.Id); }
        Check(BodilyEssenceStock(aperture).SequenceEqual(before) && Hediff_GuSustainedEffect.Of(pawn, StoneProtection) == null
            && Ledger.CommittedTotal == commits, "Interrupted activation rolls back the condition, every quality of essence and the combat commitment");
    }

    private static void ThrowAfterStoneHealthMutation(Hediff hediff)
    {
        if (hediff is Hediff_GuSustainedEffect) throw new InvalidOperationException("Injected Stone Skin health callback failure");
    }

    private static float[] StoneNativeStats(Pawn pawn) =>
        new[] { StatDefOf.ArmorRating_Sharp, StatDefOf.ArmorRating_Blunt, StatDefOf.ArmorRating_Heat, StatDefOf.MoveSpeed }
            .Select(stat => pawn.GetStatValue(stat)).ToArray();

    private static void AssertStoneStats(Pawn pawn, float[] baseline, bool active, bool checkMovement = true)
    {
        var stats = StoneNativeStats(pawn);
        Check(Math.Abs(stats[0] - baseline[0] - (active ? 0.30f : 0f)) < 0.0001f
            && Math.Abs(stats[1] - baseline[1] - (active ? 0.25f : 0f)) < 0.0001f
            && stats[2] == baseline[2] && (!checkMovement || Math.Abs(stats[3] - baseline[3]) < 0.001f)
            && (Hediff_GuSustainedEffect.Of(pawn, StoneProtection) != null) == active,
            $"Native armor, heat and movement match the {(active ? "active" : "inactive")} state ({string.Join(", ", stats)})");
    }

    private static float[] StoneDamageRolls(Pawn pawn, DamageDef damage, float penetration = 0)
    {
        var torso = pawn.health.hediffSet.GetNotMissingParts().First(part => part.def == BodyPartDefOf.Torso);
        return Enumerable.Range(100, 40).Select(seed =>
        {
            Rand.PushState(seed);
            try
            {
                var info = new DamageInfo(damage, 8f, penetration, hitPart: torso, spawnFilth: false,
                    checkForJobOverride: false, preventCascade: true);
                info.SetAllowDamagePropagation(false);
                var result = pawn.TakeDamage(info);
                if (result.hediffs != null)
                    foreach (var injury in result.hediffs) pawn.health.RemoveHediff(injury);
                return result.totalDamageDealt;
            }
            finally { Rand.PopState(); }
        }).ToArray();
    }
}
