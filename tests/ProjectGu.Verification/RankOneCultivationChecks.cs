using ProjectGu;
using ProjectGu.Domain;

internal static class RankOneCultivationChecks
{
    internal static void Run()
    {
        VerifyCapacity();
        VerifyPayments();
        VerifyWallProgress();
    }

    private static void VerifyCapacity()
    {
        foreach (var (aptitude, hourly) in new[] { (20, 2), (40, 4), (60, 6), (80, 8) })
        {
            var aperture = NewAperture(aptitude);
            for (int pulse = 0; pulse < 10; pulse++) aperture.RecoverEssence();
            Check(aperture.Rank == 1 && aperture.Stage == RankOneStage.Initial
                && aperture.EssenceOfQuality(RankOneStage.Initial) == aptitude / 2d + hourly
                && aperture.WallNourishment == 0, $"Grade {aperture.AptitudeGrade} recovers {hourly} raw essence per hour");
            Check(aperture.TryUpgradeEssence(RankOneStage.Initial, 3.125), "Same-volume conversion succeeded");
            for (int pulse = 0; pulse < 250; pulse++) aperture.RecoverEssence();
            Check(aperture.Essence == aperture.MaxEssence
                && aperture.EssenceOfQuality(RankOneStage.Initial) == aptitude - 3.125
                && aperture.EssenceOfQuality(RankOneStage.Middle) == 3.125
                && aperture.Stage == RankOneStage.Initial,
                "Recovery stops at shared capacity and conversion leaves actual stage unchanged");
        }
    }

    private static void VerifyPayments()
    {
        var aperture = NewAperture(20);
        Check(aperture.TryUpgradeEssence(RankOneStage.Initial, 9)
            && !aperture.TryUpgradeEssence(RankOneStage.Middle, 1)
            && !aperture.TryUpgradeEssence(RankOneStage.Initial, 2)
            && !aperture.TryUpgradeEssence(RankOneStage.Initial, double.Epsilon)
            && !aperture.TrySpendEssence(10.000001) && !aperture.TrySpendEssence(double.NaN)
            && aperture.Essence == 10, "Invalid transactions leave both qualities unchanged");
        Check(!aperture.TrySpendEssenceForCast(3, () =>
        {
            Check(aperture.EssenceOfQuality(RankOneStage.Initial) == 0 && aperture.EssenceOfQuality(RankOneStage.Middle) == 7
                && !aperture.TrySpendEssence(1), "The exact lowest-first debit is visible during the cast, with reentry blocked");
            return false;
        }) && aperture.EssenceOfQuality(RankOneStage.Initial) == 1 && aperture.EssenceOfQuality(RankOneStage.Middle) == 9,
            "A failed mixed cast restores the original mixture");
        Check(aperture.TrySpendEssence(3) && aperture.Essence == 7
            && aperture.EssenceOfQuality(RankOneStage.Initial) == 0 && aperture.EssenceOfQuality(RankOneStage.Middle) == 7,
            "A raw mixed payment charges unchanged cost, consuming lowest quality first");

        var process = new GuRefinementProcess(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), null, 7, 3, 0);
        for (int tick = 1; tick <= 7; tick++) Check(process.TryAdvance(tick, aperture), "Refinement paid for one tick");
        Check(process.CompletedWorkTicks == 7 && process.EssenceSpent == 3 && aperture.EssenceOfQuality(RankOneStage.Middle) == 4,
            "Refinement paid the exact quoted amount even using exclusively higher-quality essence");
    }

    private static void VerifyWallProgress()
    {
        var aperture = NewAperture(20);
        Check(aperture.TryUpgradeEssence(RankOneStage.Initial, 0.5), "Mixed nourishment stock prepared");
        for (int tick = 1; tick < Hediff_Aperture.NourishmentPulseTicks; tick++) aperture.TickWallNourishment();
        Check(aperture.WallNourishment == 0 && aperture.Essence == 10 && !aperture.TryAdvanceRankOne(RankOneStage.Initial),
            "Partial wall work takes time and cannot raise stage or charge early");
        Check(aperture.TickWallNourishment() && aperture.WallNourishment == 1.5 && aperture.Essence == 9
            && aperture.EssenceOfQuality(RankOneStage.Middle) == 0,
            "A mixed nourishment pulse consumes one raw essence and weights both qualities");
        for (var stage = RankOneStage.Initial; stage < RankOneStage.Peak; stage++)
        {
            for (int pulse = 0; pulse < 500 && !aperture.CanAdvanceRankOne; pulse++)
            {
                for (int tick = 0; tick < Hediff_Aperture.NourishmentPulseTicks; tick++) aperture.TickWallNourishment();
                aperture.RecoverEssence();
            }
            float before = aperture.Essence;
            Check(aperture.Stage == stage && aperture.TryAdvanceRankOne(stage) && !aperture.TryAdvanceRankOne(stage)
                && aperture.Stage == stage + 1 && aperture.Rank == 1 && aperture.WallNourishment == 0
                && aperture.Essence == before, $"D-grade recovery and paid nourishment can advance from {stage}");
        }
        Check(!aperture.CanNourishWalls && !aperture.TryAdvanceRankOne(RankOneStage.Peak)
            && !aperture.TryUpgradeEssence(RankOneStage.Peak, 1), "Peak is the Rank 1 ceiling");
        Console.WriteLine("PASS: Rank 1 capacity, conversion, mixed payments, wall progress and stage ceiling.");
    }

    private static Hediff_Aperture NewAperture(int aptitude)
    {
        var aperture = new Hediff_Aperture();
        aperture.Initialize(aptitude);
        return aperture;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
