using Content.Server.Power.Components;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Runs the vehicle electrical bus, alternator, cabin consumers and battery-backed lights.</summary>
public sealed class WFVehicleElectricalSystem : EntitySystem
{
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private VehicleSpotlightSystem _spotlight = default!;
    [Dependency] private CMUEmergencyLightsSystem _emergency = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private readonly Dictionary<EntityUid, float> _loads = new();
    private readonly Dictionary<EntityUid, FeedState> _feeds = new();
    private readonly HashSet<EntityUid> _connected = new();
    private readonly List<EntityUid> _disconnected = new();
    private readonly List<(EntityUid, ApcPowerReceiverComponent)> _cabinReceivers = new();
    private EntityQuery<ApcPowerReceiverComponent> _receiverQuery;

    private sealed class FeedState(bool needsPower, bool disabled)
    {
        public readonly bool NeedsPower = needsPower;
        public bool Disabled = disabled;
        public bool Offline;
    }
    private float _elapsed;

    public override void Initialize()
    {
        base.Initialize();
        _receiverQuery = GetEntityQuery<ApcPowerReceiverComponent>();
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 0.25f)
            return;
        var elapsed = _elapsed;
        _elapsed = 0f;
        _loads.Clear();
        _connected.Clear();
        // Only cabin maps: walking every receiver in the world cost the most where there are no vehicles at all.
        var cabins = EntityQueryEnumerator<VehicleInteriorLinkComponent>();
        while (cabins.MoveNext(out var map, out var link))
        {
            if (!TryComp(link.Vehicle, out WFVehicleEngineComponent? engine))
                continue;

            _cabinReceivers.Clear();
            CollectReceivers(map);
            foreach (var (uid, receiver) in _cabinReceivers)
                Feed(uid, receiver, link, engine);
        }

        UpdateDisconnected();
        UpdateEngines(elapsed);
    }

    /// <summary>Every power receiver under an entity in the transform tree, contained ones included.</summary>
    private void CollectReceivers(EntityUid parent)
    {
        var children = Transform(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_receiverQuery.TryComp(child, out var receiver))
                _cabinReceivers.Add((child, receiver));
            CollectReceivers(child);
        }
    }

    private void Feed(EntityUid uid, ApcPowerReceiverComponent receiver, VehicleInteriorLinkComponent link, WFVehicleEngineComponent engine)
    {
        _connected.Add(uid);
        if (!_feeds.TryGetValue(uid, out var feed))
            _feeds.Add(uid, feed = new FeedState(receiver.NeedsPower, receiver.PowerDisabled));
        if (!feed.Offline)
            feed.Disabled = receiver.PowerDisabled;
        _power.SetNeedsPower(uid, false, receiver);
        _power.SetPowerDisabled(uid, !engine.Powered || feed.Disabled, receiver);
        feed.Offline = !engine.Powered;

        if (!receiver.PowerDisabled)
            _loads[link.Vehicle] = _loads.GetValueOrDefault(link.Vehicle) + MathF.Max(0f, receiver.Load);
    }

    /// <summary>Portable consumers return to their original supply when taken out of the cabin.</summary>
    private void UpdateDisconnected()
    {
        _disconnected.Clear();
        foreach (var (uid, feed) in _feeds)
        {
            if (_connected.Contains(uid))
                continue;
            if (TryComp(uid, out ApcPowerReceiverComponent? receiver))
            {
                _power.SetNeedsPower(uid, feed.NeedsPower, receiver);
                if (feed.Offline)
                    _power.SetPowerDisabled(uid, feed.Disabled, receiver);
            }
            _disconnected.Add(uid);
        }
        foreach (var uid in _disconnected)
            _feeds.Remove(uid);
    }

    private void UpdateEngines(float elapsed)
    {
        var query = EntityQueryEnumerator<WFVehicleEngineComponent>();
        while (query.MoveNext(out var uid, out var engine))
        {
            var load = engine.Powered ? engine.StandbyPower + _loads.GetValueOrDefault(uid) : 0f;
            if (engine.Powered && TryComp(uid, out VehicleSpotlightComponent? lights) && lights.Enabled)
                load += 180f;
            if (TryComp(uid, out CMUEmergencyLightsComponent? emergency) && emergency.Mode > 0)
            {
                if (engine.Powered)
                    load += 100f;
                else
                    _emergency.SetMode((uid, emergency), 0);
            }
            // Shed accessories before consuming the starter reserve; only the alternator adds charge.
            var floor = engine.Running ? 0f : MathF.Min(engine.BatteryCharge, engine.StarterEnergy);
            var charge = Math.Clamp(engine.BatteryCharge + elapsed *
                ((engine.Running ? engine.AlternatorPower : 0f) - load), floor, engine.BatteryCapacity);
            if (charge != engine.BatteryCharge)
            {
                engine.BatteryCharge = charge;
                Dirty(uid, engine);
            }
            _spotlight.RefreshPower(uid);
            if (TryComp(uid, out VehicleInteriorComponent? interior) && interior.MapId != MapId.Nullspace)
                _map.SetAmbientLight(interior.MapId, engine.Powered ? Color.FromHex("#8A8A92") : Color.FromHex("#121218"));
        }
    }
}
