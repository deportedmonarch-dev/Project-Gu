using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Underground;
using RimWorld;
using Verse;

namespace ProjectGu.Springs
{
    public sealed partial class CompGuSpiritSpring
    {
        // Only new cavern generation enables this schedule. A missing key leaves older
        // sites and surface springs on their established physical habitat rules.
        private int nextFloraGrowthTick = -1;
        private bool usesBotanicalFlora;
        private IntVec3 cavernApproach = IntVec3.Invalid;
        private bool habitatDirty = true;
        private long rawFloraUnits;
        private readonly Dictionary<ThingDef, int> habitatBySpecies = new();

        private bool UsesCavernFlora => nextFloraGrowthTick >= 0 && GuPocketSiteDef.IsSpringCavern(parent.Map);
        internal bool UsesBotanicalFlora => usesBotanicalFlora && UsesCavernFlora;
        internal bool UsesCavernComposition => UsesBotanicalFlora && cavernApproach.IsValid;
        private List<GuCavernFloraTarget> CavernFlora => usesBotanicalFlora ? Props.botanicalCavernFlora : Props.cavernFlora;
        private int HabitatRadius => UsesCavernFlora
            ? usesBotanicalFlora ? Math.Clamp(Props.botanicalHabitatRadius, 1, 16) : Math.Clamp(Props.cavernHabitatRadius, 1, 12)
            : Math.Max(1, Props.habitatRadius);

        internal bool HabitatContains(IntVec3 cell) =>
            Math.Abs(parent.Position.x - cell.x) <= HabitatRadius
            && Math.Abs(parent.Position.z - cell.z) <= HabitatRadius;

        public void NotifyNearbyFloraChanged(IntVec3 cell)
        {
            if (parent.Spawned && HabitatContains(cell)) habitatDirty = true;
        }

        internal void NotifySpringChanged(CompGuSpiritSpring other)
        {
            int overlap = HabitatRadius + other.HabitatRadius;
            IntVec3 offset = parent.Position - other.parent.Position;
            if (Math.Abs(offset.x) <= overlap && Math.Abs(offset.z) <= overlap)
                habitatDirty = true;
        }

        private void EnsureHabitatFresh()
        {
            if (habitatDirty) RefreshHabitat();
        }

