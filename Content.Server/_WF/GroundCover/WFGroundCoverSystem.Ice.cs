using Content.Server._WF.Planets;
using Content.Server.Atmos.Components;
using Content.Shared._WF.GroundCover;
using Content.Shared.Atmos;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.StepTrigger.Components;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private WFPlanetClimateSystem _climate = default!;
    [Dependency] private FrictionContactsSystem _friction = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SpeedModifierContactsSystem _speedContacts = default!;
    [Dependency] private StepTriggerSystem _stepTrigger = default!;

    /// <summary>
    /// How long air must hold below a water's <see cref="WFFreezableWaterComponent.FreezeAt"/> before it freezes, or
    /// above its <see cref="WFFreezableWaterComponent.ThawAt"/> before the ice thaws.
    /// </summary>
    public static readonly TimeSpan IceDelay = TimeSpan.FromMinutes(3);

    /// <summary>How fast ice spreads in from the shore, or thaws back from it, in tiles a minute.</summary>
    public const float IceTilesPerMinute = 2f;

    /// <summary>
    /// Thickest ice grows, in degree-minutes: a minute at -8 C adds 8, a minute at 5 C melts 5. A winter night's ice
    /// outlasts a mild afternoon; a warm spring day melts it through.
    /// </summary>
    public const float IceMaxThickness = 480f;

    /// <summary>Farthest a tile counts from the shore; water deeper in freezes with the tiles this far out.</summary>
    public const int MaxShore = 8;

    /// <summary>How long a hole broken in the ice stays open.</summary>
    public static readonly TimeSpan HoleTime = TimeSpan.FromMinutes(5);

    private const int IceChunksPerTick = 4;

    /// <summary>A chunk unseen for longer than this catches up along its world's temperature curve, not the air now.</summary>
    private static readonly TimeSpan IceHistoryAfter = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan IceHistoryStep = TimeSpan.FromMinutes(5);

    /// <summary>Most steps a catch-up takes; a longer absence takes longer steps.</summary>
    private const int IceHistoryMaxSteps = 600;

    private static readonly TimeSpan IceInterval = TimeSpan.FromSeconds(2);

    private readonly Queue<(EntityUid Grid, Vector2i Chunk)> _iceQueue = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Chunk)> _iceQueued = new();
    private readonly HashSet<EntityUid> _contacts = new();
    private readonly Queue<int> _shoreQueue = new();
    private TimeSpan _nextIceScan;

    private void InitializeIce()
    {
        SubscribeLocalEvent<WFFreezableWaterComponent, MapInitEvent>(OnWaterInit);
    }

    private void UpdateIce(TimeSpan now)
    {
        if (now >= _nextIceScan)
        {
            _nextIceScan = now + ScanInterval;
            foreach (var session in _player.Sessions)
            {
                _view.Clear();
                CollectView(session, _view, weatherOnly: false);
                foreach (var key in _view)
                    QueueIce(key);
            }
        }

        for (var i = 0; i < IceChunksPerTick && _iceQueue.TryDequeue(out var key); i++)
        {
            _iceQueued.Remove(key);
            UpdateIceChunk(key.Grid, key.Chunk);
        }
    }

    private void QueueIce((EntityUid Grid, Vector2i Chunk) key)
    {
        if (_iceQueued.Add(key))
            _iceQueue.Enqueue(key);
    }

    /// <summary>Water spawned on a chunk already looked over, as a biome chunk loading again, takes its ice.</summary>
    private void OnWaterInit(Entity<WFFreezableWaterComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<SpeedModifierContactsComponent>(ent, out var speed))
        {
            ent.Comp.WalkSpeed = speed.WalkSpeedModifier;
            ent.Comp.SprintSpeed = speed.SprintSpeedModifier;
        }

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid
            || !TryComp<WFIceCoverComponent>(gridUid, out var cover)
            || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var index = WFSnowCover.ChunkOf(_map.TileIndicesFor(gridUid, grid, xform.Coordinates));
        if (!cover.Chunks.TryGetValue(index, out var chunk))
            return;

        if (chunk == null)
            cover.Chunks.Remove(index);
        else
            chunk.Stale = true;

        QueueIce((gridUid, index));
    }

    /// <summary>
    /// Freezes or thaws one chunk's water for the time since it was last worked, from its air, on maps with outdoor
    /// air. False if nothing on it changed.
    /// </summary>
    public bool UpdateIceChunk(EntityUid gridUid, Vector2i index)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid)
            || !TryComp(gridUid, out TransformComponent? xform)
            || xform.MapUid is not { } map
            || _weather.WfPlanetExposure(gridUid) == false
            || !TryComp<MapAtmosphereComponent>(map, out var air) || air.Space)
            return false;

        var now = _timing.CurTime;
        var cover = EnsureComp<WFIceCoverComponent>(gridUid);
        if (!cover.Chunks.TryGetValue(index, out var chunk))
        {
            chunk = ScanIce((gridUid, grid), index, null);
            cover.Chunks[index] = chunk;
            if (chunk == null)
                return false;

            // Water first seen in a cold spell has been freezing all along.
            if (IceTemperature(gridUid, map, index, chunk) is { } start && start <= chunk.FreezeAt)
            {
                chunk.Front = MaxShore;
                chunk.Cold = (float) IceDelay.TotalSeconds;
                chunk.Thickness = MathF.Min(IceMaxThickness, (chunk.ThawAt - start) * 60f);
            }

            chunk.Updated = now;
            return ApplyIce(gridUid, index, chunk);
        }

        if (chunk == null)
            return false;

        if (chunk.Stale)
        {
            chunk = ScanIce((gridUid, grid), index, chunk);
            cover.Chunks[index] = chunk;
            if (chunk == null)
                return true;
        }
        else if (now - chunk.Updated < IceInterval)
        {
            return false;
        }

        var since = chunk.Updated;
        var seconds = (float) (now - since).TotalSeconds;
        chunk.Updated = now;
        if (IceTemperature(gridUid, map, index, chunk) is { } temperature)
            CatchUpIce(chunk, map, temperature, since, now);

        if (chunk.Holes != null)
        {
            foreach (var (hole, left) in chunk.Holes)
            {
                if (left <= seconds)
                    chunk.Holes.Remove(hole);
                else
                    chunk.Holes[hole] = left - seconds;
            }

            if (chunk.Holes.Count == 0)
                chunk.Holes = null;
        }

        return ApplyIce(gridUid, index, chunk);
    }

    /// <summary>
    /// Moves a chunk's ice on from a time to now. A short gap is spent in the air now; a long one, as for a chunk
    /// nobody watched through a season, is walked in steps along the world's own temperature curve, shifted to the
    /// air now.
    /// </summary>
    private void CatchUpIce(WFIceChunk chunk, EntityUid map, float temperature, TimeSpan since, TimeSpan now)
    {
        var gap = now - since;
        if (gap <= TimeSpan.Zero)
            return;

        if (gap <= IceHistoryAfter || _climate.GetClearTemperatureAt(map, now) is not { } current)
        {
            StepIce(chunk, temperature, (float) gap.TotalSeconds);
            return;
        }

        var step = TimeSpan.FromTicks(Math.Max(IceHistoryStep.Ticks, gap.Ticks / IceHistoryMaxSteps));
        for (var at = since; at < now; at += step)
        {
            var end = at + step > now ? now : at + step;
            var then = _climate.GetClearTemperatureAt(map, at + (end - at) / 2) ?? current;
            StepIce(chunk, temperature + then - current, (float) (end - at).TotalSeconds);
        }
    }

    /// <summary>Moves a chunk's ice on by some seconds in air of a temperature.</summary>
    public static void StepIce(WFIceChunk chunk, float temperature, float seconds)
    {
        var delay = (float) IceDelay.TotalSeconds;
        if (temperature <= chunk.FreezeAt)
        {
            chunk.Warm = 0f;
            var cold = MathF.Min(seconds, chunk.Cold + seconds - delay);
            chunk.Cold = MathF.Min(chunk.Cold + seconds, delay * 2f);
            if (cold <= 0f)
                return;

            var spread = cold * IceTilesPerMinute / 60f;
            chunk.Thaw = MathF.Max(0f, chunk.Thaw - spread);
            chunk.Front = MathF.Min(MaxShore, chunk.Front + spread);
            chunk.Thickness = MathF.Min(IceMaxThickness, chunk.Thickness + cold / 60f * (chunk.ThawAt - temperature));
        }
        else if (temperature > chunk.ThawAt)
        {
            chunk.Cold = 0f;
            var warm = MathF.Min(seconds, chunk.Warm + seconds - delay);
            chunk.Warm = MathF.Min(chunk.Warm + seconds, delay * 2f);
            if (warm <= 0f || chunk.Front <= 0f)
                return;

            // Warmth melts the ice's thickness first, faster the warmer it is; only then does the shore open.
            if (chunk.Thickness > 0f)
            {
                var melt = (temperature - chunk.ThawAt) / 60f;
                var needed = chunk.Thickness / melt;
                if (warm <= needed)
                {
                    chunk.Thickness -= warm * melt;
                    return;
                }

                chunk.Thickness = 0f;
                warm -= needed;
            }

            var spread = warm * IceTilesPerMinute / 60f;
            chunk.Thaw = MathF.Min(chunk.Front, chunk.Thaw + spread);
            if (chunk.Thaw >= chunk.Front)
            {
                chunk.Front = 0f;
                chunk.Thaw = 0f;
            }
        }
    }

    /// <summary>Sets each water tile of a chunk frozen or not as its ice says; open water swallows its snow.</summary>
    private bool ApplyIce(EntityUid gridUid, Vector2i index, WFIceChunk chunk)
    {
        var changed = false;
        foreach (var (i, water) in chunk.Waters)
        {
            if (!TryComp<WFFreezableWaterComponent>(water, out var comp) || TerminatingOrDeleted(water))
            {
                chunk.Stale = true;
                continue;
            }

            var frozen = chunk.IsFrozen(i);
            if (comp.Frozen != frozen)
            {
                SetFrozen((water, comp), frozen);
                changed = true;
            }

            if (!frozen)
            {
                var tile = WFSnowCover.TileAt(index, i);
                if (GetDepth(gridUid, tile) > 0)
                    SetDepth(gridUid, tile, 0);
            }
        }

        return changed;
    }

    /// <summary>
    /// Freezes water into walkable, slightly slippery ice, or thaws it back: no wading slowdown, no sinking in and no
    /// splash while frozen.
    /// </summary>
    public void SetFrozen(Entity<WFFreezableWaterComponent> ent, bool frozen)
    {
        var (uid, comp) = ent;
        comp.Frozen = frozen;
        comp.IceTick = _timing.CurTick;
        Dirty(uid, comp);

        if (TryComp<SpeedModifierContactsComponent>(uid, out var speed))
        {
            if (frozen)
                _speedContacts.ChangeSpeedModifiers(uid, 1f, 1f, speed);
            else
                _speedContacts.ChangeSpeedModifiers(uid, comp.WalkSpeed, comp.SprintSpeed, speed);
        }

        if (TryComp<StepTriggerComponent>(uid, out var step))
            _stepTrigger.SetActive(uid, !frozen, step);

        _contacts.Clear();
        _physics.GetContactingEntities(uid, _contacts);

        if (frozen)
        {
            var friction = EnsureComp<FrictionContactsComponent>(uid);
            _friction.ChangeFrictionModifiers(uid, comp.IceFriction, comp.IceFriction, comp.IceAcceleration, friction);
            foreach (var other in _contacts)
            {
                _friction.AddModifiedEntity(other);
                if (TryComp<FloorOcclusionComponent>(other, out var occlusion) && occlusion.Colliding.Remove(uid))
                    Dirty(other, occlusion);
            }

            RemComp<FloorOccluderComponent>(uid);
        }
        else
        {
            RemComp<FrictionContactsComponent>(uid);
            EnsureComp<FloorOccluderComponent>(uid);
            foreach (var other in _contacts)
            {
                if (!TryComp<FloorOcclusionComponent>(other, out var occlusion) || occlusion.Colliding.Contains(uid))
                    continue;

                occlusion.Colliding.Add(uid);
                Dirty(other, occlusion);
            }
        }
    }

    /// <summary>Whether a tile is water that isn't frozen, so snow falling there melts into it.</summary>
    public bool IsOpenWater(EntityUid grid, Vector2i tile)
    {
        if (!TryComp<WFIceCoverComponent>(grid, out var cover)
            || !cover.Chunks.TryGetValue(WFSnowCover.ChunkOf(tile), out var chunk)
            || chunk == null)
            return false;

        var i = WFSnowCover.IndexIn(tile);
        return chunk.Shore[i] != 0 && !chunk.IsFrozen(i);
    }

    /// <summary>The ice chunk a tile is in, if it has water.</summary>
    public WFIceChunk? GetIceChunk(EntityUid grid, Vector2i tile)
    {
        return TryComp<WFIceCoverComponent>(grid, out var cover) && cover.Chunks.TryGetValue(WFSnowCover.ChunkOf(tile), out var chunk)
            ? chunk
            : null;
    }

    /// <summary>The air over a chunk's water, or null where it has none to freeze in.</summary>
    private float? IceTemperature(EntityUid grid, EntityUid map, Vector2i index, WFIceChunk chunk)
    {
        foreach (var i in chunk.Waters.Keys)
        {
            var tile = WFSnowCover.TileAt(index, i);
            var outdoors = _weather.WfPlanetExposure(grid) == true && IsOpen(grid, tile);
            var air = AirOver(grid, map, tile, outdoors);
            return air == null || air.TotalMoles < 1f ? null : air.Temperature;
        }

        return null;
    }

    /// <summary>
    /// Finds a chunk's water and each tile's distance to the shore, looking past the chunk's edge as far as
    /// <see cref="MaxShore"/>. Keeps an older chunk's ice. Null if the chunk has no water.
    /// </summary>
    private WFIceChunk? ScanIce(Entity<MapGridComponent> grid, Vector2i index, WFIceChunk? old)
    {
        var chunk = new WFIceChunk();
        var origin = index * WFSnowCover.ChunkSize;
        for (var i = 0; i < WFSnowCover.ChunkArea; i++)
        {
            if (WaterAt(grid, WFSnowCover.TileAt(index, i)) is { } water)
                chunk.Waters[i] = water;
        }

        if (chunk.Waters.Count == 0)
            return null;

        foreach (var water in chunk.Waters.Values)
        {
            if (TryComp<WFFreezableWaterComponent>(water, out var kind))
            {
                chunk.FreezeAt = kind.FreezeAt;
                chunk.ThawAt = kind.ThawAt;
            }

            break;
        }

        const int side = WFSnowCover.ChunkSize + MaxShore * 2;
        var distance = new byte[side * side];
        _shoreQueue.Clear();
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var local = new Vector2i(x - MaxShore, y - MaxShore);
                var inside = local.X >= 0 && local.Y >= 0 && local.X < WFSnowCover.ChunkSize && local.Y < WFSnowCover.ChunkSize;
                var wet = inside
                    ? chunk.Waters.ContainsKey(local.X + local.Y * WFSnowCover.ChunkSize)
                    : WaterAt(grid, origin + local) != null;

                if (wet)
                {
                    distance[x + y * side] = byte.MaxValue;
                    continue;
                }

                _shoreQueue.Enqueue(x + y * side);
            }
        }

        // Spreads out from dry ground a tile at a time, diagonals included.
        while (_shoreQueue.TryDequeue(out var cell))
        {
            var next = distance[cell] + 1;
            if (next > MaxShore)
                continue;

            int cx = cell % side, cy = cell / side;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= side || ny >= side || distance[nx + ny * side] <= next)
                        continue;

                    distance[nx + ny * side] = (byte) next;
                    _shoreQueue.Enqueue(nx + ny * side);
                }
            }
        }

        foreach (var i in chunk.Waters.Keys)
        {
            var cell = i % WFSnowCover.ChunkSize + MaxShore + (i / WFSnowCover.ChunkSize + MaxShore) * side;
            chunk.Shore[i] = Math.Min(distance[cell], (byte) MaxShore);
        }

        if (old != null)
        {
            chunk.Front = old.Front;
            chunk.Thaw = old.Thaw;
            chunk.Thickness = old.Thickness;
            chunk.Cold = old.Cold;
            chunk.Warm = old.Warm;
            chunk.Updated = old.Updated;
            chunk.Holes = old.Holes;
        }

        return chunk;
    }

    private EntityUid? WaterAt(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (HasComp<WFFreezableWaterComponent>(ent.Value))
                return ent.Value;
        }

        return null;
    }
}
