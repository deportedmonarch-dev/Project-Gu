using System;
using System.Collections.Generic;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>
    /// Adds the Aperture tab to every pawn race at startup, so any species that can carry an aperture shows it. The tab
    /// hides itself for pawns without an aperture, so other pawns see no change.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ApertureTabInstaller
    {
        public static int RacesWithTab { get; }

        static ApertureTabInstaller()
        {
            try
            {
                var tab = InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Aperture));
                foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def.race == null || def.category != ThingCategory.Pawn) continue;
                    def.inspectorTabs ??= new List<Type>();
                    if (!def.inspectorTabs.Contains(typeof(ITab_Pawn_Aperture))) def.inspectorTabs.Add(typeof(ITab_Pawn_Aperture));
                    def.inspectorTabsResolved ??= new List<InspectTabBase>();
                    if (!def.inspectorTabsResolved.Contains(tab)) def.inspectorTabsResolved.Add(tab);
                    RacesWithTab++;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] The Aperture tab could not be added to pawn races. The Manage aperture Gu command remains available. {exception}");
            }
        }
    }
}
