using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Operator recovery (owner decisions §8). Inspection is read-only. Each repair is an explicit Development Mode action
    /// that re-checks the evidence when it runs, records an audit entry, and never authorizes a rogue projection blindly,
    /// invents a historical fact or creates a second individual.
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        internal const string OperatorActor = "operator";

        internal IEnumerable<GuRecord> RecordsNeedingReview => records.Where(record => record != null && record.IsLiving
            && (record.IsQuarantined || record.IsUnresolved));

        internal IEnumerable<CompGuManifestation> RogueCandidates(GuRecord record) =>
            rogues.Where(tag => tag?.parent != null && string.Equals(tag.GuId, record?.Id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Read-only operator report: state, observed form, evidence history, rogue candidates and available actions.</summary>
        internal string DescribeForOperator(GuRecord record)
        {
            var candidates = RogueCandidates(record).ToArray();
            var actions = new List<string>();
            if (AdoptionBlocker(record, candidates.Length == 1 ? candidates[0] : null) == null) actions.Add("adopt the sole rogue candidate");
            if (LostDeclarationBlocker(record) == null) actions.Add("declare lost");
            if (QuarantineLiftBlocker(record) == null) actions.Add("lift quarantine");
            return record.DebugDescription + $"\nObserved: {Observe(record).Form}"
                + "\nEvidence:\n" + record.HistoryDescription
                + (candidates.Length == 0 ? "" : "\nRogue candidates: " + string.Join(", ", candidates.Select(tag =>
                    $"{tag.parent.GetUniqueLoadID()} (tag revision {tag.GuRevision}, {(tag.parent.Spawned ? "spawned" : tag.parent.ParentHolder?.GetType().Name ?? "detached")})")))
                + "\nOperator actions: " + (actions.Count == 0 ? "none (the evidence does not support a repair)" : string.Join("; ", actions));
        }

        // ---- Adopt a rogue projection ------------------------------------------------------------------------

        private string AdoptionBlocker(GuRecord record, CompGuManifestation tag)
        {
            if (record == null || !record.IsLiving) return "No living record claims this identity.";
            if (record.IsQuarantined) return "The record is quarantined. Resolve the contradiction first.";
            if (record.Unresolved != GuUnresolvedCause.ProjectionMissing || record.TerminalEvidence != GuTerminalEvidence.None)
                return "Only a record whose projection is missing without other evidence can adopt a candidate.";
            var candidates = RogueCandidates(record).Where(candidate => candidate.parent is { Destroyed: false, Discarded: false }).ToArray();
            if (candidates.Length != 1 || tag == null || candidates[0] != tag) return "Adoption needs exactly one live rogue candidate.";
            var thing = tag.parent;
            if (byProjection.ContainsKey(thing)) return "Another record already binds this native object.";
            // A single candidate is not evidence by itself. It must carry the exact custody revision the record last authorized
            // and the same manifestation form, so a stale or unrelated copy can never be adopted.
            if (tag.GuRevision != record.CustodyRevision) return "The candidate's tag revision differs from the record's last authorized custody revision.";
            if ((thing is Pawn) != (record.Manifestation == GuManifestationKind.Pawn)) return "The candidate's form differs from the record's last manifestation.";
            var species = record.Species;
            if (species == null) return "The species is not loaded.";
            bool matches = record.Condition == GuCondition.Active
                ? thing is Pawn_GuWildProjection pawn && !pawn.Dead && pawn.kindDef == species.wildProjectionKind
                : thing is Thing_GuDormantProjection dormant && dormant.def == species.dormantProjectionDef && dormant.stackCount == 1;
            return matches ? null : "The candidate's form does not match the record's species and condition.";
        }

        /// <summary>
        /// Binds the single live rogue candidate of a record whose projection is missing. The custody revision advances and
        /// the tag is rewritten, so any other copy of the old tag can never authorize.
        /// </summary>
        internal bool TryAdoptRogue(CompGuManifestation tag, string note, out string reason)
        {
            var record = FindRecord(tag?.GuId);
            reason = "Recovery requires Development Mode and no transition in progress.";
            if (!CanUseDevelopmentCustody) return false;
            reason = "Adoption is an explicit operator repair and needs a recorded reason.";
            if (string.IsNullOrWhiteSpace(note)) return false;
            if ((reason = AdoptionBlocker(record, tag)) != null) return false;
            int tick = Find.TickManager.TicksGame;
            var thing = tag.parent;
            record.CommitManifestation(thing is Pawn ? GuManifestationKind.Pawn : GuManifestationKind.Thing, thing.GetUniqueLoadID());
            tag.Bind(record);
            record.ClearUnresolved();
            BindProjection(record, thing);
            record.AddHistory(new(tick, GuLifecycleEventKind.Operator, "AdoptedRogue", null, OperatorActor, LocationOf(thing),
                $"{note} Adopted native object {thing.GetUniqueLoadID()} at tag revision {record.CustodyRevision - 1}."));
            reason = null;
            return true;
        }

        // ---- Declare lost ------------------------------------------------------------------------------------

        private static string LostDeclarationBlocker(GuRecord record)
        {
            if (record == null || !record.IsLiving) return "No living record.";
            if (record.IsQuarantined) return "A quarantined record keeps its contradiction for review.";
            if (!record.IsUnresolved) return "Only an unresolved record can be declared lost. An actionable Gu is not lost.";
            return null;
        }

        /// <summary>An explicit operator decision that an unresolved Gu is irrecoverable. Recorded as such, not as a native event.</summary>
        internal bool TryDeclareLost(string id, string note, out string reason)
        {
            var record = FindRecord(id);
            reason = "Recovery requires Development Mode and no transition in progress.";
            if (!CanUseDevelopmentCustody) return false;
            if ((reason = LostDeclarationBlocker(record)) != null) return false;
            string key = $"operator-lost:{record.Id}";
            record.EndLife(GuTerminalOutcome.Lost, key, OperatorActor, record.LastKnown,
                note ?? $"Operator declared the Gu lost while unresolved ({record.Unresolved}).", Find.TickManager.TicksGame);
            byEndEvent[key] = record;
            reason = null;
            return true;
        }

        // ---- Lift a quarantine ---------------------------------------------------------------------------------

        private string QuarantineLiftBlocker(GuRecord record)
        {
            if (record == null || !record.IsQuarantined) return "The record is not quarantined.";
            if (record.QuarantineReason.StartsWith("Retained from Schema V1", StringComparison.Ordinal))
                return "A failed V1 transaction cannot be re-verified from the current save.";
            if (RecordError(record) is { } error) return error;
            if (records.Count(other => other != null && string.Equals(other.Id, record.Id, StringComparison.OrdinalIgnoreCase)) > 1)
                return "Another record still claims this ID.";
            if (!string.IsNullOrEmpty(record.CreationEventKey)
                && records.Count(other => other?.CreationEventKey == record.CreationEventKey) > 1)
                return "Another record still claims this creation event.";
            if (record.Origin == GuRecord.CohortOrigin && !record.CohortDebitCommitted)
                return "The cohort debit was never committed, so conservation is uncertain.";
            if (record.Placement == GuPlacementKind.Manifested && records.Any(other => other != record && other != null && other.IsLiving
                    && other.Placement == GuPlacementKind.Manifested && other.ProjectionId == record.ProjectionId))
                return "Another record still claims the same native object.";
            if (record.QuarantineReason.StartsWith("The authorized native object disagrees", StringComparison.Ordinal)
                && projectionOf.ContainsKey(record.Id))
                return "The native object is still bound elsewhere.";
            return null;
        }

        /// <summary>
        /// Lifts a quarantine after re-checking that its contradiction no longer holds. The record becomes unresolved, not
        /// actionable: rebinding still needs its exact native object or an explicit adoption.
        /// </summary>
        internal bool TryLiftQuarantine(string id, string note, out string reason)
        {
            var record = FindRecord(id);
            reason = "Recovery requires Development Mode and no transition in progress.";
            if (!CanUseDevelopmentCustody) return false;
            if ((reason = QuarantineLiftBlocker(record)) != null) return false;
            int tick = Find.TickManager.TicksGame;
            string previous = record.QuarantineReason;
            record.LiftQuarantine();
            record.MarkUnresolved(record.Placement == GuPlacementKind.Manifested ? GuUnresolvedCause.ProjectionMissing : GuUnresolvedCause.HostUnavailable,
                GuTerminalEvidence.None, "Quarantine lifted by the operator. The record must rebind through evidence.", tick);
            record.AddHistory(new(tick, GuLifecycleEventKind.Operator, "QuarantineLifted", null, OperatorActor, null, note ?? previous));
            reason = null;
            return true;
        }
    }
}
