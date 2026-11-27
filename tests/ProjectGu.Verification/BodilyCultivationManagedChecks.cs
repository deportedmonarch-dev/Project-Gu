using ProjectGu;
using ProjectGu.Combat;
using ProjectGu.Domain;
using RimWorld;
using Verse;

/// <summary>
/// Managed checks of the S0 channel contract and S1 bodily cultivation configuration: Class-selected properties and their
/// validation, the zero activation cost rule, all-or-nothing per-pulse payment against the real aperture, and Scribe
/// persistence of per-pulse payment. Native fixtures cover the pawn hediff, jobs, interruption, custody and reload.
/// </summary>
internal static class BodilyCultivationManagedChecks
{
    internal static void Run(string directory)
    {
        VerifyConfiguration();
        VerifyAtomicPayment();
        VerifySessionPersistence(directory);
    }

    private static HediffDef Attainment(Action<HediffDef> change = null)
    {
        var def = new HediffDef
        {
            defName = "ManagedBoarStrength", label = "managed boar strength", hediffClass = typeof(Hediff_GuBodilyAttainment),
            isBad = false, tendable = false, everCurableByItem = false, keepOnBodyPartRestoration = true, maxSeverity = 1f,
            stages = new List<HediffStage> { new() { multiplyStatChangesBySeverity = true }, new() { minSeverity = 1f, multiplyStatChangesBySeverity = true } }
        };
        change?.Invoke(def);
        return def;
    }

    private static GuAbilityDef Ability(GuChannelProperties channel, float essenceCost = 0f) => new()
    {
        defName = "ManagedBoarCultivation", label = "managed boar cultivation", essenceCost = essenceCost, cooldownTicks = 60000,
        warmupTicks = 0, targetPawns = false, targetLocations = false, requireLineOfSight = false, category = GuAbilityCategory.Cultivation,
        channel = channel
    };

    private static void VerifyConfiguration()
    {
        var props = new GuChannelProperties_BodilyCultivation { attainment = Attainment(), pulseIntervalTicks = 500, pulseCount = 10 };
        var ability = Ability(props);
        var errors = ability.ConfigErrors().ToList();
        Check(errors.Count == 0 && props.workerClass == typeof(GuChannelWorker_BodilyCultivation)
            && ability.channel.Worker(ability) is GuChannelWorker_BodilyCultivation,
            $"Class-selected bodily properties choose their worker and validate with a zero activation cost ({string.Join("; ", errors)})");
        Check(Math.Abs(props.ProgressPerPulse * 150f - 1f) < 1e-6f, "150 pulses reach full development by default");

        Check(new GuAbilityDef { defName = "ZeroProjectile", essenceCost = 0f, projectileDef = null }.ConfigErrors().Any(e => e.Contains("essenceCost")),
            "A non-channel ability still needs a positive essence cost");
        Check(Ability(new GuChannelProperties(), -1f).ConfigErrors().Any(e => e.Contains("essenceCost")), "A negative activation cost is refused");
        Check(Ability(new GuChannelProperties { workerClass = typeof(GuChannelWorker_BodilyCultivation) }).ConfigErrors().Any(e => e.Contains("BodilyCultivation")),
            "A bodily worker on plain channel properties is refused");
        Check(Ability(new GuChannelProperties_BodilyCultivation { attainment = null }).ConfigErrors().Any(e => e.Contains("attainment")),
            "A bodily channel needs an attainment HediffDef");
        Check(Ability(new GuChannelProperties_BodilyCultivation { attainment = Attainment(), essencePerPulse = 0f, pulsesToFullDevelopment = 0 }).ConfigErrors()
            .Count(e => e.Contains("essencePerPulse") || e.Contains("pulsesToFullDevelopment")) == 2, "Per-pulse cost and pulse count are bounded");
        foreach (var (label, change) in new (string, Action<HediffDef>)[]
        {
            ("curable by medicine", def => def.everCurableByItem = true),
            ("bad", def => def.isBad = true),
            ("tendable", def => def.tendable = true),
            ("removed on resurrection", def => def.forceRemoveOnResurrection = true),
            ("lost when parts are restored", def => def.keepOnBodyPartRestoration = false),
            ("unscaled", def => def.stages[1].multiplyStatChangesBySeverity = false),
            ("uncapped", def => def.maxSeverity = 2f),
            ("an ordinary hediff class", def => def.hediffClass = typeof(Hediff)),
        })
            Check(Ability(new GuChannelProperties_BodilyCultivation { attainment = Attainment(change) }).ConfigErrors().Any(),
                $"An attainment that is {label} is refused");

        // S0: the Liquor Worm Def shape still validates and still selects its worker by default.
        var liquor = Ability(new GuChannelProperties { pulseIntervalTicks = 250, pulseCount = 10, amountPerPulse = 1f }, 2f);
        Check(!liquor.ConfigErrors().Any() && liquor.channel.Worker(liquor) is GuChannelWorker_EssenceRefinement { RefinesEssence: true },
            "Plain channel properties still default to Liquor Worm refinement");
        Check(liquor.channel.Worker(liquor).ActivationText.Contains("2") && props.Worker(ability).ActivationText.Contains("each successful pulse"),
            "Activation text distinguishes a session cost from per-pulse payment");
    }

