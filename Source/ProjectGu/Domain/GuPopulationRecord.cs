using System;
using Verse;

namespace ProjectGu.Domain
{
    /// <summary>
    /// One authoritative aggregate cohort for one Gu species in one population custodian.
    /// Not an individual Gu, a Thing, or a global pool. Persisted by its owning habitat.
    /// String species keys preserve missing optional-addon populations without dropping them.
    /// </summary>
    public sealed class GuPopulationRecord : IExposable
    {
        /// <summary>Debit evidence schema. 0: a row that has never committed a receipt-backed debit (historical rules).</summary>
        internal const int CurrentEvidenceVersion = 1;
        internal const long DigestSeed = unchecked((long)14695981039346656037UL);

        private string speciesDefName = "";
        private int matureCount;
        private int juvenileCount;
        private int nextEcologyTick = -1;
        private long revision;

        // Alpha 6 owner-side debit evidence (RFC-0007 conservation ledger). Every registry receipt that debited this row is
        // counted and folded, in commit order, into a chained digest of (revision after the debit, event key). The seal binds the
        // counts, revision and evidence together and is refreshed only by this class's own mutations, so a row substituted,
        // regenerated or edited outside its owner no longer agrees with the registry's receipts. Additive Scribe keys.
        private int evidenceVersion;
        private long receiptDebits;
        private long receiptDigest;
        private long seal;

        public string SpeciesDefName => speciesDefName;
        public int MatureCount => matureCount;
        public int JuvenileCount => juvenileCount;
        public long TotalCount => (long)matureCount + juvenileCount;
        public int NextEcologyTick => nextEcologyTick;
        public long Revision => revision;
        internal int EvidenceVersion => evidenceVersion;
        internal long ReceiptDebits => receiptDebits;
        internal long ReceiptDigest => receiptDigest;

        /// <summary>False when a row with debit evidence was changed outside its own mutations (substitution or edit).</summary>
        internal bool EvidenceIntact => evidenceVersion == 0 ? receiptDebits == 0 && receiptDigest == 0 && seal == 0
            : evidenceVersion == CurrentEvidenceVersion && receiptDebits > 0 && seal == ComputeSeal();

        // Parameterless constructor is required by RimWorld's deep Scribe loading.
        public GuPopulationRecord() { }

        public GuPopulationRecord(string species, int mature, int juveniles, int nextTick)
        {
            if (string.IsNullOrWhiteSpace(species)) throw new ArgumentException("Species cannot be empty.", nameof(species));
            if (mature < 0 || juveniles < 0) throw new ArgumentOutOfRangeException(nameof(mature), "Population counts cannot be negative.");
            speciesDefName = species;
            matureCount = mature;
            juvenileCount = juveniles;
            nextEcologyTick = nextTick;
        }

        /// <summary>A direct sink with no individual, such as awakening's Hope Gu. Not a receipt-backed debit.</summary>
        public bool TryConsumeMature(int count)
        {
            if (count <= 0 || matureCount < count) return false;
            bool intact = SealMatches;
            matureCount -= count;
            AdvanceRevision();
            Reseal(intact);
            return true;
        }

