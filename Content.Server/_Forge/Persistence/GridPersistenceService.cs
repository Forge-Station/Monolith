using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Shuttles.Components;
using Content.Server.Atmos;
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
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Persistence;

/// <summary>
/// Common file-backed save/load facade for persistent maps and hangar grids.
/// World maps are gzip-compressed; old uncompressed .yml files still load.
/// </summary>
public sealed class GridPersistenceService : EntitySystem
{
    private const string YamlExtension = ".yml";
    private const string GzipExtension = ".yml.gz";

    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private WorldPersistFilterSystem _worldFilter = default!;

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

    public bool FileExists(ResPath path)
    {
        return TryResolveExisting(path, out _);
    }

    public bool Delete(ResPath path)
    {
        foreach (var candidate in PathCandidates(path.ToRootedPath()))
        {
            if (!_resources.UserData.Exists(candidate))
                continue;

            _resources.UserData.Delete(candidate);
        }

        return true;
    }

    public bool SaveMap(EntityUid map, ResPath path)
    {
        EnsureDirectories();
        SanitizeForSave(map, isMap: true);
        _worldFilter.BeginMapSave(map);
        try
        {
            return WritePersistence(path, writer => _loader.TrySaveMap(map, writer, WorldSaveOptions));
        }
        finally
        {
            _worldFilter.EndMapSave();
        }
    }

    public bool SaveGrid(EntityUid grid, ResPath path, bool coldStartAtmosphere = false)
    {
        EnsureDirectories();
        SanitizeForSave(grid, isMap: false);

        if (!coldStartAtmosphere)
            return WritePersistence(path, writer => _loader.TrySaveGrid(grid, writer, WorldSaveOptions));

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

        // Tile atmosphere is intentionally not part of hangar persistence.
        // Entity gas holders (tanks/canisters) remain regular serialized components.
        return SaveWithStrippedAtmosphere(grid, isMap: false,
            () => WritePersistence(path, writer => _loader.TrySaveGrid(grid, writer, options)));
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
        if (!TryResolveExisting(path, out var resolved))
            return false;

        using var stream = OpenReadMaybeCompressed(resolved);
        using var reader = new StreamReader(stream);
        return _loader.TryLoadMap(reader, resolved.ToString(), out map, out grids, offset: offset, rot: rotation);
    }

    public bool LoadGrid(
        MapId mapId,
        ResPath path,
        out Entity<MapGridComponent>? grid,
        Vector2 offset = default,
        Angle rotation = default)
    {
        grid = null;
        if (!TryResolveExisting(path, out var resolved))
            return false;

        using var stream = OpenReadMaybeCompressed(resolved);
        using var reader = new StreamReader(stream);
        return _loader.TryLoadGrid(mapId, reader, resolved.ToString(), out grid, offset: offset, rot: rotation);
    }

    public ResPath GetMapSavePath(string id)
    {
        return MapsPath / $"{SanitizeId(id)}{GzipExtension}";
    }

    public ResPath GetHangarSavePath(Guid vesselId)
    {
        return HangarPath / $"{vesselId:N}{YamlExtension}";
    }

    public static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Persistence identifier cannot be empty.", nameof(id));

        var chars = id.Trim().Select(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        return new string(chars);
    }

    public static string IdFromMapFileName(string fileName)
    {
        if (fileName.EndsWith(GzipExtension, StringComparison.OrdinalIgnoreCase))
            return fileName[..^GzipExtension.Length];

        if (fileName.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase))
            return fileName[..^YamlExtension.Length];

