using System;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Record-only mortal aperture containment keyed by the transitional durable aperture ID (RFC-0004 §15.5).
    /// The Development Mode fixture keeps one occupant per aperture. The container model supports many.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        /// <summary>Provisional developer fixture capacity. Final capacity is a later balance decision.</summary>
        internal const int DevelopmentApertureCapacity = 1;

        /// <summary>Claims that occupy an aperture, including unresolved ones, which keep their place.</summary>
        private int ApertureClaims(string apertureId) => apertureId == null ? 0 : records.Count(record => record != null
            && record.Existence == GuExistence.Living && !record.IsQuarantined && record.Placement == GuPlacementKind.Contained
            && record.ContainerKind == GuContainerKind.MortalAperture && record.ContainerId == apertureId);

        internal bool TryStoreDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn host, Hediff_Aperture aperture,
            out string reason)
        {
            reason = "The Gu is not an authorized dormant manifestation, or the request is stale.";
            if (!CanUseDevelopmentCustody || Resolve(thing) is not { } record || record.Condition != GuCondition.Dormant
                || record.CustodyRevision != expectedRevision || expectedRevision == int.MaxValue)
                return false;

            reason = "Choose the living inventory holder's own aperture.";
            if (aperture == null || aperture.pawn != host || !host.IsFreeColonist || InventoryOf(host) is not { } inventory
                || !IsMapOwned(host) || !IsInInventory(thing, inventory) || aperture.def != GuWorldComponent.ApertureDef
                || !host.health.hediffSet.hediffs.Contains(aperture)) return false;
            string apertureId = aperture.EnsureApertureId();
            reason = "This aperture has no free capacity.";
            if (ApertureClaims(apertureId) >= DevelopmentApertureCapacity) return false;

            Map map = host.Map;
            IntVec3 position = host.Position;
            string hostId = host.GetUniqueLoadID();
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "Aperture storage could not stage the original Gu.";
                try
                {
                    if (!inventory.Remove(thing) || !IsDetached(thing) || inventory.Any(item => item == thing)
                        || !MatchesManifestation(record, thing) || InventoryOf(host) != inventory || !IsMapOwned(host)
                        || host.Map != map || aperture.pawn != host || !host.health.hediffSet.hediffs.Contains(aperture)) return false;
                }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] A native callback interrupted aperture storage. {exception}");
                    return false;
                }

                record.CommitContainment(GuContainerKind.MortalAperture, apertureId, hostId);
                BindAperture(record, aperture);
                committed = true;
                if (!DestroyApertureTransferThing(thing))
                {
                    // The stale Thing keeps the old revision and load ID, so it can never authorize.
                    MarkRogue(thing.GuTag(), "Aperture storage committed but the former Thing could not be retired.");
                    reason = "Aperture custody committed. The former Thing could not be retired and is recorded as a rogue projection.";
                }
                else reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (!committed) RestorePhysicalMove(record, thing, host, inventory, map, position, false);
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        internal bool TryWithdrawDormant(string id, int expectedRevision, Pawn host, Hediff_Aperture aperture,
            out Thing_GuDormantProjection withdrawn, out string reason)
        {
            withdrawn = null;
            reason = "The Gu's aperture containment or revision is unavailable.";
            if (!CanUseDevelopmentCustody || Resolve(id) is not { } record || record.Placement != GuPlacementKind.Contained
                || record.CustodyRevision != expectedRevision || expectedRevision == int.MaxValue
                || !apertureOf.TryGetValue(record.Id, out var bound) || bound != aperture || aperture.pawn != host)
                return false;

            reason = "The aperture host must be a free colonist with a native inventory on a loaded map.";
            if (!host.IsFreeColonist || InventoryOf(host) is not { } inventory || !IsMapOwned(host)) return false;
            var def = record.Species?.dormantProjectionDef;
            reason = "This species has no configured dormant inventory projection.";
            if (!Thing_GuDormantProjection.IsConfigured(def) || record.Condition != GuCondition.Dormant) return false;

            Map map = host.Map;
            Thing_GuDormantProjection staged = null;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                try
                {
                    staged = (Thing_GuDormantProjection)ThingMaker.MakeThing(def);
                    staged.InitializeBinding(record);
                    reason = "The aperture host's inventory has no room for the Gu.";
                    if (!IsLiveAperture(aperture, record) || InventoryOf(host) != inventory || !CanPack(host, inventory, staged))
                        return false;
                    reason = "Native withdrawal did not establish the expected inventory placement.";
                    if (!inventory.TryAdd(staged, false) || !IsInInventory(staged, inventory) || !IsLiveAperture(aperture, record)
                        || InventoryOf(host) != inventory || !IsMapOwned(host) || host.Map != map
                        || staged.Destroyed || staged.def != def || staged.stackCount != 1
                        || staged.GuId != record.Id || staged.GuRevision != expectedRevision) return false;
                }
                catch (Exception exception)
                {
                    reason = "A native callback interrupted aperture withdrawal.";
                    Log.Warning($"[Project GU] {reason} {exception}");
                    return false;
                }

                record.CommitManifestation(GuManifestationKind.Thing, staged.GetUniqueLoadID());
                staged.InitializeBinding(record);
                BindProjection(record, staged);
                committed = true;
                withdrawn = staged;
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (!committed && staged != null && !DestroyApertureTransferThing(staged))
                        MarkRogue(staged.GuTag(), "A failed withdrawal could not retire its provisional Thing.");
                    if (!committed && !IsBoundAperture(aperture, record))
                        ApplyApertureState(record, host, Find.TickManager.TicksGame);
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        private static bool DestroyApertureTransferThing(Thing_GuDormantProjection thing)
        {
            if (thing == null) return true;
            try
            {
                if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            }
            catch (Exception exception)
            {
                Log.Warning($"[Project GU] Aperture transfer could not retire Thing {thing.GetUniqueLoadID()}. {exception}");
            }
            return thing.Destroyed && !thing.Spawned && thing.holdingOwner == null;
        }
    }
}
