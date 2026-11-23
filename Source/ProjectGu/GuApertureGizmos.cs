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
                defaultDesc = "Inspect Gu identities. Only Gu refined by this cultivator can be stored in or withdrawn from the mortal aperture.",
                action = () => OpenManagementMenu(__instance, aperture, registry)
            };
            __result = (__result ?? Enumerable.Empty<Gizmo>()).Concat(new Gizmo[] { command });
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
            return options;
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
                $"{Describe(record)}\n{RefinementStatus(record)}\nCondition: {record.Condition}; custody revision: {record.CustodyRevision}; "
                + $"identity: {record.Id}; aperture: {aperture.ApertureId}"));
            Find.WindowStack.Add(new Dialog_MessageBox(
                $"{aperture.pawn.LabelShort}'s mortal aperture ({registry.ApertureClaimCount(aperture)}/{GuIndividualRegistry.MortalApertureCapacity})\n\n{details}"));
        }

        private static string Describe(GuRecord record) =>
            $"{record.SpeciesDefName} [{record.Id}]";

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
