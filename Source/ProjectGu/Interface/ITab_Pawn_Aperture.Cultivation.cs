using System;
using System.Collections.Generic;
using ProjectGu.Combat;
using ProjectGu.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    public sealed partial class ITab_Pawn_Aperture
    {
        private const float RowHeight = 24f;
        private float cultivationContentHeight = 600f;
        private int tooltipVersion = -1;
        private string orbTip, compositionTip;

        /// <summary>Tooltip text is rebuilt only when the view refreshes, not on every GUI event.</summary>
        private void RefreshTooltips(ApertureView view)
        {
            if (tooltipVersion == view.Version) return;
            tooltipVersion = view.Version;
            orbTip = OrbTooltip(view);
            compositionTip = CompositionText(view);
        }

        private void DrawCultivation(Rect rect)
        {
            var view = View;
            float orbSize = Mathf.Min(rect.height - 52f, rect.width * 0.44f);
            Rect orbRect = new Rect(rect.x, rect.y, orbSize, orbSize);
            var preview = DevPreview is { } vitality && Prefs.DevMode
                ? new GuApertureVisualState(vitality, 0.75f, view.Activity, true) : (GuApertureVisualState?)null;
            Orb.Draw(orbRect, view.OrbInput(preview));
            RefreshTooltips(view);
            TooltipHandler.TipRegion(orbRect, orbTip);
            if (preview != null)
                ApertureTabStyle.Label(new Rect(orbRect.x, orbRect.y, orbRect.width, 20f), $"Developer preview: {DevPreview}",
                    ApertureTabStyle.Warning, GameFont.Tiny, TextAnchor.UpperCenter);

            Rect under = new Rect(rect.x, orbRect.yMax + 6f, orbSize, rect.yMax - orbRect.yMax - 6f);
            if (view.Disclosure == ApertureDisclosure.Full) DrawComposition(under, view, compositionTip);
            else ApertureTabStyle.Label(under, "The primeval sea is concealed.", ApertureTabStyle.Faint, GameFont.Tiny, TextAnchor.UpperCenter);

            Rect column = new Rect(orbRect.xMax + 16f, rect.y, rect.xMax - orbRect.xMax - 16f, rect.height);
            if (view.Disclosure != ApertureDisclosure.Full)
            {
                float oy = column.y;
                ApertureTabStyle.Line(column, ref oy, $"Rank {view.Rank} {StageLabel(view)} Stage", ApertureTabStyle.Heading, GameFont.Medium);
                Concealed(new Rect(column.x, oy, column.width, 80f));
                return;
            }

            Rect viewRect = new Rect(0f, 0f, column.width - 16f, cultivationContentHeight);
            Widgets.BeginScrollView(column, ref cultivationScroll, viewRect);
            float y = 0f;
            Rect c = new Rect(0f, 0f, viewRect.width, 0f);
            DrawSea(c, ref y, view);
            y += 8f;
            DrawWalls(c, ref y, view);
            y += 8f;
            DrawActivityAndActions(c, ref y, view);
            y += 8f;
            DrawCultivationChannels(c, ref y, view);
            y += 8f;
            DrawBodilyAttributes(c, ref y, view.Pawn);
            if (Prefs.DevMode) DrawDevPreview(c, ref y);
            cultivationContentHeight = Mathf.Max(y + 8f, column.height);
            Widgets.EndScrollView();
        }

        private void DrawSea(Rect c, ref float y, ApertureView view)
        {
            ApertureTabStyle.Line(c, ref y, "Primeval sea", ApertureTabStyle.Heading, GameFont.Medium);
            ApertureTabStyle.Line(c, ref y, $"{view.Total:0.##} / {view.Capacity:0} primeval essence  ({Percent(view.Total, view.Capacity)} of the aperture)",
                ApertureTabStyle.Body);
            int qualities = 0;
            for (int q = 0; q < PrimevalSeaLayout.Qualities; q++)
            {
                double amount = view.Amounts[q];
                if (amount > 0) qualities++;
                Rect row = new Rect(c.x, y, c.width, RowHeight - 4f);
                ApertureTabStyle.Swatch(new Rect(row.x + 2f, row.y + 3f, 14f, 14f), view.RankDef?.StageColor(q) ?? Color.gray);
                Color text = amount > 0 ? ApertureTabStyle.Body : ApertureTabStyle.Faint;
                string label = $"{view.RankDef?.Stage(q)?.label ?? ((RankOneStage)q).ToString()}-quality essence";
                if (q == view.Stage) label += " (your stage)";
                ApertureTabStyle.Label(new Rect(row.x + 24f, row.y, row.width - 110f, row.height), label, text);
                ApertureTabStyle.Label(new Rect(row.xMax - 90f, row.y, 86f, row.height), amount.ToString("0.##"), text, GameFont.Small, TextAnchor.UpperRight);
                y += RowHeight - 4f;
            }
            y += 4f;
            if (qualities > 1)
                ApertureTabStyle.Line(c, ref y, $"The sea holds essence of {qualities} qualities, each kept separately. Your minor stage is {StageLabel(view)}: "
                    + "it sets the quality of newly recovered essence, and holding essence of another quality never changes it.", ApertureTabStyle.Muted, GameFont.Tiny);
            string recovery = view.Total >= view.Capacity - 0.0001
                ? "The sea is full; natural recovery waits for space."
                : $"Natural recovery: {view.Aperture.RegenerationPerHour:0.#} essence per hour, as {StageLabel(view)}-quality essence.";
            ApertureTabStyle.Line(c, ref y, recovery, ApertureTabStyle.Muted, GameFont.Tiny);
        }

        private void DrawWalls(Rect c, ref float y, ApertureView view)
        {
            ApertureTabStyle.Line(c, ref y, "Aperture walls", ApertureTabStyle.Heading, GameFont.Medium);
            if (view.Rank != 1)
            {
                ApertureTabStyle.Line(c, ref y, "Cultivation beyond Rank 1 is not yet possible.", ApertureTabStyle.Muted);
                return;
            }
            if (view.AtPeak)
            {
                ApertureTabStyle.Bar(new Rect(c.x, y, c.width, 14f), 1f, view.RankDef?.StageColor(view.Stage) ?? ApertureTabStyle.Accent);
                y += 18f;
                ApertureTabStyle.Line(c, ref y, "Rank 1 Peak: the aperture walls hold no further growth at this rank.", ApertureTabStyle.Body);
                return;
            }
            float fraction = PrimevalSeaLayout.WallArc(view.WallNourishment, view.WallRequired);
            ApertureTabStyle.Bar(new Rect(c.x, y, c.width, 14f), fraction, view.RankDef?.StageColor(view.Stage) ?? ApertureTabStyle.Accent);
            y += 18f;
            string next = view.RankDef?.Stage(view.Stage + 1)?.label ?? ((RankOneStage)(view.Stage + 1)).ToString();
            ApertureTabStyle.Line(c, ref y, $"{view.WallNourishment:0.##} / {view.WallRequired:0} nourishment toward {next} Stage ({fraction:P0})"
                + (view.CanAdvance ? " — ready to advance" : ""), view.CanAdvance ? ApertureTabStyle.Accent : ApertureTabStyle.Body);
            ApertureTabStyle.Line(c, ref y, "Nourishing the walls spends the highest-quality essence first. Each unit nourishes by its quality: "
                + "Initial 1, Middle 2, Upper 4, Peak 8.", ApertureTabStyle.Muted, GameFont.Tiny);
        }

        private void DrawActivityAndActions(Rect c, ref float y, ApertureView view)
        {
            ApertureTabStyle.Line(c, ref y, view.ActivityLabel ?? "Not cultivating.", view.ActivityLabel != null ? ApertureTabStyle.Accent : ApertureTabStyle.Muted);
            if (!view.CanManage)
            {
                ApertureTabStyle.Line(c, ref y, "Read-only: only the cultivator's own player-controlled body can act on this aperture.", ApertureTabStyle.Faint, GameFont.Tiny);
                return;
            }
            if (view.Rank != 1 || view.AtPeak) return;
            float half = (c.width - 8f) / 2f;
            var nourish = view.NourishAction;
            var advance = view.AdvanceAction;
            bool nourishing = (view.Activity & GuApertureActivity.NourishingWalls) != 0;
            if (nourish != null)
            {
                string reason = nourishing && nourish.Available ? "already nourishing the walls" : nourish.DisabledReason;
                if (ApertureTabStyle.ActionButton(new Rect(c.x, y, half, 32f), "Nourish aperture walls", reason,
                    "Sit and feed primeval essence into the aperture walls. One essence is spent every few seconds of work; stopping keeps all progress."))
                    Run(nourish);
            }
            if (advance != null)
            {
                string next = view.RankDef?.Stage(view.Stage + 1)?.label ?? ((RankOneStage)(view.Stage + 1)).ToString();
                if (ApertureTabStyle.ActionButton(new Rect(c.x + half + 8f, y, half, 32f), $"Advance to {next} Stage", advance.DisabledReason,
                    "Break through to the next minor stage once the walls are fully nourished. Newly recovered essence then takes the new stage's quality."))
                    Run(advance);
            }
            y += 38f;
            if (advance?.DisabledReason != null && nourish?.Available == true)
                ApertureTabStyle.Line(c, ref y, "Advancing: " + advance.DisabledReason + ".", ApertureTabStyle.Faint, GameFont.Tiny);
            else if (nourish?.DisabledReason != null && !nourishing)
                ApertureTabStyle.Line(c, ref y, "Nourishing: " + nourish.DisabledReason + ".", ApertureTabStyle.Faint, GameFont.Tiny);
        }

        private void DrawCultivationChannels(Rect c, ref float y, ApertureView view)
        {
            if (!view.CanManage) return;
            var ledger = GuCombatLedger.Instance;
            if (view.ChannelActions.Count == 0)
            {
                ApertureTabStyle.Line(c, ref y, "Cultivation techniques: no refined cultivation Gu is stored in this aperture.", ApertureTabStyle.Faint, GameFont.Tiny);
                return;
            }
            foreach (var action in view.ChannelActions)
            {
                var record = action.GuId != null ? Current.Game?.GetComponent<GuIndividualRegistry>()?.Resolve(action.GuId) : null;
                GuAbilityDef ability = action.Ability;
                if (ability?.channel == null || record == null) continue;
                var props = ability.channel;
                var worker = props.Worker(ability);

                ApertureTabStyle.Line(c, ref y, ability.LabelCap, ApertureTabStyle.Heading, GameFont.Medium);
                if (worker.RefinesEssence)
                {
                    string from = StageLabel(view);
                    string to = view.RankDef?.Stage(view.Stage + 1)?.label ?? ((RankOneStage)Math.Min(view.Stage + 1, 3)).ToString();
                    ApertureTabStyle.Line(c, ref y, view.AtPeak
                        ? "Raises essence quality within Rank 1. At Peak there is no higher Rank 1 quality to refine into."
                        : $"Raises essence quality within Rank 1: turns {from}-quality essence into the same volume of {to}-quality essence. "
                          + "It does not advance your stage, but refined essence nourishes the walls more efficiently.", ApertureTabStyle.Body, GameFont.Small);
                }
                else if (props is GuChannelProperties_BodilyCultivation bodily && bodily.attainment != null)
                {
                    var held = Hediff_GuBodilyAttainment.Of(view.Pawn, bodily.attainment);
                    ApertureTabStyle.Line(c, ref y, $"Tempers the body toward {bodily.attainment.label}: permanent, kept even if the Gu is lost. "
                        + $"Currently {held?.Progress ?? 0f:P0} developed.", ApertureTabStyle.Body, GameFont.Small);
                }

                var session = ledger?.ChannelFor(action.GuId);
                int cooldown = ledger?.CooldownRemaining(action.GuId, Find.TickManager.TicksGame) ?? 0;
                string state;
                if (session is { Active: true }) state = $"Channeling: {session.PulsesDone}/{session.PulseCount} pulses, {worker.ProgressText(view.Pawn, session)}.";
                else if (session is { Complete: false }) state = $"Suspended at {session.PulsesDone}/{session.PulseCount} pulses. Resuming is free and ignores the recovery time.";
                else state = $"Each session: {worker.ActivationText}; {props.pulseCount} pulses, one every {props.pulseIntervalTicks.ToStringTicksToPeriod()}, "
                    + $"each {worker.PerPulseText}.";
                ApertureTabStyle.Line(c, ref y, state, ApertureTabStyle.Muted, GameFont.Tiny);
                if (cooldown > 0 && session is not { Complete: false })
                    ApertureTabStyle.Line(c, ref y, $"The {record.Species?.label} is recovering: {cooldown.ToStringTicksToPeriod()} until a new session.", ApertureTabStyle.Warning, GameFont.Tiny);

                bool running = session is { Active: true };
                string reason = running ? "the session is already running" : action.DisabledReason;
                if (ApertureTabStyle.ActionButton(new Rect(c.x, y, Mathf.Min(c.width, 320f), 32f), action.Label, reason,
                    "Channel the stored Gu. Drafting, another order, incapacitation or withdrawing the Gu suspends the session with completed pulses kept."))
                    Run(action);
                y += 38f;
            }
        }

        /// <summary>
        /// Permanent bodily attainments read straight from the pawn's own hediffs, so they show whether or not the Gu that
        /// developed them is still held. Values are the native stat modifiers at the current development.
        /// </summary>
        private static void DrawBodilyAttributes(Rect c, ref float y, Pawn pawn)
        {
            ApertureTabStyle.Line(c, ref y, "Bodily attributes", ApertureTabStyle.Heading, GameFont.Medium);
            bool any = false;
            foreach (var attainment in Hediff_GuBodilyAttainment.AllOf(pawn))
            {
                any = true;
                ApertureTabStyle.Line(c, ref y, $"{attainment.def.LabelCap}: {(attainment.FullyDeveloped ? "fully developed" : attainment.Progress.ToString("P0") + " developed")}",
                    ApertureTabStyle.Body);
                ApertureTabStyle.Bar(new Rect(c.x, y, c.width, 10f), attainment.Progress, ApertureTabStyle.Accent);
                y += 14f;
                var effects = BodilyEffects(attainment);
                if (effects.Length > 0) ApertureTabStyle.Line(c, ref y, effects, ApertureTabStyle.Muted, GameFont.Tiny);
            }
            if (!any) ApertureTabStyle.Line(c, ref y, "No bodily strength has been cultivated.", ApertureTabStyle.Faint, GameFont.Tiny);
        }

        private static string BodilyEffects(Hediff_GuBodilyAttainment attainment)
        {
            var stage = attainment.CurStage;
            if (stage == null) return "";
            var parts = new List<string>();
            float severity = attainment.Severity;
            if (stage.statFactors != null)
                foreach (var modifier in stage.statFactors)
                {
                    float factor = HediffStatsUtility.GetStatFactorForSeverity(modifier.stat, stage, attainment.pawn, severity);
                    parts.Add($"{modifier.stat.LabelCap} {(factor - 1f) * 100f:+0.#;-0.#;0}%");
                }
            if (stage.statOffsets != null)
                foreach (var modifier in stage.statOffsets)
                    parts.Add($"{modifier.stat.LabelCap} {modifier.stat.ValueToString(HediffStatsUtility.GetStatOffsetForSeverity(modifier.stat, stage, attainment.pawn, severity), ToStringNumberSense.Offset)}");
            return string.Join(", ", parts);
        }

        private void DrawDevPreview(Rect c, ref float y)
        {
            y += 6f;
            ApertureTabStyle.Line(c, ref y, "Developer: preview aperture condition art (presentation only, not saved)", ApertureTabStyle.Warning, GameFont.Tiny);
            float w = (c.width - 16f) / 3f;
            if (Widgets.ButtonText(new Rect(c.x, y, w, 24f), "Actual", true, true, DevPreview != null)) DevPreview = null;
            if (Widgets.ButtonText(new Rect(c.x + w + 8f, y, w, 24f), "Cracked", true, true, DevPreview != GuApertureVitality.Cracked)) DevPreview = GuApertureVitality.Cracked;
            if (Widgets.ButtonText(new Rect(c.x + 2f * (w + 8f), y, w, 24f), "Dead", true, true, DevPreview != GuApertureVitality.Dead)) DevPreview = GuApertureVitality.Dead;
            y += 30f;
        }

        /// <summary>A proportional bar of the sea's composition against the shared capacity, with exact numbers.</summary>
        private static void DrawComposition(Rect rect, ApertureView view, string tooltip)
        {
            Rect bar = new Rect(rect.x, rect.y, rect.width, 12f);
            Widgets.DrawBoxSolid(bar, ApertureTabStyle.BarBack);
            float x = bar.x;
            for (int q = PrimevalSeaLayout.Qualities - 1; q >= 0; q--)
            {
                if (view.Amounts[q] <= 0 || view.Capacity <= 0) continue;
                float width = (float)(bar.width * view.Amounts[q] / view.Capacity);
                Widgets.DrawBoxSolid(new Rect(x, bar.y, Mathf.Max(1f, width), bar.height), view.RankDef?.StageColor(q) ?? Color.gray);
                x += width;
            }
            GUI.color = ApertureTabStyle.PanelEdge;
            Widgets.DrawBox(bar);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(bar, tooltip);
            ApertureTabStyle.Label(new Rect(rect.x, bar.yMax + 2f, rect.width, 20f),
                $"{view.Total:0.##} / {view.Capacity:0} essence", ApertureTabStyle.Muted, GameFont.Tiny, TextAnchor.UpperCenter);
        }

        private static string CompositionText(ApertureView view)
        {
            var lines = new List<string> { $"Primeval essence {view.Total:0.##} of {view.Capacity:0} shared capacity" };
            for (int q = 0; q < PrimevalSeaLayout.Qualities; q++)
                lines.Add($"  {view.RankDef?.Stage(q)?.label ?? ((RankOneStage)q).ToString()} quality: {view.Amounts[q]:0.##}");
            return string.Join("\n", lines);
        }

        private static string OrbTooltip(ApertureView view)
        {
            if (view.Disclosure != ApertureDisclosure.Full) return $"Rank {view.Rank} {StageLabel(view)} Stage aperture.";
            return $"Rank {view.Rank} {StageLabel(view)} Stage aperture\n\nThe sea's layers show each quality of essence, densest at the bottom. "
                + "The lit arc of the wall ring shows nourishment gathered toward the next minor stage.\n\n" + CompositionText(view);
        }

        private static string Percent(double value, double total) => total <= 0 ? "0%" : (value / total).ToString("P0");
    }
}
