using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    /// <summary>Prepared 3A store. Lists preserve evidence independently of the authority indexes.</summary>
    internal sealed class GuCultivatorPlan
    {
        internal readonly List<GuCultivatorRecord> Records;
        internal readonly List<GuCultivatorEvidence> Evidence;
        internal readonly Dictionary<Pawn, GuCultivatorRecord> ByPawn = new(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<string, GuCultivatorRecord> ById = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, GuCultivatorRecord> ByAperture = new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> IdentityClaims = new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> CreationClaims = new(StringComparer.Ordinal);
        internal readonly HashSet<string> PawnClaims = new(StringComparer.Ordinal);
        internal readonly HashSet<string> ApertureClaims = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ApertureWrite> writes = [];

        private sealed record ApertureWrite(Hediff_Aperture Aperture, Pawn Owner, string Before, string After);

        private GuCultivatorPlan(List<GuCultivatorRecord> records, List<GuCultivatorEvidence> evidence)
        {
            Records = records;
            Evidence = evidence;
        }

        internal static GuCultivatorPlan Empty() => new([], []);

        internal static GuCultivatorPlan PrepareLegacy(
            GuLegacyCultivatorRows source, IEnumerable<GuPawnRecord> resolved, IEnumerable<Pawn> nativePawns,
            IEnumerable<Hediff_Aperture> apertures, IReadOnlyList<GuRecord> guRecords)
        {
            if (source.HasMalformedEnvelope) throw new InvalidOperationException("Malformed legacy cultivator collection.");
            var plan = Empty();
            var rows = source.Rows;
            var rawClaims = rows.SelectMany(row => row.PawnClaims).ToLookup(token => token, StringComparer.Ordinal);
            var references = resolved.Where(row => row?.RawXml != null).ToLookup(row => row.RawXml, StringComparer.Ordinal);
            var pawns = nativePawns.Where(IsCandidate).Distinct<Pawn>(ReferenceEqualityComparer.Instance)
                .ToLookup(pawn => pawn.GetUniqueLoadID(), StringComparer.Ordinal);
            var nativeApertures = NativeApertures(apertures);
            var anchors = MortalAnchors(guRecords);
            var byAnchorPawn = anchors.ToLookup(gu => gu.AnchorHint, StringComparer.Ordinal);
            var byAnchorId = anchors.Where(gu => GuCultivatorRecord.IsId(gu.ContainerId))
                .ToLookup(gu => gu.ContainerId, StringComparer.OrdinalIgnoreCase);
            var boundSources = new Dictionary<GuCultivatorRecord, GuLegacyCultivatorRow>();

            foreach (var row in rows)
            {
                string token = row.PawnKey;
                var referenced = references[row.RawXml].Select(saved => saved.Pawn).SingleOrDefaultWhenUnique();
                var candidates = token == null ? Array.Empty<Pawn>() : pawns[token].ToArray();
                bool valid = row.TryReadFields(out int aptitude, out var state, out string physique, out bool migrated);
                bool unique = token != null && rawClaims[token].Count() == 1 && candidates.Length <= 1;
                bool bound = unique && candidates.Length == 1 && ReferenceEquals(referenced, candidates[0]);
                bool historical = unique && referenced == null && byAnchorPawn[token].Any(gu => !gu.IsQuarantined
                    && GuCultivatorRecord.IsId(gu.ContainerId)
                    && byAnchorId[gu.ContainerId].All(claim => claim.AnchorHint == token));

                if (valid && (bound || historical))
                {
                    var record = NewRecord(token, $"legacy:{row.Ordinal}", bound ? referenced : null);
                    record.LatentAptitude = aptitude;
                    record.State = state;
                    record.ExtremePhysiqueId = physique;
                    record.LegacyMigrated = migrated;
                    record.Significant = byAnchorPawn[token].Any();
                    plan.Records.Add(record);
                    if (bound)
                    {
                        boundSources.Add(record, row);
                        continue;
                    }
                }

                plan.Evidence.Add(new GuCultivatorEvidence
                {
                    Key = $"legacy-row:{row.Ordinal}",
                    SourceOrdinal = row.Ordinal,
                    PawnToken = row.PawnToken,
                    PawnClaims = row.PawnClaims.ToList(),
                    RawEvidence = row.RawXml,
                    Reason = valid && historical ? "Historical Gu anchor matches the raw token. Native association is unresolved."
                        : !valid ? "Malformed or unrecognized legacy fields."
                        : !unique || (referenced != null && !bound) ? "Missing or contradictory Pawn reference claim."
                        : "Pawn reference could not be resolved and has no verified Gu anchor."
                });
            }

            var byHost = nativeApertures.ToLookup(aperture => aperture.pawn, ReferenceEqualityComparer.Instance);
            foreach (var group in pawns)
            {
                if (group.Count() != 1 || rawClaims.Contains(group.Key)) continue;
                var pawn = group.First();
                // The old aperture-only path extracted aptitude from a single exact Hediff.
                var candidates = byHost[pawn].ToArray();
                if (candidates.Length == 0) continue;
                if (candidates.Length != 1 || !GuCultivatorRecord.ValidAptitude(candidates[0].Aptitude))
                {
                    plan.RecordConflict($"legacy-host:{group.Key}", [group.Key],
                        candidates.Select(DescribeAperture), "Ambiguous or malformed legacy aperture-only source.");
                    continue;
                }
                var aperture = candidates[0];
                var record = NewRecord(group.Key, $"legacy-aperture:{group.Key}", pawn);
                record.State = AwakeningState.Awakened;
                record.LatentAptitude = aperture.Aptitude;
                record.ExtremePhysiqueId = aperture.ExtremePhysiqueId;
                record.LegacyMigrated = true;
                record.Significant = byAnchorPawn[group.Key].Any();
                plan.Records.Add(record);
            }

            plan.ReserveApertureClaims(nativeApertures, anchors);
            foreach (var record in plan.Records.Where(record => record.Association == GuNativeAssociation.Bound))
            {
                foreach (var aperture in byHost[record.Pawn])
                {
                    string id = aperture.ApertureId;
                    if (id == null)
                    {
                        do { id = Guid.NewGuid().ToString("N"); } while (!plan.ApertureClaims.Add(id));
                        plan.writes.Add(new ApertureWrite(aperture, record.Pawn, null, id));
                    }
                    else if (!GuCultivatorRecord.IsId(id))
                    {
                        plan.RecordConflict($"invalid-aperture:{aperture.GetUniqueLoadID()}", [record.PawnLoadId],
                            [DescribeAperture(aperture)], "Malformed existing aperture ID. No replacement was assigned.");
                        continue;
                    }
                    record.ApertureIds.Add(id);
                }
            }
            plan.Index(nativeApertures, anchors);
            foreach (var (record, row) in boundSources)
                if (record.Association == GuNativeAssociation.Unresolved)
                    plan.Evidence.Add(new GuCultivatorEvidence
                    {
                        Key = $"legacy-row:{row.Ordinal}", SourceOrdinal = row.Ordinal, PawnToken = row.PawnToken,
                        PawnClaims = row.PawnClaims.ToList(), RawEvidence = row.RawXml,
                        Reason = "Legacy Pawn reference retained after conflicting ownership validation."
                    });
            // A conflicting owner must not receive new native writes.
            plan.writes.RemoveAll(write => !plan.ByAperture.ContainsKey(write.After));
            return plan;
        }

        internal static GuCultivatorPlan PrepareCommitted(
            List<GuCultivatorRecord> records, List<GuCultivatorEvidence> evidence,
            IEnumerable<Hediff_Aperture> apertures, IReadOnlyList<GuRecord> guRecords)
        {
            if (records == null || evidence == null || records.Any(record => record?.IsValid != true)
                || evidence.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.Key)
                    || string.IsNullOrWhiteSpace(entry.Reason) || entry.RawEvidence == null
                    || entry.PawnClaims == null || entry.PawnClaims.Any(token => !GuLegacyCultivatorRow.IsPawnKey(token)))
                || evidence.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() != evidence.Count)
                throw new InvalidOperationException("Invalid committed cultivator store.");
            var plan = new GuCultivatorPlan(records, evidence);
            var nativeApertures = NativeApertures(apertures);
            var anchors = MortalAnchors(guRecords);
            foreach (var record in records)
            {
                string resolvedToken = record.Pawn?.def != null ? record.Pawn.GetUniqueLoadID() : null;
                bool hasRawClaim = GuLegacyCultivatorRow.IsPawnKey(record.ReadPawnToken);
                if (record.Association == GuNativeAssociation.Bound
                        ? record.Pawn != null && resolvedToken == record.PawnLoadId
                            && (!hasRawClaim || record.ReadPawnToken == record.PawnLoadId)
                        : record.Pawn == null && !hasRawClaim)
                    continue;

                var claims = new[] { record.PawnLoadId, record.ReadPawnToken, resolvedToken }
                    .Where(GuLegacyCultivatorRow.IsPawnKey).Distinct(StringComparer.Ordinal).ToArray();
                plan.RecordConflict($"binding:{record.Id.ToLowerInvariant()}", claims,
                    [$"saved:{record.PawnLoadId}", $"reference:{record.ReadPawnToken}", $"resolved:{resolvedToken}"],
                    "Saved Pawn association could not be verified.");
            }
            plan.ReserveApertureClaims(nativeApertures, anchors);
            plan.Index(nativeApertures, anchors);
            return plan;
        }

        internal static GuCultivatorRecord NewRecord(string pawnToken, string creationKey, Pawn pawn) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            CreationKey = creationKey,
            PawnLoadId = pawnToken,
            Pawn = pawn,
            Association = pawn == null ? GuNativeAssociation.Unresolved : GuNativeAssociation.Bound
        };

        internal void CommitApertureWrites(Action publish, Action<int> checkpoint = null)
        {
            foreach (var write in writes)
                if (write.Aperture.ApertureId != write.Before || !ReferenceEquals(write.Aperture.pawn, write.Owner)
                    || !IsNativeAperture(write.Aperture))
                    throw new InvalidOperationException("Prepared aperture changed before cultivator publication.");

            int applied = 0;
            try
            {
                foreach (var write in writes)
                {
                    write.Aperture.ReplaceCultivatorApertureId(write.Before, write.After);
                    applied++;
                    checkpoint?.Invoke(applied);
                }
                publish();
            }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                for (int i = applied - 1; i >= 0; i--)
                    try { writes[i].Aperture.ReplaceCultivatorApertureId(writes[i].After, writes[i].Before); }
                    catch (Exception error) { errors.Add(error); }
                if (errors.Count != 1)
                    throw new AggregateException("Cultivator aperture rollback could not restore all preimages.", errors);
                throw;
            }
        }

        private void Index(Hediff_Aperture[] apertures, GuRecord[] anchors)
        {
            var blocked = Evidence.SelectMany(entry => entry.PawnClaims).ToHashSet(StringComparer.Ordinal);
            var identityIds = Records.ToLookup(record => record.Id, StringComparer.OrdinalIgnoreCase);
            var creationKeys = Records.ToLookup(record => record.CreationKey, StringComparer.Ordinal);
            var pawnClaims = Records.Where(record => record.PawnLoadId != null)
                .ToLookup(record => record.PawnLoadId, StringComparer.Ordinal);
            var nativePawns = apertures.Select(aperture => aperture.pawn)
                .Concat(Records.Select(record => record.Pawn)).Where(IsCandidate)
                .Distinct<Pawn>(ReferenceEqualityComparer.Instance).ToLookup(pawn => pawn.GetUniqueLoadID(), StringComparer.Ordinal);
            var owners = Records.SelectMany(record => record.ApertureIds.Select(id => (Id: id, Record: record)))
                .ToLookup(claim => claim.Id, StringComparer.OrdinalIgnoreCase);
            var nativeIds = apertures.Where(aperture => GuCultivatorRecord.IsId(aperture.ApertureId))
                .ToLookup(aperture => aperture.ApertureId, StringComparer.OrdinalIgnoreCase);
            var anchorIds = anchors.Where(gu => GuCultivatorRecord.IsId(gu.ContainerId))
                .ToLookup(gu => gu.ContainerId, StringComparer.OrdinalIgnoreCase);
            var stagedIds = writes.ToDictionary(write => write.After, StringComparer.OrdinalIgnoreCase);

            foreach (var key in owners.Select(group => group.Key).Concat(nativeIds.Select(group => group.Key))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var tokens = owners[key].Select(claim => claim.Record.PawnLoadId)
                    .Concat(nativeIds[key].Select(aperture => aperture.pawn.GetUniqueLoadID()))
                    .Concat(anchorIds[key].Select(gu => gu.AnchorHint)).Where(token => token != null)
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (owners[key].Count() <= 1 && nativeIds[key].Count() <= 1 && tokens.Length <= 1) continue;
                foreach (var token in tokens) blocked.Add(token);
                RecordConflict($"aperture:{key.ToLowerInvariant()}", tokens,
                    owners[key].Select(claim => $"claimant:{claim.Record.Id}:{claim.Record.PawnLoadId}")
                        .Concat(nativeIds[key].Select(DescribeAperture))
                        .Concat(anchorIds[key].Select(gu => $"gu:{gu.Id}:{gu.AnchorHint}")),
                    "Conflicting aperture claims. No owner was selected.");
            }

            foreach (var entry in Evidence)
                foreach (var token in entry.PawnClaims) PawnClaims.Add(token);

            var guRefs = anchors.Select(gu => gu.AnchorHint).ToHashSet(StringComparer.Ordinal);
            foreach (var record in Records)
            {
                string token = record.PawnLoadId;
                IdentityClaims.Add(record.Id);
                CreationClaims.Add(record.CreationKey);
                if (token != null) PawnClaims.Add(token);
                if (token != null && guRefs.Contains(token)) record.Significant = true;
                bool uniqueId = identityIds[record.Id].Count() == 1;
                if (uniqueId) ById.Add(record.Id, record);
                if (record.Association != GuNativeAssociation.Bound)
                {
                    record.Pawn = null;
                    continue;
                }
                if (!uniqueId || creationKeys[record.CreationKey].Count() != 1
                    || token == null || pawnClaims[token].Count() != 1 || blocked.Contains(token)
                    || !IsCandidate(record.Pawn) || record.Pawn.GetUniqueLoadID() != token
                    || nativePawns[token].Count() != 1)
                {
                    record.Association = GuNativeAssociation.Unresolved;
                    record.Pawn = null;
                    // Planned IDs leave no ownership claim unless their Hediff write can commit.
                    record.ApertureIds.RemoveAll(stagedIds.ContainsKey);
                    continue;
                }
                ByPawn.Add(record.Pawn, record);
                foreach (var id in record.ApertureIds)
                {
                    ApertureClaims.Add(id);
                    var exact = stagedIds.TryGetValue(id, out var write) ? write.Aperture : nativeIds[id].SingleOrDefaultWhenUnique();
                    if (ReferenceEquals(exact?.pawn, record.Pawn)) ByAperture.Add(id, record);
                }
            }
        }

        private void ReserveApertureClaims(IEnumerable<Hediff_Aperture> apertures, IEnumerable<GuRecord> anchors)
        {
            foreach (var id in apertures.Select(aperture => aperture.ApertureId).Concat(anchors.Select(gu => gu.ContainerId))
                         .Concat(Records.SelectMany(record => record.ApertureIds)).Where(GuCultivatorRecord.IsId))
                ApertureClaims.Add(id);
        }

        private void RecordConflict(string key, string[] tokens, IEnumerable<string> claims, string reason)
        {
            string facts = string.Join("\n", claims.OrderBy(claim => claim, StringComparer.Ordinal));
            if (Evidence.Any(entry => (entry.Key == key || entry.Key.StartsWith(key + ":", StringComparison.Ordinal))
                && entry.RawEvidence == facts)) return;
            // Distinct observations under the same key preserve both sets of claimants.
            string evidenceKey = key;
            for (int suffix = 2; Evidence.Any(entry => entry.Key == evidenceKey); suffix++)
                evidenceKey = $"{key}:{suffix}";
            Evidence.Add(new GuCultivatorEvidence { Key = evidenceKey, PawnClaims = tokens.ToList(), RawEvidence = facts, Reason = reason });
        }

        internal static bool IsCandidate(Pawn pawn) =>
            pawn is { Discarded: false, health: not null, def: not null } && pawn.RaceProps?.Humanlike == true;

        private static bool IsNativeAperture(Hediff_Aperture aperture) => IsCandidate(aperture?.pawn)
            && aperture.def?.defName == "GU_Aperture" && aperture.pawn.health.hediffSet.hediffs.Contains(aperture);

        private static Hediff_Aperture[] NativeApertures(IEnumerable<Hediff_Aperture> apertures) =>
            apertures.Where(IsNativeAperture).Distinct<Hediff_Aperture>(ReferenceEqualityComparer.Instance).ToArray();

        private static GuRecord[] MortalAnchors(IEnumerable<GuRecord> guRecords) => guRecords.Where(gu => gu != null
            && gu.Placement == GuPlacementKind.Contained && gu.ContainerKind == GuContainerKind.MortalAperture
            && GuLegacyCultivatorRow.IsPawnKey(gu.AnchorHint)).ToArray();

        private static string DescribeAperture(Hediff_Aperture aperture) =>
            $"hediff:{aperture.GetUniqueLoadID()}:{aperture.pawn.GetUniqueLoadID()}:{aperture.ApertureId}";
    }

    internal static class GuCultivatorUniqueClaim
    {
        internal static T SingleOrDefaultWhenUnique<T>(this IEnumerable<T> candidates) where T : class
        {
            using var iterator = candidates.GetEnumerator();
            if (!iterator.MoveNext()) return null;
            var candidate = iterator.Current;
            return iterator.MoveNext() ? null : candidate;
        }
    }
}
