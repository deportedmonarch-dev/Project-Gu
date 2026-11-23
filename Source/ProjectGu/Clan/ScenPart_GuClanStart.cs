using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using ProjectGu.Domain;
using ProjectGu.Underground;

namespace ProjectGu.Clan
{
    /// <summary>
    /// M1 Southern Border clan founding. Assigns clan roles to the starting colonists, opens the
    /// clan's spirit spring pit near the start, and awakens the elder through the ordinary
    /// awakening service once the cultivator registry is ready.
    ///
    /// The part never creates apertures or cultivator records itself. It only sets the hidden
    /// latent aptitude of still-unattempted records (the scenario decides who was born gifted)
    /// and then calls <see cref="GuAwakeningService"/>, which owns the state transition.
    /// </summary>
    public sealed class ScenPart_GuClanStart : ScenPart
    {
        // XML configuration.
        public IntRange elderAptitude = new(60, 74);
        public int discipleCount = 2;
        public bool disciplesHaveAptitude = true;
        public int discipleMinAge = 13;
        public bool placeSpringEntrance = true;
        public float entranceMinDistance = 7f;
        public float entranceMaxDistance = 28f;

        // Saved progress. The founding completes once, on the first tick the registry accepts it.
        private bool foundingPending;
        private bool foundingDone;
        private int nextAttemptTick;
        private int failedAttempts;

