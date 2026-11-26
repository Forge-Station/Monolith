using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>A spawner prototype and what it can spawn: the data <see cref="WFMappingSpawnerIndex"/> is built from.</summary>
public readonly record struct WFMappingSpawnerSource(
    string Id,
    IReadOnlyList<string> Prototypes,
    IReadOnlyList<string> Rare,
    float Chance);

/// <summary>
/// Which spawner prototype to place instead of an entity the map file won't keep, such as a mob. Spawner components
/// are server-only, so the server builds the index and sends it to mappers.
/// </summary>
public sealed class WFMappingSpawnerIndex
{
    private readonly Dictionary<string, string> _spawners = new();

    /// <summary>Entity prototype id to the spawner id to suggest for it.</summary>
    public IReadOnlyDictionary<string, string> Spawners => _spawners;

    /// <summary>
    /// Picks one spawner per prototype that <paramref name="wanted"/> accepts: one that spawns only it and always
    /// does first, then one with a common entry for it, then the shortest list, then the lowest id.
    /// </summary>
    public static WFMappingSpawnerIndex Build(IEnumerable<WFMappingSpawnerSource> sources, Func<string, bool> wanted)
    {
        var best = new Dictionary<string, (WFMappingSpawnerSource Source, (int, int, int) Rank)>();
        foreach (var source in sources)
        {
            var size = source.Prototypes.Count + source.Rare.Count;
            var exclusive = size == 1 && source.Chance >= 1f;
            Consider(source, source.Prototypes, 0, exclusive, size, wanted, best);
            Consider(source, source.Rare, 1, exclusive, size, wanted, best);
        }

        var index = new WFMappingSpawnerIndex();
        foreach (var (id, (source, _)) in best)
        {
            index._spawners[id] = source.Id;
        }

        return index;
    }

    private static void Consider(
        WFMappingSpawnerSource source,
        IReadOnlyList<string> list,
        int rare,
        bool exclusive,
        int size,
        Func<string, bool> wanted,
        Dictionary<string, (WFMappingSpawnerSource Source, (int, int, int) Rank)> best)
    {
        foreach (var id in list)
        {
            if (!wanted(id))
                continue;

            var rank = (exclusive ? 0 : 1, rare, size);
            if (!best.TryGetValue(id, out var current) || Better(rank, source.Id, current.Rank, current.Source.Id))
                best[id] = (source, rank);
        }
    }

    private static bool Better((int, int, int) rank, string id, (int, int, int) other, string otherId)
    {
        var order = rank.CompareTo(other);
        return order != 0 ? order < 0 : string.CompareOrdinal(id, otherId) < 0;
    }

    /// <summary>Replaces the index with a table received from the server.</summary>
    public void Load(IReadOnlyDictionary<string, string> spawners)
    {
        _spawners.Clear();
        foreach (var (id, spawner) in spawners)
        {
            _spawners[id] = spawner;
        }
    }

    /// <summary>The spawner to place instead of a prototype, if one spawns it.</summary>
    public bool TryGetSpawner(string id, [NotNullWhen(true)] out string? spawnerId)
    {
        return _spawners.TryGetValue(id, out spawnerId);
    }

    /// <summary>Whether a map file leaves the prototype out, as it does for every mob.</summary>
    public static bool WontSave(EntityPrototype prototype)
    {
        return !prototype.MapSavable;
    }
}

/// <summary>A mapper asks for the table of spawners to suggest for entities that won't save.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingSpawnerIndexRequest : EntityEventArgs;

/// <summary>Server to client: for each entity prototype that won't save, the spawner to place instead.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingSpawnerIndexEvent(Dictionary<string, string> spawners) : EntityEventArgs
{
    public readonly Dictionary<string, string> Spawners = spawners;
}
