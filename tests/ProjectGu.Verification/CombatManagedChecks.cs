using ProjectGu;
using ProjectGu.Combat;
using ProjectGu.Presentation;
using Verse;

/// <summary>
/// Stub-free managed checks of the Alpha 2 combat contract that do not need a running map: exact essence accounting,
/// the ledger's once-only commitment and resolution, its Scribe round trip, cooldown arithmetic and the cosmetic
/// timing math. Native fixtures cover projectiles, damage, jobs, save/load in flight and FX scheduling.
/// </summary>
internal static class CombatManagedChecks
{
    internal static void Run(string directory)
    {
        VerifyEssenceAccounting();
        VerifyLedger(directory);
        VerifyFxTiming();
    }

    private static void VerifyEssenceAccounting()
    {
        var aperture = new Hediff_Aperture();
        aperture.Initialize(22);
        float start = aperture.Essence;
        Check(aperture.TrySpendEssence(3f) && Math.Abs(aperture.Essence - (start - 3f)) < 0.0001f,
            "A cast debit removes exactly the ability cost from the aperture.");
        aperture.RestoreUncommittedEssence(3f);
        Check(Math.Abs(aperture.Essence - start) < 0.0001f,
            "A launch that fails in the commit tick restores exactly the uncommitted debit.");
        aperture.RestoreUncommittedEssence(1000f);
        Check(Math.Abs(aperture.Essence - aperture.MaxEssence) < 0.0001f,
            "Restoration never raises essence above the aperture's capacity.");
        aperture.RestoreUncommittedEssence(float.NaN);
        aperture.RestoreUncommittedEssence(-5f);
        Check(Math.Abs(aperture.Essence - aperture.MaxEssence) < 0.0001f, "Invalid restorations are ignored.");
        while (aperture.TrySpendEssence(3f)) { }
        Check(aperture.Essence < 3f && !aperture.TrySpendEssence(3f), "An aperture below the cost cannot pay for a cast.");
    }

