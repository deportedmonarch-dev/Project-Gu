using Verse;

namespace ProjectGu.Springs
{
    /// <summary>Species-neutral habitat contribution. Species are freely defined in XML.</summary>
    public sealed class CompProperties_GuSpiritFlora : CompProperties
    {
        public int support = 1;
        public string ecologyProfile = "generic";
        public CompProperties_GuSpiritFlora() { compClass = typeof(CompGuSpiritFlora); }
    }
}
