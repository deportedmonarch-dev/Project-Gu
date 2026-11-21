using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        private readonly Dictionary<GuCustody, (GuIndividual Record, Hediff_Aperture Endpoint)> byAperture = new();

        private static GuCustody ApertureCustody(Hediff_Aperture aperture) =>
            new(GuCustodyKind.Aperture, aperture.GetUniqueLoadID(), aperture.pawn?.GetUniqueLoadID());

        private static Hediff_Aperture FindExactAperture(Pawn host, string apertureId)
        {
            if (host is not { Dead: false, Destroyed: false, Discarded: false, IsFreeColonist: true }
                || host.health?.hediffSet == null) return null;

            Hediff_Aperture match = null;
            foreach (Hediff hediff in host.health.hediffSet.hediffs)
            {
                if (hediff is not Hediff_Aperture aperture || aperture.GetUniqueLoadID() != apertureId) continue;
                if (match != null || aperture.def != GuWorldComponent.ApertureDef || aperture.pawn != host || aperture.loadID < 0)
                    return null;
                match = aperture;
            }
            return match;
        }

        private static bool IsLiveAperture(Hediff_Aperture aperture, GuCustody custody) =>
            aperture != null && ApertureCustody(aperture) == custody
            && FindExactAperture(aperture.pawn, custody.NativeOwnerId) == aperture;

        private void BindAperture(GuIndividual record, Hediff_Aperture aperture)
        {
            byId.Add(record.Id, (record, null));
            byAperture.Add(record.Custody, (record, aperture));
        }

        private void UnbindAperture(GuIndividual record)
        {
            byId.Remove(record.Id);
            byAperture.Remove(record.Custody);
        }

        private void QuarantineAperture(GuIndividual record, string reason)
        {
            record.Quarantine(reason);
            UnbindAperture(record);
        }

        private GuIndividual ResolveApertureBinding(GuIndividual record)
        {
            if (!record.IsQuarantined && byAperture.TryGetValue(record.Custody, out var binding)
                && binding.Record == record && IsLiveAperture(binding.Endpoint, record.Custody))
                return record;

            QuarantineAperture(record, "The bound aperture or host is no longer live.");
            return null;
        }

        internal GuIndividual ResolveApertureOccupant(Hediff_Aperture aperture)
        {
            if (!ready || CustodyTransitionInProgress || aperture == null
                || !byAperture.TryGetValue(ApertureCustody(aperture), out var binding) || binding.Endpoint != aperture)
                return null;
            return ResolveApertureBinding(binding.Record);
        }

        internal void NotifyApertureLost(Hediff_Aperture aperture, string reason)
        {
            if (ready && !CustodyTransitionInProgress && byAperture.TryGetValue(ApertureCustody(aperture), out var binding)
                && binding.Endpoint == aperture)
                QuarantineAperture(binding.Record, reason);
        }

        public override void GameComponentTick()
        {
            // Native destruction and discard can bypass Hediff death and removal callbacks.
            if (!ready || CustodyTransitionInProgress || byAperture.Count == 0 || Find.TickManager.TicksGame % 250 != 0) return;
            foreach (var record in byAperture.Values.Where(binding => !IsLiveAperture(binding.Endpoint, binding.Record.Custody))
                .Select(binding => binding.Record).ToArray())
                QuarantineAperture(record, "The bound aperture or host is no longer live.");
        }

        internal bool TryStoreDormant(Thing_GuDormantProjection thing, int expectedRevision, Pawn host, Hediff_Aperture aperture,
            out string reason)
        {
            reason = "The Gu's inventory binding or revision is unavailable.";
            if (!CanUseDevelopmentCustody || Resolve(thing) is not { } record || record.Custody.Kind != GuCustodyKind.DormantInventory
                || record.Revision != expectedRevision || expectedRevision == int.MaxValue)
                return false;

            reason = "Choose the living inventory holder's exact aperture.";
            if (aperture == null || aperture.pawn != host || InventoryOf(host) is not { } inventory || !IsMapOwned(host)
                || !IsInInventory(thing, inventory)) return false;
            var destination = ApertureCustody(aperture);
            if (!IsLiveAperture(aperture, destination)) return false;
            reason = "This exact aperture's Gu slot is already claimed.";
            if (individuals.Any(entry => entry != null && entry.Custody == destination)) return false;
            if (RejectConflictingProjection(record, thing, out reason)) return false;

            Map map = host.Map;
            IntVec3 position = host.Position;
            bool committed = false;
            CustodyTransitionInProgress = true;
            try
            {
                reason = "Aperture storage could not stage the original Gu.";
                try
                {
                    if (!inventory.Remove(thing) || !IsDetached(thing) || inventory.Any(item => item == thing)
                        || !MatchesDormantThing(record, thing) || InventoryOf(host) != inventory || !IsMapOwned(host)
                        || host.Map != map || !IsLiveAperture(aperture, destination)) return false;
                }
                catch (Exception exception)
                {
                    Log.Warning($"[Project GU] A native callback interrupted aperture storage. {exception}");
                    return false;
                }

                Unbind(record, thing);
                record.CommitCustody(destination);
                BindAperture(record, aperture);
                committed = true;
                if (!DestroyApertureTransferThing(thing) || !IsLiveAperture(aperture, destination))
                {
                    reason = "Aperture custody committed but native cleanup or the endpoint failed. The Gu was quarantined.";
                    QuarantineAperture(record, $"{reason} Former Thing {thing.GetUniqueLoadID()}.");
                    return false;
                }
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (!committed && !RestoreInventoryTransfer(record, thing, host, inventory, map, position, false))
                        reason = "The original inventory custody could not be restored. The Gu was quarantined.";
                }
                finally { CustodyTransitionInProgress = false; }
            }
        }

        internal bool TryWithdrawDormant(string id, int expectedRevision, Pawn host, Hediff_Aperture aperture,
            out Thing_GuDormantProjection withdrawn, out string reason)
        {
            withdrawn = null;
            reason = "The Gu's aperture binding or revision is unavailable.";
            if (!CanUseDevelopmentCustody || Resolve(id) is not { } record || record.Custody.Kind != GuCustodyKind.Aperture
                || record.Revision != expectedRevision || expectedRevision == int.MaxValue
                || ResolveApertureOccupant(aperture) != record || aperture.pawn != host)
                return false;

            reason = "The aperture host must have a native inventory on a loaded map.";
            if (InventoryOf(host) is not { } inventory || !IsMapOwned(host)) return false;
            var def = record.Species?.dormantProjectionDef;
            reason = "This species has no configured dormant inventory projection.";
            if (!Thing_GuDormantProjection.IsConfigured(def)) return false;
            if (RejectConflictingProjection(record, null, out reason)) return false;

            Map map = host.Map;
            GuCustody source = record.Custody;
            Thing_GuDormantProjection staged = null;
            CustodyTransitionInProgress = true;
            try
            {
                try
                {
                    staged = (Thing_GuDormantProjection)ThingMaker.MakeThing(def);
                    staged.InitializeBinding(record);
                    reason = "The aperture host's inventory has no room for the Gu.";
                    if (!IsLiveAperture(aperture, source) || InventoryOf(host) != inventory || !CanPack(host, inventory, staged))
                        return false;
                    reason = "Native withdrawal did not establish the expected inventory custody.";
                    if (!inventory.TryAdd(staged, false) || !IsInInventory(staged, inventory) || !IsLiveAperture(aperture, source)
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

                UnbindAperture(record);
                record.CommitCustody(new(GuCustodyKind.DormantInventory, staged.GetUniqueLoadID(), source.HolderPawnId));
                staged.InitializeBinding(record);
                Bind(record, staged);
                withdrawn = staged;
                reason = null;
                return true;
            }
            finally
            {
                try
                {
                    if (withdrawn == null && (!DestroyApertureTransferThing(staged) || !IsLiveAperture(aperture, source)))
                    {
                        reason = "Withdrawal could not restore the exact aperture binding. The Gu was quarantined.";
                        QuarantineAperture(record, reason);
                    }
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
