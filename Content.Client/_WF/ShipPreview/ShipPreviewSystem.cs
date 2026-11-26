using System.IO;
using System.Linq;
using System.Numerics;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Client._WF.ShipPreview;

/// <summary>
/// A grid loaded into a preview map, with the numbers a previewer wants to show next to it.
/// </summary>
public readonly record struct ShipPreviewGrid(
    Entity<MapGridComponent> Grid,
    Box2 LocalBounds,
    Vector2i SizeInTiles,
    int TileCount);

/// <summary>
/// One previewer's private preview map and the grid loaded on it. Handed out by <see cref="ShipPreviewSystem.Acquire"/>.
/// </summary>
public sealed class ShipPreviewHandle
{
    internal MapId MapId = MapId.Nullspace;
    internal EntityUid MapUid = EntityUid.Invalid;
    internal ResPath? LoadedPath;
    internal ShipPreviewGrid? Loaded;
    internal LoadResult? LoadedEntities;

    /// <summary>
    /// This previewer's map, or nullspace until its first load.
    /// </summary>
    public MapId PreviewMap => MapId;
}

/// <summary>
/// Loads ship grids onto client-side preview maps so UI can render them without touching the server or the
/// player's own view. Every previewer gets its own map, so two open previews never replace or clear each other.
/// </summary>
/// <remarks>
/// Maps are created without map-init and stay paused, so spawners, atmos and timers on the loaded grid never run.
/// Client map ids are negative, so they can never collide with a server-allocated one arriving in a game state.
/// </remarks>
public sealed partial class ShipPreviewSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private readonly List<ShipPreviewHandle> _handles = new();

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var handle in _handles)
        {
            DestroyMap(handle);
        }

        _handles.Clear();
    }

    /// <summary>
    /// Hands out a private preview slot. Every call must be matched by a <see cref="Release"/>.
    /// </summary>
    public ShipPreviewHandle Acquire()
    {
        var handle = new ShipPreviewHandle();
        _handles.Add(handle);
        return handle;
    }

    /// <summary>
    /// Deletes the handle's map and everything on it.
    /// </summary>
    public void Release(ShipPreviewHandle handle)
    {
        DestroyMap(handle);
        _handles.Remove(handle);
    }

    /// <summary>
    /// The handle's previewed grid, if one is loaded and still alive.
    /// </summary>
    public ShipPreviewGrid? GetCurrent(ShipPreviewHandle handle)
    {
        return handle.Loaded is { } loaded && Exists(loaded.Grid.Owner) ? loaded : null;
    }

    /// <summary>
    /// Loads a vessel's grid, replacing whatever the handle previewed before. Loading the vessel that is already
    /// shown does nothing.
    /// </summary>
    public bool TryLoad(ShipPreviewHandle handle, VesselPrototype vessel, out ShipPreviewGrid preview)
    {
        return TryLoad(handle, vessel.ShuttlePath, vessel.Name, out preview);
    }

    /// <summary>
    /// Loads a grid file, replacing whatever the handle previewed before. Loading the file that is already shown
    /// does nothing.
    /// </summary>
    public bool TryLoad(ShipPreviewHandle handle, ResPath path, string? name, out ShipPreviewGrid preview)
    {
        return TryLoad(handle, path, name, out preview, () => _loader.TryReadFile(path, out var data) ? data : null);
    }

    /// <summary>
    /// Loads a grid from YAML text, such as a file the server sent over, under <paramref name="key"/> so loading the
    /// same text again does nothing.
    /// </summary>
    public bool TryLoadText(ShipPreviewHandle handle, ResPath key, string yaml, string? name, out ShipPreviewGrid preview)
    {
        return TryLoad(handle, key, name, out preview, () =>
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode.ToDataNode() as MappingDataNode : null;
        });
    }

    private bool TryLoad(
        ShipPreviewHandle handle,
        ResPath path,
        string? name,
        out ShipPreviewGrid preview,
        Func<MappingDataNode?> read)
    {
        preview = default;

        if (GetCurrent(handle) is { } current && handle.LoadedPath == path)
        {
            preview = current;
            return true;
        }

        if (!EnsureMap(handle))
            return false;

        Clear(handle);

        Entity<MapGridComponent>? grid;
        try
        {
            if (read() is not { } data)
            {
                Log.Warning($"Ship preview couldn't read {path}");
                return false;
            }

            if (!TryLoadGrid(handle, data, out grid))
            {
                Log.Warning($"Ship preview failed to load grid {path}");
                return false;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Ship preview threw while loading grid {path}: {e}");
            DestroyMap(handle);
            return false;
        }

        // Park it at the origin unrotated so the previewer can work in plain grid-local coordinates.
        _xform.SetLocalPositionRotation(grid.Value.Owner, Vector2.Zero, Angle.Zero);

        if (!string.IsNullOrEmpty(name))
            _meta.SetEntityName(grid.Value.Owner, name);

        var bounds = grid.Value.Comp.LocalAABB;
        var tileSize = MathF.Max(grid.Value.Comp.TileSize, 1f);
        var size = new Vector2i(
            (int) MathF.Round(bounds.Width / tileSize),
            (int) MathF.Round(bounds.Height / tileSize));

        // Counted once here rather than per frame; the grid never changes while it is previewed.
        var tiles = 0;
        var enumerator = _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp);
        while (enumerator.MoveNext(out _))
        {
            tiles++;
        }

        preview = new ShipPreviewGrid(grid.Value, bounds, size, tiles);
        handle.Loaded = preview;
        handle.LoadedPath = path;
        return true;
    }

    /// <summary>
    /// Drops components the client has no registration for, such as the server-only shuttle and pathfinding
    /// components every saved ship carries, which the loader would otherwise log an error for each time.
    /// </summary>
    private void RemoveUnknownComponents(MappingDataNode data)
    {
        if (!data.TryGet("entities", out SequenceDataNode? prototypes))
            return;

        foreach (var prototype in prototypes.OfType<MappingDataNode>())
        {
            if (!prototype.TryGet("entities", out SequenceDataNode? entities))
                continue;

            foreach (var entity in entities.OfType<MappingDataNode>())
            {
                if (!entity.TryGet("components", out SequenceDataNode? components))
                    continue;

                for (var i = components.Count - 1; i >= 0; i--)
                {
                    if (components[i] is MappingDataNode component &&
                        component.TryGet("type", out ValueDataNode? type) &&
                        !_factory.TryGetRegistration(type.Value, out _))
                    {
                        components.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Deletes the handle's previewed grid, leaving its map in place for the next load.
    /// </summary>
    public void Clear(ShipPreviewHandle handle)
    {
        if (handle.LoadedEntities is { } entities)
            _loader.Delete(entities);

        handle.LoadedEntities = null;
        handle.Loaded = null;
        handle.LoadedPath = null;
    }

    /// <summary>
    /// Creates the handle's map if it is missing. Also recovers from an entity flush (disconnect, round restart)
    /// silently taking the old map away.
    /// </summary>
    private bool EnsureMap(ShipPreviewHandle handle)
    {
        if (MapAlive(handle))
            return true;

        // Stale ids after a flush; a new map may well have taken the old id.
        Clear(handle);
        handle.MapId = MapId.Nullspace;
        handle.MapUid = EntityUid.Invalid;

        try
        {
            handle.MapUid = _map.CreateMap(out var mapId, runMapInit: false);
            handle.MapId = mapId;
        }
        catch (Exception e)
        {
            Log.Error($"Ship preview failed to create its map: {e}");
            handle.MapUid = EntityUid.Invalid;
            handle.MapId = MapId.Nullspace;
            return false;
        }

        _meta.SetEntityName(handle.MapUid, "Wolfgate ship preview");
        return true;
    }

    private bool MapAlive(ShipPreviewHandle handle)
    {
        return handle.MapId != MapId.Nullspace
               && Exists(handle.MapUid)
               && _map.TryGetMap(handle.MapId, out var existing)
               && existing == handle.MapUid;
    }

    private void DestroyMap(ShipPreviewHandle handle)
    {
        Clear(handle);

        if (MapAlive(handle))
            Del(handle.MapUid);

        handle.MapId = MapId.Nullspace;
        handle.MapUid = EntityUid.Invalid;
    }
}
