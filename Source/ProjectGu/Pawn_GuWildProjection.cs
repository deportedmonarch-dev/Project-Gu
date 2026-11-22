using System;
using System.Collections.Generic;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    /// <summary>
    /// Legacy-compatible Pawn class for the developer wild Gu. Saved V1 Pawns bind to this class name, so it stays
    /// loadable. Identity lives in <see cref="CompGuManifestation"/>. The class only suppresses ticks and commands while
    /// a release or materialization is being staged.
    /// </summary>
    public class Pawn_GuWildProjection : Pawn, IGuProjection
    {
        private bool releaseSuppressed;

        public string GuId => this.GuTag()?.GuId;
        public int GuRevision => this.GuTag()?.GuRevision ?? 0;
        internal bool ReleaseSuppressed => releaseSuppressed;

        internal void SetReleaseSuppressed(bool suppressed) => releaseSuppressed = suppressed;

        internal void InitializeBinding(GuRecord record) => this.GuTag()?.Bind(record);

        public override void ExposeData()
        {
            // The V1 identity keys are read and written by the manifestation comp in the same node.
            base.ExposeData();
            Scribe_Values.Look(ref releaseSuppressed, "guReleaseSuppressed");
        }

        protected override void Tick()
        {
            if (!releaseSuppressed) base.Tick();
        }

        public override void TickRare()
        {
            if (!releaseSuppressed) base.TickRare();
        }

        protected override void TickInterval(int delta)
        {
            if (!releaseSuppressed) base.TickInterval(delta);
        }

        public override void Kill(DamageInfo? dinfo = null, Hediff exactCulprit = null)
        {
            this.GuTag()?.CaptureEndingLocation();
            base.Kill(dinfo, exactCulprit);
        }

        public override IEnumerable<Gizmo> GetGizmos() => releaseSuppressed ? Array.Empty<Gizmo>() : base.GetGizmos();
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn) =>
            releaseSuppressed ? Array.Empty<FloatMenuOption>() : base.GetFloatMenuOptions(selPawn);

        public override string GetInspectString() => GuIndividualRegistry.InspectProjection(this, base.GetInspectString());
    }
}
