using System;
using RimWorld;
using Verse;

namespace ProjectGu.Underground
{
    internal static class GuSpringCavernIncidents
    {
        internal static bool GuardsInstalled() =>
            UsesGuard("Infestation", typeof(IncidentWorker_GuInfestation))
            && UsesGuard("DeepDrillInfestation", typeof(IncidentWorker_GuDeepDrillInfestation))
            && (!ModsConfig.BiotechActive
                || UsesGuard("WastepackInfestation", typeof(IncidentWorker_GuWastepackInfestation)));

        private static bool UsesGuard(string name, Type guard) =>
            DefDatabase<IncidentDef>.GetNamedSilentFail(name)?.workerClass == guard;

        internal static bool Allows(IncidentParms parms) => !GuPocketSiteDef.IsSpringCavern(parms.target as Map);
    }

    public sealed class IncidentWorker_GuInfestation : IncidentWorker_Infestation
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            GuSpringCavernIncidents.Allows(parms) && base.CanFireNowSub(parms);

        protected override bool TryExecuteWorker(IncidentParms parms) =>
            GuSpringCavernIncidents.Allows(parms) && base.TryExecuteWorker(parms);
    }

    public sealed class IncidentWorker_GuDeepDrillInfestation : IncidentWorker_DeepDrillInfestation
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            GuSpringCavernIncidents.Allows(parms) && base.CanFireNowSub(parms);

        protected override bool TryExecuteWorker(IncidentParms parms) =>
            GuSpringCavernIncidents.Allows(parms) && base.TryExecuteWorker(parms);
    }

    public sealed class IncidentWorker_GuWastepackInfestation : IncidentWorker_WastepackInfestation
    {
        protected override bool CanFireNowSub(IncidentParms parms) =>
            GuSpringCavernIncidents.Allows(parms) && base.CanFireNowSub(parms);

        protected override bool TryExecuteWorker(IncidentParms parms) =>
            GuSpringCavernIncidents.Allows(parms) && base.TryExecuteWorker(parms);
    }
}
