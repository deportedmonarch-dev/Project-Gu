using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu
{
    /// <summary>Player-facing mortal-aperture inspection and custody actions for the selected cultivator.</summary>
    [StaticConstructorOnStartup]
    internal static class GuApertureGizmos
    {
        internal static bool Installed { get; }

        static GuApertureGizmos()
        {
            try
            {
                var method = AccessTools.Method(typeof(Pawn), nameof(Pawn.GetGizmos))
                    ?? throw new MissingMethodException(typeof(Pawn).FullName, nameof(Pawn.GetGizmos));
                new Harmony("projectgu.livingcultivation.aperturegizmos").Patch(method,
                    postfix: new HarmonyMethod(typeof(GuApertureGizmos), nameof(AfterPawnGizmos)));
                Installed = true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Mortal aperture management controls could not be installed. {exception}");
            }
        }

        private static void AfterPawnGizmos(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Installed || __instance?.IsColonistPlayerControlled != true
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry
                || FindAperture(__instance) is not { } aperture) return;

            var command = new Command_Action
            {
                defaultLabel = "Manage aperture Gu",
                defaultDesc = "Refine dormant Gu, resume will-enforcement, and inspect, store or withdraw this cultivator's bonded Gu.",
                action = () => OpenManagementMenu(__instance, aperture, registry)
            };
            var gizmos = new List<Gizmo> { command };
            var grants = Combat.GuAbilityService.GrantsFor(__instance).ToList();
            foreach (var grant in grants)
                gizmos.Add(new Combat.Command_GuAbility(grant, grants.Count(other => other.Ability == grant.Ability) > 1));
            __result = (__result ?? Enumerable.Empty<Gizmo>()).Concat(gizmos);
        }

        internal static List<FloatMenuOption> BuildOptions(Pawn host)
        {
            var options = new List<FloatMenuOption>();
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var aperture = FindAperture(host);
            if (registry == null || aperture == null) return options;

            GuRecord[] occupants = registry.ApertureOccupants(aperture).OrderBy(record => record.Id, StringComparer.Ordinal).ToArray();
            int claims = registry.ApertureClaimCount(aperture);
            options.Add(new FloatMenuOption($"Inspect aperture Gu ({claims}/{GuIndividualRegistry.MortalApertureCapacity})",
                () => Inspect(registry, aperture, occupants)));

            if (claims < GuIndividualRegistry.MortalApertureCapacity
                && host.inventory?.GetDirectlyHeldThings() is { } inventory)
            {
                foreach (var thing in inventory.OfType<Thing_GuDormantProjection>())
                {
                    if (registry.Resolve(thing) is not { } record || !registry.IsRecognizedByCultivator(record, host)) continue;
                    options.Add(new FloatMenuOption($"Store {Describe(record)}", () =>
                    {
                        bool stored = registry.TryStoreDormant(thing, thing.GuRevision, host, aperture, out string reason);
                        Messages.Message(stored ? $"Stored {Describe(record)} in the aperture." : reason,
                            stored ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
                    }));
                }
            }

            foreach (var record in occupants.Where(record => registry.IsRecognizedByCultivator(record, host)))
            {
                options.Add(new FloatMenuOption($"Withdraw {Describe(record)}", () =>
                {
                    bool withdrawn = registry.TryWithdrawDormant(record.Id, record.CustodyRevision, host, aperture,
                        out _, out string reason);
                    Messages.Message(withdrawn ? $"Withdrew {Describe(record)} to inventory." : reason,
                        withdrawn ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
                }));
            }

            AddRefinementOptions(host, registry, options);
            return options;
        }

        private static void AddRefinementOptions(Pawn host, GuIndividualRegistry registry, List<FloatMenuOption> options)
        {
            var candidates = new HashSet<Thing_GuDormantProjection>();
            if (host.inventory?.GetDirectlyHeldThings() is { } inventory)
                foreach (var thing in inventory.OfType<Thing_GuDormantProjection>()) candidates.Add(thing);
            if (host.Map != null && !host.Map.Disposed)
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(host.Position, 8f, true))
                {
                    if (!cell.InBounds(host.Map)) continue;
                    foreach (var thing in cell.GetThingList(host.Map).OfType<Thing_GuDormantProjection>()) candidates.Add(thing);
                }

            foreach (var thing in candidates)
            {
                if (registry.Resolve(thing) is not { } record) continue;
                string label = Describe(record);
                if (registry.CanOrderRefinement(thing, host, out var quote))
                {
                    var process = record.RefinementProcess;
                    string actionLabel = process != null
                        ? $"Resume will-enforcement: {label} ({100f * process.CompletedWorkTicks / process.TotalWorkTicks:0}% complete, {process.RemainingEssence:0.0} essence left)"
                        : $"Enforce will over {label} ({quote.WorkTicks / 2500f:0.0} h, {quote.EssenceCost:0.0} essence)";
                    options.Add(new FloatMenuOption(actionLabel, () =>
                    {
                        if (!registry.CanOrderRefinement(thing, host)
                            || DefDatabase<JobDef>.GetNamedSilentFail("GU_RefineMortalGu") is not { } jobDef)
                        {
                            Messages.Message("This Gu can no longer be refined by this cultivator.", MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        host.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, thing));
                    }));
                }
                if (registry.CanAbandonRefinement(thing, host))
                    options.Add(new FloatMenuOption($"Abandon will-enforcement: {label} (essence spent is lost)", () =>
                    {
                        bool abandoned = registry.TryAbandonRefinement(thing, host, out string reason);
                        Messages.Message(reason, abandoned ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
                    }));
            }
        }

        private static void OpenManagementMenu(Pawn host, Hediff_Aperture aperture, GuIndividualRegistry registry)
        {
            if (!IsCurrentAperture(host, aperture) || Current.Game?.GetComponent<GuIndividualRegistry>() != registry)
            {
                Messages.Message("This cultivator's aperture is no longer available.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(BuildOptions(host)));
        }

        private static void Inspect(GuIndividualRegistry registry, Hediff_Aperture aperture, IReadOnlyList<GuRecord> occupants)
        {
            if (!IsCurrentAperture(aperture?.pawn, aperture) || Current.Game?.GetComponent<GuIndividualRegistry>() != registry) return;
            string details = occupants.Count == 0 ? "No bound Gu." : string.Join("\n\n", occupants.Select(record =>
                $"{Describe(record)}\n{RefinementStatus(record)}{AbilityStatus(record)}\nCondition: {record.Condition}; custody revision: {record.CustodyRevision}; "
                + $"identity: {record.Id}; aperture: {aperture.ApertureId}"));
            Find.WindowStack.Add(new Dialog_MessageBox(
                $"{aperture.pawn.LabelShort}'s mortal aperture ({registry.ApertureClaimCount(aperture)}/{GuIndividualRegistry.MortalApertureCapacity})\n\n{details}"));
        }

        private static string Describe(GuRecord record) =>
            $"{record.Species?.label ?? record.SpeciesDefName} [{record.Id}]";

        private static string AbilityStatus(GuRecord record) => record.Species?.abilities is { Count: > 0 } abilities
            ? "\nAbilities: " + string.Join(", ", abilities.Where(ability => ability != null).Select(ability =>
                $"{ability.LabelCap} ({ability.essenceCost:0.#} essence, {ability.range:0} cells)"))
                + (record.Refinement?.CultivatorId == null ? " - granted only after refinement" : "")
            : "";

        private static string RefinementStatus(GuRecord record)
        {
            string ownerId = record?.Refinement?.CultivatorId;
            if (string.IsNullOrWhiteSpace(ownerId)) return "Unrefined; no cultivator recognizes this Gu.";
            var owner = Current.Game?.GetComponent<GuWorldComponent>()?.FindCultivator(ownerId);
            return $"Recognized cultivator: {owner?.CachedName ?? ownerId}";
        }

        private static Hediff_Aperture FindAperture(Pawn host)
        {
            var matches = host?.health?.hediffSet?.hediffs.OfType<Hediff_Aperture>()
                .Where(hediff => hediff.def == GuWorldComponent.ApertureDef).Take(2).ToArray();
            return matches?.Length == 1 ? matches[0] : null;
        }

        private static bool IsCurrentAperture(Pawn host, Hediff_Aperture aperture) =>
            host?.IsColonistPlayerControlled == true && aperture != null && aperture.pawn == host
            && aperture.def == GuWorldComponent.ApertureDef && host.health?.hediffSet?.hediffs.Contains(aperture) == true;
    }
}