    private static void VerifyAtomicPayment()
    {
        var aperture = new Hediff_Aperture();
        aperture.Initialize(40);
        float before = aperture.Essence;
        var initial = aperture.EssenceOfQuality(RankOneStage.Initial);
        Check(!aperture.TrySpendEssenceForCast(1, () => false) && aperture.Essence == before && aperture.EssenceOfQuality(RankOneStage.Initial) == initial,
            "Refused progress restores the exact essence of the pulse");
        try { aperture.TrySpendEssenceForCast(1, () => throw new InvalidOperationException("body refused")); }
        catch (InvalidOperationException) { }
        Check(aperture.Essence == before, "A failure inside the pulse restores its essence");
        Check(aperture.TrySpendEssenceForCast(1, () => true) && Math.Abs(aperture.Essence - (before - 1f)) < 1e-6f, "Accepted progress pays exactly one pulse");
        Check(!aperture.TrySpendEssenceForCast(1000, () => throw new InvalidOperationException("must not run")) && Math.Abs(aperture.Essence - (before - 1f)) < 1e-6f,
            "An unaffordable pulse never reaches the progress step");
    }

    private static void VerifySessionPersistence(string directory)
    {
        var ability = Ability(new GuChannelProperties_BodilyCultivation { attainment = Attainment(), pulseIntervalTicks = 500, pulseCount = 10 });
        ability.defName = "ManagedBoarCultivationPersisted";
        if (DefDatabase<GuAbilityDef>.GetNamedSilentFail(ability.defName) == null) DefDatabase<GuAbilityDef>.Add(ability);
        var ledger = new GuCombatLedger(null);
        var session = new GuChannelSession
        {
            SessionId = Guid.NewGuid().ToString("N"), GuId = Guid.NewGuid().ToString("N"), Ability = ability, CasterId = "Thing_Human9",
            ApertureId = Guid.NewGuid().ToString("N"), PulsesDone = 4, TicksIntoPulse = 321, Converted = 4f / 150f, PulseEssencePaid = 4,
            StartedTick = 1000, Active = false
        };
        ledger.BeginChannel(session, 60000);
        string path = Path.Combine(directory, "gu-bodily-session-managed.xml");
        try
        {
            Scribe.saver.InitSaving(path, "verification");
            ledger.ExposeData();
            Scribe.saver.FinalizeSaving();
            var loaded = new GuCombatLedger(null);
            Scribe.loader.InitLoading(path);
            loaded.ExposeData();
            Scribe.loader.FinalizeLoading();
            var restored = loaded.ChannelFor(session.GuId);
            Check(restored is { PulsesDone: 4, TicksIntoPulse: 321, PulseEssencePaid: 4, EssencePaid: 0f, Complete: false }
                && Math.Abs(restored.Converted - 4.0 / 150.0) < 1e-9 && restored.Ability == ability && loaded.CooldownRemaining(session.GuId, 1000) == 60000,
                "A suspended bodily session keeps its pulses, partial pulse, per-pulse payment and cooldown across Scribe");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("PASS: S0 channel contract and S1 bodily cultivation configuration, payment and persistence (managed).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
