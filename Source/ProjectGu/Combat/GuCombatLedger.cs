using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ProjectGu.Combat
{
    /// <summary>
    /// Saved combat facts for Gu abilities. It owns per-individual cooldowns, the pending cast intent behind each
    /// ordered cast job, committed casts still in flight and a bounded history of resolved outcomes. It does not own
    /// essence (the aperture does), Gu custody (the registry does) or projectile movement and damage (the native
    /// projectile does). Every record is keyed by an immutable cast ID, so a cast can be committed and resolved at most
    /// once, including across save and load.
    /// </summary>
    public sealed class GuCombatLedger : GameComponent
    {
        internal const int ResolvedHistoryLimit = 32;
        private const int MaintenanceInterval = 250;
        private const int IntentGraceTicks = 10_000;
        private const int LostFlightTicks = 2_500;

        private Dictionary<string, int> cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, GuCastIntent> intents = new();
        private Dictionary<string, GuCastRecord> inFlight = new(StringComparer.OrdinalIgnoreCase);
        private List<GuCastRecord> resolved = new();
        private int committedTotal;
        private int resolvedTotal;
        // Transient: cast IDs of spawned projectiles, rebuilt as projectiles spawn after loading.
        private readonly HashSet<string> liveProjectiles = new(StringComparer.OrdinalIgnoreCase);

        // Scribe working lists.
        private List<string> cooldownKeys;
        private List<int> cooldownValues;
        private List<int> intentKeys;
        private List<GuCastIntent> intentValues;
        private List<GuCastRecord> inFlightValues;
        // Channel sessions, at most one per Gu individual.
        private Dictionary<string, GuChannelSession> channels = new(StringComparer.OrdinalIgnoreCase);
        private List<GuChannelSession> channelValues;

        public GuCombatLedger(Game game) { }

        internal static GuCombatLedger Instance => Current.Game?.GetComponent<GuCombatLedger>();

        internal int CommittedTotal => committedTotal;
        internal int ResolvedTotal => resolvedTotal;
        internal IReadOnlyCollection<GuCastRecord> InFlight => inFlight.Values;
        internal IReadOnlyList<GuCastRecord> Resolved => resolved;
        internal int IntentCount => intents.Count;

        // ---- Cooldowns -------------------------------------------------------------------------------------------

        internal int CooldownRemaining(string guId, int now) =>
            guId != null && cooldownUntil.TryGetValue(guId, out int until) && until > now ? until - now : 0;

        // ---- Intents ---------------------------------------------------------------------------------------------

        internal void RegisterIntent(int jobId, GuCastIntent intent) => intents[jobId] = intent;

        internal GuCastIntent IntentFor(int jobId) => intents.TryGetValue(jobId, out var intent) ? intent : null;

        internal void RemoveIntent(int jobId) => intents.Remove(jobId);

        // ---- Commitment and resolution ---------------------------------------------------------------------------

        internal void Commit(GuCastRecord cast, int cooldownTicks)
        {
            if (cast == null || string.IsNullOrEmpty(cast.CastId) || inFlight.ContainsKey(cast.CastId))
                throw new InvalidOperationException("A Gu cast must be committed once with a unique cast ID.");
            inFlight.Add(cast.CastId, cast);
            if (cooldownTicks > 0) cooldownUntil[cast.GuId] = cast.LaunchTick + cooldownTicks;
            committedTotal++;
        }

        /// <summary>
        /// Claims the single impact of a committed cast. Returns false when the cast already resolved, so a duplicate
        /// terminal callback applies no damage. Unknown casts (for example from a save written before the cast was
        /// recorded) are allowed once and recorded.
        /// </summary>
        internal bool TryClaimImpact(string castId)
        {
            if (string.IsNullOrEmpty(castId)) return true;
            return !resolved.Any(record => string.Equals(record.CastId, castId, StringComparison.OrdinalIgnoreCase));
        }

        internal void Resolve(string castId, GuCastOutcome outcome, string hitThingId, int damageAmount, float observedDamage)
        {
            if (string.IsNullOrEmpty(castId)) return;
            if (!inFlight.TryGetValue(castId, out var cast)) cast = new GuCastRecord { CastId = castId };
            inFlight.Remove(castId);
            if (resolved.Any(record => string.Equals(record.CastId, castId, StringComparison.OrdinalIgnoreCase))) return;
            cast.Outcome = outcome;
            cast.HitThingId = hitThingId;
            cast.DamageAmount = damageAmount;
            cast.ObservedDamage = observedDamage;
            cast.ResolvedTick = Find.TickManager?.TicksGame ?? -1;
            resolved.Add(cast);
            resolvedTotal++;
            if (resolved.Count > ResolvedHistoryLimit) resolved.RemoveAt(0);
        }

        internal GuCastRecord FindResolved(string castId) =>
            resolved.LastOrDefault(record => string.Equals(record.CastId, castId, StringComparison.OrdinalIgnoreCase));

        internal void NoteProjectileSpawned(string castId) { if (!string.IsNullOrEmpty(castId)) liveProjectiles.Add(castId); }

        internal void NoteProjectileDespawned(string castId) { if (!string.IsNullOrEmpty(castId)) liveProjectiles.Remove(castId); }

        internal bool HasLiveProjectile(string castId) => castId != null && liveProjectiles.Contains(castId);

        internal GuCastRecord FindInFlight(string castId) =>
            castId != null && inFlight.TryGetValue(castId, out var cast) ? cast : null;

        // ---- Channel sessions ----------------------------------------------------------------------------------

        internal IReadOnlyCollection<GuChannelSession> Channels => channels.Values;

        internal GuChannelSession ChannelFor(string guId) => guId != null && channels.TryGetValue(guId, out var session) ? session : null;

        internal GuChannelSession ChannelById(string sessionId) =>
            sessionId == null ? null : channels.Values.FirstOrDefault(session => session.SessionId == sessionId);

        /// <summary>Records a newly committed session, replacing any stale one for the same Gu, and starts its cooldown.</summary>
        internal void BeginChannel(GuChannelSession session, int cooldownTicks)
        {
            if (session == null || string.IsNullOrEmpty(session.GuId) || string.IsNullOrEmpty(session.SessionId))
                throw new InvalidOperationException("A channel session needs a Gu and a session ID.");
            channels[session.GuId] = session;
            if (cooldownTicks > 0) cooldownUntil[session.GuId] = session.StartedTick + cooldownTicks;
            committedTotal++;
        }

        internal void EndChannel(GuChannelSession session)
        {
            if (session != null && channels.TryGetValue(session.GuId, out var current) && current == session) channels.Remove(session.GuId);
        }

        private static bool IsDriven(GuChannelSession session) => Find.Maps.Any(map => map.mapPawns.AllPawnsSpawned.Any(pawn =>
            pawn.jobs?.curDriver is JobDriver_GuChannelAbility driver && driver.SessionId == session.SessionId));

        // ---- Maintenance -----------------------------------------------------------------------------------------

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % MaintenanceInterval != 0 || cooldownUntil.Count + intents.Count + inFlight.Count + channels.Count == 0) return;
            Maintain(now);
        }

        private void Maintain(int now)
        {
            // A session marked active must have a live job driving it; otherwise it is suspended and resumable.
            foreach (var session in channels.Values.Where(session => session.Active && !IsDriven(session)).ToArray())
                session.Active = false;
            foreach (string key in cooldownUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                cooldownUntil.Remove(key);
            foreach (var pair in intents.Where(pair => now - pair.Value.CreatedTick > IntentGraceTicks).ToArray())
                intents.Remove(pair.Key);
            // A projectile that vanished with its map, or by any route that skipped its own terminal callbacks.
            foreach (var cast in inFlight.Values.Where(cast => now - cast.LaunchTick > LostFlightTicks && !liveProjectiles.Contains(cast.CastId)).ToArray())
                Resolve(cast.CastId, GuCastOutcome.Lost, null, 0, 0f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                foreach (string key in cooldownUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                    cooldownUntil.Remove(key);
                inFlightValues = inFlight.Values.ToList();
            }
            Scribe_Collections.Look(ref cooldownUntil, "guAbilityCooldowns", LookMode.Value, LookMode.Value, ref cooldownKeys, ref cooldownValues);
            Scribe_Collections.Look(ref intents, "guAbilityIntents", LookMode.Value, LookMode.Deep, ref intentKeys, ref intentValues);
            Scribe_Collections.Look(ref inFlightValues, "guAbilityCastsInFlight", LookMode.Deep);
            Scribe_Collections.Look(ref resolved, "guAbilityCastsResolved", LookMode.Deep);
            Scribe_Values.Look(ref committedTotal, "guAbilityCommittedTotal");
            Scribe_Values.Look(ref resolvedTotal, "guAbilityResolvedTotal");
            if (Scribe.mode == LoadSaveMode.Saving) channelValues = channels.Values.ToList();
            Scribe_Collections.Look(ref channelValues, "guAbilityChannels", LookMode.Deep);
            // Values, Defs and deep records are complete after LoadingVars; nothing here needs cross-references.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                cooldownUntil = cooldownUntil == null ? new(StringComparer.OrdinalIgnoreCase) : new(cooldownUntil, StringComparer.OrdinalIgnoreCase);
                intents ??= new();
                inFlight = new(StringComparer.OrdinalIgnoreCase);
                foreach (var cast in inFlightValues ?? new())
                    if (!string.IsNullOrEmpty(cast?.CastId)) inFlight[cast.CastId] = cast;
                resolved ??= new();
                resolved.RemoveAll(record => record == null);
                channels = new(StringComparer.OrdinalIgnoreCase);
                foreach (var session in channelValues ?? new())
                    if (!string.IsNullOrEmpty(session?.GuId) && session.Ability?.channel != null) channels[session.GuId] = session;
            }
            inFlightValues = null;
            channelValues = null;
        }
    }

    public enum GuCastOutcome
    {
        InFlight,
        HitThing,
        HitGround,
        Blocked,
        EndedWithoutImpact,
        Lost
    }

    /// <summary>The Gu individual and ability a pending cast job will use. Its target lives on the native job.</summary>
    public sealed class GuCastIntent : IExposable
    {
        public string GuId;
        public GuAbilityDef Ability;
        public int CreatedTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref GuId, "guId");
            Scribe_Defs.Look(ref Ability, "ability");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
        }
    }

    /// <summary>One committed cast: who paid what, for which individual, and how it ended.</summary>
    public sealed class GuCastRecord : IExposable
    {
        public string CastId;
        public string GuId;
        public GuAbilityDef Ability;
        public string CasterId;
        public string ApertureId;
        public float EssencePaid;
        public int LaunchTick;
        public string TargetDescription;
        public GuCastOutcome Outcome = GuCastOutcome.InFlight;
        public string HitThingId;
        public int DamageAmount;
        public float ObservedDamage;
        public int ResolvedTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref CastId, "castId");
            Scribe_Values.Look(ref GuId, "guId");
            Scribe_Defs.Look(ref Ability, "ability");
            Scribe_Values.Look(ref CasterId, "casterId");
            Scribe_Values.Look(ref ApertureId, "apertureId");
            Scribe_Values.Look(ref EssencePaid, "essencePaid");
            Scribe_Values.Look(ref LaunchTick, "launchTick");
            Scribe_Values.Look(ref TargetDescription, "target");
            Scribe_Values.Look(ref Outcome, "outcome", GuCastOutcome.InFlight);
            Scribe_Values.Look(ref HitThingId, "hitThingId");
            Scribe_Values.Look(ref DamageAmount, "damageAmount");
            Scribe_Values.Look(ref ObservedDamage, "observedDamage");
            Scribe_Values.Look(ref ResolvedTick, "resolvedTick", -1);
        }
    }
}
