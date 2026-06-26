using Content.Shared._Forge.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateDroneHuntContractForPool(string packId, NcDroneHuntContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[Contracts] Pack '{packId}' contains a drone-hunt contract with an empty prototype id.");
            return false;
        }

        if (!TryValidateDroneHuntTargets(proto.ID, proto))
            valid = false;

        if (!TryValidateDroneHuntCompletion(proto.ID, proto.Completion))
            valid = false;

        if (!TryValidateDroneHuntSpawn(proto.ID, proto.Spawn))
            valid = false;

        if (!TryValidateDroneHuntRewardsForPool(proto))
            valid = false;

        if (!TryValidateContractConditions(proto.ID, proto.Conditions))
            valid = false;

        return valid;
    }

    private bool TryValidateDroneHuntTargets(string contractId, NcDroneHuntContractPrototype proto)
    {
        if (string.IsNullOrWhiteSpace(proto.TargetGroup))
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' must define targetGroup.");
            return false;
        }

        var valid = true;
        var hasCore = false;

        if (!_prototypes.TryIndex<NcHuntGroupPrototype>(proto.TargetGroup, out var group))
        {
            Sawmill.Warning(
                $"[Contracts] Drone hunt contract '{contractId}' references missing ncHuntGroup '{proto.TargetGroup}'.");
            valid = false;
        }
        else
        {
            for (var i = 0; i < group.Prototypes.Count; i++)
            {
                var core = group.Prototypes[i];
                if (TryValidateDroneHuntCorePrototype(contractId, $"targetGroup '{group.ID}'.prototypes[{i}]", core))
                    hasCore = true;
                else
                    valid = false;
            }
        }

        for (var i = 0; i < proto.CorePrototypes.Count; i++)
        {
            var core = proto.CorePrototypes[i];
            if (TryValidateDroneHuntCorePrototype(contractId, $"corePrototypes[{i}]", core))
                hasCore = true;
            else
                valid = false;
        }

        if (hasCore)
            return valid;

        Sawmill.Warning(
            $"[Contracts] Drone hunt contract '{contractId}' has no valid AI core target prototypes.");
        return false;
    }

    private bool TryValidateDroneHuntCorePrototype(string contractId, string path, string prototype)
    {
        if (string.IsNullOrWhiteSpace(prototype))
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' {path} is empty.");
            return false;
        }

        if (_prototypes.HasIndex<EntityPrototype>(prototype))
            return true;

        Sawmill.Warning(
            $"[Contracts] Drone hunt contract '{contractId}' {path} references missing entity prototype '{prototype}'.");
        return false;
    }

    private bool TryValidateDroneHuntCompletion(string contractId, NcDroneHuntCompletionData completion)
    {
        if (string.IsNullOrWhiteSpace(completion.Proof))
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' completion.proof is required.");
            return false;
        }

        if (_prototypes.HasIndex<EntityPrototype>(completion.Proof))
            return true;

        Sawmill.Warning(
            $"[Contracts] Drone hunt contract '{contractId}' completion.proof references missing entity prototype '{completion.Proof}'.");
        return false;
    }

    private bool TryValidateDroneHuntSpawn(string contractId, NcDroneHuntSpawnData spawn)
    {
        var valid = true;

        if (spawn.Point == null)
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' must define spawn.point.");
            valid = false;
        }
        else if (!TryValidateDroneHuntSpawnPoint(contractId, spawn.Point))
        {
            valid = false;
        }

        if (spawn.Grids.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' spawn.grids must contain at least one grid.");
            valid = false;
        }

        for (var i = 0; i < spawn.Grids.Count; i++)
        {
            var entry = spawn.Grids[i];
            if (entry == null)
            {
                Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' spawn.grids[{i}] is empty.");
                valid = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Path.ToString()))
            {
                Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' spawn.grids[{i}] must define path.");
                valid = false;
            }
            else if (!_resources.ContentFileExists(entry.Path.ToRootedPath()))
            {
                Sawmill.Warning(
                    $"[Contracts] Drone hunt contract '{contractId}' spawn.grids[{i}] references missing grid file '{entry.Path}'.");
                valid = false;
            }

            if (entry.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Drone hunt contract '{contractId}' spawn.grids[{i}] weight must be > 0.");
                valid = false;
            }
        }

        if (spawn.MinDistance <= 0f)
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' spawn.minDistance must be > 0.");
            valid = false;
        }

        if (spawn.MaxDistance > 0f && spawn.MaxDistance < spawn.MinDistance)
        {
            Sawmill.Warning(
                $"[Contracts] Drone hunt contract '{contractId}' spawn.maxDistance must be >= minDistance.");
            valid = false;
        }

        if (spawn.SafetyRadius <= 0f)
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' spawn.safetyRadius must be > 0.");
            valid = false;
        }

        if (spawn.PlacementAttempts <= 0)
        {
            Sawmill.Warning($"[Contracts] Drone hunt contract '{contractId}' spawn.placementAttempts must be > 0.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateDroneHuntSpawnPoint(string contractId, ContractPointSelectorPrototype selector)
    {
        if (selector.Type == ContractPointSelectorType.Weighted)
        {
            if (selector.Options.Count == 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Drone hunt contract '{contractId}' spawn.point weighted selector has no options.");
                return false;
            }

            var valid = true;
            for (var i = 0; i < selector.Options.Count; i++)
            {
                if (IsContractPointOptionUsable(selector.Options[i]))
                    continue;

                Sawmill.Warning(
                    $"[Contracts] Drone hunt contract '{contractId}' spawn.point options[{i}] is invalid.");
                valid = false;
            }

            return valid;
        }

        if (selector.Type is ContractPointSelectorType.MarkerId or ContractPointSelectorType.MarkerGroup &&
            string.IsNullOrWhiteSpace(selector.Id))
        {
            Sawmill.Warning(
                $"[Contracts] Drone hunt contract '{contractId}' spawn.point type {selector.Type} requires id.");
            return false;
        }

        if (selector.Type is ContractPointSelectorType.Store
            or ContractPointSelectorType.MarkerId
            or ContractPointSelectorType.MarkerGroup)
            return true;

        Sawmill.Warning(
            $"[Contracts] Drone hunt contract '{contractId}' spawn.point type {selector.Type} is unsupported.");
        return false;
    }

    private bool TryValidateDroneHuntRewardsForPool(NcDroneHuntContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Drone hunt contract '{proto.ID}' has no reward entries. " +
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
            $"[Contracts] Drone hunt contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }
}
