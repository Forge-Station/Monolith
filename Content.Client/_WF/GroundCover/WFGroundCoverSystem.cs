using System.Numerics;
using Content.Client._WF.WolfWeather;
using Content.Shared._WF.GroundCover;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.GroundCover;

/// <summary>Keeps the snow cover chunks the server sent, and draws wet ground and snow.</summary>
public sealed class WFGroundCoverSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(10);

    /// <summary>Snow depths per grid, by net entity so chunks may arrive before their grid does.</summary>
    private readonly Dictionary<NetEntity, Dictionary<Vector2i, byte[]>> _snow = new();

    private readonly HashSet<NetEntity> _missing = new();
    private readonly List<NetEntity> _gone = new();
    private List<Entity<MapGridComponent>> _covers = new();
    private TimeSpan _nextSweep;

    /// <summary>Goes up whenever snow cover changes, so the overlay knows to redraw its mask.</summary>
    public int Version { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFSnowCoverChunksMessage>(OnChunks);
        var ground = new WFGroundCoverOverlay();
        _overlay.AddOverlay(ground);
        _overlay.AddOverlay(new WFGroundReflectionOverlay(ground));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<WFGroundCoverOverlay>();
        _overlay.RemoveOverlay<WFGroundReflectionOverlay>();
        _snow.Clear();
    }

    private void OnChunks(WFSnowCoverChunksMessage message)
    {
        if (!_snow.TryGetValue(message.Grid, out var chunks))
        {
            chunks = new Dictionary<Vector2i, byte[]>();
            _snow[message.Grid] = chunks;
        }

        foreach (var chunk in message.Chunks)
        {
            if (chunk.Depths is { Length: WFSnowCover.ChunkArea } depths)
                chunks[chunk.Index] = depths;
            else
                chunks.Remove(chunk.Index);
        }

        if (chunks.Count == 0)
            _snow.Remove(message.Grid);

        Version++;
    }

    /// <summary>
    /// Whether a point lies over a tile of a vessel, which keeps snow, footprints and tracks off the ground
    /// beneath it. Marks on the ground under a ship stay in memory and show again once it lifts.
    /// </summary>
    public bool IsUnderVessel(MapId map, Vector2 world)
    {
        _covers.Clear();
        _map.FindGridsIntersecting(map, Box2.CenteredAround(world, new Vector2(0.1f)), ref _covers);
        foreach (var grid in _covers)
        {
            if (!_wolfWeather.IsCoverGrid(grid.Owner))
                continue;

            var local = Vector2.Transform(world, _xform.GetInvWorldMatrix(grid.Owner));
            var size = grid.Comp.TileSize;
            var index = new Vector2i((int) MathF.Floor(local.X / size), (int) MathF.Floor(local.Y / size));
            if (_map.TryGetTileRef(grid.Owner, grid.Comp, index, out var tile) && !tile.Tile.IsEmpty)
                return true;
        }

        return false;
    }

    /// <summary>Whether any snow cover is known on a grid.</summary>
    public bool HasSnow(EntityUid grid)
    {
        return TryGetNetEntity(grid, out var net) && _snow.ContainsKey(net.Value);
    }

    /// <summary>Snow depth on a tile, as last sent.</summary>
    public int GetDepth(EntityUid grid, Vector2i tile)
    {
        if (!TryGetNetEntity(grid, out var net)
            || !_snow.TryGetValue(net.Value, out var chunks)
            || !chunks.TryGetValue(WFSnowCover.ChunkOf(tile), out var depths))
            return 0;

        return depths[WFSnowCover.IndexIn(tile)];
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSweep)
            return;

        // A grid unknown for two sweeps in a row is gone.
        _nextSweep = _timing.CurTime + SweepInterval;
        _gone.Clear();
        foreach (var net in _snow.Keys)
        {
            if (TryGetEntity(net, out var uid) && !TerminatingOrDeleted(uid))
            {
                _missing.Remove(net);
                continue;
            }

            if (!_missing.Add(net))
                _gone.Add(net);
        }

        if (_gone.Count > 0)
            Forget(_gone);
    }

    /// <summary>
    /// Drops the snow of these grids and tells the server, which still counts it as sent: if a grid comes back, as after
    /// a full state resync, its snow is sent again rather than predicted as bare ground.
    /// </summary>
    public void Forget(List<NetEntity> grids)
    {
        foreach (var net in grids)
        {
            _snow.Remove(net);
            _missing.Remove(net);
        }

        Version++;
        RaiseNetworkEvent(new WFSnowCoverForgetMessage { Grids = new List<NetEntity>(grids) });
    }
}
