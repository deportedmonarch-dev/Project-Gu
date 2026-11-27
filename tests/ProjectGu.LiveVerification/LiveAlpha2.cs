using System.Reflection;
using System.Text.Json;
using ProjectGu;
using ProjectGu.Clan;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Presentation;
using ProjectGu.Springs;
using RimWorld;
using Verse;

/// <summary>
/// Alpha 2 native acceptance: an ordinary Southern Border clan obtains, refines and stores a Moonlight Gu, then fires
/// Moonblade at real pawns and locations. Stage "new" ends by saving while a moonblade is in flight; stage "reload"
/// resolves that projectile in a fresh process; stage "final" checks withdrawal after another fresh process.
/// </summary>
public sealed partial class LiveLifecycleChecks
{
    private static bool Alpha2 => GenCommandLine.CommandLineArgPassed("gualpha2");
    private static GuAbilityDef Moonblade => DefDatabase<GuAbilityDef>.GetNamed("GU_Moonblade");
    private static GuCombatLedger Ledger => Current.Game.GetComponent<GuCombatLedger>();
    private static string Alpha2StatePath => Path.Combine(GenFilePaths.SaveDataFolderPath, "alpha2-state.json");
    private const string MoonSpecies = "GU_MoonlightGu";

    private sealed record Alpha2State(string GuId, string CultivatorId, string ApertureId, string CastId, string TargetId,
        float TargetInjuryAtSave, float EssenceAtSave, int Committed, int Resolved, int CustodyRevision, string HabitatId,
        int TicksToImpactAtSave);

    /// <summary>Steps single ticks and tracks the aperture's essence independently of the ability code.</summary>
    private sealed class EssenceWatch
    {
        private readonly Pawn pawn;
        private readonly Hediff_Aperture aperture;
        internal float Expected;
        internal float Paid;
        internal int Commits;

        internal EssenceWatch(Pawn pawn, Hediff_Aperture aperture)
        {
            this.pawn = pawn;
            this.aperture = aperture;
            Expected = aperture.Essence;
        }

        internal void Tick()
        {
            int committed = Ledger.CommittedTotal;
            Find.TickManager.DoSingleTick();
            if (pawn.IsHashIntervalTick(250)) Expected = Math.Min(aperture.MaxEssence, Expected + aperture.RegenerationPerHour / 10f);
            int now = Find.TickManager.TicksGame;
            for (int i = committed; i < Ledger.CommittedTotal; i++)
            {
                var cast = Ledger.InFlight.Concat(Ledger.Resolved).Single(record => record.LaunchTick == now);
                Expected -= cast.EssencePaid;
                Paid += cast.EssencePaid;
                Commits++;
            }
        }

        internal void Until(Func<bool> done, int limit, string message)
        {
            for (int tick = 0; tick < limit && !done(); tick++) Tick();
            Check(done(), message);
        }

        internal void For(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++) Tick();
        }

