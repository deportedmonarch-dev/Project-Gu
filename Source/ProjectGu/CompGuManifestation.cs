using System.Collections.Generic;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public sealed class CompProperties_GuManifestation : CompProperties
    {
        public CompProperties_GuManifestation() => compClass = typeof(CompGuManifestation);
    }

    /// <summary>
    /// Identity tag of a native Gu manifestation (RFC-0004 §5.1). It saves the V1 projection keys, so legacy
    /// projections migrate in place. A tag never authorizes itself. The registry decides which tagged object is
    /// the authorized manifestation.
    /// </summary>
    public sealed class CompGuManifestation : ThingComp
    {
        // Filled during save loading and consumed by the registry in LoadedGame. Cleared when a load starts.
        internal static readonly List<CompGuManifestation> LoadBuffer = new();

        private string guId;
        private int guRevision;

        public string GuId => guId;
        public int GuRevision => guRevision;

        /// <summary>Transient. Set by the registry when this tag claims an ID without being the authorized manifestation.</summary>
        internal bool Rogue { get; set; }

        internal void Bind(GuRecord record)
        {
            guId = record.Id;
            guRevision = record.CustodyRevision;
            Rogue = false;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref guId, "guIndividualId");
            Scribe_Values.Look(ref guRevision, "guBindingRevision");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !string.IsNullOrEmpty(guId)) LoadBuffer.Add(this);
        }

        // Native lifecycle hooks only enqueue. The registry applies events after the native callback returns.
        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null) => Report(GuTerminalEvidence.Killed);

        public override void PostDestroy(DestroyMode mode, Map previousMap) =>
            Report(mode == DestroyMode.KillFinalize ? GuTerminalEvidence.Killed : GuTerminalEvidence.Destroyed);

        public override void Notify_MapRemoved() => Report(GuTerminalEvidence.MapRemoved);

        private void Report(GuTerminalEvidence evidence)
        {
            if (!string.IsNullOrEmpty(guId))
                Current.Game?.GetComponent<GuIndividualRegistry>()?.EnqueueProjectionEvent(parent, evidence);
        }
    }

    internal interface IGuProjection
    {
        string GuId { get; }
        int GuRevision { get; }
    }

    internal static class GuManifestationExtensions
    {
        internal static CompGuManifestation GuTag(this Thing thing) =>
            thing is ThingWithComps withComps ? withComps.GetComp<CompGuManifestation>() : null;
    }
}
