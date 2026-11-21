using HarmonyLib;
using ProjectGu;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

public sealed class Slice3ProbeThing : ThingWithComps
{
    internal string Identity;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref Identity, "probeIdentity");
    }
}

public sealed partial class LiveLifecycleChecks
{
    private const string HeldProbe = "slice3a-held";
    private const string GroundProbe = "slice3a-ground";
    private const int ProbeAptitude = 63;
    private static bool Slice3A => GenCommandLine.CommandLineArgPassed("guslice3a");
    private static ThingDef ProbeThingDef => DefDatabase<ThingDef>.GetNamed("GU_Slice3A_ProbeThing");
    private Pawn probeHost;
    private Hediff_Aperture probeAperture;
    private Slice3ProbeThing probeHeld;
    private Slice3ProbeThing probeGround;
    private string retiredPawnId;
    private string probeHostId;
    private string probeApertureId;
    private static Pawn observedRetirement;
    private static int killCalls;
    private static int makeCorpseCalls;
    private static int discardCleanupCalls;
    private static int corpseDestructionCalls;

    public override void ExposeData()
    {
        base.ExposeData();
        if (!Slice3A) return;
        Scribe_References.Look(ref probeHost, "slice3ProbeHost");
        Scribe_References.Look(ref probeAperture, "slice3ProbeAperture");
        Scribe_References.Look(ref probeHeld, "slice3ProbeHeld");
        Scribe_References.Look(ref probeGround, "slice3ProbeGround");
        Scribe_Values.Look(ref retiredPawnId, "slice3ProbeRetiredPawnId");
        Scribe_Values.Look(ref probeHostId, "slice3ProbeHostId");
        Scribe_Values.Look(ref probeApertureId, "slice3ProbeApertureId");
    }

    private void ExerciseSlice3A()
    {
        Map home = Find.CurrentMap;
        probeHost = home.mapPawns.FreeColonistsSpawned.First();
        Pawn otherHost = home.mapPawns.FreeColonistsSpawned.First(pawn => pawn != probeHost);
        var site = OpenIndependentSite(probeHost, home);
        var surface = site.Endpoints.Single(end => end.IsSurface).Anchor;
        Map cave = site.Levels.Single().LiveMap;
        var exit = site.Endpoints.Single(end => !end.IsSurface).Anchor;

        ProbeWildRetirement(cave, exit.Position);
        ProbeThingHolders(home);
        probeAperture = AddProbeAperture(probeHost);
        var otherAperture = AddProbeAperture(otherHost);
        probeHostId = probeHost.GetUniqueLoadID();
        probeApertureId = probeAperture.GetUniqueLoadID();
        Check(ResolveProbeAperture(probeHost, probeHostId, probeApertureId) == probeAperture
            && ResolveProbeAperture(otherHost, probeHostId, probeApertureId) == null
            && ResolveProbeAperture(probeHost, probeHostId, otherAperture.GetUniqueLoadID()) == null,
            "Aperture binding accepts only the exact host and instance");

        Check(JobDriver_GuPocketProbeTravel.TryTravel(probeHost, surface, out string reason) && probeHost.Map == cave,
            $"The native passage brought the test host underground. {reason}");
        AssertProbeHolders(home, cave);
        Check(ResolveProbeAperture(probeHost, probeHostId, probeApertureId) == probeAperture,
            "The exact aperture remains attached after cross-map travel");
        Report("PASS 2 new: unstackable Things remained distinct, failed placement preserved inventory, and the held Thing followed its host while the ground Thing stayed in the map owner.");
        Report("PASS 3 new: exact host and aperture matched after travel and rejected same-Def controls.");
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private void ProbeWildRetirement(Map map, IntVec3 near)
    {
        var kind = DefDatabase<PawnKindDef>.GetNamed("Hare");
        var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, tile: map.Tile,
            forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceNoIdeo: true));
        IntVec3 cell = EmptyCargoCell(map, near, null, 2);
        GenSpawn.Spawn(pawn, cell, map);
        retiredPawnId = pawn.GetUniqueLoadID();
        Check(pawn.Spawned && !pawn.Dead && pawn.Faction == null && !Find.WorldPawns.Contains(pawn)
            && pawn.inventory.GetDirectlyHeldThings().Count == 0 && pawn.GetLord() == null
            && !Find.TaleManager.AnyTaleConcerns(pawn), "Retirement fixture is a living, unowned wild Pawn");

        IntVec3 reserved = EmptyCargoCell(map, near, pawn, 3);
        Job job = JobMaker.MakeJob(JobDefOf.Goto, reserved);
        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        Check(pawn.CurJob == job && pawn.Reserve(reserved, job), "The active wild Pawn owns a native job and cell reservation");

