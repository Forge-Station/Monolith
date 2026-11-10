using System.Numerics;
using Content.Shared.DeviceNetwork.Systems;
using Content.Server.Medical.CrewMonitoring;
using Content.Server.Medical.SuitSensors;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Betrayal;

/// <summary>
/// A crewman who turns into a Remade keeps reading alive on the crew monitor, with their name and where the Remade walks. A suit
/// sensor only reports the body wearing it, so on turning the reading is copied and the dropped suit's sensor keeps sending it
/// through the device network, as if it were still worn. It stops when the Remade dies or someone puts the suit on.
/// </summary>
public sealed partial class WFSensorGhostSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _network = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SingletonDeviceNetServerSystem _servers = default!;
    [Dependency] private SuitSensorSystem _sensors = default!;

    private const string SuitSlot = "jumpsuit";
    private static readonly TimeSpan Rate = TimeSpan.FromSeconds(2);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeRisenEvent>(OnRisen);
    }

    private void OnRisen(ref WFRemadeRisenEvent ev)
    {
        if (!_inventory.TryGetSlotEntity(ev.Corpse, SuitSlot, out var suit) || !HasComp<SuitSensorComponent>(suit))
            return;

        // Null if the sensor is off, in which case the crew monitor never saw them and still won't.
        if (_sensors.GetSensorState(suit.Value) is not { } reading)
            return;

        var ghost = EnsureComp<WFSensorGhostComponent>(ev.Remade);
        ghost.Sensor = suit.Value;
        ghost.Snapshot = reading;
        ghost.HasCoordinates = reading.Coordinates != null;
        ghost.HasVitals = reading.TotalDamage != null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFSensorGhostComponent>();
        while (query.MoveNext(out var uid, out var ghost))
        {
            if (now < ghost.Next)
                continue;

            ghost.Next = now + Rate;
            if (TerminatingOrDeleted(ghost.Sensor) || _sensors.GetSensorState(ghost.Sensor) != null)
            {
                RemCompDeferred<WFSensorGhostComponent>(uid);
                continue;
            }

            Send((uid, ghost));
            if (_mobState.IsDead(uid))
                RemCompDeferred<WFSensorGhostComponent>(uid);
        }
    }

    /// <summary>The reading the crew monitor gets for a Remade that was a crewman: their name, alive, and where it stands.</summary>
    public SuitSensorStatus Reading(Entity<WFSensorGhostComponent> ent)
    {
        var snap = ent.Comp.Snapshot;
        var status = new SuitSensorStatus(GetNetEntity(ent.Comp.Sensor), snap.Name, snap.Job, snap.JobIcon, snap.JobDepartments, snap.LocationName)
        {
            IsAlive = !_mobState.IsDead(ent),
            WFCompanyRank = snap.WFCompanyRank,
        };

        if (ent.Comp.HasVitals)
        {
            status.TotalDamage = 0;
            status.TotalDamageThreshold = snap.TotalDamageThreshold;
        }

        if (ent.Comp.HasCoordinates)
        {
            var xform = Transform(ent);
            var world = _transform.GetWorldPosition(xform);
            if (xform.GridUid is { } grid)
            {
                status.Coordinates = GetNetCoordinates(new EntityCoordinates(grid, Vector2.Transform(world, _transform.GetInvWorldMatrix(grid))));
                status.LocationName = Name(grid);
            }
            else if (xform.MapUid is { } map)
            {
                status.Coordinates = GetNetCoordinates(new EntityCoordinates(map, world));
                status.LocationName = Loc.GetString("suit-sensor-location-space");
            }
        }

        return status;
    }

    private void Send(Entity<WFSensorGhostComponent> ent)
    {
        if (!TryComp<DeviceNetworkComponent>(ent.Comp.Sensor, out var device) || device.TransmitFrequency is null
            || !_servers.TryGetActiveServerAddress<CrewMonitoringServerComponent>(Transform(ent).MapID, out var address)
            || !_network.IsAddressPresent(device.DeviceNetId, address))
        {
            return;
        }

        var payload = new SuitSensorStatusPayload { Data = Reading(ent) };
        _network.SendPacket((ent.Comp.Sensor, device), address, ref payload);
        ent.Comp.Sent++;
    }
}
