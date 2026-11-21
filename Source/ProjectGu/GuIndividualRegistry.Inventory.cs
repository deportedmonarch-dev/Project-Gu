using System;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        internal bool TryTakeDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, out string reason) =>
            TryMoveDormant(thing, expectedRevision, pawn, thing?.Position ?? IntVec3.Invalid, taking: true, ordinary: false, out reason);

        internal bool TryDropDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, IntVec3 cell, out string reason) =>
            TryMoveDormant(thing, expectedRevision, pawn, cell, taking: false, ordinary: false, out reason);

        internal bool CanOrderDormantPickup(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn) =>
            CanUseCustody && GuGearInventoryGuard.Installed && InventoryOf(pawn) is { } inventory && IsMapOwned(pawn)
            && !pawn.Downed && pawn.IsColonistPlayerControlled && thing?.Map == pawn.Map
            && Resolve(thing) is { } record && record.Custody.Kind == GuCustodyKind.DormantGround
            && record.Revision == expectedRevision && expectedRevision != int.MaxValue && CanPack(pawn, inventory, thing)
            && pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, pawn.NormalMaxDanger());

        internal bool TryPickUpDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, out string reason) =>
            TryMoveDormant(thing, expectedRevision, pawn, thing?.Position ?? IntVec3.Invalid, taking: true, ordinary: true, out reason);

        internal bool TryDropDormantFromGear(Thing_GuDormantProjection thing, Pawn pawn, out string reason) =>
            TryMoveDormant(thing, thing.GuRevision, pawn, pawn?.Position ?? IntVec3.Invalid, taking: false, ordinary: true, out reason);

        private static ThingOwner InventoryOf(Pawn pawn) => pawn is { Dead: false, Destroyed: false, IsFreeColonist: true }
            && pawn.inventory is { } tracker && tracker.ParentHolder == pawn ? tracker.GetDirectlyHeldThings() : null;

        private static bool IsInInventory(Thing thing, ThingOwner inventory) => inventory != null && !thing.Spawned
            && thing.holdingOwner == inventory && thing.ParentHolder == inventory.Owner
            && inventory.Count(item => item == thing) == 1;

        private static bool IsOnCell(Thing thing, Map map, IntVec3 cell) =>
            IsMapOwned(thing) && thing.Map == map && thing.Position == cell && cell.GetThingList(map).Contains(thing);

        private static bool CanPack(Pawn pawn, ThingOwner inventory, Thing thing) =>
            inventory.CanAcceptAnyOf(thing, false) && MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing) >= 1;

        private bool TryMoveDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn, IntVec3 cell,
            bool taking, bool ordinary, out string reason)
        {
            reason = "The Gu's source custody or revision is unavailable.";
            if (!(ordinary ? CanUseCustody && GuGearInventoryGuard.Installed : CanUseDevelopmentCustody)
                || Resolve(thing) is not { } record || record.Revision != expectedRevision || expectedRevision == int.MaxValue
                || record.Custody.Kind != (taking ? GuCustodyKind.DormantGround : GuCustodyKind.DormantInventory))
                return false;

            reason = "The free colonist must be on the same map and within reach.";
            if (InventoryOf(pawn) is not { } inventory || !IsMapOwned(pawn)) return false;
            Map map = pawn.Map;
            bool nearDrop = ordinary && !taking;
            bool HolderReady() => IsMapOwned(pawn) && pawn.Map == map && InventoryOf(pawn) == inventory
                && (nearDrop ? pawn.Position == cell
                    : ordinary ? !pawn.Downed && pawn.IsColonistPlayerControlled && pawn.CanReachImmediate(cell, PathEndMode.ClosestTouch)
                    : pawn.Position.AdjacentToCardinal(cell));
            if (!HolderReady() || (taking ? !IsOnCell(thing, map, cell) : !IsInInventory(thing, inventory))) return false;

            bool CanTake() => CanPack(pawn, inventory, thing);
            reason = taking ? "The colonist's inventory has no room for the Gu." : "Choose an open cell for the dormant Gu.";
            if (taking ? !CanTake() : !nearDrop && !CanPlaceDormant(map, cell, null)) return false;
            if (RejectConflictingProjection(record, thing, out reason)) return false;

            string pawnId = pawn.GetUniqueLoadID();
            bool committed = false;
            IntVec3 placement = cell;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "The native inventory move did not establish the requested custody.";
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

                    if (!HolderReady() || pawn.GetUniqueLoadID() != pawnId || !MatchesDormantThing(record, thing)
                        || (taking ? !IsInInventory(thing, inventory) : !IsOnCell(thing, map, placement))) return false;
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted the Gu inventory move.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }

                // Both writes are field updates. Native callbacks cannot observe a half-committed binding.
                record.CommitCustody(new(taking ? GuCustodyKind.DormantInventory : GuCustodyKind.DormantGround,
                    record.Custody.NativeOwnerId, taking ? pawnId : null));
                thing.InitializeBinding(record);
                committed = true;
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (nearDrop && thing.Spawned && thing.Map == map) placement = thing.Position;
                    if (!committed && !RestoreInventoryTransfer(record, thing, pawn, inventory, map, placement, taking))
                        reason = "The original Gu custody could not be restored. The binding was quarantined.";
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        private static bool IsDetached(Thing thing) => !thing.Destroyed && !thing.Spawned && thing.holdingOwner == null;

        private bool RestoreInventoryTransfer(GuIndividual record, Thing_GuDormantProjection thing, Pawn pawn,
            ThingOwner inventory, Map map, IntVec3 cell, bool taking)
        {
            bool SourceRestored() => MatchesProjection(record, thing)
                && (taking ? IsOnCell(thing, map, cell) : IsInInventory(thing, inventory));

            try
            {
                if (SourceRestored()) return true;
                if (taking && IsInInventory(thing, inventory)) inventory.Remove(thing);
                else if (!taking && IsOnCell(thing, map, cell) && InventoryOf(pawn) == inventory
                    && inventory.CanAcceptAnyOf(thing, false)) thing.DeSpawnOrDeselect(DestroyMode.Vanish);

                if (!taking && IsDetached(thing) && InventoryOf(pawn) == inventory) inventory.TryAdd(thing, false);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Gu inventory rollback was interrupted. {exception}");
            }

            // Keep a detached Thing in the native save graph even if its original owner cannot accept it.
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
                    Log.Warning($"[Project GU] Gu inventory recovery placement was interrupted. {exception}");
                }
            }

            if (SourceRestored()) return true;
            record.Quarantine($"Gu inventory rollback could not restore {record.Custody.Kind}."
                + $" Observed holder: {thing.ParentHolder}; map: {thing.MapHeld?.GetUniqueLoadID()}; cell: {thing.PositionHeld}.");
            Unbind(record, thing);
            return false;
        }
    }
}