    private static void VerifyLedger(string directory)
    {
        var ledger = new GuCombatLedger(null);
        string gu = Guid.NewGuid().ToString("N");
        string castId = Guid.NewGuid().ToString("N");
        var cast = new GuCastRecord { CastId = castId, GuId = gu, EssencePaid = 3f, LaunchTick = 1000, CasterId = "Thing_Human1" };
        ledger.Commit(cast, 150);
        Check(ledger.CommittedTotal == 1 && ledger.FindInFlight(castId) == cast && ledger.CooldownRemaining(gu, 1000) == 150
            && ledger.CooldownRemaining(gu, 1149) == 1 && ledger.CooldownRemaining(gu, 1150) == 0,
            "Commitment records one in-flight cast and a per-individual cooldown measured from the launch tick.");
        bool threw = false;
        try { ledger.Commit(cast, 150); } catch (InvalidOperationException) { threw = true; }
        Check(threw && ledger.CommittedTotal == 1, "The same cast ID cannot be committed twice.");
        Check(ledger.CooldownRemaining(Guid.NewGuid().ToString("N"), 1000) == 0, "Cooldowns belong to one individual only.");

        ledger.RegisterIntent(42, new GuCastIntent { GuId = gu, CreatedTick = 990 });
        Check(ledger.IntentFor(42)?.GuId == gu && ledger.IntentCount == 1, "A pending cast job's intent is held by job ID.");

        string path = Path.Combine(directory, "gu-combat-ledger-managed.xml");
        try
        {
            var written = ledger;
            Scribe.saver.InitSaving(path, "verification");
            written.ExposeData();
            Scribe.saver.FinalizeSaving();
            var loaded = new GuCombatLedger(null);
            Scribe.loader.InitLoading(path);
            loaded.ExposeData();
            Scribe.loader.FinalizeLoading();
            Check(loaded.CommittedTotal == 1 && loaded.FindInFlight(castId) is { EssencePaid: 3f, LaunchTick: 1000 } restored
                && restored.GuId == gu && loaded.CooldownRemaining(gu, 1000) == 150 && loaded.IntentFor(42)?.GuId == gu
                && loaded.ResolvedTotal == 0,
                "Scribe round trip preserves the in-flight cast, its cooldown and the pending intent without resolving anything.");

            Check(loaded.TryClaimImpact(castId), "The first impact of a committed cast may resolve it.");
            loaded.Resolve(castId, GuCastOutcome.HitThing, "Thing_Muffalo7", 11, 11f);
            Check(loaded.FindInFlight(castId) == null && loaded.FindResolved(castId) is { Outcome: GuCastOutcome.HitThing, DamageAmount: 11 }
                && loaded.ResolvedTotal == 1 && !loaded.TryClaimImpact(castId),
                "Resolution moves the cast to history once, and any later impact claim for it is refused.");
            loaded.Resolve(castId, GuCastOutcome.HitGround, null, 0, 0f);
            Check(loaded.ResolvedTotal == 1 && loaded.FindResolved(castId).Outcome == GuCastOutcome.HitThing,
                "A duplicate terminal report cannot overwrite or double-count the outcome.");

            Scribe.saver.InitSaving(path, "verification");
            loaded.ExposeData();
            Scribe.saver.FinalizeSaving();
            var reloaded = new GuCombatLedger(null);
            Scribe.loader.InitLoading(path);
            reloaded.ExposeData();
            Scribe.loader.FinalizeLoading();
            Check(!reloaded.TryClaimImpact(castId) && reloaded.ResolvedTotal == 1 && reloaded.InFlight.Count == 0,
                "After reload a resolved cast still refuses a second impact.");

            for (int i = 0; i < GuCombatLedger.ResolvedHistoryLimit + 5; i++)
            {
                string id = Guid.NewGuid().ToString("N");
                reloaded.Commit(new GuCastRecord { CastId = id, GuId = gu, LaunchTick = 2000 + i }, 0);
                reloaded.Resolve(id, GuCastOutcome.HitGround, null, 0, 0f);
            }
            Check(reloaded.Resolved.Count == GuCombatLedger.ResolvedHistoryLimit && reloaded.InFlight.Count == 0,
                $"Resolved history stays bounded at {GuCombatLedger.ResolvedHistoryLimit} records.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void VerifyFxTiming()
    {
        var impact = new GuFxProfileDef
        {
            defName = "ManagedImpact", texPath = "x", columns = 4, rows = 2, frameCount = 8, ticksPerFrame = 3,
            lifetimeTicks = 24, fadeOutTicks = 6, startScale = 0.8f, endScale = 1.15f
        };
        Check(impact.FrameAt(0) == 0 && impact.FrameAt(2) == 0 && impact.FrameAt(3) == 1 && impact.FrameAt(23) == 7
            && impact.FrameAt(500) == 7, "Impact frames advance every three ticks and hold the last frame.");
        Check(impact.AlphaAt(0) == 1f && impact.AlphaAt(17) == 1f && impact.AlphaAt(21) == 0.5f && impact.AlphaAt(24) == 0f,
            "Impact fades out over its final six ticks.");
        Check(Math.Abs(impact.ScaleAt(0) - 0.8f) < 0.0001f && Math.Abs(impact.ScaleAt(23) - 1.15f) < 0.0001f,
            "Impact scale interpolates across its lifetime.");
        var trail = new GuFxProfileDef { defName = "ManagedTrail", texPath = "x", columns = 4, frameCount = 4, ticksPerFrame = 3, loop = true, lifetimeTicks = 600 };
        Check(trail.FrameAt(11) == 3 && trail.FrameAt(12) == 0 && trail.FrameAt(13) == 0, "Looping trail frames wrap.");
        var first = GuFxAssets.FrameUv(0, 4, 2);
        var last = GuFxAssets.FrameUv(7, 4, 2);
        Check(Math.Abs(first.xMin) < 0.0001f && Math.Abs(first.yMax - 1f) < 0.0001f && Math.Abs(first.width - 0.25f) < 0.0001f
            && Math.Abs(last.xMax - 1f) < 0.0001f && Math.Abs(last.yMin) < 0.0001f && Math.Abs(last.height - 0.5f) < 0.0001f,
            "Atlas frame 0 is the top-left cell and the last frame is the bottom-right cell.");
        Check(!impact.ConfigErrors().Any() && new GuFxProfileDef { defName = "Bad", texPath = "x", columns = 2, frameCount = 3 }.ConfigErrors().Any(),
            "Profile validation rejects frame counts larger than the atlas grid.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
