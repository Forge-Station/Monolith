using Content.Shared.Mind;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Player;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static readonly TimeSpan HuntDebrisPendingDeletionCheckInterval = TimeSpan.FromSeconds(5);
    private readonly HashSet<EntityUid> _huntDebrisPendingDeletion = new();
    private readonly List<EntityUid> _huntDebrisPendingDeletionScratch = new();
    private TimeSpan _nextHuntDebrisPendingDeletionCheck = TimeSpan.Zero;

    private void CleanupObjectiveRuntime(
        EntityUid store,
        string contractId,
        bool deleteTrackedEntities,
        bool deleteGuards = true
    )
    {
        var key = (store, contractId);

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        if (state.TargetEntity is { } target)
        {
            _objectiveRuntime.ByTarget.Remove(target);
            state.TargetEntity = null;

            if (deleteTrackedEntities && !TerminatingOrDeleted(target))
                Del(target);
        }

        DeactivateTrackedDeliveryDropoff(key, state);

        CleanupHuntDungeonGenerationMap(state);
        state.HuntDungeonAnchorCoordinates = null;
        state.HuntDungeonGenerationTask = null;
        state.HuntPendingPinpointerUser = null;

        CleanupRetrievalSpawnedEntities(state, deleteTrackedEntities);
        CleanupDroneHuntRuntime(state, deleteTrackedEntities);
        CleanupSpawnedHuntBodyTarget(state, deleteTrackedEntities);
        CleanupHuntSpawnedTargets(state, deleteTrackedEntities);
        CleanupHuntDebris(state, deleteTrackedEntities);

        CleanupObjectivePinpointers(key, state);
        CleanupGhostRoleSurvivalObjective(state);

        if (state.GuardEntities.Count > 0)
        {
            for (var i = 0; i < state.GuardEntities.Count; i++)
            {
                var guard = state.GuardEntities[i];
                _objectiveRuntime.ByGuard.Remove(guard);

                if (deleteGuards && !TerminatingOrDeleted(guard))
                    Del(guard);
            }

            state.GuardEntities.Clear();
        }

        if (state.ProofEntity is { } proof)
        {
            _objectiveRuntime.ByProof.Remove(proof);

            if (!state.DroneHuntActive &&
                deleteTrackedEntities &&
                !TerminatingOrDeleted(proof))
            {
                Del(proof);
            }
        }

        state.ProofEntity = null;
        state.ProofSpawned = false;
        state.ProofToken = string.Empty;

        if (state.HuntActive)
        {
            state.HuntActive = false;
            _objectiveRuntime.ActiveHuntObjectives.Remove(key);
        }

        if (state.RetrievalRouteDeliveryActive)
        {
            state.RetrievalRouteDeliveryActive = false;
            _objectiveRuntime.ActiveRetrievalRouteDeliveries.Remove(key);
        }

        _objectiveRuntime.ActiveGhostRoleObjectives.Remove(key);

        state.RetrievalDeliveredEntities.Clear();
        state.DroneHuntActive = false;
        state.RetrievalAcceptedCargoCount = 0;
        state.RetrievalLastAcceptedCargoCoordinates = null;
        state.RetrievalRouteDeliveryCompleted = false;
        state.HuntTargetWasKilled = false;
        state.ArtifactStudyCompleted = false;
        state.ArtifactStudyNodeTotal = 0;
        state.ArtifactStudyTriggered = 0;
        state.GhostRoleSurvivalStart = null;
        state.GhostRoleSurvivalDeadline = null;
        state.GhostRoleSurvivalMind = null;
        state.GhostRoleSurvivalObjective = null;
        state.GhostRoleSurvivalSucceeded = false;
        state.LastKnownTargetCoordinates = null;
        _objectiveRuntime.ByContract.Remove(key);
    }

    private void CleanupDroneHuntRuntime(ObjectiveRuntimeState state, bool deleteTrackedEntities)
    {
        if (state.DroneHuntCoreTargets.Count > 0)
        {
            for (var i = state.DroneHuntCoreTargets.Count - 1; i >= 0; i--)
            {
                var core = state.DroneHuntCoreTargets[i];
                _objectiveRuntime.ByDroneCore.Remove(core);
            }

            state.DroneHuntCoreTargets.Clear();
        }

        if (state.DroneHuntGridEntities.Count == 0)
            return;

        for (var i = state.DroneHuntGridEntities.Count - 1; i >= 0; i--)
        {
            var grid = state.DroneHuntGridEntities[i];
            CleanupHuntDebrisEntity(grid, deleteTrackedEntities);
        }

        state.DroneHuntGridEntities.Clear();
    }

    private void CleanupHuntDungeonGenerationMap(ObjectiveRuntimeState state)
    {
        if (state.HuntDungeonGenerationMap is not { } map ||
            map == EntityUid.Invalid ||
            TerminatingOrDeleted(map))
        {
            state.HuntDungeonGenerationMap = null;
            return;
        }

        if (TryComp(map, out TransformComponent? xform))
            _map.DeleteMap(xform.MapID);

        state.HuntDungeonGenerationMap = null;
    }

    private void CleanupGhostRoleSurvivalObjective(ObjectiveRuntimeState state)
    {
        if (state.GhostRoleSurvivalObjective is not { } objective || objective == EntityUid.Invalid)
            return;

        if (state.GhostRoleSurvivalSucceeded)
        {
            if (!TerminatingOrDeleted(objective) &&
                TryComp(objective, out NcContractGhostRoleSurvivalObjectiveComponent? survival))
            {
                survival.Finished = true;
                survival.Succeeded = true;
            }

            return;
        }

        if (state.GhostRoleSurvivalMind is { } mindId &&
            TryComp(mindId, out MindComponent? mind))
            mind.Objectives.Remove(objective);

        if (!TerminatingOrDeleted(objective))
            Del(objective);
    }

    private void CleanupRetrievalSpawnedEntities(ObjectiveRuntimeState state, bool deleteSpawnedEntities)
    {
        if (state.RetrievalSpawnedEntities.Count == 0)
        {
            state.RetrievalSpawnedEntitySet.Clear();
            return;
        }

        for (var i = state.RetrievalSpawnedEntities.Count - 1; i >= 0; i--)
        {
            var ent = state.RetrievalSpawnedEntities[i];
            _objectiveRuntime.ByRetrievalCargo.Remove(ent);

            if (deleteSpawnedEntities && ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        state.RetrievalSpawnedEntities.Clear();
        state.RetrievalSpawnedEntitySet.Clear();
    }

    private void CleanupHuntSpawnedTargets(ObjectiveRuntimeState state, bool deleteSpawnedTargets)
    {
        if (state.HuntSpawnedTargets.Count == 0)
            return;

        for (var i = state.HuntSpawnedTargets.Count - 1; i >= 0; i--)
        {
            var ent = state.HuntSpawnedTargets[i];
            if (deleteSpawnedTargets && ent != EntityUid.Invalid && !TerminatingOrDeleted(ent))
                Del(ent);
        }

        state.HuntSpawnedTargets.Clear();
    }

    private void CleanupSpawnedHuntBodyTarget(ObjectiveRuntimeState state, bool deleteBody)
    {
        if (state.HuntBodyEntity is not { } body || body == EntityUid.Invalid)
            return;

        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);

        if (deleteBody && !TerminatingOrDeleted(body))
            Del(body);
    }

    private void CleanupHuntDebris(ObjectiveRuntimeState state, bool deleteDebris)
    {
        var debris = state.HuntDebrisEntity;
        state.HuntDebrisEntity = null;

        var debrisWasTracked = false;
        for (var i = state.HuntDungeonGridEntities.Count - 1; i >= 0; i--)
        {
            var grid = state.HuntDungeonGridEntities[i];
            if (grid == debris)
                debrisWasTracked = true;

            CleanupHuntDebrisEntity(grid, deleteDebris);
        }

        state.HuntDungeonGridEntities.Clear();

        if (debris is not { } debrisEntity ||
            debrisEntity == EntityUid.Invalid ||
            debrisWasTracked)
            return;

        CleanupHuntDebrisEntity(debrisEntity, deleteDebris);
    }

    private void CleanupHuntDebrisEntity(EntityUid debris, bool deleteDebris)
    {
        if (!deleteDebris || debris == EntityUid.Invalid || TerminatingOrDeleted(debris))
            return;

        if (HuntDebrisHasAttachedPlayerOrPulledEntity(debris))
        {
            QueueHuntDebrisDeletion(debris);
            return;
        }

        DeleteHuntDebrisEntity(debris);
    }

    private void QueueHuntDebrisDeletion(EntityUid debris)
    {
        if (debris == EntityUid.Invalid || TerminatingOrDeleted(debris))
            return;

        _huntDebrisPendingDeletion.Add(debris);
    }

    private void UpdatePendingHuntDebrisDeletion()
    {
        if (_huntDebrisPendingDeletion.Count == 0)
            return;

        _huntDebrisPendingDeletionScratch.Clear();
        foreach (var debris in _huntDebrisPendingDeletion)
        {
            if (debris == EntityUid.Invalid || TerminatingOrDeleted(debris))
            {
                _huntDebrisPendingDeletionScratch.Add(debris);
                continue;
            }

            if (HuntDebrisHasAttachedPlayerOrPulledEntity(debris))
                continue;

            DeleteHuntDebrisEntity(debris);
            _huntDebrisPendingDeletionScratch.Add(debris);
        }

        for (var i = 0; i < _huntDebrisPendingDeletionScratch.Count; i++)
        {
            _huntDebrisPendingDeletion.Remove(_huntDebrisPendingDeletionScratch[i]);
        }

        _huntDebrisPendingDeletionScratch.Clear();
    }

    private void ClearPendingHuntDebrisDeletion()
    {
        _huntDebrisPendingDeletion.Clear();
        _huntDebrisPendingDeletionScratch.Clear();
    }

    private void DeleteHuntDebrisEntity(EntityUid debris)
    {
        _linkedLifecycleGrid.UnparentPlayersFromGrid(debris, true);
    }

    private bool HuntDebrisHasAttachedPlayerOrPulledEntity(EntityUid debris)
    {
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (uid != EntityUid.Invalid &&
                xform.GridUid == debris &&
                !TerminatingOrDeleted(uid))
            {
                return true;
            }

            if (TryComp(uid, out PullerComponent? puller) &&
                IsPulledEntityOnHuntDebris(puller.Pulling, debris))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPulledEntityOnHuntDebris(EntityUid? pulled, EntityUid debris)
    {
        if (pulled is not { } pulledUid ||
            pulledUid == EntityUid.Invalid ||
            TerminatingOrDeleted(pulledUid) ||
            !TryComp(pulledUid, out TransformComponent? pulledXform))
        {
            return false;
        }

        return pulledXform.GridUid == debris;
    }
}
