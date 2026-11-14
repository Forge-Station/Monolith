using Content.Server._CE.ZLevels.Core.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Ghost;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.GridSettle;

/// <summary>
/// Brings idle grids to rest. The engine sends every grid to every client, so a drifting wreck costs every player
/// bandwidth each tick until it sleeps.
/// </summary>
public sealed partial class WFGridSettleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>Multiplier on an idle grid's space friction: 0.05 becomes 0.5, so it stops within about ten seconds.</summary>
    public const float FrictionMultiplier = 10f;

    /// <summary>Speed in m/s under which an idle grid is put to sleep outright.</summary>
    public const float SleepSpeed = 0.1f;

    /// <summary>Spin in rad/s under which an idle grid is put to sleep outright.</summary>
    public const float SleepSpin = 0.02f;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextScan;
    private readonly HashSet<EntityUid> _activeGrids = new();
    private readonly HashSet<EntityUid> _group = new();
    private readonly Stack<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFGridSettleComponent, TileFrictionEvent>(OnFriction);
    }

    private void OnFriction(Entity<WFGridSettleComponent> ent, ref TileFrictionEvent args)
    {
        args.Modifier *= FrictionMultiplier;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + ScanInterval;
        CollectActiveGrids();

        var query = EntityQueryEnumerator<ShuttleComponent, MapGridComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var body))
        {
            if (body.BodyType != BodyType.Dynamic || HasComp<FTLComponent>(uid) || HasComp<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>(uid) || HasComp<CEZGridFallerComponent>(uid) ||
                !IsIdle(uid))
            {
                RemComp<WFGridSettleComponent>(uid);
                continue;
            }

            EnsureComp<WFGridSettleComponent>(uid);
            if (body.Awake && body.LinearVelocity.Length() < SleepSpeed && MathF.Abs(body.AngularVelocity) < SleepSpin)
                _physics.SetAwake((uid, body), false);
        }
    }

    /// <summary>Grids with a player aboard or a piloted console.</summary>
    private void CollectActiveGrids()
    {
        _activeGrids.Clear();

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && !HasComp<GhostComponent>(uid))
                _activeGrids.Add(grid);
        }

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var console, out var xform))
        {
            if (console.SubscribedPilots.Count > 0 && xform.GridUid is { } grid)
                _activeGrids.Add(grid);
        }
    }

    /// <summary>True when no grid jointed to this one, docks included, is crewed, piloted or thrusting.</summary>
    private bool IsIdle(EntityUid grid)
    {
        _group.Clear();
        _pending.Clear();
        _pending.Push(grid);
        _group.Add(grid);

        while (_pending.TryPop(out var current))
        {
            if (_activeGrids.Contains(current) || IsThrusting(current))
                return false;

            if (!TryComp<JointComponent>(current, out var joints))
                continue;

            foreach (var joint in joints.GetJoints.Values)
            {
                var other = joint.BodyAUid == current ? joint.BodyBUid : joint.BodyAUid;
                if (Transform(other).GridUid is { } otherGrid && _group.Add(otherGrid))
                    _pending.Push(otherGrid);
            }
        }

        return true;
    }

    private bool IsThrusting(EntityUid grid)
    {
        if (!TryComp<ShuttleComponent>(grid, out var shuttle))
            return false;

        if (shuttle.ThrustDirections != DirectionFlag.None)
            return true;

        foreach (var thruster in shuttle.AngularThrusters)
        {
            if (TryComp<ThrusterComponent>(thruster, out var comp) && comp.Firing)
                return true;
        }

        return false;
    }
}
