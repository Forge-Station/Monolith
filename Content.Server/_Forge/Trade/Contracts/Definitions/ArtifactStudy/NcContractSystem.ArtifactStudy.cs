using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared._Forge.Trade;
using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateArtifactStudyContractData(EntityUid store, NcArtifactStudyContractPrototype proto)
    {
        var rewards = BakeRewardsForContract(store, proto.ID, BuildArtifactStudyRewardDefs(store, proto));

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
            TargetPrototype = proto.Artifact,
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
            ObjectiveType = ContractObjectiveType.ArtifactStudy,
            ExecutionKind = ContractExecutionKind.ArtifactStudyObjective,
            Runtime = runtime,
            Config = config,
            Conditions = CloneContractConditions(proto.Conditions),
            FlowStatus = ContractFlowStatus.Available,
            MatchMode = PrototypeMatchMode.Exact,
            Targets =
            [
                new ContractTargetServerData
                {
                    TargetItem = proto.Artifact,
                    Required = 1,
                    Progress = 0,
                    MatchMode = PrototypeMatchMode.Exact,
                },
            ],
            TargetItem = proto.Artifact,
            Required = 1,
            Progress = 0,
            Rewards = rewards,
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private List<ContractRewardDef> BuildArtifactStudyRewardDefs(EntityUid store,
        NcArtifactStudyContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
        {
            TryAppendSupplyRewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);
        }

        return rewards;
    }

    private bool TryValidateArtifactStudyContractForPool(string packId, NcArtifactStudyContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning(
                $"[Contracts] Pack '{packId}' contains an artifact-study contract with an empty prototype id.");
            return false;
        }

        if (!TryValidateArtifactStudyArtifact(proto.ID, proto.Artifact))
            valid = false;

        if (!TryValidateArtifactStudySpawn(proto.ID, proto.Spawn))
            valid = false;

        if (!TryValidateArtifactStudyRewardsForPool(proto))
            valid = false;

        if (!TryValidateContractConditions(proto.ID, proto.Conditions))
            valid = false;

        return valid;
    }

    private bool TryValidateArtifactStudyArtifact(string contractId, string artifactPrototype)
    {
        if (string.IsNullOrWhiteSpace(artifactPrototype))
        {
            Sawmill.Warning($"[Contracts] Artifact-study contract '{contractId}' must define artifact.");
            return false;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(artifactPrototype, out var proto))
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study contract '{contractId}' references missing artifact prototype '{artifactPrototype}'.");
            return false;
        }

        if (proto.HasComponent<ArtifactComponent>(_compFactory))
            return true;

        Sawmill.Warning(
            $"[Contracts] Artifact-study contract '{contractId}' artifact '{artifactPrototype}' must be a direct artifact entity, not a random spawner.");
        return false;
    }

    private bool TryValidateArtifactStudySpawn(string contractId, NcArtifactStudySpawnData spawn)
    {
        if (spawn.Point.Type == ContractPointSelectorType.Weighted)
            return TryValidateArtifactStudyWeightedSelector(contractId, spawn.Point);

        if (spawn.Point.Type is ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup &&
            string.IsNullOrWhiteSpace(spawn.Point.Id))
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study contract '{contractId}' spawn.point type {spawn.Point.Type} requires id.");
            return false;
        }

        return spawn.Point.Type is ContractPointSelectorType.Store
            or ContractPointSelectorType.MarkerId
            or ContractPointSelectorType.MarkerGroup;
    }

    private bool TryValidateArtifactStudyWeightedSelector(string contractId, ContractPointSelectorPrototype selector)
    {
        if (selector.Options.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study contract '{contractId}' spawn.point weighted selector has no options.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < selector.Options.Count; i++)
        {
            var option = selector.Options[i];
            if (IsContractPointOptionUsable(option))
                continue;

            Sawmill.Warning(
                $"[Contracts] Artifact-study contract '{contractId}' spawn.point options[{i}] is invalid.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateArtifactStudyRewardsForPool(NcArtifactStudyContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Artifact-study contract '{proto.ID}' has no reward entries. " +
                "Use 'reward' as a list with type: Currency, Item or Pool. Contract skipped.");
            return false;
        }

        var valid = true;
        var hasAtLeastOneValidReward = false;

        for (var i = 0; i < proto.Reward.Count; i++)
        {
            if (TryValidateSupplyRewardEntry(proto.ID, $"reward[{i}]", proto.Reward[i]))
                hasAtLeastOneValidReward = true;
            else
                valid = false;
        }

        if (hasAtLeastOneValidReward)
            return valid;

        Sawmill.Warning(
            $"[Contracts] Artifact-study contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }
}
