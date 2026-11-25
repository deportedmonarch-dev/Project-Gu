using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    public sealed class JobDriver_GuNourishAperture : JobDriver
    {
        internal static JobDef NourishJob => DefDatabase<JobDef>.GetNamedSilentFail("GU_NourishAperture");

        private string apertureId;
        private Hediff_Aperture aperture;

        internal static Hediff_Aperture FindCultivatingAperture(Pawn host) =>
            host is { IsColonistPlayerControlled: true, Drafted: false }
            && Current.Game?.GetComponent<GuIndividualRegistry>() is { } registry
            && registry.TryGetAbilityCaster(host, out _, out var aperture) && aperture.Rank == 1 ? aperture : null;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            TryBind() && aperture.CanNourishWalls;

        private bool TryBind()
        {
            aperture = FindCultivatingAperture(pawn);
            if (aperture == null || (apertureId != null && aperture.ApertureId != apertureId)) return false;
            apertureId = aperture.ApertureId;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var cultivate = ToilMaker.MakeToil("GuNourishAperture");
            cultivate.defaultCompleteMode = ToilCompleteMode.Never;
            cultivate.initAction = () => pawn.pather?.StopDead();
            cultivate.tickAction = () =>
            {
                if ((aperture == null && !TryBind()) || aperture.ApertureId != apertureId
                    || !pawn.Spawned || !pawn.IsColonistPlayerControlled || pawn.Drafted || pawn.Downed
                    || !pawn.health.hediffSet.hediffs.Contains(aperture))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (!aperture.TickWallNourishment())
                {
                    Messages.Message("The aperture needs to recover essence. Choose Nourish aperture walls to resume.",
                        new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (!aperture.CanAdvanceRankOne) return;
                Messages.Message($"{pawn.LabelShort}'s aperture is ready. Advance to {aperture.Stage + 1} from the Aperture tab or Manage aperture Gu.",
                    new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, false);
                EndJobWith(JobCondition.Succeeded);
            };
            cultivate.WithProgressBar(TargetIndex.None, () => aperture == null || aperture.WallNourishmentRequired == 0
                ? 0f : (float)(aperture.WallNourishment / aperture.WallNourishmentRequired));
            yield return cultivate;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref apertureId, "guNourishmentApertureId");
        }
    }
}
