using System.Numerics;
using Content.Server.Light.EntitySystems;
using Content.Shared._WF.Birds;
using Content.Shared.Audio;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Birds;

/// <summary>
/// Drives birds without pathfinding: they glide straight at their goal, with collisions off while airborne so they
/// cross walls, and decide four times a second. A flock follows its leader and lands round the leader's spot.
/// </summary>
public sealed class WFBirdSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private RoofSystem _roof = default!;

    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan IdleThinkInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetireDelay = TimeSpan.FromSeconds(120);
    /// <summary>Metres from a player past which a perched bird only checks in now and then.</summary>
    private const float IdleRange = 40f;
    private const float RetireRange = 96f;
    private const float ArriveDistance = 0.2f;
    private const float StrikeRange = 0.9f;
    private const float MaxFlightSeconds = 40f;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private readonly List<(MapId Map, Vector2 Position)> _playerPositions = new();
    private TimeSpan _nextRetire;
    private TimeSpan _nextPlayerScan;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFBirdComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFBirdComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WFBirdComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnMapInit(Entity<WFBirdComponent> ent, ref MapInitEvent args)
    {
        var bird = ent.Comp;
        var now = _timing.CurTime;
        bird.NextThink = now + TimeSpan.FromSeconds(_random.NextFloat(0f, 0.25f));
        bird.StateEnd = now + Seconds(bird.PerchTime);
        bird.NextHop = now + TimeSpan.FromSeconds(_random.NextFloat(1f, 4f));
        bird.LastSeen = now;
        bird.FlockOffset = bird.Leader == null ? Vector2.Zero : _random.NextVector2(1.6f);
        _appearance.SetData(ent, WFBirdVisuals.Flying, false);

        if (HasComp<WFBirdHostileComponent>(ent))
            _combat.SetInCombatMode(ent, true);

        if (TryComp<AmbientSoundComponent>(ent, out var ambience))
            _ambient.SetAmbience(ent, bird.Leader == null, ambience);

        if (bird.Leader == null)
            SpawnFlock(ent);
    }

    /// <summary>Spawns the rest of a leader's flock round it.</summary>
    private void SpawnFlock(Entity<WFBirdComponent> leader)
    {
        if (MetaData(leader).EntityPrototype is not { } proto)
            return;

        var xform = Transform(leader);
        var count = _random.Next(leader.Comp.MinFlock, Math.Max(leader.Comp.MinFlock, leader.Comp.MaxFlock) + 1) - 1;
        for (var i = 0; i < count; i++)
        {
            var coords = xform.Coordinates;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var candidate = xform.Coordinates.Offset(_random.NextVector2(0.5f, 2.5f));
                if (!IsClear(candidate))
                    continue;
                coords = candidate;
                break;
            }

            var member = EntityManager.CreateEntityUninitialized(proto.ID, coords);
            var bird = EnsureComp<WFBirdComponent>(member);
            bird.Leader = leader;
            bird.Member = true;
            EntityManager.InitializeAndStartEntity(member);
        }
    }

    private void OnMobStateChanged(Entity<WFBirdComponent> ent, ref MobStateChangedEvent args)
    {
        ent.Comp.Dead = args.NewMobState != MobState.Alive;
        if (ent.Comp.Dead)
        {
            ent.Comp.Protected = true;
            Land(ent, ent.Comp);
            if (TryComp<AmbientSoundComponent>(ent, out var ambience))
                _ambient.SetAmbience(ent, false, ambience);
        }
    }

    private void OnDamaged(Entity<WFBirdComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || ent.Comp.Dead)
            return;

        ent.Comp.Protected = true;
        if (TryComp<WFBirdHostileComponent>(ent, out var hostile) && args.Origin is { } origin &&
            origin != ent.Owner && _mobState.IsAlive(origin))
        {
            hostile.Target = origin;
            hostile.NextDive = _timing.CurTime;
            return;
        }

        var from = args.Origin is { } attacker && Exists(attacker) ? _transform.GetWorldPosition(attacker) : (Vector2?) null;
        Startle(ent, from);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now >= _nextRetire)
        {
            _nextRetire = now + TimeSpan.FromSeconds(10);
            RetireMembers(now);
        }

        if (now >= _nextPlayerScan)
        {
            _nextPlayerScan = now + TimeSpan.FromSeconds(1);
            ScanPlayers();
        }

        var query = EntityQueryEnumerator<WFBirdComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var bird, out var xform))
        {
            if (bird.Dead)
                continue;

            if (bird.Moving)
                Move(uid, bird, xform, frameTime);

            if (now < bird.NextThink)
                continue;

            // A settled bird nobody can see has nothing to decide; its range checks are most of this system's cost.
            if (bird.State == WFBirdState.Perched && !bird.Moving && !NearPlayer(xform, IdleRange))
            {
                bird.NextThink = now + IdleThinkInterval;
                continue;
            }

            bird.NextThink = now + ThinkInterval;
            Think(uid, bird, xform, now);
        }
    }

    /// <summary>Whether any player is within <paramref name="range"/> of the bird, as of the last scan.</summary>
    private bool NearPlayer(TransformComponent xform, float range)
    {
        var position = _transform.GetWorldPosition(xform);
        foreach (var (map, player) in _playerPositions)
        {
            if (map == xform.MapID && (player - position).LengthSquared() <= range * range)
                return true;
        }

        return false;
    }

    private void ScanPlayers()
    {
        _playerPositions.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player || !TryComp(player, out TransformComponent? xform))
                continue;
            _playerPositions.Add((xform.MapID, _transform.GetWorldPosition(xform)));
        }
    }

    #region Movement

    private void Move(EntityUid uid, WFBirdComponent bird, TransformComponent xform, float frameTime)
    {
        var speed = bird.State switch
        {
            WFBirdState.Perched => bird.WalkSpeed,
            WFBirdState.Diving => CompOrNull<WFBirdHostileComponent>(uid)?.DiveSpeed ?? bird.FlySpeed,
            WFBirdState.Climbing => bird.FlySpeed * 1.2f,
            _ => bird.FlySpeed,
        };

        if (bird.State == WFBirdState.Circling)
        {
            bird.CircleAngle += speed / MathF.Max(bird.CircleRadius, 0.5f) * frameTime;
            bird.Goal = bird.CircleCentre + new Vector2(MathF.Cos(bird.CircleAngle), MathF.Sin(bird.CircleAngle)) * bird.CircleRadius;
        }

        var position = xform.LocalPosition;
        var delta = bird.Goal - position;
        var distance = delta.Length();
        if (distance < ArriveDistance)
        {
            if (bird.State == WFBirdState.Perched)
                bird.Moving = false;
            return;
        }

        var step = MathF.Min(distance, speed * frameTime);
        _transform.SetLocalPosition(uid, position + delta / distance * step, xform);
        _transform.SetLocalRotation(uid, delta.ToWorldAngle(), xform);
    }

    private bool Arrived(WFBirdComponent bird, TransformComponent xform, float within = 0.3f)
    {
        return (bird.Goal - xform.LocalPosition).LengthSquared() <= within * within;
    }

    #endregion

    #region Decisions

    private void Think(EntityUid uid, WFBirdComponent bird, TransformComponent xform, TimeSpan now)
    {
        if (bird.CallSound != null && _random.Prob(bird.CallChance))
            _audio.PlayPvs(bird.CallSound, uid);

        if (TryComp<WFBirdHostileComponent>(uid, out var hostile) && ThinkHostile(uid, bird, hostile, xform, now))
            return;

        if (bird.Leader is { } leaderUid)
        {
            if (!TryComp<WFBirdComponent>(leaderUid, out var leader) || leader.Dead || TerminatingOrDeleted(leaderUid) ||
                Transform(leaderUid).ParentUid != xform.ParentUid)
            {
                // The flock breaks up; each bird goes its own way.
                bird.Leader = null;
            }
            else
            {
                ThinkMember(uid, bird, xform, leaderUid, leader, now);
                return;
            }
        }

        switch (bird.State)
        {
            case WFBirdState.Perched:
                if (FindStartler(uid, bird, xform) is { } scare)
                {
                    Startle((uid, bird), scare);
                    return;
                }

                if (now >= bird.StateEnd)
                {
                    TakeOff(uid, bird, PickGoal(xform, bird, null));
                    return;
                }

                Peck(uid, bird, xform, now);
                break;
            case WFBirdState.Flying:
                if (Arrived(bird, xform) || now >= bird.StateEnd)
                {
                    var circle = _random.NextFloat(bird.CircleTime.X, bird.CircleTime.Y);
                    if (circle >= 1f && now < bird.StateEnd)
                        StartCircling(bird, bird.Goal, circle, now);
                    else
                        TryLand(uid, bird, xform);
                }
                break;
            case WFBirdState.Circling:
                if (now >= bird.StateEnd)
                {
                    bird.State = WFBirdState.Flying;
                    bird.Goal = bird.CircleCentre;
                    bird.StateEnd = now + TimeSpan.FromSeconds(MaxFlightSeconds);
                }
                break;
            default:
                // A hostile bird that lost its prey.
                bird.State = WFBirdState.Flying;
                bird.Goal = PickGoal(xform, bird, null);
                bird.StateEnd = now + TimeSpan.FromSeconds(MaxFlightSeconds);
                break;
        }
    }

    private void ThinkMember(EntityUid uid, WFBirdComponent bird, TransformComponent xform, EntityUid leaderUid,
        WFBirdComponent leader, TimeSpan now)
    {
        var leaderXform = Transform(leaderUid);
        var leaderAirborne = leader.State != WFBirdState.Perched;

        if (bird.State == WFBirdState.Perched)
        {
            if (leaderAirborne)
            {
                TakeOff(uid, bird, leader.Goal + bird.FlockOffset);
                return;
            }

            if (FindStartler(uid, bird, xform) is { } scare)
            {
                Startle((uid, bird), scare);
                return;
            }

            Peck(uid, bird, xform, now);
            return;
        }

        bird.State = WFBirdState.Flying;
        if (leaderAirborne)
        {
            bird.Goal = leaderXform.LocalPosition + bird.FlockOffset;
            return;
        }

        bird.Goal = leaderXform.LocalPosition + bird.FlockOffset;
        if (!IsClear(new EntityCoordinates(xform.ParentUid, bird.Goal)))
        {
            // Somewhere else round the leader next time.
            bird.FlockOffset = _random.NextVector2(0.6f, 2.5f);
            return;
        }

        if (Arrived(bird, xform, 0.4f))
            Land(uid, bird);
    }

    /// <summary>A short hop to a clear neighbouring tile now and then, which reads as walking and pecking.</summary>
    private void Peck(EntityUid uid, WFBirdComponent bird, TransformComponent xform, TimeSpan now)
    {
        if (now < bird.NextHop || bird.Moving)
            return;

        bird.NextHop = now + TimeSpan.FromSeconds(_random.NextFloat(1.5f, 5f));
        var target = xform.Coordinates.Offset(_random.NextVector2(0.4f, 1.2f));
        if (!IsClear(target))
            return;
        bird.Goal = target.Position;
        bird.Moving = true;
    }

    /// <summary>Something alive near enough, or running near enough, to put the flock up.</summary>
    public Vector2? FindStartler(EntityUid uid, WFBirdComponent bird, TransformComponent xform)
    {
        _mobs.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, MathF.Max(bird.NearRange, bird.ScareRange), _mobs);
        var position = _transform.GetWorldPosition(xform);
        foreach (var mob in _mobs)
        {
            if (mob.Owner == uid || HasComp<WFBirdComponent>(mob) || !_mobState.IsAlive(mob, mob.Comp))
                continue;

            var mobPosition = _transform.GetWorldPosition(mob);
            var distanceSquared = (mobPosition - position).LengthSquared();
            if (distanceSquared <= bird.NearRange * bird.NearRange)
                return mobPosition;

            if (distanceSquared <= bird.ScareRange * bird.ScareRange && TryComp<PhysicsComponent>(mob, out var body) &&
                body.LinearVelocity.LengthSquared() >= bird.ScareSpeed * bird.ScareSpeed)
                return mobPosition;
        }

        return null;
    }

    /// <summary>Puts the bird's whole flock up, flying away from <paramref name="from"/> when given.</summary>
    public void Startle(Entity<WFBirdComponent> ent, Vector2? from)
    {
        var leaderUid = ent.Comp.Leader is { } l && TryComp<WFBirdComponent>(l, out var leader) && !leader.Dead
            ? l
            : ent.Owner;
        var lead = Comp<WFBirdComponent>(leaderUid);
        if (lead.State == WFBirdState.Perched)
            TakeOff(leaderUid, lead, PickGoal(Transform(leaderUid), lead, from));
        if (leaderUid != ent.Owner && ent.Comp.State == WFBirdState.Perched)
            TakeOff(ent, ent.Comp, lead.Goal + ent.Comp.FlockOffset);
    }

    /// <summary>A landing goal up to the roam range away, heading away from a startler if there is one.</summary>
    private Vector2 PickGoal(TransformComponent xform, WFBirdComponent bird, Vector2? from)
    {
        var position = xform.LocalPosition;
        var away = Angle.Zero;
        if (from is { } fromWorld)
        {
            var local = Vector2.Transform(fromWorld, _transform.GetInvWorldMatrix(xform.ParentUid));
            var dir = position - local;
            if (dir.LengthSquared() > 0.01f)
                away = dir.ToAngle();
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var angle = from == null
                ? _random.NextAngle()
                : away + Angle.FromDegrees(_random.NextFloat(-60f, 60f));
            var goal = position + angle.ToVec() * _random.NextFloat(bird.RoamRange * 0.5f, bird.RoamRange);
            if (IsClear(new EntityCoordinates(xform.ParentUid, goal)))
                return goal;
        }

        return position + _random.NextAngle().ToVec() * bird.RoamRange * 0.5f;
    }

    #endregion

    #region Hostile

    /// <summary>Hunting: returns true while the bird is busy with prey.</summary>
    private bool ThinkHostile(EntityUid uid, WFBirdComponent bird, WFBirdHostileComponent hostile, TransformComponent xform, TimeSpan now)
    {
        if (hostile.Target is { } current && !ValidPrey(uid, hostile, xform, current, hostile.DiveRange * 1.8f))
        {
            hostile.Target = null;
            if (bird.State is WFBirdState.Diving or WFBirdState.Climbing or WFBirdState.Circling)
            {
                bird.State = WFBirdState.Flying;
                bird.Goal = PickGoal(xform, bird, null);
                bird.StateEnd = now + TimeSpan.FromSeconds(MaxFlightSeconds);
            }
        }

        if (hostile.Target == null)
        {
            if (now < hostile.NextDive || FindPrey(uid, hostile, xform) is not { } prey)
                return false;
            hostile.Target = prey;
            bird.Leader = null;
            if (bird.State == WFBirdState.Perched)
                TakeOff(uid, bird, xform.LocalPosition);
            BeginApproach(uid, bird, hostile, now);
            return true;
        }

        if (bird.State == WFBirdState.Perched)
        {
            // Hurt while perched: up and after the attacker.
            bird.Leader = null;
            TakeOff(uid, bird, xform.LocalPosition);
            BeginApproach(uid, bird, hostile, now);
            return true;
        }

        var target = hostile.Target.Value;
        var targetPosition = Transform(target).LocalPosition;
        switch (bird.State)
        {
            case WFBirdState.Diving:
                bird.Goal = targetPosition;
                if ((targetPosition - xform.LocalPosition).Length() <= StrikeRange)
                {
                    Strike(uid, target);
                    ClimbAway(uid, bird, hostile, xform, targetPosition, now);
                }
                else if (now >= bird.StateEnd)
                {
                    ClimbAway(uid, bird, hostile, xform, targetPosition, now);
                }
                break;
            case WFBirdState.Climbing:
                if (now >= bird.StateEnd || Arrived(bird, xform))
                    BeginApproach(uid, bird, hostile, now);
                break;
            case WFBirdState.Circling:
                bird.CircleCentre = targetPosition;
                if (now >= bird.StateEnd && now >= hostile.NextDive)
                    BeginDive(uid, bird, hostile, now);
                break;
            default:
                BeginApproach(uid, bird, hostile, now);
                break;
        }

        return true;
    }

    private void BeginApproach(EntityUid uid, WFBirdComponent bird, WFBirdHostileComponent hostile, TimeSpan now)
    {
        if (hostile.Target is not { } target)
            return;

        if (hostile.CircleTime > 0f)
        {
            StartCircling(bird, Transform(target).LocalPosition, hostile.CircleTime, now);
            return;
        }

        if (now < hostile.NextDive)
        {
            StartCircling(bird, Transform(target).LocalPosition, (float) (hostile.NextDive - now).TotalSeconds, now);
            return;
        }

        BeginDive(uid, bird, hostile, now);
    }

    private void BeginDive(EntityUid uid, WFBirdComponent bird, WFBirdHostileComponent hostile, TimeSpan now)
    {
        bird.State = WFBirdState.Diving;
        bird.Moving = true;
        bird.StateEnd = now + TimeSpan.FromSeconds(hostile.MaxDiveTime);
        if (hostile.DiveSound != null)
            _audio.PlayPvs(hostile.DiveSound, uid);
    }

    private void ClimbAway(EntityUid uid, WFBirdComponent bird, WFBirdHostileComponent hostile, TransformComponent xform,
        Vector2 from, TimeSpan now)
    {
        var away = xform.LocalPosition - from;
        if (away.LengthSquared() < 0.01f)
            away = _random.NextAngle().ToVec();
        bird.State = WFBirdState.Climbing;
        bird.Goal = xform.LocalPosition + Vector2.Normalize(away) * 4f;
        bird.StateEnd = now + TimeSpan.FromSeconds(hostile.ClimbTime);
        hostile.NextDive = now + TimeSpan.FromSeconds(hostile.Cooldown);
    }

    /// <summary>One bite or talon strike with the bird's own melee weapon.</summary>
    private void Strike(EntityUid uid, EntityUid target)
    {
        if (!TryComp<MeleeWeaponComponent>(uid, out var melee))
            return;
        _combat.SetInCombatMode(uid, true);
        melee.NextAttack = _timing.CurTime;
        _melee.AttemptLightAttack(uid, uid, melee, target);
    }

    private EntityUid? FindPrey(EntityUid uid, WFBirdHostileComponent hostile, TransformComponent xform)
    {
        _mobs.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, hostile.DiveRange, _mobs);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var position = _transform.GetWorldPosition(xform);
        foreach (var mob in _mobs)
        {
            if (!ValidPrey(uid, hostile, xform, mob, hostile.DiveRange))
                continue;
            if (hostile.LoneOnly && HasCompany(mob, hostile.LoneRadius))
                continue;
            var distance = (_transform.GetWorldPosition(mob) - position).LengthSquared();
            if (distance >= bestDistance)
                continue;
            best = mob;
            bestDistance = distance;
        }

        return best;
    }

    private bool ValidPrey(EntityUid uid, WFBirdHostileComponent hostile, TransformComponent xform, EntityUid target, float range)
    {
        if (target == uid || TerminatingOrDeleted(target) || HasComp<WFBirdComponent>(target) ||
            !TryComp<MobStateComponent>(target, out var state) || _mobState.IsDead(target, state))
            return false;
        if (hostile.MinTargetDamage <= 0f && !_mobState.IsAlive(target, state))
            return false;
        if (hostile.PlayersOnly && !HasComp<ActorComponent>(target))
            return false;
        if (hostile.MinTargetDamage > 0f &&
            (!TryComp<DamageableComponent>(target, out var damage) || damage.TotalDamage < hostile.MinTargetDamage))
            return false;

        var targetXform = Transform(target);
        if (targetXform.ParentUid != xform.ParentUid ||
            (targetXform.LocalPosition - xform.LocalPosition).LengthSquared() > range * range)
            return false;

        return !IsRoofed(targetXform.Coordinates);
    }

    /// <summary>Whether another player stands near the prey.</summary>
    private bool HasCompany(EntityUid prey, float radius)
    {
        var position = _transform.GetWorldPosition(prey);
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } other || other == prey || !_mobState.IsAlive(other))
                continue;
            if (Transform(other).MapID != Transform(prey).MapID)
                continue;
            if ((_transform.GetWorldPosition(other) - position).LengthSquared() <= radius * radius)
                return true;
        }

        return false;
    }

    #endregion

    #region Take-off and landing

    /// <summary>Puts the bird in the air, flying to <paramref name="goal"/> in its parent's frame.</summary>
    public void TakeOff(EntityUid uid, WFBirdComponent bird, Vector2 goal)
    {
        var wasAirborne = bird.State != WFBirdState.Perched;
        bird.State = WFBirdState.Flying;
        bird.Goal = goal;
        bird.Moving = true;
        bird.StateEnd = _timing.CurTime + TimeSpan.FromSeconds(MaxFlightSeconds);
        if (wasAirborne)
            return;

        SetAirborne(uid, bird, true);
        if (bird.TakeoffSound != null)
            _audio.PlayPvs(bird.TakeoffSound, uid);
    }

    private void StartCircling(WFBirdComponent bird, Vector2 centre, float seconds, TimeSpan now)
    {
        bird.State = WFBirdState.Circling;
        bird.Moving = true;
        bird.CircleCentre = centre;
        bird.CircleAngle = _random.NextFloat(0f, MathF.Tau);
        bird.StateEnd = now + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Lands on the tile below if it is clear open ground, or heads for the nearest one that is.</summary>
    private void TryLand(EntityUid uid, WFBirdComponent bird, TransformComponent xform)
    {
        var coords = xform.Coordinates;
        if (IsClear(coords))
        {
            Land(uid, bird);
            return;
        }

        for (var radius = 1; radius <= 4; radius++)
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var candidate = coords.Offset(_random.NextAngle().ToVec() * radius);
                if (!IsClear(candidate))
                    continue;
                bird.State = WFBirdState.Flying;
                bird.Goal = candidate.Position;
                return;
            }
        }

        bird.State = WFBirdState.Flying;
        bird.Goal = PickGoal(xform, bird, null);
    }

    private void Land(EntityUid uid, WFBirdComponent bird)
    {
        var wasAirborne = bird.State != WFBirdState.Perched;
        bird.State = WFBirdState.Perched;
        bird.Moving = false;
        bird.StateEnd = _timing.CurTime + Seconds(bird.PerchTime);
        bird.NextHop = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(1f, 3f));
        if (wasAirborne)
            SetAirborne(uid, bird, false);
    }

    /// <summary>Airborne birds collide with nothing but bullets.</summary>
    private void SetAirborne(EntityUid uid, WFBirdComponent bird, bool airborne)
    {
        _appearance.SetData(uid, WFBirdVisuals.Flying, airborne);
        if (!TryComp<FixturesComponent>(uid, out var fixtures))
            return;

        if (airborne)
        {
            bird.SavedFixtures.Clear();
            foreach (var (id, fixture) in fixtures.Fixtures)
            {
                bird.SavedFixtures[id] = (fixture.CollisionLayer, fixture.CollisionMask);
                _physics.SetCollisionMask(uid, id, fixture, 0, fixtures);
                // Bullets, lasers and hitscan still find it; walls and mobs don't.
                _physics.SetCollisionLayer(uid, id, fixture, (int) CollisionGroup.SmallMobLayer, fixtures);
            }
            return;
        }

        foreach (var (id, (layer, mask)) in bird.SavedFixtures)
        {
            if (!fixtures.Fixtures.TryGetValue(id, out var fixture))
                continue;
            _physics.SetCollisionLayer(uid, id, fixture, layer, fixtures);
            _physics.SetCollisionMask(uid, id, fixture, mask, fixtures);
        }
        bird.SavedFixtures.Clear();
    }

    #endregion

    #region Helpers

    /// <summary>Open, unroofed ground nothing solid stands on.</summary>
    public bool IsClear(EntityCoordinates coords)
    {
        if (_turf.GetTileRef(coords) is not { } tile || tile.Tile.IsEmpty)
            return false;
        if (_turf.IsTileBlocked(tile, CollisionGroup.MobMask))
            return false;
        return !IsRoofed(coords);
    }

    private bool IsRoofed(EntityCoordinates coords)
    {
        if (_transform.GetGrid(coords) is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !TryComp<RoofComponent>(gridUid, out var roof))
            return false;
        return _roof.IsRooved((gridUid, grid, roof), _map.TileIndicesFor(gridUid, grid, coords));
    }

    private TimeSpan Seconds(Vector2 range)
    {
        return TimeSpan.FromSeconds(_random.NextFloat(range.X, MathF.Max(range.X, range.Y)));
    }

    /// <summary>Flock members spawned beside a leader leave once no player has been near them for a while.</summary>
    private void RetireMembers(TimeSpan now)
    {
        ScanPlayers();

        var query = EntityQueryEnumerator<WFBirdComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var bird, out var xform))
        {
            if (!bird.Member || bird.Protected || bird.Dead)
                continue;

            var position = _transform.GetWorldPosition(xform);
            foreach (var (map, player) in _playerPositions)
            {
                if (map != xform.MapID || (player - position).LengthSquared() > RetireRange * RetireRange)
                    continue;
                bird.LastSeen = now;
                break;
            }

            if (now - bird.LastSeen >= RetireDelay)
                QueueDel(uid);
        }
    }

    #endregion
}
