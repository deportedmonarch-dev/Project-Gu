using System;
using System.Collections.Generic;
using HarmonyLib;
using ProjectGu.Presentation;
using Verse;

namespace ProjectGu.Combat
{
    public sealed class GuSustainedProperties
    {
        public HediffDef condition;
        public int intervalTicks = 250;
        public float essencePerInterval = 1f;
        public GuFxProfileDef manifestationFx;

        internal IEnumerable<string> ConfigErrors(string ability)
        {
            if (condition?.hediffClass != typeof(Hediff_GuSustainedEffect) || condition.defaultInstallPart != null
                || condition.stages.NullOrEmpty() || condition.duplicationAllowed || !condition.forceRemoveOnResurrection)
                yield return $"{ability}: the sustained condition needs a whole-body Hediff_GuSustainedEffect with stages, no duplication and removal on resurrection";
            if (intervalTicks is < 1 or > 60000)
                yield return $"{ability}: sustained intervalTicks must be between 1 and 60000";
            if (!float.IsFinite(essencePerInterval) || essencePerInterval is <= 0 or > 1000)
                yield return $"{ability}: sustained essencePerInterval must be finite, positive and at most 1000";
        }
    }

    public sealed class GuAbilityWorker_Sustained : GuAbilityWorker
    {
        internal static bool CanActivate(Pawn caster, GuAbilityDef ability, out string reason)
        {
            reason = "Sustained Gu techniques are unavailable.";
            if (!GuSustainedTick.Installed || !GuCustodySaveGuard.Installed || ability.sustained.condition == null
                || GuCombatLedger.Instance is not { SustainedActivationInProgress: false }) return false;
            reason = "Only a player-controlled Gu Master can activate this technique.";
            if (!caster.IsColonistPlayerControlled) return false;
            reason = $"{ability.LabelCap} is already active on this Gu Master.";
            if (caster.health.hediffSet.GetFirstHediffOfDef(ability.sustained.condition) != null) return false;
            reason = null;
            return true;
        }

        internal static bool TryActivate(Pawn caster, string guId, GuAbilityDef ability, out string reason)
        {
            reason = "This is not a sustained self-use technique.";
            return ability?.IsSustained == true && GuAbilityService.TryCommit(caster,
                new GuCastIntent { GuId = guId, Ability = ability }, caster, out _, out reason);
        }

        public override bool ValidateTarget(Pawn caster, LocalTargetInfo target, out string reason)
        {
            reason = target.Thing == caster ? null : "This technique can only affect its caster.";
            return reason == null;
        }

        public override bool TryLaunch(GuCastContext context, LocalTargetInfo target, out Thing launched, out string reason)
        {
            launched = null;
            if (!CanActivate(context.Caster, def, out reason)) return false;
            var ledger = GuCombatLedger.Instance;
            var effect = (Hediff_GuSustainedEffect)HediffMaker.MakeHediff(def.sustained.condition, context.Caster);
            bool committed = false;
            ledger.SustainedActivationInProgress = true;
            try
            {
                effect.Bind(context);
                context.Caster.health.AddHediff(effect);
                reason = "The sustained condition could not be established.";
                if (Hediff_GuSustainedEffect.Of(context.Caster, effect.def) != effect || !effect.TryBegin()) return false;
                committed = true;
                reason = null;
                return true;
            }
            finally
            {
                try { if (!committed) effect.Stop(); }
                finally { ledger.SustainedActivationInProgress = false; }
            }
        }
    }

    /// <summary>The one saved lease and native stat source. A staged condition has no stage until payment commits.</summary>
    public sealed class Hediff_GuSustainedEffect : Hediff
    {
        private string activationId;
        private string casterId;
        private string guId;
        private int guRevision;
        private Hediff_Aperture aperture;
        private GuAbilityDef ability;
        private int paidUntil = -1;
        private int nextVisualAttempt;
        private string VisualKey => $"gu-sustained/{activationId}";

        internal string GuId => guId;
        internal GuAbilityDef Ability => ability;
        internal int PaidUntil => paidUntil;
        private int Now => Find.TickManager.TicksGame;
        internal bool IsProtecting => paidUntil > (Find.TickManager?.TicksGame ?? int.MaxValue) && StillAuthorized();

        public override HediffStage CurStage => IsProtecting ? base.CurStage : null;
        public override bool Visible => IsProtecting;
        public override bool ShouldRemove => paidUntil < 0;
        public override bool TryMergeWith(Hediff other) => false;

        internal static Hediff_GuSustainedEffect Of(Pawn pawn, HediffDef condition) =>
            pawn?.health?.hediffSet?.GetFirstHediffOfDef(condition) as Hediff_GuSustainedEffect;

