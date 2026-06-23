using System.Numerics;
using Content.Shared.Procedural;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems;
using Content.Shared._Forge.Trade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static readonly Vector2i[] HuntDungeonExteriorCardinalOffsets =
    {
        new(0, 1),
        new(0, -1),
        new(1, 0),
        new(-1, 0),
    };

    private List<Entity<MapGridComponent>> _huntDebrisPlacementGridScratch = new();

    private bool TrySpawnHuntTargets(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        List<EntityCoordinates>? siteSpawnCoordinates = null;

        if (contract.Config.HuntDungeons.Count > 0)
            return TryStartHuntDungeonGeneration(store, contractId, contract, state);

        if (contract.Config.HuntDebris.Count > 0)
        {
            if (!TrySpawnHuntDebris(store, contractId, contract, state, out siteSpawnCoordinates))
                return false;
        }

        return TrySpawnHuntTargetEntities(store, contractId, contract, state, siteSpawnCoordinates);
    }

    private bool TrySpawnHuntTargetEntities(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        List<EntityCoordinates>? siteSpawnCoordinates
    )
    {
        var targets = GetEffectiveTargets(contract);
        var required = Math.Max(1, CalculateTotalRequired(targets));

        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var targetDef = targets[targetIndex];
            var targetRequired = Math.Max(0, targetDef.Required);
            if (targetRequired <= 0)
                continue;

            for (var i = 0; i < targetRequired; i++)
            {
                if (!TryResolveSpawnedHuntPrototype(contractId, targetDef, out var targetProtoId))
                    return false;

                if (!TryResolveHuntTargetSpawnCoordinates(
                        store,
                        contractId,
                        contract,
                        siteSpawnCoordinates,
                        out var spawnCoords))
                {
                    return false;
                }

                if (!TrySpawnObjectiveTarget(contractId, targetProtoId, spawnCoords, out var target))
                    return false;

                state.HuntSpawnedTargets.Add(target);
                if (targetDef.BodyRequired)
                    state.HuntBodyEntity = target;

                if (state.LastKnownTargetCoordinates == null && TryComp(target, out TransformComponent? targetXform))
                    state.LastKnownTargetCoordinates = targetXform.Coordinates;
            }
        }

        return state.HuntSpawnedTargets.Count == required;
    }

    private bool TryStartHuntDungeonGeneration(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var anchorCoords, true) &&
            !TryGetHuntStoreFallbackCoordinates(store, out anchorCoords))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve dungeon spawn point.");
            return false;
        }

        state.HuntDungeonAnchorCoordinates = anchorCoords;

        return TryQueueNextHuntDungeonGeneration(contractId, contract, state);
    }

    private bool TryQueueNextHuntDungeonGeneration(
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (state.HuntDungeonAnchorCoordinates == null)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve dungeon spawn anchor.");
            return false;
        }

        if (!TryPickHuntDungeonPrototype(contractId, contract.Config.HuntDungeons, out var dungeonPrototype))
            return false;

        state.HuntDungeonSelfContained = IsSelfContainedHuntDungeonPrototype(dungeonPrototype);
        var dungeonConfig = _prototypes.Index<DungeonConfigPrototype>(dungeonPrototype);
        var generationMap = _map.CreateMap(out var generationMapId, runMapInit: false);
        Entity<MapGridComponent> grid;
        try
        {
            grid = _mapManager.CreateGridEntity(generationMapId);
            _xform.SetMapCoordinates(grid, new MapCoordinates(Vector2.Zero, generationMapId));
            _mapManager.DoMapInitialize(generationMapId);
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot create dungeon grid: {e}");
            _map.DeleteMap(generationMapId);
            return false;
        }

        try
        {
            state.HuntDebrisEntity = grid.Owner;
            state.HuntDungeonGenerationMap = generationMap;
            state.HuntDungeonGenerationTask = _dungeon.GenerateDungeonAsync(
                dungeonConfig,
                dungeonConfig.ID,
                grid.Owner,
                grid.Comp,
                Vector2i.Zero,
                _random.Next());
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Hunt runtime init failed for '{contractId}': dungeon generation '{dungeonPrototype}' threw: {e}");

            state.HuntDebrisEntity = null;
            state.HuntDungeonGenerationMap = null;
            _map.DeleteMap(generationMapId);
            return false;
        }
    }

    private bool TryResolveHuntTargetSpawnCoordinates(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        List<EntityCoordinates>? debrisSpawnCoordinates,
        out EntityCoordinates spawnCoords
    )
    {
        spawnCoords = EntityCoordinates.Invalid;

        if (debrisSpawnCoordinates is { Count: > 0 })
        {
            var index = _random.Next(debrisSpawnCoordinates.Count);
            spawnCoords = debrisSpawnCoordinates[index];

            if (debrisSpawnCoordinates.Count > 1)
                debrisSpawnCoordinates.RemoveAt(index);

            return true;
        }

        if (TryResolveObjectiveSpawnCoordinates(store, contract.Config, out spawnCoords, false))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve hunt spawn point.");
        return false;
    }

    private bool TrySpawnHuntDebris(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        out List<EntityCoordinates> spawnCoordinates
    )
    {
        spawnCoordinates = new List<EntityCoordinates>();

        if (!TryResolveObjectiveSpawnCoordinates(store, contract.Config, out var debrisCoords, true) &&
            !TryGetHuntStoreFallbackCoordinates(store, out debrisCoords))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': cannot resolve debris spawn point.");
            return false;
        }

        if (!TryPickHuntDebrisPrototype(contractId, contract.Config.HuntDebris, out var debrisPrototype))
            return false;

        var attempts = Math.Max(1, contract.Config.HuntDebrisPlacementAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!TryGetHuntDebrisSpawnMapCoordinates(store, contract.Config, debrisCoords, out var spawnMapCoords))
                continue;

            EntityUid debris;
            try
            {
                debris = Spawn(debrisPrototype, spawnMapCoords);
            }
            catch (Exception e)
            {
                Sawmill.Error(
                    $"[Contracts] Hunt runtime init failed for '{contractId}': debris spawn '{debrisPrototype}' threw: {e}");
                return false;
            }

            state.HuntDebrisEntity = debris;
            ForceLoadHuntDebris(debris);

            if (!TryComp(debris, out MapGridComponent? grid))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt runtime init failed for '{contractId}': debris '{debrisPrototype}' is not a map grid.");
                Del(debris);
                state.HuntDebrisEntity = null;
                return false;
            }

            if (!IsHuntDebrisGridPlacementClear(debris, grid, contract.Config.HuntDebrisSafetyRadius))
            {
                Del(debris);
                state.HuntDebrisEntity = null;
                continue;
            }

            CollectHuntDebrisSpawnCoordinates(debris, grid, spawnCoordinates);
            if (spawnCoordinates.Count > 0)
                return true;

            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': debris '{debrisPrototype}' has no valid spawn tiles.");
            Del(debris);
            state.HuntDebrisEntity = null;
        }

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': cannot find a free debris placement after {attempts} attempts.");
        return false;
    }

    private bool TryGetHuntStoreFallbackCoordinates(EntityUid store, out EntityCoordinates coordinates)
    {
        if (TryComp(store, out TransformComponent? storeXform))
        {
            coordinates = storeXform.Coordinates;
            return coordinates != EntityCoordinates.Invalid;
        }

        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryGetHuntDebrisSpawnMapCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        EntityCoordinates debrisCoords,
        out MapCoordinates spawnCoords
    )
    {
        spawnCoords = MapCoordinates.Nullspace;
        var anchorCoords = _xform.ToMapCoordinates(debrisCoords);
        if (anchorCoords.MapId == MapId.Nullspace)
            return false;

        var angle = _random.NextAngle();
        var direction = angle.ToVec();
        var minDistance = Math.Max(0f, config.HuntDebrisMinDistance);
        var maxDistance = Math.Max(minDistance, config.HuntDebrisMaxDistance);
        var distance = MathHelper.CloseTo(minDistance, maxDistance)
            ? minDistance
            : _random.NextFloat(minDistance, maxDistance);
        var lateral = (angle + Math.PI / 2).ToVec() *
                      _random.NextFloat(-config.HuntDebrisSafetyRadius, config.HuntDebrisSafetyRadius);

        var origin = GetHuntDebrisSpawnOrigin(store, anchorCoords, direction);
        var candidate = new MapCoordinates(origin + direction * distance + lateral, anchorCoords.MapId);
        if (!IsHuntDebrisAreaClear(candidate, config.HuntDebrisSafetyRadius))
            return false;

        spawnCoords = candidate;
        return true;
    }

    private Vector2 GetHuntDebrisSpawnOrigin(EntityUid store, MapCoordinates anchorCoords, Vector2 direction)
    {
        if (!TryComp(store, out TransformComponent? storeXform) ||
            storeXform.GridUid is not { } gridUid ||
            !TryComp(gridUid, out MapGridComponent? grid))
        {
            return anchorCoords.Position;
        }

        var gridXform = Transform(gridUid);
        if (gridXform.MapID != anchorCoords.MapId)
            return anchorCoords.Position;

        var bounds = _xform.GetWorldMatrix(gridXform).TransformBox(grid.LocalAABB);
        var probe = anchorCoords.Position + direction * MathF.Max(bounds.Width, bounds.Height);
        return bounds.ClosestPoint(probe);
    }

    private bool IsHuntDebrisAreaClear(MapCoordinates coords, float safetyRadius)
    {
        var diameter = Math.Max(1f, safetyRadius * 2f);
        return IsHuntDebrisAreaClear(coords, new Vector2(diameter, diameter), 0f);
    }

    private bool IsHuntDebrisAreaClear(MapCoordinates coords, Vector2 size, float safetyRadius)
    {
        var bounds = Box2.CenteredAround(
                coords.Position,
                new Vector2(Math.Max(1f, size.X), Math.Max(1f, size.Y)))
            .Enlarged(Math.Max(0f, safetyRadius));

        _huntDebrisPlacementGridScratch.Clear();
        _mapManager.FindGridsIntersecting(
            coords.MapId,
            bounds,
            ref _huntDebrisPlacementGridScratch,
            includeMap: false);

        return _huntDebrisPlacementGridScratch.Count == 0;
    }

    private bool TryGetHuntDungeonPlacementMapCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        EntityCoordinates debrisCoords,
        Box2 generatedBounds,
        out MapCoordinates spawnCoords
    )
    {
        spawnCoords = MapCoordinates.Nullspace;
        var anchorCoords = _xform.ToMapCoordinates(debrisCoords);
        if (anchorCoords.MapId == MapId.Nullspace)
            return false;

        var angle = _random.NextAngle();
        var direction = angle.ToVec();
        var minDistance = Math.Max(0f, config.HuntDebrisMinDistance);
        var maxDistance = Math.Max(minDistance, config.HuntDebrisMaxDistance);
        var distance = MathHelper.CloseTo(minDistance, maxDistance)
            ? minDistance
            : _random.NextFloat(minDistance, maxDistance);
        var lateral = (angle + Math.PI / 2).ToVec() *
                      _random.NextFloat(-config.HuntDebrisSafetyRadius, config.HuntDebrisSafetyRadius);

        var origin = GetHuntDebrisSpawnOrigin(store, anchorCoords, direction);
        var candidate = new MapCoordinates(origin + direction * distance + lateral, anchorCoords.MapId);
        var placementPadding = config.HuntDebrisSafetyRadius + NcContractTuning.HuntDungeonExteriorPadding + 1f;
        if (!IsHuntDebrisAreaClear(candidate, generatedBounds.Size, placementPadding))
            return false;

        spawnCoords = candidate;
        return true;
    }

    private bool IsHuntDebrisGridPlacementClear(EntityUid debris, MapGridComponent grid, float safetyRadius)
    {
        var xform = Transform(debris);
        if (xform.MapUid == null)
            return false;

        var bounds = _xform.GetWorldMatrix(xform)
            .TransformBox(grid.LocalAABB)
            .Enlarged(Math.Max(0f, safetyRadius));

        _huntDebrisPlacementGridScratch.Clear();
        _mapManager.FindGridsIntersecting(
            xform.MapID,
            bounds,
            ref _huntDebrisPlacementGridScratch,
            includeMap: false);

        for (var i = 0; i < _huntDebrisPlacementGridScratch.Count; i++)
        {
            if (_huntDebrisPlacementGridScratch[i].Owner != debris)
                return false;
        }

        return true;
    }

    private bool TryPickHuntDebrisPrototype(
        string contractId,
        IReadOnlyList<NcHuntDebrisEntry> debris,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (debris.Count == 0)
            return false;

        var picked = PickWeighted(_random, debris, static entry => entry.Weight);
        prototypeId = picked.Prototype;

        if (_prototypes.HasIndex<EntityPrototype>(prototypeId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': debris prototype '{prototypeId}' is missing.");
        return false;
    }

    private bool TryPickHuntDungeonPrototype(
        string contractId,
        IReadOnlyList<NcHuntDungeonEntry> dungeons,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (dungeons.Count == 0)
            return false;

        var picked = PickWeighted(_random, dungeons, static entry => entry.Weight);
        prototypeId = picked.Prototype;

        if (_prototypes.HasIndex<DungeonConfigPrototype>(prototypeId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Hunt runtime init failed for '{contractId}': dungeonConfig '{prototypeId}' is missing.");
        return false;
    }

    private void UpdatePendingHuntDungeons()
    {
        if (_objectiveRuntime.ActiveHuntObjectives.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveHuntObjectives)
        {
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
                state.HuntDungeonGenerationTask is not { } task ||
                !task.IsCompleted)
                continue;

            _objectiveRuntime.KeysScratch.Add(key);
        }

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
            FinishPendingHuntDungeon(_objectiveRuntime.KeysScratch[i]);

        _objectiveRuntime.KeysScratch.Clear();
    }

    private void FinishPendingHuntDungeon((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.HuntDungeonGenerationTask is not { } task ||
            !task.IsCompleted)
            return;

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }

        state.HuntDungeonGenerationTask = null;

        if (task.IsCanceled || task.IsFaulted)
        {
            var reason = task.Exception?.GetBaseException().Message ?? "generation task failed";
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': {reason}");
            return;
        }

        var dungeons = task.GetAwaiter().GetResult();
        if (!HasGeneratedHuntDungeonContent(dungeons, state.HuntDungeonSelfContained))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated no usable tiles.");
            return;
        }

        if (state.HuntDungeonGenerationMap is not { } generationMap ||
            generationMap == EntityUid.Invalid ||
            TerminatingOrDeleted(generationMap) ||
            !TryConsolidateHuntDungeonGeneration(
                key.ContractId,
                state,
                generationMap,
                out var generatedGrid,
                out var generatedBounds))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated grid is missing.");
            return;
        }

        if (!TryPlaceGeneratedHuntDungeon(
                key.Store,
                contract,
                state,
                generationMap,
                generatedGrid,
                generatedBounds,
                out var placedGrid))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': cannot find a free dungeon placement after {Math.Max(1, contract.Config.HuntDebrisPlacementAttempts)} attempts.");
            return;
        }

        if (!state.HuntDungeonSelfContained)
            SpawnHuntDungeonExterior(key.ContractId, contract.Config, placedGrid.Owner, placedGrid.Comp);

        var spawnCoordinates = new List<EntityCoordinates>();
        spawnCoordinates.Clear();
        CollectHuntDungeonRoomSpawnCoordinates(dungeons, placedGrid.Owner, placedGrid.Comp, spawnCoordinates);
        if (spawnCoordinates.Count == 0)
            CollectHuntDebrisSpawnCoordinates(placedGrid.Owner, placedGrid.Comp, spawnCoordinates);

        if (spawnCoordinates.Count == 0)
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': generated grid has no valid spawn tiles.");
            return;
        }

        if (!TrySpawnHuntTargetEntities(key.Store, key.ContractId, contract, state, spawnCoordinates))
        {
            FinalizeObjectiveTerminalOutcome(
                key,
                comp,
                contract,
                $"Dungeon generation failed for hunt contract '{key.ContractId}': cannot spawn hunt targets.");
            return;
        }

        if (state.PinpointerEntities.Count > 0 &&
            TryResolveSpawnedHuntPinpointerTarget(key.Store, contract, state, out var pinpointerTarget))
        {
            RetargetObjectivePinpointers(key, state, pinpointerTarget);
        }

        if (contract.Config.GivePinpointer &&
            state.PinpointerEntities.Count == 0 &&
            state.HuntPendingPinpointerUser is { } user &&
            user != EntityUid.Invalid &&
            !TerminatingOrDeleted(user) &&
            !TryIssueSpawnedHuntPinpointer(key.Store, user, key.ContractId, contract, state))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init for '{key.ContractId}' generated targets but failed to issue initial pinpointer.");
        }

        state.HuntPendingPinpointerUser = null;
        UpdateObjectiveContractProgress(key.Store, key.ContractId, contract);
    }

    private bool TryConsolidateHuntDungeonGeneration(
        string contractId,
        ObjectiveRuntimeState state,
        EntityUid generationMap,
        out Entity<MapGridComponent> generatedGrid,
        out Box2 generatedBounds
    )
    {
        generatedGrid = default;
        generatedBounds = default;
        if (!TryComp(generationMap, out TransformComponent? mapXform) || mapXform.ChildCount == 0)
            return false;

        if (state.HuntDebrisEntity is not { } primaryGridUid ||
            primaryGridUid == EntityUid.Invalid ||
            !TryComp(primaryGridUid, out MapGridComponent? primaryGrid) ||
            !TryComp(primaryGridUid, out TransformComponent? primaryXform))
        {
            return false;
        }

        var mergeGrids = new List<Entity<MapGridComponent>>();
        var children = mapXform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp(child, out MapGridComponent? childGrid))
                continue;

            if (child == primaryGridUid)
                continue;

            mergeGrids.Add((child, childGrid));
        }

        for (var i = 0; i < mergeGrids.Count; i++)
        {
            var mergeGrid = mergeGrids[i];
            if (mergeGrid.Owner == EntityUid.Invalid || TerminatingOrDeleted(mergeGrid.Owner))
                continue;

            if (!TryComp(mergeGrid.Owner, out TransformComponent? mergeXform))
                continue;

            var mergeMatrix = Matrix3x2.Multiply(
                _xform.GetWorldMatrix(mergeXform),
                _xform.GetInvWorldMatrix(primaryXform));

            try
            {
                _gridFixture.Merge(
                    primaryGridUid,
                    mergeGrid.Owner,
                    mergeMatrix,
                    primaryGrid,
                    mergeGrid.Comp,
                    primaryXform,
                    mergeXform);
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt dungeon generation for '{contractId}' failed to merge grid fragment '{ToPrettyString(mergeGrid.Owner)}': {e}");
                return false;
            }

            if (!TryComp(primaryGridUid, out primaryGrid) ||
                !TryComp(primaryGridUid, out primaryXform))
            {
                return false;
            }
        }

        primaryGrid.CanSplit = false;
        generatedBounds = _xform.GetWorldMatrix(primaryXform).TransformBox(primaryGrid.LocalAABB);
        generatedGrid = (primaryGridUid, primaryGrid);
        return true;
    }

    private bool TryPlaceGeneratedHuntDungeon(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        EntityUid generationMap,
        Entity<MapGridComponent> generatedGrid,
        Box2 generatedBounds,
        out Entity<MapGridComponent> placedGrid
    )
    {
        placedGrid = default;

        if (state.HuntDungeonAnchorCoordinates is not { } anchorCoords)
            return false;

        var attempts = Math.Max(1, contract.Config.HuntDebrisPlacementAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (!TryGetHuntDungeonPlacementMapCoordinates(
                    store,
                    contract.Config,
                    anchorCoords,
                    generatedBounds,
                    out var placementCenter) ||
                !_map.TryGetMap(placementCenter.MapId, out var targetMap))
            {
                continue;
            }

            var placementOffset = placementCenter.Position - generatedBounds.Center;
            var xform = Transform(generatedGrid.Owner);
            var position = _xform.GetWorldPosition(xform);
            var rotation = _xform.GetWorldRotation(xform);
            _xform.SetParent(generatedGrid.Owner, xform, targetMap.Value);
            _xform.SetWorldPositionRotation(generatedGrid.Owner, position + placementOffset, rotation, xform);

            state.HuntDungeonGridEntities.Clear();
            state.HuntDungeonGridEntities.Add(generatedGrid.Owner);
            state.HuntDebrisEntity = generatedGrid.Owner;
            state.HuntDungeonGenerationMap = null;
            placedGrid = generatedGrid;
            ConfigureHuntDungeonRadarContact(generatedGrid.Owner);

            if (TryComp(generationMap, out TransformComponent? generationMapXform))
                _map.DeleteMap(generationMapXform.MapID);

            return true;
        }

        return false;
    }

    private void ConfigureHuntDungeonRadarContact(EntityUid grid)
    {
        _contractMeta.SetEntityName(grid, "Контрактный обломок");
        _shuttle.SetIFFColor(grid, Color.FromHex("#d67e27"));
        _shuttle.RemoveIFFFlag(grid, IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways);
    }

    private static bool IsSelfContainedHuntDungeonPrototype(string prototypeId)
    {
        return prototypeId.StartsWith("ForgeHuntVGRoid", StringComparison.Ordinal) ||
               prototypeId.StartsWith("NFVGRoid", StringComparison.Ordinal);
    }

    private static bool HasGeneratedHuntDungeonContent(IReadOnlyList<Dungeon> dungeons, bool allowTileOnly)
    {
        for (var i = 0; i < dungeons.Count; i++)
        {
            if (dungeons[i].Rooms.Count > 0 ||
                allowTileOnly && dungeons[i].AllTiles.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void SpawnHuntDungeonExterior(
        string contractId,
        ContractObjectiveConfigData config,
        EntityUid site,
        MapGridComponent grid
    )
    {
        if (config.HuntDungeonExteriorTiles.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt dungeon exterior for '{contractId}' skipped: no configured exterior tile prototypes exist.");
            return;
        }

        var generatedTiles = new HashSet<Vector2i>();
        if (!TryCollectHuntDungeonTileBounds(site, grid, generatedTiles, out var bounds))
            return;

        var padding = Math.Max(1, NcContractTuning.HuntDungeonExteriorPadding);
        var exteriorBounds = CreateRandomizedHuntDungeonExteriorBounds(bounds, padding);

        var exteriorTileSet = new HashSet<Vector2i>();
        BuildHuntDungeonExteriorShape(site, grid, generatedTiles, exteriorBounds, padding, exteriorTileSet);
        if (exteriorTileSet.Count == 0)
            return;

        var exteriorTiles = new List<(Vector2i Index, Tile Tile)>(exteriorTileSet.Count);
        var rockCandidates = new List<Vector2i>();
        var tileRandom = _random.GetRandom();

        foreach (var tile in exteriorTileSet)
        {
            if (!TryPickHuntDungeonExteriorTile(contractId, config.HuntDungeonExteriorTiles, out var tileDef))
                return;

            exteriorTiles.Add((tile, _tile.GetVariantTile(tileDef, tileRandom)));

            if (config.HuntDungeonExteriorRocks.Count > 0 &&
                !IsNearGeneratedHuntDungeonTile(
                    tile,
                    generatedTiles,
                    NcContractTuning.HuntDungeonExteriorCoreClearance) &&
                ShouldSpawnHuntDungeonExteriorRock(tile, generatedTiles, exteriorTileSet))
            {
                rockCandidates.Add(tile);
            }
        }

        _map.SetTiles(site, grid, exteriorTiles);
        SpawnHuntDungeonExteriorRocks(contractId, config.HuntDungeonExteriorRocks, site, grid, rockCandidates);
    }

    private Box2i CreateRandomizedHuntDungeonExteriorBounds(Box2i bounds, int padding)
    {
        var minPadding = Math.Clamp(
            NcContractTuning.HuntDungeonExteriorCoreClearance + 1,
            1,
            padding);
        var oppositeMax = Math.Max(minPadding + 1, padding / 2 + 1);

        var left = _random.Next(minPadding, padding + 1);
        var right = _random.Next(minPadding, padding + 1);
        var bottom = _random.Next(minPadding, padding + 1);
        var top = _random.Next(minPadding, padding + 1);

        switch (_random.Next(4))
        {
            case 0:
                left = padding;
                right = Math.Min(right, _random.Next(minPadding, oppositeMax));
                break;
            case 1:
                right = padding;
                left = Math.Min(left, _random.Next(minPadding, oppositeMax));
                break;
            case 2:
                bottom = padding;
                top = Math.Min(top, _random.Next(minPadding, oppositeMax));
                break;
            default:
                top = padding;
                bottom = Math.Min(bottom, _random.Next(minPadding, oppositeMax));
                break;
        }

        if (_random.Prob(0.65f))
        {
            var adjacentPadding = _random.Next(Math.Max(minPadding, padding / 2), padding + 1);
            if (_random.Prob(0.5f))
                top = Math.Max(top, adjacentPadding);
            else
                bottom = Math.Max(bottom, adjacentPadding);
        }
        else
        {
            var adjacentPadding = _random.Next(Math.Max(minPadding, padding / 2), padding + 1);
            if (_random.Prob(0.5f))
                left = Math.Max(left, adjacentPadding);
            else
                right = Math.Max(right, adjacentPadding);
        }

        return new Box2i(
            bounds.Left - left,
            bounds.Bottom - bottom,
            bounds.Right + right,
            bounds.Top + top);
    }

    private void BuildHuntDungeonExteriorShape(
        EntityUid site,
        MapGridComponent grid,
        HashSet<Vector2i> generatedTiles,
        Box2i exteriorBounds,
        int padding,
        HashSet<Vector2i> output
    )
    {
        output.Clear();
        var taken = new HashSet<Vector2i>(generatedTiles);
        var frontier = new HashSet<Vector2i>();

        foreach (var tile in generatedTiles)
            AddHuntDungeonExteriorCandidates(tile, exteriorBounds, taken, frontier);

        foreach (var tile in generatedTiles)
            PlaceHuntDungeonExteriorCore(tile, site, grid, exteriorBounds, taken, frontier, output);

        var availableTiles = CountAvailableHuntDungeonExteriorTiles(site, grid, generatedTiles, exteriorBounds);
        var targetCount = PickHuntDungeonExteriorTileCount(output.Count, availableTiles, padding);
        while (output.Count < targetCount && frontier.Count > 0)
        {
            var tile = _random.Pick(frontier);
            PlaceHuntDungeonExteriorTile(tile, site, grid, exteriorBounds, taken, frontier, output);

            for (var i = 0; i < HuntDungeonExteriorCardinalOffsets.Length; i++)
            {
                if (!_random.Prob(NcContractTuning.HuntDungeonExteriorBlobDrawChance))
                    continue;

                PlaceHuntDungeonExteriorTile(
                    tile + HuntDungeonExteriorCardinalOffsets[i],
                    site,
                    grid,
                    exteriorBounds,
                    taken,
                    frontier,
                    output);
            }
        }
    }

    private void PlaceHuntDungeonExteriorCore(
        Vector2i tile,
        EntityUid site,
        MapGridComponent grid,
        Box2i exteriorBounds,
        HashSet<Vector2i> taken,
        HashSet<Vector2i> frontier,
        HashSet<Vector2i> output
    )
    {
        var clearance = Math.Max(0, NcContractTuning.HuntDungeonExteriorCoreClearance);
        for (var x = -clearance; x <= clearance; x++)
        {
            for (var y = -clearance; y <= clearance; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                PlaceHuntDungeonExteriorTile(
                    new Vector2i(tile.X + x, tile.Y + y),
                    site,
                    grid,
                    exteriorBounds,
                    taken,
                    frontier,
                    output);
            }
        }
    }

    private bool PlaceHuntDungeonExteriorTile(
        Vector2i tile,
        EntityUid site,
        MapGridComponent grid,
        Box2i exteriorBounds,
        HashSet<Vector2i> taken,
        HashSet<Vector2i> frontier,
        HashSet<Vector2i> output
    )
    {
        frontier.Remove(tile);
        if (!IsHuntDungeonExteriorTileInsideBounds(tile, exteriorBounds) ||
            !IsHuntDungeonExteriorTileAvailable(site, grid, tile) ||
            !taken.Add(tile))
        {
            return false;
        }

        output.Add(tile);
        AddHuntDungeonExteriorCandidates(tile, exteriorBounds, taken, frontier);
        return true;
    }

    private static void AddHuntDungeonExteriorCandidates(
        Vector2i tile,
        Box2i exteriorBounds,
        HashSet<Vector2i> taken,
        HashSet<Vector2i> frontier
    )
    {
        for (var i = 0; i < HuntDungeonExteriorCardinalOffsets.Length; i++)
        {
            var neighbor = tile + HuntDungeonExteriorCardinalOffsets[i];
            if (!IsHuntDungeonExteriorTileInsideBounds(neighbor, exteriorBounds) ||
                taken.Contains(neighbor))
            {
                continue;
            }

            frontier.Add(neighbor);
        }
    }

    private int CountAvailableHuntDungeonExteriorTiles(
        EntityUid site,
        MapGridComponent grid,
        HashSet<Vector2i> generatedTiles,
        Box2i exteriorBounds
    )
    {
        var count = 0;
        for (var x = exteriorBounds.Left; x <= exteriorBounds.Right; x++)
        {
            for (var y = exteriorBounds.Bottom; y <= exteriorBounds.Top; y++)
            {
                var tile = new Vector2i(x, y);
                if (generatedTiles.Contains(tile) ||
                    !IsHuntDungeonExteriorTileAvailable(site, grid, tile))
                    continue;

                count++;
            }
        }

        return count;
    }

    private int PickHuntDungeonExteriorTileCount(int guaranteedCount, int availableTiles, int padding)
    {
        if (availableTiles <= guaranteedCount)
            return guaranteedCount;

        var minRatio = Math.Clamp(0.50f + padding * 0.015f, 0.50f, 0.62f);
        var maxRatio = Math.Clamp(0.72f + padding * 0.015f, minRatio, 0.88f);
        var ratio = _random.NextFloat(minRatio, maxRatio);
        return Math.Clamp((int) MathF.Round(availableTiles * ratio), guaranteedCount, availableTiles);
    }

    private static bool IsHuntDungeonExteriorTileInsideBounds(Vector2i tile, Box2i bounds)
    {
        return tile.X >= bounds.Left &&
               tile.X <= bounds.Right &&
               tile.Y >= bounds.Bottom &&
               tile.Y <= bounds.Top;
    }

    private bool IsHuntDungeonExteriorTileAvailable(EntityUid site, MapGridComponent grid, Vector2i tile)
    {
        return !_map.TryGetTileRef(site, grid, tile, out var tileRef) ||
               tileRef.Tile.IsEmpty;
    }

    private bool TryCollectHuntDungeonTileBounds(
        EntityUid site,
        MapGridComponent grid,
        HashSet<Vector2i> output,
        out Box2i bounds
    )
    {
        output.Clear();
        bounds = new Box2i();

        var hasTile = false;
        var left = 0;
        var right = 0;
        var bottom = 0;
        var top = 0;

        var enumerator = _map.GetAllTilesEnumerator(site, grid, true);
        while (enumerator.MoveNext(out var tile))
        {
            var tileRef = tile.Value;
            if (tileRef.Tile.IsEmpty)
                continue;

            var indices = tileRef.GridIndices;
            output.Add(indices);

            if (!hasTile)
            {
                left = right = indices.X;
                bottom = top = indices.Y;
                hasTile = true;
                continue;
            }

            left = Math.Min(left, indices.X);
            right = Math.Max(right, indices.X);
            bottom = Math.Min(bottom, indices.Y);
            top = Math.Max(top, indices.Y);
        }

        if (!hasTile)
            return false;

        bounds = new Box2i(left, bottom, right, top);
        return true;
    }

    private bool ShouldSpawnHuntDungeonExteriorRock(
        Vector2i tile,
        HashSet<Vector2i> generatedTiles,
        HashSet<Vector2i> exteriorTiles
    )
    {
        var chance = IsNearHuntDungeonExteriorBoundary(tile, generatedTiles, exteriorTiles)
            ? NcContractTuning.HuntDungeonExteriorRimRockChance
            : NcContractTuning.HuntDungeonExteriorInnerRockChance;

        return _random.Prob(chance);
    }

    private static bool IsNearHuntDungeonExteriorBoundary(
        Vector2i tile,
        HashSet<Vector2i> generatedTiles,
        HashSet<Vector2i> exteriorTiles
    )
    {
        var radius = Math.Max(1, NcContractTuning.HuntDungeonExteriorRimWidth);
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var neighbor = new Vector2i(tile.X + x, tile.Y + y);
                if (!generatedTiles.Contains(neighbor) && !exteriorTiles.Contains(neighbor))
                    return true;
            }
        }

        return false;
    }

    private static bool IsNearGeneratedHuntDungeonTile(
        Vector2i tile,
        HashSet<Vector2i> generatedTiles,
        int clearance
    )
    {
        var radius = Math.Max(0, clearance);
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (generatedTiles.Contains(new Vector2i(tile.X + x, tile.Y + y)))
                    return true;
            }
        }

        return false;
    }

    private bool TryPickHuntDungeonExteriorTile(
        string contractId,
        IReadOnlyList<NcHuntDungeonExteriorTileEntry> tiles,
        out ContentTileDefinition tileDef
    )
    {
        tileDef = default!;

        var weights = tiles.Count <= 128
            ? stackalloc int[tiles.Count]
            : new int[tiles.Count];

        long total = 0;
        for (var i = 0; i < tiles.Count; i++)
        {
            var entry = tiles[i];
            if (entry == null ||
                entry.Weight <= 0 ||
                !_prototypes.HasIndex<ContentTileDefinition>(entry.Prototype))
            {
                continue;
            }

            weights[i] = entry.Weight;
            total += entry.Weight;
        }

        if (total > 0)
        {
            var roll = total <= int.MaxValue
                ? _random.Next((int) total)
                : (long) (_random.NextDouble() * total);

            for (var i = 0; i < tiles.Count; i++)
            {
                var weight = weights[i];
                if (weight <= 0)
                    continue;

                roll -= weight;
                if (roll >= 0)
                    continue;

                var entry = tiles[i];
                if (entry == null ||
                    !_prototypes.TryIndex<ContentTileDefinition>(entry.Prototype, out var pickedTileDef))
                {
                    continue;
                }

                tileDef = pickedTileDef;
                return true;
            }
        }

        Sawmill.Warning(
            $"[Contracts] Hunt dungeon exterior for '{contractId}' skipped: no configured exterior tile prototypes exist.");
        return false;
    }

    private void SpawnHuntDungeonExteriorRocks(
        string contractId,
        IReadOnlyList<NcHuntDungeonExteriorRockEntry> rocks,
        EntityUid site,
        MapGridComponent grid,
        List<Vector2i> candidates
    )
    {
        if (rocks.Count == 0)
            return;

        var maxCount = Math.Min(candidates.Count, NcContractTuning.HuntDungeonExteriorMaxRockCount);
        var spawned = 0;
        while (spawned < maxCount && candidates.Count > 0)
        {
            var candidateIndex = _random.Next(candidates.Count);
            var tile = candidates[candidateIndex];
            candidates.RemoveAt(candidateIndex);

            if (!TryPickHuntDungeonExteriorRock(contractId, rocks, out var prototype) ||
                !IsHuntDungeonExteriorRockCoordinateValid(site, grid, tile))
            {
                continue;
            }

            try
            {
                Spawn(prototype, _map.GridTileToLocal(site, grid, tile));
                spawned++;
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt dungeon exterior for '{contractId}' failed to spawn '{prototype}': {e}");
            }
        }
    }

    private bool TryPickHuntDungeonExteriorRock(
        string contractId,
        IReadOnlyList<NcHuntDungeonExteriorRockEntry> rocks,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        var weights = rocks.Count <= 128
            ? stackalloc int[rocks.Count]
            : new int[rocks.Count];

        long total = 0;
        for (var i = 0; i < rocks.Count; i++)
        {
            var entry = rocks[i];
            if (entry == null ||
                entry.Weight <= 0 ||
                !_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
            {
                continue;
            }

            weights[i] = entry.Weight;
            total += entry.Weight;
        }

        if (total > 0)
        {
            var roll = total <= int.MaxValue
                ? _random.Next((int) total)
                : (long) (_random.NextDouble() * total);

            for (var i = 0; i < rocks.Count; i++)
            {
                var weight = weights[i];
                if (weight <= 0)
                    continue;

                roll -= weight;
                if (roll >= 0)
                    continue;

                var entry = rocks[i];
                if (entry == null)
                    continue;

                prototypeId = entry.Prototype;
                return true;
            }
        }

        Sawmill.Warning(
            $"[Contracts] Hunt dungeon exterior for '{contractId}' skipped: no configured rock prototypes exist.");
        return false;
    }

    private bool IsHuntDungeonExteriorRockCoordinateValid(
        EntityUid site,
        MapGridComponent grid,
        Vector2i tile
    )
    {
        return _map.TryGetTileRef(site, grid, tile, out var tileRef) &&
               !tileRef.Tile.IsEmpty &&
               !_turf.IsTileBlocked(tileRef, CollisionGroup.MobMask);
    }

    private void ForceLoadHuntDebris(EntityUid debris)
    {
        if (!HasComp<LocalityLoaderComponent>(debris))
            return;

        RaiseLocalEvent(debris, new LocalStructureLoadedEvent());
        RemCompDeferred<LocalityLoaderComponent>(debris);
    }

    private void CollectHuntDungeonRoomSpawnCoordinates(
        IReadOnlyList<Dungeon> dungeons,
        EntityUid debris,
        MapGridComponent grid,
        List<EntityCoordinates> output
    )
    {
        output.Clear();
        var seen = new HashSet<Vector2i>();
        for (var i = 0; i < dungeons.Count; i++)
        {
            foreach (var tile in dungeons[i].RoomTiles)
            {
                if (!seen.Add(tile) ||
                    !_map.TryGetTileRef(debris, grid, tile, out var tileRef) ||
                    tileRef.Tile.IsEmpty ||
                    _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask) ||
                    !_anchorable.TileFree(
                        (debris, grid),
                        tile,
                        (int) CollisionGroup.MachineLayer,
                        (int) CollisionGroup.MachineLayer))
                {
                    continue;
                }

                output.Add(_map.GridTileToLocal(debris, grid, tile));
            }
        }
    }

    private void CollectHuntDebrisSpawnCoordinates(
        EntityUid debris,
        MapGridComponent grid,
        List<EntityCoordinates> output
    )
    {
        output.Clear();
        AppendHuntDebrisSpawnCoordinates((debris, grid), output);
    }

    private void CollectHuntDebrisSpawnCoordinates(
        IReadOnlyList<Entity<MapGridComponent>> grids,
        List<EntityCoordinates> output
    )
    {
        output.Clear();
        for (var i = 0; i < grids.Count; i++)
            AppendHuntDebrisSpawnCoordinates(grids[i], output);
    }

    private void AppendHuntDebrisSpawnCoordinates(
        Entity<MapGridComponent> grid,
        List<EntityCoordinates> output
    )
    {
        var enumerator = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp, true);
        while (enumerator.MoveNext(out var tile))
        {
            var tileRef = tile.Value;
            if (tileRef.Tile.IsEmpty ||
                _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask) ||
                !_anchorable.TileFree(
                    grid,
                    tileRef.GridIndices,
                    (int) CollisionGroup.MachineLayer,
                    (int) CollisionGroup.MachineLayer))
                continue;

            output.Add(_map.GridTileToLocal(grid.Owner, grid.Comp, tileRef.GridIndices));
        }
    }

    private bool TryAdvanceSpawnedHuntTargetProgress(
        EntityUid killedTarget,
        ContractServerData contract,
        ObjectiveRuntimeState state
    )
    {
        if (!TryGetPlanningEntityPrototypeId(killedTarget, out var prototypeId))
            return false;

        var targets = GetEffectiveTargets(contract);
        if (state.HuntBodyEntity == killedTarget)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!target.BodyRequired || target.Progress >= target.Required)
                    continue;

                if (!MatchesSpawnedHuntTargetEntry(prototypeId, target))
                    continue;

                target.Progress = Math.Min(target.Required, target.Progress + 1);
                targets[i] = target;
                return true;
            }
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.Progress >= target.Required)
                continue;

            if (!MatchesSpawnedHuntTargetEntry(prototypeId, target))
                continue;

            target.Progress = Math.Min(target.Required, target.Progress + 1);
            targets[i] = target;
            return true;
        }

        return false;
    }

    private static int CalculateSpawnedHuntTotalProgress(ContractServerData contract)
    {
        var progress = 0;
        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            progress = SaturatingAdd(progress, Math.Max(0, targets[i].Progress));
        }

        return progress;
    }

    private bool TryGetHuntBodyEntity(ObjectiveRuntimeState state, out EntityUid body)
    {
        body = EntityUid.Invalid;
        if (state.HuntBodyEntity is not { } candidate ||
            candidate == EntityUid.Invalid ||
            TerminatingOrDeleted(candidate))
            return false;

        if (!TryComp(candidate, out MobStateComponent? mobState) ||
            mobState.CurrentState != MobState.Dead)
            return false;

        body = candidate;
        return true;
    }

    private bool TryConsumeSpawnedHuntBodyTurnIn(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        ObjectiveConsumeJournal journal,
        out ClaimAttemptResult fail
    )
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!RequiresSpawnedHuntBodyTurnIn(contract))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetHuntBodyEntity(state, out var body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' requires the marked corpse to be brought back to the store.");
            return false;
        }

        if (!IsSpawnedHuntBodyInTurnInScope(store, user, body))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingBody,
                $"Hunt contract '{contractId}' body is not being dragged by the claimant and is not near the store.");
            return false;
        }

        journal.TrackHuntBody(state, body);
        state.HuntBodyEntity = null;
        RemoveSpawnedHuntTarget(state, body);
        journal.PendingDeletes.Add(body);

        return true;
    }

    private bool IsSpawnedHuntBodyInTurnInScope(EntityUid store, EntityUid user, EntityUid body)
    {
        if (IsSpawnedHuntBodyCarriedByUser(body, user))
            return true;

        if (!TryComp(store, out TransformComponent? storeXform) ||
            !TryComp(body, out TransformComponent? bodyXform) ||
            IsTargetInEntityContainer(bodyXform))
            return false;

        var storeMap = _xform.ToMapCoordinates(storeXform.Coordinates);
        var bodyMap = _xform.ToMapCoordinates(bodyXform.Coordinates);
        if (storeMap.MapId != bodyMap.MapId)
            return false;

        var delta = _xform.GetWorldPosition(storeXform) - _xform.GetWorldPosition(bodyXform);
        return delta.LengthSquared() <=
               NcContractTuning.TrackedDeliveryStoreRange * NcContractTuning.TrackedDeliveryStoreRange;
    }

    private bool IsSpawnedHuntBodyCarriedByUser(EntityUid body, EntityUid user)
    {
        if (TryComp(body, out PullableComponent? pullable) && pullable.Puller == user)
            return true;

        return TryGetContainedEntityRoot(body, out var root) && root == user;
    }

    private bool TryResolveSpawnedHuntPrototype(
        string contractId,
        ContractTargetServerData target,
        out string prototypeId
    )
    {
        prototypeId = string.Empty;

        if (target.MatchMode == PrototypeMatchMode.Exact)
        {
            prototypeId = target.TargetItem;
            return _prototypes.HasIndex<EntityPrototype>(prototypeId);
        }

        if (string.IsNullOrWhiteSpace(target.TargetItem) ||
            !_prototypes.TryIndex<NcHuntGroupPrototype>(target.TargetItem, out var group) ||
            group.Prototypes.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': target group has no spawnable prototypes.");
            return false;
        }

        var candidates = new List<string>(group.Prototypes.Count);
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var candidate = group.Prototypes[i];
            if (!string.IsNullOrWhiteSpace(candidate) && _prototypes.HasIndex<EntityPrototype>(candidate))
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': target group '{group.ID}' has no valid entity prototypes.");
            return false;
        }

        prototypeId = _random.Pick(candidates);
        return true;
    }
}
