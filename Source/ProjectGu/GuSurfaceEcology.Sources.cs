using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuSurfaceEcology
    {
        private static bool TrySurfaceTile(string locality, out PlanetTile tile)
        {
            tile = PlanetTile.Invalid;
            var parts = locality.Split('/');
            if (parts.Length != 3 || parts[2] != "0" || !int.TryParse(parts[0], out int layer)
                || !int.TryParse(parts[1], out int index) || Find.WorldGrid?.Surface is not { } surface
                || surface.LayerID != layer || index < 0 || index >= surface.TilesCount) return false;
            tile = surface.PlanetTileForID(index);
            return tile.Valid;
        }

        internal static string RegionOf(PlanetTile tile)
        {
            if (!tile.Valid || tile.LayerDef != PlanetLayerDefOf.Surface) return null;
            var position = Find.WorldGrid.GetTileCenter(tile).normalized;
            static int Bucket(float value) => Mathf.FloorToInt((value + 1f) * 6f);
            return $"region-v1/{tile.Layer.LayerID}/{Bucket(position.x)}/{Bucket(position.y)}/{Bucket(position.z)}";
        }

        private static int WorldWeight(PlanetTile tile, GuSpeciesDef species, bool localRock = false)
        {
            if (!tile.Valid || tile.LayerDef != PlanetLayerDefOf.Surface || species?.HasSurfacePopulation != true) return 0;
            var native = Find.WorldGrid[tile];
            return Weight(species, native.PrimaryBiome, native.hilliness, localRock);
        }

        private bool TryFindDonor(PlanetTile tile, GuSpeciesDef species, int now,
            out Address address, out Region region, out Locality donor)
        {
            address = default;
            region = null;
            donor = null;
            if (RegionOf(tile) is not { } key || DonorWitness(tile, species, key) is not { } witness) return false;

            address = new(key, species.defName, true);
            if (!regions.TryGetValue(key, out region)) regions.Add(key, region = new());
            if (!region.Populations.TryGetValue(species.defName, out donor))
            {
                int seed = GenText.StableStringHash($"{Find.World.info.seedString}/{key}/{species.defName}/founding-v1");
                int founding = Rand.RangeInclusiveSeeded(1, 3, seed);
                donor = new() { Cohort = new(species.defName, founding, 0, -1), Seed = seed, FoundingCount = founding,
                    Established = true };
                region.Populations.Add(species.defName, donor);
                region.WitnessTiles.Add(species.defName, witness);
            }
            SettleRegion(address, region, donor, species, now);
            if (donor.Blocked == BlockedBy.Habitat && region.WitnessTiles.GetValueOrDefault(species.defName, -1) != witness)
            {
                region.WitnessTiles[species.defName] = witness;
                SettleRegion(address, region, donor, species, now);
            }
            return donor.Blocked == BlockedBy.None && donor.Cohort.MatureCount > 0;
        }

        internal static int? DonorWitness(PlanetTile tile, GuSpeciesDef species, string region)
        {
            if (region == null || RegionOf(tile) != region || WorldWeight(tile, species, localRock: true) == 0) return null;
            var neighbors = new List<PlanetTile>(6);
            Find.WorldGrid.GetTileNeighbors(tile, neighbors);
            int witness = int.MaxValue;
            foreach (var candidate in neighbors)
                if (candidate.tileId < witness && WorldWeight(candidate, species) > 0 && RegionOf(candidate) == region)
                    witness = candidate.tileId;
            return witness == int.MaxValue ? null : witness;
        }

        private void SettleRegion(Address address, Region region, Locality donor, GuSpeciesDef species, int now)
        {
            var surface = Find.WorldGrid?.Surface;
            int witness = region.WitnessTiles.GetValueOrDefault(address.Species, -1);
            var tile = surface != null && witness >= 0 && witness < surface.TilesCount
                ? surface.PlanetTileForID(witness) : PlanetTile.Invalid;
            int weight = WorldWeight(tile, species);
            donor.HabitatWeight = weight;
            donor.EvidenceTick = now;
            donor.EvidenceProfile = species?.surfaceHabitat ?? GuSurfaceHabitat.None;
            donor.EvidenceProfileName = species?.surfaceProfile?.defName;
            BlockedBy block = weight <= 0 || RegionOf(tile) != address.Owner ? BlockedBy.Habitat
                : !donor.Cohort.EvidenceIntact ? BlockedBy.DebitEvidence : BlockedBy.None;
            Advance(address, donor, species, tile, null, 0, block, now);
        }

        private bool TrySupply(Address address, Locality row, GuSpeciesDef species, PlanetTile tile, Map map,
            int residents, int opportunityTick, int now)
        {
            if (species?.surfaceRecoverySources == null) return false;
            foreach (var source in species.surfaceRecoverySources)
            {
                var receipt = new SourceReceipt { Source = source, Tick = opportunityTick, Opportunity = row.ReplacementOrdinal,
                    RecipientRevision = row.Cohort.Revision + 1 };
                if (source == GuSurfaceSource.Migration && !address.Regional)
                {
                    if (!row.Cohort.CanRecruitMature(Capacity, residents)
                        || !TryFindDonor(tile, species, now, out var donorAddress, out var region, out var donor)
                        || !donor.Cohort.TryTransferMatureTo(row.Cohort, donor.Cohort.Revision, row.Cohort.Revision, Capacity, residents)) continue;
                    receipt.Tick = now;
                    receipt.Donor = donorAddress.Owner;
                    receipt.DonorRevision = donor.Cohort.Revision;
                    migrations++;
                    row.Receipts.Add(receipt);
                    row.Established = true;
                    if (donor.Cohort.TotalCount <= Threshold && donor.ReplacementTicks < 0) donor.StartInterval();
                    Schedule(donorAddress, donor, now);
                    return true;
                }
                else
                {
                    bool allowed = source == GuSurfaceSource.NaturalReplenishment && row.Established
                        || source == GuSurfaceSource.NaturalFormation && !address.Regional && map != null
                        && species.surfaceHabitat == GuSurfaceHabitat.Rocky && HasExposedRock(map);
                    if (!allowed || !row.Cohort.TryRecruitMature(Capacity, residents)) continue;
                    if (source == GuSurfaceSource.NaturalFormation) receipt.Tick = now;
                }
                row.Receipts.Add(receipt);
                row.Established = true;
                return true;
            }
            return false;
        }

        internal bool TryIntroduceExisting(string id, int revision, Map map) =>
            TryAdmitExisting(id, revision, map, GuSurfaceSource.Introduction);

        internal bool TryAdmitEscape(string id, int revision, Map map) =>
            TryAdmitExisting(id, revision, map, GuSurfaceSource.Escape);

        private bool TryAdmitExisting(string id, int revision, Map map, GuSurfaceSource source)
        {
            if (Registry?.Resolve(id) is not { SurfaceResidence: null, Species: { HasSurfacePopulation: true } species } record
                || record.CustodyRevision != revision || !GuPocketProbeComponent.IsSurfaceSettlement(map)
                || !Countable(record, map.Tile, map) || source == GuSurfaceSource.Escape && !record.HasEscapeEvidence) return false;
            string key = Key(map);
            int weight = Suitability(map, species, out bool exposedRock);
            if (weight == 0 || !UniqueMap(map, key)) return false;

            int now = Find.TickManager.TicksGame;
            EnsureSchedule(now);
            var row = GetOrInitialize(key, species, weight);
            if (row == null) return false;
            var residents = Residents(key, species.defName);
            ObserveLoaded(map, key, row, species, weight, exposedRock, residents, now);
            if (row.Blocked is not (BlockedBy.None or BlockedBy.NoSource) || row.Cohort.TotalCount + residents.Length >= Capacity
                || !Registry.TryJoinSurfaceResidence(record, revision, key)) return false;
            if (source == GuSurfaceSource.Escape) record.NoteEncounterInteraction(now, historical: true);
            row.Established = true;
            row.Receipts.Add(new() { Source = source, Individual = id, CustodyRevision = revision, Tick = now,
                RecipientRevision = row.Cohort.Revision });
            ObserveLoaded(map, key, row, species, weight, exposedRock, Residents(key, species.defName), now);
            return true;
        }

        internal string Diagnostics()
        {
            var output = new StringBuilder($"Surface ecology | localities {sites.Count} | regions {regions.Count} | due entries {schedule.Count}"
                + $" | oldest due {schedule.FirstTick} | due processed {processed} | saturated turns {saturatedTurns}"
                + $" | loaded observations {loadedObservations} | migrations {migrations} | native maps generated by ecology 0");
            void Append(Address address, Locality row)
            {
                int residents = address.Regional ? 0 : Residents(address.Owner, address.Species).Length;
                output.AppendLine().Append($"{address.Owner} {address.Species} | anonymous {row.Cohort.TotalCount} | living members {residents}"
                    + $" | founding {row.FoundingCount} | established {row.Established} | revision {row.Cohort.Revision}"
                    + $" | return credits {row.Cohort.ReturnCredits}"
                    + $" | {row.Blocked} | {row.EvidenceProfileName ?? row.EvidenceProfile.ToString()} weight {row.HabitatWeight}"
                    + $" exposed rock {row.ExposedRock} measured at {row.EvidenceTick}"
                    + $" | unloaded {row.Unloaded} | consumed {row.ConsumedTick} | discarded {row.DiscardedTicks}"
                    + $" | interval {row.ReplacementOrdinal} remaining {row.ReplacementTicks} | next work {schedule.DueAt(address)}");
                foreach (var receipt in row.Receipts)
                    output.AppendLine().Append($"  {receipt.Source} at {receipt.Tick} interval {receipt.Opportunity}"
                        + $" | recipient revision {receipt.RecipientRevision}"
                        + (receipt.Donor == null ? "" : $" | donor {receipt.Donor} revision {receipt.DonorRevision}")
                        + (receipt.Individual == null ? "" : $" | Gu {receipt.Individual} custody revision {receipt.CustodyRevision}"));
            }
            foreach (var (key, site) in sites)
                foreach (var (species, row) in site.Populations) Append(new(key, species), row);
            foreach (var (key, region) in regions)
                foreach (var (species, row) in region.Populations) Append(new(key, species, true), row);
            return output.ToString();
        }
    }
}
