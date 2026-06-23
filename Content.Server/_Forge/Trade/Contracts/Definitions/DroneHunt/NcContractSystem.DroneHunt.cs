using System.Numerics;
using Content.Shared._Forge.Trade;
using Content.Shared.Shuttles.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _contractMapLoader = default!;

    private readonly HashSet<string> _droneHuntCorePrototypeScratch = new(StringComparer.Ordinal);
    private List<Entity<MapGridComponent>> _droneHuntPlacementGridScratch = new();

    private ContractServerData CreateDroneHuntContractData(EntityUid store, NcDroneHuntContractPrototype proto)
    {
        var targetGroup = proto.TargetGroup;
        var rewards = BakeRewardsForContract(store, proto.ID, BuildDroneHuntRewardDefs(store, proto));

        var runtime = new ContractRuntimeContextData
        {
            Stage = 0,
            StageGoal = 1,
            Failed = false,
            FailureReason = string.Empty,
            StatusHint = string.Empty,
        };
        NormalizeRuntimeState(runtime);

        var config = new ContractObjectiveConfigData
        {
            GivePinpointer = true,
            ProofPrototype = proto.Completion.Proof,
            DroneHuntEnabled = true,
            DroneHuntGrids = CloneDroneHuntGridEntries(proto.Spawn.Grids),
            DroneHuntCorePrototypes = BuildDroneHuntCorePrototypeList(proto),
            DroneHuntMinDistance = proto.Spawn.MinDistance,
            DroneHuntMaxDistance = proto.Spawn.MaxDistance,
            DroneHuntSafetyRadius = proto.Spawn.SafetyRadius,
            DroneHuntPlacementAttempts = proto.Spawn.PlacementAttempts,
            HuntCompletionMode = NcHuntCompletionMode.TrophyTurnIn,
            SpawnPoint = CloneContractPointSelector(proto.Spawn.Point),
        };
        NormalizeObjectiveConfig(config);

        var contract = new ContractServerData
        {
            Id = proto.ID,
            Name = proto.Name,
            Icon = proto.Icon,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = ContractObjectiveType.Hunt,
            ExecutionKind = ContractExecutionKind.DroneHuntObjective,
            Runtime = runtime,
            Config = config,
            Conditions = CloneContractConditions(proto.Conditions),
            FlowStatus = ContractFlowStatus.Available,
            MatchMode = PrototypeMatchMode.Matcher,
            Targets =
            [
                new ContractTargetServerData
                {
                    TargetItem = targetGroup,
                    Required = 1,
                    Progress = 0,
                    MatchMode = PrototypeMatchMode.Matcher,
                },
            ],
            TargetItem = targetGroup,
            Required = 1,
            Progress = 0,
            Rewards = rewards,
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private static List<NcDroneHuntGridEntry> CloneDroneHuntGridEntries(IReadOnlyList<NcDroneHuntGridEntry> source)
    {
        var result = new List<NcDroneHuntGridEntry>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            if (entry == null)
                continue;

            result.Add(new NcDroneHuntGridEntry
            {
                Path = entry.Path,
                Weight = entry.Weight,
            });
        }

        return result;
    }

    private List<string> BuildDroneHuntCorePrototypeList(NcDroneHuntContractPrototype proto)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AppendUniqueStrings(result, seen, proto.CorePrototypes);

        if (!string.IsNullOrWhiteSpace(proto.TargetGroup) &&
            _prototypes.TryIndex<NcHuntGroupPrototype>(proto.TargetGroup, out var group))
        {
            AppendUniqueStrings(result, seen, group.Prototypes);
        }

        return result;
    }

    private static void AppendUniqueStrings(
        List<string> result,
        HashSet<string> seen,
        IReadOnlyList<string> values
    )
    {
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                continue;

            result.Add(value);
        }
    }

    private List<ContractRewardDef> BuildDroneHuntRewardDefs(EntityUid store, NcDroneHuntContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
        {
            TryAppendSupplyRewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);
        }

        return rewards;
    }

    private bool TryInitializeDroneHuntObjectiveRuntimeOnTake(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.DroneHuntActive = false;
        state.DroneHuntCoreTargets.Clear();
        state.DroneHuntGridEntities.Clear();
        state.LastKnownTargetCoordinates = null;

        ResetObjectiveState(contract);

        if (!TrySpawnDroneHuntGrid(key, contract, state, out var spawnCoords))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        state.DroneHuntActive = true;

        if (!contract.Config.GivePinpointer)
            return true;

        if (!TryResolveDroneHuntPinpointerTarget(store, state, out var pinpointerTarget))
        {
            CleanupObjectiveRuntime(store, contractId, true);
            return false;
        }

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, contract.Config, spawnCoords);
    }

    private bool TrySpawnDroneHuntGrid(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out EntityCoordinates spawnCoords
    )
    {
        spawnCoords = EntityCoordinates.Invalid;

        var attempts = Math.Max(1, contract.Config.DroneHuntPlacementAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!TryPickDroneHuntGrid(key.ContractId, contract.Config.DroneHuntGrids, out var gridPath))
                return false;

            if (!TryResolveDroneHuntSpawnCoordinates(key.Store, key.ContractId, contract.Config, out var spawnMapCoords))
                continue;

            EntityUid grid;
            try
            {
                if (!_contractMapLoader.TryLoadGrid(
                        spawnMapCoords.MapId,
                        gridPath,
                        out var loadedGrid,
                        offset: spawnMapCoords.Position,
                        rot: _random.NextAngle()))
                    continue;

                grid = loadedGrid.Value;
            }
            catch (Exception e)
            {
                Sawmill.Error(
                    $"[Contracts] Drone hunt runtime init failed for '{key.ContractId}': grid load '{gridPath}' threw: {e}");
                return false;
            }

            if (!TryComp(grid, out MapGridComponent? gridComp))
            {
                Sawmill.Warning(
                    $"[Contracts] Drone hunt runtime init failed for '{key.ContractId}': loaded '{gridPath}' is not a map grid.");
                Del(grid);
                continue;
            }

            if (!IsDroneHuntGridPlacementClear(grid, gridComp, contract.Config.DroneHuntSafetyRadius))
            {
                Del(grid);
                continue;
            }

            PrepareDroneHuntGrid(grid);
            state.DroneHuntGridEntities.Add(grid);
            spawnCoords = _xform.ToCoordinates(spawnMapCoords);

            if (TryRegisterDroneHuntCores(key, contract, state, grid))
                return true;

            Del(grid);
            state.DroneHuntGridEntities.Remove(grid);
        }

        Sawmill.Warning(
            $"[Contracts] Drone hunt runtime init failed for '{key.ContractId}': cannot place a drone grid after {attempts} attempts.");
        return false;
    }

    private bool TryPickDroneHuntGrid(
        string contractId,
        IReadOnlyList<NcDroneHuntGridEntry> grids,
        out ResPath path
    )
    {
        path = default;

        if (grids.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Drone hunt runtime init failed for '{contractId}': no grid paths configured.");
            return false;
        }

        var picked = PickWeighted(_random, grids, static entry => entry.Weight);
        path = picked.Path;
        return true;
    }

    private bool TryResolveDroneHuntSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractObjectiveConfigData config,
        out MapCoordinates spawnCoords
    )
    {
        spawnCoords = MapCoordinates.Nullspace;

        if (!TryResolveObjectiveSpawnCoordinates(store, config.SpawnPoint, out var anchorCoords, true) &&
            !TryGetHuntStoreFallbackCoordinates(store, out anchorCoords))
        {
            Sawmill.Warning(
                $"[Contracts] Drone hunt runtime init failed for '{contractId}': cannot resolve spawn anchor.");
            return false;
        }

        var anchorMapCoords = _xform.ToMapCoordinates(anchorCoords);
        if (anchorMapCoords.MapId == MapId.Nullspace)
            return false;

        var angle = _random.NextAngle();
        var direction = angle.ToVec();
        var minDistance = Math.Max(0f, config.DroneHuntMinDistance);
        var maxDistance = Math.Max(minDistance, config.DroneHuntMaxDistance);
        var distance = MathHelper.CloseTo(minDistance, maxDistance)
            ? minDistance
            : _random.NextFloat(minDistance, maxDistance);
        var lateral = (angle + Math.PI / 2).ToVec() *
                      _random.NextFloat(-config.DroneHuntSafetyRadius, config.DroneHuntSafetyRadius);

        var origin = GetHuntDebrisSpawnOrigin(store, anchorMapCoords, direction);
        spawnCoords = new MapCoordinates(origin + direction * distance + lateral, anchorMapCoords.MapId);
        return true;
    }

    private bool IsDroneHuntGridPlacementClear(EntityUid grid, MapGridComponent gridComp, float safetyRadius)
    {
        var xform = Transform(grid);
        if (xform.MapUid == null)
            return false;

        var bounds = _xform.GetWorldMatrix(xform)
            .TransformBox(gridComp.LocalAABB)
            .Enlarged(Math.Max(0f, safetyRadius));

        _droneHuntPlacementGridScratch.Clear();
        _mapManager.FindGridsIntersecting(
            xform.MapID,
            bounds,
            ref _droneHuntPlacementGridScratch,
            includeMap: false);

        for (var i = 0; i < _droneHuntPlacementGridScratch.Count; i++)
        {
            if (_droneHuntPlacementGridScratch[i].Owner != grid)
                return false;
        }

        return true;
    }

    private void PrepareDroneHuntGrid(EntityUid grid)
    {
        _shuttle.SetIFFColor(grid, Color.FromHex("#9a2020"));
        _shuttle.AddIFFFlag(grid, IFFFlags.AlwaysShowColor);
        _shuttle.RemoveIFFFlag(grid, IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
    }

    private bool TryRegisterDroneHuntCores(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid grid
    )
    {
        _droneHuntCorePrototypeScratch.Clear();
        for (var i = 0; i < contract.Config.DroneHuntCorePrototypes.Count; i++)
            _droneHuntCorePrototypeScratch.Add(contract.Config.DroneHuntCorePrototypes[i]);

        if (_droneHuntCorePrototypeScratch.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Drone hunt runtime init failed for '{key.ContractId}': no target core prototypes configured.");
            return false;
        }

        var found = 0;
        var proofAssigned = false;
        var query = EntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == grid || xform.GridUid != grid)
                continue;

            if (!TryGetPlanningEntityPrototypeId(uid, out var prototypeId) ||
                !_droneHuntCorePrototypeScratch.Contains(prototypeId))
                continue;

            var comp = EnsureComp<NcContractDroneCoreComponent>(uid);
            comp.Store = key.Store;
            comp.ContractId = key.ContractId;

            _objectiveRuntime.ByDroneCore[uid] = key;
            state.DroneHuntCoreTargets.Add(uid);
            found++;

            if (!proofAssigned)
            {
                AssignDroneHuntProofCore(key, state, uid);
                proofAssigned = true;
            }

            if (state.LastKnownTargetCoordinates == null && TryComp(uid, out TransformComponent? coreXform))
                state.LastKnownTargetCoordinates = coreXform.Coordinates;
        }

        _droneHuntCorePrototypeScratch.Clear();

        if (found > 0 && proofAssigned)
            return true;

        Sawmill.Warning(
            $"[Contracts] Drone hunt runtime init failed for '{key.ContractId}': loaded grid has no configured AI cores.");
        return false;
    }

    private void AssignDroneHuntProofCore(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid core
    )
    {
        var proof = EnsureComp<NcContractProofComponent>(core);
        proof.Store = key.Store;
        proof.ContractId = key.ContractId;
        proof.ProofToken = GetOrCreateObjectiveProofToken(state);

        state.ProofEntity = core;
        state.ProofSpawned = true;
        _objectiveRuntime.ByProof[core] = key;
    }
}