        internal void Bind(GuCastContext context)
        {
            activationId = context.CastId;
            casterId = context.Caster.GetUniqueLoadID();
            guId = context.Gu.Id;
            guRevision = context.Gu.CustodyRevision;
            aperture = context.Aperture;
            ability = context.Ability;
        }

        internal bool TryBegin()
        {
            if (paidUntil >= 0 || !StillAuthorized()) return false;
            paidUntil = Now + ability.sustained.intervalTicks;
            pawn.health.hediffSet.DirtyCache();
            return IsProtecting && Of(pawn, def) == this;
        }

        private bool StillAuthorized() => ability?.sustained?.condition == def && activationId != null
            && pawn?.GetUniqueLoadID() == casterId && pawn?.IsColonistPlayerControlled == true
            && Current.Game?.GetComponent<GuIndividualRegistry>() is { } registry
            && registry.IsAbilityGrantedBy(guId, pawn, out var gu, out var live) && live == aperture
            && gu.CustodyRevision == guRevision && gu.Species.abilities.Contains(ability);

        internal bool ValidAfterLoad()
        {
            if (!IsProtecting || Of(pawn, def) != this || paidUntil - Now > ability.sustained.intervalTicks) return false;
            pawn.health.hediffSet.DirtyCache();
            return true;
        }

        internal void Maintain(int now)
        {
            if (paidUntil < now || !StillAuthorized() || Of(pawn, def) != this)
            {
                Stop();
                return;
            }
            if (paidUntil == now && !aperture.TrySpendEssenceForCast(ability.sustained.essencePerInterval, () =>
                {
                    paidUntil = now + ability.sustained.intervalTicks;
                    return true;
                }))
            {
                Stop();
                return;
            }

            if (now < nextVisualAttempt || ability.sustained.manifestationFx is not { } profile) return;
            nextVisualAttempt = now + 60;
            try { GuFxMapComponent.TryAttachSustained(pawn.Map, profile, pawn, VisualKey, () => IsProtecting); }
            catch (Exception exception)
            {
                Log.WarningOnce($"[Project GU] Sustained Gu visuals could not be attached. {exception}", 0x6A55A12);
            }
        }

        internal void Stop()
        {
            paidUntil = -1;
            if (pawn?.health?.hediffSet?.hediffs.Contains(this) == true) pawn.health.RemoveHediff(this);
            else GuCombatLedger.Instance?.Untrack(this);
        }

        // Health notifications can run inside native iteration. Disable immediately and remove on the next normal tick.
        internal static void Invalidate(Hediff_Aperture source, string guId = null)
        {
            foreach (var condition in source.pawn.health.hediffSet.hediffs)
                if (condition is Hediff_GuSustainedEffect effect && effect.aperture == source && (guId == null || effect.guId == guId))
                    effect.Invalidate();
        }

        private void Invalidate()
        {
            if (paidUntil < 0) return;
            paidUntil = -1;
            pawn.health.hediffSet.DirtyCache();
        }

        public override void Notify_Downed() => Invalidate();
        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit) => Invalidate();

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            GuCombatLedger.Instance?.Track(this);
        }

        public override void PostRemoved()
        {
            paidUntil = -1;
            GuCombatLedger.Instance?.Untrack(this);
            pawn.MapHeld?.GetComponent<GuFxMapComponent>()?.Detach(VisualKey);
            base.PostRemoved();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activationId, "guSustainedActivationId");
            Scribe_Values.Look(ref casterId, "guSustainedCasterId");
            Scribe_Values.Look(ref guId, "guSustainedGuId");
            Scribe_Values.Look(ref guRevision, "guSustainedGuRevision");
            Scribe_References.Look(ref aperture, "guSustainedAperture");
            Scribe_Defs.Look(ref ability, "guSustainedAbility");
            Scribe_Values.Look(ref paidUntil, "guSustainedPaidUntil", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) GuCombatLedger.Instance?.Track(this);
        }
    }

    /// <summary>The normal tick list runs after the tick advances and before any pawn or projectile acts.</summary>
    [StaticConstructorOnStartup]
    internal static class GuSustainedTick
    {
        internal static bool Installed { get; }

        static GuSustainedTick()
        {
            try
            {
                new Harmony("projectgu.livingcultivation.sustained").Patch(AccessTools.Method(typeof(TickList), nameof(TickList.Tick)),
                    prefix: new HarmonyMethod(typeof(GuSustainedTick), nameof(BeforeTickList)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Sustained Gu upkeep could not be installed. {exception}");
            }
        }

        private static void BeforeTickList(TickerType ___tickType)
        {
            if (___tickType == TickerType.Normal) GuCombatLedger.Instance?.MaintainSustained(Find.TickManager.TicksGame);
        }
    }
}
