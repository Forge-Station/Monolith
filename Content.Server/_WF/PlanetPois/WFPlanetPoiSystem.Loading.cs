using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Server._WF.PlanetPois;

/// <summary>Map files for sites, wrecks and camps, read and parsed off the main thread so only the entities cost a tick.</summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private IResourceManager _resources = default!;

    /// <summary>Reads and parses a map file on the thread pool; null if it is missing or isn't one document.</summary>
    private Task<MappingDataNode?> ReadMapAsync(ResPath path)
    {
        var rooted = path.ToRootedPath();
        return Task.Run(() =>
        {
            TextReader reader;
            if (_resources.UserData.Exists(rooted))
                reader = _resources.UserData.OpenText(rooted);
            else if (_resources.TryContentFileRead(rooted, out var stream))
                reader = new StreamReader(stream);
            else
                return null;

            using (reader)
            {
                var documents = DataNodeParser.ParseYamlStream(reader).ToArray();
                return documents.Length == 1 ? (MappingDataNode?) documents[0].Root : null;
            }
        });
    }

    /// <summary>
    /// Loads a single-grid map file onto a map, as the loader's own TryLoadGrid does, with the parsing off the main thread.
    /// A <paramref name="whole"/> grid is kept in one piece however many islands it has.
    /// </summary>
    private async Task<Entity<MapGridComponent>?> LoadGridAsync(MapId map, ResPath path, bool whole = false)
    {
        if (await ReadMapAsync(path) is not { } data)
        {
            Log.Error($"Cannot read map file {path}.");
            return null;
        }

        if (whole)
            KeepWhole(data);

        var deferred = data.TryGet<SequenceDataNode>("grids", out var grids) && grids.Count == 1
            ? await Task.Run(() => TakePlainEntities(data, ((ValueDataNode) grids[0]).Value))
            : new List<DeferredSpawn>();

        var options = new MapLoadOptions { MergeMap = map, ExpectedCategory = FileCategory.Grid };
        LoadResult? result;
        using (_preload.Time($"loading {path}, {deferred.Count} spawned after"))
        {
            if (!_loader.TryLoadGeneric(data, path.ToString(), out result, options))
                return null;
        }

        if (result.Grids.Count != 1)
        {
            _loader.Delete(result);
            return null;
        }

        var grid = result.Grids.Single();
        await SpawnDeferredAsync(grid.Owner, deferred);
        return TerminatingOrDeleted(grid.Owner) ? null : grid;
    }

    /// <summary>An entity left out of a map load, to spawn after it: one saved with nothing but where it stands.</summary>
    private readonly record struct DeferredSpawn(string Proto, Vector2 Position, Angle Rotation);

    /// <summary>
    /// Takes out of a parsed map file the entities saved with nothing but their place on the root grid, and that no other
    /// entity names, so the load itself stays short; <see cref="SpawnDeferredAsync"/> puts them back a slice a tick.
    /// Walls, rock, cables and water are most of a big map. Touches only the data, so it can run off the main thread.
    /// </summary>
    private List<DeferredSpawn> TakePlainEntities(MappingDataNode data, string root)
    {
        var taken = new List<DeferredSpawn>();
        if (!data.TryGet<SequenceDataNode>("entities", out var groups))
            return taken;

        // Any number saved in any entity's data may name another entity: those named stay in the load. The grid's own
        // data (tiles, decals, air) is numbers that name nothing.
        var named = new HashSet<string>();
        foreach (var group in groups)
        {
            foreach (var entity in ((MappingDataNode) group).Get<SequenceDataNode>("entities"))
            {
                if (!((MappingDataNode) entity).TryGet<SequenceDataNode>("components", out var components))
                    continue;

                if (((MappingDataNode) entity).TryGet<ValueDataNode>("uid", out var self) && self.Value == root)
                {
                    // A saved ship's repair blueprint names its parts; on the ground it repairs nothing and its names
                    // would point at entities taken out below.
                    for (var c = components.Count - 1; c >= 0; c--)
                    {
                        if (((MappingDataNode) components[c]).Get<ValueDataNode>("type").Value == "ShipRepairData")
                            components.RemoveAt(c);
                    }
                    continue;
                }

                foreach (var component in components)
                {
                    var node = (MappingDataNode) component;
                    if (node.Get<ValueDataNode>("type").Value == "Transform")
                    {
                        if (node.TryGet<ValueDataNode>("parent", out var parent))
                            named.Add(parent.Value);
                        continue;
                    }

                    CollectScalars(node, named);
                }
            }
        }

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = (MappingDataNode) groups[i];
            // A renamed or removed prototype is left to the loader, which knows the migrations.
            if (!group.TryGet<ValueDataNode>("proto", out var proto) || string.IsNullOrEmpty(proto.Value)
                || !_proto.HasIndex<EntityPrototype>(proto.Value))
                continue;

            var entities = group.Get<SequenceDataNode>("entities");
            var kept = new SequenceDataNode();
            foreach (var entity in entities)
            {
                if (Plain((MappingDataNode) entity, root, named) is { } spawn)
                    taken.Add(spawn with { Proto = proto.Value });
                else
                    kept.Add(entity);
            }

            if (kept.Count == entities.Count)
                continue;

            if (kept.Count == 0)
            {
                groups.RemoveAt(i);
                continue;
            }

            group.Remove("entities");
            group.Add("entities", kept);
        }

        return taken;
    }

    /// <summary>Where an entity stands, if all that is saved of it is its place on the root grid and nothing names it.</summary>
    private static DeferredSpawn? Plain(MappingDataNode entity, string root, HashSet<string> named)
    {
        if (!entity.TryGet<ValueDataNode>("uid", out var uid) || named.Contains(uid.Value)
            || !entity.TryGet<SequenceDataNode>("components", out var components) || components.Count != 1)
            return null;

        var transform = (MappingDataNode) components[0];
        if (transform.Get<ValueDataNode>("type").Value != "Transform"
            || !transform.TryGet<ValueDataNode>("parent", out var parent) || parent.Value != root
            || !transform.TryGet<ValueDataNode>("pos", out var pos))
            return null;

        foreach (var key in transform.Keys)
        {
            if (key is not ("type" or "parent" or "pos" or "rot"))
                return null;
        }

        var xy = pos.Value.Split(',');
        if (xy.Length != 2 || !Parse.TryFloat(xy[0], out var x) || !Parse.TryFloat(xy[1], out var y))
            return null;

        var rotation = Angle.Zero;
        if (transform.TryGet<ValueDataNode>("rot", out var rot))
        {
            if (!Parse.TryDouble(rot.Value.Replace("rad", string.Empty).Trim(), out var radians))
                return null;
            rotation = new Angle(radians);
        }

        return new DeferredSpawn(string.Empty, new Vector2(x, y), rotation);
    }

    /// <summary>Adds every scalar and key under a node.</summary>
    private static void CollectScalars(DataNode node, HashSet<string> into)
    {
        switch (node)
        {
            case ValueDataNode value:
                into.Add(value.Value);
                break;
            case SequenceDataNode sequence:
                foreach (var child in sequence)
                {
                    CollectScalars(child, into);
                }
                break;
            case MappingDataNode mapping:
                foreach (var (key, child) in mapping)
                {
                    into.Add(key);
                    CollectScalars(child, into);
                }
                break;
        }
    }

    /// <summary>Spawns what <see cref="TakePlainEntities"/> left out of a grid's load, within the generation budget.</summary>
    private async Task SpawnDeferredAsync(EntityUid grid, List<DeferredSpawn> deferred)
    {
        foreach (var (proto, position, rotation) in deferred)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(grid))
                return;

            var spawned = Spawn(proto, new EntityCoordinates(grid, position));
            if (rotation != Angle.Zero)
                _transform.SetLocalRotation(spawned, rotation);
        }
    }

    /// <summary>
    /// Stops every grid in a map file splitting as it loads. With grid splitting on, as it is outside a dev build, a grid
    /// loads in pieces wherever its tiles don't touch, and only the piece that keeps the grid would be merged.
    /// </summary>
    private static void KeepWhole(MappingDataNode data)
    {
        if (!data.TryGet<SequenceDataNode>("entities", out var groups))
            return;

        foreach (var group in groups)
        {
            if (!((MappingDataNode) group).TryGet<SequenceDataNode>("entities", out var entities))
                continue;

            foreach (var entity in entities)
            {
                if (!((MappingDataNode) entity).TryGet<SequenceDataNode>("components", out var components))
                    continue;

                foreach (var component in components)
                {
                    var node = (MappingDataNode) component;
                    if (node.Get<ValueDataNode>("type").Value != "MapGrid")
                        continue;

                    node.Remove("canSplit");
                    node.Add("canSplit", new ValueDataNode("false"));
                }
            }
        }
    }
}
