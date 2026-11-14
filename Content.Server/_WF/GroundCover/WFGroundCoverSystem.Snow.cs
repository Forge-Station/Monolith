using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Atmos;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;
    [Dependency] private SharedWolfWeatherSystem _wolfWeather = default!;

    /// <summary>Chunks updated a tick at most.</summary>
    public const int ChunksPerTick = 8;

    /// <summary>Fewest depth units laid on a chunk at once, so slow snowfall isn't rounded away.</summary>
    private const float MinFallUnits = 3f;

    /// <summary>Fewest seconds melted on a chunk at once.</summary>
    private const double MinMeltSeconds = 10;

    /// <summary>Snow doesn't settle on tiles whose air is this much above freezing.</summary>
    private const float SettleLimit = 2f;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private readonly Queue<(EntityUid Grid, Vector2i Chunk)> _queue = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Chunk)> _queued = new();
    private readonly List<(EntityUid Grid, Vector2i Chunk)> _view = new();
    private TimeSpan _nextScan;

    private void UpdateSnow(TimeSpan now)
    {
        if (now >= _nextScan)
        {
            _nextScan = now + ScanInterval;
            foreach (var session in _player.Sessions)
            {
                _view.Clear();
                CollectView(session, _view);
                foreach (var key in _view)
                {
                    if (_queued.Add(key))
                        _queue.Enqueue(key);
                }
            }
        }

        for (var i = 0; i < ChunksPerTick && _queue.TryDequeue(out var key); i++)
        {
            _queued.Remove(key);
            UpdateChunk(key.Grid, key.Chunk);
        }
    }

    /// <summary>
    /// Whether a grid may hold snow cover. A vessel never does, on a planet or off one, however its tiles are roofed;
    /// nor does any other grid on a planet layer but its ground and city streets.
    /// </summary>
    public bool CanHoldCover(EntityUid grid)
    {
        return _weather.WfPlanetExposure(grid) != false;
    }

    /// <summary>Lays, melts or starts the snow of one chunk from its map's totals. False if the chunk didn't change.</summary>
    public bool UpdateChunk(EntityUid gridUid, Vector2i index)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid)
            || !TryComp(gridUid, out TransformComponent? xform)
            || !TryComp<WFGroundWeatherComponent>(xform.MapUid, out var ground)
            || !CanHoldCover(gridUid))
            return false;

        TryComp<WFSnowCoverComponent>(gridUid, out var cover);
        WFSnowChunk? chunk = null;
        cover?.Chunks.TryGetValue(index, out chunk);

        var roofed = new Entity<MapGridComponent, RoofComponent?>(gridUid, grid, CompOrNull<RoofComponent>(gridUid));
        var map = xform.MapUid.Value;
        bool changed;

        if (chunk == null)
        {
            if (ground.BaseDepth <= 0f && ground.Snow <= 0f)
                return false;

            cover ??= EnsureComp<WFSnowCoverComponent>(gridUid);
            chunk = new WFSnowChunk { FallenAt = ground.Fallen, MeltAt = ground.MeltClock };
            cover.Chunks[index] = chunk;
            Apply(roofed, map, index, chunk, ground.BaseDepth * WFSnowCover.UnitsPerLevel, 0);
            chunk.Version++;
            return true;
        }

        var fall = (float) (ground.Fallen - chunk.FallenAt) * WFSnowCover.UnitsPerLevel;
        var melt = ground.MeltClock - chunk.MeltAt;
        var doFall = fall >= MinFallUnits;
        var doMelt = melt >= MinMeltSeconds;
        if (!doFall && !doMelt)
            return false;

        if (doFall)
            chunk.FallenAt = ground.Fallen;

        if (doMelt)
            chunk.MeltAt = ground.MeltClock;

        changed = Apply(roofed, map, index, chunk, doFall ? fall : 0f, doMelt ? (float) melt : 0f);
        if (changed)
            chunk.Version++;

        if (chunk.IsEmpty() && ground.BaseDepth <= 0f && ground.Snow <= 0f)
        {
            cover!.Chunks.Remove(index);
            if (cover.Chunks.Count == 0)
                RemComp<WFSnowCoverComponent>(gridUid);
            return true;
        }

        return changed;
    }

    /// <summary>Lays some depth units on the chunk's open tiles and melts some seconds off its warm ones.</summary>
    private bool Apply(Entity<MapGridComponent, RoofComponent?> grid, EntityUid map, Vector2i index, WFSnowChunk chunk, float fall, float meltSeconds)
    {
        var changed = false;
        var planet = _weather.WfPlanetExposure(grid.Owner) == true;
        for (var i = 0; i < WFSnowCover.ChunkArea; i++)
        {
            var tile = WFSnowCover.TileAt(index, i);
            var depth = (int) chunk.Depths[i];
            var tileRef = _map.GetTileRef(grid.Owner, grid.Comp1, tile);

            if (tileRef.Tile.IsEmpty)
            {
                depth = 0;
            }
            else if (fall > 0f || meltSeconds > 0f && depth > 0)
            {
                var flags = _wolfWeather.Classify(grid, tileRef);
                var open = (flags & WolfWeatherTile.Exposed) != 0 && (flags & WolfWeatherTile.Blocker) == 0;
                var temperature = TileTemperature(grid.Owner, map, tile, open && planet);
                if (fall > 0f && open && temperature <= Freezing + SettleLimit)
                    depth = Math.Min(WFSnowCover.MaxDepth, depth + (int) MathF.Round(fall * WFSnowCover.Drift(tile)));

                if (meltSeconds > 0f && depth > 0 && temperature > Freezing)
                    depth = Math.Max(0, depth - (int) MathF.Ceiling(MeltRate(temperature) * meltSeconds / 60f * WFSnowCover.UnitsPerLevel));

                // Snow falling on open water melts into it.
                if (depth > 0 && IsOpenWater(grid.Owner, tile))
                    depth = 0;
            }

            if (depth == chunk.Depths[i])
                continue;

            chunk.Depths[i] = (byte) depth;
            changed = true;
        }

        return changed;
    }

    /// <summary>The air temperature over a tile; a tile with no air of its own takes the map's.</summary>
    public float TileTemperature(EntityUid grid, EntityUid map, Vector2i tile, bool outdoors = false)
    {
        return AirOver(grid, map, tile, outdoors)?.Temperature ?? 0f;
    }

    /// <summary>
    /// The air over a tile. Open sky on a planet takes the planet's air, as built ground such as a city street keeps
    /// its own air from when it was laid while the climate only moves the map's.
    /// </summary>
    private GasMixture? AirOver(EntityUid grid, EntityUid map, Vector2i tile, bool outdoors)
    {
        if (outdoors && _atmos.GetTileMixture(null, map, tile) is { TotalMoles: >= 1f } outside)
            return outside;

        var air = _atmos.GetTileMixture((grid, null, null), map, tile);
        if (air == null || air.TotalMoles < 1f)
            air = _atmos.GetTileMixture(null, map, tile);

        return air;
    }

    /// <summary>Snow depth on a tile, in units of which <see cref="WFSnowCover.UnitsPerLevel"/> make a level.</summary>
    public int GetDepth(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<WFSnowCoverComponent>(grid, out var cover)
            || !cover.Chunks.TryGetValue(WFSnowCover.ChunkOf(tile), out var chunk))
            return 0;

        return chunk.Depths[WFSnowCover.IndexIn(tile)];
    }

    /// <summary>Sets the snow depth on a tile, as shoveling and admins do.</summary>
    public void SetDepth(EntityUid grid, Vector2i tile, int depth)
    {
        depth = Math.Clamp(depth, 0, WFSnowCover.MaxDepth);
        if (depth > 0 && !CanHoldCover(grid))
            return;

        var index = WFSnowCover.ChunkOf(tile);
        if (!TryComp<WFSnowCoverComponent>(grid, out var cover))
        {
            if (depth == 0)
                return;

            cover = EnsureComp<WFSnowCoverComponent>(grid);
        }

        if (!cover.Chunks.TryGetValue(index, out var chunk))
        {
            if (depth == 0)
                return;

            chunk = new WFSnowChunk();
            if (TryComp(grid, out TransformComponent? xform) && TryComp<WFGroundWeatherComponent>(xform.MapUid, out var ground))
            {
                chunk.FallenAt = ground.Fallen;
                chunk.MeltAt = ground.MeltClock;
            }

            cover.Chunks[index] = chunk;
        }

        var i = WFSnowCover.IndexIn(tile);
        if (chunk.Depths[i] == depth)
            return;

        chunk.Depths[i] = (byte) depth;
        chunk.Version++;
    }

    /// <summary>Whether weather reaches a tile, so it gets wet and snowed on.</summary>
    public bool IsOpen(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp) || !_map.TryGetTileRef(grid, gridComp, tile, out var tileRef))
            return false;

        var roofed = new Entity<MapGridComponent, RoofComponent?>(grid, gridComp, CompOrNull<RoofComponent>(grid));
        var flags = _wolfWeather.Classify(roofed, tileRef);
        return (flags & WolfWeatherTile.Exposed) != 0 && (flags & WolfWeatherTile.Blocker) == 0;
    }

    /// <summary>Strips snow a grid shouldn't hold, listing its chunks so the clients that were sent them are told it's gone.</summary>
    private void PurgeCover(EntityUid grid, List<(EntityUid Grid, Vector2i Chunk)> into)
    {
        if (!TryComp<WFSnowCoverComponent>(grid, out var cover))
            return;

        foreach (var index in cover.Chunks.Keys)
        {
            into.Add((grid, index));
        }

        RemComp<WFSnowCoverComponent>(grid);
    }

    /// <summary>The snow chunks of grids near a session's body, by default only on maps that have had weather.</summary>
    private void CollectView(Robust.Shared.Player.ICommonSession session, List<(EntityUid Grid, Vector2i Chunk)> into, bool weatherOnly = true)
    {
        if (session.AttachedEntity is not { } ent)
            return;

        // Inside a vehicle the view out is of the world around its hull.
        if (_wolfWeather.TryGetHull(ent, out var hull))
            ent = hull;

        if (!TryComp(ent, out TransformComponent? xform) || xform.MapUid is not { } map
            || weatherOnly && !HasComp<WFGroundWeatherComponent>(map))
            return;

        var position = _transform.GetWorldPosition(xform);
        var box = Box2.CenteredAround(position, new System.Numerics.Vector2(ViewRange * 2f));
        _grids.Clear();
        _map.FindGridsIntersecting(xform.MapID, box, ref _grids);
        foreach (var grid in _grids)
        {
            if (!CanHoldCover(grid))
            {
                PurgeCover(grid.Owner, into);
                continue;
            }

            var local = _transform.GetInvWorldMatrix(grid).TransformBox(box);
            var min = WFSnowCover.ChunkOf(new Vector2i((int) MathF.Floor(local.Left), (int) MathF.Floor(local.Bottom)));
            var max = WFSnowCover.ChunkOf(new Vector2i((int) MathF.Floor(local.Right), (int) MathF.Floor(local.Top)));
            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                    into.Add((grid.Owner, new Vector2i(x, y)));
            }
        }
    }
}
