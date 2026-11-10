using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>
/// The life of every growth: bound to a Monolith, standing on corruption, swelling up, and dying or going dormant when either
/// goes. What each kind does is in its own system.
/// </summary>
public sealed partial class WFGrowthSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    private static readonly TimeSpan CheckRate = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private const float StarveDamage = 3f;
    private const float DecaySeconds = 45f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFGrowthComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFGrowthComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnMapInit(Entity<WFGrowthComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0f, 1f));
        SetState(ent, WFGrowthState.Idle);

        // One placed by hand answers to the nearest Monolith.
        if (ent.Comp.Source == null && _director.NearestMonolith(ent) is { } monolith)
            Bind(ent, monolith);
    }

    private void OnTerminating(Entity<WFGrowthComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<WFRemadeDirectorComponent>(ent.Comp.Source, out var director))
            director.Growths.Remove(ent);
    }

    /// <summary>Ties a growth to a Monolith: its Director counts it and a node feeds that Monolith's corruption.</summary>
    public void Bind(Entity<WFGrowthComponent> ent, EntityUid monolith)
    {
        ent.Comp.Source = monolith;
        if (TryComp<WFRemadeDirectorComponent>(monolith, out var director))
            director.Growths.Add(ent);

        if (TryComp<WFCorruptionNodeComponent>(ent, out var node))
            _corruption.AttachNode((ent, node), monolith);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFGrowthComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var growth, out var xform))
        {
            if (now < growth.NextCheck)
                continue;

            growth.NextCheck = now + CheckRate;
            Check((uid, growth), xform, now);
        }
    }

    private void Check(Entity<WFGrowthComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var growth = ent.Comp;
        if (growth.DecayAt is { } decay)
        {
            if (now >= decay)
                QueueDel(ent);

            return;
        }

        if (growth.Source is not { } source || TerminatingOrDeleted(source) || !HasComp<WFMonolithComponent>(source))
        {
            growth.Dormant = true;
            growth.DecayAt = now + TimeSpan.FromSeconds(_random.NextFloat(0f, DecaySeconds));
            SetState(ent, WFGrowthState.Idle);
            return;
        }

        if (growth.ReadyAt is { } ready && now >= ready)
        {
            growth.ReadyAt = null;
            SetState(ent, WFGrowthState.Idle);
        }

        var def = _proto.Index(growth.Kind);
        if (!_corruption.IsOnCorruption(ent, xform) || def.RequireWall && !HasWallBeside(xform))
        {
            growth.Dormant = true;
            SetState(ent, WFGrowthState.Idle);
            _damageable.TryChangeDamage(ent, new DamageSpecifier(_proto.Index(Blunt), StarveDamage), ignoreResistances: true);
            return;
        }

        growth.Dormant = false;
    }

    public bool IsActive(EntityUid uid)
    {
        return TryComp<WFGrowthComponent>(uid, out var growth) && growth.Active;
    }

    public void SetState(Entity<WFGrowthComponent> ent, WFGrowthState state)
    {
        if (ent.Comp.ReadyAt != null)
            state = WFGrowthState.Growing;

        if (ent.Comp.State == state)
            return;

        ent.Comp.State = state;
        _appearance.SetData(ent, WFGrowthVisuals.State, state);
    }

    private bool TryGetTile(EntityCoordinates at, [NotNullWhen(true)] out EntityUid? gridUid, out MapGridComponent grid, out Vector2i tile)
    {
        grid = default!;
        tile = default;
        gridUid = _transform.GetGrid(at);
        if (gridUid == null || !TryComp(gridUid, out MapGridComponent? found))
            return false;

        grid = found;
        tile = _map.TileIndicesFor(gridUid.Value, grid, at);
        return true;
    }

    /// <summary>True if a wall (or anything solid) stands on a tile next to this one.</summary>
    public bool HasWallBeside(TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        return HasWallBeside(gridUid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
    }

    private bool HasWallBeside(EntityUid gridUid, Vector2i tile)
    {
        foreach (var direction in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
        {
            if (_turf.IsTileBlocked(gridUid, tile + direction, CollisionGroup.Impassable))
                return true;
        }

        return false;
    }

    /// <summary>The direction an open tile next to this one lies in, away from the walls, or null if there is none.</summary>
    public Vector2i? OpenSideOf(EntityUid gridUid, Vector2i tile)
    {
        foreach (var direction in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
        {
            if (_turf.IsTileBlocked(gridUid, tile - direction, CollisionGroup.Impassable)
                && !_turf.IsTileBlocked(gridUid, tile + direction, CollisionGroup.Impassable))
            {
                return direction;
            }
        }

        return null;
    }

    /// <summary>How boxed in a place is: 3 beside a door, 2 in a corridor, 1 against a wall, 0 in the open.</summary>
    public int Confinement(EntityCoordinates at)
    {
        if (!TryGetTile(at, out var gridUid, out var grid, out var tile))
            return 0;

        var walls = new List<Vector2i>();
        foreach (var direction in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
        {
            var next = _map.GetAnchoredEntitiesEnumerator(gridUid.Value, grid, tile + direction);
            while (next.MoveNext(out var uid))
            {
                if (HasComp<DoorComponent>(uid))
                    return 3;
            }

            if (_turf.IsTileBlocked(gridUid.Value, tile + direction, CollisionGroup.Impassable))
                walls.Add(direction);
        }

        if (walls.Count == 2 && walls[0] + walls[1] == Vector2i.Zero)
            return 2;

        return walls.Count > 0 ? 1 : 0;
    }

    /// <summary>True if a growth of this kind may go here: on corruption, free of other growths and solid things, spaced and walled as it needs.</summary>
    public bool CanPlace(WFRemadeGrowthPrototype def, EntityCoordinates at)
    {
        if (!TryGetTile(at, out var gridUid, out var grid, out var tile)
            || _corruption.IsSanctuary(gridUid)
            || !_corruption.IsCorrupted((gridUid.Value, grid), tile)
            || _turf.IsTileBlocked(gridUid.Value, tile, CollisionGroup.MobMask))
        {
            return false;
        }

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid.Value, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<WFGrowthComponent>(uid))
                return false;
        }

        if (def.RequireWall && !HasWallBeside(gridUid.Value, tile))
            return false;

        if (def.SameDistance > 0f)
        {
            var near = new HashSet<Entity<WFGrowthComponent>>();
            _lookup.GetEntitiesInRange(at, def.SameDistance, near);
            foreach (var other in near)
            {
                if (other.Comp.Kind == def.ID)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Raises a growth for a Monolith's Director. Fails where it can't stand, or when the Director can't pay; a growth not ready
    /// swells up over time before it works.
    /// </summary>
    public EntityUid? TryBuild(WFRemadeGrowthPrototype def, EntityCoordinates at, EntityUid monolith, bool ready, bool pay = true)
    {
        if (!TryGetTile(at, out var gridUid, out var grid, out var tile) || !CanPlace(def, at))
            return null;

        var director = CompOrNull<WFRemadeDirectorComponent>(monolith);
        if (pay)
        {
            if (director == null || director.Biomass < def.Cost)
                return null;

            director.Biomass -= def.Cost;
        }

        var uid = Spawn(def.Entity, _map.GridTileToLocal(gridUid.Value, grid, tile));
        var growth = EnsureComp<WFGrowthComponent>(uid);
        growth.Kind = def.ID;
        if (!ready)
            growth.ReadyAt = _timing.CurTime + TimeSpan.FromSeconds(def.GrowSeconds);

        SetState((uid, growth), ready ? WFGrowthState.Idle : WFGrowthState.Growing);
        Bind((uid, growth), monolith);

        // Faces away from the wall it hangs on.
        if (def.RequireWall && OpenSideOf(gridUid.Value, tile) is { } side)
            _transform.SetLocalRotation(uid, ((Vector2) side).ToWorldAngle());

        return uid;
    }

    /// <summary>The growth kinds a Director may raise at a stage.</summary>
    public IEnumerable<WFRemadeGrowthPrototype> KindsAt(WFMonolithStage stage)
    {
        foreach (var def in _proto.EnumeratePrototypes<WFRemadeGrowthPrototype>())
        {
            if (def.Weight > 0f && stage >= def.MinStage)
                yield return def;
        }
    }

    /// <summary>How many of a kind a Monolith may keep at a stage.</summary>
    public static int MaxAlive(WFRemadeGrowthPrototype def, WFMonolithStage stage)
    {
        return def.MaxAlive + def.AlivePerStage * Math.Max(0, (int) stage - (int) def.MinStage);
    }
}
