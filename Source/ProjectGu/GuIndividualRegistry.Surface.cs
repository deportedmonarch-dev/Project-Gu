using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public sealed partial class GuIndividualRegistry
    {
        private readonly Dictionary<(string Site, string Species), HashSet<GuRecord>> surfaceResidents = new();
        private readonly Dictionary<(string Site, string Species), SurfaceReceiptScope> surfaceReceipts = new();

        private sealed class SurfaceReceiptScope
        {
            public readonly HashSet<GuRecord> Records = new();
            public (int Count, long Digest)? Summary;
        }

        private void IndexSurfaceRecord(GuRecord record)
        {
            if (record.CohortDebitCommitted && record.CreationEvidence is { OwnerKind: GuSurfaceEcology.SourceKind, SourceId: var source })
            {
                var key = (source, record.SpeciesDefName);
                if (!surfaceReceipts.TryGetValue(key, out var scope)) surfaceReceipts.Add(key, scope = new());
                if (scope.Records.Add(record)) scope.Summary = null;
            }
            IndexSurfaceResident(record);
        }

        private void IndexSurfaceResident(GuRecord record)
        {
            if (record.SurfaceResidence is not { } site) return;
            var key = (site, record.SpeciesDefName);
            if (!surfaceResidents.TryGetValue(key, out var group)) surfaceResidents.Add(key, group = new());
            group.Add(record);
        }

        private void UnindexSurfaceRecord(GuRecord record, string site, string kind)
        {
            if (kind != GuSurfaceEcology.SourceKind) return;
            var key = (site, record.SpeciesDefName);
            if (surfaceReceipts.TryGetValue(key, out var scope) && scope.Records.Remove(record))
            {
                if (scope.Records.Count == 0) surfaceReceipts.Remove(key);
                scope.Summary = null;
            }
            if (surfaceResidents.TryGetValue(key, out var group)) group.Remove(record);
        }

        private bool SurfaceReceiptsMatch(string site, string species, GuPopulationRecord cohort)
        {
            if (!surfaceReceipts.TryGetValue((site, species), out var scope)) return cohort.ReceiptDebits == 0;
            if (scope.Summary == null)
            {
                long previous = -1, digest = GuPopulationRecord.DigestSeed;
                foreach (var record in scope.Records.OrderBy(record => record.CreationEvidence.RevisionAfter))
                {
                    long after = record.CreationEvidence.RevisionAfter;
                    if (after <= previous) return false;
                    digest = GuPopulationRecord.ChainDigest(digest, after, record.CreationEventKey);
                    previous = after;
                }
                scope.Summary = (scope.Records.Count, digest);
            }
            return scope.Summary.Value == (cohort.ReceiptDebits, cohort.ReceiptDigest);
        }

        internal GuRecord[] SurfaceResidents(string site, string species, int limit)
        {
            if (!surfaceResidents.TryGetValue((site, species), out var group)) return System.Array.Empty<GuRecord>();
            group.RemoveWhere(record => record.SurfaceResidence != site);
            return group.Take(limit).ToArray();
        }

        internal bool TryJoinSurfaceResidence(GuRecord record, int custodyRevision, string site)
        {
            if (!CanUseCustody || record == null || Resolve(record.Id) != record || record.SurfaceResidence != null
                || record.CustodyRevision != custodyRevision || record.Rank != 1 || record.Condition != GuCondition.Active
                || record.Refinement?.CultivatorId != null) return false;
            record.JoinSurfaceResidence(site);
            IndexSurfaceResident(record);
            return true;
        }
    }
}