        /// <summary>
        /// The registry's materialization debit: one mature unit, the revision, and the receipt folded into the owner-side
        /// evidence, in one step inside the callback-free commit window.
        /// </summary>
        internal bool TryDebitForReceipt(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey) || matureCount < 1 || revision == long.MaxValue || receiptDebits == long.MaxValue) return false;
            bool intact = SealMatches;
            matureCount--;
            revision++;
            if (evidenceVersion == 0)
            {
                evidenceVersion = CurrentEvidenceVersion;
                receiptDigest = DigestSeed;
            }
            receiptDebits++;
            receiptDigest = ChainDigest(receiptDigest, revision, eventKey);
            Reseal(intact);
            return true;
        }

        internal GuCohortPreimage Preimage => new(matureCount, juvenileCount, revision, evidenceVersion, receiptDebits, receiptDigest);

        /// <summary>
        /// Reverses exactly one receipt debit, including its revision and evidence, and nothing else. Only the registry calls this,
        /// inside a materialization's callback-free window and before publication, when no save, query or callback can have
        /// observed the debited state. Any other state means the debit may have been observed, so it stays.
        /// </summary>
        internal bool TryRestoreUnpublishedDebit(GuCohortPreimage before)
        {
            if (before.Revision == long.MaxValue || before.Mature < 1 || matureCount != before.Mature - 1
                || juvenileCount != before.Juvenile || revision != before.Revision + 1 || receiptDebits != before.ReceiptDebits + 1) return false;
            matureCount = before.Mature;
            revision = before.Revision;
            evidenceVersion = before.EvidenceVersion;
            receiptDebits = before.ReceiptDebits;
            receiptDigest = before.ReceiptDigest;
            seal = evidenceVersion == 0 ? 0 : ComputeSeal();
            return true;
        }

        public void AdvanceDaily(int capacity, bool canReproduce, int birthsPerDay)
        {
            capacity = Math.Max(0, capacity);
            int freeAdultSpace = Math.Max(0, capacity - matureCount);
            bool intact = SealMatches;
            int matured = Math.Min(juvenileCount, freeAdultSpace);
            matureCount += matured;
            juvenileCount -= matured;

            int born = 0;
            if (canReproduce && birthsPerDay > 0)
            {
                long freeSpace = Math.Max(0, capacity - TotalCount);
                born = (int)Math.Min(freeSpace, birthsPerDay);
                juvenileCount += born;
            }
            if (matured != 0 || born != 0) AdvanceRevision();
            Reseal(intact);
        }

        private void AdvanceRevision()
        {
            if (revision < long.MaxValue) revision++;
        }

        public void SetNextEcologyTick(int tick) { nextEcologyTick = tick; }

        internal bool TryRecruitMature(int capacity, int livingResidents)
        {
            if (!EvidenceIntact || revision == long.MaxValue || livingResidents < 0
                || TotalCount + livingResidents >= capacity) return false;
            matureCount++;
            AdvanceRevision();
            Reseal(true);
            return true;
        }

        /// <summary>The registry recomputes the same chain from its receipts. Stable across processes and platforms.</summary>
        internal static long ChainDigest(long previous, long revisionAfter, string eventKey)
        {
            ulong hash = unchecked((ulong)previous);
            hash = Mix(hash, unchecked((ulong)revisionAfter));
            foreach (char c in eventKey ?? "") hash = Mix(hash, c);
            return unchecked((long)Mix(hash, 0xFFUL));
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (value >> shift) & 0xFF;
                hash = unchecked(hash * 1099511628211UL);
            }
            return hash;
        }

        private long ComputeSeal()
        {
            ulong hash = unchecked((ulong)DigestSeed);
            foreach (char c in speciesDefName ?? "") hash = Mix(hash, c);
            hash = Mix(hash, unchecked((ulong)matureCount));
            hash = Mix(hash, unchecked((ulong)juvenileCount));
            hash = Mix(hash, unchecked((ulong)revision));
            hash = Mix(hash, unchecked((ulong)evidenceVersion));
            hash = Mix(hash, unchecked((ulong)receiptDebits));
            hash = Mix(hash, unchecked((ulong)receiptDigest));
            return unchecked((long)hash);
        }

        private bool SealMatches => evidenceVersion == 0 || seal == ComputeSeal();

        // Refreshes the seal only when the row was intact before the mutation, so an outside edit stays detectable afterwards.
        private void Reseal(bool wasIntact)
        {
            if (evidenceVersion != 0 && wasIntact) seal = ComputeSeal();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref speciesDefName, "speciesDefName", "");
            Scribe_Values.Look(ref matureCount, "mature", 0);
            Scribe_Values.Look(ref juvenileCount, "juveniles", 0);
            Scribe_Values.Look(ref nextEcologyTick, "nextEcologyTick", -1);
            Scribe_Values.Look(ref revision, "revision", 0L);
            Scribe_Values.Look(ref evidenceVersion, "debitEvidenceVersion", 0);
            Scribe_Values.Look(ref receiptDebits, "receiptDebits", 0L);
            Scribe_Values.Look(ref receiptDigest, "receiptDigest", 0L);
            Scribe_Values.Look(ref seal, "evidenceSeal", 0L);
        }
    }
}
