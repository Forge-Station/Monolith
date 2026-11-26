using System.Linq;
using System.Threading.Tasks;
using Content.Server.Procedural;
using Content.Shared.Procedural;
using Content.Shared.Procedural.DungeonGenerators;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Loads the room atlases a site's dungeon draws its rooms from before the dungeon job asks for them. The job loads an
/// atlas, a map of a couple of thousand entities, in one go the first time a room needs it; here it is parsed off the
/// main thread and its bare entities spawned within the generation budget.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Loads every atlas the dungeon's rooms come from that isn't loaded yet, as the dungeon system would.</summary>
    private async Task WarmAtlasesAsync(DungeonConfigPrototype config)
    {
        foreach (var path in AtlasesOf(config))
        {
            if (AtlasLoaded(path))
                continue;

            if (await ReadMapAsync(path) is not { } data || MapRoot(data) is not { } root)
                continue;

            var deferred = await Task.Run(() => TakePlainEntities(data, root));
            await _preload.Breathe();
            // Another dungeon may have loaded it meanwhile.
            if (AtlasLoaded(path))
                continue;

            var options = new MapLoadOptions
            {
                DeserializationOptions = DeserializationOptions.Default with { PauseMaps = true },
                ExpectedCategory = FileCategory.Map,
            };

            LoadResult? result;
            using (_preload.Time($"loading room atlas {path}, {deferred.Count} spawned after"))
            {
                if (!_loader.TryLoadGeneric(data, path.ToString(), out result, options) || result.Maps.Count != 1)
                    continue;
            }

            var map = result.Maps.Single().Owner;
            await SpawnDeferredAsync(map, deferred);
            if (TerminatingOrDeleted(map) || AtlasLoaded(path))
                continue;

            EnsureComp<DungeonAtlasTemplateComponent>(map).Path = path;
        }
    }

    /// <summary>The uid of a map file's map entity: listed under <c>maps</c>, or in older files the one with a Map component.</summary>
    private static string? MapRoot(MappingDataNode data)
    {
        if (data.TryGet<SequenceDataNode>("maps", out var maps))
            return maps.Count == 1 ? ((ValueDataNode) maps[0]).Value : null;

        if (!data.TryGet<SequenceDataNode>("entities", out var groups))
            return null;

        foreach (var group in groups)
        {
            foreach (var entity in ((MappingDataNode) group).Get<SequenceDataNode>("entities"))
            {
                var node = (MappingDataNode) entity;
                if (node.TryGet<SequenceDataNode>("components", out var components)
                    && components.Any(c => ((MappingDataNode) c).Get<ValueDataNode>("type").Value == "Map"))
                    return node.Get<ValueDataNode>("uid").Value;
            }
        }

        return null;
    }

    private bool AtlasLoaded(ResPath path)
    {
        var query = AllEntityQuery<DungeonAtlasTemplateComponent>();
        while (query.MoveNext(out var atlas))
        {
            if (atlas.Path.Equals(path))
                return true;
        }

        return false;
    }

    /// <summary>The atlases of the rooms a dungeon config and the configs it nests can pick, by their room tags.</summary>
    private HashSet<ResPath> AtlasesOf(DungeonConfigPrototype config)
    {
        var tags = new HashSet<string>();
        var seen = new HashSet<string>();
        var pending = new Stack<DungeonConfigPrototype>();
        pending.Push(config);
        while (pending.TryPop(out var next))
        {
            if (!seen.Add(next.ID))
                continue;

            foreach (var whitelist in next.Data.Whitelists.Values)
            {
                foreach (var tag in whitelist.Tags ?? new())
                {
                    tags.Add(tag.Id);
                }
            }

            foreach (var layer in next.Layers)
            {
                if (layer is PrototypeDunGen nested && _proto.TryIndex(nested.Proto, out var inner))
                    pending.Push(inner);
            }
        }

        var atlases = new HashSet<ResPath>();
        foreach (var room in _proto.EnumeratePrototypes<DungeonRoomPrototype>())
        {
            if (room.Tags.Any(tag => tags.Contains(tag.Id)))
                atlases.Add(room.AtlasPath);
        }

        return atlases;
    }
}
