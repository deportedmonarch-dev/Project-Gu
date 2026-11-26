using System;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Sustenance
{
    /// <summary>
    /// Feeding operations. Reads never change records. The only writes are settling elapsed hunger, attaching a missing
    /// nourishment state, and committing a meal whose items were consumed in the same tick.
    /// </summary>
    public static class GuSustenance
    {
        internal const string FeedJobDefName = "GU_FeedGu";
        internal static JobDef FeedJob => DefDatabase<JobDef>.GetNamedSilentFail(FeedJobDefName);

        private static int Now => Find.TickManager?.TicksGame ?? 0;

        /// <summary>The individual's nourishment at this tick, projected without mutation.</summary>
        public static GuNourishmentSnapshot Snapshot(GuIndividualRegistry registry, GuRecord record)
        {
            var props = record?.Species?.feeding;
            var status = record?.Species?.FeedingStatus ?? GuFeedingStatus.NotImplemented;
            if (status != GuFeedingStatus.Simulated || registry == null)
                return new GuNourishmentSnapshot(status, GuNourishmentRules.LevelOf(props, 0, 0), GuSustenanceMode.Unavailable, 0, 0, 0, 0, 0, 0, false, false);
            int now = Now;
            var mode = registry.SustenanceModeOf(record);
            var state = record.GetState<GuNourishment>();
            bool hasState = state != null;
            state ??= GuNourishmentRules.Create(props, now, GuSustenanceComponent.Instance?.WouldBeLegacy(record) == true);
            var (nourishment, starving) = GuNourishmentRules.Project(state, now, state.mode == GuSustenanceMode.Advancing && mode == GuSustenanceMode.Advancing);
            return new GuNourishmentSnapshot(status, GuNourishmentRules.LevelOf(props, nourishment, starving), mode, nourishment, props.MaxTicks,
                starving, props.ImpairTicks, props.TerminalTicks, props.HungryTicks, hasState, state.graceUntilTick > now);
        }

        /// <summary>Attaches the state if missing and accounts hunger up to now. Returns null for unsimulated species.</summary>
        internal static GuNourishment Settle(GuIndividualRegistry registry, GuRecord record, int now)
        {
            if (record?.Species?.SimulatedFeeding is not { } props || registry == null || !record.IsLiving) return null;
            var state = record.GetState<GuNourishment>();
            var mode = registry.SustenanceModeOf(record);
            if (state == null)
            {
                state = GuNourishmentRules.Create(props, now, GuSustenanceComponent.Instance?.WouldBeLegacy(record) == true);
                state.mode = mode;
                if (!record.AttachState(state)) return null;
                return state;
            }
            GuNourishmentRules.Settle(state, now, mode);
            return state;
        }

        // ---- Ordering -----------------------------------------------------------------------------------------

        /// <summary>Plans a feeding job for this feeder and Gu. Nothing is reserved or consumed until the job runs.</summary>
        internal static bool TryMakeFeedJob(Pawn feeder, GuRecord record, bool automatic, out Job job, out GuMealPlan plan, out string reason)
        {
            job = null;
            plan = default;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var component = GuSustenanceComponent.Instance;
            reason = "Gu feeding is unavailable in this game.";
            if (registry == null || component == null || FeedJob is not { } jobDef) return false;
            var access = registry.FeedAccess(feeder, record, out var dormant, out reason);
            if (access == GuFeedAccess.None) return false;
            var props = record.Species.SimulatedFeeding;
            reason = "requires an undrafted colonist";
            if (feeder.Drafted) return false;
            var snapshot = Snapshot(registry, record);
            if (props.Worker is not { } worker || !worker.TryPlanMeal(feeder, snapshot.NourishmentTicks, out plan, out reason)) return false;
            job = JobMaker.MakeJob(jobDef, plan.Food, access == GuFeedAccess.Ground ? dormant : LocalTargetInfo.Invalid);
            job.count = plan.Count;
            component.RegisterIntent(job.loadID, new GuFeedIntent
            {
                GuId = record.Id,
                CustodyRevision = record.CustodyRevision,
                FeedRevision = record.GetState<GuNourishment>()?.FeedRevision ?? 0,
                FoodDefName = plan.Option.thingDef.defName,
                Meals = plan.Meals,
                Automatic = automatic,
                CreatedTick = Now
            });
            reason = null;
            return true;
        }

        /// <summary>A player order: plans and starts the feeding job, or explains why it cannot.</summary>
        internal static bool TryOrderFeed(Pawn feeder, GuRecord record, out string reason)
        {
            if (!TryMakeFeedJob(feeder, record, false, out var job, out _, out reason)) return false;
            job.playerForced = true;
            bool taken = feeder.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            // An ordered job that could not reserve its food ends at once; report it instead of silently doing nothing.
            if (taken && (feeder.CurJob == job || feeder.jobs.jobQueue.Contains(job))) return true;
            GuSustenanceComponent.Instance?.RemoveIntent(job.loadID);
            reason = $"{feeder.LabelShort} could not begin feeding.";
            return false;
        }

        // ---- Commitment ---------------------------------------------------------------------------------------

        /// <summary>
        /// The single irreversible step of feeding. In one tick: revalidate the exact individual and its custody revision,
        /// the feeder's authority, the held food and the remaining appetite; then consume the items and apply the meal.
        /// Returns false with nothing consumed when any check fails.
        /// </summary>
        internal static bool TryCommitFeeding(Pawn feeder, GuFeedIntent intent, Thing food, Thing groundTarget, out int meals, out string reason)
        {
            meals = 0;
            reason = "The feeding order is no longer valid.";
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (registry == null || intent?.GuId == null || food == null || food.Destroyed) return false;
            if (registry.Resolve(intent.GuId) is not { } record || record.CustodyRevision != intent.CustodyRevision)
            {
                reason = "The Gu moved or changed custody before it could be fed.";
                return false;
            }
            var access = registry.FeedAccess(feeder, record, out var dormant, out reason);
            if (access == GuFeedAccess.None) return false;
            if (access == GuFeedAccess.Ground && (groundTarget != dormant || !feeder.CanReachImmediate(dormant, PathEndMode.Touch)))
            {
                reason = "The dormant Gu is no longer within reach.";
                return false;
            }
            var props = record.Species.SimulatedFeeding;
            var option = props.Worker?.OptionFor(food.def);
            reason = $"{record.Species.label} does not eat {food.LabelNoCount}.";
            if (option == null || option.thingDef.defName != intent.FoodDefName) return false;
            bool carried = feeder.carryTracker?.CarriedThing == food;
            bool inInventory = !carried && food.holdingOwner != null && food.holdingOwner == feeder.inventory?.innerContainer;
            reason = "The food is no longer in hand.";
            if (!carried && !inInventory) return false;

            int now = Now;
            var state = Settle(registry, record, now);
            if (state == null) return false;
            int wanted = GuNourishmentRules.MealsWanted(props, option, state.NourishmentTicks);
            meals = Math.Min(wanted, food.stackCount / option.mealCount);
            reason = wanted < 1 ? "The Gu is already sated." : $"Not enough {option.Label} for a meal.";
            if (meals < 1)
            {
                meals = 0;
                return false;
            }
            int count = meals * option.mealCount;
            Thing eaten = food.stackCount > count ? food.SplitOff(count) : food;
            eaten.Destroy(DestroyMode.Vanish);
            GuNourishmentRules.Feed(state, props, option, meals, now);
            state.lastFeedSource = intent.Automatic ? "automatic feeding job" : "fed by " + feeder.LabelShort;
            reason = null;
            return true;
        }
    }
}
