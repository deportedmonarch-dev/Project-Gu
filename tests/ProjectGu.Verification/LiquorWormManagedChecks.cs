using ProjectGu;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using UnityEngine;
using Verse;

/// <summary>
/// Managed checks of the Alpha 3 Liquor Worm channel against the real aperture: one-step conversion of the current
/// stage's essence, conservation, stage ceiling, binding to the stage the session began in, session persistence, and the
/// orbit geometry and layering used by the manifestation. Native fixtures cover jobs, custody, interruption and reload.
/// </summary>
internal static class LiquorWormManagedChecks
{
    internal static void Run(string directory)
    {
        VerifyConversion();
        VerifyCeilingAndBinding();
        VerifySessionPersistence(directory);
        VerifyOrbit();
    }

    private static GuChannelWorker Worker(int pulses = 10, float amount = 1f)
    {
        var ability = new GuAbilityDef
        {
            defName = "ManagedLiquorWorm",
            essenceCost = 2f,
            cooldownTicks = 30000,
            channel = new GuChannelProperties { pulseIntervalTicks = 250, pulseCount = pulses, amountPerPulse = amount }
        };
        return ability.channel.Worker(ability);
    }

    private static GuChannelSession SessionFor(GuChannelWorker worker, Hediff_Aperture aperture) => new()
    {
        SessionId = Guid.NewGuid().ToString("N"),
        GuId = Guid.NewGuid().ToString("N"),
        Ability = worker.ability,
        Binding = worker.BindingFor(aperture)
    };

    private static double Total(Hediff_Aperture aperture) => Enum.GetValues<RankOneStage>().Sum(aperture.EssenceOfQuality);

    private static void VerifyConversion()
    {
        var aperture = NewAperture(20); // D grade: capacity 20, Initial 10 at awakening.
        var worker = Worker();
        var session = SessionFor(worker, aperture);
        Check(worker.CanChannel(aperture, out _) && session.Binding == (int)RankOneStage.Initial,
            "An Initial-stage aperture with Initial essence can begin Liquor Worm refinement");
        double total = Total(aperture);
        double wall = aperture.WallNourishment;
        for (int pulse = 0; pulse < 4; pulse++)
            Check(worker.ApplyPulse(aperture, session, out _) == GuPulseResult.Applied, "A pulse converted essence");
        Check(aperture.EssenceOfQuality(RankOneStage.Initial) == 6 && aperture.EssenceOfQuality(RankOneStage.Middle) == 4
            && Total(aperture) == total && aperture.Stage == RankOneStage.Initial && aperture.WallNourishment == wall
            && session.Converted == 4,
            "Four pulses move exactly four raw units Initial→Middle; volume, stage and wall progress are unchanged");

        Check(aperture.TrySpendEssence(5.5), "Fixture spends Initial essence down to a fraction");
        Check(aperture.EssenceOfQuality(RankOneStage.Initial) == 0.5
            && worker.ApplyPulse(aperture, session, out _) == GuPulseResult.Applied
            && aperture.EssenceOfQuality(RankOneStage.Initial) == 0 && aperture.EssenceOfQuality(RankOneStage.Middle) == 4.5,
            "A pulse converts only what remains of the source quality");
        Check(worker.ApplyPulse(aperture, session, out string exhausted) == GuPulseResult.Stopped
            && aperture.EssenceOfQuality(RankOneStage.Middle) == 4.5 && aperture.EssenceOfQuality(RankOneStage.Upper) == 0
            && !worker.CanChannel(aperture, out _),
            $"With no current-stage essence left the session stops and never re-refines Middle essence ({exhausted})");

        // Wall nourishment then credits the refined essence at Middle weight.
        for (int tick = 0; tick < Hediff_Aperture.NourishmentPulseTicks; tick++) aperture.TickWallNourishment();
        Check(aperture.WallNourishment == 2 && aperture.EssenceOfQuality(RankOneStage.Middle) == 3.5,
            "One nourishment pulse spends one Liquor Worm-refined Middle unit for two wall nourishment");
    }

    private static void VerifyCeilingAndBinding()
    {
        var aperture = NewAperture(20);
        var worker = Worker();
        var session = SessionFor(worker, aperture);
        AdvanceTo(aperture, RankOneStage.Middle);
        double middle = aperture.EssenceOfQuality(RankOneStage.Middle);
        Check(worker.ApplyPulse(aperture, session, out string changed) == GuPulseResult.Stopped
            && aperture.EssenceOfQuality(RankOneStage.Middle) == middle,
            $"A session bound to Initial stops without converting once the cultivator reaches Middle ({changed})");

        AdvanceTo(aperture, RankOneStage.Upper);
        while (aperture.EssenceOfQuality(RankOneStage.Upper) < 2) aperture.RecoverEssence();
        var upper = SessionFor(worker, aperture);
        double total = Total(aperture);
        Check(worker.ApplyPulse(aperture, upper, out _) == GuPulseResult.Applied && aperture.EssenceOfQuality(RankOneStage.Peak) == 1
            && Total(aperture) == total && aperture.Stage == RankOneStage.Upper,
            "At Upper the Liquor Worm refines Upper essence into Peak essence");

        AdvanceTo(aperture, RankOneStage.Peak);
        double peak = aperture.EssenceOfQuality(RankOneStage.Peak);
        Check(!worker.CanChannel(aperture, out string ceiling) && aperture.Rank == 1
            && worker.ApplyPulse(aperture, SessionFor(worker, aperture), out _) == GuPulseResult.Stopped
            && aperture.EssenceOfQuality(RankOneStage.Peak) == peak,
            $"Rank 1 Peak cannot be refined further and nothing becomes Rank 2 quality ({ceiling})");
    }

