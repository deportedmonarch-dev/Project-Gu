using HarmonyLib;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Domain;
using ProjectGu.Springs;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.Profile;

/// <summary>
/// M1 native acceptance for the Southern Border clan start. The stock -quicktest path hardcodes Crashlanded; with
/// -guclanstart the same setup sequence runs with the clan ScenarioDef's own Scenario instance, exactly as the
/// scenario selection page does.
/// </summary>
[StaticConstructorOnStartup]
internal static class ClanQuickStart
{
    internal static bool Enabled => GenCommandLine.CommandLineArgPassed("guclanstart");
    internal static ScenarioDef Def => DefDatabase<ScenarioDef>.GetNamed("GU_SouthernBorderClan");

    static ClanQuickStart()
    {
        if (!Enabled) return;
        new Harmony("projectgu.priority0test.clanstart").Patch(AccessTools.Method(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay)),
            prefix: new HarmonyMethod(typeof(ClanQuickStart), nameof(Prefix)));
        if (GenCommandLine.CommandLineArgPassed("guclansession"))
            new Harmony("projectgu.priority0test.clansession").Patch(AccessTools.Method(typeof(Root_Entry), nameof(Root_Entry.Start)),
                postfix: new HarmonyMethod(typeof(ClanQuickStart), nameof(EntryStarted)));
    }

    private static bool Prefix()
    {
        Setup(mountainous: false);
        return false;
    }

    /// <summary>Root_Play.SetupForQuickTestPlay with the clan scenario and an optional mountainous tile.</summary>
    internal static void Setup(bool mountainous)
    {
        Current.ProgramState = ProgramState.Entry;
        Game.ClearCaches();
        Current.Game = new Game();
        Current.Game.InitData = new GameInitData();
        Current.Game.Scenario = Def.scenario;
        Find.Scenario.PreConfigure();
        Current.Game.storyteller = new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
        Current.Game.World = WorldGenerator.GenerateWorld(0.3f, GenText.RandomSeedString(), OverallRainfall.Normal,
            OverallTemperature.Normal, OverallPopulation.Normal, LandmarkDensity.Normal);
        Find.GameInitData.ChooseRandomStartingTile();
        if (mountainous)
        {
            var tiles = Find.WorldGrid.Surface.Tiles.Where(tile => tile.hilliness == Hilliness.Mountainous
                && TileFinder.IsValidTileForNewSettlement(tile.tile)).Select(tile => tile.tile).ToList();
            if (tiles.Count > 0) Find.GameInitData.startingTile = tiles.RandomElement();
            else Log.Warning("[Project GU test] No mountainous settlement tile; using the random tile.");
        }
        Find.GameInitData.mapSize = 250;
        Find.Scenario.PostIdeoChosen();
    }

    private static bool secondGamePending;

    /// <summary>
    /// A player's second game in one session: quit to the main menu (stock GenScene.GoToMainMenu), then in the Entry
    /// scene configure the same ScenarioDef as the setup pages do and start through stock PageUtility.InitGameStart,
    /// which loads the Play scene where Root_Play.Start runs InitNewGame.
    /// </summary>
    internal static void StartAnotherGame()
    {
        secondGamePending = true;
        GenScene.GoToMainMenu();
    }

    private static void EntryStarted()
    {
        if (!secondGamePending) return;
        secondGamePending = false;
        LongEventHandler.QueueLongEvent(() => Setup(mountainous: true), "GeneratingWorld", doAsynchronously: true, null);
        LongEventHandler.QueueLongEvent(PageUtility.InitGameStart, "LoadingLongEvent", doAsynchronously: false, null);
    }
}

public sealed partial class LiveLifecycleChecks
{
    private static bool ClanStart => ClanQuickStart.Enabled;
    private static bool ClanSession => GenCommandLine.CommandLineArgPassed("guclansession");
    private const string PrefoundingSave = "clan-prefounding";
    private const string FoundedLetter = "Clan founded";
    private static int clanGamesStarted;

