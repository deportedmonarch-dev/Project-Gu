using RimWorld;
using Verse;

namespace ProjectGu.Underground
{
    public sealed class CompProperties_GuCavernGlower : CompProperties_Glower
    {
        public CompProperties_GuCavernGlower() => compClass = typeof(CompGuCavernGlower);
    }

    public sealed class CompGuCavernGlower : CompGlower
    {
        protected override bool ShouldBeLitNow =>
            base.ShouldBeLitNow && GuPocketSiteDef.IsSpringCavern(parent.Map);
    }
}