        private const int RetryInterval = 60;
        private const int MaxAttempts = 600; // About ten in-game minutes of waiting for the registry.

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref elderAptitude, "elderAptitude", new IntRange(60, 74));
            Scribe_Values.Look(ref discipleCount, "discipleCount", 2);
            Scribe_Values.Look(ref disciplesHaveAptitude, "disciplesHaveAptitude", true);
            Scribe_Values.Look(ref discipleMinAge, "discipleMinAge", 13);
            Scribe_Values.Look(ref placeSpringEntrance, "placeSpringEntrance", true);
            Scribe_Values.Look(ref entranceMinDistance, "entranceMinDistance", 7f);
            Scribe_Values.Look(ref entranceMaxDistance, "entranceMaxDistance", 28f);
            Scribe_Values.Look(ref foundingPending, "guFoundingPending");
            Scribe_Values.Look(ref foundingDone, "guFoundingDone");
            Scribe_Values.Look(ref failedAttempts, "guFoundingAttempts");
        }

        public override string Summary(Scenario scen) =>
            $"One colonist begins as an awakened clan elder. Up to {discipleCount} young colonists are prospective disciples"
            + (disciplesHaveAptitude ? " born with cultivation aptitude" : "")
            + ". The rest are mortal workers. A spirit spring pit opens near the start.";

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (elderAptitude.min < 20 || elderAptitude.max > 99 || elderAptitude.min > elderAptitude.max)
                yield return "elderAptitude must be an ordinary aptitude range within 20~99.";
            if (discipleCount < 0) yield return "discipleCount cannot be negative.";
            if (entranceMinDistance < 0f || entranceMaxDistance <= entranceMinDistance || entranceMaxDistance > 50f)
                yield return "entrance distances must satisfy 0 <= min < max <= 50.";
        }

        // New games run on the ScenarioDef's own Scenario instance (Page_SelectScenario does not copy it),
        // so progress from an earlier game in this session must not carry over.
        public override void PreConfigure()
        {
            base.PreConfigure();
            ResetProgress();
        }

        private void ResetProgress()
        {
            foundingPending = foundingDone = false;
            nextAttemptTick = failedAttempts = 0;
        }

        public override void PostGameStart()
        {
            ResetProgress();
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                Log.Warning("[Project GU] Clan start found no player home map. Founding skipped.");
                return;
            }
            GuClanRoles.Assign(map.mapPawns.FreeColonistsSpawned.ToList(), discipleCount, discipleMinAge);
            if (placeSpringEntrance) TryPlaceSpringEntrance(map);
            foundingPending = true;
        }

        public override void Tick()
        {
            if (!foundingPending || foundingDone) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextAttemptTick) return;
            nextAttemptTick = now + RetryInterval;

            var world = Current.Game?.GetComponent<GuWorldComponent>();
            if (world == null || !world.CanSaveCultivators)
            {
                if (++failedAttempts >= MaxAttempts) Abandon("The cultivator registry never became available.");
                return;
            }
            CompleteFounding(world);
        }

        private void CompleteFounding(GuWorldComponent world)
        {
            foundingPending = false;
            foundingDone = true;
            var colonists = PawnsFinder.AllMaps_FreeColonistsSpawned.ToList();
            Pawn elder = colonists.FirstOrDefault(p => GuClanRoles.RoleOf(p) == GuClanRole.Elder);
            var disciples = colonists.Where(p => GuClanRoles.RoleOf(p) == GuClanRole.Disciple).ToList();
            var workers = colonists.Where(p => GuClanRoles.RoleOf(p) == GuClanRole.MortalWorker).ToList();

            var letter = new StringBuilder();
            letter.AppendLine("Your clan has claimed a spirit spring on the Southern Border.");
            letter.AppendLine();

            if (elder != null)
            {
                GuCultivatorRecord record = world.GetOrCreate(elder);
                if (record != null && record.State == AwakeningState.Unattempted)
                    record.LatentAptitude = AptitudeRules.ClampOrdinary(elderAptitude.RandomInRange);
                if (record != null && record.State == AwakeningState.Unattempted
                    && GuAwakeningService.TryAwaken(world, elder, out string outcome))
                    letter.AppendLine($"Elder {elder.LabelShort}: {outcome}");
                else
                {
                    string state = record == null ? "unavailable" : record.State.ToString();
                    Log.Warning($"[Project GU] Clan elder {elder} could not be awakened at founding (record {state}).");
                    letter.AppendLine($"Elder {elder.LabelShort} could not be awakened. See the game log.");
                }
            }

            foreach (Pawn disciple in disciples)
            {
                GuCultivatorRecord record = world.GetOrCreate(disciple);
                if (record == null) continue;
                if (disciplesHaveAptitude && record.State == AwakeningState.Unattempted && record.LatentAptitude == 0)
                    record.LatentAptitude = GuClanRoles.GiftedAptitude();
            }
            foreach (Pawn worker in workers) world.GetOrCreate(worker);

            if (disciples.Count > 0)
                letter.AppendLine("Disciples: " + string.Join(", ", disciples.Select(p => p.LabelShort))
                    + ". Each was born with aptitude, but its grade stays hidden until awakening. "
                    + "Take them down the spirit spring pit and order them to attempt awakening at the spring. "
                    + "Every attempt consumes one mature Hope Gu.");
            if (workers.Count > 0)
                letter.AppendLine("Mortal workers: " + string.Join(", ", workers.Select(p => p.LabelShort))
                    + ". They farm, build and haul. A mortal may still try awakening, but most have no aptitude.");
            letter.AppendLine();
            letter.Append("The spring produces primeval stones each day. Assign a hauler to the pit to bring them up, "
                + "and trade surplus stones with passing merchants.");

            Find.LetterStack.ReceiveLetter("Clan founded", letter.ToString().TrimEnd(), LetterDefOf.PositiveEvent,
                elder != null ? new LookTargets(elder) : LookTargets.Invalid);
        }

        private void Abandon(string reason)
        {
            foundingPending = false;
            foundingDone = true;
            Log.Warning($"[Project GU] Clan founding abandoned. {reason}");
        }

        private void TryPlaceSpringEntrance(Map map)
        {
            GuPocketSiteDef kind = GuPocketSiteDef.SpiritSpring;
            var colonists = map.mapPawns.FreeColonistsSpawned.ToList();
            if (kind == null || colonists.Count == 0)
            {
                Log.Warning("[Project GU] Clan start could not place a spirit spring pit (missing site Def or colonist).");
                return;
            }
            // Measure from the whole starting group so the pit stays clear of every colonist's arrival area.
            IntVec3 center = new((int)System.Math.Round(colonists.Average(p => p.Position.x)), 0,
                (int)System.Math.Round(colonists.Average(p => p.Position.z)));
            Pawn anchor = colonists.MinBy(p => p.Position.DistanceToSquared(center));
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, entranceMaxDistance, false))
            {
                if (colonists.Any(p => p.Position.DistanceTo(cell) < entranceMinDistance) || !cell.InBounds(map) || cell.Roofed(map)
                    || !Building_GuPocketProbeEndpoint.CanPlaceEntrance(kind, map, cell)
                    || !anchor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    continue;
                if (Building_GuPocketProbeEndpoint.TrySpawnEntrance(kind, map, cell) != null) return;
            }
            Log.Warning("[Project GU] Clan start found no clear ground for the spirit spring pit. "
                + "Use the Development Mode placement tool to add one.");
            Messages.Message("No clear ground was found for the clan's spirit spring pit.", MessageTypeDefOf.NegativeEvent, false);
        }
    }
}
