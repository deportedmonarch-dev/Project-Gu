using System;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu
{
    /// <summary>Transactional entry points for mortal will-enforcement refinement.</summary>
    public sealed partial class GuIndividualRegistry
    {
        private bool TryGetRefiner(Pawn pawn, out GuCultivatorRecord cultivator, out Hediff_Aperture aperture)
        {
            cultivator = null;
            aperture = null;
            var world = Current.Game?.GetComponent<GuWorldComponent>();
            if (!CanUseCustody || world?.FindCultivator(pawn) is not { State: AwakeningState.Awakened } found
                || pawn?.IsColonistPlayerControlled != true || pawn.Dead || pawn.Downed || !IsMapOwned(pawn)) return false;
            var matches = pawn.health?.hediffSet?.hediffs
                .OfType<Hediff_Aperture>().Where(hediff => hediff.def == GuWorldComponent.ApertureDef).Take(2).ToArray();
            if (matches is not { Length: 1 } || string.IsNullOrWhiteSpace(matches[0].ApertureId)
                || !found.ApertureIds.Contains(matches[0].ApertureId, StringComparer.OrdinalIgnoreCase)) return false;
            cultivator = found;
            aperture = matches[0];
            return true;
        }

        internal bool CanOrderRefinement(Thing_GuDormantProjection thing, Pawn pawn) =>
            CanOrderRefinement(thing, pawn, out _);

        internal bool CanOrderRefinement(Thing_GuDormantProjection thing, Pawn pawn, out GuRefinementQuote quote)
        {
            quote = default;
            if (!TryGetRefiner(pawn, out var cultivator, out var aperture)
                || Resolve(thing) is not { IsActionable: true, Condition: GuCondition.Dormant,
                    Placement: GuPlacementKind.Manifested, Manifestation: GuManifestationKind.Thing } record
                || record.Rank > 5 || record.RefinementProcess is { IsValid: false }
                || string.Equals(record.Refinement?.CultivatorId, cultivator.CultivatorId, StringComparison.OrdinalIgnoreCase)
                || !GuRefinementRules.TryQuote(record.Species, record.Rank, record.Refinement?.CultivatorId,
                    cultivator.CultivatorId, aperture, out quote)) return false;

            if (record.RefinementProcess is { } process
                && (!string.Equals(process.CultivatorId, cultivator.CultivatorId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(process.PreviousOwnerId, record.Refinement?.CultivatorId, StringComparison.OrdinalIgnoreCase))) return false;

            if (IsInInventory(thing, InventoryOf(pawn))) return true;
            return IsMapOwned(thing) && thing.Map == pawn.Map && CanPack(pawn, InventoryOf(pawn), thing)
                && pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, pawn.NormalMaxDanger());
        }

        internal bool TryBeginOrResumeRefinement(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn,
            out string processId, out GuRefinementProcess process, out string reason)
        {
            processId = null;
            process = null;
            reason = "The Gu or awakened cultivator is unavailable for will-enforcement.";
            if (!CanOrderRefinement(thing, pawn, out var quote) || Resolve(thing) is not { } record
                || record.CustodyRevision != expectedRevision) return false;
            if (!TryGetRefiner(pawn, out var cultivator, out _)) return false;

            process = record.RefinementProcess;
            if (process == null)
            {
                process = new GuRefinementProcess(Guid.NewGuid().ToString("N"), cultivator.CultivatorId,
                    record.Refinement?.CultivatorId, quote.WorkTicks, quote.EssenceCost, Find.TickManager.TicksGame);
                if (!record.BeginRefinement(process))
                {
                    process = null;
                    reason = "Another refinement process already reserves this Gu.";
                    return false;
                }
            }
            processId = process.ProcessId;
            reason = null;
            return true;
        }

        internal bool TryAdvanceRefinement(Thing_GuDormantProjection thing, int expectedRevision, Pawn pawn,
            string processId, out bool completed, out string reason)
        {
            completed = false;
            reason = "The refinement target, process or cultivator changed; the Gu remains unrefined and in its current custody.";
            if (!CanUseCustody || Resolve(thing) is not { IsActionable: true, Condition: GuCondition.Dormant,
                    Placement: GuPlacementKind.Manifested, Manifestation: GuManifestationKind.Thing } record
                || record.CustodyRevision != expectedRevision || !IsInInventory(thing, InventoryOf(pawn))
                || record.RefinementProcess is not { IsValid: true } process || process.ProcessId != processId
                || !string.Equals(process.PreviousOwnerId, record.Refinement?.CultivatorId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(process.CultivatorId, TryGetRefiner(pawn, out var cultivator, out var aperture)
                    ? cultivator.CultivatorId : null, StringComparison.OrdinalIgnoreCase)) return false;

            if (process.CompletedWorkTicks >= process.TotalWorkTicks)
            {
                if (!record.CompleteRefinement(processId, process.PreviousOwnerId, process.CultivatorId, Find.TickManager.TicksGame))
                    return false;
                completed = true;
                reason = null;
                return true;
            }
            if (!process.TryAdvance(Find.TickManager.TicksGame, aperture))
            {
                reason = "Primeval essence is insufficient to continue; completed will-work and spent essence are preserved.";
                return false;
            }
            if (process.CompletedWorkTicks == process.TotalWorkTicks)
            {
                if (!record.CompleteRefinement(processId, process.PreviousOwnerId, process.CultivatorId, Find.TickManager.TicksGame))
                    return false;
                completed = true;
            }
            reason = null;
            return true;
        }

        internal bool TryAbandonRefinement(Thing_GuDormantProjection thing, Pawn pawn, out string reason)
        {
            reason = "Select an accessible dormant Gu with an active refinement attempt.";
            if (!CanUseCustody || pawn?.IsColonistPlayerControlled != true || pawn.Downed || !IsMapOwned(pawn)
                || Resolve(thing) is not { IsActionable: true, RefinementProcess: { IsValid: true } process } record
                || !(IsInInventory(thing, InventoryOf(pawn))
                    || IsMapOwned(thing) && thing.Map == pawn.Map && pawn.Position.DistanceToSquared(thing.Position) <= 100))
                return false;
            if (!record.AbandonRefinement(process.ProcessId)) return false;
            reason = "The refinement attempt was abandoned. Its essence remains spent; Gu identity, custody and allegiance are unchanged.";
            return true;
        }

        internal bool CanAbandonRefinement(Thing_GuDormantProjection thing, Pawn pawn) =>
            CanUseCustody && pawn?.IsColonistPlayerControlled == true && !pawn.Downed && IsMapOwned(pawn)
            && Resolve(thing) is { IsActionable: true, RefinementProcess: { IsValid: true } }
            && (IsInInventory(thing, InventoryOf(pawn))
                || IsMapOwned(thing) && thing.Map == pawn.Map && pawn.Position.DistanceToSquared(thing.Position) <= 100);
    }
}
