using System.Numerics;
using Content.Server.Decals;
using Content.Server.Parallax;
using Content.Server.Tiles;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Flight;

public sealed partial class WFFlightSystem
{
    [Dependency] private BiomeSystem _scarBiome = default!;
    [Dependency] private DecalSystem _scarDecals = default!;
    [Dependency] private ITileDefinitionManager _scarTiles = default!;
    private const int ScarTilesPerBite = 256;

    /// <summary>The exposed earth a skidding hull leaves behind.</summary>
    public static readonly ProtoId<ContentTileDefinition> ScarTile = "FloorPlanetDirt";

    /// <summary>Scrape the occupied footprint into persistent exposed earth, revealed behind the moving hull.</summary>
    public void ScarSkidGround(Entity<MapGridComponent> hull, WFSkidComponent skid)
    {
        if (!_zLevels.WfHasSkidGround(hull.Owner)
            || Transform(hull).MapUid is not { } ground
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return;
        if (skid.ScarMap != ground)
        {
            skid.ScarMap = ground;
            skid.ScarredTiles.Clear();
        }

        var edits = new List<(Vector2i, Tile)>();
        var reserved = new List<(Vector2i, Tile)>();
        var matrix = _transform.GetWorldMatrix(hull);
        var scar = new Tile(_scarTiles[ScarTile].TileId);
        var tiles = _map.GetAllTilesEnumerator(hull, hull.Comp);
        while (tiles.MoveNext(out var tile))
        {
            var centre = ((Vector2) tile.Value.GridIndices + new Vector2(0.5f)) * hull.Comp.TileSize;
            var index = _map.WorldToTile(ground, groundGrid, Vector2.Transform(centre, matrix));
            if (!skid.ScarredTiles.Add(index)
                || !_map.TryGetTileRef(ground, groundGrid, index, out var original) || original.Tile.IsEmpty)
                continue;
            var definition = (ContentTileDefinition) _scarTiles[original.Tile.TypeId];
            // Never fill holes, water, blood rivers or lava with a strip of new land.
            if (definition.Reagent != null || definition.Friction <= 0f)
                continue;

            var liquid = false;
            var anchored = _map.GetAnchoredEntitiesEnumerator(ground, groundGrid, index);
            while (anchored.MoveNext(out var entity))
                liquid |= HasComp<TileEntityEffectComponent>(entity);
            if (liquid)
                continue;

            var pos = (Vector2) index * groundGrid.TileSize;
            reserved.Clear();
            _scarBiome.ReserveTiles(ground, new Box2(pos, pos + new Vector2(groundGrid.TileSize)), reserved, mapGrid: groundGrid);
            edits.Add((index, scar));
            if (edits.Count >= ScarTilesPerBite)
                break;
        }
        _map.SetTiles(ground, groundGrid, edits);
        foreach (var (index, _) in edits)
            _scarDecals.TryAddDecal("Damaged", new EntityCoordinates(ground, (Vector2) index * groundGrid.TileSize),
                out _, color: Color.FromHex("#3b3029"), cleanable: false);
    }
}
