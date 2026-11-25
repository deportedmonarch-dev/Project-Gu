using System.Text;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using ProjectGu.Underground;
using RimWorld;
using UnityEngine;
using Verse;

/// <summary>
/// Rendered Alpha 2 inspection. Loads the Alpha 2 save in a windowed game, walks the Gu Master to the surface through
/// the ordinary passage, then plays Moonblade casts in eight directions, at three game speeds and several camera
/// positions, with effects on and off. The game is paused and stepped one tick per rendered frame around each event so
/// screenshots show exact moments. Gameplay assertions still run; the screenshots are for human inspection.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha2Visual => GenCommandLine.CommandLineArgPassed("gualpha2visual");
    private static IEnumerator<Func<bool>> visualScript;
    private static readonly StringBuilder visualLog = new();
    private static string shotFolderName = "alpha2-screenshots";
    private static string ShotFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, shotFolderName);

    private static void StartAlpha2Visual()
    {
        Directory.CreateDirectory(ShotFolder);
        visualScript = VisualScript().GetEnumerator();
    }

    /// <summary>Called every rendered frame. Advances the script while its current wait condition holds.</summary>
    private static void UpdateAlpha2Visual()
    {
        if (visualScript == null) return;
        try
        {
            while (true)
            {
                var wait = visualScript.Current;
                if (wait != null && !wait()) return;
                if (!visualScript.MoveNext())
                {
                    visualScript = null;
                    File.WriteAllText(Path.Combine(ShotFolder, "visual-log.txt"), visualLog.ToString());
                    AssertNoErrors("alpha2-visual");
                    Report("PASS visual");
                    UnityEngine.Application.Quit();
                    return;
                }
            }
        }
        catch (Exception error)
        {
            visualScript = null;
            File.WriteAllText(Path.Combine(ShotFolder, "visual-log.txt"), visualLog + "\nFAIL " + error);
            Report($"FAIL visual {error}");
            UnityEngine.Application.Quit();
        }
    }

    private static Func<bool> Frames(int count)
    {
        int target = Time.frameCount + count;
        return () => Time.frameCount >= target;
    }

    private static void Note(string line) => visualLog.AppendLine($"[tick {Find.TickManager.TicksGame}, frame {Time.frameCount}] {line}");

    private static IEnumerable<Func<bool>> Shot(string name)
    {
        string path = Path.Combine(ShotFolder, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Note($"screenshot {name}");
        yield return Frames(4);
    }

    private static IEnumerable<Func<bool>> StepTicks(int ticks, int framesPerTick = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            Find.TickManager.DoSingleTick();
            yield return Frames(framesPerTick);
        }
    }

    private static IEnumerable<Func<bool>> StepUntil(Func<bool> done, int limit, string message)
    {
        for (int i = 0; i < limit && !done(); i++)
        {
            Find.TickManager.DoSingleTick();
            yield return Frames(1);
        }
        Check(done(), message);
    }

    private static IEnumerable<Func<bool>> VisualScript()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        Find.TickManager.Pause();
        var (state, disciple, aperture, record) = LoadAlpha2State();
        Map cave = disciple.Map;

        // Walk to the surface through the ordinary passage, rendering as we go.
        var exit = cave.listerThings.AllThings.OfType<Building_GuPocketProbeEndpoint>().Single();
        Order(disciple, exit, "Use underground passage");
        foreach (var wait in StepUntil(() => disciple.Map == Home, 4000, "The Gu Master climbed to the surface")) yield return wait;
        Map home = Home;
        Current.Game.CurrentMap = home;
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);
        disciple.drafter.Drafted = true; // A drafted Gu Master holds position between casts.
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 16f);
        yield return Frames(20);
        Note($"Surface: disciple at {disciple.Position}, map {home.Size}, hour {GenLocalDate.HourOfDay(home)}, aperture essence {aperture.Essence:0.0}/{aperture.MaxEssence:0.0}");
        foreach (var wait in Shot("00-selected-with-command")) yield return wait;

        // Targeting: native targeter with range ring, started from the real command.
        var command = AbilityGizmos(disciple).Single();
        Check(!command.Disabled, $"Moonblade command enabled ({command.disabledReason})");
        command.ProcessInput(null);
        yield return Frames(10);
        Check(Find.Targeter.IsTargeting, "The command opened native targeting");
        foreach (var wait in Shot("01-targeting-range-ring")) yield return wait;
        Find.Targeter.StopTargeting();
        yield return Frames(2);

        var fx = home.GetComponent<GuFxMapComponent>();
        var directions = new (string Name, IntVec3 Offset)[]
        {
            ("N", new IntVec3(0, 0, 8)), ("NE", new IntVec3(6, 0, 6)), ("E", new IntVec3(8, 0, 0)), ("SE", new IntVec3(6, 0, -6)),
            ("S", new IntVec3(0, 0, -8)), ("SW", new IntVec3(-6, 0, -6)), ("W", new IntVec3(-8, 0, 0)), ("NW", new IntVec3(-6, 0, 6))
        };
        int index = 0;
        foreach (var (name, offset) in directions)
        {
            var cell = NearestOpenCell(home, disciple.Position, disciple.Position + offset);
            if (!cell.IsValid) { Note($"direction {name}: no open cell with line of sight; skipped"); continue; }
            bool fullAnimation = index++ == 0;
            foreach (var wait in DirectedCast(disciple, aperture, record, home, fx, cell, $"{10 + index:00}-{name}", fullAnimation)) yield return wait;
        }

        // Game speeds: real-time play at Normal, Fast and Superfast.
        foreach (var speed in new[] { TimeSpeed.Normal, TimeSpeed.Fast, TimeSpeed.Superfast })
        {
            var cell = NearestOpenCell(home, disciple.Position, disciple.Position + new IntVec3(9, 0, 3));
            foreach (var wait in RealtimeCast(disciple, aperture, record, home, fx, cell, speed)) yield return wait;
        }

        // Camera: close zoom, far zoom, and off-screen (gameplay must resolve; effects must not draw).
        foreach (var (label, size) in new[] { ("zoom-close", 11f), ("zoom-far", 45f) })
        {
            Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, size);
            yield return Frames(10);
            var cell = NearestOpenCell(home, disciple.Position, disciple.Position + new IntVec3(-7, 0, 4));
            foreach (var wait in DirectedCast(disciple, aperture, record, home, fx, cell, $"30-{label}", label == "zoom-close")) yield return wait;
        }
        foreach (var wait in OffscreenCast(disciple, aperture, record, home, fx)) yield return wait;

        // Effects off: projectile still visible, no launch, trail or impact layers.
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 16f);
        ProjectGuMod.Settings.cosmeticEffects = false;
        try
        {
            var cell = NearestOpenCell(home, disciple.Position, disciple.Position + new IntVec3(0, 0, -9));
            foreach (var wait in DirectedCast(disciple, aperture, record, home, fx, cell, "40-fx-off", false)) yield return wait;
        }
        finally { ProjectGuMod.Settings.cosmeticEffects = true; }
        Note($"FX totals: spawned {fx.TotalSpawned}, retired {fx.TotalRetired}, evicted {fx.TotalEvicted}, active {fx.ActiveCount}");
        Check(fx.ActiveCount == 0, "Every cosmetic instance expired by the end of the visual run");
    }

    private static IEnumerable<Func<bool>> PrepareCast(Pawn disciple, Hediff_Aperture aperture, GuRecord record)
    {
        foreach (var wait in StepUntil(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0
            && disciple.CurJobDef?.defName != GuAbilityService.CastJobDefName, 400, "Ready to cast")) yield return wait;
        if (aperture.Essence < Moonblade.essenceCost + 1f) SetEssence(aperture, aperture.MaxEssence);
        Find.CameraDriver.JumpToCurrentMapLoc(disciple.DrawPos);
        yield return Frames(3);
    }

    /// <summary>One cast stepped tick by tick with screenshots at aim, launch, flight, impact and cleanup.</summary>
    private static IEnumerable<Func<bool>> DirectedCast(Pawn disciple, Hediff_Aperture aperture, GuRecord record, Map map,
        GuFxMapComponent fx, IntVec3 cell, string label, bool fullAnimation)
    {
        foreach (var wait in PrepareCast(disciple, aperture, record)) yield return wait;
        var target = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
        GenSpawn.Spawn(target, cell, map);
        yield return Frames(3);
        int committed = Ledger.CommittedTotal;
        float essence = aperture.Essence;
        Check(GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, target, out string reason), $"{label}: {reason ?? "ordered"}");
        foreach (var wait in StepTicks(Moonblade.warmupTicks / 2)) yield return wait;
        foreach (var wait in Shot($"{label}-a-aiming")) yield return wait;
        foreach (var wait in StepUntil(() => Ledger.CommittedTotal == committed + 1, Moonblade.warmupTicks + 5, $"{label}: launched")) yield return wait;
        var projectile = LiveProjectiles(map).Single();
        string castId = projectile.CastId;
        foreach (var wait in Shot($"{label}-b-launch")) yield return wait;
        int flight = projectile.TicksToImpact;
        foreach (var wait in StepUntil(() => projectile.Destroyed || projectile.TicksToImpact <= flight / 2, flight, $"{label}: mid-flight")) yield return wait;
        if (!projectile.Destroyed)
        {
            Note($"{label}: mid-flight heading {projectile.FxHeading:0}° at {projectile.DrawPos}; fx active {fx.ActiveCount}, draw submissions {fx.LastDrawSubmissions}");
            foreach (var wait in Shot($"{label}-c-flight")) yield return wait;
        }
        foreach (var wait in StepUntil(() => Ledger.FindResolved(castId) != null, flight + 5, $"{label}: resolved")) yield return wait;
        var cast = Ledger.FindResolved(castId);
        Note($"{label}: {cast.Outcome} damage {cast.DamageAmount} observed {cast.ObservedDamage:0.00}, paid {cast.EssencePaid:0.000} (essence {essence:0.00} -> {aperture.Essence:0.00})");
        Check(Math.Abs(cast.EssencePaid - Moonblade.essenceCost) < 0.0001f, $"{label}: paid exactly once");
        int impactShots = fullAnimation ? 8 : 2;
        for (int i = 0; i < impactShots; i++)
        {
            foreach (var wait in Shot($"{label}-d-impact-{i * (fullAnimation ? 3 : 9):00}")) yield return wait;
            foreach (var wait in StepTicks(fullAnimation ? 3 : 9)) yield return wait;
        }
        foreach (var wait in StepUntil(() => fx.ActiveCount == 0, 120, $"{label}: effects expired")) yield return wait;
        foreach (var wait in Shot($"{label}-e-clean")) yield return wait;
        if (!target.Destroyed) target.Destroy(DestroyMode.Vanish);
    }

    /// <summary>A cast played in real time at one game speed, with opportunistic screenshots.</summary>
    private static IEnumerable<Func<bool>> RealtimeCast(Pawn disciple, Hediff_Aperture aperture, GuRecord record, Map map,
        GuFxMapComponent fx, IntVec3 cell, TimeSpeed speed)
    {
        foreach (var wait in PrepareCast(disciple, aperture, record)) yield return wait;
        var target = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
        GenSpawn.Spawn(target, cell, map);
        int committed = Ledger.CommittedTotal;
        Check(GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, target, out string reason), $"{speed}: {reason ?? "ordered"}");
        int startFrame = Time.frameCount, startTick = Find.TickManager.TicksGame;
        Find.TickManager.CurTimeSpeed = speed;
        yield return () => Ledger.CommittedTotal == committed + 1 || Time.frameCount - startFrame > 2000;
        Check(Ledger.CommittedTotal == committed + 1, $"{speed}: launched in real time");
        string castId = Ledger.InFlight.Single().CastId;
        foreach (var wait in Shot($"20-speed-{speed}-flight")) yield return wait;
        yield return () => Ledger.FindResolved(castId) != null || Time.frameCount - startFrame > 3000;
        foreach (var wait in Shot($"20-speed-{speed}-impact")) yield return wait;
        int resolvedTick = Find.TickManager.TicksGame;
        yield return () => fx.ActiveCount == 0 || Time.frameCount - startFrame > 4000;
        Find.TickManager.Pause();
        var cast = Ledger.FindResolved(castId);
        Check(cast != null && fx.ActiveCount == 0, $"{speed}: resolved and cleaned up in real time");
        Note($"{speed}: {cast.Outcome}, {resolvedTick - startTick} ticks from order to impact over {Time.frameCount - startFrame} frames; effects cleared by tick {Find.TickManager.TicksGame}");
        if (!target.Destroyed) target.Destroy(DestroyMode.Vanish);
    }

    /// <summary>Camera far from the action: gameplay resolves, effects are culled and still expire by ticks.</summary>
    private static IEnumerable<Func<bool>> OffscreenCast(Pawn disciple, Hediff_Aperture aperture, GuRecord record, Map map, GuFxMapComponent fx)
    {
        foreach (var wait in PrepareCast(disciple, aperture, record)) yield return wait;
        var cell = NearestOpenCell(map, disciple.Position, disciple.Position + new IntVec3(8, 0, -2));
        var target = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
        GenSpawn.Spawn(target, cell, map);
        IntVec3 corner = disciple.Position.x < map.Size.x / 2 ? new IntVec3(map.Size.x - 10, 0, 10) : new IntVec3(10, 0, 10);
        Find.CameraDriver.SetRootPosAndSize(corner.ToVector3Shifted(), 9f);
        yield return Frames(10);
        int committed = Ledger.CommittedTotal;
        Check(GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, target, out string reason), $"offscreen: {reason ?? "ordered"}");
        foreach (var wait in StepUntil(() => Ledger.CommittedTotal == committed + 1, Moonblade.warmupTicks + 5, "offscreen: launched")) yield return wait;
        string castId = Ledger.InFlight.Single().CastId;
        int maxSubmissions = 0;
        for (int i = 0; i < 200 && Ledger.FindResolved(castId) == null; i++)
        {
            Find.TickManager.DoSingleTick();
            yield return Frames(1);
            maxSubmissions = Math.Max(maxSubmissions, fx.LastDrawSubmissions);
        }
        var cast = Ledger.FindResolved(castId);
        Check(cast is { Outcome: GuCastOutcome.HitThing }, $"offscreen: the cast resolved as a hit ({cast?.Outcome})");
        Check(maxSubmissions == 0, $"offscreen: no effect was drawn while the camera was away ({maxSubmissions})");
        foreach (var wait in StepUntil(() => fx.ActiveCount == 0, 120, "offscreen: effects expired by ticks")) yield return wait;
        Note($"offscreen: {cast.Outcome}, observed {cast.ObservedDamage:0.00}, max draw submissions {maxSubmissions}");
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 16f);
        yield return Frames(10);
        foreach (var wait in Shot("35-offscreen-returned-clean")) yield return wait;
        if (!target.Destroyed) target.Destroy(DestroyMode.Vanish);
    }

    private static IntVec3 NearestOpenCell(Map map, IntVec3 origin, IntVec3 desired) =>
        GenRadial.RadialCellsAround(desired, 4f, true).Where(cell => cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map)
            && cell.GetFirstPawn(map) == null && cell.DistanceTo(origin) >= 4f && cell.DistanceTo(origin) <= Moonblade.range - 1f
            && GenSight.LineOfSight(origin, cell, map, true)).OrderBy(cell => cell.DistanceTo(desired)).FirstOrDefault(IntVec3.Invalid);
}
