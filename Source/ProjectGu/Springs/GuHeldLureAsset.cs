using System;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Springs
{
    /// <summary>
    /// The exact bait stack a colonist holds for one lure intent at one spring (Alpha 6 Slice 0). Captured when the lure job's
    /// final toil asks the spring, revalidated by the registry before staging and after native callbacks, and spent once by
    /// the registry after the debit commits, inside the custody guard. It only constrains a lure: the spring authorizes it.
    /// </summary>
    internal sealed class GuHeldLureAsset : IGuSourceAsset
    {
        private readonly Pawn actor;
        private readonly Job job;
        private readonly Thing bait;
        private readonly int quantityAtIntent;

        internal CompGuSpiritSpring Spring { get; }
        public string Scope { get; }
        public string AssetId { get; }

        /// <param name="job">The native lure job, or null to settle an already committed intent on replay.</param>
        internal GuHeldLureAsset(CompGuSpiritSpring spring, Pawn actor, Job job, Thing bait)
        {
            Spring = spring;
            this.actor = actor;
            this.job = job;
            this.bait = bait;
            quantityAtIntent = bait?.stackCount ?? 0;
            Scope = bait?.def?.defName;
            AssetId = bait?.GetUniqueLoadID();
        }

        internal static bool HeldBy(Pawn pawn, Thing thing) => pawn != null && thing?.ParentHolder switch
        {
            Pawn_InventoryTracker inventory => inventory.pawn == pawn,
            Pawn_CarryTracker carry => carry.pawn == pawn,
            _ => false
        };

        /// <summary>Identity, quantity and holder of the exact bait.</summary>
        public string SettlementRefusal()
        {
            if (bait == null || bait.Destroyed || bait.Discarded || bait.def?.defName != Scope || bait.GetUniqueLoadID() != AssetId)
                return "The lure is gone.";
            if (bait.stackCount < 1 || bait.stackCount != quantityAtIntent) return "The lure in hand changed.";
            return HeldBy(actor, bait) ? null : "The lure is no longer in the colonist's hands.";
        }

        /// <summary>The bait, plus the actor and the native lure job that set it out at this spring.</summary>
        public string Refusal()
        {
            if (SettlementRefusal() is { } refusal) return refusal;
            if (actor is not { Spawned: true, Dead: false, Downed: false } || actor.Faction != Faction.OfPlayer
                || Spring?.parent is not { Spawned: true } spring || actor.Map != spring.Map)
                return "The colonist can no longer tend the lure at this spring.";
            if (job == null || actor.CurJob != job || job.def?.driverClass != typeof(JobDriver_GuLureWildGu) || job.targetA.Thing != spring)
                return "The lure was abandoned.";
            return null;
        }

        /// <summary>Spends one unit. True only when the exact stack is observed one unit lighter or gone from the holder.</summary>
        public bool TrySettle()
        {
            if (SettlementRefusal() != null) return false;
            int before = bait.stackCount;
            try
            {
                if (before > 1) bait.SplitOff(1).Destroy();
                else bait.Destroy();
            }
            catch (Exception exception)
            {
                try { Log.Warning($"[Project GU] Spending lure {AssetId} was interrupted. {exception.Message}"); }
                catch { }
            }
            return before > 1 ? !bait.Destroyed && bait.stackCount == before - 1 : bait.Destroyed || !HeldBy(actor, bait);
        }
    }
}
