using ProjectGu.Presentation;
using UnityEngine;
using Verse;

namespace ProjectGu
{
    /// <summary>Player options. Presentation settings only; no option changes gameplay outcomes.</summary>
    public sealed class ProjectGuSettings : ModSettings
    {
        public bool cosmeticEffects = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cosmeticEffects, "cosmeticEffects", true);
        }
    }

    public sealed class ProjectGuMod : Mod
    {
        internal static ProjectGuSettings Settings { get; private set; }

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
            list.End();
            if (before && !Settings.cosmeticEffects && Current.Game != null)
                foreach (var map in Find.Maps) map.GetComponent<GuFxMapComponent>()?.Clear();
        }
    }
}
