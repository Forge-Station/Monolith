using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Destructible;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Tag;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Shuttles.Systems;

/// <summary>
/// Feeds hull telemetry to shuttle consoles that ask for it: structure condition, fuel, and, when the
/// whole-ship view is on screen, fires, pressure and dead power. Only listening consoles get a sweep,
/// whose structure survey reads the flown grids' own anchored structures, and only tiles with something
/// to report are sent. A grid's first pilot also triggers one hull-only survey.
/// </summary>
public sealed partial class ShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private TagSystem _tags = default!;

    /// <summary>
    /// How often a listening console is refreshed. Fast enough to watch a fire spread, slow enough
    /// that the sweep doesn't matter.
    /// </summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ceiling on reported tiles per ship, so a catastrophically wrecked capital can't flood the wire.
    /// </summary>
    private const int MaxReportedTiles = 4000;

    /// <summary>
    /// Structures above this condition aren't worth drawing.
    /// </summary>
    private const float DamageReportThreshold = 0.98f;

    private const float MinNominalPressure = 80f;
    private const float MaxNominalPressure = 130f;

    /// <summary>
    /// Below this a tile counts as vented for the summary readout.
    /// </summary>
    private const float VentedPressure = 20f;

    /// <summary>
    /// Consoles currently showing the view, and which overlays each is drawing.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, (ShipOverlays Overlays, bool HullOnly)>> _listeners = new();

    /// <summary>
    /// Rebuilt each sweep: the tiles being reported, keyed by grid then tile.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, ShipTileStatus>> _gridTiles = new();

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnHullSplit);
        InitializeHullConstruction();

        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<ShipStatusRequestMessage>(OnStatusRequest);
        });
    }

    private void OnStatusRequest(Entity<ShuttleConsoleComponent> ent, ref ShipStatusRequestMessage args)
    {
        if (!args.Active)
        {
            if (_listeners.TryGetValue(ent, out var listeners))
            {
                listeners.Remove(args.Actor);
                if (listeners.Count == 0)
                    _listeners.Remove(ent);
            }
            return;
        }

        _listeners.GetOrNew(ent)[args.Actor] = (args.Overlays, args.HullOnly);

        // The client only sends this on a change, so answering every time keeps switching to the tab
        // or flipping an overlay from leaving a second of blank screen.
        SweepAndSend(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;
        ForgetDeletedHulls();

        // ShuttleConsoleSystem already owns the BoundUIClosedEvent subscription for these consoles, and
        // Robust only allows one per component, so drop stale listeners by checking the UI instead.
        foreach (var console in _listeners.Keys.ToArray())
        {
            if (!Exists(console) || !_ui.IsUiOpen(console, ShuttleConsoleUiKey.Key))
            {
                _listeners.Remove(console);
                continue;
            }
            var actors = _ui.GetActors(console, ShuttleConsoleUiKey.Key).ToHashSet();
            var listeners = _listeners[console];
            foreach (var actor in listeners.Keys.Where(actor => !actors.Contains(actor)).ToArray())
                listeners.Remove(actor);
            if (listeners.Count == 0)
                _listeners.Remove(console);
        }

        if (_listeners.Count == 0)
            return;

        // One sweep covers every listening console, so having several open costs no more than one.
        var grids = new Dictionary<EntityUid, List<EntityUid>>();

        foreach (var console in _listeners.Keys)
        {
            if (GetConsoleGrid(console) is not { } grid)
                continue;

            grids.GetOrNew(grid).Add(console);
        }

        if (grids.Count == 0)
            return;

        var detailed = grids.Where(pair => pair.Value.Any(console => _listeners[console].Values.Any(request => !request.HullOnly)))
            .Select(pair => pair.Key).ToHashSet();
        GatherTiles(grids.Keys.ToHashSet(), detailed);

        foreach (var (grid, consoles) in grids)
        {
            foreach (var console in consoles)
            {
                var request = GetRequest(console);
                _ui.ServerSendUiMessage(console,
                    ShuttleConsoleUiKey.Key,
                    BuildMessage(grid, request.Overlays, request.HullOnly));
            }
        }
    }

    /// <summary>
    /// Sweeps the hull this console flies and returns what it found. Null if the console isn't on a grid.
    /// </summary>
    public ShipStatusMessage? GetStatus(EntityUid console, ShipOverlays overlays = ShipOverlays.All, bool hullOnly = false)
    {
        if (GetConsoleGrid(console) is not { } grid)
            return null;

        var grids = new HashSet<EntityUid> { grid };
        GatherTiles(grids, hullOnly ? new HashSet<EntityUid>() : grids);
        return BuildMessage(grid, overlays, hullOnly);
    }

    /// <summary>Shared helm messages retain every viewer's requested overlays and detail level.</summary>
    private (ShipOverlays Overlays, bool HullOnly) GetRequest(EntityUid console)
    {
        var overlays = ShipOverlays.None;
        var hullOnly = true;
        foreach (var request in _listeners[console].Values)
        {
            overlays |= request.Overlays;
            hullOnly &= request.HullOnly;
        }
        return (overlays, hullOnly);
    }

    private void SweepAndSend(EntityUid console)
    {
        var request = GetRequest(console);
        if (GetStatus(console, request.Overlays, request.HullOnly) is not { } message)
            return;

        _ui.ServerSendUiMessage(console, ShuttleConsoleUiKey.Key, message);
    }

    /// <summary>
    /// Resolves what the console is actually flying, which is not its own grid for drone consoles.
    /// </summary>
    private EntityUid? GetConsoleGrid(EntityUid console)
    {
        var ev = new ConsoleShuttleEvent { Console = console };
        RaiseLocalEvent(console, ref ev);

        if (ev.Console is not { } target || !TryComp(target, out TransformComponent? xform))
            return null;

        return xform.GridUid;
    }

    private void GatherTiles(HashSet<EntityUid> grids, HashSet<EntityUid> detailed, bool fuel = true)
    {
        _gridTiles.Clear();
        _hullStructures.Clear();
        _hullIntegrity.Clear();

        foreach (var grid in grids)
        {
            _gridTiles[grid] = new Dictionary<Vector2i, ShipTileStatus>();
            _hullStructures[grid] = new Dictionary<Vector2i, (float Capacity, float Remaining)>();
        }

        GatherDamage(grids, detailed);
        GatherHullIntegrity(grids);
        if (fuel)
            GatherFuel(grids);
        if (detailed.Count > 0)
        {
            GatherPower(detailed);
            GatherAtmos(detailed);
        }
    }

    /// <summary>Reads each listened grid's own anchored structures, which are direct children of the grid.</summary>
    private void GatherDamage(HashSet<EntityUid> grids, HashSet<EntityUid> detailed)
    {
        var destructibles = GetEntityQuery<DestructibleComponent>();
        var damageables = GetEntityQuery<DamageableComponent>();
        var xforms = GetEntityQuery<TransformComponent>();

        foreach (var grid in grids)
        {
            if (!TryComp(grid, out MapGridComponent? gridComp) || !xforms.TryGetComponent(grid, out var gridXform))
                continue;

            var showDamage = detailed.Contains(grid);
            var tiles = _gridTiles[grid];
            var structures = _hullStructures[grid];
            var children = gridXform.ChildEnumerator;

            while (children.MoveNext(out var uid))
            {
                if (!destructibles.TryGetComponent(uid, out var destructible)
                    || !damageables.TryGetComponent(uid, out var damageable)
                    || !xforms.TryGetComponent(uid, out var xform)
                    || !xform.Anchored)
                    continue;

                if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                    continue;

                var hull = IsHullStructure(uid);
                // Healthy furniture and machinery do not need destruction-threshold or tile lookups.
                if (!hull && (!showDamage || damageable.TotalDamage <= 0))
                    continue;

                // A surveyed structure keeps the threshold it was measured against.
                HullSurvey known = default;
                var surveyed = hull && _surveyedHull.TryGetValue(uid, out known) && known.Grid == grid;
                float capacity;
                if (surveyed)
                {
                    capacity = known.Capacity;
                }
                else
                {
                    var destroyedAt = _destructible.DestroyedAt(uid, destructible);

                    // Entities with no destruction threshold have no meaningful condition to report.
                    if (destroyedAt <= 0 || destroyedAt == FixedPoint2.MaxValue)
                        continue;

                    capacity = destroyedAt.Float();
                }

                var index = _maps.TileIndicesFor(grid, gridComp, xform.Coordinates);
                var remaining = Math.Clamp(capacity - damageable.TotalDamage.Float(), 0f, capacity);
                if (hull)
                {
                    if (!surveyed || known.Index != index)
                        ObserveHullStructure(uid, grid, index, capacity);
                    var structure = structures.GetValueOrDefault(index);
                    structures[index] = (structure.Capacity + capacity, structure.Remaining + remaining);
                }
                var integrity = remaining / capacity;

                if (!showDamage || integrity > DamageReportThreshold)
                    continue;

                var status = tiles.GetValueOrDefault(index);

                // The worst structure on a tile is the one worth showing.
                if ((status.Flags & ShipTileFlags.Damaged) == 0 || integrity < status.Integrity / 255f)
                    status.Integrity = (byte)(integrity * 255f);

                status.Index = index;
                status.Flags |= ShipTileFlags.Damaged;
                tiles[index] = status;
            }
        }
    }

    private void GatherPower(HashSet<EntityUid> grids)
    {
        var query = EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();

        while (query.MoveNext(out _, out var receiver, out var xform))
        {
            if (receiver.Powered || !xform.Anchored)
                continue;

            if (xform.GridUid is not { } grid || !grids.Contains(grid))
                continue;

            if (!TryComp(grid, out MapGridComponent? gridComp))
                continue;

            var index = _maps.TileIndicesFor(grid, gridComp, xform.Coordinates);
            var tiles = _gridTiles[grid];
            var status = tiles.GetValueOrDefault(index);

            status.Index = index;
            status.Flags |= ShipTileFlags.Unpowered;
            tiles[index] = status;
        }
    }

    private void GatherAtmos(HashSet<EntityUid> grids)
    {
        foreach (var grid in grids)
        {
            if (!TryComp(grid, out GridAtmosphereComponent? atmos))
                continue;

            var tiles = _gridTiles[grid];

            foreach (var (index, tile) in atmos.Tiles)
            {
                // Tiles outside the hull aren't damage, they're just space.
                if (tile.Space || tile.Air == null)
                    continue;

                var pressure = tile.Air.Pressure;
                var onFire = tile.Hotspot.Valid;
                var offNominal = pressure < MinNominalPressure || pressure > MaxNominalPressure;

                if (!onFire && !offNominal)
                    continue;

                var status = tiles.GetValueOrDefault(index);
                status.Index = index;

                if (onFire)
                    status.Flags |= ShipTileFlags.Fire;

                if (offNominal)
                {
                    status.Flags |= ShipTileFlags.Pressure;
                    status.Pressure = (byte)Math.Clamp(pressure / 2f, 0f, 255f);
                }

                tiles[index] = status;
            }
        }
    }

    /// <summary>
    /// Counts everything for the summary, but only sends tiles for overlays the console is drawing.
    /// </summary>
    private ShipStatusMessage BuildMessage(EntityUid grid, ShipOverlays overlays, bool hullOnly = false)
    {
        var summary = new ShipStatusSummary
        {
            WorstIntegrity = 1f,
            HullIntegrity = _hullIntegrity.GetValueOrDefault(grid, 1f),
            Fuel = _fuelReserves.GetValueOrDefault(grid),
        };
        var list = new List<ShipTileStatus>();
        var wanted = (ShipTileFlags)overlays;

        if (hullOnly || !_gridTiles.TryGetValue(grid, out var tiles))
            return new ShipStatusMessage(GetNetEntity(grid), list, summary);

        foreach (var status in tiles.Values)
        {
            if ((status.Flags & ShipTileFlags.Damaged) != 0)
            {
                summary.DamagedTiles++;
                summary.WorstIntegrity = Math.Min(summary.WorstIntegrity, status.Integrity / 255f);
            }

            if ((status.Flags & ShipTileFlags.Fire) != 0)
                summary.FireTiles++;

            if ((status.Flags & ShipTileFlags.Unpowered) != 0)
                summary.UnpoweredTiles++;

            if ((status.Flags & ShipTileFlags.Pressure) != 0 && status.Pressure * 2f < VentedPressure)
                summary.VentedTiles++;

            var drawn = status.Flags & wanted;

            if (drawn == ShipTileFlags.None)
                continue;

            if (list.Count >= MaxReportedTiles)
            {
                summary.Truncated = true;
                continue;
            }

            var trimmed = status;
            trimmed.Flags = drawn;
            list.Add(trimmed);
        }

        return new ShipStatusMessage(GetNetEntity(grid), list, summary);
    }
}
