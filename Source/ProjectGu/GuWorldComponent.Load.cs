using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using ProjectGu.Domain;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu
{
    internal enum GuCultivatorLoadKind { Legacy, Committed, Invalid }

    public partial class GuWorldComponent
    {
        private const string LegacyKey = "guPawnRecords";
        private const string RecordsKey = "guCultivatorRecordsV1";
        private const string EvidenceKey = "guCultivatorIntegrityV1";
        private const string SchemaKey = "guCultivatorSchemaVersion";
        private const string CompleteKey = "guCultivatorMigrationComplete";
        private const int CurrentSchema = 1;

        private static readonly ConditionalWeakTable<Game, List<Hediff_Aperture>> loadedApertures = new();
        private readonly Game game;
        private GuCultivatorPlan store;
        private GuLegacyCultivatorRows legacyRows;
        private List<GuPawnRecord> legacyReferences;
        private List<GuCultivatorRecord> readRecords;
        private List<GuCultivatorEvidence> readEvidence;
        private int expectedRecords;
        private int expectedEvidence;
        private GuCultivatorLoadKind loadKind;
        private bool postLoadComplete;
        private bool componentLoaded;
        private bool loadFailed;
        private bool awakeningInProgress;
        private GuIndividualRegistry readyRegistry;

        internal bool CanSaveCultivators => store != null && !loadFailed && !awakeningInProgress;
        internal GuLegacyCultivatorRows PendingLegacyRows => legacyRows;
        internal GuCultivatorRecord FindCultivator(string id) => CanSaveCultivators && id != null ? store.ById.GetValueOrDefault(id) : null;

        internal static void NoteLoadedAperture(Game game, Hediff_Aperture aperture)
        {
            if (game != null) loadedApertures.GetOrCreateValue(game).Add(aperture);
        }

        internal static GuCultivatorLoadKind ClassifyEnvelope(XmlNode component)
        {
            if (component == null) return GuCultivatorLoadKind.Invalid;
            if (component[SchemaKey] == null && component[CompleteKey] == null
                && component[RecordsKey] == null && component[EvidenceKey] == null)
                return component.ChildNodes.OfType<XmlElement>().Count(node => node.Name == LegacyKey) <= 1
                    ? GuCultivatorLoadKind.Legacy : GuCultivatorLoadKind.Invalid;
            bool HasOne(string key) => component.ChildNodes.OfType<XmlElement>().Count(node => node.Name == key) == 1;
            bool IsMarker(string key) => HasOne(key) && !component[key].HasAttributes
                && !component[key].ChildNodes.OfType<XmlElement>().Any();
            bool IsList(string key) => HasOne(key) && !component[key].HasAttributes
                && component[key].ChildNodes.Cast<XmlNode>().All(child => child is XmlElement { Name: "li" }
                    || child.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace);
            return component[LegacyKey] == null && IsMarker(SchemaKey) && IsMarker(CompleteKey)
                && component[SchemaKey].InnerText == CurrentSchema.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && bool.TryParse(component[CompleteKey].InnerText, out bool complete) && complete
                && IsList(RecordsKey) && IsList(EvidenceKey)
                ? GuCultivatorLoadKind.Committed : GuCultivatorLoadKind.Invalid;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (!CanSaveCultivators)
                    throw new InvalidOperationException("Cultivator identity has no safe save authority.");
                int version = CurrentSchema;
                bool complete = true;
                Scribe_Values.Look(ref version, SchemaKey);
                Scribe_Values.Look(ref complete, CompleteKey);
                // Pass the exact authoritative lists; these locals are never adopted on load.
                var records = store.Records;
                var evidence = store.Evidence;
                Scribe_Collections.Look(ref records, RecordsKey, LookMode.Deep);
                Scribe_Collections.Look(ref evidence, EvidenceKey, LookMode.Deep);
                return;
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                var xml = Scribe.loader.curXmlParent;
                // This copy precedes both the native reference reader and any index construction.
                legacyRows = new GuLegacyCultivatorRows(xml?[LegacyKey]);
                store = null;
                legacyReferences = null;
                readRecords = null;
                readEvidence = null;
                postLoadComplete = false;
                componentLoaded = false;
                loadFailed = false;
                readyRegistry = null;
                loadKind = ClassifyEnvelope(xml);
                if (loadKind == GuCultivatorLoadKind.Invalid || legacyRows.HasMalformedEnvelope)
                {
                    FailLoad("Invalid or incomplete cultivator save envelope.");
                    return;
                }
                if (loadKind == GuCultivatorLoadKind.Committed)
                {
                    expectedRecords = xml[RecordsKey].ChildNodes.OfType<XmlElement>().Count();
                    expectedEvidence = xml[EvidenceKey].ChildNodes.OfType<XmlElement>().Count();
                }
            }

            if (loadFailed) return;
            try
            {
                if (loadKind == GuCultivatorLoadKind.Legacy)
                    Scribe_Collections.Look(ref legacyReferences, LegacyKey, LookMode.Deep);
                else
                {
                    Scribe_Collections.Look(ref readRecords, RecordsKey, LookMode.Deep);
                    Scribe_Collections.Look(ref readEvidence, EvidenceKey, LookMode.Deep);
                }

                if (Scribe.mode == LoadSaveMode.PostLoadInit) postLoadComplete = true;
            }
            catch (Exception error)
            {
                FailLoad($"Cultivator Scribe load failed. {error}");
            }
        }

        public override void LoadedGame()
        {
            componentLoaded = true;
            CompleteLoad();
        }

        public override void StartedNewGame()
        {
            if (store != null) return;
            loadKind = GuCultivatorLoadKind.Committed;
            readRecords = [];
            readEvidence = [];
            expectedRecords = expectedEvidence = 0;
            postLoadComplete = true;
            componentLoaded = true;
            CompleteLoad();
        }

        internal void GuRegistryCompleted(GuIndividualRegistry registry)
        {
            if (store != null || loadFailed || game?.GetComponent<GuIndividualRegistry>() != registry) return;
            readyRegistry = registry;
            CompleteLoad();
        }

        private void CompleteLoad()
        {
            if (store != null || loadFailed || !componentLoaded || readyRegistry == null) return;
            if (!postLoadComplete)
            {
                FailLoad("Cultivator PostLoadInit did not complete.");
                return;
            }
            try
            {
                loadedApertures.TryGetValue(game, out var intake);
                loadedApertures.Remove(game);
                var apertures = intake ?? [];
                GuCultivatorPlan prepared;
                if (loadKind == GuCultivatorLoadKind.Legacy)
                {
                    var crossRefs = legacyReferences ?? [];
                    var pawns = PawnsFinder.All_AliveOrDead.Concat(crossRefs.Select(row => row?.Pawn))
                        .Concat(apertures.Select(aperture => aperture.pawn)).Where(GuCultivatorPlan.IsCandidate)
                        .Distinct<Pawn>(ReferenceEqualityComparer.Instance).ToArray();
                    apertures.AddRange(pawns.SelectMany(pawn => pawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>()));
                    prepared = GuCultivatorPlan.PrepareLegacy(legacyRows, crossRefs, pawns, apertures, readyRegistry.Records);
                }
                else
                {
                    if (readRecords?.Count != expectedRecords || readEvidence?.Count != expectedEvidence)
                        throw new InvalidOperationException("Cultivator Scribe load did not account for every saved row.");
                    apertures.AddRange(readRecords.Where(record => record?.Pawn?.health != null)
                        .SelectMany(record => record.Pawn.health.hediffSet.hediffs.OfType<Hediff_Aperture>()));
                    prepared = GuCultivatorPlan.PrepareCommitted(readRecords, readEvidence, apertures, readyRegistry.Records);
                }

                // Publishing the prepared reference is the last operation in the commit.
                prepared.CommitApertureWrites(() => store = prepared);
            }
            catch (Exception error)
            {
                FailLoad($"Cultivator identity failed before safe publication. {error}");
                return;
            }
            legacyRows = null;
            legacyReferences = null;
            readRecords = null;
            readEvidence = null;
            if (store.Evidence.Count > 0)
                Log.Warning($"[Project GU] Cultivator identity retained {store.Evidence.Count} unresolved evidence entries.");
        }

        private void FailLoad(string detail)
        {
            loadFailed = true;
            Log.Error($"[Project GU] Cultivator saves and mutations are blocked. {detail}");
        }

        internal static bool HasNativeSaveOwner(Pawn pawn) => GuCultivatorPlan.IsCandidate(pawn)
            && (pawn.SpawnedOrAnyParentSpawned || pawn.Corpse?.SpawnedOrAnyParentSpawned == true
                || Find.World != null && (pawn.IsWorldPawn() || pawn.GetCaravan() is { Spawned: true }
                    || PawnUtility.IsTravelingInTransportPodWorldObject(pawn)));
    }
}
