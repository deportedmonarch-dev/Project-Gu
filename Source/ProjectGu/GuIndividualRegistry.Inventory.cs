using System;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    /// <summary>
    /// Physical movement of an authorized dormant manifestation (RFC-0004 §5, §15.1). Moving the same Thing between the
    /// ground and a Pawn inventory is not a custody transition and does not change either revision. Each move still
    /// revalidates authorization, location, reach and capacity when it executes.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        internal bool TryTakeDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, out string reason) =>
            TryMoveDormant(thing, expectedRevision, pawn, thing?.Position ?? IntVec3.Invalid, taking: true, ordinary: false, out reason);

        internal bool TryDropDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, IntVec3 cell, out string reason) =>
            TryMoveDormant(thing, expectedRevision, pawn, cell, taking: false, ordinary: false, out reason);

        internal bool CanOrderDormantPickup(Thing_GuDormantProjection thing, Pawn pawn) =>
            CanUseCustody && InventoryOf(pawn) is { } inventory && IsMapOwned(pawn) && !pawn.Downed && pawn.IsColonistPlayerControlled
            && thing?.Spawned == true && thing.Map == pawn.Map && Resolve(thing) is { } record && record.Condition == GuCondition.Dormant
            && CanPack(pawn, inventory, thing) && pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, pawn.NormalMaxDanger());

        internal bool TryPickUpDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, out string reason) =>
            TryMoveDormant(thing, expectedRevision, pawn, thing?.Position ?? IntVec3.Invalid, taking: true, ordinary: true, out reason);

        private static ThingOwner InventoryOf(Pawn pawn) => pawn is { Dead: false, Destroyed: false }
            && pawn.inventory is { } tracker && tracker.ParentHolder == pawn ? tracker.GetDirectlyHeldThings() : null;

        private static bool IsMapOwned(Thing thing) => thing?.Spawned == true && !thing.Destroyed
            && thing.Map is { Disposed: false } map && Find.Maps.Contains(map)
            && thing.holdingOwner == map.GetDirectlyHeldThings() && thing.ParentHolder == map;

        private static bool IsInInventory(Thing thing, ThingOwner inventory) => inventory != null && !thing.Spawned
            && thing.holdingOwner == inventory && thing.ParentHolder == inventory.Owner
            && inventory.Count(item => item == thing) == 1;

        private static bool IsOnCell(Thing thing, Map map, IntVec3 cell) =>
            IsMapOwned(thing) && thing.Map == map && thing.Position == cell && cell.GetThingList(map).Contains(thing);

        private static bool CanPack(Pawn pawn, ThingOwner inventory, Thing thing) =>
            inventory.CanAcceptAnyOf(thing, false) && MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing) >= 1;

        private static bool IsDetached(Thing thing) => !thing.Destroyed && !thing.Spawned && thing.holdingOwner == null;

        private bool TryMoveDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, IntVec3 cell,
            bool taking, bool ordinary, out string reason)
        {
            reason = "The Gu is not an authorized dormant manifestation, or the request is stale.";
            if (!(ordinary ? CanUseCustody : CanUseDevelopmentCustody) || Resolve(thing) is not { } record
                || record.Condition != GuCondition.Dormant || record.CustodyRevision != expectedRevision || expectedRevision == int.MaxValue)
                return false;

            // Physical preconditions are revalidated now. An unchanged revision alone is not sufficient.
            reason = "The colonist must be alive, on a loaded map and within reach.";
            if (InventoryOf(pawn) is not { } inventory || !IsMapOwned(pawn) || (ordinary && !pawn.IsColonistPlayerControlled)
                || (!ordinary && !pawn.IsFreeColonist)) return false;
            Map map = pawn.Map;
            bool nearDrop = ordinary && !taking;
            bool HolderReady() => IsMapOwned(pawn) && pawn.Map == map && InventoryOf(pawn) == inventory
                && (nearDrop ? pawn.Position == cell
                    : ordinary ? !pawn.Downed && pawn.CanReachImmediate(cell, PathEndMode.ClosestTouch)
                    : pawn.Position.AdjacentToCardinal(cell));
            if (!HolderReady() || (taking ? !IsOnCell(thing, map, cell) : !IsInInventory(thing, inventory))) return false;

            bool CanTake() => CanPack(pawn, inventory, thing);
            reason = taking ? "The colonist's inventory has no room for the Gu." : "Choose an open cell for the dormant Gu.";
            if (taking ? !CanTake() : !nearDrop && !CanPlaceDormant(map, cell, null)) return false;

            int custody = record.CustodyRevision, state = record.StateRevision;
            bool moved = false;
            IntVec3 placement = cell;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "The native move did not complete.";
                try
                {
                    if (taking)
                    {
                        thing.DeSpawnOrDeselect(DestroyMode.Vanish);
                        if (!IsDetached(thing) || !HolderReady() || !CanTake() || !inventory.TryAdd(thing, false)) return false;
                    }
                    else
                    {
                        if (!inventory.TryDrop(thing, cell, map, nearDrop ? ThingPlaceMode.Near : ThingPlaceMode.Direct, out Thing placed)
                            || placed != thing) return false;
                        if (nearDrop) placement = thing.Position;
                    }
                    if (!HolderReady() || !MatchesManifestation(record, thing)
                        || (taking ? !IsInInventory(thing, inventory) : !IsOnCell(thing, map, placement))) return false;
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted the Gu move.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }
                moved = true;
                record.NoteEncounterInteraction(Find.TickManager.TicksGame, historical: true);
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (nearDrop && thing.Spawned && thing.Map == map) placement = thing.Position;
                    if (!moved && !RestorePhysicalMove(record, thing, pawn, inventory, map, placement, taking))
                        reason = "The Gu could not be returned to its original holder. Its new location is observed natively.";
                }
                finally
                {
                    CustodyTransitionInProgress = false;
                    if (record.CustodyRevision != custody || record.StateRevision != state)
                        Log.Error($"[Project GU] A physical move changed Gu {record.Id} revisions. This is a coordinator defect.");
                }
            }
        }

        /// <summary>
        /// Returns the same Thing to its original holder after a failed move. If that is impossible the Thing stays wherever
        /// the engine placed it, and remains the authorized manifestation. Only a Thing left outside every native holder
        /// becomes unresolved, because it would not be saved.
        /// </summary>
        private bool RestorePhysicalMove(GuRecord record, Thing_GuDormantProjection thing, Pawn pawn,
            ThingOwner inventory, Map map, IntVec3 cell, bool taking)
        {
            bool SourceRestored() => taking ? IsOnCell(thing, map, cell) : IsInInventory(thing, inventory);
            try
            {
                if (SourceRestored()) return true;
                if (taking && IsInInventory(thing, inventory)) inventory.Remove(thing);
                else if (!taking && IsOnCell(thing, map, cell) && InventoryOf(pawn) == inventory
                    && inventory.CanAcceptAnyOf(thing, false)) thing.DeSpawnOrDeselect(DestroyMode.Vanish);

                if (taking && IsDetached(thing) && CanPlaceDormant(map, cell, null))
                    GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Direct, out _);
                else if (!taking && IsDetached(thing) && InventoryOf(pawn) == inventory) inventory.TryAdd(thing, false);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Gu move rollback was interrupted. {exception}");
            }

            if (IsDetached(thing))
            {
                try
                {
                    IntVec3 recovery = CanPlaceDormant(map, cell, null) ? cell : GenRadial.RadialCellsAround(cell, 6f, false)
                        .FirstOrDefault(candidate => CanPlaceDormant(map, candidate, null), IntVec3.Invalid);
                    if (recovery.IsValid) GenPlace.TryPlaceThing(thing, recovery, map, ThingPlaceMode.Direct, out _);
                }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] Gu move recovery placement was interrupted. {exception}");
                }
            }

            if (SourceRestored()) return true;
            if (IsDetached(thing))
            {
                UnbindAll(record);
                lastProjection[record.Id] = thing;
                record.MarkUnresolved(GuUnresolvedCause.ProjectionMissing, GuTerminalEvidence.None,
                    $"The Thing was left outside every native holder near {cell} on {map?.GetUniqueLoadID()}.", Find.TickManager.TicksGame);
            }
            return false;
        }
    }
}
