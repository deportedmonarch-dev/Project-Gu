using ProjectGu;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using ProjectGu.Underground;
using RimWorld;
using UnityEngine;
using Verse;

/// <summary>
/// Rendered Alpha 3 inspection of the orbiting Liquor Worm manifestation. Loads the Alpha 3 save in a windowed game,
/// walks the Gu Master to the surface, waits out the cooldown, and channels Liquor Worm refinement. One full orbit is
/// stepped a tick per frame for each pawn facing so screenshots show the worm passing behind and in front of the body.
/// Further shots cover zoom levels, game speeds, interruption cleanup and effects off. Gameplay assertions still run.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static void StartAlpha3Visual()
    {
        shotFolderName = "alpha3-screenshots";
        Directory.CreateDirectory(ShotFolder);
        visualScript = Alpha3VisualScript().GetEnumerator();
    }

    private static IEnumerable<Func<bool>> Alpha3VisualScript()
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        Find.TickManager.Pause();
        var (_, disciple, aperture, liquor) = LoadAlpha3State();
        Map cave = disciple.Map;

        var exit = cave.listerThings.AllThings.OfType<Building_GuPocketProbeEndpoint>().Single();
        Order(disciple, exit, "Use underground passage");
        foreach (var wait in StepUntil(() => disciple.Map == Home, 4000, "The Gu Master climbed to the surface")) yield return wait;
        Map home = Home;
        Current.Game.CurrentMap = home;
        // Test-only shortcut: clear the cooldown left by the headless stages instead of waiting it out. Running tens of
        // thousands of ticks between rendered frames leaves RimWorld's pawn draw positions stale and colonists undrawn.
        Check(Ledger.CooldownRemaining(liquor.Id, Find.TickManager.TicksGame) > 0, "The headless stages left the Liquor Worm recovering");
        ((Dictionary<string, int>)typeof(GuCombatLedger).GetField("cooldownUntil", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(Ledger)).Remove(liquor.Id);
        SetEssence(aperture, aperture.MaxEssence);
        var fx = home.GetComponent<GuFxMapComponent>();
        Find.Selector.ClearSelection();
        Find.Selector.Select(disciple, false, false);

        foreach (var wait in StartSession(disciple, liquor, "starting", fx)) yield return wait;
        var session = Ledger.ChannelFor(liquor.Id);
        var profile = LiquorRefinement.channel.manifestationFx;
        Note($"Surface: disciple at {disciple.Position}, hour {GenLocalDate.HourOfDay(home)}, session {session.SessionId}, Initial {aperture.EssenceOfQuality(RankOneStage.Initial):0.##}");
        foreach (var wait in Shot("00-command-and-channel")) yield return wait;

        // One full orbit per facing, close zoom, stepped one tick per frame.
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 11f);
        yield return Frames(10);
        foreach (var rotation in new[] { Rot4.South, Rot4.East, Rot4.North, Rot4.West })
        {
            disciple.Rotation = rotation;
            int step = profile.orbitPeriodTicks / 8;
            for (int shot = 0; shot < 8; shot++)
            {
                var instance = fx.FindKeyed(session.SessionId);
                Check(instance != null && fx.CountKeyed(session.SessionId) == 1, "Exactly one manifestation while channeling");
                var pose = instance.PoseAt(Find.TickManager.TicksGame);
                Note($"{rotation.ToStringHuman()} {shot}: {(pose.Front ? "front" : "back")} heading {pose.Heading:0} offset {pose.Position - disciple.DrawPos}");
                foreach (var wait in Shot($"1{rotation.AsInt}-{rotation.ToStringHuman()}-{shot}-{(pose.Front ? "front" : "back")}")) yield return wait;
                foreach (var wait in StepTicks(step)) yield return wait;
                disciple.Rotation = rotation;
            }
        }

        // Zoom levels.
        foreach (var (label, size) in new[] { ("zoom-default", 18f), ("zoom-far", 32f) })
        {
            Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, size);
            yield return Frames(10);
            foreach (var wait in Shot($"20-{label}")) yield return wait;
        }

        // Real-time play at three speeds: the orbit follows game ticks.
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 11f);
        foreach (var speed in new[] { TimeSpeed.Normal, TimeSpeed.Fast, TimeSpeed.Superfast })
        {
            int startTick = Find.TickManager.TicksGame, startFrame = Time.frameCount;
            Find.TickManager.CurTimeSpeed = speed;
            yield return Frames(40);
            foreach (var wait in Shot($"30-speed-{speed}")) yield return wait;
            Find.TickManager.Pause();
            Note($"{speed}: {Find.TickManager.TicksGame - startTick} ticks over {Time.frameCount - startFrame} frames; pulses {session.PulsesDone}/{session.PulseCount}");
            if (session.Complete || Ledger.ChannelFor(liquor.Id) == null) break;
        }

        // Interruption removes the manifestation immediately.
        if (Ledger.ChannelFor(liquor.Id) is { Active: true })
        {
            disciple.drafter.Drafted = true;
            yield return Frames(3);
            Check(fx.CountKeyed(session.SessionId) == 0 && !session.Active, "Drafting removed the manifestation at once");
            foreach (var wait in Shot("40-interrupted-clean")) yield return wait;
            disciple.drafter.Drafted = false;

            // Effects off: resume, refinement continues, no manifestation is drawn.
            ProjectGuMod.Settings.cosmeticEffects = false;
            try
            {
                int pulses = session.PulsesDone;
                Check(GuChannelService.TryOrderChannel(disciple, liquor.Id, LiquorRefinement, out string reason), reason ?? "resumed");
                foreach (var wait in StepUntil(() => session.PulsesDone > pulses || Ledger.ChannelFor(liquor.Id) == null, 300, "A pulse completed with effects off"))
                    yield return wait;
                Check(fx.CountKeyed(session.SessionId) == 0 && fx.LastDrawSubmissions == 0, "No manifestation exists or is drawn with effects off");
                foreach (var wait in Shot("41-effects-off-channeling")) yield return wait;
            }
            finally { ProjectGuMod.Settings.cosmeticEffects = true; }
        }

        foreach (var wait in StepUntil(() => Ledger.ChannelFor(liquor.Id) == null, 3000, "The session completed")) yield return wait;
        yield return Frames(3);
        Check(fx.CountKeyed(session.SessionId) == 0, "Completion removed the manifestation");
        foreach (var wait in Shot("50-completed-clean")) yield return wait;
        Note($"Done: Initial {aperture.EssenceOfQuality(RankOneStage.Initial):0.##}, Middle {aperture.EssenceOfQuality(RankOneStage.Middle):0.##}; fx spawned {fx.TotalSpawned}, retired {fx.TotalRetired}, active {fx.ActiveCount}");
    }

    private static IEnumerable<Func<bool>> StartSession(Pawn disciple, GuRecord liquor, string label, GuFxMapComponent fx)
    {
        Check(GuChannelService.TryOrderChannel(disciple, liquor.Id, LiquorRefinement, out string reason), $"{label}: {reason ?? "ordered"}");
        foreach (var wait in StepTicks(2)) yield return wait;
        var session = Ledger.ChannelFor(liquor.Id);
        Check(session is { Active: true } && fx.CountKeyed(session.SessionId) == 1, $"{label}: the session is active with one manifestation");
        Find.CameraDriver.SetRootPosAndSize(disciple.DrawPos, 11f);
        foreach (var wait in StepTicks(12)) yield return wait;
    }
}
