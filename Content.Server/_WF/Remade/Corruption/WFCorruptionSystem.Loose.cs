using Content.Server.Spreader;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>Corruption with no Monolith behind it, which grows only where something else puts it: the Coda's flesh.</summary>
public sealed partial class WFCorruptionSystem
{
    /// <summary>Puts down one tile of corruption that doesn't spread by itself, with its wall growth. Null if the tile has some already.</summary>
    public EntityUid? GrowLoose(EntityCoordinates coordinates)
    {
        if (!TryComp<MapGridComponent>(coordinates.EntityId, out var grid))
            return null;

        var indices = _map.CoordinatesToTile(coordinates.EntityId, grid, coordinates);
        if (IsCorrupted((coordinates.EntityId, grid), indices))
            return null;

        var tile = Spawn(CorruptionProto, _map.GridTileToLocal(coordinates.EntityId, grid, indices));
        RemComp<ActiveEdgeSpreaderComponent>(tile);
        Comp<WFCorruptionComponent>(tile).Grid = coordinates.EntityId;
        _gridTiles[coordinates.EntityId] = GridTiles(coordinates.EntityId) + 1;
        RefreshAround(coordinates.EntityId, indices, null);
        return tile;
    }

    /// <summary>Starts a tile dying within the given seconds.</summary>
    public void Wither(EntityUid uid, float withinSeconds)
    {
        if (TryComp<WFCorruptionComponent>(uid, out var tile) && tile.DecayAt == null)
            SetDecay(uid, tile, _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, withinSeconds)));
    }
}
