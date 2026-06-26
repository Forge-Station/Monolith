using Content.Shared._Forge.Trade;
using Robust.Shared.Map;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryInitializeHuntObjectiveRuntimeOnTake(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        if (!IsSpawnedHuntContract(contract))
            return TryInitializeHuntObjective(store, user, contractId, contract);

        return TryInitializeSpawnedHuntObjective(store, user, contractId, contract);
    }

    private static bool IsSpawnedHuntContract(ContractServerData contract)
    {
        return contract.IsHuntObjective && contract.Config.HuntEnabled;
    }

    private static bool RequiresSpawnedHuntBodyTurnIn(ContractServerData contract)
    {
        return IsSpawnedHuntContract(contract) &&
               contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn;
    }

    private bool TryInitializeSpawnedHuntObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        if (contract.Config.HuntCompletionMode is not (NcHuntCompletionMode.TrophyTurnIn
            or NcHuntCompletionMode.BodyTurnIn))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': only TrophyTurnIn and BodyTurnIn are supported.");
            return false;
        }

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.TrophyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.ProofPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': TrophyTurnIn requires proof prototype.");
            return false;
        }

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.HuntBodyPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': BodyTurnIn requires a body target.");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = null;
        state.HuntBodyEntity = null;
        state.HuntDebrisEntity = null;
        state.HuntDungeonAnchorCoordinates = null;
        state.HuntDungeonSelfContained = false;
        state.HuntDungeonGenerationMap = null;
        state.HuntDungeonGridEntities.Clear();
        state.HuntSpawnedTargets.Clear();
        state.HuntTargetWasKilled = false;
        state.LastKnownTargetCoordinates = null;

        ResetObjectiveState(contract);

        if (!TrySpawnHuntTargets(store, contractId, contract, state))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        if (!state.HuntActive)
        {
            state.HuntActive = true;
            _objectiveRuntime.ActiveHuntObjectives.Add((store, contractId));
        }

        if (state.HuntDungeonGenerationTask != null)
        {
            state.HuntPendingPinpointerUser = user;
            if (contract.Config.GivePinpointer)
                TryIssuePendingSpawnedHuntPinpointer(store, user, contractId, contract, state);

            return true;
        }

        if (!contract.Config.GivePinpointer)
            return true;

        return TryIssueSpawnedHuntPinpointer(store, user, contractId, contract, state);
    }

    private bool TryIssueSpawnedHuntPinpointer(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (!TryResolveSpawnedHuntPinpointerTargetForUser(store, user, contract, state, out var pinpointerTarget))
            return false;

        var spawnCoords = EntityCoordinates.Invalid;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(user, out TransformComponent? userXform))
            spawnCoords = userXform.Coordinates;

        if (spawnCoords == EntityCoordinates.Invalid &&
            TryComp(pinpointerTarget, out TransformComponent? targetXform))
            spawnCoords = targetXform.Coordinates;

        if (spawnCoords == EntityCoordinates.Invalid)
            return false;

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, (store, contractId), state, contract.Config, spawnCoords);
    }

    private bool TryIssuePendingSpawnedHuntPinpointer(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        var spawnCoords = EntityCoordinates.Invalid;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(user, out TransformComponent? userXform))
            spawnCoords = userXform.Coordinates;

        if (spawnCoords == EntityCoordinates.Invalid)
            return false;

        return TrySpawnPendingObjectivePinpointer(user, (store, contractId), state, contract.Config, spawnCoords);
    }
}