        var observer = new Harmony("projectgu.verification.slice3a.retirement");
        try
        {
            observedRetirement = pawn;
            killCalls = makeCorpseCalls = discardCleanupCalls = corpseDestructionCalls = 0;
            InstallRetirementObservers(observer);
            pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
            Check(pawn.CurJob == null && !map.reservationManager.IsReserved(reserved),
                "Despawn stopped the job and released the reservation");
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
            Check(pawn.Destroyed && pawn.Discarded && !pawn.Dead && pawn.Corpse == null && pawn.holdingOwner == null
                && killCalls == 0 && makeCorpseCalls == 0 && corpseDestructionCalls == 0
                && discardCleanupCalls == 1 && !Find.TaleManager.AnyTaleConcerns(pawn),
                "Explicit native discard retired the wild Pawn without killing it, creating a corpse or recording a tale");
            AssertRetiredPawnAbsent();
            Report($"PASS 1 new: wild Pawn discarded, jobs/reservations cleared, kill calls={killCalls}, corpse creation calls={makeCorpseCalls}, native discard cleanup calls={discardCleanupCalls}.");
        }
        finally
        {
            observer.UnpatchAll(observer.Id);
            observedRetirement = null;
        }
    }

    private static void InstallRetirementObservers(Harmony observer)
    {
        observer.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveKill)));
        var corpseFactories = AccessTools.GetDeclaredMethods(typeof(Pawn)).Where(method => method.Name == "MakeCorpse").ToArray();
        Check(corpseFactories.Length > 0, "The installed assembly exposes its native corpse factories");
        var observeCorpse = new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveCorpse));
        foreach (var factory in corpseFactories) observer.Patch(factory, prefix: observeCorpse);
        observer.Patch(AccessTools.Method(typeof(Corpse), "PostCorpseDestroy"),
            prefix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(ObserveCorpseCleanup)));
    }

    private static void ObserveKill(Pawn __instance)
    {
        if (__instance == observedRetirement) killCalls++;
    }

    private static void ObserveCorpse(Pawn __instance)
    {
        if (__instance == observedRetirement) makeCorpseCalls++;
    }

    private static void ObserveCorpseCleanup(Pawn __0, bool __1)
    {
        if (__0 != observedRetirement) return;
        if (__1) discardCleanupCalls++;
        else corpseDestructionCalls++;
    }

    private void AssertRetiredPawnAbsent()
    {
        Check(!string.IsNullOrEmpty(retiredPawnId)
            && !Find.WorldPawns.AllPawnsAliveOrDead.Any(pawn => pawn.GetUniqueLoadID() == retiredPawnId)
            && !Find.Maps.Any(map => map.mapPawns.AllPawns.Any(pawn => pawn.GetUniqueLoadID() == retiredPawnId)
                || map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                    .Any(corpse => corpse.InnerPawn.GetUniqueLoadID() == retiredPawnId)),
            "The retired ID is absent from world Pawns, map Pawns and map corpses");
    }

    private void ProbeThingHolders(Map home)
    {
        probeHeld = (Slice3ProbeThing)ThingMaker.MakeThing(ProbeThingDef);
        probeGround = (Slice3ProbeThing)ThingMaker.MakeThing(ProbeThingDef);
        probeHeld.Identity = HeldProbe;
        probeGround.Identity = GroundProbe;
        GenSpawn.Spawn(probeHeld, EmptyCargoCell(home, probeHost.Position, probeHost, 1), home);
        var inventory = probeHost.inventory.GetDirectlyHeldThings();
        var hands = probeHost.carryTracker.GetDirectlyHeldThings();
        Check(probeHost.carryTracker.TryStartCarry(probeHeld, 1, false) == 1
            && hands.TryTransferToContainer(probeHeld, inventory, 1, true) == 1
            && inventory.TryAdd(probeGround, canMergeWithExistingStacks: true)
            && inventory.Contains(probeHeld) && inventory.Contains(probeGround)
            && probeHeld.stackCount == 1 && probeGround.stackCount == 1,
            "Native transfer and merge-enabled add retain two distinct one-item stacks");

        Check(!inventory.TryDrop(probeHeld, probeHost.Position, home, ThingPlaceMode.Near,
                out _, null, _ => false) && probeHeld.holdingOwner == inventory && inventory.Contains(probeHeld),
            "Rejected native placement leaves the exact Thing in inventory");
        IntVec3 cell = EmptyCargoCell(home, probeHost.Position, probeHost, 1);
        Check(inventory.TryDrop(probeGround, cell, home, ThingPlaceMode.Direct, out Thing placed) && placed == probeGround,
            "Native placement spawns the exact second Thing on the ground");
    }

    private void AssertProbeHolders(Map home, Map hostMap)
    {
        Check(probeHost?.Spawned == true && probeHost.Map == hostMap, "The exact host is spawned on the expected map");
        Check(probeHeld?.Identity == HeldProbe && probeGround?.Identity == GroundProbe
            && probeHeld.stackCount == 1 && probeGround.stackCount == 1, "Both saved Thing identities and unit counts match");
        var inventory = probeHost.inventory.GetDirectlyHeldThings();
        Check(probeHeld.holdingOwner == inventory && probeHeld.ParentHolder == probeHost.inventory
            && probeHeld.SpawnedParentOrMe == probeHost && probeHeld.MapHeld == hostMap
            && inventory.Single(thing => thing.def == ProbeThingDef) == probeHeld,
            "The exact held Thing resolves through the native inventory to its spawned host");
        var ground = home.GetDirectlyHeldThings();
        Check(probeGround.Spawned && probeGround.Map == home && probeGround.ParentHolder == home
            && probeGround.holdingOwner == ground && ground.Contains(probeGround)
            && Find.Maps.SelectMany(map => map.listerThings.ThingsOfDef(ProbeThingDef)).Single() == probeGround,
            "The map's native spawned-Thing owner holds the exact ground Thing, listed once on the maps");
    }

    private static Hediff_Aperture AddProbeAperture(Pawn host)
    {
        var aperture = (Hediff_Aperture)HediffMaker.MakeHediff(ApertureDef, host);
        aperture.Initialize(ProbeAptitude);
        host.health.AddHediff(aperture);
        Check(host.health.hediffSet.hediffs.Contains(aperture), "The native health tracker owns the exact aperture fixture");
        return aperture;
    }

    private static Hediff_Aperture ResolveProbeAperture(Pawn host, string hostId, string apertureId)
    {
        if (host == null || host.Dead || host.Destroyed || host.GetUniqueLoadID() != hostId) return null;
        return host.health.hediffSet.hediffs.OfType<Hediff_Aperture>().SingleOrDefault(aperture =>
            aperture.def == ApertureDef && aperture.pawn == host && aperture.GetUniqueLoadID() == apertureId);
    }

    private void VerifySlice3AReload()
    {
        AssertRetiredPawnAbsent();
        Report("PASS 1 reload: retired Pawn ID remains absent from world Pawns, map Pawns and corpses.");
        Map home = Find.Maps.Single(map => map.IsPlayerHome);
        Map cave = Find.WorldObjects.MapParents.OfType<GuUndergroundLevelParent>().Single().Map;
        AssertProbeHolders(home, cave);
        Check(ResolveProbeAperture(probeHost, probeHostId, probeApertureId) == probeAperture
            && probeAperture is { Aptitude: ProbeAptitude, Rank: 1, Essence: 31.5f },
            "The saved host, exact Hediff instance and initialized aperture fields resolved after reload");

        var exit = cave.listerThings.ThingsOfDef(GuPocketSiteDef.SpiritSpring.ExitDef).OfType<Building_GuPocketProbeEndpoint>().Single();
        Check(JobDriver_GuPocketProbeTravel.TryTravel(probeHost, exit, out string reason) && probeHost.Map == home,
            $"The saved host returned through the native passage. {reason}");
        AssertProbeHolders(home, home);
        var inventory = probeHost.inventory.GetDirectlyHeldThings();
        IntVec3 cell = EmptyCargoCell(home, probeHost.Position, probeHost, 1);
        Check(inventory.TryDrop(probeHeld, cell, home, ThingPlaceMode.Direct, out Thing placed) && placed == probeHeld
            && probeHeld.Spawned && !inventory.Contains(probeHeld), "The reloaded inventory dropped the same identified Thing");
        Report("PASS 2 reload: exact Scribe Thing references matched their ground and inventory owners; return travel and native drop preserved the held instance.");

        Pawn otherHost = home.mapPawns.FreeColonistsSpawned.Single(pawn => pawn != probeHost
            && pawn.health.hediffSet.hediffs.Any(hediff => hediff.def == ApertureDef));
        var otherAperture = otherHost.health.hediffSet.hediffs.OfType<Hediff_Aperture>().Single();
        string otherHostId = otherHost.GetUniqueLoadID();
        string otherApertureId = otherAperture.GetUniqueLoadID();
        Check(ResolveProbeAperture(otherHost, otherHostId, otherApertureId) == otherAperture, "The second host provides a live control");
        otherHost.Kill(null);
        Check(otherHost.Dead && ResolveProbeAperture(otherHost, otherHostId, otherApertureId) == null
            && ResolveProbeAperture(probeHost, probeHostId, probeApertureId) == probeAperture,
            "Host death invalidates only that host's aperture binding");

        probeHost.health.RemoveHediff(probeAperture);
        var replacement = AddProbeAperture(probeHost);
        Check(replacement.GetUniqueLoadID() != probeApertureId
            && ResolveProbeAperture(probeHost, probeHostId, probeApertureId) == null
            && ResolveProbeAperture(probeHost, probeHostId, replacement.GetUniqueLoadID()) == replacement,
            "A same-Def replacement never satisfies the removed instance's binding");
        Report("PASS 3 reload: exact saved Pawn/Hediff references and IDs matched after reload and return travel; host death and replacement invalidate only their old bindings.");
    }
}
