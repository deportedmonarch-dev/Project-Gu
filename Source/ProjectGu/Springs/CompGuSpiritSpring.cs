using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using ProjectGu.Domain;
using ProjectGu.Underground;

namespace ProjectGu.Springs
{
    /// <summary>
    /// 0.2.4: saved finite reserve + population ecology. Existing 0.2.3 save keys
    /// stay readable; an explicit migration migrates the old Hope Gu charges once.
    /// No off-map regeneration, and the observable population is currently aggregated.
    /// </summary>
    public sealed partial class CompGuSpiritSpring : ThingComp, IGuCohortHabitat
    {
        private int nextProductionTick = -1;
        private int lifetimeProduced;
        // Legacy 0.2.2/0.2.3 keys: do not rename or remove.
        private int hopeGuAvailable = 3;
        private int nextHopeRecoveryTick = -1;
        private int nextHabitatTick = -1;
        private int currentHabitatPatches;

        // Retained v0.2.4 save keys; after migration these are compatibility shadows.
        private int reserve = -1;
        private int matureHopeGu = -1;
        private int juvenileHopeGu;
        private int carryingCapacity = 3;
        private int floraSupport;
        private int nextEcologyTick = -1;

        // v0.2.5 authoritative species-keyed cohorts, owned by this specific spring.
        // The schema marker and collection together determine whether legacy migration is safe.
        private int populationSchema;
        private bool populationsReady;
        private string habitatId = "";
        private List<GuPopulationRecord> populations;
        private const string HopeSpecies = "GU_HopeGu";
        internal const string TestSpecies = "GU_DevTestSpringGu";
        private const int EcologyDay = 60000;
        private const int SafetyRecount = 10000;
        private const int ReservePerStone = 1;
        private CompProperties_GuSpiritSpring Props => (CompProperties_GuSpiritSpring)props;
        public int HopeGuAvailable => DefDatabase<GuSpeciesDef>.GetNamedSilentFail(HopeSpecies) != null
            ? MatureCountFor(HopeSpecies) : 0;
        public int HopeGuJuvenile => GuPopulationLedger.Find(populations, HopeSpecies)?.JuvenileCount ?? 0;
        public int HabitatSupport { get { EnsureHabitatFresh(); return floraSupport; } }
        public int Reserve => reserve;
        public string HabitatId => habitatId;
        public int MatureCountFor(string speciesDefName) => populationsReady
            ? GuPopulationLedger.Mature(populations, speciesDefName) : 0;

        Thing IGuCohortHabitat.Anchor => parent;
        bool IGuCohortHabitat.HasUniqueIdentity => parent?.Spawned == true && !string.IsNullOrEmpty(habitatId)
            && parent.Map.GetComponent<GuEcologyMapComponent>()?.ContainsSpring(this) == true
            && !Find.Maps.Any(map => map.GetComponent<GuEcologyMapComponent>()?.HasOtherSpringWithId(this) == true);

        GuPopulationRecord IGuCohortHabitat.CohortFor(string speciesDefName) =>
            populationsReady ? GuPopulationLedger.Find(populations, speciesDefName) : null;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            bool freshSpring = !respawningAfterLoad && populations == null && populationSchema == 0;
            EnsureInitialized();
            if (freshSpring) SeedInitialCohorts();
            parent.Map?.GetComponent<GuEcologyMapComponent>()?.Register(this);
            habitatDirty = true;
        }
        public override void PostDeSpawn(Map previousMap, DestroyMode mode = DestroyMode.Vanish)
        {
            ClearCavernLighting(previousMap);
            previousMap?.GetComponent<GuEcologyMapComponent>()?.Unregister(this);
            base.PostDeSpawn(previousMap, mode);
        }

