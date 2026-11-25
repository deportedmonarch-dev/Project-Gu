using System.Collections.Generic;
using System.Linq;
using ProjectGu.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Springs
{
    /// <summary>
    /// Lure encounters: a colonist sets out an item a species craves (Liquor Worm: alcohol) at the spring and waits.
    /// The individual still comes from this spring's cohort through the ordinary materialization, so population and
    /// identity are conserved exactly as for a plain search. The lure item is consumed only when an individual appears.
    /// </summary>
    public sealed partial class CompGuSpiritSpring
    {
        internal const string LureJobDefName = "GU_LureWildGu";

        private IEnumerable<FloatMenuOption> LureFloatMenuOptions(Pawn selPawn)
        {
            if (!populationsReady || parent?.Spawned != true || selPawn?.IsColonistPlayerControlled != true
                || selPawn.Map != parent.Map || !PlayerCanAccessEncounterMap(parent.Map)
                || DefDatabase<JobDef>.GetNamedSilentFail(LureJobDefName) is not { } jobDef) yield break;

            foreach (var species in LureSpecies())
            {
                string lureNames = string.Join(" or ", species.encounterLures.Select(lure => lure.label));
                if (GuPopulationLedger.Find(populations, species.defName)?.MatureCount is not > 0)
                {
                    yield return new FloatMenuOption($"Lure {species.label} with {lureNames} (none remain at this spring)", null);
                    continue;
                }
                var lure = FindLure(selPawn, species);
                if (lure == null)
                {
                    yield return new FloatMenuOption($"Lure {species.label} (requires {lureNames} carried or reachable here)", null);
                    continue;
                }
                var captured = species;
                yield return new FloatMenuOption($"Lure {species.label} with {lure.LabelNoCount}", () =>
                {
                    var current = FindLure(selPawn, captured);
                    if (current == null) return;
                    var job = JobMaker.MakeJob(jobDef, parent, current.Spawned ? current : LocalTargetInfo.Invalid);
                    job.count = 1;
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
            }
        }

        internal IEnumerable<GuSpeciesDef> LureSpecies() => populations == null ? Enumerable.Empty<GuSpeciesDef>()
            : populations.Select(cohort => DefDatabase<GuSpeciesDef>.GetNamedSilentFail(cohort?.SpeciesDefName))
                .Where(species => species is { wildPopulationEnabled: true, UsesLureEncounter: true }
                    && species.capabilityTags?.Contains("development_test") != true
                    && species.wildProjectionKind != null && Thing_GuDormantProjection.IsConfigured(species.dormantProjectionDef));

        /// <summary>A lure item in the pawn's own inventory, otherwise the nearest reachable one on this map.</summary>
        internal Thing FindLure(Pawn pawn, GuSpeciesDef species)
        {
            var held = pawn.inventory?.innerContainer?.FirstOrDefault(thing => species.encounterLures.Contains(thing.def));
            if (held != null) return held;
            if (pawn.carryTracker?.CarriedThing is { } carried && species.encounterLures.Contains(carried.def)) return carried;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                thing => species.encounterLures.Contains(thing.def) && !thing.IsForbidden(pawn) && pawn.CanReserve(thing));
        }

        /// <summary>The lure species this item attracts, if one still has a mature adult here.</summary>
        internal GuSpeciesDef LureSpeciesFor(ThingDef lure) => LureSpecies().FirstOrDefault(species =>
            species.encounterLures.Contains(lure) && GuPopulationLedger.Find(populations, species.defName)?.MatureCount > 0);
    }
}
