using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Procedural;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Parallax;

public sealed partial class BiomeSystem
{
    private const string XenoHiveBiome = "ForgeXenoHive";
    private const string XenoCaveFloor = "FloorCaveDrought";
    private const string XenoCaveDoor = "DoorXenoResin";
    private const string XenoCaveWeeds = "XenoWeeds";
    private const int XenoCaveTunnelLimit = 48;

    private static readonly string[] XenoCaveWalls =
    {
        "WallRock",
        "WallRockBasalt",
        "WallXenoResin",
    };

    private static readonly Vector2i[] XenoCaveCardinals =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    /// <summary>
    /// Digs a weeded tunnel from each hive entrance out to the first open cavern,
    /// and closes the mouth with a resin door. Cave walls only generate on
    /// <see cref="XenoCaveFloor"/>, so this is what keeps the entrance from being sealed.
    /// </summary>
    public void ConnectXenoHive(EntityUid mapUid, MapGridComponent grid, BiomeComponent biome, Dungeon dungeon)
    {
        if (biome.Template != XenoHiveBiome)
            return;

        foreach (var tile in dungeon.AllTiles)
        {
            if (!HasAnchoredPrototype(mapUid, grid, tile, XenoCaveDoor))
                continue;

            if (!TryGetHiveOutward(dungeon, tile, out var step))
                continue;

            CarveHiveTunnel(mapUid, grid, biome, dungeon, tile + step, step);
        }
    }

    /// <summary>
    /// Turns thin rock between two caverns into resin doors. Runs after the chunk's
    /// walls exist, so the door replaces the wall instead of stacking on it.
    /// </summary>
    private void PlaceXenoCavePassages(BiomeComponent component, EntityUid gridUid, MapGridComponent grid, Vector2i chunk)
    {
        if (component.Template != XenoHiveBiome)
            return;

        var endX = chunk.X + ChunkSize;
        var endY = chunk.Y + ChunkSize;

        for (var x = chunk.X; x < endX; x++)
        {
            for (var y = chunk.Y; y < endY; y++)
            {
                var tile = new Vector2i(x, y);

                if (!TryGetCaveWall(gridUid, grid, component, tile, out var wall))
                    continue;

                if (!IsCavePassage(gridUid, grid, component, tile))
                    continue;

                if (!ShouldOpenPassage(gridUid, grid, component, tile))
                    continue;

                Del(wall);
                SpawnAnchored(gridUid, grid, tile, XenoCaveDoor);
            }
        }
    }

    private void CarveHiveTunnel(
        EntityUid mapUid,
        MapGridComponent grid,
        BiomeComponent biome,
        Dungeon dungeon,
        Vector2i origin,
        Vector2i step)
    {
        var carved = new List<Vector2i>(XenoCaveTunnelLimit);
        var pos = origin;

        for (var i = 0; i < XenoCaveTunnelLimit && !dungeon.AllTiles.Contains(pos); i++)
        {
            if (!IsXenoCaveWall(mapUid, grid, biome, pos))
                break;

            carved.Add(pos);
            pos += step;
        }

        if (carved.Count == 0)
            return;

        var reachedCave = !dungeon.AllTiles.Contains(pos) && !IsXenoCaveWall(mapUid, grid, biome, pos);
        var perp = new Vector2i(-step.Y, step.X);
        var last = carved.Count - 1;

        for (var i = 0; i < carved.Count; i++)
        {
            var tile = carved[i];
            var mouth = reachedCave && i == last;
            CarveHiveTile(mapUid, grid, biome, tile, mouth);

            if (mouth)
                continue;

            CarveHiveTile(mapUid, grid, biome, tile + perp, door: false);
            CarveHiveTile(mapUid, grid, biome, tile - perp, door: false);
        }
    }

    private void CarveHiveTile(EntityUid mapUid, MapGridComponent grid, BiomeComponent biome, Vector2i tile, bool door)
    {
        if (!IsXenoCaveWall(mapUid, grid, biome, tile))
            return;

        SetXenoCaveFloor(mapUid, grid, tile);
        SpawnAnchored(mapUid, grid, tile, door ? XenoCaveDoor : XenoCaveWeeds);
    }

