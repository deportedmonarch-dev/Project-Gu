using System;
using System.Collections.Generic;
using ProjectGu.Domain;
using Verse;

namespace ProjectGu
{
    public partial class GuWorldComponent
    {
        internal enum NativePath { Kill, CorpseDestroy, PawnDestroy, Discard, Gc }

        internal sealed class NativeObservation(GuCultivatorRecord record, Pawn pawn)
        {
            internal readonly GuCultivatorRecord Record = record;
            internal readonly Pawn Pawn = pawn;
            internal int Depth;
            internal int KillDepth;
            internal int CorpseDepth;
            internal int GcDepth;
            internal string BodyId;
            internal bool CorpselessDeath;
            internal string DiscardCause;
            internal bool InterruptedGc;
            internal GuObservedLife Life;
            internal int Tick;
        }

        internal sealed record NativeScope(GuWorldComponent World, NativeObservation Observation, NativePath Path,
            Corpse Body, bool PreviouslyDestroyed, bool PreviouslyDiscarded, bool PreviouslyDead);

        private readonly Dictionary<Pawn, NativeObservation> nativeScopes = new(ReferenceEqualityComparer.Instance);
        private readonly Queue<NativeObservation> nativeObservations = new();
        internal int PendingCultivatorObservations => nativeObservations.Count;
        // Set only by the live verification fixture.
        internal Action<string> CultivatorLifecycleTrace { get; set; }

        internal NativeScope BeginNativeObservation(Pawn pawn, NativePath path, Corpse body = null)
        {
            if (pawn == null || store == null || loadFailed || !store.ByPawn.TryGetValue(pawn, out var record))
                return null;
            if (!nativeScopes.TryGetValue(pawn, out var observation))
                nativeScopes.Add(pawn, observation = new NativeObservation(record, pawn));
            observation.Depth++;
            if (path == NativePath.Kill && !pawn.Dead) observation.KillDepth++;
            if (path == NativePath.CorpseDestroy) observation.CorpseDepth++;
            if (path == NativePath.Gc) observation.GcDepth++;
            if (CultivatorLifecycleTrace != null)
                Trace(observation, $"enter {path} depth={observation.Depth} gc={observation.GcDepth}");
            return new NativeScope(this, observation, path, body, body?.Destroyed == true, pawn.Discarded, pawn.Dead);
        }

        internal void ObservePostCorpse(Pawn pawn, bool discarded)
        {
            if (pawn == null || !nativeScopes.TryGetValue(pawn, out var observation)) return;
            if (CultivatorLifecycleTrace != null)
                Trace(observation, $"PostCorpseDestroy discarded={discarded} kill={observation.KillDepth} corpse={observation.CorpseDepth}");
            // The pinned native path without a corpse calls this from Pawn.Kill. Corpse.Destroy
            // and Pawn.Discard use the same notification and have their own positive evidence.
            if (!discarded && observation.KillDepth > 0 && observation.CorpseDepth == 0 && pawn.Dead && pawn.Corpse == null)
                observation.CorpselessDeath = true;
        }

        internal void EndNativeObservation(NativeScope scope, Exception error)
        {
            var observation = scope.Observation;
            var pawn = observation.Pawn;
            try
            {
                if (scope.Path == NativePath.CorpseDestroy && !scope.PreviouslyDestroyed && scope.Body.Destroyed)
                    observation.BodyId = scope.Body.GetUniqueLoadID();
                if (scope.Path == NativePath.Discard && !scope.PreviouslyDiscarded && pawn.Discarded)
                    observation.DiscardCause ??= observation.GcDepth > 0 ? "WorldPawnGC" : "NativeDiscard";
                if (scope.Path == NativePath.Gc && error != null && !pawn.Discarded && !HasNativeSaveOwner(pawn))
                    observation.InterruptedGc = true;

                if (CultivatorLifecycleTrace != null)
                    Trace(observation, $"exit {scope.Path} depth={observation.Depth} error={error?.GetType().Name ?? "none"} discarded={pawn.Discarded}");
            }
            finally
            {
                if (scope.Path == NativePath.Kill && !scope.PreviouslyDead) observation.KillDepth--;
                if (scope.Path == NativePath.CorpseDestroy) observation.CorpseDepth--;
                if (scope.Path == NativePath.Gc) observation.GcDepth--;
                if (--observation.Depth == 0)
                {
                    nativeScopes.Remove(pawn);
                    if (observation.BodyId != null || observation.CorpselessDeath || observation.DiscardCause != null
                        || observation.InterruptedGc || pawn.Dead != scope.PreviouslyDead)
                    {
                        observation.Tick = Find.TickManager?.TicksGame ?? 0;
                        observation.Life = pawn.Dead ? GuObservedLife.Dead : GuObservedLife.Alive;
                        nativeObservations.Enqueue(observation);
                    }
                }
            }
        }

        internal void ApplyNativeCultivatorObservations()
        {
            if (nativeScopes.Count != 0 || awakeningInProgress || loadFailed) return;
            while (nativeObservations.TryDequeue(out var observation))
            {
                var record = observation.Record;
                if (record.Association != GuNativeAssociation.Bound || !ReferenceEquals(record.Pawn, observation.Pawn))
                    continue;
                record.LastObservedLife = observation.Life;
                record.ObservationTick = observation.Tick;
                if (observation.BodyId != null)
                    record.LastCorpseDestruction = Fact(observation, "corpse:" + observation.BodyId, "Corpse.Destroy");
                if (observation.CorpselessDeath)
                    record.LastProvenCorpselessDeath = Fact(observation, $"corpse-less:{record.StateRevision}", "Pawn.Kill");
                if (observation.DiscardCause != null && record.ConfirmedDiscard == null)
                {
                    record.ConfirmedDiscard = Fact(observation, "discard", observation.DiscardCause);
                    UnbindObservedPawn(record, GuNativeAssociation.Detached);
                    if (CultivatorLifecycleTrace != null)
                        Trace(observation, $"commit discard cause={observation.DiscardCause} life={observation.Life}");
                }
                else if (observation.InterruptedGc)
                {
                    store.Evidence.Add(new GuCultivatorEvidence
                    {
                        Key = $"interrupted-gc:{record.Id}:{record.AssociationRevision}",
                        PawnToken = record.PawnLoadId, PawnClaims = [record.PawnLoadId],
                        RawEvidence = $"tick={observation.Tick};previousAssociation={record.Association}",
                        Reason = "GC removed native ownership before completing discard."
                    });
                    UnbindObservedPawn(record, GuNativeAssociation.Unresolved);
                    Trace(observation, "commit unresolved interrupted GC");
                }
                if (record.StateRevision < int.MaxValue) record.StateRevision++;
            }
        }

        private void UnbindObservedPawn(GuCultivatorRecord record, GuNativeAssociation result)
        {
            store.ByPawn.Remove(record.Pawn);
            foreach (var id in record.ApertureIds)
                if (store.ByAperture.GetValueOrDefault(id) == record) store.ByAperture.Remove(id);
            record.Association = result;
            record.Pawn = null;
        }

        private static GuCultivatorFact Fact(NativeObservation observation, string key, string cause) => new()
        {
            EventKey = $"{observation.Record.Id}:{observation.Record.AssociationRevision}:{key}",
            Tick = observation.Tick, Cause = cause, Life = observation.Life
        };

        private void Trace(NativeObservation observation, string message)
        {
            try { CultivatorLifecycleTrace?.Invoke($"{observation.Record.Id} {message}"); }
            catch (Exception error) { Log.Error($"[Project GU] Cultivator verification trace failed. {error}"); }
        }
    }
}
