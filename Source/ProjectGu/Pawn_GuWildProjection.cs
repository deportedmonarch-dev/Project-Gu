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
            if (!releaseSuppressed) base.TickInterval(delta);
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
