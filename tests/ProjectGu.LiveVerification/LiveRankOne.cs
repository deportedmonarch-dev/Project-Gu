using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static bool RankOne => GenCommandLine.CommandLineArgPassed("gurankone");
    private static string RankOneStatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "rank-one-state.json");
    private sealed record RankOneState(string ApertureId, int Aptitude, RankOneStage Stage,
        double Nourishment, int WorkTicks, double[] Essences);

    private static void ExerciseRankOne(Pawn elder, Hediff_Aperture aperture)
    {
        DebugSettings.enableStoryteller = false;
        Check(!Prefs.DevMode && aperture.Stage == RankOneStage.Initial && aperture.Rank == 1
            && aperture.WallNourishment == 0 && aperture.EssenceOfQuality(RankOneStage.Middle) == 0,
            "The newly founded elder starts at Rank 1 Initial with ordinary essence");
        Check(!GuApertureGizmos.BuildOptions(elder).Any(o => o.Label.StartsWith("Advance to", StringComparison.Ordinal)),
            "The player must nourish the walls to unlock advancement");
        OrderRankOneCultivation(elder, "Nourish aperture walls");
        TickUntil(() => aperture.WallNourishment == 1 && aperture.WallWorkTicks >= 87, 500,
            "A paid nourishment pulse and partial next pulse were earned through the native job");
        double nourishment = aperture.WallNourishment;
        int work = aperture.WallWorkTicks;
        elder.drafter.Drafted = true;
        TickUntil(() => elder.CurJobDef != JobDriver_GuNourishAperture.NourishJob, 30, "Drafting interrupted nourishment");
        TickUntil(() => false, 300);
        Check(aperture.WallNourishment == nourishment && aperture.WallWorkTicks == work
            && aperture.Stage == RankOneStage.Initial, "Interrupted cultivation paused both work and debits");
        elder.drafter.Drafted = false;
        OrderRankOneCultivation(elder, "Nourish aperture walls");
        AdvanceRankOneStage(elder, aperture, RankOneStage.Middle, 6000);

        Check(aperture.EssenceOfQuality(RankOneStage.Initial) > 0 && aperture.EssenceOfQuality(RankOneStage.Middle) == 0,
            "Advancement kept the raw Initial stock at its original quality");
        CheckRankOneRecovery(aperture, RankOneStage.Middle);
        FillThroughNaturalRecovery(aperture);

        OrderRankOneCultivation(elder, "Nourish aperture walls");
        TickUntil(() => aperture.WallNourishment == 2 && aperture.WallWorkTicks >= 111, 550,
            "One pulse of Middle essence delivered two nourishment and the next pulse is in flight");
        Check(aperture.EssenceOfQuality(RankOneStage.Initial) > 0 && aperture.EssenceOfQuality(RankOneStage.Middle) > 0
            && aperture.TipStringExtra.Contains("Rank 1 Middle") && aperture.TipStringExtra.Contains("Wall nourishment"),
            "Both qualities and progress are inspectable while the actual stage remains Middle");
        SaveRankOne(aperture);
        Report($"Saved an active Middle cultivation job after {aperture.WallNourishment} paid nourishment and {aperture.WallWorkTicks} partial ticks. Stock: {string.Join(", ", ReadEssences(aperture))}.");
        AssertNoErrors("rank-one-new");
    }

    private static void VerifyRankOneReload()
    {
        var (elder, aperture) = LoadRankOne();
        Check(aperture.Stage == RankOneStage.Middle && elder.CurJobDef == JobDriver_GuNourishAperture.NourishJob,
            "The Middle-stage nourishment job survived the process restart");
        int partial = aperture.WallWorkTicks;
        TickUntil(() => aperture.WallNourishment == 4, Hediff_Aperture.NourishmentPulseTicks - partial,
            "The loaded job resumed the saved partial pulse and paid for exactly one more pulse");
        AdvanceRankOneStage(elder, aperture, RankOneStage.Upper, 15000);
        CheckRankOneRecovery(aperture, RankOneStage.Upper);
        FillThroughNaturalRecovery(aperture);
        OrderRankOneCultivation(elder, "Nourish aperture walls");
        AdvanceRankOneStage(elder, aperture, RankOneStage.Peak, 23000);
        CheckRankOneRecovery(aperture, RankOneStage.Peak);
        SaveRankOne(aperture);
        AssertNoErrors("rank-one-reload");
    }

    private static void VerifyRankOnePeak()
    {
        var (elder, aperture) = LoadRankOne();
        Check(aperture is { Stage: RankOneStage.Peak, Rank: 1, WallNourishment: 0 } && !aperture.CanAdvanceRankOne
            && !GuApertureGizmos.BuildOptions(elder).Any(o => o.Label.StartsWith("Advance to", StringComparison.Ordinal)
                || o.Label.StartsWith("Nourish aperture walls", StringComparison.Ordinal))
            && !aperture.TryUpgradeEssence(RankOneStage.Peak, 1),
            "The original aperture persisted at Rank 1 Peak and exposes no further advancement");
        Report($"Fresh-process Peak reload restored exact qualities: {string.Join(", ", ReadEssences(aperture))}.");
        AssertNoErrors("rank-one-peak");
    }

    private static void AdvanceRankOneStage(Pawn pawn, Hediff_Aperture aperture, RankOneStage next, int ticks)
    {
        TickUntil(() => aperture.CanAdvanceRankOne, ticks, $"Nourishment reached the requirement for {next}");
        Check(aperture.Stage == next - 1 && aperture.WallNourishment == aperture.WallNourishmentRequired,
            $"Only the player's advancement action promotes the actual stage to {next}");
        double[] before = ReadEssences(aperture);
        double paid = aperture.WallNourishment;
        OrderRankOneCultivation(pawn, $"Advance to {next}");
        Check(aperture.Stage == next && aperture.Rank == 1 && aperture.WallNourishment == 0
            && ReadEssences(aperture).SequenceEqual(before), "Advancement consumed paid nourishment and preserved every essence quality");
        Report($"Advanced to Rank 1 {next} with {paid} paid nourishment. Stored quantities: {string.Join(", ", before)}.");
    }

    private static void CheckRankOneRecovery(Hediff_Aperture aperture, RankOneStage quality)
    {
        double[] before = ReadEssences(aperture);
        TickUntil(() => aperture.EssenceOfQuality(quality) > before[(int)quality], 250,
            $"Native regeneration produced {quality} essence");
        before[(int)quality] += aperture.RegenerationPerHour / 10d;
        Check(ReadEssences(aperture).Zip(before).All(pair => Math.Abs(pair.First - pair.Second) < 0.00000001),
            $"Regeneration credited exactly one grade-based increment of {quality} and left older stock unchanged");
    }

    private static void FillThroughNaturalRecovery(Hediff_Aperture aperture)
    {
        TickUntil(() => aperture.Essence == aperture.MaxEssence, 35000, "Natural recovery filled the one shared capacity");
        Check(Math.Abs(ReadEssences(aperture).Sum() - aperture.MaxEssence) < 0.00000001,
            "Total across grades equals aptitude capacity");
    }

    private static void OrderRankOneCultivation(Pawn pawn, string label)
    {
        var option = GuApertureGizmos.BuildOptions(pawn).Single(o => o.Label.StartsWith(label, StringComparison.Ordinal));
        Check(option.action != null, $"Ordinary option is available: {option.Label}");
        option.action();
    }

    private static double[] ReadEssences(Hediff_Aperture aperture) =>
        Enum.GetValues<RankOneStage>().Select(aperture.EssenceOfQuality).ToArray();

    private static void SaveRankOne(Hediff_Aperture aperture)
    {
        var state = new RankOneState(aperture.ApertureId, aperture.Aptitude, aperture.Stage,
            aperture.WallNourishment, aperture.WallWorkTicks, ReadEssences(aperture));
        File.WriteAllText(RankOneStatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(SaveName);
    }

    private static (Pawn Elder, Hediff_Aperture Aperture) LoadRankOne()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        var state = JsonSerializer.Deserialize<RankOneState>(File.ReadAllText(RankOneStatePath));
        Pawn elder = Clan(GuClanRole.Elder).Single();
        var aperture = Apertures(elder).Single();
        Check(aperture.ApertureId == state.ApertureId && aperture.Aptitude == state.Aptitude && aperture.Stage == state.Stage
            && aperture.WallNourishment == state.Nourishment && aperture.WallWorkTicks == state.WorkTicks
            && ReadEssences(aperture).SequenceEqual(state.Essences),
            "The same clan aperture reloaded its exact aptitude, stage, wall progress, partial work and essence quantities");
        return (elder, aperture);
    }
}
