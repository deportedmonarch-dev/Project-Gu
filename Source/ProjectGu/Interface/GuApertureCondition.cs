using System.Collections.Generic;

namespace ProjectGu.Interface
{
    /// <summary>How an aperture looks. Only a legitimate gameplay state may select anything other than Healthy.</summary>
    public enum GuApertureVitality { Healthy, Cracked, Dead }

    /// <summary>Running processes that the orb may show as subtle activity. Each flag comes from a live job or saved session.</summary>
    [System.Flags]
    public enum GuApertureActivity
    {
        None = 0,
        NourishingWalls = 1,
        EssenceRefinement = 2,
        WillEnforcement = 4
    }

    /// <summary>Everything the orb needs besides essence amounts. Presentation only; never saved.</summary>
    public readonly struct GuApertureVisualState
    {
        public readonly GuApertureVitality Vitality;
        /// <summary>0..1, meaningful only when <see cref="Vitality"/> is Cracked.</summary>
        public readonly float CrackSeverity;
        public readonly GuApertureActivity Activity;
        /// <summary>True when the state came from a developer preview rather than gameplay.</summary>
        public readonly bool Preview;

        public GuApertureVisualState(GuApertureVitality vitality, float crackSeverity, GuApertureActivity activity, bool preview = false)
        {
            Vitality = vitality;
            CrackSeverity = vitality == GuApertureVitality.Cracked ? UnityEngine.Mathf.Clamp01(crackSeverity) : 0f;
            Activity = vitality == GuApertureVitality.Dead ? GuApertureActivity.None : activity;
            Preview = preview;
        }

        public bool Functional => Vitality != GuApertureVitality.Dead;
    }

    /// <summary>
    /// A gameplay system that owns an aperture condition (damage, death, zombification) reports it here. The interface never
    /// invents a condition: with no source registered, or every source declining, an aperture is Healthy. Alpha 4A ships no
    /// source because no gameplay state for damaged or dead apertures exists yet.
    /// </summary>
    public interface IGuApertureConditionSource
    {
        /// <summary>Returns true and the vitality when this source has authority over the aperture's condition.</summary>
        bool TryGetCondition(Hediff_Aperture aperture, out GuApertureVitality vitality, out float crackSeverity);
    }

    public static class GuApertureConditions
    {
        private static readonly List<IGuApertureConditionSource> sources = new();

        /// <summary>Registers an authoritative condition source. Call once at startup.</summary>
        public static void Register(IGuApertureConditionSource source)
        {
            if (source != null && !sources.Contains(source)) sources.Add(source);
        }

        public static int SourceCount => sources.Count;

        public static GuApertureVitality Resolve(Hediff_Aperture aperture, out float crackSeverity)
        {
            crackSeverity = 0f;
            if (aperture == null) return GuApertureVitality.Healthy;
            for (int i = 0; i < sources.Count; i++)
            {
                try
                {
                    if (sources[i].TryGetCondition(aperture, out var vitality, out crackSeverity)) return vitality;
                }
                catch (System.Exception exception)
                {
                    Verse.Log.WarningOnce($"[Project GU] An aperture condition source failed; the aperture is shown healthy. {exception}", 0x4A70C0);
                }
            }
            crackSeverity = 0f;
            return GuApertureVitality.Healthy;
        }
    }
}
