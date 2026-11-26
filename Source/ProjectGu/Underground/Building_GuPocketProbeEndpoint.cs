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

        public override string LabelNoCount => Spawned
            ? Map.IsPocketMap ? "return passage to surface"
                : Map.Parent is GuUndergroundLevelParent ? "underground passage" : "underground entrance"
            : base.LabelNoCount;

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            base.DeSpawn(mode);
            Current.Game?.GetComponent<GuPocketProbeComponent>()?.NotifyEndpointRemoved(this);
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
                yield return option;
            bool independent = GuPocketProbeComponent.TryGetIndependentEndpoint(this, out var haulSite, out var endpoint);
            if (independent && GuPassageHauling.Supports(haulSite) && selPawn.IsColonistPlayerControlled)
            {
                yield return new FloatMenuOption("Prioritize underground hauling", () => GuPassageHauling.Prioritize(haulSite, selPawn));
                if (GuPassageHauling.CanAssign(haulSite, selPawn))
                    yield return new FloatMenuOption("Assign to underground hauling", () => GuPassageHauling.Assign(haulSite, selPawn));
            }
            if (!Prefs.DevMode && !(independent && haulSite.Kind?.playerTravel == true))
                yield break;
            if (!GuPocketProbeComponent.TryGetSite(this, out GuPocketSiteRecord site) && !independent)
                yield break;

            if (JobDriver_GuPocketProbeTravel.CanTravel(selPawn, this, out string reason))
            {
                JobDef travel = DefDatabase<JobDef>.GetNamed("GU_PocketProbeTravel");
                string label = independent ? endpoint.IsSurface ? "Enter underground level" : "Use underground passage"
                    : site.Entrance == this ? site.Kind == GuPocketSiteDef.SpiritSpring
                        ? "Enter spirit spring cavern" : "Enter probe cavern" : "Return to surface";
                yield return new FloatMenuOption(label,
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
            if (GuPocketProbeComponent.TryGetIndependentEndpoint(this, out var haulSite, out _) && GuPassageHauling.Supports(haulSite))
            {
                var hauling = GuPassageHauling.HaulingFor(haulSite);
                string worker = hauling.Worker?.LabelShort ?? "the assigned colonist";
                yield return new Command_Toggle
                {
                    defaultLabel = "Underground hauling",
                    defaultDesc = (hauling.Automatic
                        ? "Allow any eligible colonist to collect underground cargo using their Hauling work priority. "
                        : $"Allow {worker} to collect underground cargo using their Hauling work priority. ")
                        + "Pausing keeps any packed cargo in the colonist's inventory.",
                    isActive = () => hauling.Enabled,
                    toggleAction = () => GuPassageHauling.SetEnabled(hauling, !hauling.Enabled)
                };
                if (!hauling.Automatic)
                    yield return new Command_Action
                    {
                        defaultLabel = "Allow any hauler",
                        defaultDesc = "Allow any eligible colonist to take the next underground hauling trip.",
                        action = () => GuPassageHauling.UseAnyWorker(haulSite)
                    };
            }
            if (Prefs.DevMode && (GuPocketProbeComponent.TryGetSite(this, out GuPocketSiteRecord site)
                && site.Entrance == this && !site.GenerationStarted
                || GuPocketProbeComponent.TryGetIndependentEndpoint(this, out var independent, out var end)
                && end.IsSurface && independent.Levels.Exists(level => !level.GenerationStarted)))
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "Reject next generation",
                    defaultDesc = "Reject the next newly generated cavern to test failure cleanup.",
                    isActive = () => RejectNextGeneration,
                    toggleAction = () => RejectNextGeneration = !RejectNextGeneration
                };
            }
        }

        public override string GetInspectString()
        {
            if (GuPocketProbeComponent.TryGetIndependentEndpoint(this, out var independent, out var end))
                return $"{independent.Kind.label}\nSite {independent.SiteId}\nEndpoint {end.EndpointId}"
                    + (end.LevelId == null ? "" : $"\nLevel {end.LevelId}");

            if (!GuPocketProbeComponent.TryGetSite(this, out GuPocketSiteRecord site))
                return base.GetInspectString();

            string destination = site.Entrance == this ? site.Kind?.label ?? "Underground cavern" : "Surface settlement";
            return Prefs.DevMode
                ? $"{destination} · {site.Status}\nSite {site.SiteId}\nLevel {site.LevelId}"
                : $"{destination} · {site.Status}";
        }

        internal bool ConsumeGenerationRejection()
        {
            bool reject = RejectNextGeneration;
            RejectNextGeneration = false;
            return reject;
        }

        [DebugAction("Project GU", "Place pocket probe entrance", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlaceProbeEntrance() => PlaceEntrance(GuPocketSiteDef.Probe);

        [DebugAction("Project GU", "Place spirit spring cavern entrance", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlaceSpringEntrance() => PlaceEntrance(GuPocketSiteDef.SpiritSpring);

        private static void PlaceEntrance(GuPocketSiteDef kind)
        {
            if (!Prefs.DevMode || TrySpawnEntrance(kind, Find.CurrentMap, UI.MouseCell()) == null)
                Messages.Message("Place the entrance on clear, unfogged ground in your surface settlement.",
                    MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>True when a new entrance of this kind may occupy the cell on a surface settlement map.</summary>
        internal static bool CanPlaceEntrance(GuPocketSiteDef kind, Map map, IntVec3 cell) =>
            kind?.IsConfigured == true && (!kind.independentOwner || GuUndergroundLifecycleGuards.Installed)
            && GuPocketProbeComponent.IsSurfaceSettlement(map)
            && cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map) && cell.GetThingList(map).Count == 0
            && HasEntranceClearance(map, cell, kind.entranceClearance);

        /// <summary>Spawns and registers one entrance. Shared by the developer tool and scenario starts.</summary>
        internal static Building_GuPocketProbeEndpoint TrySpawnEntrance(GuPocketSiteDef kind, Map map, IntVec3 cell)
        {
            if (!CanPlaceEntrance(kind, map, cell)) return null;
            var entrance = GenSpawn.Spawn(kind.endpoint, cell, map) as Building_GuPocketProbeEndpoint;
            if (entrance?.Spawned != true || entrance.Map != map) return null;
            var registry = Current.Game.GetComponent<GuPocketProbeComponent>();
            if (kind.independentOwner) registry.AddIndependentSite(kind, entrance);
            else registry.AddSite(kind, entrance);
            return entrance;
        }

        private static bool HasEntranceClearance(Map map, IntVec3 center, int clearance)
        {
            if (clearance == 0) return true;
            foreach (IntVec3 cell in CellRect.CenteredOn(center, clearance).Cells)
            {
                if (!cell.InBounds(map) || cell.Fogged(map) || !cell.Standable(map)
                    || cell.GetTerrain(map).IsWater || cell.GetEdifice(map) != null)
                    return false;
                foreach (Thing thing in cell.GetThingList(map))
                    if (thing is Blueprint or Frame || thing.def.category == ThingCategory.Building)
                        return false;
            }
            return true;
        }
    }
}
