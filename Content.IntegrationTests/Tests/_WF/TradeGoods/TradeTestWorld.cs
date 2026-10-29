#nullable enable
using System.Numerics;
using Content.Server._WF.TradeGoods.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>Builds market maps for the trade tests: named grids, chutes and crates hauled in from elsewhere.</summary>
public static class TradeTestWorld
{
    public const string Chute = "CargoChuteTradeMall";
    public const string Fence = "WFCargoChuteUnderground";
    public const string Fabric = "TradeGoodFabric";

    /// <summary>Makes the test map a market map and puts a chute on its grid.</summary>
    public static EntityUid MarketChute(IEntityManager entMan, TestMapData map, string chute = Chute)
    {
        entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
        return entMan.SpawnEntity(chute, map.GridCoords);
    }

    /// <summary>A named grid of a row of tiles on the test map.</summary>
    public static EntityUid Grid(IEntityManager entMan, TestMapData map, Vector2 position, string name, int tiles = 1)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var grid = mapSys.CreateGridEntity(map.MapId);
        for (var x = 0; x < tiles; x++)
        {
            mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
        }

        entMan.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, position);
        entMan.System<MetaDataSystem>().SetEntityName(grid.Owner, name);
        return grid.Owner;
    }

    /// <summary>A named station grid with a chute of its own.</summary>
    public static (EntityUid Grid, EntityUid Chute) Station(IEntityManager entMan, TestMapData map, Vector2 position, string name,
        string chute = Chute)
    {
        var grid = Grid(entMan, map, position, name);
        return (grid, entMan.SpawnEntity(chute, new EntityCoordinates(grid, 0.5f, 0.5f)));
    }

    /// <summary>A crate made on one grid, then carried somewhere else.</summary>
    public static EntityUid Hauled(IEntityManager entMan, EntityUid madeOn, EntityCoordinates to, string proto = Fabric)
    {
        var crate = entMan.SpawnEntity(proto, new EntityCoordinates(madeOn, 0.5f, 0.5f));
        entMan.System<SharedTransformSystem>().SetCoordinates(crate, to);
        return crate;
    }

    /// <summary>An entity's name.</summary>
    public static string Name(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityName;
    }
}
