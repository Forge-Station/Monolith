using Content.Shared._Forge.Trade;
using Content.Shared.Destructible;
using Robust.Shared.Map;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void OnContractDroneCoreDestroyed(
        EntityUid uid,
        NcContractDroneCoreComponent component,
        DestructionEventArgs args
    )
    {
        var key = component.Store != EntityUid.Invalid && !string.IsNullOrWhiteSpace(component.ContractId)
            ? (component.Store, component.ContractId)
            : _objectiveRuntime.ByDroneCore.TryGetValue(uid, out var indexedKey)
                ? indexedKey
                : default;

        if (key.Store == EntityUid.Invalid || string.IsNullOrWhiteSpace(key.ContractId))
            return;

        HandleContractDroneCoreDestroyed(key, uid);
    }

    private void HandleContractDroneCoreDestroyed(
        (EntityUid Store, string ContractId) key,
        EntityUid core
    )
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            _objectiveRuntime.ByDroneCore.Remove(core);
            return;
        }

        RemoveDroneHuntCoreTarget(state, core);

        if (TryComp(core, out TransformComponent? coreXform))
            state.LastKnownTargetCoordinates = coreXform.Coordinates;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken || contract.Runtime.Failed || contract.Completed)
            return;

        var previousRequired = contract.Required;
        var previousProgress = contract.Progress;
        var previousStatus = contract.FlowStatus;

        if (TryGetLiveObjectiveProof(state, out var proof))
            RetargetObjectivePinpointers(key, state, proof);

        SyncDroneHuntObjectiveProgress(key.Store, key.ContractId, contract);
        RaiseContractsChangedIfSnapshotChanged(key, contract, previousRequired, previousProgress, previousStatus);
    }

    private ClaimAttemptResult TryClaimDroneHuntContract(
        EntityUid store,
        EntityUid user,
        string contractId,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        EnsureObjectiveRuntimeDefaults(contract);
        SyncDroneHuntObjectiveProgress(store, contractId, contract);

        if (contract.Runtime.Failed)
            return ClaimAttemptResult.Fail(ClaimFailureReason.ObjectiveFailed, contract.Runtime.FailureReason);

        if (!TryValidateDroneHuntProofClaim(store, user, contractId, contract, out var proofFail))
            return proofFail;

        if (!TryValidateContractRewards(user, contract.Rewards, out var rewardFail))
            return rewardFail;

        var snapshot = CaptureContractProgressSnapshot(contract);
        MarkObjectiveComplete(contract);

        if (!TryExecuteObjectiveClaimRewards(store, user, contractId, contract, out rewardFail))
        {
            snapshot.Restore(contract);
            return rewardFail;
        }

        FinalizeClaim(store, comp, contractId, contract.Repeatable);
        return ClaimAttemptResult.Ok();
    }

    private bool TryValidateDroneHuntProofClaim(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryGetObjectiveProofPrototype(contract, out var proofPrototype))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            string.IsNullOrWhiteSpace(state.ProofToken))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingProof,
                $"Drone hunt contract '{contractId}' requires a proof core, but no proof token is registered.");
            return false;
        }

        if (TryFindObjectiveProofEntity(store, user, key, contract, state, proofPrototype, out _))
            return true;

        fail = ClaimAttemptResult.Fail(
            ClaimFailureReason.MissingProof,
            $"Drone hunt contract '{contractId}' requires its proof core to be brought back to the store.");
        return false;
    }

    private void OnContractDroneCoreLost(
        (EntityUid Store, string ContractId) key,
        EntityUid core
    )
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        RemoveDroneHuntCoreTarget(state, core);

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
            return;

        if (!contract.Taken ||
            contract.Runtime.Failed ||
            contract.Completed ||
            contract.ExecutionKind != ContractExecutionKind.DroneHuntObjective)
            return;

        if (HasLiveDroneHuntCoreTarget(state) || HasLiveObjectiveProof(state))
            return;

        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-drone-hunt-target-lost"),
            deleteGuards: false);
    }

    private void SyncDroneHuntObjectiveProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            SyncObjectiveProgressFromRuntime(contract);
            ResetContractTargetProgress(contract);
            SyncContractFlowStatus(contract);
            return;
        }

        if (contract.Completed)
        {
            SyncObjectiveProgressFromRuntime(contract);
            ResetContractTargetProgress(contract);
            SyncContractFlowStatus(contract);
            return;
        }

        PruneLostDroneHuntCoreTargets(state);
        if (contract.Taken &&
            !contract.Runtime.Failed &&
            state.DroneHuntActive &&
            !HasLiveDroneHuntCoreTarget(state) &&
            !HasLiveObjectiveProof(state))
        {
            if (TryGetObjectiveContract(key, out var comp, out var liveContract))
            {
                FinalizeObjectiveTerminalOutcome(
                    key,
                    comp,
                    liveContract,
                    Loc.GetString("nc-store-contract-drone-hunt-target-lost"),
                    deleteGuards: false);
                return;
            }
        }

        SyncObjectiveProgressFromRuntime(contract);
        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);
    }

    private void RefreshDroneHuntObjectiveProgressFromProofScan(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems
    )
    {
        SyncDroneHuntObjectiveProgress(store, contractId, contract);

        if (!contract.Taken || contract.Runtime.Failed)
            return;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        var progress = HasDroneHuntProofInProgressSources(store, key, contract, state, userItems, crateItems)
            ? contract.Runtime.StageGoal
            : 0;

        SetObjectiveStage(contract, progress);
        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);
    }

    private bool HasDroneHuntProofInProgressSources(
        EntityUid store,
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems
    )
    {
        if (!TryGetObjectiveProofPrototype(contract, out var proofPrototype))
            return true;

        if (string.IsNullOrWhiteSpace(state.ProofToken))
            return false;

        if (ContainsMatchingDroneHuntProof(userItems, key, contract, state, proofPrototype))
            return true;

        if (ContainsMatchingDroneHuntProof(crateItems, key, contract, state, proofPrototype))
            return true;

        return TryFindNearbyStoreObjectiveProof(store, key, contract, state, proofPrototype, out _);
    }

    private bool ContainsMatchingDroneHuntProof(
        IReadOnlyList<EntityUid>? items,
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        string proofPrototype
    )
    {
        if (items == null)
            return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (IsMatchingObjectiveProof(items[i], key, contract, state, proofPrototype, out _))
                return true;
        }

        return false;
    }

    private void PruneLostDroneHuntCoreTargets(ObjectiveRuntimeState state)
    {
        for (var i = state.DroneHuntCoreTargets.Count - 1; i >= 0; i--)
        {
            var core = state.DroneHuntCoreTargets[i];
            if (core != EntityUid.Invalid && !TerminatingOrDeleted(core))
                continue;

            RemoveDroneHuntCoreTargetAt(state, i);
        }
    }

    private bool HasLiveDroneHuntCoreTarget(ObjectiveRuntimeState state)
    {
        for (var i = 0; i < state.DroneHuntCoreTargets.Count; i++)
        {
            var core = state.DroneHuntCoreTargets[i];
            if (core != EntityUid.Invalid && !TerminatingOrDeleted(core))
                return true;
        }

        return false;
    }

    private bool HasLiveObjectiveProof(ObjectiveRuntimeState state)
    {
        return TryGetLiveObjectiveProof(state, out _);
    }

    private bool TryGetLiveObjectiveProof(ObjectiveRuntimeState state, out EntityUid proof)
    {
        if (state.ProofEntity is { } existing &&
            existing != EntityUid.Invalid &&
            !TerminatingOrDeleted(existing))
        {
            proof = existing;
            return true;
        }

        proof = EntityUid.Invalid;
        return false;
    }

    private EntityCoordinates ResolveDroneHuntCompletionCoordinates(EntityUid store, ObjectiveRuntimeState state)
    {
        if (state.LastKnownTargetCoordinates is { } targetCoords && targetCoords != EntityCoordinates.Invalid)
            return targetCoords;

        if (TryComp(store, out TransformComponent? storeXform))
            return storeXform.Coordinates;

        return EntityCoordinates.Invalid;
    }

    private EntityCoordinates OffsetDroneHuntProofCoordinates(EntityCoordinates coords)
    {
        return coords == EntityCoordinates.Invalid
            ? coords
            : coords.Offset(_random.NextVector2(0.35f));
    }

    private bool TryResolveDroneHuntPinpointerTarget(
        EntityUid store,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        if (TryGetLiveObjectiveProof(state, out target))
            return true;

        return TryResolveDroneHuntCorePinpointerTarget(store, state, out target);
    }

    private bool TryResolveDroneHuntPinpointerTargetForUser(
        EntityUid store,
        EntityUid user,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        if (TryGetLiveObjectiveProof(state, out var proof))
        {
            target = TryGetContainedEntityRoot(proof, out var proofCarrier) && proofCarrier == user
                ? store
                : proof;
            return true;
        }

        return TryResolveDroneHuntCorePinpointerTarget(store, state, out target);
    }

    private bool TryResolveDroneHuntCorePinpointerTarget(
        EntityUid store,
        ObjectiveRuntimeState state,
        out EntityUid target
    )
    {
        target = EntityUid.Invalid;

        EntityUid best = EntityUid.Invalid;
        var bestDistance = float.MaxValue;
        var hasStorePosition = false;
        var storeCoords = MapCoordinates.Nullspace;
        if (TryComp(store, out TransformComponent? storeXform))
        {
            hasStorePosition = true;
            storeCoords = _xform.ToMapCoordinates(storeXform.Coordinates);
        }

        for (var i = state.DroneHuntCoreTargets.Count - 1; i >= 0; i--)
        {
            var core = state.DroneHuntCoreTargets[i];
            if (core == EntityUid.Invalid || TerminatingOrDeleted(core))
            {
                RemoveDroneHuntCoreTargetAt(state, i);
                continue;
            }

            if (!hasStorePosition)
            {
                best = core;
                break;
            }

            if (!TryComp(core, out TransformComponent? coreXform))
            {
                best = core;
                break;
            }

            var coreCoords = _xform.ToMapCoordinates(coreXform.Coordinates);
            if (coreCoords.MapId != storeCoords.MapId)
            {
                best = core;
                break;
            }

            var distance = (coreCoords.Position - storeCoords.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = core;
        }

        if (best == EntityUid.Invalid)
            return false;

        target = best;
        return true;
    }

    private void RemoveDroneHuntCoreTarget(ObjectiveRuntimeState state, EntityUid core)
    {
        state.DroneHuntCoreTargets.Remove(core);
        _objectiveRuntime.ByDroneCore.Remove(core);
    }

    private void RemoveDroneHuntCoreTargetAt(ObjectiveRuntimeState state, int index)
    {
        var core = state.DroneHuntCoreTargets[index];
        state.DroneHuntCoreTargets.RemoveAt(index);
        _objectiveRuntime.ByDroneCore.Remove(core);
    }
}
