using Content.Server.Administration.Logs;
using Content.Server.Cargo.Systems;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.VehicleShop;

/// <summary>
/// Vehicle delivery beacons: set down, they count down and open into their vehicle where they lie, square to the
/// ground, if nothing solid is in the way. Priced off their catalogue, so a beacon never sells for more than it cost.
/// </summary>
public sealed class WFVehicleBeaconSystem : EntitySystem
{
    /// <summary>The key handed over with a vehicle the vehicle locks key.</summary>
    private static readonly EntProtoId VehicleKey = "RMCVehicleKey";

    /// <summary>How near the one who set it off has to be to be handed the key.</summary>
    private const float HandOverRange = 12f;

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private VehicleLockSystem _locks = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFVehicleShopSystem _shop = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFVehicleBeaconComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WFVehicleBeaconComponent, PriceCalculationEvent>(OnPrice);
        SubscribeLocalEvent<EstimatedPriceCalculationEvent>(OnEstimatedPrice);
    }

    private void OnUseInHand(Entity<WFVehicleBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (ent.Comp.OpensAt != null)
            return;

        _hands.TryDrop(args.User, ent.Owner);
        Arm(ent, args.User);
        _popup.PopupEntity(Loc.GetString("wf-vehicle-beacon-armed", ("seconds", (int) ent.Comp.Delay.TotalSeconds)), ent, args.User);
    }

    /// <summary>Starts the countdown.</summary>
    public void Arm(Entity<WFVehicleBeaconComponent> ent, EntityUid? user)
    {
        ent.Comp.OpensAt = _timing.CurTime + ent.Comp.Delay;
        ent.Comp.NextBeep = _timing.CurTime;
        ent.Comp.ArmedBy = user;
        _appearance.SetData(ent, WFVehicleBeaconVisuals.Armed, true);
    }

    private void Disarm(Entity<WFVehicleBeaconComponent> ent)
    {
        ent.Comp.OpensAt = null;
        ent.Comp.ArmedBy = null;
        _appearance.SetData(ent, WFVehicleBeaconVisuals.Armed, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFVehicleBeaconComponent>();
        while (query.MoveNext(out var uid, out var beacon))
        {
            if (beacon.OpensAt is not { } opensAt)
                continue;

            if (now >= opensAt)
            {
                TryOpen((uid, beacon));
                continue;
            }

            if (now < beacon.NextBeep)
                continue;

            beacon.NextBeep = now + TimeSpan.FromSeconds(1);
            _audio.PlayPvs(beacon.BeepSound, uid);
            var left = (int) Math.Ceiling((opensAt - now).TotalSeconds);
            if (left <= 3)
                _popup.PopupEntity(Loc.GetString("wf-vehicle-beacon-countdown", ("seconds", left)), uid, PopupType.Medium);
        }
    }

    /// <summary>Opens into the vehicle if it lies loose on a deck with room for it; otherwise says why and stops.</summary>
    private void TryOpen(Entity<WFVehicleBeaconComponent> ent)
    {
        var xform = Transform(ent);
        if (_container.IsEntityInContainer(ent) || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-beacon-no-ground"), ent, PopupType.MediumCaution);
            Disarm(ent);
            return;
        }

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var spot = _map.GridTileToLocal(grid, gridComp, tile);
        var footprint = _shop.Footprint(new[] { ent.Comp.Vehicle });
        if (_shop.FindBlocker(_transform.ToMapCoordinates(spot), _transform.GetWorldRotation(grid), footprint, ent) is { } blocker)
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-beacon-blocked", ("thing", blocker)), ent, PopupType.MediumCaution);
            Disarm(ent);
            return;
        }

        var vehicle = Spawn(ent.Comp.Vehicle, spot);
        _transform.SetLocalRotation(vehicle, Angle.Zero);

        EntityUid? key = null;
        if (HasComp<RMCVehicleComponent>(vehicle))
        {
            key = Spawn(VehicleKey, spot);
            _locks.BindKey((key.Value, EnsureComp<VehicleKeyComponent>(key.Value)), _locks.EnsureVehicleKeyId(vehicle), vehicle);
        }
        else if (ent.Comp.Key is { } keyProto)
        {
            key = Spawn(keyProto, spot);
        }

        if (key != null && ent.Comp.ArmedBy is { } user && !TerminatingOrDeleted(user)
            && _transform.InRange(Transform(user).Coordinates, spot, HandOverRange))
            _hands.PickupOrDrop(user, key.Value);

        _audio.PlayPvs(ent.Comp.OpenSound, vehicle);
        _popup.PopupEntity(Loc.GetString("wf-vehicle-beacon-opened", ("vehicle", vehicle)), vehicle, PopupType.Medium);
        _adminLog.Add(LogType.EntitySpawn, LogImpact.Low,
            $"{ToPrettyString(ent):beacon} armed by {ToPrettyString(ent.Comp.ArmedBy):actor} opened into {ToPrettyString(vehicle)}");
        QueueDel(ent);
    }

    /// <summary>An unopened beacon sells as the vehicle would at a terminal's pad.</summary>
    private void OnPrice(Entity<WFVehicleBeaconComponent> ent, ref PriceCalculationEvent args)
    {
        if (args.Handled || !_proto.TryIndex(ent.Comp.Vehicle, out var vehicle))
            return;

        var value = _pricing.GetEstimatedPrice(vehicle);
        if (ListingPrice(ent.Comp) is { } listing)
            value = Math.Min(value, listing);

        args.Price = Math.Floor(value * ent.Comp.SellFraction);
        args.Handled = true;
    }

    /// <summary>A beacon's stock price, what a trader sells it for, is its catalogue listing.</summary>
    private void OnEstimatedPrice(ref EstimatedPriceCalculationEvent args)
    {
        if (args.Handled || !args.Prototype.TryGetComponent<WFVehicleBeaconComponent>(out var beacon, EntityManager.ComponentFactory))
            return;

        if (ListingPrice(beacon) is not { } listing)
            return;

        args.Price = listing;
        args.Handled = true;
    }

    /// <summary>What the beacon's catalogue asks for its vehicle, if it lists it.</summary>
    public int? ListingPrice(WFVehicleBeaconComponent beacon)
    {
        if (!_proto.TryIndex(beacon.Catalog, out var catalog))
            return null;

        foreach (var listing in catalog.Vehicles)
        {
            if (listing.Vehicle == beacon.Vehicle)
                return listing.Price;
        }

        return null;
    }
}
