using System;
using System.Collections.Generic;
using System.Linq;
using ProjectGu.Combat;
using ProjectGu.Domain;
using ProjectGu.Jobs;
using RimWorld;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>How much of an aperture the viewer may see.</summary>
    public enum ApertureDisclosure
    {
        /// <summary>The player's own cultivator, prisoner or slave: everything the game knows.</summary>
        Full,
        /// <summary>Someone else's cultivator: only the outwardly perceptible rank and stage. No aptitude, essence or Gu.</summary>
        Observed
    }

    /// <summary>One Gu shown on the Gu page, with the actions currently available for it.</summary>
    public sealed class ApertureGuEntry
    {
        public GuRecord Record;
        public Thing_GuDormantProjection Thing;
        public GuApertureGuPlacement Placement;
        public bool RecognizesHost;
        public string OwnerName;
        public readonly List<GuApertureAction> Actions = new();
        /// <summary>Nourishment at the moment of the refresh. Read-only.</summary>
        public GuNourishmentSnapshot Nourishment;
        /// <summary>Feeding, kept apart from the custody actions so the Gu page can draw it beside the meter.</summary>
        public GuApertureAction FeedAction;
        /// <summary>Accepted foods and how many of each this cultivator could use now.</summary>
        public readonly List<(GuFoodOption Option, int Available)> Foods = new();
    }

    public enum GuApertureGuPlacement { Aperture, Inventory, Nearby }

    /// <summary>
    /// A read-only picture of one cultivator's aperture, rebuilt from the authoritative aperture, registry, ledger and
    /// job state a few times a second while the tab is open, and immediately after every action. It never stores
    /// cultivation or custody values of its own between refreshes and is never saved.
    /// </summary>
    public sealed class ApertureView
    {
        private const float RefreshSeconds = 0.25f;

        public Pawn Pawn { get; private set; }
        public Hediff_Aperture Aperture { get; private set; }
        public ApertureDisclosure Disclosure { get; private set; }
        public bool CanManage { get; private set; }
        public GuApertureRankDef RankDef { get; private set; }
        public int Rank { get; private set; }
        public int Stage { get; private set; }
        public readonly double[] Amounts = new double[PrimevalSeaLayout.Qualities];
        public double Total { get; private set; }
        public double Capacity { get; private set; }
        public double WallNourishment { get; private set; }
        public double WallRequired { get; private set; }
        public bool CanAdvance { get; private set; }
        public GuApertureActivity Activity { get; private set; }
        public GuApertureVitality Vitality { get; private set; }
        public float CrackSeverity { get; private set; }
        public string ActivityLabel { get; private set; }
        public int Claims { get; private set; }
        public int ClaimCapacity => GuIndividualRegistry.MortalApertureCapacity;
        public GuChannelSession ActiveChannel { get; private set; }
        public GuRefinementProcess ActiveRefinement { get; private set; }
        /// <summary>Whether this cultivator feeds their own hungry Gu automatically (player policy).</summary>
        public bool AutoTend { get; private set; }

        public GuApertureAction NourishAction { get; private set; }
        public GuApertureAction AdvanceAction { get; private set; }
        public readonly List<GuApertureAction> ChannelActions = new();
        public readonly List<ApertureGuEntry> Gu = new();
        /// <summary>Gu whose techniques this cultivator can use or will be able to use: refined by them and in their keeping.</summary>
        public readonly List<GuRecord> KnownGu = new();

        private float refreshedAt = -10f;
        private readonly List<Thing_GuDormantProjection> held = new();
        private readonly List<Thing_GuDormantProjection> nearby = new();

        /// <summary>Increments on every refresh, so callers can cache text derived from the snapshot.</summary>
        public int Version { get; private set; }
        public bool Valid => Pawn != null && Aperture != null;
        public bool AtPeak => Rank == 1 && Stage == (int)RankOneStage.Peak;

        public void Invalidate() => refreshedAt = -10f;

        /// <summary>Refreshes when the pawn changed, after an action, or every quarter second.</summary>
        public void RefreshIfNeeded(Pawn pawn)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (pawn == Pawn && now - refreshedAt < RefreshSeconds && (Aperture == null || Aperture.pawn == pawn)) return;
            Refresh(pawn);
            refreshedAt = now;
        }

        public void Refresh(Pawn pawn)
        {
            Version++;
            Pawn = pawn;
            Aperture = GuApertureActions.FindAperture(pawn);
            Gu.Clear();
            KnownGu.Clear();
            ChannelActions.Clear();
            NourishAction = AdvanceAction = null;
            AutoTend = false;
            ActiveChannel = null;
            ActiveRefinement = null;
            Activity = GuApertureActivity.None;
            ActivityLabel = null;
            Array.Clear(Amounts, 0, Amounts.Length);
            if (Aperture == null) return;

            Disclosure = DisclosureFor(pawn);
            CanManage = Disclosure == ApertureDisclosure.Full && GuApertureActions.CanManage(pawn, Aperture);
            Rank = Aperture.Rank;
            Stage = (int)Aperture.Stage;
            RankDef = GuApertureRankDef.ForRank(Rank);
            Vitality = GuApertureConditions.Resolve(Aperture, out float severity);
            CrackSeverity = severity;
            for (int q = 0; q < Amounts.Length; q++) Amounts[q] = Aperture.EssenceOfQuality((RankOneStage)q);
            Total = Amounts.Sum();
            Capacity = Aperture.MaxEssence;
            WallNourishment = Aperture.WallNourishment;
            WallRequired = Aperture.WallNourishmentRequired;
            CanAdvance = Aperture.CanAdvanceRankOne;
            ReadActivity(pawn);

            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            if (Disclosure != ApertureDisclosure.Full || registry == null) return;
            Claims = registry.ApertureClaimCount(Aperture);
            if (CanManage && Vitality != GuApertureVitality.Dead)
            {
                NourishAction = GuApertureActions.Nourish(pawn, Aperture);
                AdvanceAction = GuApertureActions.Advance(pawn, Aperture);
                ChannelActions.AddRange(GuApertureActions.CultivationChannels(pawn, Aperture));
            }
            CollectGu(pawn, registry);
            AutoTend = CanManage && Sustenance.GuSustenanceComponent.Instance?.AutoTendEnabled(pawn) == true;
        }

        private void ReadActivity(Pawn pawn)
        {
            var driver = pawn.jobs?.curDriver;
            if (driver == null) return;
            if (driver is JobDriver_GuNourishAperture)
            {
                Activity |= GuApertureActivity.NourishingWalls;
                ActivityLabel = "Nourishing the aperture walls";
            }
            else if (driver is JobDriver_GuChannelAbility channel && GuCombatLedger.Instance?.ChannelById(channel.SessionId) is { Active: true } session)
            {
                ActiveChannel = session;
                if (session.Ability?.channel?.Worker(session.Ability).RefinesEssence == true) Activity |= GuApertureActivity.EssenceRefinement;
                ActivityLabel = $"Channeling {session.Ability?.label} ({session.PulsesDone}/{session.PulseCount} pulses)";
            }
            else if (driver is JobDriver_GuRefineMortalGu && driver.job?.targetA.Thing is Thing_GuDormantProjection thing
                && Current.Game?.GetComponent<GuIndividualRegistry>()?.Resolve(thing) is { RefinementProcess: { } process } record)
            {
                ActiveRefinement = process;
                Activity |= GuApertureActivity.WillEnforcement;
                ActivityLabel = $"Enforcing will over {record.Species?.label ?? record.SpeciesDefName} ({100f * process.CompletedWorkTicks / Math.Max(1, process.TotalWorkTicks):0}%)";
            }
        }

        private void CollectGu(Pawn pawn, GuIndividualRegistry registry)
        {
            foreach (var record in registry.ApertureOccupants(Aperture).OrderBy(record => record.Id, StringComparer.Ordinal))
            {
                var entry = Entry(record, null, GuApertureGuPlacement.Aperture, pawn, registry);
                if (CanManage && GuApertureActions.Withdraw(pawn, Aperture, registry, record) is { } withdraw) entry.Actions.Add(withdraw);
                Gu.Add(entry);
            }
            GuApertureActions.CollectDormant(pawn, held, CanManage ? nearby : null);
            foreach (var thing in held) AddDormant(thing, GuApertureGuPlacement.Inventory, pawn, registry);
            foreach (var thing in nearby) if (!held.Contains(thing)) AddDormant(thing, GuApertureGuPlacement.Nearby, pawn, registry);
            foreach (var entry in Gu)
                if (entry.RecognizesHost && entry.Placement != GuApertureGuPlacement.Nearby) KnownGu.Add(entry.Record);
        }

        private void AddDormant(Thing_GuDormantProjection thing, GuApertureGuPlacement placement, Pawn pawn, GuIndividualRegistry registry)
        {
            if (registry.Resolve(thing) is not { } record) return;
            var entry = Entry(record, thing, placement, pawn, registry);
            if (CanManage)
            {
                if (placement == GuApertureGuPlacement.Inventory && GuApertureActions.Store(pawn, Aperture, registry, thing) is { } store) entry.Actions.Add(store);
                if (GuApertureActions.EnforceWill(pawn, registry, thing) is { } enforce) entry.Actions.Add(enforce);
                if (GuApertureActions.AbandonWill(pawn, registry, thing) is { } abandon) entry.Actions.Add(abandon);
            }
            // Ground Gu are listed only when this cultivator can act on them.
            if (placement == GuApertureGuPlacement.Nearby && entry.Actions.Count == 0) return;
            Gu.Add(entry);
        }

        private ApertureGuEntry Entry(GuRecord record, Thing_GuDormantProjection thing, GuApertureGuPlacement placement, Pawn pawn, GuIndividualRegistry registry)
        {
            string ownerId = record.Refinement?.CultivatorId;
            var entry = new ApertureGuEntry
            {
                Record = record,
                Thing = thing,
                Placement = placement,
                RecognizesHost = registry.IsRecognizedByCultivator(record, pawn),
                OwnerName = string.IsNullOrEmpty(ownerId) ? null
                    : Current.Game?.GetComponent<GuWorldComponent>()?.FindCultivator(ownerId)?.CachedName ?? "another cultivator",
                Nourishment = Sustenance.GuSustenance.Snapshot(registry, record)
            };
            if (record.Species?.SimulatedFeeding is { } props)
            {
                foreach (var option in props.foods)
                    if (option?.thingDef != null) entry.Foods.Add((option, CanManage ? props.Worker?.CountAvailable(pawn, option) ?? 0 : 0));
                if (CanManage) entry.FeedAction = GuApertureActions.Feed(pawn, registry, record);
            }
            return entry;
        }

        /// <summary>Full detail for the player's own people; outward rank and stage only for anyone else.</summary>
        public static ApertureDisclosure DisclosureFor(Pawn pawn) =>
            pawn != null && (pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer)
                ? ApertureDisclosure.Full : ApertureDisclosure.Observed;

        public ApertureOrbInput OrbInput(GuApertureVisualState? preview = null) => new()
        {
            Rank = RankDef,
            Stage = Stage,
            Amounts = Disclosure == ApertureDisclosure.Full ? Amounts : null,
            Capacity = Capacity,
            WallArc = Disclosure == ApertureDisclosure.Full ? (AtPeak ? 1f : PrimevalSeaLayout.WallArc(WallNourishment, WallRequired)) : 0f,
            Visual = preview ?? new GuApertureVisualState(Vitality, CrackSeverity, Activity)
        };
    }
}
