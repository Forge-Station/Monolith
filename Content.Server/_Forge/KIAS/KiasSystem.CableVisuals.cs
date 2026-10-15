using Content.Shared._Forge.KIAS;
using Content.Shared.Wires;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasSystem
{
    private void UpdateCableVisuals(EntityUid grid, MapGridComponent map, KiasGridComponent runtime)
    {
        var tiles = new HashSet<Vector2i>();
        foreach (var cable in runtime.Cables)
            if (!TerminatingOrDeleted(cable)) tiles.Add(_map.TileIndicesFor(grid, map, Transform(cable).Coordinates));
        foreach (var cable in runtime.Cables)
        {
            if (TerminatingOrDeleted(cable)) continue;
            var tile = _map.TileIndicesFor(grid, map, Transform(cable).Coordinates);
            var mask = WireVisDirFlags.None;
            if (tiles.Contains(tile + new Vector2i(0, 1))) mask |= WireVisDirFlags.North;
            if (tiles.Contains(tile + new Vector2i(0, -1))) mask |= WireVisDirFlags.South;
            if (tiles.Contains(tile + new Vector2i(1, 0))) mask |= WireVisDirFlags.East;
            if (tiles.Contains(tile + new Vector2i(-1, 0))) mask |= WireVisDirFlags.West;
            _appearance.SetData(cable, WireVisVisuals.ConnectedMask, mask);
        }
    }
}
