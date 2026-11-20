using System;
using System.Collections.Generic;
using ProjectGu.Underground;
using RimWorld;
using Verse;
using Verse.AI;

namespace ProjectGu.Springs
{
    public sealed partial class CompGuSpiritSpring
    {
        private bool producing;
        private string lastProductionAttempt;
        private string ProductionDue => nextProductionTick < 0 ? "initializing"
            : $"{Math.Max(0, nextProductionTick - (Current.Game?.tickManager?.TicksGame ?? 0))} ticks";

        private IEnumerable<Gizmo> ProductionGizmos()
        {
            yield return new Command_Action
            {
                defaultLabel = "GU: Produce stone now",
                defaultDesc = "Attempt one normal production batch without advancing the scheduled timer.",
                action = () => Messages.Message(AttemptProduction(), MessageTypeDefOf.NeutralEvent, false)
            };
            yield return new Command_Action
            {
                defaultLabel = "GU: Show output cells",
                defaultDesc = "Show ready, blocked and inaccessible output positions for this spring.",
                action = ShowOutputCells
            };
            yield return new Command_Action
            {
                defaultLabel = "GU: Production diagnostics",
                defaultDesc = "Report the last attempt, its accounting and the next scheduled production.",
                action = () => Find.WindowStack.Add(new Dialog_MessageBox(
                    (lastProductionAttempt ?? $"No attempt this session. Reserve {reserve}, lifetime {lifetimeProduced}.")
                    + $"\nNext scheduled production now: {ProductionDue}."))
            };
        }

        private string AttemptProduction()
        {
            if (producing)
                return "Production is already in progress.";

            producing = true;
            int beforeReserve = reserve;
            int beforeLifetime = lifetimeProduced;
            try
            {
                string outcome = ProduceBatch(out IntVec3 output);
                lastProductionAttempt = $"{outcome}\nOutput position: {(output.IsValid ? output.ToString() : "none")}"
                    + $"\nReserve: {beforeReserve} → {reserve}\nLifetime production: {beforeLifetime} → {lifetimeProduced}"
                    + $"\nNext scheduled production at attempt: {ProductionDue}.";
                return outcome;
            }
            finally
            {
                producing = false;
            }
        }

        private string ProduceBatch(out IntVec3 output)
        {
            output = IntVec3.Invalid;
            if (!parent.Spawned || parent.Map == null)
                return "Production failed: the spring has no live map.";
            if (reserve < ReservePerStone)
                return "Production failed: insufficient reserve.";
            ThingDef stoneDef = StoneDef();
            if (stoneDef == null)
                return "Production failed: missing or invalid primeval stone Def.";
            int requested = BatchSize(stoneDef);
            if (lifetimeProduced > int.MaxValue - requested)
                return "Production failed: lifetime counter is full.";

            Map map = parent.Map;
            Thing stone = ThingMaker.MakeThing(stoneDef);
            stone.stackCount = requested;
            bool cavern = GuPocketSiteDef.IsSpringCavern(map);
            int placed = 0;
            int blocked = 0;
            int inaccessible = 0;
            int wrongTerrain = 0;
            int refused = 0;
            IntVec3 position = IntVec3.Invalid;
            void RecordPlacement(Thing physical, int count)
            {
                if (physical?.Spawned != true || physical.Map != map || physical.def != stoneDef || count <= 0)
                    return;
                position = physical.Position;
                placed += count;
            }

            try
            {
                if (cavern)
                {
                    if (!GuPocketProbeComponent.TryGetCavernArrival(map, out IntVec3 arrival))
                        return "Production failed: there is no live passage to the basin.";
                    foreach (var candidate in CavernOutputs())
                    {
                        switch (CavernOutputStatus(map, stoneDef, arrival, candidate.Cell, candidate.Water))
                        {
                            case OutputStatus.Ready:
                                GenPlace.TryPlaceThing(stone, candidate.Cell, map, ThingPlaceMode.Direct, RecordPlacement);
                                if (placed == 0) refused++;
                                break;
                            case OutputStatus.Blocked: blocked++; break;
                            case OutputStatus.Inaccessible: inaccessible++; break;
                            case OutputStatus.WrongTerrain: wrongTerrain++; break;
                        }
                        if (placed > 0 || stone.Destroyed || stone.Spawned)
                            break;
                    }
                }
                else
                {
                    // Keep native Nearby ranking and stacking, with enough space for the entire batch.
                    GenPlace.TryPlaceThing(stone, parent.Position, map, ThingPlaceMode.Near, RecordPlacement,
                        cell => SurfaceOutputStatus(map, cell, stone) == OutputStatus.Ready);
                }
            }
            finally
            {
                // The native callback acknowledges the exact quantity spawned or absorbed into a live stack.
                if (placed > 0)
                {
                    reserve -= placed * ReservePerStone;
                    lifetimeProduced += placed;
                }
                if (!stone.Spawned && !stone.Destroyed)
                    stone.Destroy();
            }

            if (placed == 0)
                return cavern
                    ? $"Production failed: {blocked} blocked, {inaccessible} inaccessible, "
                        + $"{wrongTerrain} wrong terrain, {refused} refused by native placement."
                    : "Production failed: native nearby placement found no reachable cell for the full batch.";
            output = position;
            if (cavern && Find.CurrentMap == map && position.GetTerrain(map).IsWater)
                FleckMaker.WaterSplash(position.ToVector3Shifted(), map, 0.65f, 1.5f);

            return placed == requested
                ? $"Produced {placed} primeval stone(s) at {position}."
                : $"Placed {placed} of {requested} primeval stones at {position}. Only placed stones were debited.";
        }