    private bool IsCavePassage(EntityUid gridUid, MapGridComponent grid, BiomeComponent biome, Vector2i tile)
    {
        var north = IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(0, 1));
        var south = IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(0, -1));
        var east = IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(1, 0));
        var west = IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(-1, 0));

        // A lone boulder in a cavern is not a passage.
        if (!north && !south && !east && !west)
            return false;

        if (!north && !south)
            return true;

        if (!east && !west)
            return true;

        // Two tiles of rock with open cave on either side.
        if (!north && south && !IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(0, -2)))
            return true;

        if (!south && north && !IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(0, 2)))
            return true;

        if (!west && east && !IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(2, 0)))
            return true;

        if (!east && west && !IsXenoCaveWall(gridUid, grid, biome, tile + new Vector2i(-2, 0)))
            return true;

        return false;
    }

    /// <summary>
    /// Pinch points always open. A long thin membrane keeps some rock so it does not become a door curtain.
    /// </summary>
    private bool ShouldOpenPassage(EntityUid gridUid, MapGridComponent grid, BiomeComponent biome, Vector2i tile)
    {
        var walls = 0;

        foreach (var dir in XenoCaveCardinals)
        {
            if (IsXenoCaveWall(gridUid, grid, biome, tile + dir))
                walls++;
        }

        if (walls <= 1)
            return true;

        unchecked
        {
            var hash = tile.X * 73856093 ^ tile.Y * 19349663 ^ biome.Seed;
            return (hash & 3) != 0;
        }
    }

    private bool IsXenoCaveWall(EntityUid gridUid, MapGridComponent grid, BiomeComponent biome, Vector2i tile)
    {
        if (_mapSystem.TryGetTileRef(gridUid, grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty)
        {
            if (TileDefManager[tileRef.Tile.TypeId].ID != XenoCaveFloor)
                return false;
        }

        if (TryGetAnchoredCaveWall(gridUid, grid, tile, out _))
            return true;

        if (HasAnchoredEntity(gridUid, grid, tile))
            return false;

        return TryGetEntity(tile, biome, (gridUid, grid), out var proto) && IsCaveWallPrototype(proto);
    }

    private bool TryGetCaveWall(EntityUid gridUid, MapGridComponent grid, BiomeComponent biome, Vector2i tile, out EntityUid wall)
    {
        wall = default;

        if (!IsXenoCaveWall(gridUid, grid, biome, tile))
            return false;

        return TryGetAnchoredCaveWall(gridUid, grid, tile, out wall);
    }

    private bool TryGetAnchoredCaveWall(EntityUid gridUid, MapGridComponent grid, Vector2i tile, out EntityUid wall)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);

        while (anchored.MoveNext(out var ent))
        {
            if (ent is not { } uid)
                continue;

            var id = MetaData(uid).EntityPrototype?.ID;

            if (id == null || !IsCaveWallPrototype(id))
                continue;

            wall = uid;
            return true;
        }

        wall = default;
        return false;
    }

    private bool HasAnchoredPrototype(EntityUid gridUid, MapGridComponent grid, Vector2i tile, string prototype)
    {
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);

        while (anchored.MoveNext(out var ent))
        {
            if (ent is { } uid && MetaData(uid).EntityPrototype?.ID == prototype)
                return true;
        }

        return false;
    }

    private static bool IsCaveWallPrototype(string prototype)
    {
        foreach (var wall in XenoCaveWalls)
        {
            if (wall == prototype)
                return true;
        }

        return false;
    }

    private static bool TryGetHiveOutward(Dungeon dungeon, Vector2i entrance, out Vector2i step)
    {
        foreach (var dir in XenoCaveCardinals)
        {
            var inward = entrance + dir;

            if (!dungeon.RoomTiles.Contains(inward) && !dungeon.CorridorTiles.Contains(inward))
                continue;

            var outward = entrance - dir;

            if (dungeon.AllTiles.Contains(outward))
                continue;

            step = -dir;
            return true;
        }

        step = default;
        return false;
    }

    private void SetXenoCaveFloor(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var def = (ContentTileDefinition) TileDefManager[XenoCaveFloor];
        _mapSystem.SetTile(gridUid, grid, tile, new Tile(def.TileId, 0));
    }

    private void SpawnAnchored(EntityUid gridUid, MapGridComponent grid, Vector2i tile, string prototype)
    {
        var ent = Spawn(prototype, _mapSystem.GridTileToLocal(gridUid, grid, tile));

        if (_xformQuery.TryGetComponent(ent, out var xform) && !xform.Anchored)
            _transform.AnchorEntity((ent, xform), (gridUid, grid), tile);
    }
}
