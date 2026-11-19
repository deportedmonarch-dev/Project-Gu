using Verse;
using ProjectGu.Domain;
using ProjectGu.Springs;

namespace ProjectGu
{
    /// <summary>
    /// Single authoritative state transition. Future spirit springs, rituals and
    /// scenarios call this service; no ritual rolls or applies an aperture itself.
    /// Prototype 0.2.0 exposes only a developer-test entrance.
    /// </summary>
    public static class GuAwakeningService
    {
        /// <summary>Commits a completed ceremony once, consuming one locally available Hope Gu.</summary>
        public static bool TryAwakenAtSpring(Pawn pawn, CompGuSpiritSpring spring, out string result)
        {
            result = "The spirit spring is unavailable.";
            if (pawn == null || pawn.Dead || spring == null || !spring.parent.Spawned ||
                pawn.Map != spring.parent.Map) return false;
            GuWorldComponent world = Current.Game?.GetComponent<GuWorldComponent>();
            GuPawnRecord record = world?.GetOrCreate(pawn);
            if (record == null) return false;
            if (record.State != AwakeningState.Unattempted)
            {
                result = "This person has already undergone awakening.";
                return false;
            }
            if (spring.HopeGuAvailable <= 0)
            {
                result = "No Hope Gu are available at the spirit spring.";
                return false;
            }
            // The existing authoritative service owns the one-way transition.
            // Both successful and failed natural attempts consume one Hope Gu.
            if (!TryAwaken(world, pawn, out result)) return false;
            spring.TryConsumeHopeGu();
            return true;
        }

        public static bool TryAwaken(GuWorldComponent world, Pawn pawn, out string result)
        {
            result = "Invalid humanlike pawn or missing cultivation registry.";
            if (world == null || pawn == null || pawn.Dead) return false;
            GuPawnRecord record = world.GetOrCreate(pawn);
            if (record == null) return false;
            if (record.State == AwakeningState.Awakened)
            {
                result = "Already awakened. No duplicate aperture created.";
                return false;
            }
            if (record.State == AwakeningState.Failed)
            {
                result = "This pawn has already failed their natural awakening attempt.";
                return false;
            }
            // On failure, a pawn has no aptitude rather than D-grade aptitude.
            if (record.LatentAptitude == 0)
            {
                record.State = AwakeningState.Failed;
                world.SyncMortalStatus(pawn, record);
                result = "Awakening failed: no aperture formed.";
                return true;
            }
            // Only commit the transition after the health system actually owns
            // the aperture; avoid a half-awakened state if a def is missing.
            if (!world.AddAperture(pawn, record.LatentAptitude))
            {
                result = "Could not add aperture. Awakening state was not changed.";
                return false;
            }
            record.State = AwakeningState.Awakened;
            world.SyncMortalStatus(pawn, record);
            result = "Aperture opened: " + AptitudeRules.Grade(record.LatentAptitude) +
                     "-grade (" + record.LatentAptitude + "/100).";
            return true;
        }
    }
}