        return new ResPath(fileName).FilenameWithoutExtension;
    }

    public static bool IsMapPayloadFile(string fileName)
    {
        return fileName.EndsWith(GzipExtension, StringComparison.OrdinalIgnoreCase)
               || fileName.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static ResPath NormalizeRoot(string configured)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? "/Persisted" : configured.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return new ResPath(path).ToRootedPath();
    }

    private bool WritePersistence(ResPath path, Func<TextWriter, bool> save)
    {
        var rooted = path.ToRootedPath();
        if (IsCompressedPath(rooted))
            return SaveCompressed(rooted, save);

        try
        {
            using var writer = _resources.UserData.OpenWriteText(rooted);
            return save(writer);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to write persistence file {rooted}: {exception}");
            DeleteExact(rooted);
            return false;
        }
    }

    private bool SaveCompressed(ResPath path, Func<TextWriter, bool> save)
    {
        var gzPath = ToCompressedPath(path.ToRootedPath());
        try
        {
            using (var file = _resources.UserData.Open(gzPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new StreamWriter(gzip))
            {
                if (!save(writer))
                {
                    writer.Flush();
                    goto Failed;
                }

                writer.Flush();
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to write compressed persistence file {gzPath}: {exception}");
            DeleteExact(gzPath);
            return false;
        }

        DeleteUncompressedSibling(gzPath);
        return true;

        Failed:
        DeleteExact(gzPath);
        return false;
    }

    private Stream OpenReadMaybeCompressed(ResPath path)
    {
        var stream = _resources.UserData.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (IsCompressedPath(path) || IsGzipMagic(stream))
            return new GZipStream(stream, CompressionMode.Decompress);

        return stream;
    }

    private bool TryResolveExisting(ResPath path, out ResPath resolved)
    {
        foreach (var candidate in PathCandidates(path.ToRootedPath()))
        {
            if (!_resources.UserData.Exists(candidate))
                continue;

            resolved = candidate;
            return true;
        }

        resolved = path.ToRootedPath();
        return false;
    }

    private static IEnumerable<ResPath> PathCandidates(ResPath path)
    {
        yield return path;

        var compressed = ToCompressedPath(path);
        if (compressed != path)
            yield return compressed;

        var uncompressed = ToUncompressedPath(path);
        if (uncompressed != path)
            yield return uncompressed;
    }

    private static ResPath ToCompressedPath(ResPath path)
    {
        var text = path.ToString();
        if (text.EndsWith(GzipExtension, StringComparison.OrdinalIgnoreCase))
            return path;

        if (text.EndsWith(YamlExtension, StringComparison.OrdinalIgnoreCase))
            return new ResPath(text + ".gz");

        return new ResPath(text + GzipExtension);
    }

    private static ResPath ToUncompressedPath(ResPath path)
    {
        var text = path.ToString();
        if (text.EndsWith(GzipExtension, StringComparison.OrdinalIgnoreCase))
            return new ResPath(text[..^3]);

        return path;
    }

    private static bool IsCompressedPath(ResPath path)
    {
        return path.ToString().EndsWith(GzipExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGzipMagic(Stream stream)
    {
        if (!stream.CanSeek)
            return false;

        var position = stream.Position;
        var header = stream.ReadByte() == 0x1F && stream.ReadByte() == 0x8B;
        stream.Position = position;
        return header;
    }

    private void DeleteExact(ResPath path)
    {
        if (_resources.UserData.Exists(path))
            _resources.UserData.Delete(path);
    }

    private void DeleteUncompressedSibling(ResPath gzPath)
    {
        var uncompressed = ToUncompressedPath(gzPath);
        if (uncompressed != gzPath)
            DeleteExact(uncompressed);
    }

    private bool SaveWithStrippedAtmosphere(EntityUid root, bool isMap, Func<bool> save)
    {
        var snapshots = new List<(GridAtmosphereComponent Atmos, Dictionary<Vector2i, TileAtmosphere> Tiles)>();
        var query = AllEntityQuery<GridAtmosphereComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var atmosphere, out var xform))
        {
            if (!BelongsToRoot(root, uid, xform, isMap))
                continue;

            snapshots.Add((atmosphere, atmosphere.Tiles));
            atmosphere.Tiles = [];
        }

        try
        {
            return save();
        }
        finally
        {
            foreach (var (atmosphere, tiles) in snapshots)
                atmosphere.Tiles = tiles;
        }
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
                    if (!contained.IsValid() || !Exists(contained))
                        continue;

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
