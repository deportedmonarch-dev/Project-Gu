using System;
using System.Collections.Generic;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    /// <summary>
    /// Native body for a wild Gu. New bodies require the registry's creation path. Scribe restores existing bodies
    /// separately, and the registry validates their saved identity and custody before authorizing them.
    /// </summary>
    public class Pawn_GuWildProjection : Pawn, IGuProjection
    {
        [ThreadStatic] private static ThingDef permittedRace;
        private bool releaseSuppressed;
        private Pawn curiosityTarget;
        private int curiosityUntil;
        private int nextEvadeTick;
        private GuSpeciesDef Species => Current.Game?.GetComponent<GuIndividualRegistry>()?.Resolve(this)?.Species;
        internal bool UsesSurfaceEncounter => Species?.HasSurfacePopulation == true;

        public string GuId => this.GuTag()?.GuId;
        public int GuRevision => this.GuTag()?.GuRevision ?? 0;
        internal bool ReleaseSuppressed => releaseSuppressed;

        internal void SetReleaseSuppressed(bool suppressed) => releaseSuppressed = suppressed;

        internal void InitializeBinding(GuRecord record) => this.GuTag()?.Bind(record);

        internal static bool IsGuKind(PawnKindDef kind) =>
            kind?.race?.thingClass is { } pawnClass && typeof(Pawn_GuWildProjection).IsAssignableFrom(pawnClass);

        internal static bool HasNativeGenerationExclusions(PawnKindDef kind) =>
            kind is { canArriveManhunter: false, ecoSystemWeight: 0, isFighter: false }
            && kind.race is { tradeability: Tradeability.None } race && race.tradeTags.NullOrEmpty()
            && race.race is { petness: 0, neverIncludeInQuests: true, herdMigrationAllowed: false, packAnimal: false, hasGenders: false } properties
            && properties.wildBiomes.NullOrEmpty();

        internal static Pawn_GuWildProjection GenerateForRegistry(PawnGenerationRequest request)
        {
            if (permittedRace != null || !request.ForceGenerateNewPawn || request.Faction != null
                || !IsGuKind(request.KindDef) || !HasNativeGenerationExclusions(request.KindDef))
                throw new InvalidOperationException("Wild Gu generation requires a protected projection Def and a new, factionless Pawn.");

            permittedRace = request.KindDef.race;
            try { return (Pawn_GuWildProjection)PawnGenerator.GeneratePawn(request); }
            finally { permittedRace = null; }
        }

        public sealed override void PostMake()
        {
            // Both ThingMaker and fresh PawnGenerator requests use PostMake. Scribe does not.
            if (def == null || def != permittedRace)
                throw new InvalidOperationException("Wild Gu Pawns require Project GU registry generation. Vanilla Spawn pawn/thing cannot "
                    + "create a Gu, because every Gu body must belong to one registered individual. In Development Mode use "
                    + "Project GU > Spawn registered wild Gu..., or meet one through a spirit spring.");
            permittedRace = null;
            base.PostMake();
        }

        public override void ExposeData()
        {
            // The V1 identity keys are read and written by the manifestation comp in the same node.
            base.ExposeData();
            Scribe_Values.Look(ref releaseSuppressed, "guReleaseSuppressed");
            Scribe_References.Look(ref curiosityTarget, "guCuriosityTarget");
            Scribe_Values.Look(ref curiosityUntil, "guCuriosityUntil");
            Scribe_Values.Look(ref nextEvadeTick, "guNextEvadeTick");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                needs?.AddOrRemoveNeedsAsAppropriate();
                if (health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition) is { } malnutrition)
                    health.RemoveHediff(malnutrition);
                var designations = map.designationManager;
                if (designations.DesignationOn(this, DesignationDefOf.Hunt) is { } hunt) designations.RemoveDesignation(hunt);
                if (designations.DesignationOn(this, DesignationDefOf.Tame) is { } tame) designations.RemoveDesignation(tame);
            }
        }

        protected override void Tick()
        {
            if (!releaseSuppressed) base.Tick();
        }

        public override void TickRare()
        {
            if (!releaseSuppressed) base.TickRare();
        }

        protected override void TickInterval(int delta)
        {
            if (releaseSuppressed) return;
            base.TickInterval(delta);
            if (Spawned && Faction == null && !Downed && !InMentalState && this.IsHashIntervalTick(250, delta)
                && Species is { HasSurfacePopulation: true, wildCuriosityChancePerHour: > 0f } species)
                ObserveCuriosity(species);
        }

        private bool CuriousAbout(Pawn pawn) => pawn != null && pawn == curiosityTarget && Find.TickManager.TicksGame < curiosityUntil;

        private void ObserveCuriosity(GuSpeciesDef species)
        {
            int now = Find.TickManager.TicksGame;
            if (curiosityTarget != null && (!CuriousAbout(curiosityTarget) || !curiosityTarget.Spawned || curiosityTarget.Map != Map))
                curiosityTarget = null;
            if (curiosityTarget == null && this.IsHashIntervalTick(2500, 250)
                && species.wildCuriosityChancePerHour > 0f
                && Rand.Chance(species.wildCuriosityChancePerHour))
            {
                foreach (var pawn in Map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.Downed || pawn.Position.DistanceToSquared(Position) > 144
                        || !this.CanReach(pawn, PathEndMode.Touch, Danger.Some)) continue;
                    curiosityTarget = pawn;
                    curiosityUntil = now + 10000;
                    Messages.Message($"A wild {species.label} is lingering near {pawn.LabelShort}.", this, MessageTypeDefOf.NeutralEvent, false);
                    break;
                }
            }
            if (curiosityTarget == null || CurJob?.def == JobDefOf.Flee || !jobs.IsCurrentJobPlayerInterruptible()) return;
            if (Position.DistanceToSquared(curiosityTarget.Position) <= 16)
            {
                jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 275), JobCondition.InterruptOptional);
                return;
            }
            foreach (var cell in GenRadial.RadialCellsAround(curiosityTarget.Position, 3f, false))
            {
                if (!cell.InBounds(Map) || !cell.Standable(Map) || cell.GetFirstPawn(Map) != null
                    || !this.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, cell), JobCondition.InterruptOptional);
                break;
            }
        }

        internal void EvadeCapture(Pawn captor)
        {
            int now = Find.TickManager.TicksGame;
            if (releaseSuppressed || !Spawned || captor.Map != Map || Downed || InMentalState
                || CuriousAbout(captor) || now < nextEvadeTick || Position.DistanceToSquared(captor.Position) > 49
                || CurJob?.def == JobDefOf.Flee || !jobs.IsCurrentJobPlayerInterruptible()) return;
            nextEvadeTick = now + Rand.RangeInclusive(90, 210);
            if (!Rand.Chance(0.55f) || FleeUtility.FleeJob(this, captor, 9) is not { } flee) return;
            flee.expiryInterval = 130;
            jobs.StartJob(flee, JobCondition.InterruptOptional);
        }

        public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
        {
            this.GuTag()?.CaptureEndingLocation();
            base.Kill(dinfo, exactCulprit);
        }

        public override IEnumerable<Gizmo> GetGizmos() => releaseSuppressed ? Array.Empty<Gizmo>() : base.GetGizmos();
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            if (releaseSuppressed) yield break;
            foreach (var option in base.GetFloatMenuOptions(selPawn)) yield return option;

            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var jobDef = DefDatabase<JobDef>.GetNamedSilentFail("GU_CaptureWildGu");
            if (jobDef == null || registry == null) yield break;
            string speciesName = registry.Resolve(this)?.Species?.label ?? "wild Gu";
            if (!registry.CanOrderWildCapture(this, selPawn))
            {
                // Alpha 5B: explain a refused capture instead of offering nothing.
                string refusal = registry.WildCaptureRefusal(this, selPawn);
                if (refusal != null && registry.Resolve(this) != null && selPawn?.IsColonistPlayerControlled == true)
                    yield return new FloatMenuOption($"Cannot capture {speciesName}: {refusal}", null);
                yield break;
            }
            yield return new FloatMenuOption($"Capture {speciesName} (unrefined)", () =>
            {
                if (registry.CanOrderWildCapture(this, selPawn))
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, this));
            });
        }

        public override string GetInspectString() => GuIndividualRegistry.InspectProjection(this, base.GetInspectString());
    }
}
