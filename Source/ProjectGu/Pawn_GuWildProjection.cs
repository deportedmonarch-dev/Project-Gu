using System;
using System.Collections.Generic;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public class Pawn_GuWildProjection : Pawn, IGuProjection
    {
        private string guId;
        private int guRevision;
        private bool releaseSuppressed;

        public string GuId => guId;
        public int GuRevision => guRevision;
        internal bool ReleaseSuppressed => releaseSuppressed;

        internal void SetReleaseSuppressed(bool suppressed) => releaseSuppressed = suppressed;

        internal void InitializeBinding(GuIndividual individual)
        {
            guId = individual.Id;
            guRevision = individual.Revision;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref guId, "guIndividualId");
            Scribe_Values.Look(ref guRevision, "guBindingRevision");
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

        public override IEnumerable<Gizmo> GetGizmos() => releaseSuppressed ? Array.Empty<Gizmo>() : base.GetGizmos();
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn) =>
            releaseSuppressed ? Array.Empty<FloatMenuOption>() : base.GetFloatMenuOptions(selPawn);

        public override string GetInspectString() => GuIndividualRegistry.InspectProjection(this, base.GetInspectString());
    }
}
