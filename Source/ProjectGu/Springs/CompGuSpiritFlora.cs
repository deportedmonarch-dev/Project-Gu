using Verse;

namespace ProjectGu.Springs
{
    /// <summary>One physical patch or colony. Its ThingDef identifies the flora species.</summary>
    public sealed class CompGuSpiritFlora : ThingComp
    {
        private CompProperties_GuSpiritFlora Props => (CompProperties_GuSpiritFlora)props;
        public int Support => System.Math.Max(0, Props.support);
        public int CavernSupportUnits => System.Math.Max(0, Props.cavernSupportUnits);
        public string EcologyProfile => Props.ecologyProfile;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map?.GetComponent<GuEcologyMapComponent>()?.NotifyFloraChanged(parent.Position);
        }
        public override void PostDeSpawn(Map previousMap, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(previousMap, mode);
            previousMap?.GetComponent<GuEcologyMapComponent>()?.NotifyFloraChanged(parent.Position);
        }
    }
}
