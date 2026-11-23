using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using Verse;

namespace ProjectGu.Domain
{
    public enum GuNativeAssociation { Unresolved, Bound, Detached }
    public enum GuObservedLife { Unknown, Alive, Dead }

    /// <summary>Confirmed facts are separate from the Pawn binding. Native observers land after 3A-I.</summary>
    public sealed class GuCultivatorFact : IExposable
    {
        public string EventKey;
        public string Cause;
        public int Tick = -1;
        public GuObservedLife Life;
        private bool invalidSavedLife;

        internal bool IsValid => !string.IsNullOrWhiteSpace(EventKey) && !string.IsNullOrWhiteSpace(Cause)
            && Tick >= 0 && Enum.IsDefined(Life) && !invalidSavedLife;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?["life"] is { } life)
                invalidSavedLife = !Enum.TryParse(life.InnerText, out GuObservedLife state) || !Enum.IsDefined(state);
            Scribe_Values.Look(ref EventKey, "eventKey");
            Scribe_Values.Look(ref Cause, "cause");
            Scribe_Values.Look(ref Tick, "tick", -1);
            Scribe_Values.Look(ref Life, "life");
        }
    }

    public sealed class GuCultivatorRecord : IExposable
    {
        internal string Id;
        internal string CreationKey;
        internal string PawnLoadId;
        internal string ReadPawnToken { get; private set; }
        internal Pawn Pawn;
        internal GuNativeAssociation Association;
        internal int AssociationRevision = 1;
        internal GuObservedLife LastObservedLife;
        internal int ObservationTick = -1;
        internal int StateRevision = 1;
        internal string CachedName;
        internal bool Significant;
        internal List<string> ApertureIds = [];
        internal GuCultivatorFact LastCorpseDestruction;
        internal GuCultivatorFact LastProvenCorpselessDeath;
        internal GuCultivatorFact ConfirmedDiscard;
        private bool invalidSavedFields;

        public string CultivatorId => Id;
        public GuNativeAssociation NativeAssociation => Association;
        public GuObservedLife CurrentLife => Association == GuNativeAssociation.Bound && Pawn is { Discarded: false, health: not null }
            ? Pawn.Dead ? GuObservedLife.Dead : GuObservedLife.Alive
            : GuObservedLife.Unknown;
        public int LatentAptitude;
        public string ExtremePhysiqueId = "";
        public AwakeningState State;
        public bool LegacyMigrated;

        internal static bool ValidAptitude(int value) => value == 0 || value is >= 20 and <= 100;
        internal static bool IsId(string value) => Guid.TryParseExact(value, "N", out _);

        internal bool IsValid => !invalidSavedFields && IsId(Id) && !string.IsNullOrWhiteSpace(CreationKey)
            && (PawnLoadId == null || GuLegacyCultivatorRow.IsPawnKey(PawnLoadId))
            && (Association != GuNativeAssociation.Bound || PawnLoadId != null)
            && AssociationRevision > 0 && StateRevision > 0 && ValidAptitude(LatentAptitude)
            && Enum.IsDefined(State) && Enum.IsDefined(Association) && Enum.IsDefined(LastObservedLife)
            && (LastObservedLife == GuObservedLife.Unknown ? ObservationTick == -1 : ObservationTick >= 0)
            && ApertureIds != null && ApertureIds.All(IsId)
            && (LastCorpseDestruction == null || LastCorpseDestruction.IsValid)
            && (LastProvenCorpselessDeath == null || LastProvenCorpselessDeath.IsValid)
            && (ConfirmedDiscard == null || ConfirmedDiscard.IsValid)
            && (Association == GuNativeAssociation.Detached) == (ConfirmedDiscard != null);

        internal void ObserveBoundLife(int tick)
        {
            var life = CurrentLife;
            if (life == GuObservedLife.Unknown) return;
            LastObservedLife = life;
            ObservationTick = tick;
            if (Significant) CachedName = Pawn.LabelShort;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var xml = Scribe.loader.curXmlParent;
                ReadPawnToken = xml?["pawn"]?.InnerText;
                invalidSavedFields = !HasValidSavedFields(xml);
            }
            Pawn reference = Pawn;
            var association = Association;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (association != GuNativeAssociation.Bound || !GuWorldComponent.HasNativeSaveOwner(reference))
                {
                    reference = null;
                    if (association == GuNativeAssociation.Bound) association = GuNativeAssociation.Unresolved;
                }
                else
                    ObserveBoundLife(Find.TickManager?.TicksGame ?? 0);
            }
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref CreationKey, "creationKey");
            Scribe_Values.Look(ref PawnLoadId, "pawnLoadId");
            Scribe_References.Look(ref reference, "pawn", saveDestroyedThings: true);
            Scribe_Values.Look(ref association, "association");
            if (Scribe.mode == LoadSaveMode.LoadingVars) Association = association;
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs) Pawn = reference;
            Scribe_Values.Look(ref AssociationRevision, "associationRevision", 1);
            Scribe_Values.Look(ref LastObservedLife, "lastObservedLife");
            Scribe_Values.Look(ref ObservationTick, "observationTick", -1);
            Scribe_Values.Look(ref StateRevision, "stateRevision", 1);
            Scribe_Values.Look(ref CachedName, "cachedName");
            Scribe_Values.Look(ref Significant, "significant");
            Scribe_Values.Look(ref LatentAptitude, "latentAptitude", 0);
            Scribe_Values.Look(ref ExtremePhysiqueId, "extremePhysiqueId", "");
            Scribe_Values.Look(ref State, "awakeningState");
            Scribe_Values.Look(ref LegacyMigrated, "legacyMigrated", false);
            // Omit empty links on save without skipping the Scribe phases for a present list.
            if (Scribe.mode != LoadSaveMode.Saving || ApertureIds.Count > 0)
                Scribe_Collections.Look(ref ApertureIds, "apertures", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && ApertureIds == null) ApertureIds = [];
            ExposeFact(ref LastCorpseDestruction, "lastCorpseDestruction");
            ExposeFact(ref LastProvenCorpselessDeath, "lastProvenCorpselessDeath");
            ExposeFact(ref ConfirmedDiscard, "confirmedDiscard");
        }

        private static void ExposeFact(ref GuCultivatorFact fact, string key)
        {
            if (Scribe.mode != LoadSaveMode.Saving || fact != null)
                Scribe_Deep.Look(ref fact, key);
        }

        private static bool HasValidSavedFields(XmlNode node)
        {
            if (node == null || node.ChildNodes.OfType<XmlElement>().GroupBy(child => child.Name).Any(group => group.Count() != 1))
                return false;
            if (node.ChildNodes.OfType<XmlElement>().Any(child => child.Name is not
                ("id" or "creationKey" or "pawnLoadId" or "pawn" or "association" or "associationRevision"
                or "lastObservedLife" or "observationTick" or "stateRevision" or "cachedName" or "significant"
                or "latentAptitude" or "extremePhysiqueId" or "awakeningState" or "legacyMigrated" or "apertures"
                or "lastCorpseDestruction" or "lastProvenCorpselessDeath" or "confirmedDiscard")))
                return false;
            bool Valid(string key, Func<string, bool> check) => node[key] is not { } child || check(child.InnerText);
            bool IsInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            bool IsEnum<T>(string value) where T : struct, Enum => Enum.TryParse(value, out T parsed) && Enum.IsDefined(parsed);
            return Valid("association", IsEnum<GuNativeAssociation>) && Valid("lastObservedLife", IsEnum<GuObservedLife>)
                && Valid("awakeningState", IsEnum<AwakeningState>)
                && Valid("latentAptitude", IsInt) && Valid("observationTick", IsInt)
                && Valid("associationRevision", IsInt) && Valid("stateRevision", IsInt)
                && Valid("legacyMigrated", value => bool.TryParse(value, out _))
                && Valid("significant", value => bool.TryParse(value, out _));
        }
    }

    public sealed class GuCultivatorEvidence : IExposable
    {
        internal string Key;
        internal int SourceOrdinal = -1;
        internal string PawnToken;
        internal List<string> PawnClaims = [];
        internal string RawEvidence;
        internal string Reason;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key");
            Scribe_Values.Look(ref SourceOrdinal, "sourceOrdinal", -1);
            Scribe_Values.Look(ref PawnToken, "pawnToken");
            if (Scribe.mode != LoadSaveMode.Saving || PawnClaims.Count > 0)
                Scribe_Collections.Look(ref PawnClaims, "pawnClaims", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && PawnClaims == null) PawnClaims = [];
            Scribe_Values.Look(ref RawEvidence, "rawEvidence");
            Scribe_Values.Look(ref Reason, "reason");
        }
    }
}