    private static ScenPart_GuClanStart ClanPart => Find.Scenario.AllParts.OfType<ScenPart_GuClanStart>().Single();
    private static bool FoundingDone => (bool)AccessTools.Field(typeof(ScenPart_GuClanStart), "foundingDone").GetValue(ClanPart);
    private static bool FoundingPending => (bool)AccessTools.Field(typeof(ScenPart_GuClanStart), "foundingPending").GetValue(ClanPart);
    private static GuWorldComponent Cultivators => Current.Game.GetComponent<GuWorldComponent>();
    private static Map Home => Find.Maps.Single(map => map.IsPlayerHome && map.Parent is Settlement);
    private static List<Pawn> Clan(GuClanRole role) => PawnsFinder.AllMaps_FreeColonistsSpawned.Where(p => GuClanRoles.RoleOf(p) == role).ToList();
    private static List<Hediff_Aperture> Apertures(Pawn pawn) => pawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>().ToList();
    private static int FoundedLetters => Find.LetterStack.LettersListForReading.Count(letter => letter.Label.ToString() == FoundedLetter);
    private static List<Building_GuPocketProbeEndpoint> Pits(Map map) =>
        map.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.endpoint).OfType<Building_GuPocketProbeEndpoint>().ToList();
    private static string ClanStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "clan-state.txt");
    private static string ClanPlanPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "clan-plan.txt");
    private static Dictionary<string, int> PlannedAptitudes =>
        (Dictionary<string, int>)AccessTools.Field(typeof(ScenPart_GuClanStart), "plannedAptitudes").GetValue(ClanPart);
    private static Pawn firstSessionElder;
    private static string FoundingState => $"pending={FoundingPending} done={FoundingDone} letters={FoundedLetters} tick={Find.TickManager.TicksGame} "
        + $"elderApertures={Clan(GuClanRole.Elder).Sum(p => Apertures(p).Count)} registryReady={Cultivators?.CanSaveCultivators}";

    // Runs inside Game.InitNewGame after Scenario.PostGameStart and before the first tick. Pausing here keeps the
    // fixture's pre-founding observations and save independent of how many frames pass before the test starts.
    private static void NoteClanLoadedGame()
    {
        Find.TickManager.Pause();
        Report($"LoadedGame: {FoundingState}");
    }

    private static void NoteClanStartedNewGame()
    {
        Find.TickManager.Pause();
        Report($"StartedNewGame: {FoundingState}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Stage "new": generated start, founding, ordinary travel, awakening, hauling, trading, obstructed placement.
    private static void ExerciseClanStart()
    {
        Map home = Home;
        Check(ReferenceEquals(Find.Scenario, ClanQuickStart.Def.scenario), "The game runs the clan ScenarioDef's Scenario instance");
        var colonists = home.mapPawns.FreeColonistsSpawned.ToList();
        Check(colonists.Count == 6, $"Six starting colonists, found {colonists.Count}");
        Pawn elder = Clan(GuClanRole.Elder).Single();
        var disciples = Clan(GuClanRole.Disciple);
        var workers = Clan(GuClanRole.MortalWorker);
        Check(disciples.Count == 2 && workers.Count == 3, $"Roles: 1 elder, {disciples.Count} disciples, {workers.Count} workers");
        Check(colonists.All(p => p.ageTracker.AgeBiologicalYearsFloat <= elder.ageTracker.AgeBiologicalYearsFloat), "The elder is the oldest colonist");
        Check(disciples.All(d => d.ageTracker.AgeBiologicalYears >= 13 && workers.All(w => w.ageTracker.AgeBiologicalYears < 13
            || w.ageTracker.AgeBiologicalYearsFloat >= d.ageTracker.AgeBiologicalYearsFloat)), "Disciples are the youngest colonists aged 13 or older");
        Check(FoundingPending && !FoundingDone && Apertures(elder).Count == 0 && FoundedLetters == 0,
            $"Founding waits for the cultivator registry after PostGameStart ({FoundingState})");

        var pit = Pits(home).Single();
        Check(GuPocketProbeComponent.TryGetIndependentEndpoint(pit, out var site, out var surfaceEnd) && surfaceEnd.IsSurface,
            "The spring pit is a registered independent spring route");
        float distance = colonists.Min(p => p.Position.DistanceTo(pit.Position));
        Check(distance >= 7f && distance <= 60f && !pit.Position.Roofed(home), $"The pit is unroofed and near the colonists ({distance:0.0} cells)");
        int stones = PhysicalStoneCount();
        Check(stones == 60, $"The clan starts with 60 primeval stones, found {stones}");
        Report($"Start: elder {Describe(elder)}; disciples {string.Join(", ", disciples.Select(Describe))}; workers {string.Join(", ", workers.Select(Describe))}; pit {distance:0.0} cells from the nearest colonist at {pit.Position}; biome {home.Biome.defName}, hilliness {home.TileInfo.hilliness}.");

        var plan = AssertPlanned(colonists);
        if (!Alpha1 && !Alpha2 && !Alpha3 && !RankOne && !GuGeneration && !Slice0 && !Slice0Wildlife)
        {
            File.WriteAllLines(ClanPlanPath, plan.Select(p => $"{p.Key}={p.Value}"));
            GameDataSaveLoader.SaveGame(PrefoundingSave);
        }
        TickUntil(() => FoundingDone, 600, "Founding completed within ten seconds of play");
        var elderRecord = AssertFounded(elder, disciples, workers);
        AssertPlanApplied(plan, elder, disciples, workers);
        var elderAperture = Apertures(elder).Single();

        // Ordinary play: no Development Mode from here on.
        Prefs.DevMode = false;
        Pawn disciple = disciples.First();
        var discipleRecord = Cultivators.GetOrCreate(disciple);
        Check(discipleRecord.State == AwakeningState.Unattempted, "The disciple has not attempted awakening");
        int ordered = Find.TickManager.TicksGame;
        Order(disciple, pit, "Enter underground level");
        TickUntil(() => disciple.Map != home, 6000, "The disciple walked to the pit and descended without Development Mode");
        int ticks = Find.TickManager.TicksGame - ordered;
        Map cave = disciple.Map;
        Check(GuPocketSiteDef.IsSpringCavern(cave) && site.Levels.Single().LiveMap == cave, "The first descent generated the spring cavern");
        var springThing = cave.listerThings.AllThings.Single(thing => thing.TryGetComp<CompGuSpiritSpring>() != null);
        var spring = springThing.TryGetComp<CompGuSpiritSpring>();
        int mature = spring.HopeGuAvailable, juvenile = spring.HopeGuJuvenile;
        Check(mature == 3, $"The new cavern holds 3 mature Hope Gu, found {mature}");
        if (Alpha1) VerifyAlpha1SpringArrival(disciple, springThing, spring, site, surfaceEnd);
        Check(!springThing.GetFloatMenuOptions(elder).Any(o => o.Label.Contains("awakening")) || elder.Map != cave,
            "The awakened elder is not offered another awakening");
        Order(disciple, springThing, "Attempt awakening at spirit spring");
        TickUntil(() => discipleRecord.State != AwakeningState.Unattempted, 4000, "The disciple's awakening ceremony completed");
        Check(discipleRecord.State == AwakeningState.Awakened && Apertures(disciple).Count == 1, "The gifted disciple opened exactly one aperture");
        Check(spring.HopeGuAvailable == mature - 1 && spring.HopeGuJuvenile == juvenile, $"Awakening consumed exactly one mature Hope Gu ({mature}+{juvenile} -> {spring.HopeGuAvailable}+{spring.HopeGuJuvenile})");
        Check(!disciple.health.hediffSet.hediffs.Any(h => h.def.defName == "GU_MortalUnawakened") && GuClanRoles.RoleOf(disciple) == GuClanRole.Disciple,
            "The disciple keeps the clan role and loses the unawakened marker");
        var discipleAperture = Apertures(disciple).Single();
        Check(discipleAperture.Aptitude == plan[disciple.ThingID],
            $"The disciple's aperture used the aptitude planned at game creation ({plan[disciple.ThingID]})");
        Report($"Ordinary travel and awakening: {disciple.LabelShort} descended in {ticks} ticks and opened a {discipleAperture.AptitudeGrade}-grade aperture ({discipleAperture.Aptitude}). Hope Gu {mature}+{juvenile} -> {spring.HopeGuAvailable}+{spring.HopeGuJuvenile}.");

        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;
        if (GuGeneration)
        {
            ExerciseGuGeneration(disciple, spring);
            return;
        }
        if (Slice0Wildlife)
        {
            ExerciseSlice0Wildlife(disciple);
            return;
        }
        if (Slice0)
        {
            ExerciseSlice0(disciple, spring);
            return;
        }
        if (RankOne)
        {
            ExerciseRankOne(elder, elderAperture);
            return;
        }
        if (Alpha1)
        {
            ExerciseAlpha1(disciple, discipleRecord, discipleAperture, springThing, spring, exit);
            return;
        }
        if (Alpha2)
        {
            ExerciseAlpha2(disciple, discipleRecord, discipleAperture, springThing, spring);
            return;
        }
        if (Alpha3)
        {
            ExerciseAlpha3(disciple, discipleRecord, discipleAperture, springThing, spring);
            return;
        }
        Order(disciple, exit, "Use underground passage");
        TickUntil(() => disciple.Map == home, 3000, "The disciple climbed back to the surface");

        // A day of simulated hauling must not depend on what the storyteller sends. One run lost the elder
        // (the trade negotiator) to an unlogged event during that day. Fixture-fired incidents still run.
        DebugSettings.enableStoryteller = false;
        try
        {
            ExerciseClanHauling(home, site, pit, cave, spring, workers);
            Check(!elder.Dead && elder.Map == home, "The elder survived the simulated day and can negotiate");
            ExerciseClanTrade(home, elder);
        }
        finally { DebugSettings.enableStoryteller = true; }
        ExerciseObstructedPit(home);

        File.WriteAllLines(ClanStatePath, new[]
        {
            $"elder={elder.ThingID}", $"elderRevision={elderRecord.StateRevision}", $"elderAperture={elderAperture.ApertureId}",
            $"disciple={disciple.ThingID}", $"discipleAperture={discipleAperture.ApertureId}"
        });
        AssertNoErrors("new");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    // Every founder's aptitude is rolled once in PostGameStart and kept on the scenario part until founding.
    private static Dictionary<string, int> AssertPlanned(List<Pawn> colonists)
    {
        var plan = new Dictionary<string, int>(PlannedAptitudes);
        Check(plan.Count == colonists.Count && colonists.All(p => plan.ContainsKey(p.ThingID)), $"Every founder has a planned aptitude ({plan.Count})");
        Pawn elder = Clan(GuClanRole.Elder).Single();
        Check(plan[elder.ThingID] is >= 60 and <= 74, $"The elder's planned aptitude is B-grade ({plan[elder.ThingID]})");
        Check(Clan(GuClanRole.Disciple).All(d => plan[d.ThingID] is >= 20 and <= 99), "Each disciple's planned aptitude is ordinary and nonzero");
        Report("Planned aptitudes: " + string.Join(", ", colonists.Select(p => $"{p.LabelShort} {GuClanRoles.RoleOf(p)} {plan[p.ThingID]}")) + ".");
        return plan;
    }

    private static void AssertPlanApplied(Dictionary<string, int> plan, Pawn elder, List<Pawn> disciples, List<Pawn> workers)
    {
        int aptitude = Apertures(elder).Single().Aptitude;
        Check(aptitude == plan[elder.ThingID], $"The elder awakened with the planned aptitude ({plan[elder.ThingID]}, got {aptitude})");
        foreach (Pawn founder in disciples.Concat(workers))
        {
            var r = Cultivators.GetOrCreate(founder);
            Check(r.State == AwakeningState.Unattempted && r.LatentAptitude == plan[founder.ThingID],
                $"{founder.LabelShort} keeps the planned hidden aptitude ({plan[founder.ThingID]}, record {r.LatentAptitude})");
        }
        Check(PlannedAptitudes.Count == 0, "Founding handed the planned aptitudes to the records and cleared them");
    }

    private static GuCultivatorRecord AssertFounded(Pawn elder, List<Pawn> disciples, List<Pawn> workers)
    {
        Check(FoundedLetters == 1, $"Exactly one founding letter, found {FoundedLetters}");
        var record = Cultivators.GetOrCreate(elder);
        var apertures = Apertures(elder);
        Check(record.State == AwakeningState.Awakened && apertures.Count == 1 && record.ApertureIds.SequenceEqual(new[] { apertures[0].ApertureId }),
            "The elder has one awakened record bound to one aperture");
        Check(apertures[0].Aptitude is >= 60 and <= 74 && apertures[0].AptitudeGrade == "B" && apertures[0].Rank == 1,
            $"The elder aperture is B-grade Rank 1 ({apertures[0].Aptitude})");
        Check(!elder.health.hediffSet.hediffs.Any(h => h.def.defName is "GU_MortalUnawakened" or "GU_MortalNoAptitude"), "The elder carries no mortal marker");
        foreach (Pawn disciple in disciples)
        {
            var r = Cultivators.GetOrCreate(disciple);
            Check(r.State == AwakeningState.Unattempted && r.LatentAptitude is >= 20 and <= 99 && Apertures(disciple).Count == 0
                && disciple.health.hediffSet.hediffs.Any(h => h.def.defName == "GU_MortalUnawakened"),
                $"Disciple {disciple.LabelShort} is unawakened with hidden ordinary aptitude");
        }
        foreach (Pawn worker in workers)
        {
            var r = Cultivators.GetOrCreate(worker);
            Check(r.State == AwakeningState.Unattempted && Apertures(worker).Count == 0, $"Worker {worker.LabelShort} is an unawakened mortal");
        }
        Report($"Founding: elder {elder.LabelShort} awakened at {Apertures(elder)[0].AptitudeGrade}-grade ({Apertures(elder)[0].Aptitude}), essence {Apertures(elder)[0].Essence:0.0}/{Apertures(elder)[0].MaxEssence:0}; one letter.");
        return record;
    }

    private static void ExerciseClanHauling(Map home, GuIndependentSite site, Building_GuPocketProbeEndpoint pit, Map cave,
        CompGuSpiritSpring spring, List<Pawn> workers)
    {
        Pawn hauler = workers.First(p => !p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
        IntVec3 cell = EmptyCargoCell(home, pit.Position, hauler, 4);
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, home.zoneManager);
        home.zoneManager.RegisterZone(stockpile);
        stockpile.AddCell(cell);
        stockpile.GetStoreSettings().filter.SetDisallowAll();
        stockpile.GetStoreSettings().filter.SetAllow(StoneDef, true);
        Order(hauler, pit, AssignHaulerLabel, job: false);
        Check(site.Hauling?.Worker == hauler && site.Hauling.Enabled, "A mortal worker was assigned to underground hauling without Development Mode");

        int before = PhysicalStoneCount();
        int reserve = spring.Reserve;
        int start = Find.TickManager.TicksGame;
        TickUntil(() => cave.listerThings.ThingsOfDef(StoneDef).Any() || site.Hauling.Cargo != null, 64000, "The spring produced stones within a day");
        int produced = reserve - spring.Reserve;
        Check(produced > 0 && PhysicalStoneCount() == before + produced, $"Production added {produced} stone(s) and conserved the total");
        TickUntil(() => cell.GetThingList(home).Any(t => t.def == StoneDef), 12000, "The hauler delivered spring stones to the surface stockpile");
        int delivered = cell.GetThingList(home).Where(t => t.def == StoneDef).Sum(t => t.stackCount);
        Check(PhysicalStoneCount() == before + produced, "Hauling conserved every stone");
        Report($"Hauling: {hauler.LabelShort} delivered {delivered} stone(s) to the surface {(Find.TickManager.TicksGame - start) / 2500f:0.0} in-game hours after assignment; spring produced {produced}; total stones {PhysicalStoneCount()}.");
    }

    private static void ExerciseClanTrade(Map home, Pawn negotiator)
    {
        foreach (Thing stone in home.listerThings.ThingsOfDef(StoneDef)) home.areaManager.Home[stone.Position] = true;
        Pawn trader = null;
        for (int attempt = 0; attempt < 6 && trader == null; attempt++)
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, home);
            parms.target = home;
            IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms);
            trader = home.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.trader?.traderKind != null && p.Faction != Faction.OfPlayer);
        }
        Check(trader != null, "A trader caravan arrived");
        TickUntil(() => trader.trader.CanTradeNow, 9000, "The caravan trader became available to trade");
        Check(trader.trader.traderKind.WillTrade(StoneDef), $"Trader kind {trader.trader.traderKind.defName} buys primeval stones");

        int total = PhysicalStoneCount();
        int colonyStones = ColonyStones(home);
        int silver = ColonySilver(home);
        TradeSession.SetupWith(trader, negotiator, false);
        try
        {
            var tradeable = TradeSession.deal.AllTradeables.FirstOrDefault(t => t.ThingDef == StoneDef && t.CountHeldBy(Transactor.Colony) >= 20);
            Check(tradeable != null && tradeable.TraderWillTrade, "Primeval stones appear as a sellable trade line");
            Check(tradeable.CanAdjustTo(-20).Accepted, "The player can offer 20 stones");
            tradeable.AdjustTo(-20);
            Check(tradeable.ActionToDo == TradeAction.PlayerSells, "Outside gift mode a negative transfer is a sale");
            float price = tradeable.GetPriceFor(TradeAction.PlayerSells);
            Check(TradeSession.deal.TryExecute(out bool traded) && traded, "The trade executed");
            int gained = ColonySilver(home) - silver;
            Check(ColonyStones(home) == colonyStones - 20 && PhysicalStoneCount() == total && gained > 0,
                $"The sale moved 20 stones to the trader and paid {gained} silver");
            Report($"Trading: sold 20 primeval stones to {trader.trader.traderKind.defName} at {price:0.00} silver each for {gained} silver. Stones conserved ({total}).");
        }
        finally { TradeSession.Close(); }
    }

    private static void ExerciseObstructedPit(Map home)
    {
        var roofed = home.AllCells.Where(c => !c.Roofed(home)).ToList();
        foreach (IntVec3 c in roofed) home.roofGrid.SetRoof(c, RoofDefOf.RoofRockThin);
        int pits = Pits(home).Count;
        int errors = ErrorCount();
        try
        {
            AccessTools.Method(typeof(ScenPart_GuClanStart), "TryPlaceSpringEntrance").Invoke(ClanPart, new object[] { home });
            Check(Pits(home).Count == pits && ErrorCount() == errors, "An obstructed start places no pit and logs no error");
        }
        finally { foreach (IntVec3 c in roofed) home.roofGrid.SetRoof(c, null); }
        Report($"Obstructed start: with every open cell on the map roofed, no pit was placed; the player gets a warning message and the Development Mode tool remains.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Stage "reload": founding must not repeat after a fresh-process load.
    private static void VerifyClanStartReload()
    {
        var state = File.ReadAllLines(ClanStatePath).Select(line => line.Split('=')).ToDictionary(p => p[0], p => p[1]);
        Check(FoundingDone && !FoundingPending, "The saved scenario restored completed founding");
        Pawn elder = Clan(GuClanRole.Elder).Single();
        Check(elder.ThingID == state["elder"] && FoundedLetters == 1, "The same elder and one founding letter after reload");
        var record = Cultivators.GetOrCreate(elder);
        TickUntil(() => false, 900);
        Check(FoundedLetters == 1 && Apertures(elder).Count == 1 && Apertures(elder)[0].ApertureId == state["elderAperture"]
            && record.StateRevision.ToString() == state["elderRevision"], "Fifteen seconds of play after reload did not repeat founding or touch the elder");
        Pawn disciple = PawnsFinder.AllMaps_FreeColonistsSpawned.Single(p => p.ThingID == state["disciple"]);
        Check(Apertures(disciple).Single().ApertureId == state["discipleAperture"] && Cultivators.GetOrCreate(disciple).State == AwakeningState.Awakened,
            "The awakened disciple kept the same aperture");
        Check(Clan(GuClanRole.Disciple).Count == 2 && Clan(GuClanRole.MortalWorker).Count == 3, "Roles survived reload");
        AssertNoErrors("reload");
        Report("Reload: founding stayed complete; one letter, the elder's aperture and revision and the disciple's aperture were unchanged after 900 ticks.");
    }

    // Stage "prefounding": a save written before the first tick founds exactly once after loading.
    private static void VerifyClanPrefounding()
    {
        Check(FoundingPending && !FoundingDone && FoundedLetters == 0, $"The pre-founding save restored pending founding ({FoundingState})");
        var saved = File.ReadAllLines(ClanPlanPath).Select(line => line.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1]));
        Check(PlannedAptitudes.Count == saved.Count && saved.All(p => PlannedAptitudes.TryGetValue(p.Key, out int v) && v == p.Value),
            "The pre-founding save restored every planned aptitude unchanged");
        Pawn elder = Clan(GuClanRole.Elder).Single();
        Check(Apertures(elder).Count == 0 && Pits(Home).Count == 1, "Before play the elder is unawakened and one pit exists");
        TickUntil(() => FoundingDone, 600, "Founding completed after loading the pre-founding save");
        AssertFounded(elder, Clan(GuClanRole.Disciple), Clan(GuClanRole.MortalWorker));
        AssertPlanApplied(saved, elder, Clan(GuClanRole.Disciple), Clan(GuClanRole.MortalWorker));
        TickUntil(() => false, 600);
        Check(FoundedLetters == 1 && Apertures(elder).Count == 1 && Pits(Home).Count == 1, "Founding ran once; no extra pit or aperture");
        AssertNoErrors("prefounding");
        Report($"Pre-founding save: founding ran exactly once after load; the elder awakened at {Apertures(elder)[0].AptitudeGrade}-grade "
            + $"({Apertures(elder)[0].Aptitude}), matching the value planned at game creation, and every founder kept the planned aptitude.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Unit "session": two Southern Border games in one process. The second uses a mountainous tile.
    private static void StartClanSessionGame()
    {
        clanGamesStarted++;
        if (clanGamesStarted == 1)
        {
            try
            {
                Find.TickManager.Pause();
                Check(FoundingPending && !FoundingDone, $"First game: founding pending ({FoundingState})");
                var firstPlan = AssertPlanned(Home.mapPawns.FreeColonistsSpawned.ToList());
                firstSessionElder = Clan(GuClanRole.Elder).Single();
                TickUntil(() => FoundingDone, 600, "First game: founding completed");
                AssertPlanApplied(firstPlan, firstSessionElder, Clan(GuClanRole.Disciple), Clan(GuClanRole.MortalWorker));
                Check(FoundedLetters == 1 && Apertures(Clan(GuClanRole.Elder).Single()).Count == 1, "First game: one letter and one elder aperture");
                Report($"Session game 1: founded on {Home.Biome.defName}/{Home.TileInfo.hilliness}; ScenPart instance {RuntimeHelpers(ClanPart)}.");
                ClanQuickStart.StartAnotherGame();
            }
            catch (Exception error)
            {
                Report($"FAIL session {error}");
                UnityEngine.Application.Quit();
            }
            return;
        }
        Run(VerifySecondClanGame, "session");
    }

    private static string RuntimeHelpers(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value).ToString("x8");

    private static void VerifySecondClanGame()
    {
        Check(clanGamesStarted == 2 && ReferenceEquals(Find.Scenario, ClanQuickStart.Def.scenario), "Second game reuses the ScenarioDef's Scenario instance");
        Check(FoundingPending && !FoundingDone && FoundedLetters == 0, "Second game: founding was reset and is pending, not leaked from game 1");
        Map home = Home;
        Check(home.mapPawns.FreeColonistsSpawned.Count == 6 && Clan(GuClanRole.Elder).Count == 1 && Clan(GuClanRole.Disciple).Count == 2,
            "Second game: six colonists with fresh roles");
        var secondPlan = AssertPlanned(home.mapPawns.FreeColonistsSpawned.ToList());
        Check(firstSessionElder != null && !home.mapPawns.FreeColonistsSpawned.Contains(firstSessionElder),
            "Second game: the founders are new pawns, not game 1's");
        TickUntil(() => FoundingDone, 600, "Second game: founding completed");
        Pawn elder = Clan(GuClanRole.Elder).Single();
        AssertFounded(elder, Clan(GuClanRole.Disciple), Clan(GuClanRole.MortalWorker));
        AssertPlanApplied(secondPlan, elder, Clan(GuClanRole.Disciple), Clan(GuClanRole.MortalWorker));
        int pits = Pits(home).Count;
        string pitResult = pits == 1
            ? $"pit placed {home.mapPawns.FreeColonistsSpawned.Min(p => p.Position.DistanceTo(Pits(home)[0].Position)):0.0} cells from the colonists"
            : "no pit placed";
        Check(pits <= 1, "At most one pit in the second game");
        AssertNoErrors("session");
        Report($"Session game 2: {home.Biome.defName}/{home.TileInfo.hilliness}; founding ran again with one letter and one elder aperture; {pitResult}; ScenPart instance {RuntimeHelpers(ClanPart)}.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    private static void Order(Pawn pawn, Thing target, string label, bool job = true)
    {
        var options = target.GetFloatMenuOptions(pawn).ToList();
        var option = options.FirstOrDefault(o => o.Label == label);
        Check(option?.action != null, $"'{label}' is offered to {pawn.LabelShort} on {target.LabelShort} without Development Mode. Offered: {string.Join(" | ", options.Select(o => o.Label))}");
        option.action();
        if (job) Check(pawn.CurJob != null, $"'{label}' started a job");
    }

    private static string Describe(Pawn pawn) => $"{pawn.LabelShort} ({pawn.ageTracker.AgeBiologicalYears})";

    private static int ColonyStones(Map home) => home.listerThings.ThingsOfDef(StoneDef).Sum(t => t.stackCount)
        + home.mapPawns.FreeColonistsSpawned.Sum(p => p.inventory.GetDirectlyHeldThings().Where(t => t.def == StoneDef).Sum(t => t.stackCount));

    private static int ColonySilver(Map home) => home.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t => t.stackCount)
        + home.mapPawns.FreeColonistsSpawned.Sum(p => p.inventory.GetDirectlyHeldThings().Where(t => t.def == ThingDefOf.Silver).Sum(t => t.stackCount));

    private static int ErrorCount() => Log.Messages.Count(m => m.type == LogMessageType.Error);

    private static void AssertNoErrors(string stage)
    {
        var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToList();
        File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, $"clan-errors-{stage}.txt"), errors);
        Check(errors.Count == 0, $"{errors.Count} red error(s) logged. First: {errors.FirstOrDefault()}");
    }
}
