using System.Numerics;
using System.Linq;
using Content.Server._Mono.Shuttles.Components;
using Content.Server.Atmos.Components;
using Content.Server.Spreader;
using Content.Server.Station.Components;
using Content.Server.Worldgen.Components;
using Content.Shared._Forge;
using Content.Shared._Forge.Persistence;
using Content.Shared._Mono;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Persistence;

/// <summary>
/// Common file-backed save/load facade for persistent maps and hangar grids.
/// </summary>
public sealed class GridPersistenceService : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public ResPath RootPath => NormalizeRoot(_configuration.GetCVar(ForgeVars.PersistenceRoot));
    public ResPath MapsPath => RootPath / "maps";
    public ResPath HangarPath => RootPath / "hangar";

    private static readonly SerializationOptions WorldSaveOptions = SerializationOptions.Default with
    {
        MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
        ErrorOnOrphan = false,
        EntityExceptionBehaviour = EntityExceptionBehaviour.IgnoreEntity,
        LogAutoInclude = null,
    };

    public override void Initialize()
    {
        base.Initialize();
        EnsureDirectories();
    }

    public void EnsureDirectories()
    {
        _resources.UserData.CreateDir(RootPath);
        _resources.UserData.CreateDir(MapsPath);
        _resources.UserData.CreateDir(HangarPath);
    }

    public bool Exists(ResPath path)
    {
        return _resources.UserData.Exists(path.ToRootedPath());
    }

    public bool Delete(ResPath path)
    {
        path = path.ToRootedPath();
        if (!_resources.UserData.Exists(path))
            return true;

        _resources.UserData.Delete(path);
        return true;
    }

    public bool SaveMap(EntityUid map, ResPath path)
    {
        EnsureDirectories();
        SanitizeForSave(map, isMap: true);
        return _loader.TrySaveMap(map, path.ToRootedPath(), WorldSaveOptions);
    }

    public bool SaveGrid(EntityUid grid, ResPath path, bool coldStartAtmosphere = false)
    {
        EnsureDirectories();
        SanitizeForSave(grid, isMap: false);
        if (!coldStartAtmosphere)
            return _loader.TrySaveGrid(grid, path.ToRootedPath(), WorldSaveOptions);

        // A hangar grid is deliberately detached from its current map, station,
        // docking partners, and runtime network entities. Those references must
        // not pull the station entity (and its transient StationRecords) into
        // the grid file; they are rebuilt when the shuttle is deployed.
        var options = SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            ErrorOnOrphan = false,
            LogAutoInclude = null,
        };

        if (!TryComp<GridAtmosphereComponent>(grid, out var atmosphere))
            return _loader.TrySaveGrid(grid, path.ToRootedPath(), options);

        // Tile and pipe atmosphere is intentionally not part of hangar persistence.
        // Entity gas holders (tanks/canisters) remain regular serialized components.
        var tiles = atmosphere.Tiles;
        atmosphere.Tiles = [];
        try
        {
            return _loader.TrySaveGrid(grid, path.ToRootedPath(), options);
        }
        finally
        {
            atmosphere.Tiles = tiles;
        }
    }

    public bool LoadMap(
        ResPath path,
        out Entity<MapComponent>? map,
        out HashSet<Entity<MapGridComponent>>? grids,
        Vector2 offset = default,
        Angle rotation = default)
    {
        map = null;
        grids = null;
        if (!Exists(path))
            return false;

        return _loader.TryLoadMap(path.ToRootedPath(), out map, out grids, offset: offset, rot: rotation);
    }

    public bool LoadGrid(
        MapId mapId,
        ResPath path,
        out Entity<MapGridComponent>? grid,
        Vector2 offset = default,
        Angle rotation = default)
    {
        grid = null;
        if (!Exists(path))
            return false;

        return _loader.TryLoadGrid(mapId, path.ToRootedPath(), out grid, offset: offset, rot: rotation);
    }

    public ResPath GetMapSavePath(string id)
    {
        return MapsPath / $"{SanitizeId(id)}.yml";
    }

    public ResPath GetHangarSavePath(Guid vesselId)
    {
        return HangarPath / $"{vesselId:N}.yml";
    }

    public static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Persistence identifier cannot be empty.", nameof(id));

        var chars = id.Trim().Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    private static ResPath NormalizeRoot(string configured)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? "/Persisted" : configured.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return new ResPath(path).ToRootedPath();
    }

    private void SanitizeForSave(EntityUid root, bool isMap)
    {
        var spreaderQuery = AllEntityQuery<SpreaderGridComponent, TransformComponent>();
        while (spreaderQuery.MoveNext(out var uid, out var spreader, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            spreader.SpreadQueues.Clear();
        }

        var damageQuery = AllEntityQuery<DamageOnInteractComponent, TransformComponent>();
        while (damageQuery.MoveNext(out var uid, out var damage, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            damage.Damage ??= new DamageSpecifier();
        }

        var worldQuery = AllEntityQuery<WorldControllerComponent, TransformComponent>();
        while (worldQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            RemComp<WorldControllerComponent>(uid);
            RemComp<BiomeSelectionComponent>(uid);
        }

        var godQuery = AllEntityQuery<GridGodModeComponent, TransformComponent>();
        while (godQuery.MoveNext(out var uid, out var god, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            god.ProtectedEntities.Clear();
        }

        var memberQuery = AllEntityQuery<StationMemberComponent, TransformComponent>();
        while (memberQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            RemComp<StationMemberComponent>(uid);
        }

        var poiQuery = AllEntityQuery<PersistentGridComponent, TransformComponent>();
        while (poiQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            RemComp<BecomesStationComponent>(uid);
        }

        var containerQuery = AllEntityQuery<ContainerManagerComponent, TransformComponent>();
        while (containerQuery.MoveNext(out var uid, out var manager, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            foreach (var container in manager.Containers.Values)
            {
                foreach (var contained in container.ContainedEntities.ToArray())
                {
                    if (Prototype(contained)?.MapSavable == false)
                        _containers.Remove(contained, container, force: true);
                }
            }
        }

        var aiQuery = AllEntityQuery<StationAiCoreComponent, TransformComponent>();
        while (aiQuery.MoveNext(out var uid, out var core, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            if (core.RemoteEntity is { Valid: true } eye)
            {
                core.RemoteEntity = null;
                if (Exists(eye))
                    Del(eye);
            }
        }

        var slotQuery = AllEntityQuery<ShuttleConsoleJobSlotsComponent, TransformComponent>();
        while (slotQuery.MoveNext(out var uid, out var slots, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            slots.OwningStation = null;
        }
    }

    private static bool BelongsToRoot(EntityUid root, EntityUid uid, TransformComponent xform, bool isMap)
    {
        if (uid == root)
            return true;

        return isMap ? xform.MapUid == root : xform.GridUid == root;
    }
}
