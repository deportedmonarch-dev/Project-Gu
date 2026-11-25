using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>Restrained palette and small drawing helpers for the Aperture tab.</summary>
    [StaticConstructorOnStartup]
    internal static class ApertureTabStyle
    {
        internal static readonly Color Panel = new(0.075f, 0.085f, 0.08f, 0.92f);
        internal static readonly Color PanelEdge = new(0.30f, 0.36f, 0.32f, 0.55f);
        internal static readonly Color Heading = new(0.86f, 0.90f, 0.82f);
        internal static readonly Color Body = new(0.80f, 0.82f, 0.78f);
        internal static readonly Color Muted = new(0.58f, 0.61f, 0.57f);
        internal static readonly Color Faint = new(0.42f, 0.45f, 0.42f);
        internal static readonly Color Accent = new(0.55f, 0.80f, 0.62f);
        internal static readonly Color Warning = new(0.88f, 0.68f, 0.40f);
        internal static readonly Color BarBack = new(0.12f, 0.13f, 0.12f, 1f);
        internal static readonly Color Selected = new(0.55f, 0.80f, 0.62f, 0.14f);

        internal static void Section(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            GUI.color = PanelEdge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        internal static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = color;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        /// <summary>A label that advances <paramref name="y"/> by its wrapped height.</summary>
        internal static void Line(Rect column, ref float y, string text, Color color, GameFont font = GameFont.Small, float gap = 2f)
        {
            var oldFont = Text.Font;
            Text.Font = font;
            float height = Text.CalcHeight(text, column.width);
            Text.Font = oldFont;
            Label(new Rect(column.x, y, column.width, height), text, color, font);
            y += height + gap;
        }

        internal static void Bar(Rect rect, float fraction, Color fill)
        {
            Widgets.DrawBoxSolid(rect, BarBack);
            if (fraction > 0f) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), fill);
            GUI.color = PanelEdge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        internal static void Swatch(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(rect, color);
            GUI.color = PanelEdge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        /// <summary>A button that is drawn inactive with a tooltip explaining why when the action is unavailable.</summary>
        internal static bool ActionButton(Rect rect, string label, string disabledReason, string tooltip = null)
        {
            bool active = disabledReason == null;
            string tip = active ? tooltip : (tooltip.NullOrEmpty() ? "" : tooltip + "\n\n") + "Unavailable: " + disabledReason.CapitalizeFirst();
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tip);
            if (active) return Widgets.ButtonText(rect, label, true, true, true);
            // Unavailable: drawn visibly dimmed so it never reads as clickable.
            var old = GUI.color;
            GUI.color = new Color(old.r * 0.55f, old.g * 0.55f, old.b * 0.55f, old.a);
            Widgets.ButtonText(rect, label, true, false, Muted, false);
            GUI.color = old;
            return false;
        }
    }
}
