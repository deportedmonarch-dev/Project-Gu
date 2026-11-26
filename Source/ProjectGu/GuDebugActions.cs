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

        /// <summary>
        /// Alpha 5B: spawn a registered wild individual of any configured species. Vanilla "Spawn pawn" and "Spawn thing" cannot
        /// make Gu: a Gu body without a canonical registry record would be an unidentified duplicate, so the projection's
        /// PostMake refuses it. This tool goes through the registry's own creation path instead; the individual carries the
        /// development origin, can be captured with an ordinary colonist, refined, stored and fed like any other, and no spring
        /// population is debited.
        /// </summary>
        [DebugAction("Project GU", "Spawn registered wild Gu...", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 900)]
        private static System.Collections.Generic.List<DebugActionNode> SpawnRegisteredWildGu()
        {
            var nodes = new System.Collections.Generic.List<DebugActionNode>();
            foreach (var species in DefDatabase<GuSpeciesDef>.AllDefsListForReading.Where(GuIndividualRegistry.IsDevelopmentSpawnable).OrderBy(def => def.label))
            {
                var chosen = species;
                nodes.Add(new DebugActionNode(species.LabelCap, DebugActionType.ToolMap, () =>
                {
                    var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
                    var cell = UI.MouseCell();
                    if (registry?.TrySpawnDevelopmentGu(chosen, Find.CurrentMap, cell, configuredSpecies: true, out var pawn) == true)
                        Messages.Message($"Spawned a registered wild {chosen.label} [{pawn.GuId?.Substring(0, 8)}] (development origin). "
                            + "Capture it with a colonist through the right-click menu.", new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
                    else Messages.Message($"Could not spawn a {chosen.label} here: choose an open, unfogged, standable cell with no pawn on it, "
                        + "while Gu custody is idle.", MessageTypeDefOf.RejectInput, false);
                }));
            }
            return nodes;
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
            if (registry == null || aperture == null)
            {
                Messages.Message("Choose a colonist with one valid aperture.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            ChooseRecord(registry.ApertureOccupants(aperture), record =>
                Report(registry.TryWithdrawDormant(record.Id, record.CustodyRevision, host, aperture, out _, out string reason),
                    reason, $"Withdrew Gu {record.Id} from the aperture."), "The aperture has no withdrawable Gu.");
        }

        [DebugAction("Project GU", "Release active Gu from selected colonist", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReleaseActiveGu()
        {
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (Find.Selector.SingleSelectedThing is not Pawn host || host.Map != Find.CurrentMap
                || SingleDevelopmentAperture(host) is not { } aperture || registry == null)
            {
                Messages.Message("Select a colonist with one valid aperture, then click an adjacent open cell.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            IntVec3 cell = UI.MouseCell();
            ChooseRecord(registry.ApertureOccupants(aperture), record =>
                Report(registry.TryReleaseActive(record.Id, record.CustodyRevision, host, aperture, Find.CurrentMap, cell,
                    out _, out string reason), reason, $"Released Gu {record.Id} from the aperture."),
                "The aperture has no active Gu to release.");
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

        // ---- Phase 2 lifecycle and operator recovery (Development Mode only) ----------------------------------

        private static GuIndividualRegistry LifecycleRegistry => Prefs.DevMode ? Current.Game?.GetComponent<GuIndividualRegistry>() : null;

        private static void Report(bool success, string reason, string done) =>
            Messages.Message(success ? reason ?? done : reason, success ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.RejectInput, false);

        private static void ChooseRecord(System.Collections.Generic.IEnumerable<GuRecord> candidates, System.Action<GuRecord> act, string none)
        {
            var options = candidates.Select(record => new FloatMenuOption($"{record.SpeciesDefName} {record.Id}"
                + (record.IsQuarantined ? " (quarantined)" : record.IsUnresolved ? $" ({record.Unresolved})" : ""), () => act(record))).ToList();
            if (options.Count == 0) Messages.Message(none, MessageTypeDefOf.RejectInput, false);
            else Find.WindowStack.Add(new FloatMenu(options));
        }

        [DebugAction("Project GU", "Inspect unresolved and quarantined Gu", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InspectRecordsNeedingReview()
        {
            if (LifecycleRegistry is not { } registry) return;
            // Read-only: inspection never changes records.
            var review = registry.RecordsNeedingReview.ToArray();
            Log.Message(review.Length == 0 ? "[Project GU] No unresolved or quarantined Gu."
                : $"[Project GU] {review.Length} Gu need review:\n\n" + string.Join("\n\n", review.Select(registry.DescribeForOperator)));
        }

        [DebugAction("Project GU", "Recovery: adopt selected rogue projection", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AdoptRogue()
        {
            if (LifecycleRegistry is not { } registry) return;
            var tag = Find.Selector.SingleSelectedThing?.GuTag();
            bool success = registry.TryAdoptRogue(tag, "Operator reviewed the evidence in Development Mode and adopted the sole matching candidate.", out string reason);
            Report(success, reason, "The operator adopted the rogue projection.");
        }

        [DebugAction("Project GU", "Recovery: declare unresolved Gu lost", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DeclareLost()
        {
            if (LifecycleRegistry is not { } registry) return;
            ChooseRecord(registry.RecordsNeedingReview.Where(record => !record.IsQuarantined), record =>
                Report(registry.TryDeclareLost(record.Id, null, out string reason), reason, $"Gu {record.Id} was declared lost."),
                "No unresolved Gu can be declared lost.");
        }

        [DebugAction("Project GU", "Recovery: lift resolved quarantine", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LiftQuarantine()
        {
            if (LifecycleRegistry is not { } registry) return;
            ChooseRecord(registry.RecordsNeedingReview.Where(record => record.IsQuarantined), record =>
                Report(registry.TryLiftQuarantine(record.Id, null, out string reason), reason, $"Quarantine lifted on Gu {record.Id}."),
                "No quarantined Gu.");
        }

        [DebugAction("Project GU", "Recover world-location Gu here", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RecoverWorldSite()
        {
            if (LifecycleRegistry is not { } registry) return;
            Map map = Find.CurrentMap;
            IntVec3 cell = UI.MouseCell();
            ChooseRecord(registry.WorldSiteRecordsAt(map), record =>
                Report(registry.TryRecoverFromWorldSite(record.Id, record.CustodyRevision, map, cell, out _, out string reason), reason,
                    $"Recovered Gu {record.Id}."), "No Gu is held at this map's world location.");
        }

        [DebugAction("Project GU", "Extract dormant Gu from selected corpse", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExtractFromCorpse(Pawn recipient)
        {
            if (LifecycleRegistry is not { } registry) return;
            if (Find.Selector.SingleSelectedThing is not Corpse { InnerPawn: { } host }
                || SingleDevelopmentAperture(host) is not { } aperture)
            {
                Messages.Message("Select a cultivator's corpse, then click an adjacent colonist.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            ChooseRecord(registry.Records.Where(record => record != null && record.IsActionable && record.ContainerId == aperture.ApertureId
                && record.ContainerKind == GuContainerKind.MortalAperture), record =>
                Report(registry.TryExtractFromDeceased(record.Id, record.CustodyRevision, recipient, out _, out string reason), reason,
                    $"Extracted Gu {record.Id}."), "The corpse's aperture holds no authorized Gu.");
        }

        [DebugAction("Project GU", "Deliberately destroy a held or contained Gu", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DestroyDeliberately(Pawn actor)
        {
            if (LifecycleRegistry is not { } registry) return;
            var aperture = SingleDevelopmentAperture(actor);
            var held = actor.inventory?.GetDirectlyHeldThings().Select(registry.Resolve).Where(record => record != null)
                ?? Enumerable.Empty<GuRecord>();
            var contained = aperture == null ? Enumerable.Empty<GuRecord>() : registry.ApertureOccupants(aperture);
            ChooseRecord(held.Concat(contained), record =>
                Report(registry.TryEndLife(record.Id, record.CustodyRevision, GuTerminalOutcome.DeliberatelyDestroyed,
                    $"dev-destroy:{record.Id}", actor, "Destroyed by its cultivator (Development Mode).", out string reason), reason,
                    $"Gu {record.Id} was destroyed."), "This Pawn holds no authorized Gu and its aperture is empty.");
        }

        [DebugAction("Project GU", "Log Gu history of all records", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogHistory()
        {
            if (LifecycleRegistry is not { } registry) return;
            Log.Message("[Project GU] Gu lifecycle evidence:\n\n" + string.Join("\n\n", registry.Records.Where(record => record != null)
                .Select(record => record.DebugDescription + "\n" + record.HistoryDescription)));
        }

        [DebugAction("Project GU", "Inspect hidden cultivation potential", actionType = DebugActionType.ToolMapForPawns,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Inspect(Pawn pawn)
        {
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            var record = world?.GetOrCreate(pawn);
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
