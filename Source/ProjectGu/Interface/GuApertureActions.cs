using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using ProjectGu.Sustenance;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Interface
{
    public enum GuApertureActionKind { NourishWalls, AdvanceStage, Channel, EnforceWill, ResumeWill, AbandonWill, Store, Withdraw, Feed }

    /// <summary>
    /// One player action on an aperture or one of its Gu, described from the authoritative state at the moment it was
    /// built. The action owns no state: running it calls the existing registry, aperture or channel service, which
    /// revalidates everything against the live game before committing. The Aperture tab and the fallback "Manage aperture
    /// Gu" menu both build their controls from these descriptions, so the two always offer the same operations.
    /// </summary>
    public sealed class GuApertureAction
    {
        public GuApertureActionKind Kind { get; }
        /// <summary>Short label, identical to the long-standing menu wording.</summary>
        public string Label { get; }
        /// <summary>Why the action cannot run now, or null when it can.</summary>
        public string DisabledReason { get; }
        public string GuId { get; }
        public string Tooltip { get; internal set; }
        private readonly Func<string> run;

        internal GuApertureAction(GuApertureActionKind kind, string label, string guId, Func<string> run, string disabledReason = null)
        {
            Kind = kind;
            Label = label;
            GuId = guId;
            this.run = run;
            DisabledReason = run == null && disabledReason == null ? "unavailable" : disabledReason;
        }

        public bool Available => DisabledReason == null && run != null;

        /// <summary>Runs the action after revalidation. Returns false with the reason when the live state no longer allows it.</summary>
        public bool Execute(out string failure)
        {
            failure = DisabledReason;
            if (!Available) return false;
            try { failure = run(); }
            catch (Exception exception)
            {
                failure = "The action failed.";
                Log.Error($"[Project GU] Aperture action {Kind} failed. {exception}");
            }
            return failure == null;
        }
    }

    /// <summary>Builds aperture actions from authoritative state. Every builder is read-only.</summary>
    public static class GuApertureActions
    {
        internal const string RefinementJobDefName = "GU_RefineMortalGu";
        internal const float NearbyRefinementRadius = 8f;

        /// <summary>The pawn's single aperture, or null when it has none or an ambiguous duplicate.</summary>
        public static Hediff_Aperture FindAperture(Pawn host)
        {
            var hediffs = host?.health?.hediffSet?.hediffs;
            if (hediffs == null) return null;
            Hediff_Aperture found = null;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i] is not Hediff_Aperture aperture || aperture.def != GuWorldComponent.ApertureDef) continue;
                if (found != null) return null;
                found = aperture;
            }
            return found;
        }

        /// <summary>Only the cultivator's own player-controlled body can manage its aperture.</summary>
        public static bool CanManage(Pawn host, Hediff_Aperture aperture) =>
            host?.IsColonistPlayerControlled == true && aperture != null && aperture.pawn == host
            && aperture.def == GuWorldComponent.ApertureDef && host.health?.hediffSet?.hediffs.Contains(aperture) == true;

        // ---- Cultivation -----------------------------------------------------------------------------------------

        /// <summary>Wall nourishment, or null at Rank 1 Peak (nothing left to nourish) or above Rank 1.</summary>
        public static GuApertureAction Nourish(Pawn host, Hediff_Aperture aperture)
        {
            if (!CanManage(host, aperture) || aperture.Rank != 1 || aperture.Stage == RankOneStage.Peak) return null;
            RankOneStage stage = aperture.Stage;
            string label = $"Nourish aperture walls ({aperture.WallNourishment:0.##}/{aperture.WallNourishmentRequired:0} wall nourishment)";
            if (host.Drafted || host.Downed)
                return new GuApertureAction(GuApertureActionKind.NourishWalls, label, null, null, "requires an undrafted, conscious cultivator");
            if (aperture.CanAdvanceRankOne)
                return new GuApertureAction(GuApertureActionKind.NourishWalls, label, null, null, "the walls are fully nourished; advance the minor stage");
            if (!aperture.CanNourishWalls)
                return new GuApertureAction(GuApertureActionKind.NourishWalls, label, null, null, "waiting for essence recovery");
            return new GuApertureAction(GuApertureActionKind.NourishWalls, label, null, () =>
            {
                if (JobDriver_GuNourishAperture.FindCultivatingAperture(host) != aperture || aperture.Stage != stage
                    || !aperture.CanNourishWalls || JobDriver_GuNourishAperture.NourishJob is not { } job)
                    return "The aperture can no longer be nourished.";
                return host.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job), JobTag.Misc) ? null : "The cultivator could not begin.";
            });
        }

        /// <summary>Minor-stage advancement, or null at Rank 1 Peak or above Rank 1.</summary>
        public static GuApertureAction Advance(Pawn host, Hediff_Aperture aperture)
        {
            if (!CanManage(host, aperture) || aperture.Rank != 1 || aperture.Stage == RankOneStage.Peak) return null;
            RankOneStage stage = aperture.Stage;
            string label = $"Advance to {stage + 1}";
            if (host.Drafted || host.Downed)
                return new GuApertureAction(GuApertureActionKind.AdvanceStage, label, null, null, "requires an undrafted, conscious cultivator");
            if (!aperture.CanAdvanceRankOne)
                return new GuApertureAction(GuApertureActionKind.AdvanceStage, label, null, null,
                    $"the walls need {aperture.WallNourishmentRequired - aperture.WallNourishment:0.##} more nourishment");
            return new GuApertureAction(GuApertureActionKind.AdvanceStage, label, null, () =>
            {
                if (JobDriver_GuNourishAperture.FindCultivatingAperture(host) != aperture || !aperture.TryAdvanceRankOne(stage))
                    return "The aperture is not ready to advance.";
                Messages.Message($"{host.LabelShort} advanced to Rank 1 {aperture.Stage}. Newly recovered essence is now {aperture.Stage} quality.",
                    new LookTargets(host), MessageTypeDefOf.PositiveEvent, false);
                return null;
            });
        }

        /// <summary>Self-use channel techniques that act on the aperture itself, such as Liquor Worm refinement.</summary>
        public static IEnumerable<GuApertureAction> CultivationChannels(Pawn host, Hediff_Aperture aperture)
        {
            if (!CanManage(host, aperture)) yield break;
            foreach (var grant in GuAbilityService.GrantsFor(host))
            {
                if (!grant.Ability.IsChannel || grant.Ability.category != GuAbilityCategory.Cultivation) continue;
                yield return Channel(host, grant);
            }
        }

        internal static GuApertureAction Channel(Pawn host, GuAbilityGrant grant)
        {
            var ability = grant.Ability;
            string guId = grant.Gu.Id;
            bool available = GuChannelService.CanChannel(host, guId, ability, out _, out var resumable, out string reason);
            string label = resumable != null ? $"Resume {ability.label} ({resumable.PulsesDone}/{resumable.PulseCount})" : ability.LabelCap.ToString();
            if (!available) return new GuApertureAction(GuApertureActionKind.Channel, label, guId, null, reason ?? "unavailable");
            return new GuApertureAction(GuApertureActionKind.Channel, label, guId,
                () => GuChannelService.TryOrderChannel(host, guId, ability, out string failure) ? null : failure);
        }

        // ---- Gu custody and will-enforcement ---------------------------------------------------------------------

        /// <summary>Storing a recognized dormant Gu that the cultivator carries.</summary>
        public static GuApertureAction Store(Pawn host, Hediff_Aperture aperture, GuIndividualRegistry registry, Thing_GuDormantProjection thing)
        {
            if (!CanManage(host, aperture) || registry?.Resolve(thing) is not { } record || !registry.IsRecognizedByCultivator(record, host)) return null;
            string label = $"Store {Describe(record)}";
            int claims = registry.ApertureClaimCount(aperture);
            if (claims >= GuIndividualRegistry.MortalApertureCapacity)
                return new GuApertureAction(GuApertureActionKind.Store, label, record.Id, null,
                    $"the aperture is full ({claims}/{GuIndividualRegistry.MortalApertureCapacity})");
            if (record.RefinementProcess != null)
                return new GuApertureAction(GuApertureActionKind.Store, label, record.Id, null, "will-enforcement is in progress");
            int revision = thing.GuRevision;
            return new GuApertureAction(GuApertureActionKind.Store, label, record.Id, () =>
                registry.TryStoreDormant(thing, revision, host, aperture, out string reason) ? null : reason);
        }

        /// <summary>Withdrawing a recognized Gu from the aperture into the cultivator's inventory.</summary>
        public static GuApertureAction Withdraw(Pawn host, Hediff_Aperture aperture, GuIndividualRegistry registry, GuRecord record)
        {
            if (!CanManage(host, aperture) || record == null || registry == null || !registry.IsRecognizedByCultivator(record, host)) return null;
            string id = record.Id;
            int revision = record.CustodyRevision;
            return new GuApertureAction(GuApertureActionKind.Withdraw, $"Withdraw {Describe(record)}", id, () =>
                registry.TryWithdrawDormant(id, revision, host, aperture, out _, out string reason) ? null : reason);
        }

        /// <summary>Starting or resuming will-enforcement over a dormant Gu, or null when the cultivator cannot.</summary>
        public static GuApertureAction EnforceWill(Pawn host, GuIndividualRegistry registry, Thing_GuDormantProjection thing)
        {
            if (registry?.Resolve(thing) is not { } record || !registry.CanOrderRefinement(thing, host, out var quote)) return null;
            var process = record.RefinementProcess;
            string label = Describe(record);
            string actionLabel = process != null
                ? $"Resume will-enforcement: {label} ({100f * process.CompletedWorkTicks / process.TotalWorkTicks:0}% complete, {process.RemainingEssence:0.0} essence left)"
                : $"Enforce will over {label} ({quote.WorkTicks / 2500f:0.0} h, {quote.EssenceCost:0.0} essence)";
            return new GuApertureAction(process != null ? GuApertureActionKind.ResumeWill : GuApertureActionKind.EnforceWill, actionLabel, record.Id, () =>
            {
                if (!registry.CanOrderRefinement(thing, host) || DefDatabase<JobDef>.GetNamedSilentFail(RefinementJobDefName) is not { } jobDef)
                    return "This Gu can no longer be refined by this cultivator.";
                return host.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, thing)) ? null : "The cultivator could not begin.";
            });
        }

        /// <summary>Abandoning an active will-enforcement attempt, or null when none can be abandoned.</summary>
        public static GuApertureAction AbandonWill(Pawn host, GuIndividualRegistry registry, Thing_GuDormantProjection thing)
        {
            if (registry?.Resolve(thing) is not { } record || !registry.CanAbandonRefinement(thing, host)) return null;
            return new GuApertureAction(GuApertureActionKind.AbandonWill, $"Abandon will-enforcement: {Describe(record)} (essence spent is lost)", record.Id, () =>
                registry.TryAbandonRefinement(thing, host, out string reason) ? null : reason);
        }

        // ---- Feeding -------------------------------------------------------------------------------------------

        /// <summary>
        /// Feeding one Gu this pawn can reach, or null when the species has no simulated feeding or the pawn has no way to
        /// reach it. Building the action is cheap and read-only; running it plans the meal, and the job consumes food only
        /// at its commitment toil.
        /// </summary>
        public static GuApertureAction Feed(Pawn feeder, GuIndividualRegistry registry, GuRecord record)
        {
            if (registry == null || record?.Species?.SimulatedFeeding is not { } props || feeder?.IsColonistPlayerControlled != true) return null;
            string id = record.Id;
            int revision = record.CustodyRevision;
            var access = registry.FeedAccess(feeder, record, out _, out string reason);
            string label = $"Feed {record.Species.label}";
            if (access == GuFeedAccess.None)
                return new GuApertureAction(GuApertureActionKind.Feed, label, id, null, reason) { Tooltip = FeedTooltip(props) };
            if (feeder.Drafted || feeder.Downed)
                return new GuApertureAction(GuApertureActionKind.Feed, label, id, null, "requires an undrafted, conscious colonist") { Tooltip = FeedTooltip(props) };
            var snapshot = GuSustenance.Snapshot(registry, record);
            GuFoodOption usable = null;
            bool wantsAny = false;
            foreach (var option in props.foods)
            {
                if (GuNourishmentRules.MealsWanted(props, option, snapshot.NourishmentTicks) < 1) continue;
                wantsAny = true;
                if (props.Worker?.CountAvailable(feeder, option) >= option.mealCount)
                {
                    usable = option;
                    break;
                }
            }
            if (!wantsAny)
                return new GuApertureAction(GuApertureActionKind.Feed, label, id, null, "it is sated") { Tooltip = FeedTooltip(props) };
            if (usable == null)
                return new GuApertureAction(GuApertureActionKind.Feed, label, id, null,
                    $"no {GuSustenanceComponent.DietSummary(props)} available on this map") { Tooltip = FeedTooltip(props) };
            return new GuApertureAction(GuApertureActionKind.Feed, $"Feed {record.Species.label} ({usable.mealCount} {usable.Label} per meal)", id, () =>
            {
                if (registry.Resolve(id) is not { } live || live.CustodyRevision != revision) return "The Gu moved or changed custody.";
                return GuSustenance.TryOrderFeed(feeder, live, out string failure) ? null : failure?.CapitalizeFirst();
            }) { Tooltip = FeedTooltip(props) };
        }

        private static string FeedTooltip(GuFeedingProperties props)
        {
            var lines = new List<string>();
            foreach (var food in props.foods)
                if (food?.thingDef != null)
                    lines.Add($"{food.mealCount} {food.Label} per meal, sustaining {food.daysPerMeal:0.##} days" + (food.note.NullOrEmpty() ? "" : $" ({food.note})"));
            return "The colonist fetches the food and feeds this exact Gu. Food is used only when the meal is given; an interrupted feeding keeps it.\n\n"
                + string.Join("\n", lines);
        }

        /// <summary>Dormant Gu the cultivator carries, and those within reach on the ground, for refinement and storage.</summary>
        public static void CollectDormant(Pawn host, List<Thing_GuDormantProjection> held, List<Thing_GuDormantProjection> nearby)
        {
            held?.Clear();
            nearby?.Clear();
            if (host == null) return;
            if (held != null && host.inventory?.GetDirectlyHeldThings() is { } inventory)
                for (int i = 0; i < inventory.Count; i++)
                    if (inventory[i] is Thing_GuDormantProjection thing) held.Add(thing);
            if (nearby == null || host.Map is not { Disposed: false } map || !host.Spawned) return;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(host.Position, NearbyRefinementRadius, true))
            {
                if (!cell.InBounds(map)) continue;
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] is Thing_GuDormantProjection thing && !nearby.Contains(thing)) nearby.Add(thing);
            }
        }

        public static string Describe(GuRecord record) => $"{record.Species?.label ?? record.SpeciesDefName} [{record.Id}]";

        /// <summary>A float-menu option with the long-standing "label (reason)" wording for unavailable actions.</summary>
        internal static FloatMenuOption ToOption(GuApertureAction action, Func<string> successMessage = null)
        {
            if (!action.Available) return new FloatMenuOption($"{action.Label} ({action.DisabledReason})", null);
            return new FloatMenuOption(action.Label, () => Run(action, successMessage?.Invoke()));
        }

        /// <summary>Runs an action and reports the outcome the way the menus always have.</summary>
        internal static bool Run(GuApertureAction action, string successMessage = null)
        {
            bool done = action.Execute(out string failure);
            if (!done && !failure.NullOrEmpty()) Messages.Message(failure, MessageTypeDefOf.RejectInput, false);
            else if (done && !successMessage.NullOrEmpty()) Messages.Message(successMessage, MessageTypeDefOf.NeutralEvent, false);
            return done;
        }
    }
}