    private static void VerifySessionPersistence(string directory)
    {
        var ledger = new GuCombatLedger(null);
        var worker = Worker();
        // Sessions save their ability by Def reference, so the fixture Def must be resolvable like a loaded XML Def.
        worker.ability.defName = "ManagedLiquorWormPersisted";
        if (DefDatabase<GuAbilityDef>.GetNamedSilentFail(worker.ability.defName) == null) DefDatabase<GuAbilityDef>.Add(worker.ability);
        var aperture = NewAperture(40);
        var session = SessionFor(worker, aperture);
        session.CasterId = "Thing_Human7";
        session.ApertureId = Guid.NewGuid().ToString("N");
        session.StartedTick = 5000;
        session.EssencePaid = 2f;
        session.PulsesDone = 3;
        session.TicksIntoPulse = 117;
        session.Converted = 3;
        session.Active = true;
        ledger.BeginChannel(session, 30000);
        Check(ledger.ChannelFor(session.GuId) == session && ledger.CooldownRemaining(session.GuId, 5000) == 30000 && ledger.CommittedTotal == 1,
            "Beginning a session records it once and starts the Gu's cooldown");

        string path = Path.Combine(directory, "gu-liquor-session-managed.xml");
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
            Check(restored is { PulsesDone: 3, TicksIntoPulse: 117, Converted: 3, EssencePaid: 2f, Active: true }
                && restored.SessionId == session.SessionId && restored.CasterId == session.CasterId
                && restored.Binding == session.Binding && loaded.CooldownRemaining(session.GuId, 5000) == 30000 && loaded.CommittedTotal == 1,
                "Scribe round trip keeps the exact pulse count, partial pulse, refined total, payment and cooldown");
            loaded.EndChannel(restored);
            Check(loaded.ChannelFor(session.GuId) == null, "Ending a session removes it");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void VerifyOrbit()
    {
        var profile = new GuFxProfileDef
        {
            defName = "ManagedOrbit", texPath = "x", columns = 4, frameCount = 4, ticksPerFrame = 6, loop = true,
            lifetimeTicks = 24, orbit = true, orbitRadius = new Vector2(0.4f, 0.15f), orbitCenterOffset = new Vector2(0f, -0.18f),
            orbitPeriodTicks = 90, orbitFrontLayer = 100.6f, orbitBackLayer = -12.5f, orbitBackScale = 0.85f
        };
        var right = profile.OrbitAt(0);
        var far = profile.OrbitAt(22);
        var left = profile.OrbitAt(45);
        var near = profile.OrbitAt(67);
        float highestPawnLayer = 100f * GuFxProfileDef.PawnLayerAltitude;
        float lowestPawnLayer = -10f * GuFxProfileDef.PawnLayerAltitude;
        Check(Mathf.Abs(right.Offset.x - 0.4f) < 0.001f && Mathf.Abs(right.Offset.y + 0.18f) < 0.001f
            && Mathf.Abs(left.Offset.x + 0.4f) < 0.001f && far.Offset.y > -0.18f && near.Offset.y < -0.18f,
            "The orbit is an ellipse around a point just below the anchor's centre");
        Check(!far.Front && near.Front && far.AltitudeOffset < lowestPawnLayer && near.AltitudeOffset > highestPawnLayer,
            "The far half draws beneath the pawn's lowest render layer and the near half above its highest");
        Check(far.Scale < near.Scale && Mathf.Abs(near.Scale - 1f) < 0.02f && Mathf.Abs(far.Scale - 0.85f) < 0.02f,
            "The far side is drawn slightly smaller for depth");
        Check(Mathf.Abs(profile.OrbitAt(90).Offset.x - right.Offset.x) < 0.0001f && profile.OrbitAt(91).Offset != right.Offset,
            "Position depends only on the tick age and repeats each period");
        Check(Mathf.Abs(right.Heading - 0f) < 1f && Mathf.Abs(left.Heading - 180f) < 1f,
            "The sprite faces along the orbit tangent: north at the right extreme, south at the left");
        Check(!profile.ConfigErrors().Any()
            && new GuFxProfileDef { defName = "BadOrbit", texPath = "x", orbit = true, orbitPeriodTicks = 1 }.ConfigErrors().Any(),
            "Orbit profiles validate their period and radii");
        Console.WriteLine("PASS: Liquor Worm conversion, ceiling, binding, session persistence and orbit layering (managed).");
    }

    private static void AdvanceTo(Hediff_Aperture aperture, RankOneStage target)
    {
        while (aperture.Stage < target)
        {
            var stage = aperture.Stage;
            for (int pulse = 0; pulse < 1000 && !aperture.CanAdvanceRankOne; pulse++)
            {
                for (int tick = 0; tick < Hediff_Aperture.NourishmentPulseTicks; tick++) aperture.TickWallNourishment();
                aperture.RecoverEssence();
            }
            Check(aperture.TryAdvanceRankOne(stage), $"Fixture advanced from {stage} through ordinary nourishment");
        }
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
        Console.WriteLine("PASS: " + message);
    }
}
