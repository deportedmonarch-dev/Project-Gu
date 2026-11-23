using System;
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
            return CanUseSpring(pawn, spring)
                && TryAwaken(Current.Game?.GetComponent<GuWorldComponent>(), pawn, spring, out result);
        }

        public static bool TryAwaken(GuWorldComponent world, Pawn pawn, out string result) =>
            TryAwaken(world, pawn, null, out result);

        private static bool CanUseSpring(Pawn pawn, CompGuSpiritSpring spring) =>
            pawn != null && !pawn.Dead && spring?.parent?.Spawned == true && pawn.Map == spring.parent.Map;

        private static bool TryAwaken(GuWorldComponent world, Pawn pawn, CompGuSpiritSpring spring, out string result)
        {
            result = "Invalid humanlike pawn or missing cultivation registry.";
            if (world == null || pawn == null || pawn.Dead) return false;
            if (!world.TryBeginAwakening())
            {
                result = "Cultivator identity is unavailable or an awakening is already in progress.";
                return false;
            }

            Hediff_Aperture aperture = null;
            bool committed = false;
            bool publishing = false;
            try
            {
                var record = world.PrepareAwakeningRecord(pawn);
                if (record == null) return false;
                if (record.State != AwakeningState.Unattempted)
                {
                    result = spring != null ? "This person has already undergone awakening."
                        : record.State == AwakeningState.Awakened ? "Already awakened. No duplicate aperture created."
                        : "This pawn has already failed their natural awakening attempt.";
                    return false;
                }
                if (spring != null && spring.HopeGuAvailable <= 0)
                {
                    result = "No Hope Gu are available at the spirit spring.";
                    return false;
                }

                bool opensAperture = record.LatentAptitude != 0;
                if (opensAperture)
                {
                    result = "Could not add aperture. Awakening state was not changed.";
                    aperture = world.CreateAperture(pawn, record.LatentAptitude);
                    if (aperture == null) return false;
                    // Native AddHediff can throw from callbacks after inserting the object.
                    pawn.health.AddHediff(aperture);
                    if (pawn.Dead || !pawn.health.hediffSet.hediffs.Contains(aperture)) return false;
                }

                string outcome = opensAperture
                    ? "Aperture opened: " + AptitudeRules.Grade(record.LatentAptitude) +
                        "-grade (" + record.LatentAptitude + "/100)."
                    : "Awakening failed: no aperture formed.";
                result = "The prepared cultivator identity or aperture changed.";
                if (!world.PrepareAwakeningPublication(record, aperture)) return false;
                if (spring != null)
                {
                    if (!CanUseSpring(pawn, spring))
                    {
                        result = "The spirit spring is unavailable.";
                        return false;
                    }
                    publishing = true;
                    if (!spring.TryConsumeHopeGu())
                    {
                        publishing = false;
                        result = "No Hope Gu are available at the spirit spring.";
                        return false;
                    }
                }

                // The debit and record assignment are synchronous and invoke no engine callbacks.
                publishing = true;
                world.PublishAwakening(record, aperture);
                committed = true;
                result = outcome;
                // These health markers are derived from the committed record. Discovery retries them.
                world.SyncMortalStatus(pawn, record);
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"[Project GU] Awakening for {pawn} encountered an error. {exception}");
                if (publishing && !committed) world.FailAwakeningRecovery(exception);
                if (!committed) result = "The awakening ceremony was interrupted.";
                return committed;
            }
            finally
            {
                try
                {
                    if (!committed && aperture != null && pawn.health.hediffSet.hediffs.Contains(aperture))
                    {
                        pawn.health.RemoveHediff(aperture);
                        if (pawn.health.hediffSet.hediffs.Contains(aperture))
                            throw new InvalidOperationException("Provisional aperture survived awakening rollback.");
                    }
                }
                catch (Exception error) { world.FailAwakeningRecovery(error); }
                finally
                {
                    world.EndAwakening();
                }
            }
        }
    }
}
