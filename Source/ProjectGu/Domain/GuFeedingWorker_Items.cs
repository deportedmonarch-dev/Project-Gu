using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Domain
{
    /// <summary>
    /// Default feeding worker: meals are physical RimWorld items. Food comes from the feeder's own inventory first, then
    /// from unforbidden, reservable, reachable stacks on the feeder's own map. Nothing is ever taken from another map.
    /// </summary>
    public sealed class GuFeedingWorker_Items : GuFeedingWorker
    {
        /// <summary>Feeding jobs reserve part of a stack, so up to this many feeders may share one stack.</summary>
        internal const int MaxPawnsPerStack = 10;

        public override bool TryPlanMeal(Pawn feeder, int nourishment, out GuMealPlan plan, out string reason)
        {
            plan = default;
            reason = "This Gu does not eat physical food.";
            if (props?.foods == null || props.foods.Count == 0 || feeder == null) return false;
            bool anyWanted = false;
            foreach (var option in props.foods)
            {
                int wanted = GuNourishmentRules.MealsWanted(props, option, nourishment);
                if (wanted < 1) continue;
                anyWanted = true;
                var food = FindFood(feeder, option, out bool fromInventory);
                if (food == null) continue;
                int meals = Math.Min(wanted, food.stackCount / option.mealCount);
                if (!fromInventory)
                {
                    int space = feeder.carryTracker?.AvailableStackSpace(food.def) ?? 0;
                    meals = Math.Min(meals, space / option.mealCount);
                }
                // The job reserves its whole count; plan only what this feeder can actually reserve now.
                while (meals > 0 && !fromInventory && !feeder.CanReserve(food, MaxPawnsPerStack, meals * option.mealCount)) meals--;
                if (meals < 1) continue;
                plan = new GuMealPlan(food, option, meals);
                reason = null;
                return true;
            }
            reason = anyWanted ? $"no {DietList()} available to {feeder.LabelShort} on this map" : "it is sated";
            return false;
        }

        public override int CountAvailable(Pawn feeder, GuFoodOption option)
        {
            if (feeder == null || option?.thingDef == null) return 0;
            int count = 0;
            if (feeder.inventory?.innerContainer is { } inventory)
                foreach (var thing in inventory)
                    if (thing.def == option.thingDef) count += thing.stackCount;
            if (feeder.Spawned && feeder.Map is { } map)
                foreach (var thing in map.listerThings.ThingsOfDef(option.thingDef))
                    if (thing.Spawned && !thing.IsForbidden(feeder)) count += thing.stackCount;
            return count;
        }

        private static Thing FindFood(Pawn feeder, GuFoodOption option, out bool fromInventory)
        {
            fromInventory = false;
            if (feeder.inventory?.innerContainer is { } inventory)
                foreach (var thing in inventory)
                    if (thing.def == option.thingDef && thing.stackCount >= option.mealCount)
                    {
                        fromInventory = true;
                        return thing;
                    }
            if (!feeder.Spawned || feeder.Map is not { } map) return null;
            return GenClosest.ClosestThingReachable(feeder.Position, map, ThingRequest.ForDef(option.thingDef), PathEndMode.ClosestTouch,
                TraverseParms.For(feeder, Danger.Some), 9999f, thing => Usable(feeder, thing, option));
        }

        internal static bool Usable(Pawn feeder, Thing thing, GuFoodOption option) =>
            thing is { Spawned: true, Destroyed: false } && thing.def == option.thingDef && thing.stackCount >= option.mealCount
            && !thing.IsForbidden(feeder) && !thing.IsBurning()
            && feeder.CanReserve(thing, MaxPawnsPerStack, option.mealCount);

        private string DietList()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var food in props.foods)
                if (food?.thingDef != null) names.Add(food.thingDef.label);
            return names.Count == 0 ? "food" : string.Join(" or ", names);
        }
    }
}