        private void RefreshHabitat()
        {
            if (!parent.Spawned || parent.Map == null) return;

            Map map = parent.Map;
            var ecology = map.GetComponent<GuEcologyMapComponent>();
            bool managed = UsesCavernFlora;
            var orchids = UsesCavernComposition ? new List<IntVec3>(5) : null;
            habitatBySpecies.Clear();
            currentHabitatPatches = 0;
            rawFloraUnits = 0;
            foreach (IntVec3 cell in CellRect.CenteredOn(parent.Position, HabitatRadius).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing.Position != cell || thing.TryGetComp<CompGuSpiritFlora>() is not { } flora
                        || ecology.HabitatOwnerAt(cell) != this)
                        continue;
                    currentHabitatPatches++;
                    habitatBySpecies[thing.def] = habitatBySpecies.GetValueOrDefault(thing.def) + 1;
                    if (orchids != null && orchids.Count < 5 && flora.EcologyProfile == "moon"
                        && thing is Plant { Growth: >= 0.65f } plant && !plant.DeliberatelyCultivated())
                        orchids.Add(cell);
                }
            }
            foreach (var entry in habitatBySpecies)
                rawFloraUnits += entry.Key.GetCompProperties<CompProperties_GuSpiritFlora>().HabitatUnits(entry.Value, managed);
            floraSupport = managed
                ? GuFloraGrowth.EffectiveSupport(rawFloraUnits, Props.cavernSupportUnitsPerPoint, Props.cavernMaxFloraSupport)
                : (int)Math.Min(rawFloraUnits, int.MaxValue);
            // Existing adults survive habitat loss. Only subsequent births use the new capacity.
            carryingCapacity = GuFloraGrowth.HopeCapacity(floraSupport, Props.maxHopeGu);
            if (orchids != null) RefreshCavernLighting(map, orchids);
            habitatDirty = false;
            if (Find.TickManager != null) nextHabitatTick = Find.TickManager.TicksGame + SafetyRecount;
        }

        internal void InitializeCavernFlora(int seed, IntVec3 arrival)
        {
            if (nextFloraGrowthTick >= 0) return;
            var targets = Props.botanicalCavernFlora;
            if (targets.Count == 0 || targets.Any(target => target.flora == null
                || target.flora.plant == null
                || target.flora.size != new IntVec2(1, 1) || target.targetPatches < 1
                || target.flora.GetCompProperties<CompProperties_GuSpiritFlora>()?.growthSoils.Count is not > 0)
                || targets.Select(target => target.flora).Distinct().Count() != targets.Count)
                throw new InvalidOperationException("The spring cavern requires unique, configured one-cell flora targets.");

            usesBotanicalFlora = true;
            cavernApproach = arrival;
            nextFloraGrowthTick = Find.TickManager.TicksGame + Math.Max(250, Props.cavernGrowthIntervalTicks);
            parent.GetComp<CompGuCavernGlower>()?.UpdateLit(parent.Map);
            parent.GetComp<CompGuBotanicalGlower>()?.UpdateLit(parent.Map);
            habitatDirty = true;
            parent.Map.GetComponent<GuEcologyMapComponent>().NotifyHabitatRangeChanged(this);
            GrowCavernFlora(int.MaxValue, seed, initial: true);
        }

        private int GrowCavernFlora(int budget, int seed, bool initial = false)
        {
            if (!UsesCavernFlora || reserve <= 0) return 0;
            Map map = parent.Map;
            var ecology = map.GetComponent<GuEcologyMapComponent>();
            EnsureHabitatFresh();

            TerrainDef TerrainAt(IntVec3 cell) => cell.InBounds(map) ? cell.GetTerrain(map) : null;
            var footprint = parent.OccupiedRect();
            var approach = UsesCavernComposition ? GuSpringCavernLayout.MakeDryApproach(cavernApproach, footprint) : null;
            Func<IntVec3, float> basinScore = null;
            if (approach != null && CavernFlora.Any(target => habitatBySpecies.GetValueOrDefault(target.flora) < target.targetPatches))
            {
                var shore = GuSpringCavernLayout.MainBasinDistances(footprint, cell => cell.InBounds(map),
                    cell => TerrainAt(cell).IsWater);
                // Vacancy fills use the same field shape as the original seed.
                int fieldSeed = Gen.HashCombineInt(parent.Position.GetHashCode(), cavernApproach.GetHashCode());
                basinScore = cell => GuSpringCavernLayout.BasinPlantingScore(cell, parent.Position, fieldSeed, shore);
            }
            int planted = 0;
            foreach (GuCavernFloraTarget target in CavernFlora)
            {
                ThingDef flora = target.flora;
                planted += GuFloraGrowth.Fill(target, parent.Position, HabitatRadius, seed,
                    habitatBySpecies.GetValueOrDefault(flora), budget - planted, TerrainAt,
                    cell => ecology.HabitatOwnerAt(cell) == this && CanPlant(map, cell, flora, footprint, approach),
                    cell => cell.InBounds(map) && cell.GetThingList(map).Any(thing => thing.def == flora),
                    cell =>
                    {
                        if (initial)
                        {
                            var plant = (Plant)GuPocketProbeComponent.SpawnFixture(flora, cell, map);
                            plant.Growth = Rand.RangeSeeded(0.84f, 1f, Gen.HashCombineInt(seed, cell.GetHashCode()));
                            return true;
                        }
                        Thing grown = GenSpawn.Spawn(flora, cell, map, Rot4.North);
                        return grown?.Spawned == true && grown.Map == map && grown.Position == cell;
                    }, basinScore);
                if (planted == budget) break;
            }
            EnsureHabitatFresh();
            return planted;
        }

        private static bool CanPlant(Map map, IntVec3 cell, ThingDef flora, CellRect footprint, HashSet<IntVec3> approach)
        {
            if (footprint.ExpandedBy(1).Contains(cell) || approach?.Contains(cell) == true
                || (approach == null && BasinWaterOutputs(footprint).Contains(cell))
                || !cell.Standable(map) || map.zoneManager.ZoneAt(cell) != null)
                return false;

            foreach (Thing thing in cell.GetThingList(map))
                if (thing.def.category is ThingCategory.Building or ThingCategory.Item or ThingCategory.Pawn or ThingCategory.Plant
                    || thing is Blueprint or Frame or Fire)
                    return false;
            return GenSpawn.CanSpawnAt(flora, cell, map, Rot4.North)
                && (flora.plant == null || PlantUtility.CanNowPlantAt(flora, cell, map, false));
        }

        private string CavernFloraStep()
        {
            if (!UsesCavernFlora || reserve <= 0) return "The spring cannot sustain flora growth.";
            if (GrowCavernFlora(1, Gen.HashCombineInt(parent.thingIDNumber, nextFloraGrowthTick)) == 1)
                return "One flora patch grew.";

            Map map = parent.Map;
            var ecology = map.GetComponent<GuEcologyMapComponent>();
            TerrainDef TerrainAt(IntVec3 cell) => cell.InBounds(map) ? cell.GetTerrain(map) : null;
            foreach (IntVec3 cell in CellRect.CenteredOn(parent.Position, HabitatRadius).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing.Position != cell || !thing.def.useHitPoints
                        || thing.def.GetCompProperties<CompProperties_GuSpiritFlora>() is not { } species)
                        continue;
                    int maximum = thing.MaxHitPoints;
                    if (thing.HitPoints >= maximum || ecology.HabitatOwnerAt(cell) != this
                        || !GuFloraGrowth.SuitableGround(species, cell, TerrainAt))
                        continue;
                    // A surviving patch uses its existing native hit points for partial recovery.
                    thing.HitPoints = Math.Min(maximum, thing.HitPoints + Math.Max(1, maximum / 6));
                    return "One damaged flora patch recovered some health.";
                }
            }
            return "No suitable flora vacancy or recoverable damage.";
        }

        private IEnumerable<Gizmo> FloraGizmos()
        {
            if (UsesCavernFlora)
                yield return new Command_Action
                {
                    defaultLabel = "GU: Simulate flora growth step",
                    defaultDesc = "Run one normal regrowth step without changing the scheduled tick or advancing time.",
                    action = () => Messages.Message(CavernFloraStep(), MessageTypeDefOf.NeutralEvent, false)
                };
            yield return new Command_Action
            {
                defaultLabel = "GU: Habitat diagnostics",
                defaultDesc = "List physical flora credited to this spring, their support, and the Hope Gu capacity.",
                action = () =>
                {
                    RefreshHabitat();
                    var report = habitatBySpecies.OrderBy(entry => entry.Key.defName).Select(entry =>
                    {
                        var flora = entry.Key.GetCompProperties<CompProperties_GuSpiritFlora>();
                        long units = flora.HabitatUnits(entry.Value, UsesCavernFlora);
                        return $"{entry.Key.LabelCap}: {entry.Value} plants, {units} support units";
                    });
                    string detail = UsesCavernFlora
                        ? $"\n{rawFloraUnits} raw units, {Props.cavernSupportUnitsPerPoint} per support point, cap {Props.cavernMaxFloraSupport}."
                        : "";
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        string.Join("\n", report) + detail + $"\n{currentHabitatPatches} patches, {floraSupport} support, {carryingCapacity} Hope Gu capacity."));
                }
            };
        }
    }
}
