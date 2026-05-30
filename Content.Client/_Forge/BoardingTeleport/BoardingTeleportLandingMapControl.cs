using System.Numerics;
using Content.Client.Pinpointer.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._Forge.BoardingTeleport;

public sealed class BoardingTeleportLandingMapControl : NavMapControl
{
    private readonly SharedMapSystem _map;
    private readonly IGameTiming _timing;

    public NetEntity? NetGrid;
    public float ScatterRadius;

    public event Action<NetEntity, Vector2i>? TileSelected;

    private Vector2i? _selectedTile;

    public BoardingTeleportLandingMapControl()
    {
        IoCManager.InjectDependencies(this);
        _map = EntManager.System<SharedMapSystem>();
        _timing = IoCManager.Resolve<IGameTiming>();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick ||
            MapUid is not { } gridUid ||
            NetGrid is not { } netGrid ||
            !EntManager.TryGetComponent<MapGridComponent>(gridUid, out var grid) ||
            !EntManager.TryGetComponent<PhysicsComponent>(gridUid, out var physics))
        {
            return;
        }

        var localPosition = args.PointerLocation.Position - GlobalPixelPosition;
        var unscaledPosition = (localPosition - MidPointVector) / MinimapScale;
        var gridPosition = new Vector2(unscaledPosition.X, -unscaledPosition.Y) + Offset + physics.LocalCenter;
        var tile = _map.LocalToTile(gridUid, grid, new EntityCoordinates(gridUid, gridPosition));

        _selectedTile = tile;
        TileSelected?.Invoke(netGrid, tile);
        args.Handle();
    }

    public void SetSelectedTile(Vector2i? tile)
    {
        _selectedTile = tile;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (MapUid is not { } gridUid ||
            !EntManager.TryGetComponent<MapGridComponent>(gridUid, out var grid) ||
            !EntManager.TryGetComponent<PhysicsComponent>(gridUid, out var physics))
        {
            return;
        }

        if (_selectedTile is { } selectedTile)
            DrawLandingMarker(handle, gridUid, grid, physics, selectedTile);

        if (_selectedTile is { } scatterCenter && ScatterRadius > 0.01f)
            DrawScatterZone(handle, gridUid, grid, physics, scatterCenter, ScatterRadius);
    }

    private void DrawLandingMarker(DrawingHandleScreen handle, EntityUid gridUid, MapGridComponent grid, PhysicsComponent physics, Vector2i tile)
    {
        var center = TileToControlPosition(gridUid, grid, physics, tile);
        var radius = MathF.Max(5f, MinimapScale * 0.45f);
        var pulse = (float) ((Math.Sin(_timing.CurTime.TotalSeconds * 5.0) + 1.0) * 0.5);
        var pulseRadius = radius + 2f + pulse * 5f;

        handle.DrawCircle(center, pulseRadius, Color.FromHex("#FF6868"), filled: false);
        handle.DrawCircle(center, radius, Color.FromHex("#FFE27A"), filled: false);
        handle.DrawLine(center + new Vector2(-radius - 4f, 0f), center + new Vector2(radius + 4f, 0f), Color.FromHex("#FFB35C"));
        handle.DrawLine(center + new Vector2(0f, -radius - 4f), center + new Vector2(0f, radius + 4f), Color.FromHex("#FFB35C"));
        handle.DrawCircle(center, 2f, Color.White);
    }

    private void DrawScatterZone(DrawingHandleScreen handle, EntityUid gridUid, MapGridComponent grid, PhysicsComponent physics, Vector2i centerTile, float scatterRadius)
    {
        var center = TileToControlPosition(gridUid, grid, physics, centerTile);
        var scatterPixels = scatterRadius * MinimapScale;
        var pulse = (float) ((Math.Sin(_timing.CurTime.TotalSeconds * 2.5) + 1.0) * 0.5);
        var outer = scatterPixels + 2f + pulse * 3f;

        handle.DrawCircle(center, outer, Color.FromHex("#6BB8FF55"), filled: false);
        handle.DrawCircle(center, scatterPixels, Color.FromHex("#6BB8FFAA"), filled: false);
    }

    private Vector2 TileToControlPosition(EntityUid gridUid, MapGridComponent grid, PhysicsComponent physics, Vector2i tile)
    {
        var tileCoordinates = _map.GridTileToLocal(gridUid, grid, tile);
        var offset = Offset + physics.LocalCenter;
        return ScalePosition(new Vector2(tileCoordinates.X, -tileCoordinates.Y) - new Vector2(offset.X, -offset.Y));
    }
}