        private ThingDef StoneDef()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(Props.stoneDefName);
            return def?.category == ThingCategory.Item && def.stackLimit > 0 ? def : null;
        }

        private int BatchSize(ThingDef stoneDef) =>
            Math.Max(1, Math.Min(Props.stonesPerCycle, Math.Min(stoneDef.stackLimit, reserve / ReservePerStone)));

        internal static IEnumerable<IntVec3> BasinWaterOutputs(CellRect footprint)
        {
            for (int z = footprint.minZ; z <= footprint.maxZ; z++)
                yield return new IntVec3(footprint.maxX + 2, 0, z);
        }

        private IEnumerable<(IntVec3 Cell, bool Water)> CavernOutputs()
        {
            CellRect footprint = parent.OccupiedRect();
            foreach (IntVec3 cell in BasinWaterOutputs(footprint))
                yield return (cell, true);
            for (int x = footprint.minX - 1; x <= footprint.maxX + 1; x++)
                yield return (new IntVec3(x, 0, footprint.minZ - 1), false);
        }

        private enum OutputStatus { Ready, Blocked, Inaccessible, WrongTerrain }

        private static OutputStatus CavernOutputStatus(Map map, ThingDef stone, IntVec3 arrival, IntVec3 cell, bool water)
        {
            if (!cell.InBounds(map))
                return OutputStatus.Inaccessible;
            TerrainDef terrain = cell.GetTerrain(map);
            if (water ? terrain.defName != "WaterShallow" : terrain.IsWater)
                return OutputStatus.WrongTerrain;
            if (!cell.Standable(map) || !GenSpawn.CanSpawnAt(stone, cell, map) || cell.GetMaxItemsAllowedInCell(map) < 1)
                return OutputStatus.Blocked;
            foreach (Thing thing in cell.GetThingList(map))
                if (thing.def.category is ThingCategory.Pawn or ThingCategory.Item or ThingCategory.Building
                    || thing is Blueprint or Frame or Fire)
                    return OutputStatus.Blocked;
            return arrival.IsValid && !cell.Fogged(map)
                && map.reachability.CanReach(arrival, cell, PathEndMode.OnCell, TraverseMode.PassDoors)
                    ? OutputStatus.Ready : OutputStatus.Inaccessible;
        }

        private OutputStatus SurfaceOutputStatus(Map map, IntVec3 cell, Thing stone)
        {
            if (!cell.InBounds(map) || !GenSpawn.CanSpawnAt(stone.def, cell, map))
                return OutputStatus.Blocked;
            bool fullBatchMerge = false;
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing is Fire)
                    return OutputStatus.Blocked;
                if (thing.def.category != ThingCategory.Item || !thing.CanStackWith(stone))
                    continue;
                int space = thing.def.stackLimit - thing.stackCount;
                if (space > 0 && space < stone.stackCount)
                    return OutputStatus.Blocked;
                fullBatchMerge |= space >= stone.stackCount;
            }
            if (!fullBatchMerge && cell.GetItemCount(map) >= cell.GetMaxItemsAllowedInCell(map))
                return OutputStatus.Blocked;
            return !cell.Fogged(map)
                && map.reachability.CanReach(parent.Position, cell, PathEndMode.OnCell, TraverseMode.PassDoors)
                    ? OutputStatus.Ready : OutputStatus.Inaccessible;
        }

        private void ShowOutputCells()
        {
            if (!parent.Spawned || StoneDef() is not ThingDef def)
                return;
            Map map = parent.Map;
            if (GuPocketSiteDef.IsSpringCavern(map))
            {
                GuPocketProbeComponent.TryGetCavernArrival(map, out IntVec3 arrival);
                foreach (var candidate in CavernOutputs())
                    if (candidate.Cell.InBounds(map))
                        FlashOutput(map, candidate.Cell, CavernOutputStatus(map, def, arrival, candidate.Cell, candidate.Water),
                            candidate.Water ? "Water" : "Shore");
                Messages.Message("The spring tries ready water outputs first, then the dry shore.", MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            Thing sample = ThingMaker.MakeThing(def);
            sample.stackCount = BatchSize(def);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, 6f, true))
                if (cell.InBounds(map))
                    FlashOutput(map, cell, SurfaceOutputStatus(map, cell, sample), "Nearby");
            sample.Destroy();
            Messages.Message("Preview of nearby surface cells. Native placement ranks candidates and can search farther.",
                MessageTypeDefOf.NeutralEvent, false);
        }

        private static void FlashOutput(Map map, IntVec3 cell, OutputStatus status, string label)
        {
            float color = status == OutputStatus.Ready ? 0.35f : status == OutputStatus.Blocked ? 0f : 0.13f;
            string detail = status == OutputStatus.WrongTerrain ? "wrong terrain" : status.ToString().ToLowerInvariant();
            map.debugDrawer.FlashCell(cell, color, $"{label}: {detail}", 420);
        }
    }
}
