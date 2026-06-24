using System.Threading.Tasks;
using Content.Shared._Forge.Trade;
using Content.Shared.Procedural;
using Robust.Shared.Map;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static bool IsTargetInEntityContainer(TransformComponent xform)
    {
        var parent = xform.ParentUid;
        if (parent == EntityUid.Invalid)
            return false;

        if (xform.MapUid is { } mapUid && parent == mapUid)
            return false;

        if (xform.GridUid is { } gridUid && parent == gridUid)
            return false;

        return true;
    }

    private void UpdateObjectiveContractProgress(EntityUid store, string contractId, ContractServerData contract)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        if (TryGetObjectiveHandler(contract.ExecutionKind, out var handler))
        {
            handler.RefreshObjectiveProgress(this, store, contractId, contract);
            return;
        }

        SyncObjectiveProgressFromRuntime(contract);
        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);
    }

    internal sealed class ObjectiveRuntimeState
    {
        public readonly List<EntityUid> GuardEntities = new();
        public readonly List<EntityUid> DroneHuntCoreTargets = new();
        public readonly List<EntityUid> DroneHuntGridEntities = new();
        public readonly List<EntityUid> HuntSpawnedTargets = new();
        public readonly HashSet<EntityUid> PinpointerEntities = new();
        public readonly HashSet<EntityUid> RetrievalDeliveredEntities = new();
        public readonly List<EntityUid> RetrievalSpawnedEntities = new();
        public readonly HashSet<EntityUid> RetrievalSpawnedEntitySet = new();
        public readonly Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int> TurnedInByTarget = new();
        public bool ActiveDeliveryDropoff;
        public bool ArtifactStudyCompleted;
        public int ArtifactStudyNodeTotal;
        public int ArtifactStudyTriggered;
        public bool DeliveryDropoffCompleted;
        public bool DroneHuntActive;
        public MapCoordinates? DeliveryDropoffCoordinates;
        public EntityUid? DeliveryDropoffEntity;
        public TimeSpan? GhostRoleAcceptDeadline;
        public long GhostRoleRoundEndId;
        public TimeSpan? GhostRoleSurvivalDeadline;
        public EntityUid? GhostRoleSurvivalMind;
        public EntityUid? GhostRoleSurvivalObjective;
        public TimeSpan? GhostRoleSurvivalStart;
        public bool GhostRoleSurvivalSucceeded;
        public bool GhostRoleTaken;
        public bool HuntActive;
        public EntityUid? HuntBodyEntity;
        public EntityUid? HuntDebrisEntity;
        public EntityCoordinates? HuntDungeonAnchorCoordinates;
        public bool HuntDungeonSelfContained;
        public EntityUid? HuntDungeonGenerationMap;
        public readonly List<EntityUid> HuntDungeonGridEntities = new();
        public Task<List<Dungeon>>? HuntDungeonGenerationTask;
        public EntityUid? HuntPendingPinpointerUser;
        public bool HuntTargetWasKilled;
        public EntityCoordinates? LastKnownTargetCoordinates;
        public EntityUid? ProofEntity;
        public bool ProofSpawned;
        public string ProofToken = string.Empty;
        public int RetrievalAcceptedCargoCount;
        public EntityCoordinates? RetrievalDeliveryCoordinates;
        public EntityCoordinates? RetrievalLastAcceptedCargoCoordinates;
        public bool RetrievalRouteDeliveryActive;
        public bool RetrievalRouteDeliveryCompleted;
        public EntityUid? TargetEntity;
    }
}
