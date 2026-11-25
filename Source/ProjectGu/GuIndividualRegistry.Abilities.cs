using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Read-only ability authorization. A Gu grants its species abilities only while it is a living, resolved individual
    /// contained in the live mortal aperture of the awakened cultivator it recognizes. Every other placement (inventory,
    /// ground, wild, another aperture, unresolved, quarantined or ended) grants nothing. No ability state is stored here.
    /// A withering (long-starved) Gu stays listed so its command can explain itself, but <see cref="IsAbilityGrantedBy"/>,
    /// which every cast, channel start and channel tick uses, refuses it (Alpha 5A).
    /// </summary>
    public sealed partial class GuIndividualRegistry
    {
        /// <summary>The caster's single live aperture when the pawn may act as an awakened Gu Master.</summary>
        internal bool TryGetAbilityCaster(Pawn pawn, out GuCultivatorRecord cultivator, out Hediff_Aperture aperture)
        {
            cultivator = null;
            aperture = null;
            if (!ready || CustodyTransitionInProgress || pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed
                || pawn.Map?.Disposed != false) return false;
            var world = Current.Game?.GetComponent<GuWorldComponent>();
            if (world?.FindCultivator(pawn) is not { State: AwakeningState.Awakened } found) return false;
            var matches = pawn.health?.hediffSet?.hediffs
                .OfType<Hediff_Aperture>().Where(hediff => hediff.def == GuWorldComponent.ApertureDef).Take(2).ToArray();
            if (matches is not { Length: 1 } || string.IsNullOrWhiteSpace(matches[0].ApertureId)
                || !found.ApertureIds.Contains(matches[0].ApertureId, StringComparer.OrdinalIgnoreCase)) return false;
            cultivator = found;
            aperture = matches[0];
            return true;
        }

        /// <summary>Aperture occupants that currently authorize their species abilities for this exact pawn.</summary>
        internal IEnumerable<GuRecord> AbilityGrantingOccupants(Pawn pawn, out Hediff_Aperture aperture)
        {
            if (!TryGetAbilityCaster(pawn, out var cultivator, out aperture)) return Enumerable.Empty<GuRecord>();
            var live = aperture;
            string ownerId = cultivator.CultivatorId;
            return ApertureOccupants(live).Where(record => GrantsAbilities(record, live, ownerId))
                .OrderBy(record => record.Id, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Revalidates one exact individual for one exact caster. Used before every irreversible commitment.</summary>
        internal bool IsAbilityGrantedBy(string guId, Pawn pawn, out GuRecord record, out Hediff_Aperture aperture)
        {
            record = null;
            aperture = null;
            if (!TryGetAbilityCaster(pawn, out var cultivator, out var live) || Resolve(guId) is not { } found
                || !apertureOf.TryGetValue(found.Id, out var bound) || bound != live
                || !GrantsAbilities(found, live, cultivator.CultivatorId)
                // A withering Gu stays listed (its command shows why) but cannot act: every commitment path checks here.
                || StarvationImpairment(found) != null) return false;
            record = found;
            aperture = live;
            return true;
        }

        private static bool GrantsAbilities(GuRecord record, Hediff_Aperture aperture, string ownerId) =>
            record is { IsActionable: true, RefinementProcess: null } && IsLiveAperture(aperture, record)
            && record.Species?.abilities is { Count: > 0 }
            && !string.IsNullOrEmpty(ownerId)
            && string.Equals(record.Refinement?.CultivatorId, ownerId, StringComparison.OrdinalIgnoreCase);
    }
}
