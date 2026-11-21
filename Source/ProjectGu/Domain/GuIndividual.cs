using System;
using Verse;

namespace ProjectGu.Domain
{
    public enum GuBiologicalState { Active = 1, Dormant = 2 }

    public enum GuCustodyKind { WildPawn = 1, DormantGround = 2, DormantInventory = 3 }

    public readonly record struct GuCustody(GuCustodyKind Kind, string NativeOwnerId, string HolderPawnId = null);

    public sealed class GuIndividual : IExposable
    {
        internal const int CurrentVersion = 1;
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

        public int Version => version;
        public string Id => id;
        public string SpeciesDefName => speciesDefName;
        public GuSpeciesDef Species => string.IsNullOrWhiteSpace(speciesDefName)
            ? null : DefDatabase<GuSpeciesDef>.GetNamedSilentFail(speciesDefName);
        public int Rank => rank;
        public string Origin => origin;
        public string OriginId => originId;
        public int CreatedTick => createdTick;
        public GuBiologicalState BiologicalState => (GuBiologicalState)biologicalState;
        public GuCustody Custody => new((GuCustodyKind)custodyKind, nativeOwnerId, holderPawnId);
        public int Revision => revision;
        public string QuarantineReason => quarantineReason;
        public bool IsQuarantined => !string.IsNullOrEmpty(quarantineReason);

        public GuIndividual() { }

        internal GuIndividual(string id, GuSpeciesDef species, string origin, string originId, int tick, string pawnId)
        {
            version = CurrentVersion;
            this.id = id;
            speciesDefName = species.defName;
            rank = species.minRank;
            this.origin = origin;
            this.originId = originId;
            createdTick = tick;
            biologicalState = (int)GuBiologicalState.Active;
            custodyKind = (int)GuCustodyKind.WildPawn;
            nativeOwnerId = pawnId;
            revision = 1;
        }

        internal void Quarantine(string reason)
        {
            if (IsQuarantined) return;
            quarantineReason = reason;
            Log.Warning($"[Project GU] Quarantined Gu individual {id ?? "(missing ID)"} ({nativeOwnerId ?? "missing owner"}). {reason}");
        }

        internal void CommitDormancy(string thingId)
        {
            biologicalState = (int)GuBiologicalState.Dormant;
            custodyKind = (int)GuCustodyKind.DormantGround;
            nativeOwnerId = thingId;
        }

        internal void CommitDormantTransfer(string pawnId)
        {
            custodyKind = (int)(pawnId == null ? GuCustodyKind.DormantGround : GuCustodyKind.DormantInventory);
            holderPawnId = pawnId;
            revision++;
        }

        internal string DebugDescription => $"Gu {id ?? "(missing ID)"} | {speciesDefName} rank {rank} | revision {revision}"
            + $"\nCustody: {Custody.Kind} ({nativeOwnerId}) | {BiologicalState}"
            + (holderPawnId == null ? "" : $"\nInventory holder: {holderPawnId}")
            + (IsQuarantined ? $"\nQuarantined: {quarantineReason}" : "");

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
        }
    }
}
