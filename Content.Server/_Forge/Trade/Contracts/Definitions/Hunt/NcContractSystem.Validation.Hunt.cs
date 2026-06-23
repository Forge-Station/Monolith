using Content.Shared._Forge.Trade;
using Content.Shared.Maps;
using Content.Shared.Procedural;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateHuntContractForPool(string packId, NcHuntContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[Contracts] Pack '{packId}' contains a hunt contract with an empty prototype id.");
            return false;
        }

        if (!TryValidateHuntTargets(proto.ID, proto.Targets))
            valid = false;

        if (!TryValidateHuntCompletion(proto.ID, proto.Completion, proto.Targets))
            valid = false;

        if (!TryValidateHuntSpawn(proto.ID, proto.Spawn))
            valid = false;

        if (!TryValidateHuntRewardsForPool(proto))
            valid = false;

        if (!TryValidateContractConditions(proto.ID, proto.Conditions))
            valid = false;

        return valid;
    }

    private bool TryValidateHuntSpawn(string contractId, NcHuntSpawnData spawn)
    {
        if (spawn.Point == null)
        {
            Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' must define spawn.point.");
            return false;
        }

        var valid = spawn.Point.Type switch
        {
            ContractPointSelectorType.MarkerId => RequireHuntSpawnPointId(contractId, spawn.Point),
            ContractPointSelectorType.MarkerGroup => RequireHuntSpawnPointId(contractId, spawn.Point),
            ContractPointSelectorType.Weighted => TryValidateHuntSpawnWeightedSelector(contractId, spawn.Point),
            ContractPointSelectorType.Store => true,
            _ => RejectHuntUnknownSpawnPoint(contractId, spawn.Point.Type),
        };

        if (!TryValidateHuntDebris(contractId, spawn.Debris))
            valid = false;

        if (!TryValidateHuntDungeons(contractId, spawn.Dungeons))
            valid = false;

        if (spawn.Dungeons.Count > 0)
        {
            if (!TryValidateHuntDungeonExteriorTilePreset(contractId, spawn.DungeonExteriorTilePreset))
                valid = false;

            if (!TryValidateHuntDungeonExteriorTiles(contractId, spawn.DungeonExteriorTiles))
                valid = false;

            if (!TryValidateHuntDungeonExteriorRockPreset(contractId, spawn.DungeonExteriorRockPreset))
                valid = false;

            if (!TryValidateHuntDungeonExteriorRocks(contractId, spawn.DungeonExteriorRocks))
                valid = false;
        }

        if (!TryValidateHuntDebrisPlacement(contractId, spawn))
            valid = false;

        return valid;
    }

    private bool TryValidateHuntDebris(string contractId, List<NcHuntDebrisEntry> debris)
    {
        if (debris.Count == 0)
            return true;

        var valid = true;
        for (var i = 0; i < debris.Count; i++)
        {
            var entry = debris[i];
            if (entry == null)
            {
                Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' spawn.debris[{i}] is empty.");
                valid = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.debris[{i}] must define prototype.");
                valid = false;
            }
            else if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.debris[{i}] references missing entity prototype '{entry.Prototype}'.");
                valid = false;
            }

            if (entry.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.debris[{i}] weight must be > 0.");
                valid = false;
            }
        }

        return valid;
    }

    private bool TryValidateHuntDungeons(string contractId, List<NcHuntDungeonEntry> dungeons)
    {
        if (dungeons.Count == 0)
            return true;

        var valid = true;
        for (var i = 0; i < dungeons.Count; i++)
        {
            var entry = dungeons[i];
            if (entry == null)
            {
                Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' spawn.dungeons[{i}] is empty.");
                valid = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.dungeons[{i}] must define prototype.");
                valid = false;
            }
            else if (!_prototypes.HasIndex<DungeonConfigPrototype>(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.dungeons[{i}] references missing dungeonConfig '{entry.Prototype}'.");
                valid = false;
            }

            if (entry.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.dungeons[{i}] weight must be > 0.");
                valid = false;
            }
        }

        return valid;
    }

    private bool TryValidateHuntDungeonExteriorTilePreset(string contractId, string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            return true;

        if (!_prototypes.TryIndex<NcHuntDungeonExteriorTilePresetPrototype>(presetId, out var preset))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.dungeonExteriorTilePreset references missing ncHuntDungeonExteriorTilePreset '{presetId}'.");
            return false;
        }

        return TryValidateHuntDungeonExteriorTileEntries(
            contractId,
            $"spawn.dungeonExteriorTilePreset '{presetId}'.entries",
            preset.Entries);
    }

    private bool TryValidateHuntDungeonExteriorTiles(
        string contractId,
        List<NcHuntDungeonExteriorTileEntry> tiles
    )
    {
        return TryValidateHuntDungeonExteriorTileEntries(contractId, "spawn.dungeonExteriorTiles", tiles);
    }

    private bool TryValidateHuntDungeonExteriorTileEntries(
        string contractId,
        string path,
        List<NcHuntDungeonExteriorTileEntry> tiles
    )
    {
        if (tiles.Count == 0)
            return true;

        var valid = true;
        for (var i = 0; i < tiles.Count; i++)
        {
            var entry = tiles[i];
            if (entry == null)
            {
                Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path}[{i}] is empty.");
                valid = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' {path}[{i}] must define prototype.");
                valid = false;
            }
            else if (!_prototypes.HasIndex<ContentTileDefinition>(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' {path}[{i}] references missing tile prototype '{entry.Prototype}'.");
                valid = false;
            }

            if (entry.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' {path}[{i}] weight must be > 0.");
                valid = false;
            }
        }

        return valid;
    }

    private bool TryValidateHuntDungeonExteriorRockPreset(string contractId, string presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            return true;

        if (!_prototypes.TryIndex<NcHuntDungeonExteriorRockPresetPrototype>(presetId, out var preset))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.dungeonExteriorRockPreset references missing ncHuntDungeonExteriorRockPreset '{presetId}'.");
            return false;
        }

        return TryValidateHuntDungeonExteriorRockEntries(
            contractId,
            $"spawn.dungeonExteriorRockPreset '{presetId}'.entries",
            preset.Entries);
    }

    private bool TryValidateHuntDungeonExteriorRocks(
        string contractId,
        List<NcHuntDungeonExteriorRockEntry> rocks
    )
    {
        return TryValidateHuntDungeonExteriorRockEntries(contractId, "spawn.dungeonExteriorRocks", rocks);
    }

    private bool TryValidateHuntDungeonExteriorRockEntries(
        string contractId,
        string path,
        List<NcHuntDungeonExteriorRockEntry> rocks
    )
    {
        if (rocks.Count == 0)
            return true;

        var valid = true;
        for (var i = 0; i < rocks.Count; i++)
        {
            var entry = rocks[i];
            if (entry == null)
            {
                Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' {path}[{i}] is empty.");
                valid = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' {path}[{i}] must define prototype.");
                valid = false;
            }
            else if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' {path}[{i}] references missing entity prototype '{entry.Prototype}'.");
                valid = false;
            }

            if (entry.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' {path}[{i}] weight must be > 0.");
                valid = false;
            }
        }

        return valid;
    }

    private static bool TryValidateHuntDebrisPlacement(string contractId, NcHuntSpawnData spawn)
    {
        var valid = true;

        if (spawn.DebrisMinDistance < 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.debrisMinDistance must be >= 0.");
            valid = false;
        }

        if (spawn.DebrisMaxDistance < 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.debrisMaxDistance must be >= 0.");
            valid = false;
        }

        if (spawn.DebrisMinDistance > 0 &&
            spawn.DebrisMaxDistance > 0 &&
            spawn.DebrisMaxDistance < spawn.DebrisMinDistance)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.debrisMaxDistance must be >= debrisMinDistance.");
            valid = false;
        }

        if (spawn.DebrisSafetyRadius < 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.debrisSafetyRadius must be >= 0.");
            valid = false;
        }

        if (spawn.DebrisPlacementAttempts < 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' spawn.debrisPlacementAttempts must be >= 0.");
            valid = false;
        }

        return valid;
    }

    private static bool RequireHuntSpawnPointId(string contractId, ContractPointSelectorPrototype selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Id))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt contract '{contractId}' spawn.point type {selector.Type} requires id.");
        return false;
    }

    private bool TryValidateHuntSpawnWeightedSelector(string contractId, ContractPointSelectorPrototype selector)
    {
        if (selector.Options.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' spawn.point weighted selector has no options.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < selector.Options.Count; i++)
        {
            var option = selector.Options[i];
            if (option.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' spawn.point options[{i}] weight must be > 0.");
                valid = false;
            }

            switch (option.Type)
            {
                case ContractPointSelectorType.MarkerId:
                case ContractPointSelectorType.MarkerGroup:
                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        Sawmill.Warning(
                            $"[Contracts] Hunt contract '{contractId}' spawn.point options[{i}] type {option.Type} requires id.");
                        valid = false;
                    }

                    break;

                default:
                    Sawmill.Warning(
                        $"[Contracts] Hunt contract '{contractId}' spawn.point options[{i}] type {option.Type} is not supported. Use MarkerId or MarkerGroup.");
                    valid = false;
                    break;
            }
        }

        return valid;
    }

    private static bool RejectHuntUnknownSpawnPoint(string contractId, ContractPointSelectorType type)
    {
        Sawmill.Warning(
            $"[Contracts] Hunt contract '{contractId}' spawn.point.type={type} is not supported.");
        return false;
    }

    private bool TryValidateHuntTargets(string contractId, List<NcHuntTargetData> targets)
    {
        if (targets.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' must define at least one targets entry.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < targets.Count; i++)
        {
            if (!TryValidateHuntTarget(contractId, i, targets[i]))
                valid = false;
        }

        return valid;
    }

    private bool TryValidateHuntTarget(string contractId, int index, NcHuntTargetData target)
    {
        var hasGroup = !string.IsNullOrWhiteSpace(target.Group);
        var hasPrototype = !string.IsNullOrWhiteSpace(target.Prototype);

        if (hasGroup == hasPrototype)
        {
            Sawmill.Warning(
                hasGroup
                    ? $"[Contracts] Hunt contract '{contractId}' targets[{index}] has both group and prototype. Use exactly one."
                    : $"[Contracts] Hunt contract '{contractId}' targets[{index}] has neither group nor prototype.");
            return false;
        }

        if (target.Count <= 0)
        {
            Sawmill.Warning($"[Contracts] Hunt contract '{contractId}' targets[{index}] count must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(target.Prototype))
                return true;

            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' targets[{index}] references missing entity prototype '{target.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcHuntGroupPrototype>(target.Group, out var group))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' targets[{index}] references missing ncHuntGroup '{target.Group}'.");
            return false;
        }

        return TryValidateHuntGroup(contractId, target.Group, group);
    }

    private bool TryValidateHuntGroup(string ownerId, string groupId, NcHuntGroupPrototype group)
    {
        if (group.Prototypes.Count == 0)
        {
            Sawmill.Warning($"[Contracts] Hunt group '{groupId}' used by '{ownerId}' has no prototypes.");
            return false;
        }

        var valid = true;
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var proto = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(proto))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt group '{groupId}' used by '{ownerId}' has empty prototypes[{i}].");
                valid = false;
                continue;
            }

            if (_prototypes.HasIndex<EntityPrototype>(proto))
                continue;

            Sawmill.Warning(
                $"[Contracts] Hunt group '{groupId}' used by '{ownerId}' references missing entity prototype '{proto}'.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateHuntCompletion(
        string contractId,
        NcHuntCompletionData completion,
        List<NcHuntTargetData> targets
    )
    {
        switch (completion.Mode)
        {
            case NcHuntCompletionMode.ConfirmedKill:
                if (!string.IsNullOrWhiteSpace(completion.Trophy))
                {
                    Sawmill.Warning(
                        $"[Contracts] Hunt contract '{contractId}' completion.mode=ConfirmedKill must not define trophy.");
                    return false;
                }

                return TryValidateHuntNoBodyTargets(contractId, completion.Mode, targets);

            case NcHuntCompletionMode.TrophyTurnIn:
                if (string.IsNullOrWhiteSpace(completion.Trophy))
                {
                    Sawmill.Warning(
                        $"[Contracts] Hunt contract '{contractId}' completion.mode=TrophyTurnIn requires trophy.");
                    return false;
                }

                if (_prototypes.HasIndex<EntityPrototype>(completion.Trophy))
                    return TryValidateHuntNoBodyTargets(contractId, completion.Mode, targets);

                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' references missing trophy prototype '{completion.Trophy}'.");
                return false;

            case NcHuntCompletionMode.BodyTurnIn:
                if (!string.IsNullOrWhiteSpace(completion.Trophy))
                {
                    Sawmill.Warning(
                        $"[Contracts] Hunt contract '{contractId}' completion.mode=BodyTurnIn must not define trophy.");
                    return false;
                }

                return TryValidateHuntBodyTurnInTarget(contractId, targets);

            default:
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' completion.mode={completion.Mode} is not supported.");
                return false;
        }
    }

    private bool TryValidateHuntNoBodyTargets(
        string contractId,
        NcHuntCompletionMode mode,
        List<NcHuntTargetData> targets
    )
    {
        for (var i = 0; i < targets.Count; i++)
        {
            if (!targets[i].Body)
                continue;

            Sawmill.Warning(
                $"[Contracts] Hunt contract '{contractId}' targets[{i}] uses body: true but completion.mode={mode}. " +
                "Use body: true only with BodyTurnIn.");
            return false;
        }

        return true;
    }

    private bool TryValidateHuntBodyTurnInTarget(string contractId, List<NcHuntTargetData> targets)
    {
        var bodyTargets = 0;
        var valid = true;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!target.Body)
                continue;

            bodyTargets++;
            if (string.IsNullOrWhiteSpace(target.Prototype) || !string.IsNullOrWhiteSpace(target.Group))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' targets[{i}] uses body: true. " +
                    "BodyTurnIn body target must be a single direct prototype target.");
                valid = false;
            }

            if (target.Count != 1)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt contract '{contractId}' targets[{i}] uses body: true with count={target.Count}. " +
                    "BodyTurnIn requires exactly one body target.");
                valid = false;
            }
        }

        if (bodyTargets == 1)
            return valid;

        Sawmill.Warning(
            bodyTargets == 0
                ? $"[Contracts] Hunt contract '{contractId}' completion.mode=BodyTurnIn requires exactly one targets entry with body: true."
                : $"[Contracts] Hunt contract '{contractId}' completion.mode=BodyTurnIn has {bodyTargets} body targets. Use exactly one.");
        return false;
    }

    private bool TryValidateHuntRewardsForPool(NcHuntContractPrototype proto)
    {
        if (proto.Reward.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt contract '{proto.ID}' has no reward entries. " +
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
            $"[Contracts] Hunt contract '{proto.ID}' has reward entries, but none of them are valid. Contract skipped.");
        return false;
    }
}
