using System;
using System.Linq;
using ProjectGu.Presentation;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Combat
{
    /// <summary>
    /// The single authoritative projectile of one committed Gu cast. Native <see cref="Bullet"/> code moves it, saves it
    /// with the map, chooses what it hits and applies damage. This class adds an immutable cast ID, a saved "resolved"
    /// marker that allows exactly one impact, a report of that impact to the combat ledger, and an optional cosmetic cue
    /// issued only after the gameplay result exists. Cosmetic settings are never read on the gameplay path.
    /// </summary>
    public class Projectile_GuAbility : Bullet, IGuFxHeadingSource
    {
        private string castId;
        private string guId;
        private GuAbilityDef ability;
        private bool resolved;
        private bool abortedBeforeCommit;

        internal string CastId => castId;
        internal string GuId => guId;
        internal GuAbilityDef Ability => ability;
        internal bool Resolved => resolved;
        internal Vector3 Origin => origin;
        internal Vector3 Destination => destination;
        internal int TicksToImpact => ticksToImpact;

        public float FxHeading => ExactRotation.eulerAngles.y;

        internal void ConfigureCast(string castId, string guId, GuAbilityDef ability)
        {
            if (this.castId != null) throw new InvalidOperationException("A Gu projectile's cast identity is assigned once.");
            this.castId = castId;
            this.guId = guId;
            this.ability = ability;
        }

        /// <summary>Marks a projectile whose launch failed before commitment, so its removal reports nothing.</summary>
        internal void AbortBeforeCommit() => abortedBeforeCommit = true;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            GuCombatLedger.Instance?.NoteProjectileSpawned(castId);
            // Reload starts with no historical cosmetics; a live projectile re-attaches its own travel accent.
            if (respawningAfterLoad && !resolved && ability?.travelFx != null)
                GuFxMapComponent.TrySpawn(map, ability.travelFx, DrawPos, FxHeading, this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            GuCombatLedger.Instance?.NoteProjectileDespawned(castId);
            base.DeSpawn(mode);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var ledger = GuCombatLedger.Instance;
            if (resolved || ledger?.TryClaimImpact(castId) == false)
            {
                // A second terminal callback, or a cast the ledger has already settled: no second impact.
                resolved = true;
                if (!Destroyed) Destroy();
                return;
            }
            resolved = true;

            // Snapshot before native impact, which destroys this projectile and may move or kill the target.
            Map map = Map;
            Vector3 contact = ExactPosition;
            float heading = FxHeading;
            int amount = DamageAmount;
            float before = DamageProbe(hitThing);
            string hitId = hitThing?.GetUniqueLoadID();

            base.Impact(hitThing, blockedByShield);

            var outcome = blockedByShield ? GuCastOutcome.Blocked : hitThing != null ? GuCastOutcome.HitThing : GuCastOutcome.HitGround;
            float observed = hitThing == null ? 0f : Math.Max(0f, DamageProbe(hitThing) - before);
            ledger?.Resolve(castId, outcome, hitId, outcome == GuCastOutcome.HitThing ? amount : 0, observed);

            try
            {
                if (map is { Disposed: false }) GuFxMapComponent.TrySpawn(map, ability?.impactFx, contact, heading);
            }
            catch (Exception exception)
            {
                Log.WarningOnce($"[Project GU] A cosmetic impact cue failed; the impact is unaffected. {exception}", 0x6A55A12);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            bool unresolved = !resolved && !abortedBeforeCommit && !Destroyed;
            resolved = true;
            base.Destroy(mode);
            // Leaving the map or any other removal without contact ends the cast; essence stays spent.
            if (unresolved) GuCombatLedger.Instance?.Resolve(castId, GuCastOutcome.EndedWithoutImpact, null, 0, 0f);
        }

        /// <summary>Observed harm: total injury severity for pawns, lost hit points for other things.</summary>
        internal static float DamageProbe(Thing thing) => thing switch
        {
            null => 0f,
            Pawn pawn => pawn.health?.hediffSet?.hediffs.OfType<Hediff_Injury>().Sum(injury => injury.Severity) ?? 0f,
            _ => thing.def.useHitPoints ? thing.MaxHitPoints - thing.HitPoints : 0f
        };

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref castId, "guCastId");
            Scribe_Values.Look(ref guId, "guId");
            Scribe_Defs.Look(ref ability, "guAbility");
            Scribe_Values.Look(ref resolved, "guResolved");
        }
    }
}
