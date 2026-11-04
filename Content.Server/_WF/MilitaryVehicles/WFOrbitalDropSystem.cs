using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Server._WF.VehicleShop;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.LandingGuide;
using Content.Shared._WF.MilitaryVehicles;
using Content.Shared._WF.Planets;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.MilitaryVehicles;

/// <summary>
/// Orbital drop beacons: set off on a planet's open ground, they count down and the vehicle drops from orbit onto them,
/// square to the ground, with the key handed to whoever set it off. Indoors, off the ground or blocked, they say why.
/// Sold from a catalogue that lists the beacons themselves.
/// </summary>
public sealed class WFOrbitalDropSystem : EntitySystem
{
    /// <summary>The key handed over with a dropped vehicle.</summary>
    private static readonly EntProtoId VehicleKey = "RMCVehicleKey";

    /// <summary>How near the one who set it off has to be to be handed the key.</summary>
    private const float HandOverRange = 12f;

    /// <summary>How far the landing is heard.</summary>
    private const float SoundRange = 25f;

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private WFLandingSurveySystem _landing = default!;
    [Dependency] private VehicleLockSystem _locks = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private WFVehicleShopSystem _shop = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    private List<Entity<MapGridComponent>> _gridsAbove = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOrbitalDropBeaconComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WFOrbitalDropBeaconComponent, PriceCalculationEvent>(OnPrice);
    }

    private void OnUseInHand(Entity<WFOrbitalDropBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.LandsAt != null)
            return;

        // Checked where the user stands, before the beacon leaves the hand, so a bad spot keeps it in hand. People can
        // walk off in the countdown, so only the touchdown counts them.
        if (CheckDropZone(ent, Transform(args.User).Coordinates, out var thing, mobs: false) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal, ("thing", thing ?? ent.Owner)), args.User, args.User, PopupType.MediumCaution);
            return;
        }

        _hands.TryDrop(args.User, ent.Owner);
        Arm(ent, args.User);
        _popup.PopupEntity(Loc.GetString("wf-orbital-drop-armed", ("seconds", (int) ent.Comp.Delay.TotalSeconds)), ent, args.User, PopupType.Medium);
    }

    /// <summary>Starts the countdown.</summary>
    public void Arm(Entity<WFOrbitalDropBeaconComponent> ent, EntityUid? user)
    {
        ent.Comp.LandsAt = _timing.CurTime + ent.Comp.Delay;
        ent.Comp.NextBeep = _timing.CurTime;
        ent.Comp.Rumbled = false;
        ent.Comp.ArmedBy = user;
        _appearance.SetData(ent, WFVehicleBeaconVisuals.Armed, true);
    }

    private void Disarm(Entity<WFOrbitalDropBeaconComponent> ent)
    {
        ent.Comp.LandsAt = null;
        ent.Comp.ArmedBy = null;
        _appearance.SetData(ent, WFVehicleBeaconVisuals.Armed, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFOrbitalDropBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            if (beacon.LandsAt is not { } landsAt)
                continue;

            if (now >= landsAt)
            {
                TryLand((uid, beacon));
                continue;
            }

            if (!beacon.Rumbled && now >= landsAt - beacon.RumbleLead)
            {
                beacon.Rumbled = true;
                _audio.PlayPvs(beacon.RumbleSound, uid, AudioParams.Default.WithMaxDistance(SoundRange));
                _popup.PopupEntity(Loc.GetString("wf-orbital-drop-inbound"), uid, PopupType.LargeCaution);
            }

            if (now < beacon.NextBeep)
                continue;

            beacon.NextBeep = now + TimeSpan.FromSeconds(1);
            _audio.PlayPvs(beacon.BeepSound, uid);
            var left = (int) Math.Ceiling((landsAt - now).TotalSeconds);
            if (left <= 3)
                _popup.PopupEntity(Loc.GetString("wf-orbital-drop-countdown", ("seconds", left)), uid, PopupType.Medium);
        }
    }

    /// <summary>
    /// Why a vehicle can't drop at a spot, as a locale id, with the thing in the way when it is one; null when it can:
    /// open ground on a planet's ground layer, no roof or upper level over its room and nothing solid in it.
    /// </summary>
    public string? CheckDropZone(Entity<WFOrbitalDropBeaconComponent> ent, EntityCoordinates coords, out EntityUid? thing, bool mobs = true)
    {
        thing = null;
        var map = _transform.GetMap(coords);
        if (map is not { } mapUid || !HasComp<WFPlanetLayerComponent>(mapUid) || HasComp<WFOrbitLayerComponent>(mapUid)
            || HasComp<WFCavernLayerComponent>(mapUid) || !_landing.IsGroundLayer(mapUid))
            return "wf-orbital-drop-not-planet";

        // The ground is the layer's own grid; anything else here is a hull, station or building.
        var grid = _transform.GetGrid(coords);
        if (grid != mapUid || !TryComp<MapGridComponent>(mapUid, out var gridComp))
            return grid == null ? "wf-orbital-drop-not-planet" : "wf-orbital-drop-on-grid";

        var tile = _map.TileIndicesFor(mapUid, gridComp, coords);
        var spot = _map.GridTileToLocal(mapUid, gridComp, tile);
        var mapSpot = _transform.ToMapCoordinates(spot);
        var footprint = _shop.Footprint(new[] { ent.Comp.Vehicle });
        var room = footprint.Translated(mapSpot.Position);

        if (Roofed((mapUid, gridComp), room))
            return "wf-orbital-drop-roofed";

        if (_shop.FindBlocker(mapSpot, _transform.GetWorldRotation(mapUid), footprint, ent, mobs) is { } blocker)
        {
            thing = blocker;
            return "wf-orbital-drop-blocked";
        }

        return null;
    }

    /// <summary>Whether any tile of the room is under a roof, or under a grid on the level above.</summary>
    private bool Roofed(Entity<MapGridComponent> ground, Box2 room)
    {
        if (TryComp<RoofComponent>(ground, out var roof))
        {
            var min = _map.WorldToTile(ground, ground.Comp, room.BottomLeft);
            var max = _map.WorldToTile(ground, ground.Comp, room.TopRight);
            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    if (_roof.IsRooved((ground, ground.Comp, roof), new Vector2i(x, y)))
                        return true;
                }
            }
        }

        if (!_zLevels.TryMapOffset(ground.Owner, 1, out var above) || !TryComp<MapComponent>(above, out var aboveMap))
            return false;

        _gridsAbove.Clear();
        _map.FindGridsIntersecting(aboveMap.MapId, room, ref _gridsAbove, approx: false, includeMap: false);
        return _gridsAbove.Count > 0;
    }

    /// <summary>Drops the vehicle onto the beacon if the spot is still good; otherwise says why and stops.</summary>
    private void TryLand(Entity<WFOrbitalDropBeaconComponent> ent)
    {
        var xform = Transform(ent);
        if (_container.IsEntityInContainer(ent))
        {
            _popup.PopupEntity(Loc.GetString("wf-orbital-drop-not-planet"), ent, PopupType.MediumCaution);
            Disarm(ent);
            return;
        }

        if (CheckDropZone(ent, xform.Coordinates, out var thing) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal, ("thing", thing ?? ent.Owner)), ent, PopupType.MediumCaution);
            Disarm(ent);
            return;
        }

        var ground = xform.GridUid!.Value;
        var gridComp = Comp<MapGridComponent>(ground);
        var spot = _map.GridTileToLocal(ground, gridComp, _map.TileIndicesFor(ground, gridComp, xform.Coordinates));

        var vehicle = Spawn(ent.Comp.Vehicle, spot);
        _transform.SetLocalRotation(vehicle, Angle.Zero);

        EntityUid? key = null;
        if (HasComp<RMCVehicleComponent>(vehicle))
        {
            key = Spawn(VehicleKey, spot);
            _locks.BindKey((key.Value, EnsureComp<VehicleKeyComponent>(key.Value)), _locks.EnsureVehicleKeyId(vehicle), vehicle);
        }

        if (key != null && ent.Comp.ArmedBy is { } user && !TerminatingOrDeleted(user)
            && _transform.InRange(Transform(user).Coordinates, spot, HandOverRange))
            _hands.PickupOrDrop(user, key.Value);

        // Dust thrown up at the middle and corners of where it came down.
        var room = _shop.Footprint(new[] { ent.Comp.Vehicle });
        Spawn(ent.Comp.LandEffect, spot);
        foreach (var corner in new[] { room.BottomLeft, room.BottomRight, room.TopLeft, room.TopRight })
            Spawn(ent.Comp.LandEffect, spot.Offset(corner));

        _audio.PlayPvs(ent.Comp.LandSound, vehicle, AudioParams.Default.WithMaxDistance(SoundRange));
        _popup.PopupEntity(Loc.GetString("wf-orbital-drop-landed", ("vehicle", vehicle)), vehicle, PopupType.Large);
        _adminLog.Add(LogType.EntitySpawn, LogImpact.Low,
            $"{ToPrettyString(ent):beacon} armed by {ToPrettyString(ent.Comp.ArmedBy):actor} dropped {ToPrettyString(vehicle)}");
        QueueDel(ent);
    }

    /// <summary>An unused beacon sells as the vehicle would at a terminal's pad, never above its listing.</summary>
    private void OnPrice(Entity<WFOrbitalDropBeaconComponent> ent, ref PriceCalculationEvent args)
    {
        if (args.Handled || !_proto.TryIndex(ent.Comp.Vehicle, out var vehicle))
            return;

        var value = _pricing.GetEstimatedPrice(vehicle);
        if (MetaData(ent).EntityPrototype is { } proto && ListingPrice(ent.Comp, proto.ID) is { } listing)
            value = Math.Min(value, listing);

        args.Price = Math.Floor(value * ent.Comp.SellFraction);
        args.Handled = true;
    }

    /// <summary>What the beacon's catalogue asks for a beacon prototype, if it lists it.</summary>
    public int? ListingPrice(WFOrbitalDropBeaconComponent beacon, EntProtoId beaconProto)
    {
        if (!_proto.TryIndex(beacon.Catalog, out var catalog))
            return null;

        foreach (var listing in catalog.Vehicles)
        {
            if (listing.Vehicle == beaconProto)
                return listing.Price;
        }

        return null;
    }
}
