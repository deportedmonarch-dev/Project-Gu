using System.Text.Json;
using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Phase 2 engine investigation: can a Gu manifest as a Pawn of an ordinary class carrying only
/// <see cref="CompGuManifestation"/>? The probe stages a vanilla animal exactly as release and materialization stage the
/// legacy class, counts native ticks and jobs during staging, and checks load-time tag registration after a reload.
/// </summary>
[StaticConstructorOnStartup]
internal static class ManifestationProbeInstaller
{
    internal const string ProbeKind = "Muffalo";

    static ManifestationProbeInstaller()
    {
        if (!GenCommandLine.CommandLineArgPassed("gumanifestprobe")) return;
        var race = DefDatabase<PawnKindDef>.GetNamed(ProbeKind).race;
        race.comps.Add(new CompProperties_GuManifestation());
        race.notifyMapRemoved = true;
    }
}

public sealed partial class LiveLifecycleChecks
{
    private static bool ManifestationProbe => GenCommandLine.CommandLineArgPassed("gumanifestprobe");
    private static string ManifestationProbePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "gu-manifestation-probe.json");
    private static Pawn probeSubject;
    private static int probeTicks;
    private record ManifestationProbeFixture(string PawnId, string GuId, int Revision);

    private static void CountProbeTick(Pawn __instance)
    {
        if (__instance == probeSubject) probeTicks++;
    }

    private static void ExerciseManifestationProbe()
    {
        Map map = Find.CurrentMap;
        var kind = DefDatabase<PawnKindDef>.GetNamed(ManifestationProbeInstaller.ProbeKind);
        Check(kind.race.thingClass == typeof(Pawn), "The probe uses an ordinary Pawn class, not the legacy Gu class");
        var counter = new Harmony("projectgu.verification.manifestationprobe");
        foreach (var name in new[] { "Tick", "TickInterval" })
            counter.Patch(AccessTools.DeclaredMethod(typeof(Pawn), name), postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(CountProbeTick)));
        try
        {
            int startTick = Find.TickManager.TicksGame;
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, tile: map.Tile, forceGenerateNewPawn: true,
                canGeneratePawnRelations: false, forceNoIdeo: true, developmentalStages: DevelopmentalStage.Adult));
            probeSubject = pawn;
            probeTicks = 0;
            var tag = pawn.GuTag();
            Check(tag != null, "The ordinary Pawn received the manifestation comp from its race Def");
            var record = GuRecord.CreateManifested(Guid.NewGuid().ToString("N"), DefDatabase<GuSpeciesDef>.GetNamed("GU_DevTestSpringGu"),
                "probe", map.GetUniqueLoadID(), startTick, GuCondition.Active, GuManifestationKind.Pawn, pawn.GetUniqueLoadID());
            tag.Bind(record);
            IntVec3 cell = EmptyCargoCell(map, map.mapPawns.FreeColonistsSpawned.First().Position, null, 6);
            Check(GenSpawn.Spawn(pawn, cell, map) == pawn && pawn.Spawned, "Spawned the staged ordinary Pawn");
            Check(probeTicks == 0 && pawn.CurJob == null && Find.TickManager.TicksGame == startTick,
                $"Synchronous staging ran no native tick and assigned no job. Ticks {probeTicks}, job {pawn.CurJob?.def.defName ?? "none"}");
            Report($"OBS staging: ordinary Pawn generated, tagged and spawned with {probeTicks} ticks and no job inside one call");
            Find.TickManager.DoSingleTick();
            Report($"OBS first tick after staging: ticks {probeTicks}, job {pawn.CurJob?.def.defName ?? "none"}");
            File.WriteAllText(ManifestationProbePath, JsonSerializer.Serialize(new ManifestationProbeFixture(pawn.GetUniqueLoadID(), tag.GuId, tag.GuRevision)));
        }
        finally
        {
            counter.UnpatchAll(counter.Id);
            probeSubject = null;
        }
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static void VerifyManifestationProbeReload()
    {
        var fixture = JsonSerializer.Deserialize<ManifestationProbeFixture>(File.ReadAllText(ManifestationProbePath));
        var pawn = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(candidate => candidate.GetUniqueLoadID() == fixture.PawnId);
        var tag = pawn.GuTag();
        Check(tag != null && tag.GuId == fixture.GuId && tag.GuRevision == fixture.Revision,
            "The ordinary Pawn's tag kept the manifestation keys across a fresh-process reload");
        // No canonical record was created, so load-time registration must surface the tag as a rogue projection.
        Check(IndividualRegistry.RogueProjections.Contains(tag) && IndividualRegistry.Resolve(pawn) == null,
            "The tag registered during load without a world scan and was refused authorization without a record");
        Report("An ordinary Pawn class carried the manifestation comp through staging and reload. Binding still requires the legacy class.");
    }
}
