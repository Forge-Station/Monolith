using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server._WF.Tether;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Atmos;
using Content.Shared.Destructible;
using Content.Shared.EntityTable;
using Content.Shared.Ghost;
using Content.Shared.Gravity;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared._WF.Planets;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.SandMaw;

/// <summary>
/// The sand maw: lashes a tentacle round whoever comes near and reels them in, sucks in loose things, swallows what
/// reaches its throat into its belly, and lets the swallowed tear their way out.
/// </summary>
public sealed class WFSandMawSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityTableSystem _tables = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapSystem _maps = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private RopeSystem _ropes = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float Interval = 0.1f;

    /// <summary>Metres from a tear at which someone climbs out through it.</summary>
    private const float ExitReach = 0.5f;

    private float _accumulator;
    private readonly HashSet<EntityUid> _near = new();
    private readonly HashSet<EntityUid> _bellyNear = new();
    private readonly List<EntityUid> _release = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSandMawComponent, ComponentShutdown>(OnMawShutdown);
        SubscribeLocalEvent<WFSandMawGripComponent, DestructionEventArgs>(OnGripDestroyed);
        SubscribeLocalEvent<WFSandMawBellyWallComponent, DestructionEventArgs>(OnWallDestroyed);
        SubscribeLocalEvent<WFSandMawGrabbedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<WFSandMawGrabbedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Speed);
    }

    private void OnMawShutdown(Entity<WFSandMawComponent> ent, ref ComponentShutdown args)
    {
        foreach (var victim in ent.Comp.Grabs.Keys.ToList())
            Release(ent, victim);
        if (ent.Comp.PocketBelly && ent.Comp.Belly is { } belly && TryComp(belly, out TransformComponent? bellyXform) && bellyXform.MapUid is { } map)
            QueueDel(map);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Reeling runs every tick so the drag is smooth; the rest can wait.
        var reels = EntityQueryEnumerator<WFSandMawComponent, TransformComponent>();
        while (reels.MoveNext(out var uid, out var maw, out var xform))
        {
            if (maw.Grabs.Count > 0 && xform.MapUid is { } reelMap && !Paused(reelMap))
                Reel((uid, maw), _transform.GetWorldPosition(xform), frameTime);
        }

        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;
        var dt = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<WFSandMawComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var maw, out var xform))
        {
            if (xform.MapUid is not { } map || Paused(map))
                continue;
            var centre = _transform.GetWorldPosition(xform);
            if (_timing.CurTime >= maw.NextGrab && maw.Grabs.Count == 0)
                TryLash((uid, maw), xform, centre);
            Suck((uid, maw), xform, centre, dt);
        }

        var exits = EntityQueryEnumerator<WFSandMawExitComponent, TransformComponent>();
        while (exits.MoveNext(out var uid, out var exit, out var xform))
        {
            _near.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, ExitReach, _near, LookupFlags.Dynamic);
            foreach (var climber in _near)
            {
                if (HasComp<MobStateComponent>(climber))
                    ClimbOut(exit.Maw, climber);
            }
        }
    }

    /// <summary>The nearest creature in reach gets a tentacle round it.</summary>
    private void TryLash(Entity<WFSandMawComponent> maw, TransformComponent xform, Vector2 centre)
    {
        _near.Clear();
        _lookup.GetEntitiesInRange(xform.MapID, centre, maw.Comp.GrabRange, _near, LookupFlags.Dynamic);
        EntityUid? prey = null;
        var best = float.MaxValue;
        foreach (var other in _near)
        {
            if (!HasComp<MobStateComponent>(other) || !Edible(other))
                continue;
            if (TryComp(other, out RopeAttachPointComponent? point) && point.Ropes.Count > 0)
                continue;
            var distance = Vector2.Distance(centre, _transform.GetWorldPosition(other));
            if (distance > maw.Comp.SwallowRadius && distance < best)
            {
                best = distance;
                prey = other;
            }
        }
        if (prey is not { } victim)
            return;

        maw.Comp.NextGrab = _timing.CurTime + maw.Comp.GrabCooldown;
        EnsureComp<RopeAttachPointComponent>(maw);
        var added = !HasComp<RopeAttachPointComponent>(victim);
        EnsureComp<RopeAttachPointComponent>(victim);
        if (!_ropes.TryCreateRope(maw, victim, maw.Comp.Tentacle, best, out var rope) || rope is not { } made)
        {
            if (added)
                RemComp<RopeAttachPointComponent>(victim);
            return;
        }

        var grip = Spawn(maw.Comp.Grip, Transform(victim).Coordinates);
        _transform.SetParent(grip, victim);
        _transform.SetLocalPosition(grip, Vector2.Zero);
        var gripComp = EnsureComp<WFSandMawGripComponent>(grip);
        gripComp.Maw = maw;
        gripComp.Victim = victim;

        maw.Comp.Grabs[victim] = new WFSandMawGrab { Rope = made, Grip = grip, AddedPoint = added, Since = _timing.CurTime, Length = best };
        EnsureComp<WFSandMawGrabbedComponent>(victim).Speed = maw.Comp.GrippedSpeed;
        _speed.RefreshMovementSpeedModifiers(victim);
        _audio.PlayPvs(maw.Comp.LashSound, maw);
        _popup.PopupEntity(Loc.GetString("wf-sand-maw-grabbed"), victim, victim, PopupType.LargeCaution);
    }

    /// <summary>
    /// Shortens each tentacle, faster the longer it holds, and keeps its catch within that length; a catch at the throat
    /// is swallowed. Mobs move kinematically, so the rope can't pull them: it is the tentacle you see, and the leash is
    /// done here, stopping short of anything solid in the way. The catch slows as it tires.
    /// </summary>
    private void Reel(Entity<WFSandMawComponent> maw, Vector2 centre, float dt)
    {
        _release.Clear();
        foreach (var (victim, grab) in maw.Comp.Grabs)
        {
            if (TerminatingOrDeleted(victim) || TerminatingOrDeleted(grab.Rope) || TerminatingOrDeleted(grab.Grip)
                || Transform(victim).MapID != Transform(maw).MapID)
            {
                _release.Add(victim);
                continue;
            }

            var distance = Vector2.Distance(centre, _transform.GetWorldPosition(victim));
            // Only at the throat itself, not anywhere on the funnel's rim.
            if (distance <= maw.Comp.SwallowRadius)
            {
                _release.Add(victim);
                continue;
            }

            var held = (float) (_timing.CurTime - grab.Since).TotalSeconds;
            var reel = MathF.Min(maw.Comp.MaxReelSpeed, maw.Comp.ReelSpeed * (1f + 0.25f * held));
            grab.Length = MathF.Max(maw.Comp.SwallowRadius * 0.5f, MathF.Min(grab.Length, distance) - reel * dt);
            if (distance > grab.Length)
            {
                var position = _transform.GetWorldPosition(victim);
                var next = centre + (position - centre) / distance * grab.Length;
                if (!Blocked(victim, position, next))
                    _transform.SetWorldPosition(victim, next);
            }
            _ropes.SetLength(grab.Rope, grab.Length);

            if (TryComp(victim, out WFSandMawGrabbedComponent? grabbed))
            {
                var speed = MathF.Max(maw.Comp.MinGrippedSpeed, maw.Comp.GrippedSpeed - 0.12f * held);
                if (MathF.Abs(grabbed.Speed - speed) > 0.02f)
                {
                    grabbed.Speed = speed;
                    _speed.RefreshMovementSpeedModifiers(victim);
                }
            }
        }

        foreach (var victim in _release)
        {
            var swallow = !TerminatingOrDeleted(victim) && maw.Comp.Grabs[victim] is var grab && !TerminatingOrDeleted(grab.Rope)
                && !TerminatingOrDeleted(grab.Grip);
            Release(maw, victim);
            if (swallow)
                Swallow(maw, victim);
        }
    }

    /// <summary>Whether something solid stands where the drag would put the victim.</summary>
    private bool Blocked(EntityUid victim, Vector2 from, Vector2 to)
    {
        var mapId = Transform(victim).MapID;
        var length = (to - from).Length();
        if (length < 0.0001f)
            return false;
        var ray = new CollisionRay(from, (to - from) / length, (int) CollisionGroup.Impassable);
        foreach (var hit in _physics.IntersectRay(mapId, ray, length + 0.3f, victim, returnOnFirstHit: false))
        {
            if (!HasComp<WFSandMawComponent>(hit.HitEntity) && !HasComp<MapGridComponent>(hit.HitEntity))
                return true;
        }
        return false;
    }

    /// <summary>Lets go: the rope and the coil go, and the victim's rope point if the maw gave it one.</summary>
    private void Release(Entity<WFSandMawComponent> maw, EntityUid victim)
    {
        if (!maw.Comp.Grabs.Remove(victim, out var grab))
            return;
        if (!TerminatingOrDeleted(grab.Rope))
            _ropes.BreakRope(grab.Rope);
        if (!TerminatingOrDeleted(grab.Grip))
            QueueDel(grab.Grip);
        if (grab.AddedPoint && !TerminatingOrDeleted(victim))
            RemComp<RopeAttachPointComponent>(victim);
        if (!TerminatingOrDeleted(victim) && RemComp<WFSandMawGrabbedComponent>(victim))
            _speed.RefreshMovementSpeedModifiers(victim);
    }

    private void OnGripDestroyed(Entity<WFSandMawGripComponent> ent, ref DestructionEventArgs args)
    {
        if (!TryComp(ent.Comp.Maw, out WFSandMawComponent? maw))
            return;
        Release((ent.Comp.Maw, maw), ent.Comp.Victim);
        _popup.PopupEntity(Loc.GetString("wf-sand-maw-severed"), ent.Comp.Victim, PopupType.Medium);
    }

    /// <summary>Loose things slide in; anything at the throat goes down.</summary>
    private void Suck(Entity<WFSandMawComponent> maw, TransformComponent xform, Vector2 centre, float dt)
    {
        _near.Clear();
        _lookup.GetEntitiesInRange(xform.MapID, centre, MathF.Max(maw.Comp.SuctionRadius, maw.Comp.SwallowRadius), _near,
            LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var other in _near)
        {
            if (!Edible(other) || maw.Comp.Grabs.ContainsKey(other) || !TryComp(other, out PhysicsComponent? body))
                continue;

            var offset = centre - _transform.GetWorldPosition(other);
            var distance = offset.Length();
            if (distance <= maw.Comp.SwallowRadius)
            {
                Swallow(maw, other);
                continue;
            }

            // Only loose things are drawn in; the living have to be caught.
            if (!HasComp<ItemComponent>(other) || distance > maw.Comp.SuctionRadius)
                continue;
            var falloff = 0.3f + 0.7f * (1f - distance / maw.Comp.SuctionRadius);
            _physics.ApplyLinearImpulse(other, offset / distance * maw.Comp.Pull * falloff * body.Mass * dt, body: body);
        }
    }

    /// <summary>The living and the loose; not ghosts, vehicles, anything bolted down or carried.</summary>
    private bool Edible(EntityUid uid)
    {
        if (!HasComp<MobStateComponent>(uid) && !HasComp<ItemComponent>(uid))
            return false;
        if (HasComp<GhostComponent>(uid) || HasComp<RMCVehicleComponent>(uid) || Transform(uid).Anchored)
            return false;
        return !_containers.IsEntityInContainer(uid);
    }

    private void Swallow(Entity<WFSandMawComponent> maw, EntityUid prey)
    {
        var belly = EnsureBelly(maw);
        var spot = (Vector2) maw.Comp.BellyCentre + new Vector2(0.5f) + new Vector2(_random.NextFloat(-1.5f, 1.5f), _random.NextFloat(-1.5f, 1.5f));
        _transform.SetCoordinates(prey, new EntityCoordinates(belly, spot));
        _transform.AttachToGridOrMap(prey);
        if (TryComp(prey, out PhysicsComponent? body))
            _physics.SetLinearVelocity(prey, Vector2.Zero, body: body);

        _audio.PlayPvs(maw.Comp.SwallowSound, maw.Owner);
        if (HasComp<MobStateComponent>(prey))
        {
            _popup.PopupEntity(Loc.GetString("wf-sand-maw-swallowed"), prey, prey, PopupType.LargeCaution);
            EnsureEscapeTool(maw, belly, spot);
        }
    }

    /// <summary>Whoever is swallowed finds a way out lying in the belly, even if an earlier meal carried it off.</summary>
    private void EnsureEscapeTool(Entity<WFSandMawComponent> maw, EntityUid belly, Vector2 spot)
    {
        var centre = (Vector2) maw.Comp.BellyCentre + new Vector2(0.5f);
        // Its own set: a swallow can come from inside a loop over _near.
        _bellyNear.Clear();
        _lookup.GetEntitiesInRange(new EntityCoordinates(belly, centre), maw.Comp.BellyRadius + 1f, _bellyNear, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var other in _bellyNear)
        {
            if (MetaData(other).EntityPrototype?.ID == maw.Comp.EscapeTool.Id)
                return;
        }

        Spawn(maw.Comp.EscapeTool, new EntityCoordinates(belly, spot + new Vector2(0.5f, 0f)));
    }

    /// <summary>
    /// The belly, made the first time the maw eats: cut out of the layer right under it, or a pocket map of its own on
    /// a world with nothing below.
    /// </summary>
    private EntityUid EnsureBelly(Entity<WFSandMawComponent> maw)
    {
        if (maw.Comp.Belly is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        if (TryGetLayerBelow(maw, out var below))
        {
            var centre = _transform.GetWorldPosition(maw).Floored();
            CarveBelly(maw, below, centre, true);
            maw.Comp.Belly = below;
            maw.Comp.BellyCentre = centre;
            maw.Comp.PocketBelly = false;
            return below;
        }

        var map = _maps.CreateMap(out var mapId);
        _meta.SetEntityName(map, Loc.GetString("wf-sand-maw-belly"));
        var gravity = EnsureComp<GravityComponent>(map);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(map, gravity);
        var air = new GasMixture(Atmospherics.CellVolume) { Temperature = 310f };
        air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        _atmos.SetMapAtmosphere(map, false, air);

        var grid = _maps.CreateGridEntity(mapId);
        CarveBelly(maw, grid.Owner, Vector2i.Zero, false);
        maw.Comp.Belly = grid.Owner;
        maw.Comp.BellyCentre = Vector2i.Zero;
        maw.Comp.PocketBelly = true;
        return grid.Owner;
    }

    /// <summary>The planet layer directly under a maw on the ground, if there is one.</summary>
    private bool TryGetLayerBelow(EntityUid maw, out EntityUid below)
    {
        below = default;
        if (Transform(maw).MapUid is not { } map || !TryComp(map, out WFPlanetLayerComponent? layer)
            || !TryGetEntity(layer.Network, out var network) || !TryComp(network, out WFPlanetNetworkComponent? comp))
            return false;
        if (comp.Layers.Count == 0 || comp.Layers[0] != map || comp.LowerLayers.Count == 0)
            return false;

        below = comp.LowerLayers[0];
        return HasComp<MapGridComponent>(below);
    }

    /// <summary>
    /// A ring of meat round a floor of flesh, acid pooled in it and the loot of earlier meals scattered about. Cut out of a
    /// planet layer, it clears the rock there and pins its tiles.
    /// </summary>
    private void CarveBelly(Entity<WFSandMawComponent> maw, EntityUid gridUid, Vector2i centre, bool clear)
    {
        var grid = Comp<MapGridComponent>(gridUid);
        var floor = new Tile(_tiles[maw.Comp.BellyFloor].TileId);
        var radius = maw.Comp.BellyRadius;
        var tiles = new List<(Vector2i, Tile)>();
        var walls = new List<Vector2i>();
        var open = new List<Vector2i>();
        for (var x = -radius - 1; x <= radius + 1; x++)
        for (var y = -radius - 1; y <= radius + 1; y++)
        {
            var distance = MathF.Sqrt(x * x + y * y);
            if (distance > radius + 1.5f)
                continue;
            var tile = centre + new Vector2i(x, y);
            tiles.Add((tile, floor));
            if (distance > radius + 0.5f)
                walls.Add(tile);
            else
                open.Add(tile);
        }

        if (clear)
        {
            var anchored = new List<EntityUid>();
            foreach (var (tile, _) in tiles)
            {
                var enumerator = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
                while (enumerator.MoveNext(out var uid))
                    anchored.Add(uid.Value);
            }
            foreach (var uid in anchored)
                Del(uid);
        }
        _maps.SetTiles(gridUid, grid, tiles);
        if (TryComp(gridUid, out BiomeComponent? biome))
            _biome.WfPinTiles((gridUid, biome), tiles.Select(t => t.Item1).ToList());

        foreach (var tile in walls)
        {
            var wall = Spawn(maw.Comp.BellyWall, new EntityCoordinates(gridUid, tile + new Vector2(0.5f)));
            EnsureComp<WFSandMawBellyWallComponent>(wall).Maw = maw.Owner;
        }

        _random.Shuffle(open);
        var next = 0;
        for (; next < Math.Min(6, open.Count); next++)
            Spawn(maw.Comp.Acid, new EntityCoordinates(gridUid, open[next] + new Vector2(0.5f)));
        for (var glow = 0; glow < 3 && next < open.Count; glow++, next++)
            Spawn(maw.Comp.Glow, new EntityCoordinates(gridUid, open[next] + new Vector2(0.5f)));

        // The loot of the fallen, off the acid.
        if (maw.Comp.Loot is { } loot && next < open.Count)
        {
            foreach (var id in _tables.GetSpawns(loot))
            {
                var tile = open[_random.Next(next, open.Count)];
                Spawn(id, new EntityCoordinates(gridUid, tile + new Vector2(_random.NextFloat(0.2f, 0.8f), _random.NextFloat(0.2f, 0.8f))));
            }
        }
    }

    private void OnWallDestroyed(Entity<WFSandMawBellyWallComponent> ent, ref DestructionEventArgs args)
    {
        // Under the maw, a broken wall is just the way out into the rock.
        if (!TryComp(ent.Comp.Maw, out WFSandMawComponent? maw) || !maw.PocketBelly)
            return;
        var exit = Spawn(ent.Comp.Exit, Transform(ent).Coordinates);
        EnsureComp<WFSandMawExitComponent>(exit).Maw = ent.Comp.Maw;
    }

    /// <summary>Out beside the maw, past the reach of its tentacle.</summary>
    private void ClimbOut(EntityUid mawUid, EntityUid climber)
    {
        if (TerminatingOrDeleted(mawUid) || !TryComp(mawUid, out WFSandMawComponent? maw))
            return;

        var outside = _random.NextAngle().ToVec() * (maw.GrabRange + 1.5f);
        _transform.SetCoordinates(climber, Transform(mawUid).Coordinates.Offset(outside));
        _transform.AttachToGridOrMap(climber);
        maw.NextGrab = _timing.CurTime + maw.GrabCooldown * 2;
        _popup.PopupEntity(Loc.GetString("wf-sand-maw-escaped"), climber, climber, PopupType.Medium);
    }
}
