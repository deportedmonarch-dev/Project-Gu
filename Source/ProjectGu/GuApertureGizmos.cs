using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ProjectGu.Domain;
using ProjectGu.Interface;
using RimWorld;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Fallback "Manage aperture Gu" menu and the Gu ability commands for the selected cultivator. The Aperture inspection
    /// tab is the primary interface; this menu offers the same operations, built from the same
    /// <see cref="GuApertureActions"/>, until the tab has been accepted as a full replacement.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class GuApertureGizmos
    {
        private static readonly UnityEngine.Texture2D ManageIcon = ContentFinder<UnityEngine.Texture2D>.Get("ProjectGU/UI/ManageAperture", false);

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
                || GuApertureActions.FindAperture(__instance) is not { } aperture) return;

            var command = new Command_Action
            {
                defaultLabel = "Manage aperture Gu",
                icon = ManageIcon,
                defaultDesc = "Inspect and cultivate the aperture. Refine dormant Gu, or manage this cultivator's bonded Gu.\n\n"
                    + "The Aperture tab in this pawn's inspection pane offers the same actions with a full view of the aperture.",
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
            var aperture = GuApertureActions.FindAperture(host);
            if (registry == null || aperture == null) return options;

            GuRecord[] occupants = registry.ApertureOccupants(aperture).OrderBy(record => record.Id, StringComparer.Ordinal).ToArray();
            int claims = registry.ApertureClaimCount(aperture);
            options.Add(new FloatMenuOption($"Inspect aperture Gu ({claims}/{GuIndividualRegistry.MortalApertureCapacity})",
                () => Inspect(registry, aperture, occupants)));

            // One cultivation option, as before: advancement once the walls are full, otherwise nourishment.
            var cultivation = aperture.CanAdvanceRankOne
                ? GuApertureActions.Advance(host, aperture) : GuApertureActions.Nourish(host, aperture);
            if (cultivation != null) options.Add(GuApertureActions.ToOption(cultivation));

            var held = new List<Thing_GuDormantProjection>();
            var nearby = new List<Thing_GuDormantProjection>();
            GuApertureActions.CollectDormant(host, held, nearby);

            if (claims < GuIndividualRegistry.MortalApertureCapacity)
                foreach (var thing in held)
                    if (GuApertureActions.Store(host, aperture, registry, thing) is { } store)
                    {
                        string label = registry.Resolve(thing) is { } record ? GuApertureActions.Describe(record) : thing.LabelCap;
                        options.Add(store.Available
                            ? new FloatMenuOption(store.Label, () => GuApertureActions.Run(store, $"Stored {label} in the aperture."))
                            : new FloatMenuOption(store.Label, () => Messages.Message(store.DisabledReason, MessageTypeDefOf.RejectInput, false)));
                    }

            foreach (var record in occupants)
                if (GuApertureActions.Withdraw(host, aperture, registry, record) is { } withdraw)
                    options.Add(new FloatMenuOption(withdraw.Label,
                        () => GuApertureActions.Run(withdraw, $"Withdrew {GuApertureActions.Describe(record)} to inventory.")));

            var candidates = new List<Thing_GuDormantProjection>(held);
            foreach (var thing in nearby) if (!candidates.Contains(thing)) candidates.Add(thing);
            foreach (var thing in candidates)
            {
                if (GuApertureActions.EnforceWill(host, registry, thing) is { } enforce)
                    options.Add(new FloatMenuOption(enforce.Label, () => GuApertureActions.Run(enforce)));
                if (GuApertureActions.AbandonWill(host, registry, thing) is { } abandon)
                    options.Add(new FloatMenuOption(abandon.Label, () => GuApertureActions.Run(abandon,
                        "The refinement attempt was abandoned. Its essence remains spent; Gu identity, custody and allegiance are unchanged.")));
            }
            return options;
        }

        private static void OpenManagementMenu(Pawn host, Hediff_Aperture aperture, GuIndividualRegistry registry)
        {
            if (!GuApertureActions.CanManage(host, aperture) || Current.Game?.GetComponent<GuIndividualRegistry>() != registry)
            {
                Messages.Message("This cultivator's aperture is no longer available.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(BuildOptions(host)));
        }

        private static void Inspect(GuIndividualRegistry registry, Hediff_Aperture aperture, IReadOnlyList<GuRecord> occupants)
        {
            if (!GuApertureActions.CanManage(aperture?.pawn, aperture) || Current.Game?.GetComponent<GuIndividualRegistry>() != registry) return;
            string details = occupants.Count == 0 ? "No bound Gu." : string.Join("\n\n", occupants.Select(record =>
                $"{GuApertureActions.Describe(record)}\n{RefinementStatus(record)}{AbilityStatus(record)}\nCondition: {record.Condition}; custody revision: {record.CustodyRevision}; "
                + $"identity: {record.Id}; aperture: {aperture.ApertureId}"));
            Find.WindowStack.Add(new Dialog_MessageBox(
                $"{aperture.pawn.LabelShort}'s mortal aperture ({registry.ApertureClaimCount(aperture)}/{GuIndividualRegistry.MortalApertureCapacity})"
                + $"\n\n{aperture.CultivationStatus}\n\n{details}"));
        }

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
    }
}
