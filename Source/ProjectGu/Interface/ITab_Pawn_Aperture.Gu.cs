using System.Linq;
using ProjectGu.Domain;
using RimWorld;
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
            float listTop = header.yMax + 4f;
            if (view.CanManage)
            {
                // Player feeding policy for this cultivator: tend their own hungry, refined Gu when food is at hand.
                Rect toggle = new Rect(rect.x, listTop, listWidth, 24f);
                bool autoTend = view.AutoTend;
                Widgets.CheckboxLabeled(toggle, "Feed own Gu automatically", ref autoTend);
                TooltipHandler.TipRegion(toggle, "When on, this Gu Master feeds their own hungry, refined Gu from their inventory or this map's stockpiles "
                    + "while choosing a new job. It never interrupts a running job, combat or a drafted order.");
                if (autoTend != view.AutoTend)
                {
                    Sustenance.GuSustenanceComponent.Instance?.SetAutoTend(view.Pawn, autoTend);
                    view.Invalidate();
                }
                listTop = toggle.yMax + 4f;
            }
            Rect list = new Rect(rect.x, listTop, listWidth, rect.yMax - listTop);
            Rect detail = new Rect(list.xMax + 12f, rect.y, rect.xMax - list.xMax - 12f, rect.height);

            if (view.Gu.Count == 0)
            {
                ApertureTabStyle.Label(list, "No Gu are stored in this aperture or carried by this cultivator.", ApertureTabStyle.Muted);
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
            ApertureTabStyle.Label(new Rect(x, rect.y + 42f, w, 18f), $"{PlacementLabel(entry)} · {RecognitionLabel(entry)}",
                entry.RecognizesHost ? ApertureTabStyle.Accent : ApertureTabStyle.Body, GameFont.Tiny);
            var n = entry.Nourishment;
            if (n.Simulated)
            {
                // A thin meter and the level, so a hungry Gu stands out in the list before it is selected.
                Rect meter = new Rect(x, rect.y + 62f, Mathf.Max(20f, w - 70f), 6f);
                ApertureTabStyle.Bar(meter, n.Fraction, LevelColor(n.Level));
                ApertureTabStyle.Label(new Rect(meter.xMax + 6f, rect.y + 56f, 64f, 18f), LevelLabel(n.Level),
                    n.Level >= GuNourishmentLevel.Hungry ? LevelColor(n.Level) : ApertureTabStyle.Muted, GameFont.Tiny);
            }
            else ApertureTabStyle.Label(new Rect(x, rect.y + 58f, w, 18f), n.Status == GuFeedingStatus.Exempt ? "Exempt from feeding" : "No feeding policy",
                ApertureTabStyle.Faint, GameFont.Tiny);
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
            // Nourishment first: it is the state a keeper checks most often, and its Feed button must not need scrolling.
            DrawNourishment(c, ref y, entry);
            y += 4f;

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

        /// <summary>
        /// The selected Gu's nourishment: level, meter, time until the next threshold, accepted food with what is at hand,
        /// any starvation effect and the Feed action. Every value comes from the view's snapshot of the canonical record.
        /// </summary>
        private void DrawNourishment(Rect c, ref float y, ApertureGuEntry entry)
        {
            var n = entry.Nourishment;
            float top = y;
            Rect inner = new Rect(c.x + 8f, y + 6f, c.width - 16f, 0f);
            float iy = inner.y;
            ApertureTabStyle.Line(inner, ref iy, "Nourishment", ApertureTabStyle.Heading);
            if (!n.Simulated)
            {
                string text = n.Status == GuFeedingStatus.Exempt
                    ? "Exempt from feeding. " + (entry.Record.Species?.feeding?.note ?? "")
                    : entry.Record.Species?.feeding?.note ?? $"No individual feeding policy is implemented for {entry.Record.Species?.label ?? "this Gu"}. It is not fed and does not starve.";
                ApertureTabStyle.Line(inner, ref iy, text, ApertureTabStyle.Muted, GameFont.Tiny);
            }
            else
            {
                Color tone = LevelColor(n.Level);
                ApertureTabStyle.Line(inner, ref iy, $"{LevelLabel(n.Level)} · {NourishmentDays(n.NourishmentTicks)} of {NourishmentDays(n.MaxTicks)} stored", tone);
                ApertureTabStyle.Bar(new Rect(inner.x, iy, inner.width, 12f), n.Fraction, tone);
                // Hunger threshold marker.
                float mark = inner.x + inner.width * Mathf.Clamp01(n.MaxTicks <= 0 ? 0f : (float)n.HungryTicks / n.MaxTicks);
                Widgets.DrawBoxSolid(new Rect(mark - 1f, iy - 2f, 2f, 16f), ApertureTabStyle.Muted);
                iy += 16f;
                ApertureTabStyle.Line(inner, ref iy, TimingLine(n), n.Level >= GuNourishmentLevel.Starving ? ApertureTabStyle.Warning : ApertureTabStyle.Body, GameFont.Tiny);
                if (EffectLine(n, entry.Record) is { } effect) ApertureTabStyle.Line(inner, ref iy, effect, ApertureTabStyle.Warning, GameFont.Tiny);
                if (n.Mode != GuSustenanceMode.Advancing) ApertureTabStyle.Line(inner, ref iy, PausedLine(n.Mode), ApertureTabStyle.Muted, GameFont.Tiny);
                if (n.InGrace) ApertureTabStyle.Line(inner, ref iy, "Grace period: hunger has not yet begun for this Gu after the feeding system arrived in this save.", ApertureTabStyle.Muted, GameFont.Tiny);
                iy += 2f;
                foreach (var (option, available) in entry.Foods)
                {
                    Rect row = new Rect(inner.x, iy, inner.width, 28f);
                    Rect icon = new Rect(row.x, row.y + 2f, 24f, 24f);
                    Widgets.ThingIcon(icon, option.thingDef);
                    string have = View.CanManage ? $" · {available} at hand" : "";
                    ApertureTabStyle.Label(new Rect(icon.xMax + 6f, row.y, row.width - 30f, 28f),
                        $"{option.mealCount} {option.Label} per meal, {NourishmentDays(option.TicksPerMeal)} each{have}",
                        available >= option.mealCount || !View.CanManage ? ApertureTabStyle.Body : ApertureTabStyle.Warning, GameFont.Tiny, TextAnchor.MiddleLeft);
                    TooltipHandler.TipRegion(row, option.thingDef.LabelCap + (option.note.NullOrEmpty() ? "" : "\n" + option.note));
                    iy += 28f;
                }
                if (entry.Record.Species?.feeding?.note is { Length: > 0 } diet) ApertureTabStyle.Line(inner, ref iy, diet, ApertureTabStyle.Faint, GameFont.Tiny);
                if (View.CanManage && entry.FeedAction is { } feed)
                {
                    iy += 4f;
                    if (ApertureTabStyle.ActionButton(new Rect(inner.x, iy, Mathf.Min(inner.width, 300f), 30f), feed.Label, feed.DisabledReason, feed.Tooltip))
                        Run(feed);
                    iy += 34f;
                    if (feed.DisabledReason != null)
                        ApertureTabStyle.Line(inner, ref iy, "Unavailable: " + feed.DisabledReason.CapitalizeFirst() + ".", ApertureTabStyle.Muted, GameFont.Tiny);
                }
            }
            Rect box = new Rect(c.x, top, c.width, iy - top + 6f);
            GUI.color = ApertureTabStyle.PanelEdge;
            Widgets.DrawBox(box);
            GUI.color = Color.white;
            y = box.yMax + 4f;
        }

        internal static string LevelLabel(GuNourishmentLevel level) => level switch
        {
            GuNourishmentLevel.Sated => "Sated",
            GuNourishmentLevel.Hungry => "Hungry",
            GuNourishmentLevel.Starving => "Starving",
            GuNourishmentLevel.Withering => "Withering",
            GuNourishmentLevel.Exempt => "Exempt",
            _ => "Not fed"
        };

        private static Color LevelColor(GuNourishmentLevel level) => level switch
        {
            GuNourishmentLevel.Sated => ApertureTabStyle.Accent,
            GuNourishmentLevel.Hungry => new Color(0.85f, 0.78f, 0.45f),
            GuNourishmentLevel.Starving => ApertureTabStyle.Warning,
            GuNourishmentLevel.Withering => new Color(0.86f, 0.42f, 0.36f),
            _ => ApertureTabStyle.Muted
        };

        internal static string NourishmentDays(int ticks) => ticks >= GenDate.TicksPerDay
            ? $"{ticks / (float)GenDate.TicksPerDay:0.#} days" : $"{ticks / (float)GenDate.TicksPerHour:0} hours";

        private static string TimingLine(GuNourishmentSnapshot n) => n.Level switch
        {
            GuNourishmentLevel.Sated => $"Hungry in about {NourishmentDays(n.TicksUntilHungry)}; out of food in {NourishmentDays(n.TicksUntilStarving)}.",
            GuNourishmentLevel.Hungry => $"Feed it soon: out of food in about {NourishmentDays(n.TicksUntilStarving)}.",
            GuNourishmentLevel.Starving => $"Starving for {NourishmentDays(n.StarvingTicks)}. Withers in {NourishmentDays(n.TicksUntilImpaired)}, dies in {NourishmentDays(n.TicksUntilDeath)} unless fed.",
            GuNourishmentLevel.Withering => $"Starving for {NourishmentDays(n.StarvingTicks)}. Dies in about {NourishmentDays(n.TicksUntilDeath)} unless fed.",
            _ => ""
        };

        private static string EffectLine(GuNourishmentSnapshot n, GuRecord record)
        {
            if (n.Level == GuNourishmentLevel.Withering)
                return record.Species?.abilities is { Count: > 0 }
                    ? "Withering: it cannot act for its master. Its techniques are unavailable until it is fed."
                    : "Withering: it is too weak to serve its master until it is fed.";
            if (n.Level == GuNourishmentLevel.Starving)
                return "Starving: it still serves, but it will wither if it is not fed.";
            return null;
        }

        private static string PausedLine(GuSustenanceMode mode) => mode switch
        {
            GuSustenanceMode.Wild => "Hunger is paused: a wild Gu is sustained by its habitat.",
            GuSustenanceMode.OffMap => "Hunger is paused while away from any map. Nothing is eaten and nothing is restored.",
            GuSustenanceMode.OtherFaction => "Hunger is paused while held by another faction.",
            GuSustenanceMode.DeceasedHost => "Hunger is paused while it remains in a dead host's aperture.",
            _ => "Hunger is paused while this Gu is unavailable."
        };

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
            if (granting && entry.Nourishment.Impaired) return $"{names} (unavailable while it is withering from starvation)";
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
