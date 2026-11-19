using Verse;

namespace ProjectGu.Springs
{
    /// <summary>Instant local habitat invalidation on spawn/removal, including save reload.</summary>
    public sealed class CompGuSpiritFlora : ThingComp
    {
        public int Support => System.Math.Max(0, ((CompProperties_GuSpiritFlora)props).support);
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map?.GetComponent<GuEcologyMapComponent>()?.NotifyFloraChanged(parent.Position);
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            // Notify springs when habitat is destroyed. Map recount is a fallback
            // for a non-destructive despawn, which has no 1.7 ThingComp hook here.
            IntVec3 cell = parent.Position;
            base.PostDestroy(mode, previousMap);
            previousMap?.GetComponent<GuEcologyMapComponent>()?.NotifyFloraChanged(cell);
        }
    }
}
