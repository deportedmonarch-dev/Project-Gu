using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Player command for one ability of one exact Gu individual. Selecting it starts native targeting with a range
    /// ring; choosing a target orders the cast job. Availability is recomputed from the registry every time the command
    /// is built, so a withdrawn, transferred or dead Gu stops offering it immediately.
    /// </summary>
    [StaticConstructorOnStartup]
    public sealed class Command_GuAbility : Command
    {
        private readonly Pawn caster;
        private readonly string guId;
        private readonly GuAbilityDef ability;

        internal Pawn Caster => caster;
        internal string GuId => guId;
        internal GuAbilityDef Ability => ability;

        internal Command_GuAbility(GuAbilityGrant grant, bool distinguishIndividual)
        {
            caster = grant.Caster;
            guId = grant.Gu.Id;
            ability = grant.Ability;
            icon = ability.Icon;
            string speciesLabel = grant.Gu.Species?.label ?? grant.Gu.SpeciesDefName;
            defaultLabel = distinguishIndividual ? $"{ability.LabelCap} ({grant.Gu.Id.Substring(0, 4)})" : ability.LabelCap.ToString();
            defaultDesc = $"{ability.description}\n\nGu: {speciesLabel} [{grant.Gu.Id}]\nCost: {ability.essenceCost:0.#} primeval essence"
                + $" (paid at launch)\nRange: {ability.range:0} cells\nAim time: {ability.warmupTicks.ToStringSecondsFromTicks()}"
                + $"\nRecovery: {ability.cooldownTicks.ToStringSecondsFromTicks()}\nAperture essence: {grant.Aperture.Essence:0.0}/{grant.Aperture.MaxEssence:0.0}";
            if (!GuAbilityService.CanCast(caster, guId, ability, out _, out string reason)) Disable(reason);
        }

        public override bool GroupsWith(Gizmo other) => false;

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            if (!GuAbilityService.CanCast(caster, guId, ability, out _, out string reason))
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var parameters = new TargetingParameters
            {
                canTargetPawns = ability.targetPawns,
                canTargetLocations = ability.targetLocations,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetSelf = false,
                mapObjectTargetsMustBeAutoAttackable = false
            };
            Find.Targeter.BeginTargeting(parameters,
                action: target =>
                {
                    if (!GuAbilityService.TryOrderCast(caster, guId, ability, target, out string failure))
                        Messages.Message(failure, MessageTypeDefOf.RejectInput, false);
                },
                highlightAction: target =>
                {
                    if (target.IsValid) GenDraw.DrawTargetHighlight(target);
                },
                targetValidator: target => target.IsValid && GuAbilityService.ValidateTarget(caster, ability, target, out _),
                caster: caster,
                mouseAttachment: ability.Icon,
                onUpdateAction: _ =>
                {
                    if (caster.Spawned) GenDraw.DrawRadiusRing(caster.Position, ability.range);
                });
        }
    }
}
