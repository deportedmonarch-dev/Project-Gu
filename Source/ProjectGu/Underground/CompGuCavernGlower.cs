using ProjectGu.Springs;
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
            base.ShouldBeLitNow && GuPocketSiteDef.IsSpringCavern(parent.Map)
                && parent.GetComp<CompGuSpiritSpring>()?.UsesCavernComposition != true;
    }

    public sealed class CompProperties_GuBotanicalGlower : CompProperties_Glower
    {
        public CompProperties_GuBotanicalGlower() => compClass = typeof(CompGuBotanicalGlower);
    }

    public sealed class CompGuBotanicalGlower : CompGlower
    {
        protected override bool ShouldBeLitNow =>
            base.ShouldBeLitNow && parent.GetComp<CompGuSpiritSpring>() is
                { UsesBotanicalFlora: true, UsesCavernComposition: false };

        // The spring's profile determines this fixed light on spawn and load.
        public override void PostExposeData() { }
    }
}
