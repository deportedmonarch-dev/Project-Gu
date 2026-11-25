using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    /// <summary>How a feeder physically reaches a Gu to feed it.</summary>
    internal enum GuFeedAccess { None = 0, Aperture = 1, Inventory = 2, Ground = 3 }

    /// <summary>
    /// Alpha 5A sustenance authority. Feeding never changes custody, identity or allegiance. Starvation death goes through
    /// the same terminal transaction as other endings, under one idempotent event key per individual.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        internal static string StarvationEventKey(string id) => "starvation:" + id;

        /// <summary>Whether hunger advances for this record now, observed from its placement and the native graph. Read-only.</summary>
        internal GuSustenanceMode SustenanceModeOf(GuRecord record)
        {
            if (!ready || CustodyTransitionInProgress || record == null || !record.IsActionable) return GuSustenanceMode.Unavailable;
            if (record.Placement == GuPlacementKind.Contained)
            {
                if (record.ContainerKind == GuContainerKind.WorldSite) return GuSustenanceMode.OffMap;
                if (!apertureOf.TryGetValue(record.Id, out var aperture) || !IsBoundAperture(aperture, record)) return GuSustenanceMode.Unavailable;
                var host = aperture.pawn;
                if (host.Dead) return GuSustenanceMode.DeceasedHost;
                if (host.MapHeld == null) return GuSustenanceMode.OffMap;
                return host.Faction == Faction.OfPlayer ? GuSustenanceMode.Advancing : GuSustenanceMode.OtherFaction;
            }
            if (!projectionOf.TryGetValue(record.Id, out var thing) || !MatchesManifestation(record, thing)) return GuSustenanceMode.Unavailable;
            if (thing is Pawn) return GuSustenanceMode.Wild;
            if (thing.MapHeld == null) return GuSustenanceMode.OffMap;
            Pawn holder = thing.ParentHolder switch
            {
                Pawn_InventoryTracker inventory => inventory.pawn,
                Pawn_CarryTracker carry => carry.pawn,
                _ => null
            };
            return holder != null && holder.Faction != Faction.OfPlayer ? GuSustenanceMode.OtherFaction : GuSustenanceMode.Advancing;
        }

        /// <summary>The host whose live aperture holds this record, or null.</summary>
        internal Pawn ApertureHostOf(GuRecord record) =>
            record != null && apertureOf.TryGetValue(record.Id, out var aperture) && IsBoundAperture(aperture, record) ? aperture.pawn : null;

        /// <summary>
        /// How this feeder can reach the Gu with food, or None with the reason. Only the cultivator can feed Gu inside their
        /// own aperture; a dormant Gu can be fed by the colonist carrying it, or by any colonist who can reach it on the ground.
        /// </summary>
        internal GuFeedAccess FeedAccess(Pawn feeder, GuRecord record, out Thing_GuDormantProjection dormant, out string reason)
        {
            dormant = null;
            reason = "Gu feeding is unavailable right now.";
            if (!CanUseCustody || record == null) return GuFeedAccess.None;
            reason = "This Gu is not a living, resolved individual.";
            if (Resolve(record.Id) != record) return GuFeedAccess.None;
            string name = record.Species?.label ?? record.SpeciesDefName;
            reason = $"{name} has no individual feeding policy";
            if (record.Species?.SimulatedFeeding == null) return GuFeedAccess.None;
            reason = "requires a conscious colonist on a map";
            if (feeder == null || !IsMapOwned(feeder) || feeder.Downed || !feeder.IsColonistPlayerControlled) return GuFeedAccess.None;
            reason = $"{feeder.LabelShort} cannot handle food";
            if (feeder.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) != true) return GuFeedAccess.None;
            if (record.Placement == GuPlacementKind.Contained)
            {
                reason = "only the cultivator whose aperture holds this Gu can feed it";
                return record.ContainerKind == GuContainerKind.MortalAperture && apertureOf.TryGetValue(record.Id, out var aperture)
                    && IsLiveAperture(aperture, record) && aperture.pawn == feeder ? GuFeedAccess.Aperture : GuFeedAccess.None;
            }
            reason = "a wild Gu feeds itself";
            if (record.Manifestation != GuManifestationKind.Thing || ProjectionOf(record) is not Thing_GuDormantProjection thing)
                return GuFeedAccess.None;
            dormant = thing;
            if (IsInInventory(thing, InventoryOf(feeder)) || feeder.carryTracker?.CarriedThing == thing) return GuFeedAccess.Inventory;
            reason = $"the dormant Gu is not within {feeder.LabelShort}'s reach";
            return thing.Spawned && thing.Map == feeder.Map && !thing.IsForbidden(feeder)
                && feeder.CanReach(thing, PathEndMode.Touch, Danger.Some) ? GuFeedAccess.Ground : GuFeedAccess.None;
        }

        /// <summary>Why starvation stops this Gu acting for its master, or null. Read-only.</summary>
        internal string StarvationImpairment(GuRecord record)
        {
            if (record?.Species?.SimulatedFeeding == null) return null;
            var snapshot = Sustenance.GuSustenance.Snapshot(this, record);
            return snapshot.Impaired
                ? $"{record.Species.LabelCap} is withering from starvation and cannot act until it is fed"
                : null;
        }

        /// <summary>
        /// Ends a starved individual. One event key per individual makes this idempotent: replaying it, or reloading a save
        /// written after it, never produces a second death. The record is unbound before its dormant body is retired, so the
        /// retirement is not mistaken for physical destruction; an aperture place is freed because the occupant is no longer living.
        /// </summary>
        internal bool TryEndLifeByStarvation(GuRecord record, string detail, out string reason)
        {
            reason = "Starvation death needs a living, resolved individual and settled custody.";
            if (record == null) return false;
            string key = StarvationEventKey(record.Id);
            if (byEndEvent.TryGetValue(key, out var previous))
            {
                reason = $"Gu {record.Id} has already died of starvation.";
                return false;
            }
            if (!CanUseCustody || Resolve(record.Id) != record) return false;

            int tick = Find.TickManager.TicksGame;
            Thing thing = record.Placement == GuPlacementKind.Manifested ? ProjectionOf(record) : null;
            GuLocation location = thing != null ? LocationOf(thing) : ContainerLocation(record, tick);
            CustodyTransitionInProgress = true;
            try
            {
                UnbindAll(record);
                record.EndLife(GuTerminalOutcome.Died, key, null, location, detail, tick);
                byEndEvent[key] = record;
            }
            finally { CustodyTransitionInProgress = false; }
            RetireNativeObject(thing, record);
            ApplyTerminalEffect(record, GuTerminalOutcome.Died, location);
            reason = null;
            return true;
        }
    }
}
