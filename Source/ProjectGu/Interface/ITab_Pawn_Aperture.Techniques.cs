using System.Collections.Generic;
using System.Linq;
using ProjectGu.Combat;
using ProjectGu.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    public sealed partial class ITab_Pawn_Aperture
    {
        private float techniqueHeight = 600f;

        /// <summary>
        /// A reference to what the cultivator's own Gu can do, read from the species and ability Defs. It is not a second
        /// command bar: combat techniques stay on the pawn's command bar, and cultivation channels live on the Cultivation page.
        /// </summary>
        private void DrawTechniques(Rect rect)
        {
            if (Concealed(rect)) return;
            var view = View;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, techniqueHeight);
            Widgets.BeginScrollView(rect, ref techniqueScroll, viewRect);
            float y = 0f;
            Rect c = new Rect(0f, 0f, viewRect.width, 0f);
            ApertureTabStyle.Line(c, ref y, "Combat techniques are used from the pawn's command bar. Cultivation techniques are controlled on the Cultivation page.",
                ApertureTabStyle.Muted, GameFont.Tiny, 8f);
            if (view.KnownGu.Count == 0)
                ApertureTabStyle.Line(c, ref y, "This cultivator has refined no Gu that they keep. Refined Gu and the techniques they grant appear here.", ApertureTabStyle.Muted);
            foreach (var record in view.KnownGu) DrawGuTechniques(c, ref y, record, view);
            techniqueHeight = Mathf.Max(y + 8f, rect.height);
            Widgets.EndScrollView();
        }

        private void DrawGuTechniques(Rect c, ref float y, GuRecord record, ApertureView view)
        {
            var species = record.Species;
            bool stored = view.Gu.Any(entry => entry.Record == record && entry.Placement == GuApertureGuPlacement.Aperture);
            float top = y;
            Rect portrait = new Rect(c.x, y, 48f, 48f);
            Widgets.DrawBoxSolid(portrait, new Color(0f, 0f, 0f, 0.35f));
            GUI.DrawTexture(portrait.ContractedBy(2f), GuPortraits.For(species), ScaleMode.ScaleToFit);
            Rect head = new Rect(portrait.xMax + 10f, y, c.width - 58f, 48f);
            float hy = head.y;
            ApertureTabStyle.Line(head, ref hy, species?.LabelCap ?? record.SpeciesDefName, ApertureTabStyle.Heading, GameFont.Medium);
            ApertureTabStyle.Line(head, ref hy, $"Rank {record.Rank} Gu" + (species?.role.NullOrEmpty() == false ? $" · {species.role}" : "")
                + (stored ? " · stored in the aperture: its techniques are available" : " · carried: store it in the aperture to use its techniques"),
                stored ? ApertureTabStyle.Accent : ApertureTabStyle.Warning, GameFont.Tiny);
            y = Mathf.Max(portrait.yMax, hy) + 4f;

            if (species?.effectNotes != null)
                foreach (string note in species.effectNotes) Bullet(c, ref y, note, ApertureTabStyle.Body);
            if (species?.abilities != null)
                foreach (var ability in species.abilities.Where(ability => ability != null)) DrawAbility(c, ref y, ability, record, view, stored);
            if ((species?.abilities == null || species.abilities.Count == 0) && (species?.effectNotes == null || species.effectNotes.Count == 0))
                ApertureTabStyle.Line(c, ref y, "No technique is recorded for this Gu.", ApertureTabStyle.Faint, GameFont.Tiny);
            y += 6f;
            Widgets.DrawLineHorizontal(c.x, y, c.width, ApertureTabStyle.PanelEdge);
            y += 10f;
            if (y - top < 60f) y = top + 60f;
        }

        private static void DrawAbility(Rect c, ref float y, GuAbilityDef ability, GuRecord record, ApertureView view, bool stored)
        {
            Rect box = new Rect(c.x + 12f, y, c.width - 12f, 0f);
            Rect icon = new Rect(box.x, y + 2f, 32f, 32f);
            GUI.DrawTexture(icon, ability.Icon, ScaleMode.ScaleToFit);
            Rect text = new Rect(icon.xMax + 8f, y, box.width - 40f, 0f);
            float ty = y;
            ApertureTabStyle.Line(text, ref ty, $"{ability.LabelCap}  ·  {CategoryLabel(ability.category)}", ApertureTabStyle.Heading);
            ApertureTabStyle.Line(text, ref ty, $"Granted by {record.Species?.label ?? record.SpeciesDefName}. "
                + (ability.IsChannel ? "Used from the Cultivation page or the command bar." : "Used from the pawn's command bar."), ApertureTabStyle.Muted, GameFont.Tiny);
            y = Mathf.Max(icon.yMax, ty) + 2f;

            foreach (string note in ability.effectNotes ?? new List<string>()) Bullet(box, ref y, note, ApertureTabStyle.Body);
            foreach (var (label, value) in Numbers(ability)) Stat(box, ref y, label, value);
            int cooldown = GuCombatLedger.Instance?.CooldownRemaining(record.Id, Find.TickManager.TicksGame) ?? 0;
            if (cooldown > 0) Stat(box, ref y, "Recovering", cooldown.ToStringTicksToPeriod(), ApertureTabStyle.Warning);
            if (!stored) Stat(box, ref y, "Status", "Unavailable until stored in the aperture", ApertureTabStyle.Warning);
            if (record.Species?.SimulatedFeeding is { } feeding)
            {
                // The restriction the authoritative ability check enforces; feeding itself lives on the Gu page.
                var nourishment = view.Gu.FirstOrDefault(entry => entry.Record == record)?.Nourishment;
                if (nourishment?.Impaired == true)
                    Stat(box, ref y, "Sustenance", "Unavailable: the Gu is withering from starvation. Feed it on the Gu page.", ApertureTabStyle.Warning);
                else
                {
                    bool hungry = nourishment?.Level >= GuNourishmentLevel.Hungry;
                    Stat(box, ref y, "Sustenance", $"Needs a fed Gu: unavailable after {feeding.impairAfterStarvingDays:0.#} days of starvation"
                        + (hungry ? $" (currently {LevelLabel(nourishment.Value.Level).ToLowerInvariant()})" : ""),
                        hungry ? ApertureTabStyle.Warning : ApertureTabStyle.Body);
                }
            }
            foreach (string limit in ability.limitations ?? new List<string>()) Bullet(box, ref y, limit, ApertureTabStyle.Muted);
            y += 6f;
        }

        /// <summary>Current numbers from the ability Def and its projectile; nothing here is a copy.</summary>
        internal static IEnumerable<(string, string)> Numbers(GuAbilityDef ability)
        {
            if (ability.IsChannel)
            {
                var props = ability.channel;
                var worker = props.Worker(ability);
                yield return ("Activation cost", worker.ActivationText);
                yield return ("Channel", $"{props.pulseCount} pulses, one every {props.pulseIntervalTicks.ToStringTicksToPeriod()} "
                    + $"({(props.pulseCount * props.pulseIntervalTicks).ToStringTicksToPeriod()} in all)");
                yield return ("Per pulse", worker.PerPulseText);
                yield return ("Recovery", $"{ability.cooldownTicks.ToStringTicksToPeriod()} after a new session begins");
                yield break;
            }
            yield return ("Range", $"{ability.minRange:0.#}–{ability.range:0.#} cells");
            yield return ("Essence cost", $"{ability.essenceCost:0.##} primeval essence, paid when the technique is released");
            yield return ("Aim time", ability.warmupTicks.ToStringSecondsFromTicks());
            yield return ("Recovery", ability.cooldownTicks.ToStringSecondsFromTicks());
            var targets = new List<string>();
            if (ability.targetPawns) targets.Add("pawns");
            if (ability.targetLocations) targets.Add("locations");
            yield return ("Targets", string.Join(" or ", targets) + (ability.requireLineOfSight ? "; needs line of sight" : ""));
            if (ability.projectileDef?.projectile is { } projectile && projectile.damageDef != null)
            {
                yield return ("Damage", $"{projectile.GetDamageAmount((Thing)null)} {projectile.damageDef.label}, "
                    + $"{projectile.GetArmorPenetration((Thing)null):P0} armor penetration");
            }
        }

        private static string CategoryLabel(GuAbilityCategory category) => category switch
        {
            GuAbilityCategory.Combat => "combat technique",
            GuAbilityCategory.Cultivation => "cultivation support",
            _ => category.ToString().ToLowerInvariant()
        };

        private static void Bullet(Rect c, ref float y, string text, Color color)
        {
            Rect line = new Rect(c.x + 14f, y, c.width - 14f, 0f);
            ApertureTabStyle.Label(new Rect(c.x + 2f, y, 12f, 20f), "•", color);
            ApertureTabStyle.Line(line, ref y, text, color, GameFont.Small, 1f);
        }

        private static void Stat(Rect c, ref float y, string label, string value, Color? color = null)
        {
            const float labelWidth = 130f;
            Text.Font = GameFont.Tiny;
            float height = Mathf.Max(18f, Text.CalcHeight(value, c.width - labelWidth));
            Text.Font = GameFont.Small;
            ApertureTabStyle.Label(new Rect(c.x + 14f, y, labelWidth, height), label, ApertureTabStyle.Muted, GameFont.Tiny);
            ApertureTabStyle.Label(new Rect(c.x + 14f + labelWidth, y, c.width - labelWidth - 14f, height), value, color ?? ApertureTabStyle.Body, GameFont.Tiny);
            y += height;
        }
    }
}
