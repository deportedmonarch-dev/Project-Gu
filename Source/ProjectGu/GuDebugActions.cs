using LudeonTK;
using RimWorld;
using Verse;
using ProjectGu.Domain;
using ProjectGu.Springs;
using System.Linq;

namespace ProjectGu
{
    /// <summary>
    /// Dev-mode tools ONLY. These bypass spirit-spring ritual prerequisites,
    /// which will be introduced in 0.2.1+. Not available in normal gameplay.
    /// </summary>
    public static class GuDebugActions
    {
        [DebugAction("Project GU", "Spawn test Gu", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnTestGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var species = DefDatabase<GuSpeciesDef>.GetNamedSilentFail("GU_DevTestSpringGu");
            if (registry?.TrySpawnDevelopmentGu(species, Find.CurrentMap, UI.MouseCell(), out _) != true)
                Messages.Message("Choose an open cell on a loaded map for the test Gu.", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Materialize adult Gu from selected spring", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MaterializeTestGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var spring = Find.Selector.SingleSelectedThing?.TryGetComp<CompGuSpiritSpring>();
            if (registry == null || spring == null || spring.parent.Map != Find.CurrentMap
                || !GuIndividualRegistry.TryPrepareMaterializationRequest(spring, CompGuSpiritSpring.TestSpecies,
                    $"dev-cohort:{spring.HabitatId}:{CompGuSpiritSpring.TestSpecies}", out var request))
            {
                Messages.Message("Select a spring with a seeded secondary test cohort, then click an open nearby cell.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            bool success = registry.TryMaterializeCohort(spring, request, Find.CurrentMap, UI.MouseCell(), out _, out string reason);
            Messages.Message(reason, success ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Make test Gu dormant", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeTestGuDormant(Pawn selected)
        {
            if (selected is not Pawn_GuWildProjection pawn
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return;
            if (!registry.TryMakeDormant(pawn, pawn.GuRevision, pawn.Position, out _, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Put selected dormant Gu in inventory", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TakeDormantGu(Pawn pawn)
        {
            if (Find.Selector.SingleSelectedThing is not Thing_GuDormantProjection thing
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry)
            {
                Messages.Message("Select a dormant Gu, then click the adjacent colonist.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!registry.TryTakeDormant(thing, thing.GuRevision, pawn, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Drop dormant Gu from selected colonist", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DropDormantGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (Find.Selector.SingleSelectedThing is not Pawn pawn || pawn.Map != Find.CurrentMap
                || pawn.inventory?.GetDirectlyHeldThings() is not { } inventory || registry == null)
            {
                Messages.Message("Select a colonist with a dormant Gu, then click an adjacent open cell.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            IntVec3 cell = UI.MouseCell();
            var options = inventory.OfType<Thing_GuDormantProjection>().Where(thing => registry.Resolve(thing) != null).Select(thing =>
            {
                int revision = thing.GuRevision;
                return new FloatMenuOption($"{thing.LabelCap} ({thing.GuId})", () =>
                {
                    if (!registry.TryDropDormant(thing, revision, pawn, cell, out string reason))
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                });
            }).ToList();
            if (options.Count == 0)
                Messages.Message("The selected colonist has no Gu with a valid inventory binding.", MessageTypeDefOf.RejectInput, false);
            else Find.WindowStack.Add(new FloatMenu(options));
        }

        [DebugAction("Project GU", "Store dormant Gu in aperture", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StoreDormantGu(Pawn host)
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var aperture = SingleDevelopmentAperture(host);
            if (aperture == null || host.inventory?.GetDirectlyHeldThings() is not { } inventory || registry == null)
            {
                Messages.Message("Choose a colonist with one aperture and a native inventory.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var options = inventory.OfType<Thing_GuDormantProjection>().Where(thing => registry.Resolve(thing) != null).Select(thing =>
            {
                int revision = thing.GuRevision;
                return new FloatMenuOption($"{thing.LabelCap} ({thing.GuId})", () =>
                {
                    if (!registry.TryStoreDormant(thing, revision, host, aperture, out string reason))
                        Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                });
            }).ToList();
            if (options.Count == 0)
                Messages.Message("The colonist's inventory has no Gu with a valid binding.", MessageTypeDefOf.RejectInput, false);
            else Find.WindowStack.Add(new FloatMenu(options));
        }

        [DebugAction("Project GU", "Withdraw dormant Gu from aperture", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WithdrawDormantGu(Pawn host)
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            var aperture = SingleDevelopmentAperture(host);
            if (registry?.ResolveApertureOccupant(aperture) is not { } record)
            {
                Messages.Message("Choose a colonist whose exact aperture has an authorized Gu.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!registry.TryWithdrawDormant(record.Id, record.CustodyRevision, host, aperture, out _, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Release active Gu from selected colonist", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReleaseActiveGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (Find.Selector.SingleSelectedThing is not Pawn host || host.Map != Find.CurrentMap
                || SingleDevelopmentAperture(host) is not { } aperture
                || registry?.ResolveApertureOccupant(aperture) is not { } record)
            {
                Messages.Message("Select a colonist with an authorized aperture Gu, then click an adjacent open cell.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!registry.TryReleaseActive(record.Id, record.CustodyRevision, host, aperture, Find.CurrentMap, UI.MouseCell(), out _, out string reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Project GU", "Inspect aperture Gu custody", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InspectApertureCustody(Pawn host)
        {
            if (!Prefs.DevMode || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return;
            // Read-only: inspection never changes records.
            var claims = registry.Records.Where(entry => entry?.Placement == GuPlacementKind.Contained
                && entry.AnchorHint == host.GetUniqueLoadID()).ToArray();
            string apertureId = SingleDevelopmentAperture(host)?.ApertureId ?? "(no single current aperture, or no Gu ever stored)";
            Log.Message($"[Project GU] Aperture custody for {host.LabelShort} ({host.GetUniqueLoadID()}) | {apertureId}\n"
                + (claims.Length == 0 ? "No Gu claims this host's aperture." : string.Join("\n\n", claims.Select(entry => entry.DebugDescription))));
        }

        private static Hediff_Aperture SingleDevelopmentAperture(Pawn host)
        {
            var apertures = host.health?.hediffSet?.hediffs.OfType<Hediff_Aperture>()
                .Where(hediff => hediff.def == GuWorldComponent.ApertureDef).Take(2).ToArray();
            return apertures?.Length == 1 ? apertures[0] : null;
        }

        [DebugAction("Project GU", "Log Gu individual records", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogIndividuals()
        {
            if (!Prefs.DevMode || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry) return;
            Log.Message((registry.Records.Count == 0 ? "[Project GU] No individual Gu records."
                : "[Project GU] Individual Gu records:\n" + string.Join("\n\n", registry.Records.Where(record => record != null)
                    .Select(record => record.DebugDescription + $"\nObserved: {registry.Observe(record).Form}")))
                + (registry.RogueProjections.Count == 0 ? "" : "\n\nRogue projections: " + string.Join(", ",
                    registry.RogueProjections.Select(tag => $"{tag.parent?.GetUniqueLoadID()} claims {tag.GuId}")))
                + (registry.LastMigrationSummary == null ? "" : "\n\n" + registry.LastMigrationSummary));
        }

        [DebugAction("Project GU", "Inspect hidden cultivation potential", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Inspect(Pawn pawn)
        {
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            GuPawnRecord record = world?.GetOrCreate(pawn);
            if (record == null) return;
            string grade = record.LatentAptitude == 0 ? "none" :
                AptitudeRules.Grade(record.LatentAptitude) + " (" + record.LatentAptitude + ")";
            Messages.Message(pawn.LabelShort + ": state=" + record.State + ", hidden potential=" + grade,
                MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Project GU", "Test awakening on pawn", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Awaken(Pawn pawn)
        {
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            if (world == null) return;
            world.TryAwaken(pawn, out string result);
            Messages.Message(pawn.LabelShort + ": " + result, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
