using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>Whether a species' individuals simulate hunger. Missing XML means NotImplemented, never an invented diet.</summary>
    public enum GuFeedingStatus
    {
        /// <summary>No individual feeding policy exists for this species yet. Nothing is simulated and nothing is invented.</summary>
        NotImplemented = 0,
        /// <summary>Deliberately excluded from hunger, with the reason declared in XML (developer fixtures).</summary>
        Exempt = 1,
        /// <summary>Individuals keep a persistent nourishment state and must be fed.</summary>
        Simulated = 2
    }

    /// <summary>How fed an individual is. Sated, Hungry, Starving and Withering apply only to simulated species.</summary>
    public enum GuNourishmentLevel { Unconfigured = 0, Exempt = 1, Sated = 2, Hungry = 3, Starving = 4, Withering = 5 }

    /// <summary>Whether hunger currently advances for an individual, and why not.</summary>
    public enum GuSustenanceMode
    {
        Advancing = 0,
        /// <summary>Wild and active: sustained by its habitat, outside the player's care.</summary>
        Wild = 1,
        /// <summary>Not on a loaded map: a caravan, a world pawn or an abstract world location.</summary>
        OffMap = 2,
        /// <summary>Held by a pawn of another faction; no non-player Gu economy exists yet.</summary>
        OtherFaction = 3,
        /// <summary>In the aperture of a dead host.</summary>
        DeceasedHost = 4,
        /// <summary>Unresolved, quarantined or otherwise not actionable.</summary>
        Unavailable = 5
    }

    /// <summary>One accepted food: a physical item, how many make one meal, and how long one meal sustains the Gu.</summary>
    public sealed class GuFoodOption
    {
        public ThingDef thingDef;
        public int mealCount = 1;
        public float daysPerMeal = 1f;
        public bool stripCorpseFlesh;
        public List<ThingDef> exclusiveMeatSources;
        private bool? verifiedMeat;
        /// <summary>Optional note shown to the player, for example that beer is a poor substitute for wine.</summary>
        [MustTranslate] public string note;

        public int TicksPerMeal => (int)Math.Round(daysPerMeal * GuNourishmentRules.TicksPerDay);
        public string Label => thingDef?.label ?? "?";

        internal bool Accepts(Thing food) => food is { Destroyed: false } && food.def == thingDef
            && (!stripCorpseFlesh || GuCorpseFeeding.HasFlesh(food)) && MatchesMeatSources;

        private bool MatchesMeatSources => exclusiveMeatSources.NullOrEmpty() || (verifiedMeat ??=
            exclusiveMeatSources.Any(source => source?.race?.meatDef == thingDef)
            && !DefDatabase<ThingDef>.AllDefsListForReading.Any(def => def.category == ThingCategory.Pawn
                && def.race?.meatDef == thingDef && !exclusiveMeatSources.Contains(def)));

        internal void Consume(Thing food, int count)
        {
            if (stripCorpseFlesh) GuCorpseFeeding.Consume((Corpse)food);
            else (food.stackCount > count ? food.SplitOff(count) : food).Destroy(DestroyMode.Vanish);
        }
    }

    /// <summary>
    /// Species feeding specification. Values are game-balance choices unless a comment says they are lore. The worker is
    /// the extension boundary: the default plans meals from physical items; a future species that eats emotions, souls or
    /// qi-rich soil supplies its own worker without changing the scheduler.
    /// </summary>
    public sealed class GuFeedingProperties
    {
        public GuFeedingStatus status = GuFeedingStatus.NotImplemented;
        /// <summary>Explains an unimplemented or exempt policy, or describes the diet.</summary>
        [MustTranslate] public string note;
        /// <summary>Accepted foods in order of preference.</summary>
        public List<GuFoodOption> foods = new();
        /// <summary>Most nourishment an individual can store.</summary>
        public float maxDays = 2f;
        public bool requireFullMealSpace;
        /// <summary>Hungry when stored nourishment falls to this or below.</summary>
        public float hungryBelowDays = 0.5f;
        /// <summary>Nourishment of an individual first taken into care (fresh from the wild or newly created).</summary>
        public float initialDays = 1f;
        /// <summary>Hunger does not advance for this long after the system first activates on an older save.</summary>
        public float legacyGraceDays = 3f;
        /// <summary>After this long starving, the Gu is withering and cannot act for its master.</summary>
        public float impairAfterStarvingDays = 2f;
        /// <summary>After this long starving, the Gu dies. Feeding at any point before then ends the starvation.</summary>
        public float terminalAfterStarvingDays = 10f;
        public Type workerClass = typeof(GuFeedingWorker_Items);

        [Unsaved] private GuFeedingWorker worker;

        public bool Simulated => status == GuFeedingStatus.Simulated;
        public int MaxTicks => Days(maxDays);
        public int HungryTicks => Days(hungryBelowDays);
        public int InitialTicks => Days(initialDays);
        public int LegacyGraceTicks => Days(legacyGraceDays);
        public int ImpairTicks => Days(impairAfterStarvingDays);
        public int TerminalTicks => Days(terminalAfterStarvingDays);

        private static int Days(float days) => (int)Math.Round(Math.Max(0f, days) * GuNourishmentRules.TicksPerDay);

        public GuFeedingWorker Worker
        {
            get
            {
                if (worker == null && Simulated && workerClass != null && typeof(GuFeedingWorker).IsAssignableFrom(workerClass))
                {
                    worker = (GuFeedingWorker)Activator.CreateInstance(workerClass);
                    worker.props = this;
                }
                return worker;
            }
        }

        public IEnumerable<string> ConfigErrors(string owner)
        {
            if (status != GuFeedingStatus.Simulated)
            {
                if (foods is { Count: > 0 }) yield return $"{owner}: feeding lists foods but its status is {status}";
                if (status == GuFeedingStatus.Exempt && note.NullOrEmpty()) yield return $"{owner}: an exempt feeding policy must declare why in note";
                yield break;
            }
            if (foods == null || foods.Count == 0) yield return $"{owner}: simulated feeding needs at least one food";
            else
                foreach (var food in foods)
                {
                    if (food?.thingDef == null || food.thingDef.category != ThingCategory.Item)
                        yield return $"{owner}: every food must be an item ThingDef";
                    else if (food.mealCount < 1 || food.mealCount > 10000)
                        yield return $"{owner}: {food.thingDef.defName} mealCount must be between 1 and 10000";
                    if (food?.stripCorpseFlesh == true && (food.mealCount != 1 || food.thingDef?.thingClass != typeof(Corpse)))
                        yield return $"{owner}: corpse meals require one corpse";
                    if (food != null && (!float.IsFinite(food.daysPerMeal) || food.daysPerMeal <= 0f || food.daysPerMeal > maxDays))
                        yield return $"{owner}: daysPerMeal must be positive and no more than maxDays";
                }
            if (!float.IsFinite(maxDays) || maxDays <= 0f || maxDays > 60f) yield return $"{owner}: maxDays must be between 0 and 60";
            if (!(hungryBelowDays > 0f && hungryBelowDays < maxDays)) yield return $"{owner}: hungryBelowDays must be between 0 and maxDays";
            if (!(initialDays > 0f && initialDays <= maxDays)) yield return $"{owner}: initialDays must be between 0 and maxDays";
            if (!(legacyGraceDays >= 0f && legacyGraceDays <= 30f)) yield return $"{owner}: legacyGraceDays must be between 0 and 30";
            if (!(impairAfterStarvingDays >= 0f && impairAfterStarvingDays < terminalAfterStarvingDays))
                yield return $"{owner}: impairAfterStarvingDays must be below terminalAfterStarvingDays";
            // No death because a single meal was missed (Alpha 5A requirement).
            if (!(terminalAfterStarvingDays >= 3f && terminalAfterStarvingDays <= 120f))
                yield return $"{owner}: terminalAfterStarvingDays must be between 3 and 120";
            if (workerClass == null || !typeof(GuFeedingWorker).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
                yield return $"{owner}: feeding workerClass must be a concrete ProjectGu.Domain.GuFeedingWorker";
        }
    }

    /// <summary>
    /// Persistent nourishment of one individual, saved on its canonical record so it follows the Gu through every
    /// manifestation, container and holder. Values are absolute game ticks, so saving and loading never repeat a deduction.
    /// </summary>
    public sealed class GuNourishment : GuStateComponent
    {
        internal int nourishmentTicks;
        internal int starvingTicks;
        /// <summary>Hunger has been accounted up to this tick.</summary>
        internal int settledTick = -1;
        /// <summary>Hunger does not advance before this tick (older-save grace period), or -1.</summary>
        internal int graceUntilTick = -1;
        internal GuSustenanceMode mode = GuSustenanceMode.Advancing;
        internal int lastFedTick = -1;
        internal string lastFoodDefName;
        internal int mealsEaten;
        internal int itemsEaten;
        /// <summary>Increases on every meal; a feeding job revalidates it before committing.</summary>
        internal int feedRevision;
        internal GuNourishmentLevel notifiedLevel = GuNourishmentLevel.Sated;
        internal bool fromLegacySave;
        /// <summary>Alpha 5B: meals given by background provisioning, and where the last automatic meal came from.</summary>
        internal int automaticMeals;
        internal string lastFeedSource;

        public int NourishmentTicks => nourishmentTicks;
        public int StarvingTicks => starvingTicks;
        public int SettledTick => settledTick;
        public int GraceUntilTick => graceUntilTick;
        public GuSustenanceMode Mode => mode;
        public int LastFedTick => lastFedTick;
        public string LastFoodDefName => lastFoodDefName;
        public int MealsEaten => mealsEaten;
        public int ItemsEaten => itemsEaten;
        public int FeedRevision => feedRevision;
        public bool FromLegacySave => fromLegacySave;
        public int AutomaticMeals => automaticMeals;
        public string LastFeedSource => lastFeedSource;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nourishmentTicks, "nourishmentTicks");
            Scribe_Values.Look(ref starvingTicks, "starvingTicks");
            Scribe_Values.Look(ref settledTick, "settledTick", -1);
            Scribe_Values.Look(ref graceUntilTick, "graceUntilTick", -1);
            Scribe_Values.Look(ref mode, "mode", GuSustenanceMode.Advancing);
            Scribe_Values.Look(ref lastFedTick, "lastFedTick", -1);
            Scribe_Values.Look(ref lastFoodDefName, "lastFoodDefName");
            Scribe_Values.Look(ref mealsEaten, "mealsEaten");
            Scribe_Values.Look(ref itemsEaten, "itemsEaten");
            Scribe_Values.Look(ref feedRevision, "feedRevision");
            Scribe_Values.Look(ref notifiedLevel, "notifiedLevel", GuNourishmentLevel.Sated);
            Scribe_Values.Look(ref fromLegacySave, "fromLegacySave");
            Scribe_Values.Look(ref automaticMeals, "automaticMeals");
            Scribe_Values.Look(ref lastFeedSource, "lastFeedSource");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nourishmentTicks = Math.Max(0, nourishmentTicks);
                starvingTicks = Math.Max(0, starvingTicks);
            }
        }
    }

    /// <summary>A read-only projection of an individual's nourishment at one tick.</summary>
    public readonly struct GuNourishmentSnapshot
    {
        public readonly GuFeedingStatus Status;
        public readonly GuNourishmentLevel Level;
        public readonly GuSustenanceMode Mode;
        public readonly int NourishmentTicks;
        public readonly int MaxTicks;
        public readonly int StarvingTicks;
        public readonly int ImpairTicks;
        public readonly int TerminalTicks;
        public readonly int HungryTicks;
        public readonly bool HasState;
        public readonly bool InGrace;

        public GuNourishmentSnapshot(GuFeedingStatus status, GuNourishmentLevel level, GuSustenanceMode mode, int nourishment, int max,
            int starving, int impair, int terminal, int hungry, bool hasState, bool inGrace)
        {
            Status = status;
            Level = level;
            Mode = mode;
            NourishmentTicks = nourishment;
            MaxTicks = max;
            StarvingTicks = starving;
            ImpairTicks = impair;
            TerminalTicks = terminal;
            HungryTicks = hungry;
            HasState = hasState;
            InGrace = inGrace;
        }

        public bool Simulated => Status == GuFeedingStatus.Simulated;
        public float Fraction => MaxTicks <= 0 ? 0f : Math.Clamp((float)NourishmentTicks / MaxTicks, 0f, 1f);
        public int TicksUntilHungry => Math.Max(0, NourishmentTicks - HungryTicks);
        public int TicksUntilStarving => NourishmentTicks;
        public int TicksUntilImpaired => Math.Max(0, ImpairTicks - StarvingTicks) + NourishmentTicks;
        public int TicksUntilDeath => Math.Max(0, TerminalTicks - StarvingTicks) + NourishmentTicks;
        public bool Impaired => Level == GuNourishmentLevel.Withering;
    }

    /// <summary>Pure nourishment arithmetic. No loops over time: any elapsed interval settles in one step.</summary>
    public static class GuNourishmentRules
    {
        public const int TicksPerDay = 60000;

        public static GuNourishment Create(GuFeedingProperties props, int now, bool legacy)
        {
            var state = new GuNourishment
            {
                nourishmentTicks = legacy ? props.MaxTicks : Math.Min(props.InitialTicks, props.MaxTicks),
                settledTick = now,
                fromLegacySave = legacy,
                graceUntilTick = legacy && props.LegacyGraceTicks > 0 ? now + props.LegacyGraceTicks : -1
            };
            state.notifiedLevel = LevelOf(props, state.nourishmentTicks, 0);
            return state;
        }

        /// <summary>Hunger ticks that count between the settled tick and now, excluding any grace period.</summary>
        public static long EffectiveElapsed(GuNourishment state, int now)
        {
            if (state.settledTick < 0 || now <= state.settledTick) return 0;
            long start = state.settledTick;
            if (state.graceUntilTick > start) start = Math.Min((long)state.graceUntilTick, now);
            return Math.Max(0L, now - start);
        }

        /// <summary>The nourishment and starvation the state would have at <paramref name="now"/>, without changing it.</summary>
        public static (int Nourishment, int Starving) Project(GuNourishment state, int now, bool advancing)
        {
            if (!advancing) return (state.nourishmentTicks, state.starvingTicks);
            long remaining = (long)state.nourishmentTicks - EffectiveElapsed(state, now);
            long starving = state.starvingTicks;
            if (remaining < 0)
            {
                starving -= remaining;
                remaining = 0;
            }
            return ((int)remaining, (int)Math.Min(starving, int.MaxValue / 2));
        }

        /// <summary>Accounts hunger up to now in one step. Attributes the whole interval to the mode observed now.</summary>
        public static void Settle(GuNourishment state, int now, GuSustenanceMode mode)
        {
            if (state.settledTick < 0) state.settledTick = now;
            if (now > state.settledTick)
            {
                (state.nourishmentTicks, state.starvingTicks) = Project(state, now, state.mode == GuSustenanceMode.Advancing && mode == GuSustenanceMode.Advancing);
                state.settledTick = now;
            }
            state.mode = mode;
            if (state.graceUntilTick >= 0 && now >= state.graceUntilTick) state.graceUntilTick = -1;
        }

        public static GuNourishmentLevel LevelOf(GuFeedingProperties props, int nourishment, int starving)
        {
            if (props == null || props.status == GuFeedingStatus.NotImplemented) return GuNourishmentLevel.Unconfigured;
            if (props.status == GuFeedingStatus.Exempt) return GuNourishmentLevel.Exempt;
            if (nourishment <= 0) return starving >= props.ImpairTicks ? GuNourishmentLevel.Withering : GuNourishmentLevel.Starving;
            return nourishment <= props.HungryTicks ? GuNourishmentLevel.Hungry : GuNourishmentLevel.Sated;
        }

        public static bool IsTerminal(GuFeedingProperties props, int nourishment, int starving) =>
            props?.Simulated == true && nourishment <= 0 && starving >= props.TerminalTicks;

        /// <summary>
        /// Whole meals the Gu will eat now. A meal is accepted while at least half of it fits; the excess is lost, so the
        /// player is never refused a meal for a Gu that is almost but not quite empty enough.
        /// </summary>
        public static int MealsWanted(GuFeedingProperties props, GuFoodOption option, int nourishment)
        {
            int perMeal = option?.TicksPerMeal ?? 0;
            if (props == null || perMeal <= 0) return 0;
            long room = (long)props.MaxTicks - Math.Max(0, nourishment);
            return (int)Math.Max(0L, (room + (props.requireFullMealSpace ? 0 : perMeal / 2)) / perMeal);
        }

        /// <summary>Applies meals already physically consumed. Feeding ends any starvation.</summary>
        public static void Feed(GuNourishment state, GuFeedingProperties props, GuFoodOption option, int meals, int now)
        {
            if (meals <= 0) return;
            long total = (long)state.nourishmentTicks + (long)meals * option.TicksPerMeal;
            state.nourishmentTicks = (int)Math.Min(total, props.MaxTicks);
            state.starvingTicks = 0;
            state.lastFedTick = now;
            state.lastFoodDefName = option.thingDef?.defName;
            state.mealsEaten += meals;
            state.itemsEaten += meals * option.mealCount;
            state.feedRevision++;
            state.notifiedLevel = LevelOf(props, state.nourishmentTicks, 0);
        }
    }

    /// <summary>One planned physical meal: which item, which food option, how many meals and items.</summary>
    public readonly struct GuMealPlan
    {
        public readonly Thing Food;
        public readonly GuFoodOption Option;
        public readonly int Meals;
        internal readonly List<(Thing Food, int Count)> Portions;
        public int Count => Meals * (Option?.mealCount ?? 0);

        public GuMealPlan(Thing food, GuFoodOption option, int meals)
        {
            Food = food;
            Option = option;
            Meals = meals;
            Portions = null;
        }

        internal GuMealPlan(GuFoodOption option, List<(Thing Food, int Count)> portions)
        {
            Food = portions[0].Food;
            Option = option;
            Meals = 1;
            Portions = portions;
        }
    }

    /// <summary>
    /// Extension boundary for how a species is fed. The scheduler only asks the worker to plan a meal and describe the
    /// diet; it never inspects species names.
    /// </summary>
    public abstract class GuFeedingWorker
    {
        public GuFeedingProperties props;

        /// <summary>Finds food this feeder can physically bring to the Gu now. Never creates or consumes anything.</summary>
        public abstract bool TryPlanMeal(Pawn feeder, int nourishment, out GuMealPlan plan, out string reason);

        /// <summary>Items of this option the feeder could use: its own inventory and unforbidden stacks on its map.</summary>
        public abstract int CountAvailable(Pawn feeder, GuFoodOption option);

        /// <summary>The option matching an item, if this worker accepts it.</summary>
        public virtual GuFoodOption OptionFor(ThingDef def)
        {
            if (def == null || props?.foods == null) return null;
            foreach (var food in props.foods)
                if (food?.thingDef == def) return food;
            return null;
        }
    }
}
