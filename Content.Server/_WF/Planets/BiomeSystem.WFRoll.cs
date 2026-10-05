using System.Linq;
using Content.Server._WF.Planets;
using Content.Server.Spawners.Components;
using Content.Shared._WF.Planets;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Parallax;

/// <summary>
/// Rolls a planet layer's one-rock spawners in the chunk loader. Left to roll for itself, a spawner marker lays a rock
/// the biome doesn't know and pins the tile as it deletes itself, so that rock never unloads. A wildlife marker is
/// taken away here too, before its deleting itself can pin the floor under it.
/// </summary>
// The roll is seeded from the tile, so a rock nobody touched comes back as the same rock, and salted per layer, so
// every round still lays its ore differently.
public sealed partial class BiomeSystem
{
    [Dependency] private EntityTableSystem _wfTables = default!;

    /// <summary>Per entity prototype: the table of a spawner the loader may roll itself, or null for anything else.</summary>
    private readonly Dictionary<string, EntityTableSelector?> _wfRollTables = new();

    /// <summary>Per entity prototype: whether a roll that comes to it alone can be laid and tracked as the tile's own.</summary>
    private readonly Dictionary<string, bool> _wfRollResults = new();

    /// <summary>Per entity prototype: whether it is a wildlife marker that notes its place and deletes itself.</summary>
    private readonly Dictionary<string, bool> _wfPassingMarkers = new();

    /// <summary>
    /// Lays what a spawner prototype rolls for a tile and tracks it, in place of the spawner. False when the prototype
    /// is not such a spawner or the roll is not a single fixed structure; the loader then spawns it as it always did.
    /// </summary>
    private bool WfLayRoll(BiomeComponent biome, EntityUid map, MapGridComponent grid, Vector2i tile, string prototype,
        Dictionary<EntityUid, Vector2i> loaded)
    {
        if (WfPassingMarker(prototype))
        {
            if (!HasComp<WFPlanetLayerComponent>(map))
                return false;

            // It has told the wildlife its place by the time it is spawned, and that is kept by map cell, so it can
            // go at once and come again with its chunk. Tracked, its deleting itself would pin the tile for good.
            var marker = Spawn(prototype, _mapSystem.GridTileToLocal(map, grid, tile));
            _wfUnloading = true;

            try
            {
                Del(marker);
            }
            finally
            {
                _wfUnloading = false;
            }

            return true;
        }

        if (!WfRollTable(prototype, out var table) || !HasComp<WFPlanetLayerComponent>(map))
            return false;

        string? result;

        try
        {
            result = WfRoll(table, WfTileSeed(biome.Seed, _wfLayers.GetOrNew(map).RollSalt ??= _random.Next(), tile));
        }
        catch (Exception e)
        {
            // A weighted pick can throw on a draw of exactly one, and a seeded roll would throw on that tile for good.
            Log.Debug($"Rolling {prototype} for {tile} of {ToPrettyString(map)} failed; its own spawner rolls instead. {e.Message}");
            return false;
        }

        if (result == null)
            return false;

        var uid = Spawn(result, _mapSystem.GridTileToLocal(map, grid, tile));

        if (_xformQuery.TryGetComponent(uid, out var xform) && !xform.Anchored)
            _transform.AnchorEntity((uid, xform), (map, grid), tile);

        loaded.Add(uid, tile);
        return true;
    }

    /// <summary>What a table rolls from a seed, if that is one structure fixed to its tile and nothing else.</summary>
    private string? WfRoll(EntityTableSelector table, int seed)
    {
        // Walked once: a selector draws as it is walked, so a second walk is a second roll.
        var rolled = _wfTables.GetSpawns(table, new System.Random(seed)).ToList();

        if (rolled.Count != 1)
            return null;

        var id = rolled[0].Id;
        return WfRollResult(id) ? id : null;
    }

    /// <summary>The table of a spawner that lays its roll on its own tile and then deletes itself.</summary>
    private bool WfRollTable(string prototype, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EntityTableSelector? table)
    {
        if (!_wfRollTables.TryGetValue(prototype, out table))
        {
            if (ProtoManager.TryIndex<EntityPrototype>(prototype, out var proto)
                && proto.TryGetComponent<EntityTableSpawnerComponent>(out var spawner, Factory)
                && spawner is { Offset: 0f, DeleteSpawnerAfterSpawn: true })
            {
                table = spawner.Table;
            }

            _wfRollTables[prototype] = table;
        }

        return table != null;
    }

    /// <summary>Whether a prototype is a wildlife marker that deletes itself once it has noted its place.</summary>
    private bool WfPassingMarker(string prototype)
    {
        if (_wfPassingMarkers.TryGetValue(prototype, out var passing))
            return passing;

        passing = ProtoManager.TryIndex<EntityPrototype>(prototype, out var proto)
                  && proto.TryGetComponent<WFPlanetFaunaSpawnerComponent>(out var marker, Factory)
                  && !marker.KeepEntity;

        _wfPassingMarkers[prototype] = passing;
        return passing;
    }

    /// <summary>Whether a rolled prototype is a structure fixed to its tile, and not another spawner.</summary>
    private bool WfRollResult(string prototype)
    {
        if (_wfRollResults.TryGetValue(prototype, out var fixedStructure))
            return fixedStructure;

        fixedStructure = ProtoManager.TryIndex<EntityPrototype>(prototype, out var proto)
                         && proto.TryGetComponent<TransformComponent>(out var xform, Factory)
                         && xform.Anchored
                         && !proto.TryGetComponent<EntityTableSpawnerComponent>(out _, Factory)
                         && !proto.TryGetComponent<ConditionalSpawnerComponent>(out _, Factory)
                         && !proto.TryGetComponent<RandomSpawnerComponent>(out _, Factory)
                         && !proto.TryGetComponent<WFPlanetFaunaSpawnerComponent>(out _, Factory);

        _wfRollResults[prototype] = fixedStructure;
        return fixedStructure;
    }

    /// <summary>Forgets what the loader learned of entity prototypes when they reload.</summary>
    private void WfRollReload(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>())
            return;

        _wfRollTables.Clear();
        _wfRollResults.Clear();
        _wfPassingMarkers.Clear();
    }

    /// <summary>A seed for one tile's roll: the same for the same biome seed, layer salt and tile, and nothing else.</summary>
    public static int WfTileSeed(int seed, int salt, Vector2i tile)
    {
        unchecked
        {
            var hash = ((uint) seed * 0x9E3779B1u) ^ (uint) salt;
            hash = (hash ^ (uint) tile.X) * 0x85EBCA6Bu;
            hash = (hash ^ (hash >> 13) ^ (uint) tile.Y) * 0xC2B2AE35u;
            return (int) (hash ^ (hash >> 16));
        }
    }

    /// <summary>Whether an entity is a spawner that is still there: one that lays things beside itself and stays.</summary>
    private bool WfIsStandingSpawner(EntityUid uid)
    {
        return HasComp<ConditionalSpawnerComponent>(uid)
               || HasComp<RandomSpawnerComponent>(uid)
               || HasComp<EntityTableSpawnerComponent>(uid)
               || HasComp<WFPlanetFaunaSpawnerComponent>(uid);
    }
}
