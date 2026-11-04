using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.GroundCover;

/// <summary>
/// Slows mobs in deep snow and gives their steps a crunch on snow cover. Both sides work it out from the snow depths
/// each has, so walking stays predicted: the server from what it last sent clients, the client from what it got.
/// </summary>
public abstract class SharedWFSnowSlowSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier SnowSteps = new SoundCollectionSpecifier("FootstepSnow");
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(30);

    /// <summary>Where each mob's slowdown was last worked out, and what it came to.</summary>
    private readonly Dictionary<EntityUid, (EntityUid Grid, Vector2i Tile, int Version, float Speed)> _tracked = new();

    private readonly List<EntityUid> _stale = new();
    private EntityQuery<MapGridComponent> _gridQuery;
    private TimeSpan _nextPrune;
    private bool _forget;

    public override void Initialize()
    {
        base.Initialize();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
        SubscribeLocalEvent<WFSnowFootstepEvent>(OnFootstep);
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _tracked.Clear();
    }

    /// <summary>
    /// Forgets what each mob's slowdown was, so the next update sets it again for all of them. The client calls this
    /// when a game state resets its mobs' speeds to the server's, which can lag their tile by a tick.
    /// </summary>
    protected void ForgetTracked()
    {
        _tracked.Clear();
        _forget = true;
    }

    /// <summary>Snow depth on a tile as this side knows it for a mob, 0 where it isn't known yet.</summary>
    protected abstract int TileDepth(EntityUid mob, EntityUid grid, Vector2i tile);

    /// <summary>Whether a grid has any snow cover this side knows of.</summary>
    protected abstract bool HasSnow(EntityUid grid);

    /// <summary>Goes up whenever the depths this side reads change, so standing mobs are looked at again.</summary>
    protected abstract int SnowVersion { get; }

    /// <summary>Whether this side works out a mob's slowdown: every mob on the server, predicted ones on the client.</summary>
    protected abstract bool Tracks(EntityUid mob);

    /// <summary>The share of its walking speed a mob has in the snow where it stands.</summary>
    public float GetSpeed(EntityUid mob, TransformComponent? xform = null)
    {
        if (!Resolve(mob, ref xform, false) || !TileUnder(xform, out var grid, out var tile))
            return 1f;

        return WFSnowCover.SpeedAt(WFSnowCover.MoveDepth(tile, t => TileDepth(mob, grid, t)));
    }

    /// <summary>Whether the tile a mob stands on is snow covered, by this side's depths.</summary>
    public bool OnCover(EntityUid mob, TransformComponent? xform = null)
    {
        return Resolve(mob, ref xform, false)
               && TileUnder(xform, out var grid, out var tile)
               && TileDepth(mob, grid, tile) >= WFSnowCover.CoverFrom;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var version = SnowVersion;
        var query = EntityQueryEnumerator<InputMoverComponent, MovementSpeedModifierComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var move, out var xform))
        {
            if (!Tracks(uid))
                continue;

            var on = TileUnder(xform, out var grid, out var tile);
            var had = _tracked.TryGetValue(uid, out var last);
            if (!on)
            {
                if (_forget || had && last.Speed < 1f)
                    _speed.RefreshMovementSpeedModifiers(uid, move);

                if (had)
                    _tracked.Remove(uid);

                continue;
            }

            if (had && last.Grid == grid && last.Tile == tile && last.Version == version)
                continue;

            var speed = WFSnowCover.SpeedAt(WFSnowCover.MoveDepth(tile, t => TileDepth(uid, grid, t)));
            _tracked[uid] = (grid, tile, version, speed);
            if (_forget || (had ? last.Speed != speed : speed < 1f))
                _speed.RefreshMovementSpeedModifiers(uid, move);
        }

        _forget = false;

        var now = _timing.CurTime;
        if (now < _nextPrune)
            return;

        _nextPrune = now + PruneInterval;
        _stale.Clear();
        foreach (var uid in _tracked.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _tracked.Remove(uid);
        }
    }

    /// <summary>The grid tile a mob stands on, if that grid has snow.</summary>
    private bool TileUnder(TransformComponent xform, out EntityUid grid, out Vector2i tile)
    {
        tile = default;
        grid = default;
        if (xform.GridUid is not { } gridUid
            || xform.ParentUid != gridUid
            || !HasSnow(gridUid)
            || !_gridQuery.TryComp(gridUid, out var gridComp))
            return false;

        grid = gridUid;
        tile = _map.TileIndicesFor(gridUid, gridComp, xform.Coordinates);
        return true;
    }

    private void OnRefresh(Entity<MovementSpeedModifierComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = GetSpeed(ent);
        if (speed < 1f)
            args.ModifySpeed(speed);
    }

    private void OnFootstep(ref WFSnowFootstepEvent args)
    {
        if (args.Sound == null && HasSnow(args.Grid) && TileDepth(args.User, args.Grid, args.Tile) >= WFSnowCover.CoverFrom)
            args.Sound = SnowSteps;
    }
}
