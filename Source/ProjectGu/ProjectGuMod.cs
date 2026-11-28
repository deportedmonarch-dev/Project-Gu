using ProjectGu.Presentation;
using UnityEngine;
using Verse;

namespace ProjectGu
{
    /// <summary>Player presentation options and encounter retention intervals.</summary>
    public sealed class ProjectGuSettings : ModSettings
    {
        public bool cosmeticEffects = true;
        public bool ambientSpirits = true;
        public int encounterQuietDays = 1;
        public int encounterHistoryDays = 60;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cosmeticEffects, "cosmeticEffects", true);
            Scribe_Values.Look(ref ambientSpirits, "ambientSpirits", true);
            Scribe_Values.Look(ref encounterQuietDays, "encounterQuietDays", 1);
            Scribe_Values.Look(ref encounterHistoryDays, "encounterHistoryDays", 60);
        }
    }

    public sealed class ProjectGuMod : Mod
    {
        internal static ProjectGuSettings Settings { get; private set; }
        private string quietDaysInput;
        private string historyDaysInput;

        public ProjectGuMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ProjectGuSettings>();
        }

        public override string SettingsCategory() => "Project GU";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            bool before = Settings.cosmeticEffects;
            list.CheckboxLabeled("Gu combat visual effects", ref Settings.cosmeticEffects,
                "Draw launch, travel and impact effects for Gu abilities. Projectiles, damage, costs and cooldowns are identical either way.");
            list.CheckboxLabeled("Ambient spirits at spirit springs", ref Settings.ambientSpirits,
                "Draw the softly glowing Hope Gu that drift around spirit springs. Their number follows the spring's real Hope Gu population; they are scenery only and cannot be selected or captured. Encounters, populations and awakening are identical either way.");
            list.TextFieldNumericLabeled("Wild Gu out-of-sight days", ref Settings.encounterQuietDays, ref quietDaysInput, 1, 600);
            list.TextFieldNumericLabeled("Wild Gu historical significance days", ref Settings.encounterHistoryDays, ref historyDaysInput, 1, 600);
            list.End();
            if (before && !Settings.cosmeticEffects && Current.Game != null)
                foreach (var map in Find.Maps) map.GetComponent<GuFxMapComponent>()?.Clear();
        }
    }
}
