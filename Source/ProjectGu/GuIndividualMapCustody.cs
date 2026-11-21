using ProjectGu.Underground;
using Verse;

namespace ProjectGu
{
    public sealed class GuIndividualMapCustody : MapComponent, IGuUndergroundCustodyProtection
    {
        public GuIndividualMapCustody(Map map) : base(map) { }

        public bool HasProtectedCustody =>
            Current.Game?.GetComponent<GuIndividualRegistry>()?.HasAuthorizedGroundCustody(map) == true;
    }
}
