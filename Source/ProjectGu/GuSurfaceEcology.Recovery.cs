using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu
{
    public enum GuSurfaceSource { NaturalReplenishment, Migration, NaturalFormation, Introduction, Escape }

    public sealed partial class GuSurfaceEcology
    {
        private const int DueBudget = 16;
        private const int MaximumOverdue = 30 * TicksPerDay;
        private const int RetryTicks = TicksPerDay;
        private Dictionary<string, Region> regions = new();
        private readonly DueSchedule schedule = new();
        private bool scheduleReady;
        private long processed, saturatedTurns, loadedObservations, migrations;

        internal enum BlockedBy { Unknown, None, Habitat, Destroyed, MapBinding, Residents, DebitEvidence, NoSource, Abandoned }

        internal readonly record struct Address(string Owner, string Species, bool Regional = false);
        internal readonly record struct Work(Address Address, int Tick);

        internal sealed class DueSchedule
        {
            private readonly Dictionary<Address, int> pending = new();
            private readonly Work[] batch = new Work[DueBudget];
            private readonly SortedSet<Work> order = new(Comparer<Work>.Create((left, right) =>
            {
                int result = left.Tick.CompareTo(right.Tick);
                if (result != 0) return result;
                result = StringComparer.Ordinal.Compare(left.Address.Owner, right.Address.Owner);
                if (result != 0) return result;
                result = StringComparer.Ordinal.Compare(left.Address.Species, right.Address.Species);
                return result != 0 ? result : left.Address.Regional.CompareTo(right.Address.Regional);
            }));

            internal int Count => pending.Count;
            internal int FirstTick => order.Count == 0 ? -1 : order.Min.Tick;
            internal int DueAt(Address address) => pending.GetValueOrDefault(address, -1);
            internal void Clear()
            {
                pending.Clear();
                order.Clear();
            }

            internal void Set(Address address, int tick)
            {
                if (pending.TryGetValue(address, out int previous))
                {
                    if (tick == previous) return;
                    order.Remove(new(address, previous));
                    pending.Remove(address);
                }
                if (tick < 0) return;
                pending.Add(address, tick);
                order.Add(new(address, tick));
            }

            internal int Service(int now, Action<Address> settle, out bool saturated)
            {
                int count = 0;
                // Freeze this turn's batch before settlement can reinsert work.
                while (count < DueBudget && order.Count != 0 && order.Min.Tick <= now)
                {
                    Work first = order.Min;
                    Set(first.Address, -1);
                    batch[count++] = first;
                }
                for (int i = 0; i < count; i++) settle(batch[i].Address);
                saturated = order.Count != 0 && order.Min.Tick <= now;
                return count;
            }
        }

        internal sealed class Locality : IExposable
        {
            public int NextManifestationTick;
            public GuPopulationRecord Cohort;
            public int Seed;
            public int ReplacementOrdinal;
            public int ReplacementTicks = -1;
            public int ConsumedTick = -1;
            public int RetryAfter = -1;
            public long DiscardedTicks;
            public bool Established;
            public int FoundingCount = -1;
            public BlockedBy Blocked;
            public int EvidenceTick = -1;
            public int HabitatWeight;
            public GuSurfaceHabitat EvidenceProfile;
            public string EvidenceProfileName;
            public bool ExposedRock;
            public string LastMap;
            public string LastParent;
            public bool Unloaded;
            public List<SourceReceipt> Receipts = new();

            internal void StartInterval()
            {
                int roll = Gen.HashCombineInt(Seed, ReplacementOrdinal++);
                ReplacementTicks = Rand.RangeInclusiveSeeded(3 * TicksPerDay, 9 * TicksPerDay, roll);
            }

            public void ExposeData()
            {
                Scribe_Deep.Look(ref Cohort, "cohort");
                Scribe_Values.Look(ref Seed, "seed");
                Scribe_Values.Look(ref ReplacementOrdinal, "replacementOrdinal");
                Scribe_Values.Look(ref ReplacementTicks, "replacementTicks", -1);
                Scribe_Values.Look(ref ConsumedTick, "consumedTick", -1);
                Scribe_Values.Look(ref RetryAfter, "retryAfter", -1);
                Scribe_Values.Look(ref DiscardedTicks, "discardedTicks");
                Scribe_Values.Look(ref Established, "established");
                Scribe_Values.Look(ref FoundingCount, "foundingCount", -1);
                Scribe_Values.Look(ref Blocked, "blocked");
                Scribe_Values.Look(ref EvidenceTick, "evidenceTick", -1);
                Scribe_Values.Look(ref NextManifestationTick, "nextManifestationTick");
                Scribe_Values.Look(ref HabitatWeight, "habitatWeight");
                Scribe_Values.Look(ref EvidenceProfile, "evidenceProfile");
                Scribe_Values.Look(ref EvidenceProfileName, "evidenceProfileName");
                Scribe_Values.Look(ref ExposedRock, "exposedRock");
                Scribe_Values.Look(ref LastMap, "lastMap");
                Scribe_Values.Look(ref LastParent, "lastParent");
                Scribe_Values.Look(ref Unloaded, "unloaded");
                Scribe_Collections.Look(ref Receipts, "recoveryReceipts", LookMode.Deep);
                Receipts ??= new();
                if (Scribe.mode == LoadSaveMode.PostLoadInit && FoundingCount < 0 && Cohort != null)
                    Established |= Cohort.TotalCount > 0 || Cohort.Revision > 0;
            }
        }

        internal sealed class SourceReceipt : IExposable
        {
            public GuSurfaceSource Source;
            public int Tick;
            public int Opportunity;
            public string Donor;
            public long DonorRevision;
            public long RecipientRevision;
            public string Individual;
            public int CustodyRevision;

            public void ExposeData()
            {
                Scribe_Values.Look(ref Source, "source");
                Scribe_Values.Look(ref Tick, "tick");
                Scribe_Values.Look(ref Opportunity, "opportunity");
                Scribe_Values.Look(ref Donor, "donor");
                Scribe_Values.Look(ref DonorRevision, "donorRevision");
                Scribe_Values.Look(ref RecipientRevision, "recipientRevision");
                Scribe_Values.Look(ref Individual, "individual");
                Scribe_Values.Look(ref CustodyRevision, "custodyRevision");
            }
        }

        private sealed class Region : IExposable
        {
            public Region() { }
            public Dictionary<string, Locality> Populations = new();
            public Dictionary<string, int> WitnessTiles = new();

            public void ExposeData()
            {
                Scribe_Collections.Look(ref Populations, "populations", LookMode.Value, LookMode.Deep);
                Scribe_Collections.Look(ref WitnessTiles, "witnessTiles", LookMode.Value, LookMode.Value);
                Populations ??= new();
                WitnessTiles ??= new();
            }
        }

        private static int AddTicks(int tick, int delta) => (int)Math.Min(int.MaxValue, (long)tick + delta);

        private void EnsureSchedule(int now)
        {
            if (scheduleReady) return;
            schedule.Clear();
            foreach (var (key, site) in sites)
                foreach (var (species, row) in site.Populations)
                    if (row?.Cohort != null) Schedule(new(key, species), row, now, rebuilding: true);
            foreach (var (key, region) in regions)
                foreach (var (species, row) in region.Populations)
                    if (row?.Cohort != null) Schedule(new(key, species, true), row, now, rebuilding: true);
            scheduleReady = true;
        }

        private void Schedule(Address address, Locality row, int now, bool rebuilding = false)
        {
            int due;
            if (row.Blocked is BlockedBy.Destroyed)
                due = -1;
            else if (row.RetryAfter > row.ConsumedTick)
                due = row.RetryAfter;
            else if (row.ReplacementTicks >= 0 && row.Blocked == BlockedBy.None && row.ConsumedTick >= 0)
                due = AddTicks(row.ConsumedTick, row.ReplacementTicks);
            else if (!address.Regional && !row.Unloaded && row.EvidenceTick >= 0 && !rebuilding)
                due = -1; // The loaded observation path touches this row every sample.
            else
                due = rebuilding ? now : AddTicks(now, RetryTicks);
            schedule.Set(address, due < 0 ? -1 : rebuilding ? due : Math.Max(AddTicks(now, 1), due));
        }

        private void ServiceSchedule(int now)
        {
            processed += schedule.Service(now, address => SettleScheduled(address, now), out bool saturated);
            if (saturated) saturatedTurns++;
        }

        private void SettleScheduled(Address address, int now)
        {
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail(address.Species);
            if (address.Regional)
            {
                if (regions.TryGetValue(address.Owner, out var region) && region.Populations.TryGetValue(address.Species, out var donor))
                    SettleRegion(address, region, donor, species, now);
                return;
            }
            if (!sites.TryGetValue(address.Owner, out var site) || !site.Populations.TryGetValue(address.Species, out var row)) return;
            if (!TrySurfaceTile(address.Owner, out var tile) || species?.HasSurfacePopulation != true)
            {
                Advance(address, row, species, PlanetTile.Invalid, null, 0, BlockedBy.Unknown, now);
                return;
            }

            var map = Current.Game.FindMap(tile);
            if (map != null)
            {
                if (GuPocketProbeComponent.IsSurfaceSettlement(map) && UniqueMap(map, address.Owner))
                {
                    int weight = Suitability(map, species, out bool rock);
                    ObserveLoaded(map, address.Owner, row, species, weight, rock, Residents(address.Owner, address.Species), now);
                }
                else
                    Advance(address, row, species, tile, null, 0, BlockedBy.MapBinding, now);
                return;
            }
            var residents = Residents(address.Owner, address.Species);
            BlockedBy habitat = row.Blocked is BlockedBy.Destroyed or BlockedBy.Abandoned ? row.Blocked
                : !row.Unloaded || row.EvidenceTick < 0 || row.EvidenceProfile != species.surfaceHabitat
                    || row.EvidenceProfileName != species.surfaceProfile?.defName ? BlockedBy.Unknown
                : row.HabitatWeight <= 0 || WorldWeight(tile, species, row.ExposedRock) <= 0 ? BlockedBy.Habitat : BlockedBy.None;
            Advance(address, row, species, tile, null, residents.Length, PopulationBlock(address.Owner, row, tile, null, residents, habitat), now);
        }

        private void ObserveLoaded(Map map, string key, Locality row, GuSpeciesDef species, int weight, bool exposedRock,
            GuRecord[] residents, int now)
        {
            loadedObservations++;
            var habitat = weight > 0 ? BlockedBy.None : BlockedBy.Habitat;
            string binding = map.GetUniqueLoadID();
            if (!row.Unloaded && row.LastMap != binding && row.ConsumedTick >= 0 && row.Blocked == BlockedBy.None)
                row.Blocked = BlockedBy.MapBinding;
            row.LastMap = binding;
            row.LastParent = map.Parent.GetUniqueLoadID();
            row.Unloaded = false;
            row.EvidenceTick = now;
            row.HabitatWeight = weight;
            row.EvidenceProfile = species.surfaceHabitat;
            row.EvidenceProfileName = species.surfaceProfile?.defName;
            row.ExposedRock = exposedRock;
            Advance(new(key, species.defName), row, species, map.Tile, map, residents.Length,
                PopulationBlock(key, row, map.Tile, map, residents, habitat), now);
        }

        private static BlockedBy PopulationBlock(string key, Locality row, PlanetTile tile, Map map, GuRecord[] residents, BlockedBy habitat)
        {
            if (habitat != BlockedBy.None) return habitat;
            if (residents.Length > Capacity || residents.Any(record => !Countable(record, tile, map))) return BlockedBy.Residents;
            return Registry.SurfaceEvidenceIntact(key, row.Cohort) ? BlockedBy.None : BlockedBy.DebitEvidence;
        }

        private void Advance(Address address, Locality row, GuSpeciesDef species, PlanetTile tile, Map map, int residents, BlockedBy block, int now)
        {
            int elapsed = row.ConsumedTick < 0 ? 0 : Math.Max(0, now - row.ConsumedTick);
            int supported = (block == BlockedBy.None && row.Blocked == BlockedBy.None && row.RetryAfter < 0)
                ? Math.Min(elapsed, MaximumOverdue) : 0;
            row.DiscardedTicks += elapsed - supported;
            row.ConsumedTick = Math.Max(row.ConsumedTick, now);
            row.Blocked = block;
            if (block != BlockedBy.None)
            {
                Schedule(address, row, now);
                return;
            }

            long occupied = row.Cohort.TotalCount + residents;
            if (occupied >= Capacity)
            {
                row.ReplacementTicks = -1;
                row.RetryAfter = -1;
            }
            else if (row.ReplacementTicks < 0 && occupied <= Threshold)
                row.StartInterval();
            else if (row.RetryAfter > now)
                row.Blocked = BlockedBy.NoSource;
            else if (row.ReplacementTicks >= 0 && now >= row.RetryAfter)
            {
                row.RetryAfter = -1;
                int window = supported;
                for (int units = 0; units < Capacity; units++)
                {
                    if (row.ReplacementTicks > window)
                    {
                        row.ReplacementTicks -= window;
                        break;
                    }
                    window -= row.ReplacementTicks;
                    row.ReplacementTicks = 0;
                    int opportunityTick = now - window;
                    if (!TrySupply(address, row, species, tile, map, residents, opportunityTick, now))
                    {
                        row.Blocked = BlockedBy.NoSource;
                        row.DiscardedTicks += window;
                        row.RetryAfter = AddTicks(now, RetryTicks);
                        break;
                    }
                    if (++occupied >= Capacity)
                    {
                        row.ReplacementTicks = -1;
                        break;
                    }
                    row.StartInterval();
                }
            }
            Schedule(address, row, now);
        }

        internal void ObserveBeforeMapRemoval(Map map)
        {
            if (Registry == null || Registry.CustodyTransitionInProgress || !GuPocketProbeComponent.IsSurfaceSettlement(map)) return;
            string key = Key(map);
            if (!sites.TryGetValue(key, out var site) || !UniqueMap(map, key)) return;
            int now = Find.TickManager.TicksGame;
            EnsureSchedule(now);
            foreach (var species in SurfaceSpecies)
            {
                if (!site.Populations.TryGetValue(species.defName, out var row)) continue;
                int weight = Suitability(map, species, out bool rock);
                ObserveLoaded(map, key, row, species, weight, rock, Residents(key, species.defName), now);
            }
        }

        internal void ConfirmMapRemoval(Map map, int now)
        {
            if (map.Parent is not Settlement || !map.Tile.Valid || !sites.TryGetValue(Key(map), out var site)) return;
            string key = Key(map), binding = map.GetUniqueLoadID();
            foreach (var (species, row) in site.Populations)
            {
                if (row.LastMap != binding) continue;
                row.Unloaded = true;
                Schedule(new(key, species), row, now);
            }
        }

        internal void ObserveRemovedSettlement(Settlement settlement, bool abandoned)
        {
            if (settlement.Spawned || !settlement.Tile.Valid || settlement.Tile.LayerDef != PlanetLayerDefOf.Surface) return;
            string key = Key(settlement.Tile);
            if (!sites.TryGetValue(key, out var site)) return;
            int now = Find.TickManager.TicksGame;
            EnsureSchedule(now);
            foreach (var (species, row) in site.Populations)
                if (row.LastParent == settlement.GetUniqueLoadID())
                    Advance(new(key, species), row, null, settlement.Tile, null, 0,
                        abandoned ? BlockedBy.Abandoned : BlockedBy.Destroyed, now);
        }
    }
}
