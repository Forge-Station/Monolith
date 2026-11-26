using System.Numerics;
using Content.Server._Forge.Botany.Events;
using Content.Server.Administration.Logs;
using Content.Server.Damage.Systems;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Database;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Botany.BluespaceTomato;

public sealed partial class BluespaceTomatoSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<BluespaceTomatoComponent, UseInHandEvent>(OnUseInHand, before: [typeof(FoodSystem)]);
        SubscribeLocalEvent<BluespaceTomatoComponent, LandEvent>(OnLanded, before: [typeof(DamageOnLandSystem)]);
        SubscribeLocalEvent<BluespaceTomatoComponent, ThrowDoHitEvent>(OnThrowHit);
        SubscribeLocalEvent<BluespaceTomatoComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<PlantHarvestedEvent>(OnPlantHarvested);
    }

    private void OnThrown(EntityUid uid, BluespaceTomatoComponent component, ref ThrownEvent args)
    {
        component.Spent = false;
    }

    private void OnPlantHarvested(PlantHarvestedEvent ev)
    {
        foreach (var product in ev.Products)
        {
            if (!TryComp<BluespaceTomatoComponent>(product, out var component))
                continue;

            if (!TryGetRandomCoordinates(ev.Uid, component.Attempts, component.Radius, out var coordinates))
            {
                _popup.PopupEntity(Loc.GetString("bluespace-tomato-blocked"), ev.Uid);
                return;
            }

            Teleport(ev.Uid, coordinates, ev.Plantholder ?? product, component.SoundOnTeleport);
            return;
        }
    }

    private void OnThrowHit(EntityUid uid, BluespaceTomatoComponent component, ThrowDoHitEvent args)
    {
        // The in-flight fixture is not hard, so a mob hit never arrives as a hard StartCollide.
        if (component.Spent)
            return;

        if (args.Component.Thrower is not { } who || !Exists(who) || args.Target == who)
            return;

        component.Spent = true;

        if (HasComp<MobStateComponent>(args.Target))
        {
            Swap(who, args.Target, uid, component.SoundOnTeleport);
            return;
        }

        if (!TryGetLandingCoordinates(who, uid, out var coordinates))
        {
            _popup.PopupEntity(Loc.GetString("bluespace-tomato-blocked"), who);
            return;
        }

        Teleport(who, coordinates, uid, component.SoundOnTeleport);
    }

    private void OnLanded(EntityUid uid, BluespaceTomatoComponent component, ref LandEvent args)
    {
        if (args.User is not { } user || !Exists(user) || component.Spent)
            return;

        component.Spent = true;

        if (!TryGetLandingCoordinates(user, uid, out var coordinates))
        {
            _popup.PopupEntity(Loc.GetString("bluespace-tomato-blocked"), user);
            return;
        }

        Teleport(user, coordinates, uid, component.SoundOnTeleport);
    }

    private void OnUseInHand(EntityUid uid, BluespaceTomatoComponent component, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TeleportOnUse(uid, component, args.User);
    }

    private void TeleportOnUse(EntityUid tomato, BluespaceTomatoComponent component, EntityUid user)
    {
        if (TryComp<BluespaceTomatoCooldownComponent>(user, out var cooldown))
        {
            var remaining = cooldown.TimeNextActivate - _timing.CurTime.TotalSeconds;
            if (remaining > 0)
            {
                _popup.PopupEntity(Loc.GetString("bluespace-tomato-cooldown", ("time", (int) Math.Ceiling(remaining))), user);
                return;
            }

            RemComp<BluespaceTomatoCooldownComponent>(user);
        }

        if (!TryGetRandomCoordinates(user, component.Attempts, component.Radius, out var coordinates))
        {
            _popup.PopupEntity(Loc.GetString("bluespace-tomato-blocked"), user);
            return;
        }

        var next = EnsureComp<BluespaceTomatoCooldownComponent>(user);
        next.TimeNextActivate = _timing.CurTime.TotalSeconds + component.Cooldown;
        Timer.Spawn(TimeSpan.FromSeconds(component.Cooldown), () =>
        {
            if (!Exists(user) || !TryComp<BluespaceTomatoCooldownComponent>(user, out var pending))
                return;

            if (_timing.CurTime.TotalSeconds >= pending.TimeNextActivate)
                RemComp<BluespaceTomatoCooldownComponent>(user);
        });

        Teleport(user, coordinates, tomato, component.SoundOnTeleport);
        QueueDel(tomato);
    }

    /// <summary>
    /// Picks a spot on a grid, outside space and outside static impassable fixtures.
    /// </summary>
    private bool TryGetRandomCoordinates(EntityUid uid, int attempts, float radius, out MapCoordinates coordinates)
    {
        coordinates = default;
        if (Transform(uid).MapID == MapId.Nullspace)
            return false;

        var origin = _transform.GetMapCoordinates(uid);
        var minRadius = MathF.Min(1f, radius);

        for (var i = 0; i < attempts; i++)
        {
            var distance = minRadius + (radius - minRadius) * MathF.Sqrt(_random.NextFloat());
            var candidate = origin.Offset(_random.NextAngle().ToVec() * distance);
            if (!IsValidDestination(candidate))
                continue;

            coordinates = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Steps back from the impact toward the thrower so the destination is not inside the hit fixture.
    /// </summary>
    private bool TryGetLandingCoordinates(EntityUid user, EntityUid tomato, out MapCoordinates coordinates)
    {
        var tomatoCoords = _transform.GetMapCoordinates(tomato);
        var userCoords = _transform.GetMapCoordinates(user);
        var direction = Vector2.Zero;

        if (userCoords.MapId == tomatoCoords.MapId)
        {
            var delta = tomatoCoords.Position - userCoords.Position;
            if (delta.LengthSquared() > 0.0001f)
                direction = Vector2.Normalize(delta);
        }

        for (var step = 0; step <= 3; step++)
        {
            var candidate = tomatoCoords.Offset(-direction * (0.45f * step));
            if (!IsValidDestination(candidate))
                continue;

            coordinates = candidate;
            return true;
        }

        coordinates = default;
        return false;
    }

    private bool IsValidDestination(MapCoordinates coordinates)
    {
        if (!_map.TryFindGridAt(coordinates, out var gridUid, out var grid))
            return false;

        foreach (var entity in _map.GetAnchoredEntities((gridUid, grid), coordinates))
        {
            if (!_physicsQuery.TryGetComponent(entity, out var body))
                continue;

            if (body.BodyType != BodyType.Static ||
                !body.Hard ||
                (body.CollisionLayer & (int) CollisionGroup.Impassable) == 0)
                continue;

            return false;
        }

        return true;
    }

    private void Swap(EntityUid thrower, EntityUid target, EntityUid tomato, SoundSpecifier? sound)
    {
        if (Transform(thrower).MapID == MapId.Nullspace || Transform(target).MapID == MapId.Nullspace)
            return;

        StopPull(thrower);
        StopPull(target);

        var throwerCoordinates = _transform.GetMapCoordinates(thrower);
        var targetCoordinates = _transform.GetMapCoordinates(target);
        _transform.SetMapCoordinates(thrower, targetCoordinates);
        _transform.SetMapCoordinates(target, throwerCoordinates);

        _popup.PopupEntity(Loc.GetString("bluespace-tomato-teleported"), thrower);
        _popup.PopupEntity(Loc.GetString("bluespace-tomato-teleported"), target);

        if (sound != null)
        {
            _audio.PlayPvs(sound, thrower);
            _audio.PlayPvs(sound, target);
        }

        _adminLogger.Add(
            LogType.Teleport,
            LogImpact.Medium,
            $"{ToPrettyString(thrower):actor} swapped with {ToPrettyString(target):target} using {ToPrettyString(tomato):Entity} from: {throwerCoordinates:coordinates} to: {targetCoordinates:coordinates}");
    }

    private void StopPull(EntityUid uid)
    {
        if (TryComp<PullableComponent>(uid, out var pull) && _pulling.IsPulled(uid, pull))
            _pulling.TryStopPull(uid, pull);
    }

    private void Teleport(EntityUid uid, MapCoordinates coordinates, EntityUid usedEntity, SoundSpecifier? sound)
    {
        if (Transform(uid).MapID == MapId.Nullspace)
            return;

        StopPull(uid);

        var currentCoordinates = _transform.GetMapCoordinates(uid);
        _transform.SetMapCoordinates(uid, coordinates);
        _popup.PopupEntity(Loc.GetString("bluespace-tomato-teleported"), uid);

        if (sound != null)
            _audio.PlayPvs(sound, uid);

        _adminLogger.Add(
            LogType.Teleport,
            LogImpact.Medium,
            $"{ToPrettyString(uid):actor} used: {ToPrettyString(usedEntity):Entity} teleport: from: {currentCoordinates:coordinates} to: {coordinates:coordinates}");
    }
}
