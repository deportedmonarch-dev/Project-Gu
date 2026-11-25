using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ProjectGu.Interface
{
    /// <summary>
    /// A future cultivation foundation (special physique, dao marks, wills and thoughts, deductions, path attainment,
    /// immortal properties) adds a section to the Foundation page with one of these Defs and a worker. A worker returns
    /// nothing when the foundation is absent or undiscovered, so hidden information is never revealed.
    /// </summary>
    public sealed class GuApertureFoundationSectionDef : Def
    {
        public Type workerClass;
        public int order = 100;

        [Unsaved] private GuApertureFoundationSectionWorker worker;

        public GuApertureFoundationSectionWorker Worker => worker ??= workerClass != null
            ? (GuApertureFoundationSectionWorker)Activator.CreateInstance(workerClass) : null;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (workerClass == null || !typeof(GuApertureFoundationSectionWorker).IsAssignableFrom(workerClass) || workerClass.IsAbstract)
                yield return $"{defName}: workerClass must be a concrete ProjectGu.Interface.GuApertureFoundationSectionWorker";
        }
    }

    public abstract class GuApertureFoundationSectionWorker
    {
        /// <summary>Lines to show for this pawn, or an empty sequence when the foundation is absent or undiscovered.</summary>
        public abstract IEnumerable<(string label, string value)> Facts(Pawn pawn, Hediff_Aperture aperture);
    }

    public sealed partial class ITab_Pawn_Aperture
    {
        private float foundationHeight = 400f;
        private static List<GuApertureFoundationSectionDef> sections;

        private void DrawFoundation(Rect rect)
        {
            if (Concealed(rect)) return;
            var view = View;
            var aperture = view.Aperture;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, foundationHeight);
            Widgets.BeginScrollView(rect, ref foundationScroll, viewRect);
            float y = 0f;
            Rect c = new Rect(0f, 0f, viewRect.width, 0f);

            ApertureTabStyle.Line(c, ref y, "Aptitude", ApertureTabStyle.Heading, GameFont.Medium);
            if (aperture.HasExtremePhysique)
                Fact(c, ref y, "Grade", "Extreme physique (aptitude beyond ordinary grading)");
            else
                Fact(c, ref y, "Grade", $"{aperture.AptitudeGrade}-grade aptitude, {aperture.Aptitude}%");
            Fact(c, ref y, "Primeval sea", $"{aperture.MaxEssence:0} essence capacity" + (aperture.HasExtremePhysique ? "" : $" (the sea fills {aperture.Aptitude}% of the aperture)"));
            Fact(c, ref y, "Recovery", $"{aperture.RegenerationPerHour:0.#} essence per hour, about {aperture.RegenerationPerHour * 24f:0} per day");
            y += 8f;

            ApertureTabStyle.Line(c, ref y, "Cultivation", ApertureTabStyle.Heading, GameFont.Medium);
            Fact(c, ref y, "Realm", $"Rank {view.Rank}, {StageLabel(view)} Stage");
            if (view.RankDef != null) Fact(c, ref y, "Essence", view.RankDef.EssenceLabelCap);
            Fact(c, ref y, "Aperture", $"Mortal aperture, {view.ClaimCapacity} Gu places");
            if (aperture.HasExtremePhysique) Fact(c, ref y, "Special physique", aperture.ExtremePhysiqueId);

            sections ??= new List<GuApertureFoundationSectionDef>(DefDatabase<GuApertureFoundationSectionDef>.AllDefsListForReading);
            sections.SortBy(section => section.order);
            foreach (var section in sections)
            {
                List<(string, string)> facts;
                try { facts = new List<(string, string)>(section.Worker?.Facts(view.Pawn, aperture) ?? Array.Empty<(string, string)>()); }
                catch (Exception exception)
                {
                    Log.ErrorOnce($"[Project GU] Foundation section {section.defName} failed. {exception}", section.shortHash ^ 0x4F0D);
                    continue;
                }
                if (facts.Count == 0) continue;
                y += 8f;
                ApertureTabStyle.Line(c, ref y, section.LabelCap, ApertureTabStyle.Heading, GameFont.Medium);
                foreach (var (label, value) in facts) Fact(c, ref y, label, value);
            }

            y += 10f;
            ApertureTabStyle.Line(c, ref y, "Further foundations of the Dao appear here once this cultivator possesses them.", ApertureTabStyle.Faint, GameFont.Tiny);
            foundationHeight = Mathf.Max(y + 8f, rect.height);
            Widgets.EndScrollView();
        }
    }
}
