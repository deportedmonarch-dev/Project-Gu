using System.Collections.Generic;
using ProjectGu.Presentation;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Jobs
{
    public sealed class FloatMenuOptionProvider_GuSearchSurface : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => false;

        protected override FloatMenuOption GetSingleOption(FloatMenuContext context)
        {
            var pawn = context.FirstSelectedPawn;
            IntVec3 cell = context.ClickedCell;
            if (pawn?.IsColonistPlayerControlled != true || !GuSurfaceEcology.SearchGround(pawn.Map, cell)
                || !pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger())) return null;
            return new FloatMenuOption("Search for wild Gu", () => pawn.jobs.TryTakeOrderedJob(
                JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("GU_SearchSurface"), cell), JobTag.Misc));
        }
    }

    public sealed class JobDriver_GuSearchSurface : JobDriver
    {
        internal const float SearchRadius = 6f;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !GuSurfaceEcology.SearchGround(pawn.Map, job.targetA.Cell));
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(300, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                var wild = GuSurfaceEcology.FindWildGu(pawn, job.targetA.Cell, SearchRadius);
                if (wild != null) Current.Game.GetComponent<GuIndividualRegistry>().NoteSurfaceSearchResult(wild);
                if (!job.playerForced || !pawn.IsColonistPlayerControlled) return;
                if (wild == null)
                {
                    Messages.Message("You found no signs of wild Gu here.", pawn, MessageTypeDefOf.NeutralEvent, false);
                    return;
                }

                CameraJumper.TryJump(wild, CameraJumper.MovementMode.Cut);
                wild.Map.GetComponent<GuFxMapComponent>().HighlightSearchResult(wild);
                Messages.Message($"{pawn.LabelShort} discovered a wild {wild.LabelShort}.", wild, MessageTypeDefOf.NeutralEvent, false);
            });
        }
    }
}