        internal void AssertExact(string context) => Check(Math.Abs(Expected - aperture.Essence) < 0.001f,
            $"{context}: aperture essence {aperture.Essence:0.0000} matches the independent account {Expected:0.0000}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Stage "new"
    private static void ExerciseAlpha2(Pawn disciple, GuCultivatorRecord cultivator, Hediff_Aperture aperture,
        Thing springThing, CompGuSpiritSpring spring)
    {
        DebugSettings.enableStoryteller = false;
        var registry = IndividualRegistry;
        Map cave = disciple.Map;
        var springRecords = registry.Records.Where(record => record.CreationEvidence?.OwnerKind != GuSurfaceEcology.SourceKind);
        Check(!Prefs.DevMode && spring.MatureCountFor(MoonSpecies) == 2 && spring.HopeGuAvailable == 2 && !springRecords.Any(),
            "The new cavern spring holds two mature Moonlight Gu beside the Hope Gu cohort, with no individual Gu yet");

        // 1. Acquire: the ordinary spring command, capture and pickup.
        var search = springThing.GetGizmos().OfType<Command_Action>().Single(command => command.defaultLabel == "Search for Moonlight Gu");
        search.action();
        var record = springRecords.Single();
        var wild = (Pawn_GuWildProjection)registry.ProjectionOf(record);
        Check(record.SpeciesDefName == MoonSpecies && wild.kindDef.defName == "GU_WildMoonlightGu" && wild.Map == cave
            && record.CohortDebitCommitted && spring.MatureCountFor(MoonSpecies) == 1 && spring.HopeGuAvailable == 2 && record.Refinement == null,
            $"One wild Moonlight Gu {record.Id} emerged, debiting exactly one Moonlight adult and no Hope Gu");
        Order(disciple, wild, "Capture Moonlight Gu (unrefined)");
        TickUntil(() => registry.ProjectionOf(record) is Thing_GuDormantProjection, 2400, "The capture job produced a dormant Moonlight Gu");
        var dormant = (Thing_GuDormantProjection)registry.ProjectionOf(record);
        Check(dormant.def.defName == "GU_DormantMoonlightGu" && dormant.GuId == record.Id, "The dormant form is the Moonlight Gu Thing of the same identity");
        Order(disciple, dormant, $"Pick up {dormant.Label}");
        TickUntil(() => dormant.holdingOwner == disciple.inventory.GetDirectlyHeldThings(), 2400, "The disciple picked up the dormant Moonlight Gu");

        // Acceptance 1: an unrefined Moonlight Gu grants nothing, in inventory or anywhere else.
        string unrefinedReason = null;
        Check(!GuAbilityService.GrantsFor(disciple).Any() && !AbilityGizmos(disciple).Any()
            && !GuAbilityService.CanCast(disciple, record.Id, Moonblade, out _, out unrefinedReason)
            && !GuApertureGizmos.BuildOptions(disciple).Any(option => option.Label.StartsWith("Store ", StringComparison.Ordinal)),
            $"Acceptance 1: the unrefined Gu cannot cast or be stored ({unrefinedReason})");

        // 2. Refine through the ordinary aperture menu. Fixture setup tops up essence so refinement cannot starve.
        SetEssence(aperture, aperture.MaxEssence);
        RunAlpha1ApertureOption(disciple, "Enforce will over", record.Id, "essence");
        TickUntil(() => record.Refinement != null, 30_000, "Will-enforcement over the Moonlight Gu completed");
        Check(record.Refinement.CultivatorId == cultivator.CultivatorId && record.RefinementProcess == null
            && !GuAbilityService.GrantsFor(disciple).Any(),
            "Refinement bonded the Gu to the disciple; a refined Gu still grants nothing outside the aperture");
        RunAlpha1ApertureOption(disciple, "Store ", record.Id);
        var occupants = registry.ApertureOccupants(aperture).ToList();
        var projected = ActuallyProjectedGu()
            .Where(thing => registry.FindRecord(thing.GuTag()?.GuId)?.CreationEvidence?.OwnerKind != GuSurfaceEcology.SourceKind).ToList();
        Check(occupants.Count == 1 && occupants[0] == record && dormant.Destroyed && projected.Count == 0,
            $"The refined Moonlight Gu is stored in the disciple's aperture with no physical projection (occupants {occupants.Count}, "
            + $"dormant destroyed {dormant.Destroyed}, projected: {string.Join(", ", projected.Select(thing => $"{thing.def.defName} {thing.GetUniqueLoadID()} spawned={thing.Spawned} destroyed={thing.Destroyed} gu={thing.GuTag()?.GuId}"))})");

        // Acceptance 2: the exact refined Gu grants Moonblade to its legitimate user only.
        var grant = GuAbilityService.GrantsFor(disciple).Single();
        var command = AbilityGizmos(disciple).Single();
        Pawn elder = Clan(GuClanRole.Elder).Single();
        Check(grant.Gu == record && grant.Ability == Moonblade && grant.Aperture == aperture
            && command.GuId == record.Id && !command.Disabled && !GuAbilityService.GrantsFor(elder).Any()
            && !GuAbilityService.CanCast(elder, record.Id, Moonblade, out _, out _),
            "Acceptance 2: the stored Gu offers an enabled Moonblade command to the disciple and nothing to the elder");
        Report($"Acquired and refined Moonlight Gu {record.Id}; disciple aptitude {aperture.AptitudeGrade} {aperture.Aptitude}, max essence {aperture.MaxEssence:0.0}.");

        SetEssence(aperture, Math.Min(aperture.MaxEssence, 15f));
        Check(aperture.TryUpgradeEssence(RankOneStage.Initial, 14) && aperture.Stage == RankOneStage.Initial,
            "The combat acceptance cast is funded by a mixture of Initial and Middle essence");
        var watch = new EssenceWatch(disciple, aperture);
        var fx = cave.GetComponent<GuFxMapComponent>();
        Check(fx != null && GuFxMapComponent.EffectsEnabled, "The cavern has a presentation component and cosmetic effects are on");

        // Acceptance 3 and 5: one debit, one projectile, one impact with real native damage.
        var target = SpawnTarget(cave, disciple.Position, 7, 10);
        int fxBefore = fx.TotalSpawned;
        var first = CastAndResolve(disciple, record, target, watch, "FX on");
        Check(first.Cast.Outcome == GuCastOutcome.HitThing && first.Cast.HitThingId == target.GetUniqueLoadID()
            && first.Cast.DamageAmount == 11 && first.Observed > 0f && Math.Abs(first.Cast.EssencePaid - Moonblade.essenceCost) < 0.0001f,
            $"Acceptance 3/5: one cast paid {first.Cast.EssencePaid:0.000} essence and dealt {first.Observed:0.00} observed damage to {target.LabelShort}");
        Check(fx.TotalSpawned - fxBefore == 3, $"Launch, travel and impact effects were scheduled ({fx.TotalSpawned - fxBefore})");
        watch.AssertExact("After the first cast");
        Check(aperture.EssenceOfQuality(RankOneStage.Middle) is > 11 and < 14,
            "The first Moonblade consumed both qualities at face value for the same three-unit charge");

        // Acceptance 10 (cooldown and repeat use): the same Gu recovers before it can cast again.
        Check(!GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, target, out string cooldown) && cooldown.Contains("recovering")
            && AbilityGizmos(disciple).Single().Disabled, $"The Gu is recovering immediately after its cast ({cooldown})");
        watch.Until(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0, Moonblade.cooldownTicks, "The cooldown elapsed");
        watch.For(Math.Max(0, 700 - (Find.TickManager.TicksGame - first.Cast.LaunchTick)));
        Check(fx.ActiveCount == 0 && fx.TotalRetired + fx.TotalEvicted == fx.TotalSpawned,
            $"Acceptance 10: every cosmetic instance expired ({fx.TotalSpawned} spawned, {fx.TotalRetired} retired)");
        DespawnTarget(target);

        // Acceptance 8: identical gameplay with cosmetic effects disabled.
        ProjectGuMod.Settings.cosmeticEffects = false;
        try
        {
            target = SpawnTarget(cave, disciple.Position, 7, 10);
            fxBefore = fx.TotalSpawned;
            var plain = CastAndResolve(disciple, record, target, watch, "FX off");
            Check(plain.Cast.Outcome == GuCastOutcome.HitThing && plain.Cast.DamageAmount == first.Cast.DamageAmount
                && Math.Abs(plain.Cast.EssencePaid - first.Cast.EssencePaid) < 0.0001f && plain.Observed > 0f
                && fx.TotalSpawned == fxBefore && fx.ActiveCount == 0,
                $"Acceptance 8: with effects off the cast paid {plain.Cast.EssencePaid:0.000} and dealt {plain.Cast.DamageAmount} ({plain.Observed:0.00} observed), scheduling no effects");
        }
        finally { ProjectGuMod.Settings.cosmeticEffects = true; }
        DespawnTarget(target);
        watch.Until(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0, Moonblade.cooldownTicks, "Cooldown before interruption tests");
        watch.AssertExact("After the effects-off cast");

        // Acceptance 4: interruptions and invalid states before launch cost nothing and start no cooldown.
        target = SpawnTarget(cave, disciple.Position, 7, 10);
        AssertFreeCancellation(disciple, record, target, watch, "drafting during aim", () => disciple.drafter.Drafted = true);
        disciple.drafter.Drafted = false;
        Thing_GuDormantProjection withdrawn = null;
        AssertFreeCancellation(disciple, record, target, watch, "the Gu withdrawn during aim", () =>
        {
            Check(registry.TryWithdrawDormant(record.Id, record.CustodyRevision, disciple, aperture, out withdrawn, out string why), why ?? "withdrawn");
            Check(!GuAbilityService.GrantsFor(disciple).Any() && !AbilityGizmos(disciple).Any(), "A withdrawn Gu stops granting Moonblade immediately");
        });
        Check(registry.TryStoreDormant(withdrawn, withdrawn.GuRevision, disciple, aperture, out string restoreReason), restoreReason ?? "restored");
        Check(GuAbilityService.GrantsFor(disciple).Single().Gu == record, "Restoring the same Gu to the aperture restores its ability");
        var doomed = SpawnTarget(cave, disciple.Position, 7, 10);
        AssertFreeCancellation(disciple, record, doomed, watch, "the target removed during aim", () => doomed.Destroy(DestroyMode.Vanish));
        string self = null, far = null;
        Check(!GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, new LocalTargetInfo(disciple), out self)
            && !GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, new LocalTargetInfo(FarCell(cave, disciple.Position)), out far)
            && Ledger.IntentCount == 0,
            $"Acceptance 9: invalid targets are refused before any job starts ({self}; {far})");
        float low = aperture.Essence;
        SetEssence(aperture, Moonblade.essenceCost - 0.5f);
        watch.Expected = aperture.Essence;
        Check(!GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, target, out string poor) && poor.Contains("essence"),
            $"A Gu Master without enough essence cannot cast ({poor})");
        SetEssence(aperture, low);
        watch.Expected = aperture.Essence;
        watch.AssertExact("After every cancelled or refused cast");

        // Drafted combat use and a location target.
        SetEssence(aperture, Math.Min(aperture.MaxEssence, 15f));
        watch.Expected = aperture.Essence;
        disciple.drafter.Drafted = true;
        var drafted = CastAndResolve(disciple, record, target, watch, "drafted");
        Check(drafted.Cast.Outcome == GuCastOutcome.HitThing, "A drafted Gu Master can use Moonblade");
        DespawnTarget(target);
        watch.Until(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0, Moonblade.cooldownTicks, "Cooldown before the location cast");
        // Native ImpactSomething may still strike something that occupies or enters the cell; either result is one resolution.
        IntVec3 ground = FindTargetCell(cave, disciple.Position, 6, 9, emptyCell: true);
        var groundCast = CastAndResolve(disciple, record, new LocalTargetInfo(ground), watch, "location");
        Check(groundCast.Cast.Outcome is GuCastOutcome.HitGround or GuCastOutcome.HitThing
            && (groundCast.Cast.Outcome == GuCastOutcome.HitThing) == (groundCast.Cast.DamageAmount > 0),
            $"A location cast at {ground} resolved once as {groundCast.Cast.Outcome} ({groundCast.Cast.HitThingId ?? "ground"})");
        watch.Until(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0, Moonblade.cooldownTicks, "Cooldown before the vanishing-target cast");

        // Acceptance 9: the target disappears while the moonblade is in flight.
        var vanishing = SpawnTarget(cave, disciple.Position, 10, 14);
        int committed = Ledger.CommittedTotal;
        Check(GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, vanishing, out string reason), reason ?? "ordered");
        watch.Until(() => Ledger.CommittedTotal == committed + 1, Moonblade.warmupTicks + 5, "The vanishing-target cast launched");
        string vanishingCast = Ledger.InFlight.Single().CastId;
        vanishing.Destroy(DestroyMode.Vanish);
        watch.Until(() => Ledger.FindResolved(vanishingCast) != null, 200, "The projectile resolved after its target vanished");
        Check(Ledger.FindResolved(vanishingCast).Outcome is GuCastOutcome.HitGround or GuCastOutcome.HitThing && !LiveProjectiles(cave).Any(),
            $"Acceptance 9: the in-flight cast resolved safely as {Ledger.FindResolved(vanishingCast).Outcome}");
        watch.AssertExact("After the vanishing-target cast");
        disciple.drafter.Drafted = false;
        watch.Until(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0, Moonblade.cooldownTicks, "Cooldown before the save cast");

        // Acceptance 7: save while one moonblade is in flight.
        SetEssence(aperture, Math.Min(aperture.MaxEssence, 12f));
        watch.Expected = aperture.Essence;
        target = SpawnTarget(cave, disciple.Position, 14, 18);
        committed = Ledger.CommittedTotal;
        Check(GuAbilityService.TryOrderCast(disciple, record.Id, Moonblade, target, out reason), reason ?? "ordered");
        watch.Until(() => Ledger.CommittedTotal == committed + 1, Moonblade.warmupTicks + 5, "The save cast launched");
        watch.For(4);
        var projectile = LiveProjectiles(cave).Single();
        Check(!projectile.Resolved && projectile.TicksToImpact > 4 && Ledger.FindInFlight(projectile.CastId) != null,
            $"One unresolved moonblade is in flight with {projectile.TicksToImpact} ticks to impact");
        watch.AssertExact("At the in-flight save");
        var state = new Alpha2State(record.Id, cultivator.CultivatorId, aperture.ApertureId, projectile.CastId, target.GetUniqueLoadID(),
            Projectile_GuAbility.DamageProbe(target), aperture.Essence, Ledger.CommittedTotal, Ledger.ResolvedTotal, record.CustodyRevision,
            spring.HabitatId, projectile.TicksToImpact);
        File.WriteAllText(Alpha2StatePath, JsonSerializer.Serialize(state));
        GameDataSaveLoader.SaveGame(SaveName);
        Report($"Saved with moonblade {projectile.CastId} in flight; {watch.Commits} casts committed for {watch.Paid:0.000} essence; essence {aperture.Essence:0.000}.");
        AssertNoErrors("alpha2-new");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Stage "reload": fresh process, projectile still in flight.
    private static void VerifyAlpha2InFlight()
    {
        var (state, disciple, aperture, record) = LoadAlpha2State();
        Map cave = disciple.Map;
        var projectile = LiveProjectiles(cave).Single();
        var target = cave.mapPawns.AllPawnsSpawned.Single(pawn => pawn.GetUniqueLoadID() == state.TargetId);
        Check(projectile.CastId == state.CastId && !projectile.Resolved && projectile.TicksToImpact == state.TicksToImpactAtSave
            && Ledger.FindInFlight(state.CastId) is { EssencePaid: 3f } && Ledger.CommittedTotal == state.Committed
            && Ledger.ResolvedTotal == state.Resolved && Math.Abs(aperture.Essence - state.EssenceAtSave) < 0.0001f
            && Math.Abs(Projectile_GuAbility.DamageProbe(target) - state.TargetInjuryAtSave) < 0.0001f,
            "Acceptance 7: reload restored the same unresolved projectile, its in-flight ledger entry, the paid essence and the unharmed target");
        Check(cave.GetComponent<GuFxMapComponent>().Active.Any(instance => instance.Anchor == projectile),
            "The live projectile re-attached its travel accent without replaying launch or impact effects");

        // Acceptance 6: identity and eligibility survive reload.
        var grant = GuAbilityService.GrantsFor(disciple).Single();
        Check(grant.Gu == record && record.CustodyRevision == state.CustodyRevision && record.Refinement.CultivatorId == state.CultivatorId,
            "Acceptance 6: the same Gu identity, bond and aperture still grant Moonblade after reload");

        var watch = new EssenceWatch(disciple, aperture);
        int injuriesBefore = InjuryCount(target);
        watch.Until(() => Ledger.FindResolved(state.CastId) != null, 200, "The reloaded projectile resolved");
        var resolved = Ledger.FindResolved(state.CastId);
        float harmed = Projectile_GuAbility.DamageProbe(target) - state.TargetInjuryAtSave;
        int injuriesAtImpact = InjuryCount(target);
        watch.For(30);
        Check(resolved.Outcome == GuCastOutcome.HitThing && resolved.HitThingId == state.TargetId && harmed > 0f
            && injuriesAtImpact > injuriesBefore,
            $"Acceptance 7: the reloaded moonblade hit its saved target ({resolved.Outcome}, {resolved.HitThingId} vs {state.TargetId}, {harmed:0.00} observed, injuries {injuriesBefore} -> {injuriesAtImpact})");
        Check(InjuryCount(target) == injuriesAtImpact && !LiveProjectiles(cave).Any(),
            $"Acceptance 7: no second impact after reload (injuries {injuriesAtImpact} -> {InjuryCount(target)}, projectiles {LiveProjectiles(cave).Count()})");
        Check(Ledger.CommittedTotal == state.Committed && Ledger.ResolvedTotal == state.Resolved + 1 && watch.Commits == 0,
            $"Acceptance 7: no new debit or commitment after reload (committed {state.Committed} -> {Ledger.CommittedTotal}, resolved {state.Resolved} -> {Ledger.ResolvedTotal}, watch commits {watch.Commits})");
        watch.AssertExact("After the reloaded impact");
        DespawnTarget(target);

        // Repeat use after reload, then withdraw the Gu for the final stage.
        watch.Until(() => Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0, Moonblade.cooldownTicks, "Cooldown after reload");
        target = SpawnTarget(cave, disciple.Position, 7, 10);
        var again = CastAndResolve(disciple, record, target, watch, "after reload");
        Check(again.Cast.Outcome == GuCastOutcome.HitThing, "Moonblade works again after reload");
        DespawnTarget(target);
        watch.AssertExact("After the post-reload cast");
        Check(IndividualRegistry.TryWithdrawDormant(record.Id, record.CustodyRevision, disciple, aperture, out var withdrawn, out string why), why ?? "withdrawn");
        Check(!GuAbilityService.GrantsFor(disciple).Any() && withdrawn.GuId == record.Id, "Withdrawal ends the grant before saving");
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha2-reload");
    }

    // Stage "final": the withdrawn Gu keeps its identity and bond but grants nothing until stored again.
    private static void VerifyAlpha2Final()
    {
        var (state, disciple, aperture, _) = LoadAlpha2State(stored: false);
        var record = IndividualRegistry.FindRecord(state.GuId);
        var thing = disciple.inventory.GetDirectlyHeldThings().OfType<Thing_GuDormantProjection>().Single();
        Check(thing.GuId == state.GuId && IndividualRegistry.Resolve(thing) == record && record.Refinement.CultivatorId == state.CultivatorId
            && IndividualRegistry.ApertureClaimCount(aperture) == 0 && !GuAbilityService.GrantsFor(disciple).Any()
            && !GuAbilityService.CanCast(disciple, state.GuId, Moonblade, out _, out _) && !AbilityGizmos(disciple).Any(),
            "A withdrawn Gu keeps its identity and bond across reload but grants no ability");
        Check(IndividualRegistry.TryStoreDormant(thing, thing.GuRevision, disciple, aperture, out string why), why ?? "stored");
        Check(GuAbilityService.GrantsFor(disciple).Single().Gu == record, "Storing it again restores Moonblade");
        Report($"Ledger totals: {Ledger.CommittedTotal} committed, {Ledger.ResolvedTotal} resolved, {Ledger.InFlight.Count} in flight.");
        GameDataSaveLoader.SaveGame(SaveName);
        AssertNoErrors("alpha2-final");
    }

    // ---------------------------------------------------------------------------------------------------------------
    private readonly record struct CastResult(GuCastRecord Cast, float Observed);

    private static CastResult CastAndResolve(Pawn caster, GuRecord record, LocalTargetInfo target, EssenceWatch watch, string label)
    {
        Map map = caster.Map;
        int committed = Ledger.CommittedTotal;
        int resolvedTotal = Ledger.ResolvedTotal;
        float before = Projectile_GuAbility.DamageProbe(target.Thing);
        Check(GuAbilityService.TryOrderCast(caster, record.Id, Moonblade, target, out string reason), $"{label}: {reason ?? "ordered"}");
        Check(caster.CurJobDef?.defName == GuAbilityService.CastJobDefName, $"{label}: the cast job started");
        watch.Until(() => Ledger.CommittedTotal == committed + 1, Moonblade.warmupTicks + 5, $"{label}: launched after aiming");
        var cast = Ledger.InFlight.Single();
        var projectile = LiveProjectiles(map).Single();
        Check(projectile.CastId == cast.CastId && projectile.GuId == record.Id && cast.GuId == record.Id
            && Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) > 0 && Ledger.IntentCount == 0,
            $"{label}: exactly one projectile carries the committed cast and the cooldown started");
        watch.Until(() => Ledger.FindResolved(cast.CastId) != null, 300, $"{label}: the projectile resolved");
        float observed = Projectile_GuAbility.DamageProbe(target.Thing) - before;
        int injuries = InjuryCount(target.Thing);
        watch.For(30);
        Check(Ledger.ResolvedTotal == resolvedTotal + 1 && Ledger.CommittedTotal == committed + 1 && !LiveProjectiles(map).Any()
            && InjuryCount(target.Thing) == injuries,
            $"{label}: one impact, no lingering projectile and no new injury afterward (injuries {injuries} -> {InjuryCount(target.Thing)})");
        Report($"Cast {label}: {cast.Outcome}, paid {cast.EssencePaid:0.000}, damage {cast.DamageAmount}, observed {observed:0.00}, launch tick {cast.LaunchTick}, resolved tick {cast.ResolvedTick}.");
        return new CastResult(Ledger.FindResolved(cast.CastId), observed);
    }

    private static void AssertFreeCancellation(Pawn caster, GuRecord record, Thing target, EssenceWatch watch, string label, Action interrupt)
    {
        int committed = Ledger.CommittedTotal;
        Check(GuAbilityService.TryOrderCast(caster, record.Id, Moonblade, target, out string reason), $"{label}: {reason ?? "ordered"}");
        watch.For(Moonblade.warmupTicks / 2);
        Check(caster.CurJobDef?.defName == GuAbilityService.CastJobDefName && Ledger.CommittedTotal == committed, $"{label}: still aiming");
        interrupt();
        watch.For(Moonblade.warmupTicks + 10);
        Check(Ledger.CommittedTotal == committed && !LiveProjectiles(caster.Map).Any() && Ledger.IntentCount == 0
            && Ledger.CooldownRemaining(record.Id, Find.TickManager.TicksGame) == 0 && caster.CurJobDef?.defName != GuAbilityService.CastJobDefName,
            $"Acceptance 4: {label} cancelled the cast with no debit, projectile or cooldown");
        watch.AssertExact(label);
    }

    private static (Alpha2State, Pawn, Hediff_Aperture, GuRecord) LoadAlpha2State(bool stored = true)
    {
        Prefs.DevMode = false;
        DebugSettings.enableStoryteller = false;
        var state = JsonSerializer.Deserialize<Alpha2State>(File.ReadAllText(Alpha2StatePath));
        Pawn disciple = Clan(GuClanRole.Disciple).Single(pawn => Cultivators.FindCultivator(pawn)?.CultivatorId == state.CultivatorId);
        var aperture = Apertures(disciple).Single();
        var record = stored ? IndividualRegistry.Resolve(state.GuId) : IndividualRegistry.FindRecord(state.GuId);
        Check(aperture.ApertureId == state.ApertureId && record is { IsActionable: true } && record.SpeciesDefName == MoonSpecies
            && IndividualRegistry.Records.Count(entry => entry.CreationEvidence?.OwnerKind != GuSurfaceEcology.SourceKind) == 1
            && record.CohortDebitCommitted,
            "Reload kept the disciple's aperture and the single Moonlight Gu identity");
        if (stored) Check(IndividualRegistry.ApertureOccupants(aperture).Single() == record, "The Gu is still in the disciple's aperture");
        return (state, disciple, aperture, record);
    }

    private static int InjuryCount(Thing thing) => thing is Pawn pawn ? pawn.health.hediffSet.hediffs.Count(hediff => hediff is Hediff_Injury) : 0;

    private static IEnumerable<Command_GuAbility> AbilityGizmos(Pawn pawn) => pawn.GetGizmos().OfType<Command_GuAbility>();

    private static IEnumerable<Projectile_GuAbility> LiveProjectiles(Map map) =>
        map.listerThings.AllThings.OfType<Projectile_GuAbility>().Where(projectile => projectile.Spawned);

    private static void SetEssence(Hediff_Aperture aperture, float value)
    {
        double volume = Enum.GetValues<RankOneStage>().Sum(aperture.EssenceOfQuality);
        while (volume < value)
        {
            aperture.RecoverEssence();
            volume = Enum.GetValues<RankOneStage>().Sum(aperture.EssenceOfQuality);
        }
        if (volume > value) Check(aperture.TrySpendEssence(volume - value), "Adjusted fixture essence via the aperture");
    }

    private static Pawn SpawnTarget(Map map, IntVec3 origin, int minDistance, int maxDistance)
    {
        var target = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
        GenSpawn.Spawn(target, FindTargetCell(map, origin, minDistance, maxDistance), map);
        return target;
    }

    private static void DespawnTarget(Thing target)
    {
        if (target is { Destroyed: false }) target.Destroy(DestroyMode.Vanish);
    }

    private static IntVec3 FindTargetCell(Map map, IntVec3 origin, int minDistance, int maxDistance, bool emptyCell = false)
    {
        var cell = GenRadial.RadialCellsAround(origin, maxDistance, true).Where(candidate => candidate.InBounds(map)
            && (!emptyCell || candidate.GetThingList(map).Count == 0)
            && candidate.DistanceTo(origin) >= minDistance && candidate.Standable(map) && !candidate.Fogged(map)
            && candidate.GetFirstPawn(map) == null && candidate.GetFirstItem(map) == null
            && GenSight.LineOfSight(origin, candidate, map, true)).OrderBy(candidate => candidate.DistanceTo(origin)).FirstOrDefault(IntVec3.Invalid);
        Check(cell.IsValid, $"No open cell with line of sight {minDistance}-{maxDistance} cells from {origin}");
        return cell;
    }

    private static IntVec3 FarCell(Map map, IntVec3 origin) =>
        map.AllCells.Where(cell => cell.DistanceTo(origin) > Moonblade.range + 2).OrderBy(cell => cell.DistanceTo(origin)).First();
}
