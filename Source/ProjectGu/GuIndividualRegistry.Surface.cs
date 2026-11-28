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
        private readonly Dictionary<(string Site, string Species), SurfaceReturnScope> surfaceReturns = new();

        private sealed class SurfaceReceiptScope
        {
            public readonly HashSet<GuRecord> Records = new();
            public (int Count, long Digest)? Summary;
        }

        private void IndexSurfaceRecord(GuRecord record)
        {
            if (record.PopulationReturn != null || record.Existence == GuExistence.Aggregated) IndexSurfaceReturn(record, append: false);
            if (record.CohortDebitCommitted && record.CreationEvidence is { OwnerKind: GuSurfaceEcology.SourceKind, SourceId: var source })
            {
                var key = (source, record.SpeciesDefName);
                if (!surfaceReceipts.TryGetValue(key, out var scope)) surfaceReceipts.Add(key, scope = new());
                if (scope.Records.Add(record)) scope.Summary = null;
            }
            IndexSurfaceResident(record);
        }

        private sealed class SurfaceReturnScope
        {
            public readonly List<GuRecord> Records = new();
            public (int Count, long Digest, long Last)? Summary;
        }

        private void IndexSurfaceReturn(GuRecord record, bool append)
        {
            void Add(string site)
            {
                if (site == null) return;
                var key = (site, record.SpeciesDefName);
                if (!surfaceReturns.TryGetValue(key, out var scope)) surfaceReturns.Add(key, scope = new());
                var previous = scope.Summary ?? (0, GuPopulationRecord.DigestSeed, 0L);
                scope.Summary = append && previous.Count == scope.Records.Count && previous.Last < record.PopulationReturn.CohortAfter
                    ? (previous.Count + 1, GuPopulationRecord.ChainDigest(previous.Digest, record.PopulationReturn.CohortAfter, record.PopulationReturn.CreditKey(record.Id)),
                        record.PopulationReturn.CohortAfter) : null;
                scope.Records.Add(record);
            }

            var destination = record.PopulationReturn?.Site;
            Add(destination);
            if (record.SurfaceResidenceEvidence != destination) Add(record.SurfaceResidenceEvidence);
        }

        private bool SurfaceReturnsMatch(string site, string species, GuPopulationRecord cohort)
        {
            if (!surfaceReturns.TryGetValue((site, species), out var scope)) return cohort.ReturnCredits == 0;
            if (scope.Summary == null)
            {
                long last = 0, digest = GuPopulationRecord.DigestSeed;
                foreach (var record in scope.Records.OrderBy(record => record.PopulationReturn?.CohortAfter ?? 0))
                {
                    if (record.IsQuarantined || record.PopulationReturn is not { } receipt || !receipt.Matches(record)
                        || receipt.Site != site || receipt.CohortAfter <= last) return false;
                    last = receipt.CohortAfter;
                    digest = GuPopulationRecord.ChainDigest(digest, last, receipt.CreditKey(record.Id));
                }
                scope.Summary = (scope.Records.Count, digest, last);
            }
            var (count, expected, revision) = scope.Summary.Value;
            return cohort.ReturnCredits == count && cohort.ReturnDigest == expected && cohort.Revision >= revision;
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