        public override void PostMapInit()
        {
            base.PostMapInit();
            if (UsesCavernComposition) EnsureHabitatFresh();
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) SyncLegacyShadows();
            Scribe_Values.Look(ref nextProductionTick, "guNextProductionTick", -1);
            Scribe_Values.Look(ref lifetimeProduced, "guLifetimeProduced", 0);
            Scribe_Values.Look(ref hopeGuAvailable, "guHopeGuAvailable", 3);
            Scribe_Values.Look(ref nextHopeRecoveryTick, "guNextHopeRecoveryTick", -1);
            Scribe_Values.Look(ref nextHabitatTick, "guNextHabitatTick", -1);
            Scribe_Values.Look(ref currentHabitatPatches, "guHabitatPatches", 0);
            Scribe_Values.Look(ref reserve, "guReserve024", -1);
            Scribe_Values.Look(ref matureHopeGu, "guMatureHope024", -1);
            Scribe_Values.Look(ref juvenileHopeGu, "guJuvenileHope024", 0);
            Scribe_Values.Look(ref carryingCapacity, "guHopeCapacity024", 3);
            Scribe_Values.Look(ref floraSupport, "guFloraSupport024", 0);
            Scribe_Values.Look(ref nextEcologyTick, "guNextEcology024", -1);
            Scribe_Values.Look(ref nextFloraGrowthTick, "guNextFloraGrowthTick", -1);
            Scribe_Values.Look(ref usesBotanicalFlora, "guUsesBotanicalFlora");
            Scribe_Values.Look(ref cavernApproach, "guCavernApproach", IntVec3.Invalid);
            Scribe_Values.Look(ref populationSchema, "guPopulationSchema025", 0);
            Scribe_Values.Look(ref habitatId, "guHabitatId025", "");
            Scribe_Collections.Look(ref populations, "guPopulationRecords025", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) EnsureInitialized();
        }
        private void EnsureInitialized()
        {
            if (reserve < 0) reserve = Math.Max(1, Props.initialReserve);
            if (string.IsNullOrEmpty(habitatId)) habitatId = "gu-habitat-" + Guid.NewGuid().ToString("N");
            populationsReady = false;
            if (populationSchema < 0 || populationSchema > 1 ||
                (populationSchema == 1 && populations == null) ||
                GuPopulationLedger.HasDuplicateSpecies(populations) ||
                (populations != null && populations.Exists(record =>
                    record.MatureCount < 0 || record.JuvenileCount < 0 || record.Revision < 0)))
            {
                Log.ErrorOnce("[Project GU] Invalid spring population data; cohort changes disabled: " +
                    habitatId, habitatId.GetHashCode());
                return;
            }

            if (populations == null)
            {
                // A missing list in an unversioned save is the legacy format.
                // An existing list, including an empty one, already owns the counts.
                if (matureHopeGu < -1 || juvenileHopeGu < 0 ||
                    (matureHopeGu == -1 && hopeGuAvailable < 0) ||
                    DefDatabase<GuSpeciesDef>.GetNamedSilentFail(HopeSpecies) == null)
                {
                    Log.ErrorOnce("[Project GU] Cannot migrate legacy spring population: " +
                        habitatId, habitatId.GetHashCode());
                    return;
                }
                int adults = matureHopeGu >= 0 ? matureHopeGu : Math.Min(hopeGuAvailable, Props.maxHopeGu);
                int tick = nextEcologyTick;
                if (tick < 0 && Find.TickManager != null)
                {
                    int now = Find.TickManager.TicksGame;
                    tick = nextHopeRecoveryTick > now ? nextHopeRecoveryTick : now + EcologyDay;
                }
                populations = new List<GuPopulationRecord>
                {
                    new GuPopulationRecord(HopeSpecies, Math.Max(0, adults), juvenileHopeGu, tick)
                };
            }

            populationSchema = 1;
            populationsReady = true;
            foreach (GuPopulationRecord record in populations)
            {
                if (DefDatabase<GuSpeciesDef>.GetNamedSilentFail(record.SpeciesDefName) == null)
                    Log.Warning("[Project GU] Dormant spring cohort " + record.SpeciesDefName +
                        " has no species definition: " + habitatId);
            }
            SyncLegacyShadows();
        }

        // Only a spring created in this session reaches here; saved springs already own their cohort list.
        private void SeedInitialCohorts()
        {
            if (!populationsReady || Props.initialCohorts == null) return;
            foreach (var entry in Props.initialCohorts)
            {
                if (entry?.species == null || entry.mature < 0 || entry.juvenile < 0 || entry.mature + entry.juvenile == 0
                    || GuPopulationLedger.Find(populations, entry.species.defName) != null) continue;
                GuPopulationLedger.TryAddCohort(populations, new GuPopulationRecord(entry.species.defName, entry.mature, entry.juvenile, -1));
            }
            SyncLegacyShadows();
        }

        private void SyncLegacyShadows()
        {
            if (!populationsReady) return;
            GuPopulationRecord hope = GuPopulationLedger.Find(populations, HopeSpecies);
            // Shadows are compatibility diagnostics, not a second mutable authority.
            matureHopeGu = hope?.MatureCount ?? 0;
            juvenileHopeGu = hope?.JuvenileCount ?? 0;
            hopeGuAvailable = matureHopeGu;
            nextEcologyTick = hope?.NextEcologyTick ?? -1;
        }

        public bool TryConsumePopulation(string speciesDefName, int matureCount)
        {
            if (!populationsReady || !parent.Spawned || matureCount <= 0 ||
                string.IsNullOrWhiteSpace(speciesDefName) ||
                DefDatabase<GuSpeciesDef>.GetNamedSilentFail(speciesDefName) == null) return false;
            if (!GuPopulationLedger.TryConsumeMature(populations, speciesDefName, matureCount))
                return false;
            SyncLegacyShadows();
            return true;
        }

        // Compatibility with the 0.2.4 awakening service.
        public bool TryConsumeHopeGu() => TryConsumePopulation(HopeSpecies, 1);

        // Developer-only test: proves the same spring can persist two distinct species.
        // It never runs automatically and is not a real lore Gu species.
        public void DebugSeedSecondaryCohort()
        {
            if (!populationsReady || !Prefs.DevMode || !parent.Spawned) return;
            if (DefDatabase<GuSpeciesDef>.GetNamedSilentFail(TestSpecies) == null) return;
            if (GuPopulationLedger.TryAddCohort(populations,
                new GuPopulationRecord(TestSpecies, 2, 1, -1)))
                Messages.Message("GU: secondary test cohort added to spring.",
                    MessageTypeDefOf.NeutralEvent, false);
        }

        private IEnumerable<Gizmo> EncounterGizmos()
        {
            if (!populationsReady || parent?.Spawned != true || !PlayerCanAccessEncounterMap(parent.Map)) yield break;
            foreach (GuPopulationRecord cohort in populations)
            {
                if (cohort?.MatureCount <= 0 || DefDatabase<GuSpeciesDef>.GetNamedSilentFail(cohort.SpeciesDefName) is not { } species
                    || !species.wildPopulationEnabled || species.capabilityTags?.Contains("development_test") == true
                    || species.wildProjectionKind == null || !Thing_GuDormantProjection.IsConfigured(species.dormantProjectionDef)) continue;

                string speciesName = species.label;
                yield return new Command_Action
                {
                    defaultLabel = $"Search for {speciesName}",
                    defaultDesc = $"Draw one mature {speciesName} from this spring cohort. It remains wild and unrefined until separately captured and refined.",
                    action = () => EncounterWildGu(species)
                };
            }
        }

        private void EncounterWildGu(GuSpeciesDef species)
        {
            if (parent?.Spawned != true || !PlayerCanAccessEncounterMap(parent.Map) || species == null
                || Current.Game?.GetComponent<GuIndividualRegistry>() is not { } registry)
            {
                Messages.Message("This spring cannot provide a wild Gu on an inaccessible map.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            var cohort = GuPopulationLedger.Find(populations, species.defName);
            if (cohort == null || cohort.MatureCount <= 0)
            {
                Messages.Message($"No mature {species.label} remain in this spring cohort.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Map map = parent.Map;
            IntVec3 cell = GenRadial.RadialCellsAround(parent.Position, 6f, true).FirstOrDefault(candidate =>
                candidate.InBounds(map) && !candidate.Fogged(map) && candidate.Standable(map)
                && !map.reservationManager.IsReserved(candidate)
                && candidate.GetThingList(map).Count == 0, IntVec3.Invalid);
            string eventKey = $"player-encounter:{HabitatId}:{species.defName}:{cohort.Revision}";
            string reason = "No safe nearby place is available for a wild Gu.";
            GuRecord individual = null;
            bool success = cell.IsValid
                && GuIndividualRegistry.TryPrepareMaterializationRequest(this, species.defName, eventKey, out var request)
                && registry.TryEncounterWildCohort(this, request, map, cell, out individual, out reason);
            if (success)
                Messages.Message($"A wild {species.label} emerged from the spring. Select a colonist and right-click the Gu to capture it.",
                    new LookTargets(registry.ProjectionOf(individual)), MessageTypeDefOf.PositiveEvent, false);
            else
                Messages.Message(cell.IsValid ? reason ?? "The spring could not safely release a Gu." : "No safe nearby place is available for a wild Gu.",
                    MessageTypeDefOf.RejectInput, false);
        }

        private static bool PlayerCanAccessEncounterMap(Map map) => map is { Disposed: false }
            && (map.IsPlayerHome || Current.Game?.GetComponent<GuPocketProbeComponent>()?.HasProtectedProjectionRoute(map) == true);
        private void EcologyDayStep()
        {
            if (!populationsReady || DefDatabase<GuSpeciesDef>.GetNamedSilentFail(HopeSpecies) == null) return;
            GuPopulationRecord hope = GuPopulationLedger.Find(populations, HopeSpecies);
            if (hope == null) return;
            // Same 0.2.4 rule: mature juveniles first, one birth only with flora + reserve.
            hope.AdvanceDaily(carryingCapacity, floraSupport > 0 && reserve > 0, 1);
            SyncLegacyShadows();
        }
        public void DebugSimulateEcologyDay()
        {
            if (!parent.Spawned) return;
            RefreshHabitat();
            EcologyDayStep();
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            foreach (Gizmo gizmo in EncounterGizmos()) yield return gizmo;
            if (Prefs.DevMode && parent.Spawned)
            {
                foreach (Gizmo gizmo in ProductionGizmos())
                    yield return gizmo;
                foreach (Gizmo gizmo in FloraGizmos())
                    yield return gizmo;
            }
            if (populationsReady && Prefs.DevMode && parent.Spawned)
                yield return new Command_Action
                {
                    defaultLabel = "GU: simulate ecology day",
                    defaultDesc = "Advance Hope Gu maturation and reproduction by one daily step, without advancing global time or generating stones. TEST ONLY.",
                    action = DebugSimulateEcologyDay
                };
            if (populationsReady && Prefs.DevMode && parent.Spawned)
                yield return new Command_Action
                {
                    defaultLabel = "GU: seed secondary test cohort",
                    defaultDesc = "Add 2 mature + 1 juvenile developer-only Gu to this spring, once. Tests multi-species persistence. Use a backup save.",
                    action = DebugSeedSecondaryCohort
                };
        }
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned || parent.Map == null || Find.TickManager == null) return;
            int now = Find.TickManager.TicksGame;
            if (UsesCavernFlora && now >= nextFloraGrowthTick)
            {
                CavernFloraStep();
                nextFloraGrowthTick = now + Math.Max(250, Props.cavernGrowthIntervalTicks);
            }
            if (habitatDirty || nextHabitatTick < 0 || now >= nextHabitatTick) RefreshHabitat();
            GuPopulationRecord hope = GuPopulationLedger.Find(populations, HopeSpecies);
            if (populationsReady && hope != null &&
                DefDatabase<GuSpeciesDef>.GetNamedSilentFail(HopeSpecies) != null)
            {
                if (hope.NextEcologyTick < 0) hope.SetNextEcologyTick(now + EcologyDay);
                if (now >= hope.NextEcologyTick)
                {
                    // Bounded single update. Never generate thousands of catch-up events.
                    EcologyDayStep();
                    hope.SetNextEcologyTick(now + EcologyDay);
                    nextHopeRecoveryTick = hope.NextEcologyTick;
                    SyncLegacyShadows();
                }
            }
            int interval = Math.Max(250, Props.productionIntervalTicks);
            if (nextProductionTick < 0) { nextProductionTick = now + interval; return; }
            if (now < nextProductionTick) return;
            nextProductionTick = now + interval;
            AttemptProduction();
        }
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null || !selPawn.IsColonistPlayerControlled || !parent.Spawned) yield break;
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            var record = world?.GetOrCreate(selPawn);
            if (record == null || record.State != AwakeningState.Unattempted) yield break;
            if (HopeGuAvailable <= 0)
            {
                yield return new FloatMenuOption("Attempt awakening (no mature Hope Gu present)", null);
                yield break;
            }
            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
            {
                yield return new FloatMenuOption("Attempt awakening (cannot reach spring)", null);
                yield break;
            }
            yield return new FloatMenuOption("Attempt awakening at spirit spring", () =>
            {
                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("GU_AwakenAtSpring");
                if (def != null && selPawn.Spawned && parent.Spawned)
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, parent));
            });
        }
        public override string CompInspectStringExtra()
        {
            EnsureHabitatFresh();
            GuPopulationRecord extra = GuPopulationLedger.Find(populations, TestSpecies);
            return "Spring profile: " + Props.regionProfile + " (prototype preset)" +
                "\nPrimeval reserve: " + Math.Max(0, reserve) + " / " + Props.initialReserve +
                "\nPurity: " + Props.purityPercent + "%" +
                "\nPrimeval stones produced: " + lifetimeProduced + "; next: " + ProductionDue +
                "\nSpirit flora: " + currentHabitatPatches + " patches (support " + floraSupport + ")" +
                (populationsReady
                    ? "\nHope Gu: " + HopeGuAvailable + " mature + " + juvenileHopeGu +
                        " juvenile / " + carryingCapacity + " capacity"
                    : "\nGu population unavailable. See the game log.") +
                (extra != null ? "\nDev test Gu: " + extra.MatureCount + " mature + " + extra.JuvenileCount + " juvenile" : "") +
                (floraSupport > 0 ? "\nHabitat supports Hope Gu reproduction" : "\nNo habitat: reproduction suspended");
        }
    }
}
