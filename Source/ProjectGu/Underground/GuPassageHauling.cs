using System;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ProjectGu.Underground
{
    [DefOf]
    public static class GuPassageHaulDefOf
    {
        public static JobDef GU_PassageHaulTravel;
        public static JobDef GU_PassageHaulPack;
        public static JobDef GU_PassageHaulStore;

        static GuPassageHaulDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(GuPassageHaulDefOf));
    }

    public sealed class GuPassageHaul : IExposable
    {
        internal Pawn Worker;
        internal bool Enabled;
        internal Thing Packed;
        internal int NextCheckTick;
        /// <summary>Index of the cargo kind that gets first claim on the next trip. It advances after every pack, so one plentiful
        /// kind (primeval stone) can never starve the others (moon orchid petals).</summary>
        internal int NextCargo;

        internal Thing Cargo => Packed?.holdingOwner != null
            && Packed.holdingOwner == Worker?.inventory?.GetDirectlyHeldThings() ? Packed : null;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Packed = Cargo;
                if (Worker?.Destroyed == true) Worker = null;
            }
            Scribe_References.Look(ref Worker, "worker");
            Scribe_Values.Look(ref Enabled, "enabled");
            Scribe_References.Look(ref Packed, "packed");
            Scribe_Values.Look(ref NextCheckTick, "nextCheckTick");
            Scribe_Values.Look(ref NextCargo, "nextCargo");
        }
    }

    public sealed class WorkGiver_GuPassageHaul : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn) => GuPassageHauling.Plan(pawn);
    }

    internal static class GuPassageHauling
    {
        private const int CheckInterval = 150;
        private const int EmptyReturnCooldown = 1200;
        private static GuPocketProbeComponent Registry => Current.Game?.GetComponent<GuPocketProbeComponent>();

        internal static bool Supports(GuIndependentSite site) => site.Kind?.haulingCargo?.Count > 0;

        internal static bool CanAssign(GuIndependentSite site, Pawn pawn) => pawn?.IsColonistPlayerControlled == true
            && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)
            && (Registry.HaulingSiteFor(pawn) is not { } other || other == site || other.Hauling.Cargo == null)
            && (site.Hauling?.Worker == pawn || site.Hauling?.Cargo == null);

        internal static void Assign(GuIndependentSite site, Pawn pawn)
        {
            if (!CanAssign(site, pawn)) return;
            if (Registry.HaulingSiteFor(pawn) is { } other && other != site) other.Hauling = null;
            if (site.Hauling?.Worker != pawn) site.Hauling = new GuPassageHaul { Worker = pawn };
            SetEnabled(site.Hauling, true);
        }

        internal static void SetEnabled(GuPassageHaul hauling, bool enabled)
        {
            hauling.Enabled = enabled;
            if (enabled) hauling.NextCheckTick = 0;
        }

        internal static GuIndependentSite ActiveSite(Pawn pawn)
        {
            if (pawn?.Spawned != true || !pawn.IsColonistPlayerControlled || pawn.Drafted || pawn.Downed
                || pawn.GetLord() != null || pawn.inventory == null || pawn.carryTracker == null
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                || pawn.workSettings?.WorkIsActive(WorkTypeDefOf.Hauling) != true)
                return null;

            var site = Registry?.HaulingSiteFor(pawn);
            return site?.Hauling.Enabled == true && Supports(site) ? site : null;
        }

        private static bool TryRoute(GuIndependentSite site, out GuIndependentEndpoint surface, out GuIndependentEndpoint below)
        {
            surface = site.Endpoints.Find(end => end.IsSurface);
            below = site.DestinationFor(surface?.EndpointId);
            return GuUndergroundLifecycleGuards.Installed && surface != null && below is { IsSurface: false }
                && site.DestinationFor(below.EndpointId) == surface
                && GuPocketProbeComponent.ValidateIndependentEndpoints(site, surface, below);
        }

        private static bool PassageReady(Pawn pawn, GuIndependentEndpoint from, GuIndependentEndpoint to) =>
            JobDriver_GuPocketProbeTravel.CanUsePassage(pawn, from.Anchor, out _)
            && !from.Anchor.IsForbidden(pawn) && !to.Anchor.IsForbidden(pawn.Faction)
            && JobDriver_GuPocketProbeTravel.CanLand(from.Anchor.Map, from.Anchor.Position, pawn)
            && JobDriver_GuPocketProbeTravel.CanLand(to.Anchor.Map, to.Anchor.Position, pawn);

        internal static Job Plan(Pawn pawn)
        {
            var site = ActiveSite(pawn);
            if (site == null || pawn.carryTracker.CarriedThing != null || Registry.OperationInProgress) return null;

            var haul = site.Hauling;
            int tick = Find.TickManager.TicksGame;
            if (tick < haul.NextCheckTick) return null;
            haul.NextCheckTick = tick + CheckInterval;

            Thing cargo = haul.Cargo;
            if (cargo != null && site.Endpoints.Exists(end => end.IsSurface && end.Resolve(site) == pawn.Map))
                return TryStorage(pawn, pawn.Map, pawn.Position, cargo, out IntVec3 cell, out _)
                    ? JobMaker.MakeJob(GuPassageHaulDefOf.GU_PassageHaulStore, cargo, cell) : null;
            if (!TryRoute(site, out var surface, out var below)) return null;

            bool onSurface = pawn.Map == surface.Anchor.Map;
            var from = onSurface ? surface : below;
            var to = onSurface ? below : surface;
            if (!PassageReady(pawn, from, to) || !pawn.CanReserveAndReach(from.Anchor, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return null;

            if (cargo == null && TryPickup(site, pawn, below.Anchor, surface.Anchor, out var source, out int count))
            {
                if (!onSurface)
                {
                    var pack = JobMaker.MakeJob(GuPassageHaulDefOf.GU_PassageHaulPack, source);
                    pack.count = count;
                    return pack;
                }
            }
            else if (onSurface) return null;

            return JobMaker.MakeJob(GuPassageHaulDefOf.GU_PassageHaulTravel, from.Anchor);
        }

        private static bool TryPickup(GuIndependentSite site, Pawn pawn, Building_GuPocketProbeEndpoint below,
            Building_GuPocketProbeEndpoint surface, out Thing source, out int count)
        {
            Map map = below.Map;
            bool local = pawn.Map == map;
            var kinds = site.Kind.haulingCargo;
            int start = site.Hauling?.NextCargo ?? 0;
            for (int k = 0; k < kinds.Count; k++)
            {
                ThingDef def = kinds[((start % kinds.Count) + kinds.Count + k) % kinds.Count];
                foreach (Thing item in map.listerThings.ThingsOfDef(def))
                {
                    if (!item.Spawned || item.Fogged() || item.IsForbidden(pawn.Faction) || item.IsBurning()
                        || StoreUtility.IsInValidStorage(item)) continue;
                    int load = LoadLimit(pawn, item);
                    if (load <= 0) continue;
                    if (local ? item.IsForbidden(pawn) || !HaulAIUtility.PawnCanAutomaticallyHaul(pawn, item, false)
                        : map.reservationManager.IsReservedByAnyoneOf(item, pawn.Faction)
                            || !ReachableFromArrival(pawn, map, below.Position, item, PathEndMode.ClosestTouch))
                        continue;

                    if (!TryStorage(pawn, surface.Map, surface.Position, item, out _, out int room)) break;
                    source = item;
                    count = Math.Min(load, room);
                    return true;
                }
            }
            source = null;
            count = 0;
            return false;
        }

        private static int LoadLimit(Pawn pawn, Thing item) => Math.Min(item.stackCount,
            Math.Min(pawn.carryTracker.AvailableStackSpace(item.def), MassUtility.CountToPickUpUntilOverEncumbered(pawn, item)));

        // RimWorld reachability requires its Pawn argument to be on the map being queried.
        // The work leg checks native Pawn-specific reachability again after arrival.
        private static bool ReachableFromArrival(Pawn pawn, Map map, IntVec3 arrival, LocalTargetInfo target,
            PathEndMode mode) => map.reachability.CanReach(arrival, target, mode,
                TraverseMode.PassDoors, pawn.NormalMaxDanger());

        private static bool TryStorage(Pawn pawn, Map map, IntVec3 arrival, Thing item,
            out IntVec3 target, out int room)
        {
            bool local = pawn.Map == map;
            // A source stack still held by the cave map must not supply a path origin on the surface.
            bool itemLocal = item.MapHeld == map;
            foreach (SlotGroup group in map.haulDestinationManager.AllGroupsListInPriorityOrder)
            {
                if (!group.Settings.AllowedToAccept(item)) continue;
                foreach (IntVec3 cell in group.CellsList)
                {
                    int space = StorageRoom(map, cell, item, pawn, local && itemLocal, checkReservation: true);
                    if (space <= 0 || (!itemLocal && !ReachableFromArrival(pawn, map, arrival, cell, PathEndMode.ClosestTouch))
                        || (local && cell.IsForbidden(pawn))) continue;
                    target = cell;
                    room = space;
                    return true;
                }
            }
            target = IntVec3.Invalid;
            room = 0;
            return false;
        }

        private static int StorageRoom(Map map, IntVec3 cell, Thing item, Pawn pawn, bool localPath, bool checkReservation)
        {
            if (!cell.InBounds(map) || cell.Fogged(map)
                || !StoreUtility.IsGoodStoreCell(cell, map, item, localPath ? pawn : null, checkReservation ? pawn.Faction : null))
                return 0;
            return cell.GetItemStackSpaceLeftFor(map, item.def);
        }

        internal static bool TryTravel(Pawn pawn, Building_GuPocketProbeEndpoint origin)
        {
            var site = ActiveSite(pawn);
            var registry = Registry;
            if (site == null || registry.OperationInProgress || !TryRoute(site, out var surface, out var below)) return false;
            bool outbound = origin == surface.Anchor;
            var from = outbound ? surface : below;
            var to = outbound ? below : surface;
            if (from.Anchor != origin || !PassageReady(pawn, from, to)
                || (outbound && (site.Hauling.Cargo != null || !TryPickup(site, pawn, below.Anchor, surface.Anchor, out _, out _))))
                return false;

            registry.OperationInProgress = true;
            try
            {
                if (!JobDriver_GuPocketProbeTravel.TryCross(pawn, origin, to.Anchor,
                    () => GuPocketProbeComponent.ValidateIndependentEndpoints(site, from, to), out _)) return false;
                if (!outbound && site.Hauling.Cargo == null)
                    site.Hauling.NextCheckTick = Find.TickManager.TicksGame + EmptyReturnCooldown;
                return true;
            }
            finally
            {
                registry.OperationInProgress = false;
            }
        }

        internal static bool TryPack(Pawn pawn, Job job)
        {
            var site = ActiveSite(pawn);
            Thing source = job.targetA.Thing;
            if (site == null || site.Hauling.Cargo != null || pawn.carryTracker.CarriedThing != null
                || !TryRoute(site, out var surface, out var below) || !PassageReady(pawn, below, surface)
                || source?.Map != pawn.Map || !site.Kind.haulingCargo.Contains(source.def)
                || source.IsForbidden(pawn) || source.IsBurning() || StoreUtility.IsInValidStorage(source)
                || !pawn.Map.reservationManager.ReservedBy(source, pawn, job)
                || !TryStorage(pawn, surface.Anchor.Map, surface.Anchor.Position, source, out _, out int room)) return false;

            int count = Math.Min(job.count, Math.Min(LoadLimit(pawn, source), room));
            if (count <= 0 || pawn.carryTracker.TryStartCarry(source, count, false) <= 0) return false;
            Thing carried = pawn.carryTracker.CarriedThing;
            var hands = pawn.carryTracker.GetDirectlyHeldThings();
            var inventory = pawn.inventory.GetDirectlyHeldThings();
            if (hands.TryTransferToContainer(carried, inventory, carried.stackCount, out Thing packed, false) <= 0)
                return false;
            site.Hauling.Packed = packed;
            // Rotate first claim to the next cargo kind after this one.
            var kinds = site.Kind.haulingCargo;
            site.Hauling.NextCargo = (kinds.IndexOf(packed.def) + 1) % kinds.Count;
            return true;
        }

        internal static bool TryStore(Pawn pawn, Thing cargo, IntVec3 cell, Job job)
        {
            var site = ActiveSite(pawn);
            Map map = pawn.Map;
            if (site == null || cargo == null || site.Hauling.Cargo != cargo
                || !site.Endpoints.Exists(end => end.IsSurface && end.Resolve(site) == map)
                || !map.reservationManager.ReservedBy(cell, pawn, job) || cell.IsForbidden(pawn)
                || map.haulDestinationManager.SlotGroupAt(cell)?.Settings.AllowedToAccept(cargo) != true) return false;

            int count = Math.Min(cargo.stackCount, StorageRoom(map, cell, cargo, pawn, localPath: false, checkReservation: false));
            return count > 0 && pawn.inventory.GetDirectlyHeldThings().TryDrop(cargo, cell, map, ThingPlaceMode.Direct, count, out _);
        }
    }
}
