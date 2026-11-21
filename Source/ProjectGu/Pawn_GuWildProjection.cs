using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public class Pawn_GuWildProjection : Pawn
    {
        private string guId;
        private int guRevision;

        internal string GuId => guId;
        internal int GuRevision => guRevision;

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

        public override string GetInspectString()
        {
            string native = base.GetInspectString();
            if (!Prefs.DevMode) return native;
            var individual = Current.Game?.GetComponent<GuIndividualRegistry>()?.Resolve(this);
            string binding = individual?.DebugDescription
                ?? $"Gu {guId ?? "(missing ID)"} | revision {guRevision}\nCustody: quarantined, no authorized binding";
            return string.IsNullOrEmpty(native) ? binding : native + "\n" + binding;
        }
    }
}
