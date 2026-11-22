using Verse;

namespace ProjectGu.Domain
{
    public enum GuV1BiologicalState { Active = 1, Dormant = 2 }

    public enum GuV1CustodyKind { WildPawn = 1, DormantGround = 2, DormantInventory = 3, Aperture = 4 }

    /// <summary>
    /// Read-only Schema V1 record, loaded from <c>guIndividualRecordsV1</c> only to migrate it to <see cref="GuRecord"/>.
    /// The Scribe keys match the 0.2.5 release exactly. V2 never writes this list.
    /// </summary>
    public sealed class GuIndividualV1 : IExposable
    {
        internal const int SchemaVersion = 1;
        private int version;
        private string id;
        private string speciesDefName;
        private int rank;
        private string origin;
        private string originId;
        private int createdTick = -1;
        private int biologicalState;
        private int custodyKind;
        private string nativeOwnerId;
        private string holderPawnId;
        private int revision;
        private string quarantineReason;
        private string creationEventKey;
        private string originNativeId;
        private bool cohortDebitCommitted;

        public int Version => version;
        public string Id => id;
        public string SpeciesDefName => speciesDefName;
        public int Rank => rank;
        public string Origin => origin;
        public string OriginId => originId;
        public int CreatedTick => createdTick;
        public GuV1BiologicalState BiologicalState => (GuV1BiologicalState)biologicalState;
        public GuV1CustodyKind CustodyKind => (GuV1CustodyKind)custodyKind;
        public string NativeOwnerId => nativeOwnerId;
        public string HolderPawnId => holderPawnId;
        public int Revision => revision;
        public string QuarantineReason => quarantineReason;
        public string CreationEventKey => creationEventKey;
        public string OriginNativeId => originNativeId;
        public bool CohortDebitCommitted => cohortDebitCommitted;

        public void ExposeData()
        {
            Scribe_Values.Look(ref version, "version");
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref speciesDefName, "speciesDefName");
            Scribe_Values.Look(ref rank, "rank");
            Scribe_Values.Look(ref origin, "origin");
            Scribe_Values.Look(ref originId, "originId");
            Scribe_Values.Look(ref createdTick, "createdTick", -1);
            Scribe_Values.Look(ref biologicalState, "biologicalState");
            Scribe_Values.Look(ref custodyKind, "custodyKind");
            Scribe_Values.Look(ref nativeOwnerId, "nativeOwnerId");
            Scribe_Values.Look(ref holderPawnId, "holderPawnId");
            Scribe_Values.Look(ref revision, "revision");
            Scribe_Values.Look(ref quarantineReason, "quarantineReason");
            Scribe_Values.Look(ref creationEventKey, "creationEventKey");
            Scribe_Values.Look(ref originNativeId, "originNativeId");
            Scribe_Values.Look(ref cohortDebitCommitted, "cohortDebitCommitted");
        }
    }
}
