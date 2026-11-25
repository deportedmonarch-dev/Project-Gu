using System.Collections.Generic;
using ProjectGu.Domain;
using RimWorld;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Legacy-compatible Thing class for the developer dormant Gu. Saved V1 Things bind to this class name, so it stays
    /// loadable. Identity lives in <see cref="CompGuManifestation"/>.
    /// </summary>
    public sealed class Thing_GuDormantProjection : ThingWithComps, IGuProjection
    {
        public string GuId => this.GuTag()?.GuId;
        public int GuRevision => this.GuTag()?.GuRevision ?? 0;

        internal static bool IsConfigured(ThingDef def) => def?.thingClass == typeof(Thing_GuDormantProjection)
            && def.HasComp(typeof(CompGuManifestation)) && def.notifyMapRemoved
            && def.category == ThingCategory.Item && def.stackLimit == 1 && def.tickerType == TickerType.Never
            && !def.IsIngestible && !def.destroyOnDrop && def.tradeability == Tradeability.None;

        internal void InitializeBinding(GuRecord record) => this.GuTag()?.Bind(record);

        public override bool CanStackWith(Thing other) => false;

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            this.GuTag()?.CaptureEndingLocation();
            base.Destroy(mode);
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var option in base.GetFloatMenuOptions(selPawn)) yield return option;
            var registry = Current.Game?.GetComponent<GuIndividualRegistry>();
            // Any colonist who can reach a dormant Gu on the ground can feed it; the food is consumed only when it is given.
            if (registry?.Resolve(this) is { } record && Interface.GuApertureActions.Feed(selPawn, registry, record) is { } feed)
                yield return feed.Available
                    ? new FloatMenuOption($"Feed {Label}", () => Interface.GuApertureActions.Run(feed))
                    : new FloatMenuOption($"Feed {Label} ({feed.DisabledReason})", null);
            if (registry?.CanOrderDormantPickup(this, selPawn) != true
                || DefDatabase<JobDef>.GetNamedSilentFail("GU_PickupDormant") is not { } jobDef) yield break;

            yield return new FloatMenuOption($"Pick up {Label}", () =>
            {
                if (registry.CanOrderDormantPickup(this, selPawn))
                    selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, this));
            });
        }

        public override string GetInspectString() => GuIndividualRegistry.InspectProjection(this, base.GetInspectString());
    }
}
