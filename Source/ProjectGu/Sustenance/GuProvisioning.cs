using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Sustenance
{
    /// <summary>
    /// One place automatic feeding may draw real food from, in the order given. Alpha 5B declares the cultivator's own
    /// inventory and the colony's stockpiles on the cultivator's map. A future Storage Gu, pantry or logistics network adds
    /// another def with its own worker; the provisioning service never changes.
    /// </summary>
    public sealed class GuProvisionSourceDef : Def
    {
        public Type workerClass;
        /// <summary>Lower orders are tried first.</summary>
        public int order;

        [Unsaved] private GuProvisionSource worker;

        public GuProvisionSource Worker
        {
            get
            {
                if (worker == null && workerClass != null && typeof(GuProvisionSource).IsAssignableFrom(workerClass) && !workerClass.IsAbstract)
                {
                    worker = (GuProvisionSource)Activator.CreateInstance(workerClass);
                    worker.def = this;
                }
                return worker;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (workerClass == null || !typeof(GuProvisionSource).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
                yield return "workerClass must be a concrete ProjectGu.Sustenance.GuProvisionSource";
        }
    }

    /// <summary>A physical stack automatic feeding may take from, and how many of its items are free to take now.</summary>
    public readonly struct GuSupply
    {
        public readonly Thing Thing;
        public readonly int Available;
        public readonly GuProvisionSourceDef Source;

        public GuSupply(Thing thing, int available, GuProvisionSourceDef source)
        {
            Thing = thing;
            Available = available;
            Source = source;
        }
    }

    /// <summary>
    /// Extension boundary for where automatic feeding finds food. A source lists real stacks that are legitimately accessible
    /// to the cultivator now, and revalidates one at the moment of consumption. It never creates or moves anything.
    /// </summary>
    public abstract class GuProvisionSource
    {
        public GuProvisionSourceDef def;

        /// <summary>Adds the stacks of this food the cultivator may use now, with the free count of each.</summary>
        public abstract void CollectSupplies(Pawn cultivator, ThingDef food, List<GuSupply> into);

        /// <summary>How many items of this exact stack are free to take at this instant (0 if it is no longer legitimate).</summary>
        public abstract int Revalidate(Pawn cultivator, Thing thing);

        /// <summary>Short player-facing name, for example "inventory" or "stockpile".</summary>
        public virtual string Label => def?.label ?? "supply";
    }

    /// <summary>The cultivator's own inventory. Passage-hauling cargo in transit is excluded.</summary>
    public sealed class GuProvisionSource_CultivatorInventory : GuProvisionSource
    {
        public override void CollectSupplies(Pawn cultivator, ThingDef food, List<GuSupply> into)
        {
            if (cultivator?.inventory?.innerContainer is not { } inventory) return;
            for (int i = 0; i < inventory.Count; i++)
            {
                int free = Revalidate(cultivator, inventory[i]);
                if (inventory[i].def == food && free > 0) into.Add(new GuSupply(inventory[i], free, def));
            }
        }

        public override int Revalidate(Pawn cultivator, Thing thing)
        {
            if (thing == null || thing.Destroyed || cultivator?.inventory?.innerContainer is not { } inventory || thing.holdingOwner != inventory) return 0;
            if (Current.Game?.GetComponent<GuPocketProbeComponent>()?.HaulingSiteFor(cultivator)?.Hauling?.Cargo == thing) return 0;
            return thing.stackCount;
        }
    }

    /// <summary>
    /// The colony's stockpiles and storage on the map the cultivator stands on: stacks in valid player storage that are not
    /// forbidden, not burning, reachable from the cultivator, and not claimed by another pawn's reservation. Loose items on
    /// the ground, other maps and other factions' storage never count.
    /// </summary>
    public sealed class GuProvisionSource_ColonyStockpile : GuProvisionSource
    {
        public override void CollectSupplies(Pawn cultivator, ThingDef food, List<GuSupply> into)
        {
            if (cultivator is not { Spawned: true } || cultivator.Map is not { } map) return;
            var stacks = map.listerThings.ThingsOfDef(food);
            for (int i = 0; i < stacks.Count; i++)
            {
                int free = Revalidate(cultivator, stacks[i]);
                if (free > 0) into.Add(new GuSupply(stacks[i], free, def));
            }
        }

        public override int Revalidate(Pawn cultivator, Thing thing)
        {
            if (thing is not { Spawned: true, Destroyed: false } || cultivator is not { Spawned: true } || thing.Map != cultivator.Map) return 0;
            if (thing.IsForbidden(Faction.OfPlayer) || thing.IsBurning() || !StoreUtility.IsInValidStorage(thing)) return 0;
            var owner = thing.GetSlotGroup()?.parent;
            if (owner is Thing building && building.Faction != Faction.OfPlayer) return 0;
            if (owner is not Thing && owner is not Zone) return 0;
            var reservations = thing.Map.reservationManager;
            if (reservations.ReservedBy(thing, cultivator)) return 0;
            int free = reservations.IsReservedByAnyoneOf(thing, cultivator.Faction)
                ? reservations.CanReserveStack(cultivator, thing, GuFeedingWorker_Items.MaxPawnsPerStack)
                : thing.stackCount;
            if (free <= 0) return 0;
            return cultivator.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly) ? Math.Min(free, thing.stackCount) : 0;
        }
    }

    /// <summary>What one provisioning attempt did, for the interface and the tests.</summary>
    public readonly struct GuProvisionResult
    {
        public readonly bool Fed;
        public readonly int Meals;
        public readonly int Items;
        public readonly string FoodDefName;
        public readonly string SourceLabel;
        public readonly string Reason;

        public GuProvisionResult(bool fed, int meals, int items, string food, string source, string reason)
        {
            Fed = fed;
            Meals = meals;
            Items = items;
            FoodDefName = food;
            SourceLabel = source;
            Reason = reason;
        }
    }

    /// <summary>
    /// Alpha 5B background provisioning. A Gu Master's own hungry, refined Gu are fed from real supplies without any job,
    /// movement or interruption: in one tick, the service settles hunger, plans whole meals, revalidates every stack it will
    /// take, consumes exactly those items and applies the meals. It runs only from the sustenance scheduler's bounded visits
    /// (each individual about once per in-game hour), only for Gu whose hunger is advancing (so never for off-map caravans,
    /// other factions or wild Gu), and only when the cultivator's per-cultivator policy is on. Manual feeding keeps its
    /// physical job.
    /// </summary>
    public static class GuProvisioning
    {
        private static List<GuProvisionSourceDef> sources;
        private static readonly List<GuSupply> supplies = new();
        private static readonly List<(GuSupply Supply, int Take)> plan = new();
        /// <summary>Negative cache: (map, food) searched with nothing found, the tick and the map's stack count then.</summary>
        private static readonly Dictionary<(int Map, ThingDef Food), (int Tick, int Stacks)> empty = new();
        internal const int EmptyCacheTicks = 1250;

        internal static long Attempts { get; private set; }
        internal static long Feedings { get; private set; }

        internal static IReadOnlyList<GuProvisionSourceDef> Sources => sources ??= DefDatabase<GuProvisionSourceDef>.AllDefsListForReading
            .Where(source => source.Worker != null).OrderBy(source => source.order).ToList();

        internal static void ResetCaches()
        {
            empty.Clear();
            sources = null;
        }

        /// <summary>The player cultivator who provisions this Gu (its aperture host, or the recognized cultivator carrying it), or null.</summary>
        internal static Pawn ProvisionerOf(GuIndividualRegistry registry, GuRecord record)
        {
            var holder = GuSustenanceComponent.HolderOf(registry, record);
            if (holder == null || holder.Dead || holder.Faction != Faction.OfPlayer || !holder.IsColonist) return null;
            return registry.IsRecognizedByCultivator(record, holder) ? holder : null;
        }

        /// <summary>
        /// Feeds a hungry Gu from its cultivator's legitimate supplies, or explains why not. The caller has settled hunger to
        /// <paramref name="now"/>. Nothing is reserved; everything taken is revalidated and consumed within this call.
        /// </summary>
        internal static GuProvisionResult TryProvision(GuIndividualRegistry registry, GuRecord record, GuNourishment state, int now)
        {
            var props = record?.Species?.SimulatedFeeding;
            if (props == null || state == null) return Refuse("no individual feeding policy");
            if (state.Mode != GuSustenanceMode.Advancing) return Refuse("hunger is paused");
            var component = GuSustenanceComponent.Instance;
            var cultivator = ProvisionerOf(registry, record);
            if (cultivator == null || component == null) return Refuse("not in a player cultivator's own care");
            if (!component.AutoTendEnabled(cultivator)) return Refuse("automatic feeding is off");
            if (cultivator.MapHeld == null) return Refuse("its cultivator is not on a map");
            Attempts++;

            bool anyWanted = false;
            foreach (var option in props.foods)
            {
                if (option?.thingDef == null) continue;
                int wanted = GuNourishmentRules.MealsWanted(props, option, state.NourishmentTicks);
                if (wanted < 1) continue;
                anyWanted = true;
                if (!GatherPlan(cultivator, option, wanted, out int meals, out string source)) continue;
                // Commit: every stack was revalidated an instant ago in the same tick; take exactly the planned items.
                int items = 0;
                foreach (var (supply, take) in plan)
                {
                    var thing = supply.Thing;
                    var eaten = thing.stackCount > take ? thing.SplitOff(take) : thing;
                    items += eaten.stackCount;
                    eaten.Destroy(DestroyMode.Vanish);
                }
                plan.Clear();
                GuNourishmentRules.Feed(state, props, option, meals, now);
                state.automaticMeals += meals;
                state.lastFeedSource = source;
                Feedings++;
                return new GuProvisionResult(true, meals, items, option.thingDef.defName, source, null);
            }
            plan.Clear();
            if (!anyWanted) return Refuse("it is sated");
            return Refuse($"no {GuSustenanceComponent.DietSummary(props)} in {cultivator.LabelShort}'s inventory or reachable stockpiles");
        }

        /// <summary>Plans whole meals of one food across the sources in order, revalidating each stack. Fills <see cref="plan"/>.</summary>
        private static bool GatherPlan(Pawn cultivator, GuFoodOption option, int wanted, out int meals, out string source)
        {
            meals = 0;
            source = null;
            plan.Clear();
            supplies.Clear();
            var map = cultivator.MapHeld;
            var key = (map.uniqueID, option.thingDef);
            int stacks = map.listerThings.ThingsOfDef(option.thingDef).Count;
            bool skipMap = empty.TryGetValue(key, out var miss) && miss.Stacks == stacks && Find.TickManager.TicksGame - miss.Tick < EmptyCacheTicks;
            foreach (var sourceDef in Sources)
            {
                if (skipMap && sourceDef.workerClass == typeof(GuProvisionSource_ColonyStockpile)) continue;
                sourceDef.Worker.CollectSupplies(cultivator, option.thingDef, supplies);
            }
            int total = 0;
            foreach (var supply in supplies) total += supply.Available;
            meals = Math.Min(wanted, total / option.mealCount);
            if (meals < 1)
            {
                if (!skipMap) empty[key] = (Find.TickManager.TicksGame, stacks);
                supplies.Clear();
                return false;
            }
            empty.Remove(key);
            int needed = meals * option.mealCount;
            var used = new List<string>();
            foreach (var supply in supplies)
            {
                if (needed <= 0) break;
                int free = supply.Source.Worker.Revalidate(cultivator, supply.Thing);
                int take = Math.Min(free, needed);
                if (take <= 0) continue;
                plan.Add((supply, take));
                needed -= take;
                if (!used.Contains(supply.Source.Worker.Label)) used.Add(supply.Source.Worker.Label);
            }
            supplies.Clear();
            if (needed > 0)
            {
                plan.Clear();
                meals = 0;
                return false;
            }
            source = string.Join(" and ", used);
            return true;
        }

        private static GuProvisionResult Refuse(string reason) => new(false, 0, 0, null, null, reason);
    }
}
