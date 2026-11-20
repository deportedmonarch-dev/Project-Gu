using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ProjectGu.Underground
{
    /// <summary>RimWorld owns this parent and its map. Project GU routes resolve it by level identity.</summary>
    public sealed class GuUndergroundLevelParent : MapParent
    {
        internal string LevelId;
        internal MapGeneratorDef Generator;

        public override MapGeneratorDef MapGeneratorDef => Generator ?? base.MapGeneratorDef;

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan) => System.Array.Empty<FloatMenuOption>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref LevelId, "guLevelId");
            Scribe_Defs.Look(ref Generator, "guLevelGenerator");
        }
    }
}
