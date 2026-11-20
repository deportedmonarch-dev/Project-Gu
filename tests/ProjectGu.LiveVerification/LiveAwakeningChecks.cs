using HarmonyLib;
using ProjectGu;
using ProjectGu.Domain;
using ProjectGu.Springs;
using Verse;

public sealed partial class LiveLifecycleChecks
{
    private static HediffDef ApertureDef => DefDatabase<HediffDef>.GetNamed("GU_Aperture");
    private static GuWorldComponent Cultivation => Current.Game.GetComponent<GuWorldComponent>();
    private static Action OnApertureAdded;

    private static void ExerciseAwakening()
    {
        Map map = Find.CurrentMap;
        Pawn[] colonists = map.mapPawns.FreeColonistsSpawned.Take(2).ToArray();
        Pawn candidate = colonists[0];
        Pawn noAptitude = colonists[1];
        Cultivation.GetOrCreate(candidate).LatentAptitude = 63;
        Cultivation.GetOrCreate(noAptitude).LatentAptitude = 0;
        var spring = SpawnAwakeningSpring(map, map.Center + East * 4);
        var contested = SpawnAwakeningSpring(map, map.Center - East * 4);
        int startingAdults = spring.HopeGuAvailable;
        int juveniles = spring.HopeGuJuvenile;
        Check(startingAdults == 3, "Fresh spring starts with three adult Hope Gu");

        Type originalClass = ApertureDef.hediffClass;
        try
        {
            ApertureDef.hediffClass = typeof(Hediff);
            Check(!GuAwakeningService.TryAwakenAtSpring(candidate, spring, out _)
                && spring.HopeGuAvailable == startingAdults, "Unavailable aperture creation leaves the cohort intact");
            AssertAwakening(candidate, AwakeningState.Unattempted, false);
        }
        finally
        {
            ApertureDef.hediffClass = originalClass;
        }

        new Harmony("projectgu.verification.awakening").Patch(AccessTools.Method(typeof(Hediff), nameof(Hediff.PostAdd)),
            postfix: new HarmonyMethod(typeof(LiveLifecycleChecks), nameof(AfterTestHediffAdded)));
        try
        {
            OnApertureAdded = () => throw new InvalidOperationException("Injected awakening PostAdd failure");
            Check(!GuAwakeningService.TryAwakenAtSpring(candidate, spring, out _)
                && spring.HopeGuAvailable == startingAdults, "Native PostAdd failure leaves the cohort intact");
            AssertAwakening(candidate, AwakeningState.Unattempted, false);

            int externalDebits = 0;
            OnApertureAdded = () =>
            {
                while (contested.TryConsumeHopeGu()) externalDebits++;
            };
            Check(!GuAwakeningService.TryAwakenAtSpring(candidate, contested, out _)
                && externalDebits == startingAdults && contested.HopeGuAvailable == 0,
                "Loss of the last adult during native callbacks rolls back the provisional aperture");
            AssertAwakening(candidate, AwakeningState.Unattempted, false);
            Check(!GuAwakeningService.TryAwakenAtSpring(candidate, contested, out _)
                && !GuAwakeningService.TryAwakenAtSpring(noAptitude, contested, out _),
                "An empty cohort blocks both positive- and zero-aptitude attempts");
            AssertAwakening(candidate, AwakeningState.Unattempted, false);
            AssertAwakening(noAptitude, AwakeningState.Unattempted, false);

            int reentriesBlocked = 0;
            OnApertureAdded = () =>
            {
                if (!GuAwakeningService.TryAwakenAtSpring(candidate, spring, out _)) reentriesBlocked++;
                if (!Cultivation.TryAwaken(candidate, out _)) reentriesBlocked++;
            };
            Check(GuAwakeningService.TryAwakenAtSpring(candidate, spring, out string success)
                && reentriesBlocked == 2 && spring.HopeGuAvailable == startingAdults - 1,
                $"Success debits once and both reentrant entry points are blocked. {success}");
            AssertAwakening(candidate, AwakeningState.Awakened, true);
        }
        finally
        {
            OnApertureAdded = null;
        }

        Check(GuAwakeningService.TryAwakenAtSpring(noAptitude, spring, out string failure)
            && spring.HopeGuAvailable == startingAdults - 2 && spring.HopeGuJuvenile == juveniles,
            $"A completed failure consumes one adult and preserves juveniles. {failure}");
        AssertAwakening(noAptitude, AwakeningState.Failed, false);
        Check(!GuAwakeningService.TryAwakenAtSpring(candidate, spring, out _)
            && !GuAwakeningService.TryAwakenAtSpring(noAptitude, spring, out _)
            && spring.HopeGuAvailable == startingAdults - 2, "Repeated attempts cause no debit");
        GameDataSaveLoader.SaveGame(SaveName);
        Report("Awakening conserved adults and Pawn state through rejection, callback failure, late depletion and reentry.");
    }

    private static void VerifyAwakeningReload()
    {
        Map map = Find.CurrentMap;
        var springDef = DefDatabase<ThingDef>.GetNamed("GU_SpiritSpring");
        var springs = map.listerThings.ThingsOfDef(springDef).Select(thing => thing.TryGetComp<CompGuSpiritSpring>()).ToArray();
        var spring = springs.Single(comp => comp.HopeGuAvailable == 1);
        Check(springs.Length == 2 && springs.Any(comp => comp.HopeGuAvailable == 0),
            "Both debited cohorts survived a fresh-process reload");
        var pawns = map.mapPawns.FreeColonistsSpawned;
        Pawn awakened = pawns.Single(pawn => Cultivation.GetOrCreate(pawn).State == AwakeningState.Awakened);
        Pawn failed = pawns.Single(pawn => Cultivation.GetOrCreate(pawn).State == AwakeningState.Failed);
        AssertAwakening(awakened, AwakeningState.Awakened, true);
        AssertAwakening(failed, AwakeningState.Failed, false);
        Check(!GuAwakeningService.TryAwakenAtSpring(awakened, spring, out _)
            && !GuAwakeningService.TryAwakenAtSpring(failed, spring, out _)
            && spring.HopeGuAvailable == 1, "Reload preserves once-only awakening");
    }

    private static CompGuSpiritSpring SpawnAwakeningSpring(Map map, IntVec3 cell) =>
        GenSpawn.Spawn(DefDatabase<ThingDef>.GetNamed("GU_SpiritSpring"), cell, map).TryGetComp<CompGuSpiritSpring>();

    private static void AfterTestHediffAdded(Hediff __instance)
    {
        if (__instance is Hediff_Aperture) OnApertureAdded?.Invoke();
    }

    private static void AssertAwakening(Pawn pawn, AwakeningState state, bool hasAperture)
    {
        Check(Cultivation.GetOrCreate(pawn).State == state, $"Pawn has awakening state {state}");
        var apertures = pawn.health.hediffSet.hediffs.Where(hediff => hediff.def == ApertureDef).ToArray();
        Check(hasAperture ? apertures is [Hediff_Aperture { Aptitude: 63, Rank: 1, Essence: 31.5f }] : apertures.Length == 0,
            "Pawn has the expected aperture count and initialized stats");
    }
}
