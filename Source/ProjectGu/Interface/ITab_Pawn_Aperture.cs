using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    public enum ApertureTabPage { Cultivation, Gu, Techniques, Foundation }

    /// <summary>
    /// The Aperture inspection tab. It appears for any pawn with an aperture, human or not. The player's own cultivators
    /// get the full view and their controls; anyone else's aperture shows only its outward rank and stage. Every value is
    /// read from the authoritative aperture, registry, ledger and jobs through <see cref="ApertureView"/>, and every
    /// control runs a <see cref="GuApertureAction"/> that revalidates before it commits. The tab stores nothing in saves.
    /// </summary>
    public sealed partial class ITab_Pawn_Aperture : ITab
    {
        private static readonly Vector2 PreferredSize = new(820f, 600f);
        private const float Margin = 12f;
        private const float HeaderHeight = 54f;
        private const float TabsHeight = 32f;

        internal ApertureTabPage Page { get; set; } = ApertureTabPage.Cultivation;
        internal ApertureView View { get; } = new();
        internal ApertureOrbRenderer Orb { get; } = new();
        /// <summary>Developer-mode preview of a condition overlay. Presentation only; reset when the tab closes.</summary>
        internal GuApertureVitality? DevPreview { get; set; }
        internal string SelectedGuId { get; set; }
        internal int FillCount { get; private set; }

        private readonly List<TabRecord> tabs = new();
        private Vector2 cultivationScroll, guListScroll, guDetailScroll, techniqueScroll, foundationScroll;

        public ITab_Pawn_Aperture()
        {
            labelKey = "GU_ApertureTab";
            tutorTag = "GU_Aperture";
            size = PreferredSize;
        }

        public override bool IsVisible => GuApertureActions.FindAperture(SelPawn) != null;

        public override void OnOpen()
        {
            base.OnOpen();
            View.Invalidate();
            DevPreview = null;
            cultivationScroll = guListScroll = guDetailScroll = techniqueScroll = foundationScroll = Vector2.zero;
        }

        protected override void UpdateSize()
        {
            // Fit the space above the inspection pane at any UI scale; pages scroll when they need more room.
            float availableHeight = Mathf.Max(360f, PaneTopY - 30f - 8f);
            size = new Vector2(Mathf.Min(PreferredSize.x, UI.screenWidth - 8f), Mathf.Min(PreferredSize.y, availableHeight));
        }

        protected override void FillTab()
        {
            FillCount++;
            Pawn pawn = SelPawn;
            View.RefreshIfNeeded(pawn);
            if (!View.Valid) return;

            Rect outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(Margin);
            outer.yMin += 4f;
            DrawHeader(new Rect(outer.x, outer.y, outer.width - 24f, HeaderHeight));

            Rect pageRect = new Rect(outer.x, outer.y + HeaderHeight + TabsHeight, outer.width, outer.height - HeaderHeight - TabsHeight);
            tabs.Clear();
            AddTab("Cultivation", ApertureTabPage.Cultivation);
            AddTab("Gu", ApertureTabPage.Gu);
            AddTab("Techniques", ApertureTabPage.Techniques);
            AddTab("Foundation", ApertureTabPage.Foundation);
            TabDrawer.DrawTabs(pageRect, tabs, 160f);

            ApertureTabStyle.Section(pageRect);
            Rect inner = pageRect.ContractedBy(10f);
            switch (Page)
            {
                case ApertureTabPage.Cultivation: DrawCultivation(inner); break;
                case ApertureTabPage.Gu: DrawGu(inner); break;
                case ApertureTabPage.Techniques: DrawTechniques(inner); break;
                case ApertureTabPage.Foundation: DrawFoundation(inner); break;
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void AddTab(string label, ApertureTabPage page) =>
            tabs.Add(new TabRecord(label, () => Page = page, Page == page));

        private void DrawHeader(Rect rect)
        {
            var view = View;
            string name = view.Pawn.LabelShortCap;
            string standing = view.RankDef?.implemented == false ? $"Rank {view.Rank}" : $"Rank {view.Rank} · {StageLabel(view)} Stage";
            ApertureTabStyle.Label(new Rect(rect.x, rect.y, rect.width, 32f), $"{name}'s aperture", ApertureTabStyle.Heading, GameFont.Medium);
            string sub = standing + (view.RankDef != null ? $"  —  {view.RankDef.EssenceLabelCap}" : "");
            if (view.Disclosure == ApertureDisclosure.Observed) sub += "  (outward observation only)";
            else if (!view.CanManage) sub += "  (read-only)";
            ApertureTabStyle.Label(new Rect(rect.x, rect.y + 30f, rect.width, 24f), sub, ApertureTabStyle.Muted);
        }

        internal static string StageLabel(ApertureView view) =>
            view.RankDef?.Stage(view.Stage)?.label ?? ((Domain.RankOneStage)view.Stage).ToString();

        /// <summary>Runs an action, reports a failure the way the menus do, and refreshes the view at once.</summary>
        private void Run(GuApertureAction action, string successMessage = null)
        {
            GuApertureActions.Run(action, successMessage);
            View.Invalidate();
        }

        private bool Concealed(Rect rect)
        {
            if (View.Disclosure == ApertureDisclosure.Full) return false;
            ApertureTabStyle.Label(rect, "This aperture belongs to someone outside your clan. Only its outward rank and stage can be perceived.",
                ApertureTabStyle.Muted, GameFont.Small, TextAnchor.MiddleCenter);
            return true;
        }
    }
}
