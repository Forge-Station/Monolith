using Content.Shared._Forge.Trade;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static void NormalizeRuntimeState(ContractRuntimeContextData runtime)
    {
        runtime.StageGoal = runtime.StageGoal > 0
            ? runtime.StageGoal
            : NcContractTuning.DefaultObjectiveStageGoal;
        runtime.Stage = Math.Clamp(runtime.Stage, 0, runtime.StageGoal);
        runtime.AcceptTimeoutRemainingSeconds = Math.Max(0, runtime.AcceptTimeoutRemainingSeconds);
        runtime.GhostRoleSurvivalRemainingSeconds = Math.Max(0, runtime.GhostRoleSurvivalRemainingSeconds);
    }

    private static void NormalizeObjectiveConfig(ContractObjectiveConfigData config)
    {
        config.AcceptTimeoutSeconds = Math.Max(0, config.AcceptTimeoutSeconds);
        config.SpawnPoint = NormalizeContractPointSelector(config.SpawnPoint, true);
        config.DropoffPoint = NormalizeContractPointSelector(config.DropoffPoint, false);
        config.GhostRoleSurvivalDurationSeconds = NormalizePositiveOrDefault(
            config.GhostRoleSurvivalDurationSeconds,
            NcGhostRoleSurvivalData.DefaultDurationSeconds);
        config.PinpointerPrototype = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        config.GuardCount = Math.Max(0, config.GuardCount);
        NormalizeHuntDebrisConfig(config.HuntDebris);
        NormalizeHuntDungeonConfig(config.HuntDungeons);
        NormalizeHuntDungeonExteriorTileConfig(config.HuntDungeonExteriorTiles);
        NormalizeHuntDungeonExteriorRockConfig(config.HuntDungeonExteriorRocks);
        NormalizeHuntDebrisPlacementConfig(config);
        NormalizeDroneHuntConfig(config);

        NormalizeRetrievalSpawnConfig(config);
        config.RetrievalDestinationRadius = Math.Max(0.25f, config.RetrievalDestinationRadius);
        config.RetrievalDestinationPoint = NormalizeContractPointSelector(config.RetrievalDestinationPoint, false);

        NormalizeRetrievalClaimConfig(config);
        config.RetrievalGuidancePinpointerPrototype =
            ResolvePinpointerPrototypeId(config.RetrievalGuidancePinpointerPrototype);
        config.RetrievalGuidanceMaxActivePinpointers = Math.Max(0, config.RetrievalGuidanceMaxActivePinpointers);

        RemoveBlankStrings(config.SpawnSpecific);
    }

    private static void NormalizeHuntDebrisConfig(List<NcHuntDebrisEntry> debris)
    {
        for (var i = debris.Count - 1; i >= 0; i--)
        {
            var entry = debris[i];
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.Prototype) ||
                entry.Weight <= 0)
            {
                debris.RemoveAt(i);
            }
        }
    }

    private static void NormalizeHuntDungeonConfig(List<NcHuntDungeonEntry> dungeons)
    {
        for (var i = dungeons.Count - 1; i >= 0; i--)
        {
            var entry = dungeons[i];
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.Prototype) ||
                entry.Weight <= 0)
            {
                dungeons.RemoveAt(i);
            }
        }
    }

    private static void NormalizeHuntDungeonExteriorTileConfig(List<NcHuntDungeonExteriorTileEntry> tiles)
    {
        for (var i = tiles.Count - 1; i >= 0; i--)
        {
            var entry = tiles[i];
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.Prototype) ||
                entry.Weight <= 0)
            {
                tiles.RemoveAt(i);
            }
        }
    }

    private static void NormalizeHuntDungeonExteriorRockConfig(List<NcHuntDungeonExteriorRockEntry> rocks)
    {
        for (var i = rocks.Count - 1; i >= 0; i--)
        {
            var entry = rocks[i];
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.Prototype) ||
                entry.Weight <= 0)
            {
                rocks.RemoveAt(i);
            }
        }
    }

    private static void NormalizeHuntDebrisPlacementConfig(ContractObjectiveConfigData config)
    {
        config.HuntDebrisMinDistance = NormalizePositiveOrDefault(
            config.HuntDebrisMinDistance,
            NcContractTuning.HuntDebrisMinSpawnDistance);
        config.HuntDebrisMaxDistance = NormalizePositiveOrDefault(
            config.HuntDebrisMaxDistance,
            NcContractTuning.HuntDebrisMaxSpawnDistance);

        if (config.HuntDebrisMaxDistance < config.HuntDebrisMinDistance)
            config.HuntDebrisMaxDistance = config.HuntDebrisMinDistance;

        config.HuntDebrisSafetyRadius = NormalizePositiveOrDefault(
            config.HuntDebrisSafetyRadius,
            NcContractTuning.HuntDebrisSpawnSafetyRadius);
        config.HuntDebrisPlacementAttempts = NormalizePositiveOrDefault(
            config.HuntDebrisPlacementAttempts,
            NcContractTuning.HuntDebrisSpawnPlacementAttempts);
    }

    private static void NormalizeDroneHuntConfig(ContractObjectiveConfigData config)
    {
        if (!config.DroneHuntEnabled)
        {
            config.DroneHuntGrids.Clear();
            config.DroneHuntCorePrototypes.Clear();
            config.DroneHuntMinDistance = 0f;
            config.DroneHuntMaxDistance = 0f;
            config.DroneHuntSafetyRadius = 0f;
            config.DroneHuntPlacementAttempts = 0;
            return;
        }

        for (var i = config.DroneHuntGrids.Count - 1; i >= 0; i--)
        {
            var entry = config.DroneHuntGrids[i];
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.Path.ToString()) ||
                entry.Weight <= 0)
            {
                config.DroneHuntGrids.RemoveAt(i);
            }
        }

        RemoveBlankStrings(config.DroneHuntCorePrototypes);

        config.DroneHuntMinDistance = NormalizePositiveOrDefault(
            config.DroneHuntMinDistance,
            NcContractTuning.HuntDebrisMinSpawnDistance);
        config.DroneHuntMaxDistance = NormalizePositiveOrDefault(
            config.DroneHuntMaxDistance,
            config.DroneHuntMinDistance);

        if (config.DroneHuntMaxDistance < config.DroneHuntMinDistance)
            config.DroneHuntMaxDistance = config.DroneHuntMinDistance;

        config.DroneHuntSafetyRadius = NormalizePositiveOrDefault(
            config.DroneHuntSafetyRadius,
            NcContractTuning.HuntDebrisSpawnSafetyRadius);
        config.DroneHuntPlacementAttempts = NormalizePositiveOrDefault(
            config.DroneHuntPlacementAttempts,
            NcContractTuning.HuntDebrisSpawnPlacementAttempts);
    }

    private static int NormalizePositiveOrDefault(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    private static float NormalizePositiveOrDefault(float value, float fallback)
    {
        return value > 0 ? value : fallback;
    }

    private static void NormalizeRetrievalSpawnConfig(ContractObjectiveConfigData config)
    {
        if (!config.RetrievalSpawnEnabled)
        {
            ClearRetrievalSpawnConfig(config);
            return;
        }

        config.RetrievalSpawnPoint = NormalizeContractPointSelector(
            config.RetrievalSpawnPoint,
            config.RetrievalSpaceSpawnEnabled || config.RetrievalSpawnFallbackToStore);

        if (config.RetrievalSpawnPoint != null)
        {
            NormalizeRetrievalSpaceSpawnConfig(config);
            return;
        }

        config.RetrievalSpawnEnabled = false;
        config.RetrievalRequireSpawnedEntities = false;
    }

    private static void NormalizeRetrievalSpaceSpawnConfig(ContractObjectiveConfigData config)
    {
        if (!config.RetrievalSpaceSpawnEnabled)
            return;

        config.RetrievalSpaceSpawnMinDistance = NormalizePositiveOrDefault(
            config.RetrievalSpaceSpawnMinDistance,
            NcContractTuning.RetrievalSpaceSpawnDistance);
        config.RetrievalSpaceSpawnMaxDistance = NormalizePositiveOrDefault(
            config.RetrievalSpaceSpawnMaxDistance,
            config.RetrievalSpaceSpawnMinDistance);

        if (config.RetrievalSpaceSpawnMaxDistance < config.RetrievalSpaceSpawnMinDistance)
            config.RetrievalSpaceSpawnMaxDistance = config.RetrievalSpaceSpawnMinDistance;

        config.RetrievalSpaceSpawnSafetyRadius = NormalizePositiveOrDefault(
            config.RetrievalSpaceSpawnSafetyRadius,
            NcContractTuning.RetrievalSpaceSpawnSafetyRadius);
        config.RetrievalSpaceSpawnPlacementAttempts = NormalizePositiveOrDefault(
            config.RetrievalSpaceSpawnPlacementAttempts,
            NcContractTuning.RetrievalSpaceSpawnPlacementAttempts);
    }

    private static void ClearRetrievalSpawnConfig(ContractObjectiveConfigData config)
    {
        config.RetrievalSpawnPoint = null;
        config.RetrievalSpawnFallbackToStore = false;
        config.RetrievalRequireSpawnedEntities = false;
        config.RetrievalSpaceSpawnEnabled = false;
        config.RetrievalSpaceSpawnMinDistance = 0f;
        config.RetrievalSpaceSpawnMaxDistance = 0f;
        config.RetrievalSpaceSpawnSafetyRadius = 0f;
        config.RetrievalSpaceSpawnPlacementAttempts = 0;
    }

    private static void NormalizeRetrievalClaimConfig(ContractObjectiveConfigData config)
    {
        if (config.RetrievalClaimMode != NcRetrievalClaimMode.StoreCargo)
            return;

        config.RetrievalProofEnabled = false;
        config.RetrievalProofConsumeOnRewardClaim = false;
        config.RetrievalProofOwnership = NcRetrievalProofOwnership.Bearer;
        config.RetrievalProofReissue = NcRetrievalProofReissuePolicy.Never;

        if (!string.IsNullOrWhiteSpace(config.RetrievalRouteId))
            config.ProofPrototype = string.Empty;
    }

    private static void RemoveBlankStrings(List<string> values)
    {
        for (var i = values.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
                values.RemoveAt(i);
        }
    }

    private static ContractPointSelectorPrototype? CloneContractPointSelector(ContractPointSelectorPrototype? selector)
    {
        if (selector == null)
            return null;

        var sourceOptions = selector.Options;
        var clone = new ContractPointSelectorPrototype
        {
            Type = selector.Type,
            Id = selector.Id,
            Options = new List<WeightedContractPointOptionEntry>(sourceOptions.Count),
        };

        for (var i = 0; i < sourceOptions.Count; i++)
        {
            clone.Options.Add(sourceOptions[i]);
        }

        return clone;
    }

    private static ContractPointSelectorPrototype? NormalizeContractPointSelector(
        ContractPointSelectorPrototype? selector,
        bool defaultToStore
    )
    {
        if (selector == null)
            return defaultToStore ? new ContractPointSelectorPrototype() : null;

        return selector.Type switch
        {
            ContractPointSelectorType.Store => NormalizeStorePointSelector(selector),
            ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup =>
                NormalizeNamedPointSelector(selector, defaultToStore),
            ContractPointSelectorType.Weighted => NormalizeWeightedPointSelector(selector, defaultToStore),
            _ => GetFallbackPointSelector(defaultToStore),
        };
    }

    private static ContractPointSelectorPrototype NormalizeStorePointSelector(ContractPointSelectorPrototype selector)
    {
        selector.Id = string.Empty;
        selector.Options.Clear();
        return selector;
    }

    private static ContractPointSelectorPrototype? NormalizeNamedPointSelector(
        ContractPointSelectorPrototype selector,
        bool defaultToStore
    )
    {
        selector.Options.Clear();
        return !string.IsNullOrWhiteSpace(selector.Id)
            ? selector
            : GetFallbackPointSelector(defaultToStore);
    }

    private static ContractPointSelectorPrototype? NormalizeWeightedPointSelector(
        ContractPointSelectorPrototype selector,
        bool defaultToStore
    )
    {
        RemoveInvalidPointOptions(selector.Options);
        selector.Id = string.Empty;
        return selector.Options.Count > 0
            ? selector
            : GetFallbackPointSelector(defaultToStore);
    }

    private static ContractPointSelectorPrototype? GetFallbackPointSelector(bool defaultToStore)
    {
        return defaultToStore ? new ContractPointSelectorPrototype() : null;
    }

    private static void RemoveInvalidPointOptions(List<WeightedContractPointOptionEntry> options)
    {
        for (var i = options.Count - 1; i >= 0; i--)
        {
            if (!IsContractPointOptionUsable(options[i]))
                options.RemoveAt(i);
        }
    }

    private static bool IsContractPointOptionUsable(in WeightedContractPointOptionEntry option)
    {
        return option.Weight > 0 && option.Type switch
        {
            ContractPointSelectorType.Store => true,
            ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup =>
                !string.IsNullOrWhiteSpace(option.Id),
            _ => false,
        };
    }

    private static ContractFlowStatus ComputeContractFlowStatus(ContractServerData contract)
    {
        var runtime = contract.Runtime;

        if (runtime.Failed)
            return ContractFlowStatus.Failed;

        if (!contract.Taken)
            return ContractFlowStatus.Available;

        if (contract.Completed)
            return ContractFlowStatus.ReadyToTurnIn;

        if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && runtime.GhostRolePendingAcceptance)
            return ContractFlowStatus.AwaitingActivation;

        return ContractFlowStatus.InProgress;
    }

    private static void SyncContractFlowStatus(ContractServerData contract)
    {
        contract.FlowStatus = ComputeContractFlowStatus(contract);
    }

    private static string ResolveObjectiveTargetId(ContractObjectiveConfigData config)
    {
        if (!string.IsNullOrWhiteSpace(config.TargetPrototype))
            return config.TargetPrototype;

        if (!string.IsNullOrWhiteSpace(config.GhostRolePrototype))
            return config.GhostRolePrototype;

        return string.Empty;
    }

    private static string ResolveTrackedObjectivePrototypeId(string? runtimePrototype, string? fallbackTargetId)
    {
        return !string.IsNullOrWhiteSpace(runtimePrototype)
            ? runtimePrototype
            : fallbackTargetId ?? string.Empty;
    }

    private static string ResolvePinpointerPrototypeId(string? prototypeId)
    {
        return string.IsNullOrWhiteSpace(prototypeId)
            ? NcContractTuning.DefaultContractPinpointerPrototypeId
            : prototypeId;
    }

    private static void ResetContractTargetProgress(ContractServerData contract)
    {
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            target.Progress = 0;
            targets[i] = target;
        }
    }
}
