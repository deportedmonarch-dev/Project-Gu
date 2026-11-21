using System.Collections.Generic;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu
{
    internal interface IGuProjection
    {
        string GuId { get; }
        int GuRevision { get; }
    }

    public sealed class Thing_GuDormantProjection : ThingWithComps, IGuProjection
    {
        private string guId;
        private int guRevision;

        public string GuId => guId;
        public int GuRevision => guRevision;

        internal static bool IsConfigured(ThingDef def) => def?.thingClass == typeof(Thing_GuDormantProjection)
            && def.category == ThingCategory.Item && def.stackLimit == 1 && def.tickerType == TickerType.Never
            && !def.EverHaulable && !def.IsIngestible && !def.destroyOnDrop && def.tradeability == Tradeability.None;

        internal void InitializeBinding(GuIndividual individual)
        {
            guId = individual.Id;
            guRevision = individual.Revision;
        }

        public override bool CanStackWith(Thing other) => false;

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var option in base.GetFloatMenuOptions(selPawn)) yield return option;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            int revision = GuRevision;
            if (registry?.CanOrderDormantPickup(this, revision, selPawn) != true
                || DefDatabase<JobDef>.GetNamedSilentFail("GU_PickupDormant") is not { } jobDef) yield break;

            yield return new FloatMenuOption($"Pick up {Label}", () =>
            {
                if (registry.CanOrderDormantPickup(this, revision, selPawn))
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, this));
            });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref guId, "guIndividualId");
            Scribe_Values.Look(ref guRevision, "guBindingRevision");
        }

        public override string GetInspectString() => GuIndividualRegistry.InspectProjection(this, base.GetInspectString());
    }
}
