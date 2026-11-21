using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public class Pawn_GuWildProjection : Pawn, IGuProjection
    {
        private string guId;
        private int guRevision;

        public string GuId => guId;
        public int GuRevision => guRevision;

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
        }

        public override string GetInspectString() => GuIndividualRegistry.InspectProjection(this, base.GetInspectString());
    }
}
