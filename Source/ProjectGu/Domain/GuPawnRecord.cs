using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using Verse;

namespace ProjectGu.Domain
{
    // Saved separately from the visible aperture. Mortals are not given a fake hediff.
    public enum AwakeningState { Unattempted = 0, Failed = 1, Awakened = 2 }

    /// <summary>Read-only legacy cross-reference reader. Raw fields are classified separately.</summary>
    public sealed class GuPawnRecord : IExposable
    {
        internal Pawn Pawn;
        internal string RawXml { get; private set; }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                RawXml = Scribe.loader.curXmlParent?.OuterXml;
            Scribe_References.Look(ref Pawn, "pawn");
        }
    }

    internal sealed class GuLegacyCultivatorRows(XmlNode node)
    {
        private readonly XmlNode source = node == null ? null : new XmlDocument().ImportNode(node, deep: true);
        internal string SourceXml => source?.OuterXml;
        internal GuLegacyCultivatorRow[] Rows => source == null ? []
            : source.ChildNodes.Cast<XmlNode>()
                .Where(child => child.NodeType is not (XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                .Select((child, ordinal) => new GuLegacyCultivatorRow(ordinal, child)).ToArray();

        internal bool HasMalformedEnvelope => source != null
            && (source.Attributes.Cast<XmlAttribute>().Any(attribute => attribute.Name != "IsNull"
                    || !bool.TryParse(attribute.Value, out _))
                || (string.Equals(source.Attributes["IsNull"]?.Value, "true", StringComparison.OrdinalIgnoreCase)
                    && Rows.Length > 0));
    }

    internal sealed class GuLegacyCultivatorRow(int ordinal, XmlNode row)
    {
        internal int Ordinal { get; } = ordinal;
        internal string RawXml { get; } = row.OuterXml;
        internal string PawnToken => row["pawn"]?.InnerText;
        internal string PawnKey => IsPawnKey(PawnToken) ? PawnToken : null;
        internal string[] PawnClaims => row.ChildNodes.OfType<XmlElement>()
            .Where(node => node.Name == "pawn").Select(node => node.InnerText)
            .Where(IsPawnKey).Distinct(StringComparer.Ordinal).ToArray();

        internal static bool IsPawnKey(string token) => !string.IsNullOrWhiteSpace(token)
            && token != "null" && string.Equals(token.Trim(), token, StringComparison.Ordinal);

        internal bool TryReadFields(out int aptitude, out AwakeningState state, out string physique, out bool legacyMigrated)
        {
            aptitude = 0;
            state = AwakeningState.Unattempted;
            physique = row["extremePhysiqueId"]?.InnerText ?? "";
            legacyMigrated = false;
            if (row.Name != "li" || row.Attributes.Cast<XmlAttribute>().Any(attribute =>
                    attribute.Name != "Class" || attribute.Value != typeof(GuPawnRecord).FullName))
                return false;

            var fields = row.ChildNodes.OfType<XmlElement>().ToArray();
            if (fields.Any(field => field.HasAttributes || field.ChildNodes.OfType<XmlElement>().Any()
                    || field.Name is not ("pawn" or "latentAptitude" or "extremePhysiqueId" or "awakeningState" or "legacyMigrated"))
                || fields.GroupBy(field => field.Name, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || row.ChildNodes.Cast<XmlNode>().Any(child => child is not XmlElement && !string.IsNullOrWhiteSpace(child.Value)))
                return false;

            if (!int.TryParse(row["latentAptitude"]?.InnerText ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out aptitude)
                || !GuCultivatorRecord.ValidAptitude(aptitude)
                || !int.TryParse(row["awakeningState"]?.InnerText ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var stateValue)
                || !Enum.IsDefined((AwakeningState)stateValue)
                || !bool.TryParse(row["legacyMigrated"]?.InnerText ?? "false", out legacyMigrated))
            {
                return false;
            }
            state = (AwakeningState)stateValue;
            return true;
        }
    }
}
