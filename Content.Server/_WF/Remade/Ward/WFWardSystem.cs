using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Ward;
using Content.Shared._WF.ShipShields;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Ward;

/// <summary>
/// The Ward gift. The stone is a ship shield generator that costs no power: it feeds the stock <c>ShipShieldEmitter</c>, so the
/// field, what it blocks and how it breaks are the ship shield's own. The Ward dial and the stone's resonance set the
/// emitter's capacity and mending; a rooted stone's field can't be dented. Rooted, the field also holds other ships out: any
/// ship that isn't the stone's own or docked to it is pushed back to the edge, so nobody can fly in and fire from inside.
/// People on foot, in jetpacks or in suits aren't ships and cross as they like. <see cref="HoldOut(EntityUid, Vector2, Vector2, HashSet{EntityUid}, bool)"/>
/// is also how the Lattice's anchor shields hold out ships and people alike.
/// </summary>
public sealed partial class WFWardSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WFMonolithSystem _monolith = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(0.5);
    private static readonly Color FieldColor = Color.FromHex("#EC3232");

    /// <summary>Speed a turned-back ship is given away from the wall, as a share of the speed it hit it at.</summary>
    private const float Bounce = 0.25f;

    private readonly HashSet<EntityUid> _docked = new();
    private readonly HashSet<Entity<MobStateComponent>> _people = new();
    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFWardComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFWardComponent> ent, ref MapInitEvent args)
    {
        var receiver = EnsureComp<ApcPowerReceiverComponent>(ent);
        receiver.NeedsPower = false;
        receiver.Load = 0f;
        receiver.PowerDisabled = true;

        var emitter = EnsureComp<ShipShieldEmitterComponent>(ent);
        emitter.ShieldColor = FieldColor;
        emitter.BaseDraw = 0f;
        emitter.MaxDraw = 1f;
        emitter.PowerModifier = 0f;
        emitter.UnpoweredBonus = 1f;
        emitter.DamageOverloadTimePunishment = WFMonolithMath.WardOverloadSeconds;
        emitter.DamageLimit = 1f;
        emitter.HealPerSecond = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFWardComponent, WFMonolithComponent, ShipShieldEmitterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var ward, out var monolith, out var emitter, out var receiver))
        {
            if (now >= ward.Next)
            {
                ward.Next = now + TickRate;
                Tick((uid, monolith), ward, emitter, receiver);
            }

            if (ward.Walled)
                HoldOut(uid, ward);
        }
    }

    /// <summary>Sets the emitter to what the dial and the stage give, and works out whether the wall is up.</summary>
    private void Tick(Entity<WFMonolithComponent> scion, WFWardComponent ward, ShipShieldEmitterComponent emitter, ApcPowerReceiverComponent receiver)
    {
        var monolith = scion.Comp;
        var barred = _monolith.IsBarred(scion);
        var dial = _monolith.GetDial(scion, WFGift.Ward);
        var capacity = barred ? 0f : WFMonolithMath.WardStrength(monolith.Stage, dial, monolith.Resonance);

        if (capacity <= 0f)
        {
            // With the dial off the emitter has no power, which drops the field.
            receiver.PowerDisabled = true;
            ward.Capacity = 0f;
            ward.Walled = false;
            return;
        }

        var rooted = monolith.Stage == WFMonolithStage.Crescendo;
        if (ward.Capacity <= 0f)
        {
            // Switched on fresh, not still sick from the last time.
            emitter.Damage = 0f;
            emitter.Recharging = false;
            emitter.OverloadAccumulator = 0f;
        }
        else if (!rooted && Math.Abs(ward.Capacity - capacity) > 0.5f)
        {
            emitter.Damage *= capacity / ward.Capacity;
        }

        ward.Capacity = capacity;
        receiver.PowerDisabled = false;
        if (emitter.Shield is { } field)
            MarkField(field, monolith.Stage, rooted);

        emitter.DamageLimit = capacity;
        emitter.HealPerSecond = WFMonolithMath.WardMendRate(monolith.Stage, dial, monolith.Resonance);

        // A rooted stone can't be broken, so nothing the ships throw leaves a mark.
        if (rooted)
        {
            emitter.Damage = 0f;
            emitter.Recharging = false;
            emitter.OverloadAccumulator = 0f;
        }

        ward.Walled = rooted && emitter.Shield is { } shield && SetOutline(ward, shield) && Transform(scion).GridUid is { } grid
            && SetFriendly(ward, grid);
    }

    /// <summary>Tells clients this shield is the stone's field and how far gone the stone is, so they draw it their own way.</summary>
    private void MarkField(EntityUid shield, WFMonolithStage stage, bool rooted)
    {
        var field = EnsureComp<WFWardFieldComponent>(shield);
        if (field.Stage == stage && field.Rooted == rooted)
            return;

        field.Stage = stage;
        field.Rooted = rooted;
        Dirty(shield, field);
    }

    /// <summary>Takes the wall's shape from the field's outline, which is an oval round the ship.</summary>
    private bool SetOutline(WFWardComponent ward, EntityUid shield)
    {
        if (!TryComp<WFShipShieldVisualsComponent>(shield, out var visuals) || visuals.Contours.Length == 0)
            return false;

        if (ReferenceEquals(ward.Contours, visuals.Contours))
            return true;

        if (!TryGetOval(shield, out ward.Centre, out ward.Radii))
            return false;

        ward.Contours = visuals.Contours;
        return true;
    }

    /// <summary>A shield field's outline as an oval in its ship's frame: a centre and two radii.</summary>
    public bool TryGetOval(EntityUid shield, out Vector2 centre, out Vector2 radii)
    {
        centre = radii = Vector2.Zero;
        if (!TryComp<WFShipShieldVisualsComponent>(shield, out var visuals) || visuals.Contours.Length == 0)
            return false;

        var points = visuals.Contours.SelectMany(contour => contour).ToArray();
        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        centre = (min + max) / 2f;
        radii = (max - min) / 2f;
        return radii.X > 0f && radii.Y > 0f;
    }

    private bool SetFriendly(WFWardComponent ward, EntityUid grid)
    {
        _docked.Clear();
        _shuttle.GetAllDockedShuttles(grid, _docked);
        _docked.Add(grid);
        if (!_docked.SetEquals(ward.Friendly))
            ward.Friendly = new HashSet<EntityUid>(_docked);

        return true;
    }

    /// <summary>
    /// Pushes every ship that isn't friendly out of the oval. A ship is tested at its corners and edge midpoints; the
    /// deepest one is moved back to the edge, and the speed it was carrying into the wall is taken off it.
    /// </summary>
    private void HoldOut(EntityUid scion, WFWardComponent ward)
    {
        if (Transform(scion).GridUid is not { } home || TerminatingOrDeleted(home))
            return;

        ward.Turned += HoldOut(home, ward.Centre, ward.Radii, ward.Friendly, false);
    }

    /// <summary>
    /// Pushes every ship not in <paramref name="friendly"/> out of an oval in <paramref name="home"/>'s frame, and with
    /// <paramref name="people"/> everyone loose in space too. Returns how many were turned back.
    /// </summary>
    public int HoldOut(EntityUid home, Vector2 centre, Vector2 radii, HashSet<EntityUid> friendly, bool people)
    {
        var turned = 0;
        var homeXform = Transform(home);
        if (homeXform.MapUid == null)
            return 0;

        var (_, _, toWorld, toLocal) = _transform.GetWorldPositionRotationMatrixWithInv(homeXform);
        var world = toWorld.TransformBox(Box2.CenteredAround(centre, radii * 2f));
        var homeVelocity = CompOrNull<PhysicsComponent>(home)?.LinearVelocity ?? Vector2.Zero;

        // Wide enough that a big ship straddling the edge is found by its box.
        _grids.Clear();
        _maps.FindGridsIntersecting(homeXform.MapID, world.Enlarged(60f), ref _grids);
        foreach (var other in _grids)
        {
            if (other.Owner == home || friendly.Contains(other.Owner) || !TryComp<PhysicsComponent>(other, out var body) || body.BodyType != BodyType.Dynamic)
                continue;

            var otherXform = Transform(other);
            var otherToLocal = _transform.GetWorldMatrix(otherXform) * toLocal;
            var local = other.Comp.LocalAABB;
            var worst = 1f;
            var deepest = Vector2.Zero;
            foreach (var sample in Samples(local))
            {
                var inHome = Vector2.Transform(sample, otherToLocal);
                var depth = Depth(inHome, centre, radii);
                if (depth < worst)
                {
                    worst = depth;
                    deepest = inHome;
                }
            }

            if (worst >= 1f)
                continue;

            PushOut(other, otherXform, body, deepest, worst, centre, radii, toWorld, homeVelocity);
            turned++;
        }

        if (!people)
            return turned;

        // Anyone loose in space inside the oval, on foot, in a suit or a jetpack. People aboard a ship go with the ship,
        // so only the map's own broadphase is asked: a map-wide lookup walked every entity of every grid in the oval,
        // the whole station included, at 25 ms/s on a busy round.
        _people.Clear();
        _lookup.GetLocalEntitiesIntersecting(homeXform.MapUid.Value, world, _people, LookupFlags.Dynamic | LookupFlags.Approximate);
        foreach (var person in _people)
        {
            var xform = Transform(person);
            // Only what sits loose on the map: a rider goes with their vehicle, and moving an organ would pull
            // it out of the body it is in.
            if (xform.GridUid != null || xform.ParentUid != homeXform.MapUid || !TryComp<PhysicsComponent>(person, out var body))
                continue;

            var inHome = Vector2.Transform(_transform.GetWorldPosition(xform), toLocal);
            var depth = Depth(inHome, centre, radii);
            if (depth >= 1f)
                continue;

            PushOut(person, xform, body, inHome, depth, centre, radii, toWorld, homeVelocity);
            turned++;
        }

        return turned;
    }

    private static float Depth(Vector2 point, Vector2 centre, Vector2 radii)
    {
        var offset = point - centre;
        return MathF.Sqrt(offset.X * offset.X / (radii.X * radii.X) + offset.Y * offset.Y / (radii.Y * radii.Y));
    }

    /// <summary>Moves something out along the oval's radius to its edge, and takes off the speed it carried into it.</summary>
    private void PushOut(EntityUid uid, TransformComponent xform, PhysicsComponent body, Vector2 deepest, float depth,
        Vector2 centre, Vector2 radii, Matrix3x2 toWorld, Vector2 homeVelocity)
    {
        var from = deepest - centre;
        var edge = centre + from / Math.Max(depth, 0.001f);
        if (depth <= 0.001f)
            edge = centre + new Vector2(radii.X, 0f);

        var normal = Vector2.Normalize(new Vector2(from.X / (radii.X * radii.X), from.Y / (radii.Y * radii.Y)));
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y))
            normal = Vector2.UnitX;

        var shift = Vector2.TransformNormal(edge - deepest, toWorld);
        _transform.SetWorldPosition(uid, _transform.GetWorldPosition(xform) + shift);

        var outward = Vector2.Normalize(Vector2.TransformNormal(normal, toWorld));
        var relative = body.LinearVelocity - homeVelocity;
        var into = Vector2.Dot(relative, outward);
        if (into < 0f)
            _physics.SetLinearVelocity(uid, relative - (1f + Bounce) * into * outward + homeVelocity, body: body);
    }

    /// <summary>The corners and the edge midpoints of a ship's box, in its own frame.</summary>
    private static IEnumerable<Vector2> Samples(Box2 box)
    {
        var halfX = (box.Left + box.Right) / 2f;
        var halfY = (box.Bottom + box.Top) / 2f;
        yield return box.BottomLeft;
        yield return new Vector2(halfX, box.Bottom);
        yield return box.BottomRight;
        yield return new Vector2(box.Right, halfY);
        yield return box.TopRight;
        yield return new Vector2(halfX, box.Top);
        yield return box.TopLeft;
        yield return new Vector2(box.Left, halfY);
    }

    /// <summary>True while a stone's wall is up and turning ships back. Tests read it.</summary>
    public bool IsWalled(EntityUid scion) => TryComp<WFWardComponent>(scion, out var ward) && ward.Walled;
}
