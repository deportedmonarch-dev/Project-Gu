using System.Linq;
using ProjectGu.Domain;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    public sealed partial class ITab_Pawn_Aperture
    {
        private const float CardHeight = 78f;
        private float guDetailHeight = 500f;

        private void DrawGu(Rect rect)
        {
            if (Concealed(rect)) return;
            var view = View;
            float listWidth = Mathf.Min(320f, rect.width * 0.42f);
            Rect header = new Rect(rect.x, rect.y, listWidth, 24f);
            ApertureTabStyle.Label(header, $"Aperture Gu: {view.Claims}/{view.ClaimCapacity} places", ApertureTabStyle.Heading);
            Rect list = new Rect(rect.x, header.yMax + 4f, listWidth, rect.height - header.height - 4f);
            Rect detail = new Rect(list.xMax + 12f, rect.y, rect.xMax - list.xMax - 12f, rect.height);

            if (view.Gu.Count == 0)
            {
                ApertureTabStyle.Label(list, "No Gu are stored in this aperture or carried by this cultivator.", ApertureTabStyle.Muted);
                DrawFeedingReserve(detail, null);
                return;
            }
            if (SelectedGuId == null || view.Gu.All(entry => entry.Record.Id != SelectedGuId)) SelectedGuId = view.Gu[0].Record.Id;

            Rect viewRect = new Rect(0f, 0f, list.width - 16f, view.Gu.Count * (CardHeight + 6f));
            Widgets.BeginScrollView(list, ref guListScroll, viewRect);
            float y = 0f;
            foreach (var entry in view.Gu)
            {
                Rect card = new Rect(0f, y, viewRect.width, CardHeight);
                DrawCard(card, entry, entry.Record.Id == SelectedGuId);
                if (Widgets.ButtonInvisible(card)) SelectedGuId = entry.Record.Id;
                y += CardHeight + 6f;
            }
            Widgets.EndScrollView();

            var selected = view.Gu.FirstOrDefault(entry => entry.Record.Id == SelectedGuId);
            if (selected != null) DrawGuDetail(detail, selected);
        }

        private static void DrawCard(Rect rect, ApertureGuEntry entry, bool selected)
        {
            ApertureTabStyle.Section(rect);
            if (selected) Widgets.DrawBoxSolid(rect, ApertureTabStyle.Selected);
            Widgets.DrawHighlightIfMouseover(rect);
            var record = entry.Record;
            Rect portrait = new Rect(rect.x + 6f, rect.y + 7f, 64f, 64f);
            Widgets.DrawBoxSolid(portrait, new Color(0f, 0f, 0f, 0.35f));
            GUI.DrawTexture(portrait.ContractedBy(2f), GuPortraits.For(record.Species), ScaleMode.ScaleToFit);
            float x = portrait.xMax + 8f, w = rect.xMax - x - 6f;
            ApertureTabStyle.Label(new Rect(x, rect.y + 6f, w, 22f), record.Species?.LabelCap ?? record.SpeciesDefName, ApertureTabStyle.Heading);
            string rank = $"Rank {record.Rank} Gu" + (record.Species?.role.NullOrEmpty() == false ? $" · {record.Species.role}" : "");
            ApertureTabStyle.Label(new Rect(x, rect.y + 27f, w, 20f), rank, ApertureTabStyle.Muted, GameFont.Tiny);
            ApertureTabStyle.Label(new Rect(x, rect.y + 44f, w, 30f), $"{PlacementLabel(entry)} · {RecognitionLabel(entry)}",
                entry.RecognizesHost ? ApertureTabStyle.Accent : ApertureTabStyle.Body, GameFont.Tiny);
        }

        private void DrawGuDetail(Rect rect, ApertureGuEntry entry)
        {
            ApertureTabStyle.Section(rect);
            Rect inner = rect.ContractedBy(10f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, guDetailHeight);
            Widgets.BeginScrollView(inner, ref guDetailScroll, viewRect);
            var record = entry.Record;
            Rect c = new Rect(0f, 0f, viewRect.width, 0f);
            Rect portrait = new Rect(0f, 0f, 96f, 96f);
            Widgets.DrawBoxSolid(portrait, new Color(0f, 0f, 0f, 0.35f));
            GUI.DrawTexture(portrait.ContractedBy(3f), GuPortraits.For(record.Species), ScaleMode.ScaleToFit);
            Rect title = new Rect(portrait.xMax + 10f, 0f, c.width - portrait.width - 10f, 96f);
            float ty = title.y;
            ApertureTabStyle.Line(title, ref ty, record.Species?.LabelCap ?? record.SpeciesDefName, ApertureTabStyle.Heading, GameFont.Medium);
            ApertureTabStyle.Line(title, ref ty, $"Rank {record.Rank} Gu" + (record.Species?.role.NullOrEmpty() == false ? $" · {record.Species.role}" : ""), ApertureTabStyle.Muted);
            ApertureTabStyle.Line(title, ref ty, $"Individual {ShortId(record.Id)}", ApertureTabStyle.Faint, GameFont.Tiny);
            float y = Mathf.Max(portrait.yMax, ty) + 8f;

            if (record.Species?.description is { Length: > 0 } description) ApertureTabStyle.Line(c, ref y, description, ApertureTabStyle.Body, GameFont.Small, 6f);
            Fact(c, ref y, "Recognition", RecognitionDetail(entry));
            Fact(c, ref y, "Custody", PlacementDetail(entry));
            Fact(c, ref y, "Condition", record.Condition == GuCondition.Dormant ? "Dormant" : "Active");
            if (record.RefinementProcess is { } process)
                Fact(c, ref y, "Will-enforcement", $"{100f * process.CompletedWorkTicks / Mathf.Max(1, process.TotalWorkTicks):0}% complete, "
                    + $"{process.EssenceSpent:0.#} of {process.TotalEssenceCost:0.#} essence spent");
            Fact(c, ref y, "Grants", GrantsLabel(entry));
            Fact(c, ref y, "Identity", $"{record.Id} (custody revision {record.CustodyRevision})", ApertureTabStyle.Faint);

            y += 6f;
            if (View.CanManage)
            {
                foreach (var action in entry.Actions)
                {
                    string label = ActionLabel(action);
                    if (ApertureTabStyle.ActionButton(new Rect(c.x, y, Mathf.Min(c.width, 300f), 30f), label, action.DisabledReason, action.Label))
                        Run(action, SuccessMessage(action, record));
                    y += 34f;
                    if (action.DisabledReason != null)
                        ApertureTabStyle.Line(c, ref y, "Unavailable: " + action.DisabledReason.CapitalizeFirst() + ".", ApertureTabStyle.Warning, GameFont.Tiny);
                }
                if (entry.Actions.Count == 0) ApertureTabStyle.Line(c, ref y, "No action is available for this Gu right now.", ApertureTabStyle.Faint, GameFont.Tiny);
            }
            y += 6f;
            DrawFeedingReserveInline(c, ref y);
            guDetailHeight = Mathf.Max(y + 8f, inner.height);
            Widgets.EndScrollView();
        }

        private static void Fact(Rect c, ref float y, string label, string value, Color? color = null)
        {
            const float labelWidth = 120f;
            Text.Font = GameFont.Small;
            float height = Mathf.Max(22f, Text.CalcHeight(value, c.width - labelWidth));
            ApertureTabStyle.Label(new Rect(c.x, y, labelWidth, height), label, ApertureTabStyle.Muted);
            ApertureTabStyle.Label(new Rect(c.x + labelWidth, y, c.width - labelWidth, height), value, color ?? ApertureTabStyle.Body);
            y += height + 2f;
        }

        /// <summary>Space reserved for the future Gu-feeding system. Nothing here is simulated yet.</summary>
        private static void DrawFeedingReserveInline(Rect c, ref float y)
        {
            Rect box = new Rect(c.x, y, c.width, 44f);
            Widgets.DrawBoxSolid(box, new Color(1f, 1f, 1f, 0.03f));
            GUI.color = ApertureTabStyle.PanelEdge;
            Widgets.DrawBox(box);
            GUI.color = Color.white;
            ApertureTabStyle.Label(box.ContractedBy(6f), "Nourishment and health\nGu feeding is not yet part of cultivation.", ApertureTabStyle.Faint, GameFont.Tiny);
            y += box.height + 4f;
        }

        private static void DrawFeedingReserve(Rect rect, ApertureGuEntry _)
        {
            float y = rect.y;
            DrawFeedingReserveInline(rect, ref y);
        }

        private static string PlacementLabel(ApertureGuEntry entry) => entry.Placement switch
        {
            GuApertureGuPlacement.Aperture => "In aperture",
            GuApertureGuPlacement.Inventory => "Carried",
            _ => "Nearby"
        };

        private static string PlacementDetail(ApertureGuEntry entry) => entry.Placement switch
        {
            GuApertureGuPlacement.Aperture => "Stored in this aperture. A stored Gu has no physical body.",
            GuApertureGuPlacement.Inventory => "Dormant, carried in this cultivator's inventory.",
            _ => entry.Thing?.Spawned == true ? $"Dormant, on the ground nearby ({entry.Thing.Position})." : "Dormant, nearby."
        };

        private static string RecognitionLabel(ApertureGuEntry entry)
        {
            if (entry.Record.RefinementProcess != null) return "being refined";
            if (entry.RecognizesHost) return "recognizes this cultivator";
            return entry.OwnerName != null ? "bonded elsewhere" : "unrefined";
        }

        private static string RecognitionDetail(ApertureGuEntry entry)
        {
            if (entry.RecognizesHost) return "Refined: it recognizes this cultivator as its master.";
            if (entry.OwnerName != null) return $"Refined by {entry.OwnerName}; it does not answer to this cultivator.";
            return "Unrefined: its own will still resists. Enforce your will over it to establish recognition.";
        }

        private string GrantsLabel(ApertureGuEntry entry)
        {
            var abilities = entry.Record.Species?.abilities;
            if (abilities == null || abilities.Count == 0)
                return entry.Record.Species?.effectNotes?.Count > 0 ? "No technique; see the Techniques page." : "No technique.";
            string names = string.Join(", ", abilities.Where(ability => ability != null).Select(ability => ability.LabelCap.ToString()));
            bool granting = entry.RecognizesHost && entry.Placement == GuApertureGuPlacement.Aperture && entry.Record.RefinementProcess == null;
            return granting ? $"{names} (available)" : $"{names} (only while refined by and stored in its master's aperture)";
        }

        private static string ActionLabel(GuApertureAction action) => action.Kind switch
        {
            GuApertureActionKind.Store => "Store in aperture",
            GuApertureActionKind.Withdraw => "Withdraw to inventory",
            GuApertureActionKind.EnforceWill => "Enforce will",
            GuApertureActionKind.ResumeWill => "Resume will-enforcement",
            GuApertureActionKind.AbandonWill => "Abandon will-enforcement",
            _ => action.Label
        };

        private static string SuccessMessage(GuApertureAction action, GuRecord record) => action.Kind switch
        {
            GuApertureActionKind.Store => $"Stored {GuApertureActions.Describe(record)} in the aperture.",
            GuApertureActionKind.Withdraw => $"Withdrew {GuApertureActions.Describe(record)} to inventory.",
            GuApertureActionKind.AbandonWill => "The refinement attempt was abandoned. Its essence remains spent; Gu identity, custody and allegiance are unchanged.",
            _ => null
        };

        private static string ShortId(string id) => id == null ? "?" : id.Length > 8 ? id.Substring(0, 8) : id;
    }
}
