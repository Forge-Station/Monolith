using System.Numerics;
using Content.Server._Forge.Botany.Events;
using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Interaction.Events;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Botany.BluespaceTomato;

public sealed class BluespaceTomatoSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BluespaceTomatoComponent, UseInHandEvent>(OnUseInHandEvent);
        SubscribeLocalEvent<BluespaceTomatoComponent, LandEvent>(OnLanded);
        SubscribeLocalEvent<BluespaceTomatoComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<PlantHarvestedEvent>(OnPlantHarvested);
    }

    private void OnPlantHarvested(PlantHarvestedEvent ev)
    {
        var playerUid = ev.Uid;
        var products = ev.Products;

        foreach (var product in products)
        {
            if (!TryComp<BluespaceTomatoComponent>(product, out var bluespaceTomatoComponent))
                continue;

            var mapCoordinates = GetRandomMapCoordinatesFromEntity(playerUid, bluespaceTomatoComponent.Attempts, bluespaceTomatoComponent.Radius);

            if (mapCoordinates is not { } coordinates)
            {
                _popup.PopupEntity(Loc.GetString("bluespace-tomato-blocked"), playerUid);
                return;
            }

            Teleport(playerUid, coordinates, ev.Plantholder, bluespaceTomatoComponent.SoundOnTeleport);
            return;
        }
    }

    private void OnStartCollide(EntityUid uid, BluespaceTomatoComponent bluespaceTomatoComponent, StartCollideEvent args)
    {
        if (!TryComp<ThrownItemComponent>(uid, out var thrown))
            return;

        if (bluespaceTomatoComponent.Spent)
            return;

        if (thrown.Thrower is not { } who || !Exists(who))
            return;

        if (args.OtherEntity == who)
            return;

        bluespaceTomatoComponent.Spent = true;
        TeleportToLanding(who, uid, bluespaceTomatoComponent);
    }


    private void OnLanded(EntityUid uid, BluespaceTomatoComponent bluespaceTomatoComponent, LandEvent args)
    {
        if (!Exists(args.User))
            return;

        TeleportToLanding(args.User.Value, uid, bluespaceTomatoComponent);
    }

    private void OnUseInHandEvent(EntityUid uid, BluespaceTomatoComponent bluespaceTomatoComponent, UseInHandEvent args)
    {
        TeleportOnUse(uid, bluespaceTomatoComponent, args.User);
        args.Handled = true;
    }

    private void TeleportToLanding(EntityUid userUid, EntityUid tomatoUid, BluespaceTomatoComponent bluespaceTomatoComponent)
{
    var userXform = Transform(userUid);
    if (userXform.MapID == MapId.Nullspace)
        return;

    Teleport(userUid, _transform.GetMapCoordinates(tomatoUid), tomatoUid, bluespaceTomatoComponent.SoundOnTeleport);
}

    private void TeleportOnUse(EntityUid bluespaceTomatoUid, BluespaceTomatoComponent bluespaceTomatoComponent, EntityUid userUid)
    {
        var bluespaceTomatoCooldownComponent = EnsureComp<BluespaceTomatoCooldownComponent>(userUid);

        if (_timing.CurTime.TotalSeconds < bluespaceTomatoCooldownComponent.TimeNextActivate)
        {
            var remaining = bluespaceTomatoCooldownComponent.TimeNextActivate - _timing.CurTime.TotalSeconds;

            _popup.PopupEntity(Loc.GetString("bluespace-tomato-cooldown", ("time", (int)Math.Ceiling(remaining))), userUid);
            return;
        }

        var mapCoordinates = GetRandomMapCoordinatesFromEntity(userUid, bluespaceTomatoComponent.Attempts, bluespaceTomatoComponent.Radius);

        if (mapCoordinates is not { } coordinates)
        {
            _popup.PopupEntity(Loc.GetString("bluespace-tomato-blocked"), userUid);
            return;
        }

        bluespaceTomatoCooldownComponent.TimeNextActivate = _timing.CurTime.TotalSeconds + bluespaceTomatoComponent.Cooldown;
        Teleport(userUid, coordinates, bluespaceTomatoUid, bluespaceTomatoComponent.SoundOnTeleport);
        QueueDel(bluespaceTomatoUid);
    }

    private MapCoordinates? GetRandomMapCoordinatesFromEntity(EntityUid uid, int attempts, float radius)
    {
        var mapId = Transform(uid).MapID;

        if (mapId == MapId.Nullspace)
            return null;

        var currentPosition = _transform.GetWorldPosition(uid);

        for (var i = 0; i < attempts; i++)
        {
            var angle = _random.NextFloat(MathF.Tau);
            var dist = _random.NextFloat(radius);
            var newPosition = currentPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;

            if (IsWallAt(newPosition, mapId))
                continue;

            return new(newPosition, mapId);
        }

        return null;
    }

    private void Teleport(EntityUid uid, MapCoordinates coordinates, EntityUid usedEntity, SoundPathSpecifier? sound = null)
    {
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

    private bool IsWallAt(Vector2 worldPos, MapId mapId)
    {
        var box = new Box2(worldPos - new Vector2(0.4f, 0.4f), worldPos + new Vector2(0.4f, 0.4f));

        var query = new FixtureQueryArgs(new()
        {
            Flags = QueryFlags.Static,
            LayerBits = (int) CollisionGroup.MobMask,
            MaskBits = (int) CollisionGroup.Impassable,
        });

        var fixtures = new HashSet<FixtureProxy>();
        _lookup.GetFixturesIntersecting(mapId, box, fixtures, query);

        return fixtures.Count > 0;
    }
}
