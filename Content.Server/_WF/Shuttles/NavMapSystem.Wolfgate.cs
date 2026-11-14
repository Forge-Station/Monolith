using Content.Shared.Pinpointer;

namespace Content.Server.Pinpointer;

public sealed partial class NavMapSystem
{
    /// <summary>
    /// Gives a grid nav map data if it doesn't have any yet, so consoles can display its layout.
    /// Station grids get this on station init; ships spawned outside a station do not.
    /// </summary>
    public void EnsureGridNavMap(EntityUid gridUid)
    {
        if (_navQuery.HasComponent(gridUid))
            return;

        if (!_gridQuery.TryComp(gridUid, out var mapGrid))
            return;

        RefreshGrid(gridUid, EnsureComp<NavMapComponent>(gridUid), mapGrid);
    }
}
