using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Underground
{
    public sealed class Building_GuPocketProbeEndpoint : Building
    {
        internal bool RejectNextGeneration { get; private set; }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
                yield return option;
            if (!Prefs.DevMode || !GuPocketProbeComponent.TryGetSite(this, out GuPocketSiteRecord site))
                yield break;

            if (JobDriver_GuPocketProbeTravel.CanTravel(selPawn, this, out string reason))
            {
                JobDef travel = DefDatabase<JobDef>.GetNamed("GU_PocketProbeTravel");
                yield return new FloatMenuOption(site.Entrance == this ? "Enter probe cavern" : "Return to surface",
                    () => selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(travel, this), JobTag.Misc));
            }
            else
            {
                yield return new FloatMenuOption(reason, null);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;
            if (Prefs.DevMode && GuPocketProbeComponent.TryGetSite(this, out GuPocketSiteRecord site)
                && site.Entrance == this && !site.GenerationStarted)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "Reject next generation",
                    defaultDesc = "Reject the next empty cavern after native generation, to test failure cleanup.",
                    isActive = () => RejectNextGeneration,
                    toggleAction = () => RejectNextGeneration = !RejectNextGeneration
                };
            }
        }

        public override string GetInspectString()
        {
            if (Prefs.DevMode && GuPocketProbeComponent.TryGetSite(this, out GuPocketSiteRecord site))
                return $"Probe site {site.SiteId}\nLevel {site.LevelId}\n{site.Status}";
            return base.GetInspectString();
        }

        internal bool ConsumeGenerationRejection()
        {
            bool reject = RejectNextGeneration;
            RejectNextGeneration = false;
            return reject;
        }

        [DebugAction("Project GU", "Place pocket probe entrance", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlaceEntrance()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            GuPocketSiteDef kind = GuPocketSiteDef.Probe;
            if (!Prefs.DevMode || !GuPocketProbeComponent.IsSurfaceSettlement(map)
                || !cell.InBounds(map) || cell.Fogged(map)
                || !cell.Standable(map) || cell.GetThingList(map).Count != 0 || kind?.IsConfigured != true)
            {
                Messages.Message("Place the probe on a clear, unfogged cell of your surface settlement.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var entrance = GenSpawn.Spawn(kind.endpoint, cell, map) as Building_GuPocketProbeEndpoint;
            if (entrance?.Spawned == true && entrance.Map == map)
                Current.Game.GetComponent<GuPocketProbeComponent>().AddSite(kind, entrance);
        }
    }
}
