using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using ProjectGu.Domain;

namespace ProjectGu.Springs
{
    /// <summary>
    /// 0.2.4: saved finite reserve + population ecology. Existing 0.2.3 save keys
    /// stay readable; an explicit migration migrates the old Hope Gu charges once.
    /// No off-map regeneration, and the observable population is currently aggregated.
    /// </summary>
    public sealed class CompGuSpiritSpring : ThingComp
    {
        private int nextProductionTick = -1;
        private int lifetimeProduced;
        // Legacy 0.2.2/0.2.3 keys: do not rename or remove.
        private int hopeGuAvailable = 3;
        private int nextHopeRecoveryTick = -1;
        private int nextHabitatTick = -1;
        private int currentHabitatPatches;

        // New save-backed ecology state.
        private int reserve = -1;
        private int matureHopeGu = -1;
        private int juvenileHopeGu;
        private int carryingCapacity = 3;
        private int floraSupport;
        private int nextEcologyTick = -1;
        private const int EcologyDay = 60000;
        private const int SafetyRecount = 10000;
        private const int ReservePerStone = 1;
        private CompProperties_GuSpiritSpring Props => (CompProperties_GuSpiritSpring)props;
        public int HopeGuAvailable => Math.Max(0, matureHopeGu >= 0 ? matureHopeGu : hopeGuAvailable);
        public int HopeGuJuvenile => juvenileHopeGu;
        public int HabitatSupport => floraSupport;
        public int Reserve => reserve;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map?.GetComponent<GuEcologyMapComponent>()?.Register(this);
            EnsureInitialized();
            RefreshHabitat();
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            // RimWorld 1.7's ThingComp API does not expose PostDeSpawn(Map).
            // Remove the spring from the transient registry when it is destroyed.
            previousMap?.GetComponent<GuEcologyMapComponent>()?.Unregister(this);
            base.PostDestroy(mode, previousMap);
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Migration: 0.2.3 charged Hope Gu become mature Hope Gu, never rerolled.
                EnsureInitialized();
                juvenileHopeGu = Math.Max(0, juvenileHopeGu);
            }
        }
        private void EnsureInitialized()
        {
            if (reserve < 0) reserve = Math.Max(1, Props.initialReserve);
            if (matureHopeGu < 0) matureHopeGu = Math.Max(0, Math.Min(hopeGuAvailable, Props.maxHopeGu));
            if (nextEcologyTick < 0 && Find.TickManager != null)
            {
                int now = Find.TickManager.TicksGame;
                // Carry forward pre-existing daily recovery progress on migration.
                nextEcologyTick = nextHopeRecoveryTick > now ? nextHopeRecoveryTick : now + EcologyDay;
            }
        }
        public bool TryConsumeHopeGu()
        {
            EnsureInitialized();
            if (!parent.Spawned || matureHopeGu <= 0) return false;
            matureHopeGu--;
            hopeGuAvailable = matureHopeGu; // maintain legacy key for downgrade diagnostics
            return true;
        }
        public void NotifyNearbyFloraChanged(IntVec3 cell)
        {
            if (!parent.Spawned) return;
            int radius = Math.Max(1, Props.habitatRadius);
            if (Math.Abs(parent.Position.x-cell.x) > radius || Math.Abs(parent.Position.z-cell.z) > radius) return;
            RefreshHabitat();
        }
        private void RefreshHabitat()
        {
            if (!parent.Spawned || parent.Map == null) return;
            int radius = Math.Max(1, Props.habitatRadius);
            int count = 0;
            int support = 0;
            for (int dx=-radius; dx<=radius; dx++)
                for (int dz=-radius; dz<=radius; dz++)
                {
                    IntVec3 cell = new IntVec3(parent.Position.x+dx, 0, parent.Position.z+dz);
                    if (!cell.InBounds(parent.Map)) continue;
                    foreach (Thing t in parent.Map.thingGrid.ThingsListAt(cell))
                    {
                        CompGuSpiritFlora flora = t.TryGetComp<CompGuSpiritFlora>();
                        if (flora == null) continue;
                        count++;
                        support += flora.Support;
                    }
                }
            currentHabitatPatches = count;
            floraSupport = Math.Max(0, support);
            // Adults survive habitat removal; no unsupported reproduction occurs.
            carryingCapacity = Math.Max(3, Math.Min(Props.maxHopeGu, 3 + 2 * floraSupport));
            if (Find.TickManager != null) nextHabitatTick = Find.TickManager.TicksGame + SafetyRecount;
        }
        private void EcologyDayStep()
        {
            // Maturation happens before reproduction. No spontaneous births without flora.
            int space = Math.Max(0, carryingCapacity - matureHopeGu);
            int matured = Math.Min(space, juvenileHopeGu);
            matureHopeGu += matured;
            juvenileHopeGu -= matured;
            if (floraSupport > 0 && reserve > 0 && matureHopeGu + juvenileHopeGu < carryingCapacity)
            {
                // One juvenile per day is a conservative prototype; not the final biology.
                juvenileHopeGu++;
            }
            hopeGuAvailable = matureHopeGu;
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
            if (Prefs.DevMode && parent.Spawned)
                yield return new Command_Action
                {
                    defaultLabel = "GU: simulate ecology day",
                    defaultDesc = "Advance Hope Gu maturation and reproduction by one daily step, without advancing global time or generating stones. TEST ONLY.",
                    action = DebugSimulateEcologyDay
                };
        }
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned || parent.Map == null || Find.TickManager == null) return;
            EnsureInitialized();
            int now = Find.TickManager.TicksGame;
            if (nextHabitatTick < 0 || now >= nextHabitatTick) RefreshHabitat();
            if (nextEcologyTick < 0) nextEcologyTick = now + EcologyDay;
            if (now >= nextEcologyTick)
            {
                // Bounded single update. Never generate thousands of catch-up events.
                EcologyDayStep();
                nextEcologyTick = now + EcologyDay;
                nextHopeRecoveryTick = nextEcologyTick;
            }
            int interval = Math.Max(250, Props.productionIntervalTicks);
            if (nextProductionTick < 0) { nextProductionTick = now + interval; return; }
            if (now < nextProductionTick) return;
            nextProductionTick = now + interval;
            if (reserve < ReservePerStone) return;
            ThingDef stoneDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props.stoneDefName);
            if (stoneDef == null) return;
            int count = Math.Max(1, Math.Min(Props.stonesPerCycle, Math.Min(stoneDef.stackLimit, reserve / ReservePerStone)));
            Thing stone = ThingMaker.MakeThing(stoneDef);
            stone.stackCount = count;
            if (GenPlace.TryPlaceThing(stone, parent.Position, parent.Map, ThingPlaceMode.Near))
            {
                lifetimeProduced += count;
                reserve -= count * ReservePerStone;
            }
            else if (!stone.Destroyed) stone.Destroy();
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
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            string next = nextProductionTick < 0 ? "initializing" : Math.Max(0, nextProductionTick-now) + " ticks";
            return "Spring profile: " + Props.regionProfile + " (prototype preset)" +
                "\nPrimeval reserve: " + Math.Max(0, reserve) + " / " + Props.initialReserve +
                "\nPurity: " + Props.purityPercent + "%" +
                "\nPrimeval stones produced: " + lifetimeProduced + "; next: " + next +
                "\nSpirit flora: " + currentHabitatPatches + " patches (support " + floraSupport + ")" +
                "\nHope Gu: " + HopeGuAvailable + " mature + " + juvenileHopeGu +
                " juvenile / " + carryingCapacity + " capacity" +
                (floraSupport > 0 ? "\nHabitat supports Hope Gu reproduction" : "\nNo habitat: reproduction suspended");
        }
    }
}
