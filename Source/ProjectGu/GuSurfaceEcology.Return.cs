using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuSurfaceEcology
    {
        internal bool TryPrepareReturn(GuRecord record, Map map, out Locality row, out int remainingResidents)
        {
            row = null;
            remainingResidents = 0;
            if (record.Species is not { HasSurfacePopulation: true } species || !GuPocketProbeComponent.IsSurfaceSettlement(map)
                || map.Disposed || !Find.Maps.Contains(map) || record.SurfaceResidence is not { } site || Key(map) != site
                || !UniqueMap(map, site) || LocalityAt(map, record.SpeciesDefName) is not { Cohort: { } cohort } target
                || target.Unloaded || target.LastParent != map.Parent.GetUniqueLoadID() || target.LastMap != map.GetUniqueLoadID()
                || Suitability(map, species) <= 0 || !Registry.SurfaceEvidenceIntact(site, cohort)) return false;

            var residents = Residents(site, record.SpeciesDefName);
            if (!residents.Contains(record) || residents.Length > Capacity
                || residents.Any(other => other != record && !Countable(other, map.Tile, map))) return false;
            if (record.IsAdmittedSurfaceResident && !target.Receipts.Any(evidence =>
                    evidence.Source is GuSurfaceSource.Introduction or GuSurfaceSource.Escape && evidence.Individual == record.Id)) return false;

            remainingResidents = residents.Length - 1;
            if (!cohort.CanRecruitMature(Capacity, remainingResidents)) return false;
            row = target;
            return true;
        }

        internal static bool CommitReturnCredit(Locality row, string eventKey, long revision, int remainingResidents, int now)
        {
            if (!row.Cohort.TryCreditReturn(eventKey, revision, Capacity, remainingResidents)) return false;
            row.NextManifestationTick = AddTicks(now, TicksPerDay);
            return true;
        }
    }
}
