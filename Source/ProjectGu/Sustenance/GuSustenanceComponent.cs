using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu.Sustenance
{
    /// <summary>A planned feeding job's target individual and expectations, kept until the job's driver takes them.</summary>
    public sealed class GuFeedIntent : IExposable
    {
        public string GuId;
        public int CustodyRevision;
        public int FeedRevision;
        public string FoodDefName;
        public int Meals;
        public bool Automatic;
        public int CreatedTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref GuId, "guId");
            Scribe_Values.Look(ref CustodyRevision, "custodyRevision");
            Scribe_Values.Look(ref FeedRevision, "feedRevision");
            Scribe_Values.Look(ref FoodDefName, "foodDefName");
            Scribe_Values.Look(ref Meals, "meals");
            Scribe_Values.Look(ref Automatic, "automatic");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
        }
    }

    /// <summary>
    /// Game-level sustenance scheduler. Every <see cref="SweepInterval"/> ticks it settles a bounded slice of the canonical
    /// records, so each individual is visited about once per <see cref="SweepPeriod"/> ticks however many exist, with a hard
    /// per-call budget. Settling is a single subtraction from absolute ticks: there are no catch-up loops, and reloading
    /// never repeats a deduction. Opening the interface is never required for hunger to advance.
    /// </summary>
    public sealed class GuSustenanceComponent : GameComponent
    {
        internal const int SweepInterval = 250;
        internal const int SweepPeriod = 2500;
        internal const int SweepBudget = 400;
        internal const int IntentLifetime = GenDate.TicksPerDay;

        /// <summary>Tick at which feeding became active in this game. Records created earlier begin with the legacy grace period.</summary>
        private int activatedTick = -1;
        private bool activatedOnLegacySave;
        private List<string> autoTendDisabled = new();
        private Dictionary<int, GuFeedIntent> intents = new();
        private int cursor;
        private int legacyInitialized;

        /// <summary>The last automatic provisioning attempt per individual, for the interface. Transient: rebuilt within an hour.</summary>
        internal readonly Dictionary<string, GuProvisionResult> LastProvision = new();

        // Measurements for the performance check. Never saved.
        internal long SweepCalls { get; private set; }
        internal long RecordsVisited { get; private set; }
        internal long SweepTicks { get; private set; }
        internal long MaxSweepTicks { get; private set; }

        public GuSustenanceComponent(Game game) { }

        internal static GuSustenanceComponent Instance => Current.Game?.GetComponent<GuSustenanceComponent>();
        internal int ActivatedTick => activatedTick;
        internal bool ActivatedOnLegacySave => activatedOnLegacySave;
        internal IReadOnlyDictionary<int, GuFeedIntent> Intents => intents;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activatedTick, "activatedTick", -1);
            Scribe_Values.Look(ref activatedOnLegacySave, "activatedOnLegacySave");
            Scribe_Collections.Look(ref autoTendDisabled, "autoTendDisabled", LookMode.Value);
            Scribe_Collections.Look(ref intents, "feedIntents", LookMode.Value, LookMode.Deep);
            autoTendDisabled ??= new();
            intents ??= new();
        }

        public override void StartedNewGame()
        {
            if (activatedTick < 0) activatedTick = Find.TickManager.TicksGame;
        }

        public override void LoadedGame()
        {
            if (activatedTick >= 0) return;
            // A save written before Alpha 5A: existing Gu start full, with a grace period, and no food is created.
            activatedTick = Find.TickManager.TicksGame;
            activatedOnLegacySave = true;
            Log.Message($"[Project GU] Gu feeding activated on an older save at tick {activatedTick}. Existing Gu begin fully nourished with a grace period; no items were created.");
        }

        /// <summary>Records created before feeding activated begin with the older-save grace period.</summary>
        internal bool WouldBeLegacy(GuRecord record) =>
            activatedOnLegacySave && record != null && record.CreatedTick < activatedTick;

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % SweepInterval != 0) return;
            Sweep(now);
            if (now % GenDate.TicksPerDay == 0) PruneIntents(now);
        }

        /// <summary>Settles one bounded slice of records. Each living simulated individual is visited about once per period.</summary>
        internal void Sweep(int now, int? budgetOverride = null)
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (registry == null || registry.CustodyTransitionInProgress || activatedTick < 0) return;
            var records = registry.Records;
            int count = records.Count;
            if (count == 0) return;
            long started = Stopwatch.GetTimestamp();
            int slice = budgetOverride ?? Math.Min(SweepBudget, Math.Max(1, (count * SweepInterval + SweepPeriod - 1) / SweepPeriod));
            if (cursor >= count) cursor = 0;
            int visited = 0;
            for (int step = 0; step < slice && step < count; step++)
            {
                var record = records[cursor];
                cursor = cursor + 1 >= count ? 0 : cursor + 1;
                if (record == null || !record.IsLiving || record.Species?.SimulatedFeeding == null) continue;
                visited++;
                Visit(registry, record, now);
            }
            long elapsed = Stopwatch.GetTimestamp() - started;
            SweepCalls++;
            RecordsVisited += visited;
            SweepTicks += elapsed;
            if (elapsed > MaxSweepTicks) MaxSweepTicks = elapsed;
        }

        internal void ResetMeasurements()
        {
            SweepCalls = RecordsVisited = SweepTicks = MaxSweepTicks = 0;
        }

        /// <summary>Settles one individual, reports a worsening level to the player, and ends it after terminal starvation.</summary>
        internal void Visit(GuIndividualRegistry registry, GuRecord record, int now)
        {
            var props = record.Species.SimulatedFeeding;
            var existing = record.GetState<GuNourishment>();
            // Wild Gu outside care carry no state: their habitat sustains them, and thousands of them cost nothing to save.
            if (existing == null && registry.SustenanceModeOf(record) == GuSustenanceMode.Wild) return;
            var state = GuSustenance.Settle(registry, record, now);
            if (state == null) return;
            if (existing == null && state.FromLegacySave) legacyInitialized++;
            // Alpha 5B background provisioning: a hungry Gu in its cultivator's own care is fed from real supplies, with no job.
            if (state.Mode == GuSustenanceMode.Advancing
                && GuNourishmentRules.LevelOf(props, state.NourishmentTicks, state.StarvingTicks) >= GuNourishmentLevel.Hungry)
                LastProvision[record.Id] = GuProvisioning.TryProvision(registry, record, state, now);
            var level = GuNourishmentRules.LevelOf(props, state.NourishmentTicks, state.StarvingTicks);
            if (state.Mode == GuSustenanceMode.Advancing && GuNourishmentRules.IsTerminal(props, state.NourishmentTicks, state.StarvingTicks))
            {
                Starve(registry, record, state);
                return;
            }
            if (level < state.notifiedLevel) state.notifiedLevel = level;
            else if (level > state.notifiedLevel)
            {
                state.notifiedLevel = level;
                if (state.Mode == GuSustenanceMode.Advancing) Notify(registry, record, level);
            }
        }

        private static void Starve(GuIndividualRegistry registry, GuRecord record, GuNourishment state)
        {
            var holder = HolderOf(registry, record);
            string name = record.Species?.LabelCap ?? record.SpeciesDefName;
            string days = (state.StarvingTicks / (float)GenDate.TicksPerDay).ToString("0.#");
            string detail = $"Starved after {days} days without food.";
            if (!registry.TryEndLifeByStarvation(record, detail, out string reason))
            {
                Log.Warning($"[Project GU] Gu {record.Id} reached terminal starvation but could not be ended now: {reason}");
                return;
            }
            Find.LetterStack.ReceiveLetter($"{name} starved", $"{name} [{record.Id}] died of starvation after {days} days without food."
                + (holder != null ? $" It was in the care of {holder.LabelShort}." : "")
                + "\n\nIts identity and history remain in the Gu records. Feeding a starving Gu at any time before death ends the starvation.",
                LetterDefOf.NegativeEvent, holder != null && holder.Spawned ? new LookTargets(holder) : LookTargets.Invalid);
        }

        private static void Notify(GuIndividualRegistry registry, GuRecord record, GuNourishmentLevel level)
        {
            string name = $"{record.Species?.LabelCap ?? record.SpeciesDefName} [{ShortId(record.Id)}]";
            string food = DietSummary(record.Species?.SimulatedFeeding);
            string auto = Instance?.LastProvision.TryGetValue(record.Id, out var last) == true && !last.Fed && last.Reason != null
                && last.Reason.StartsWith("no ", StringComparison.Ordinal) ? $" Automatic feeding found {last.Reason}." : "";
            string text = level switch
            {
                GuNourishmentLevel.Hungry => $"{name} is hungry. Feed it {food}.{auto}",
                GuNourishmentLevel.Starving => $"{name} is starving. Feed it {food} soon.{auto}",
                GuNourishmentLevel.Withering => $"{name} is withering from starvation and can no longer act. Feed it {food} before it dies.{auto}",
                _ => null
            };
            if (text == null) return;
            var holder = HolderOf(registry, record);
            var target = holder != null && holder.Spawned ? new LookTargets(holder)
                : registry.ProjectionOf(record) is { Spawned: true } thing ? new LookTargets(thing) : LookTargets.Invalid;
            Messages.Message(text, target, level >= GuNourishmentLevel.Starving ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.CautionInput, false);
        }

        internal static Pawn HolderOf(GuIndividualRegistry registry, GuRecord record)
        {
            if (registry.ApertureHostOf(record) is { } host) return host;
            return registry.ProjectionOf(record)?.ParentHolder switch
            {
                Pawn_InventoryTracker inventory => inventory.pawn,
                Pawn_CarryTracker carry => carry.pawn,
                _ => null
            };
        }

        internal static string DietSummary(GuFeedingProperties props)
        {
            if (props?.foods == null || props.foods.Count == 0) return "its food";
            return string.Join(" or ", props.foods.Where(food => food?.thingDef != null).Select(food => food.thingDef.label));
        }

        private static string ShortId(string id) => id == null ? "?" : id.Length > 8 ? id.Substring(0, 8) : id;

        // ---- Feeding intents ----------------------------------------------------------------------------------

        internal void RegisterIntent(int jobId, GuFeedIntent intent) => intents[jobId] = intent;

        internal GuFeedIntent PeekIntent(int jobId) => intents.TryGetValue(jobId, out var intent) ? intent : null;

        internal void RemoveIntent(int jobId) => intents.Remove(jobId);

        /// <summary>Intents whose job never started (a cancelled queue entry) expire after a day. They hold nothing.</summary>
        private void PruneIntents(int now)
        {
            if (intents.Count == 0) return;
            foreach (int key in intents.Where(entry => now - entry.Value.CreatedTick > IntentLifetime).Select(entry => entry.Key).ToArray())
                intents.Remove(key);
        }

        // ---- Routine feeding policy ---------------------------------------------------------------------------

        /// <summary>Whether this cultivator tends their own refined Gu automatically. On by default; the player controls it.</summary>
        internal bool AutoTendEnabled(Pawn pawn)
        {
            string id = Current.Game?.GetComponent<GuWorldComponent>()?.FindCultivator(pawn)?.CultivatorId;
            return id != null && !autoTendDisabled.Contains(id);
        }

        internal void SetAutoTend(Pawn pawn, bool enabled)
        {
            string id = Current.Game?.GetComponent<GuWorldComponent>()?.FindCultivator(pawn)?.CultivatorId;
            if (id == null) return;
            if (enabled) autoTendDisabled.Remove(id);
            else if (!autoTendDisabled.Contains(id)) autoTendDisabled.Add(id);
        }
    }
}
