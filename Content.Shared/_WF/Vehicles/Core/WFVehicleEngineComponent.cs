using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Ignition, fuel and braking state shared with the driver display and predicted mover.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFVehicleEngineComponent : Component
{
    public const string IgnitionContainer = "wf-vehicle-ignition";

    /// <summary>The physical key held in the ignition slot.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? IgnitionKey;

    /// <summary>Whether the combustion engine is supplying propulsion and alternator power.</summary>
    [DataField, AutoNetworkedField]
    public bool Running;

    /// <summary>The ignition accessory circuit, independent of whether the engine is running.</summary>
    [DataField, AutoNetworkedField]
    public bool Electrics;

    /// <summary>Stored electrical energy in joules.</summary>
    [DataField, AutoNetworkedField]
    public float BatteryCharge = 120000f;

    /// <summary>Maximum battery energy in joules.</summary>
    [DataField, AutoNetworkedField]
    public float BatteryCapacity = 120000f;

    /// <summary>Electrical output in watts while the engine runs.</summary>
    [DataField]
    public float AlternatorPower = 1500f;

    /// <summary>Baseline consumption of enabled vehicle electronics in watts.</summary>
    [DataField]
    public float StandbyPower = 80f;

    /// <summary>Energy consumed by ignition and reserved from accessory discharge.</summary>
    [DataField]
    public float StarterEnergy = 2000f;

    /// <summary>Whether the battery can supply switched electrical consumers.</summary>
    public bool Powered => Electrics && (Running || BatteryCharge > StarterEnergy);

    /// <summary>Fuel currently stored, measured in reagent units.</summary>
    [DataField, AutoNetworkedField]
    public float Fuel = 100f;

    /// <summary>Maximum fuel quantity in reagent units.</summary>
    [DataField, AutoNetworkedField]
    public float Capacity = 100f;

    /// <summary>Fuel units consumed per second at idle.</summary>
    [DataField]
    public float IdleConsumption = 0.015f;

    /// <summary>Additional fuel units per second at full driving speed.</summary>
    [DataField]
    public float DrivingConsumption = 0.08f;

    /// <summary>Whether the parking brake suppresses throttle and applies full deceleration.</summary>
    [DataField, AutoNetworkedField]
    public bool ParkingBrake;
}

/// <summary>A driver requests an ignition or parking brake change; the server checks their seat.</summary>
[Serializable, NetSerializable]
public sealed class WFVehicleControlRequest : EntityEventArgs
{
    public NetEntity Vehicle;
    public bool Brake;
    public bool Accessories;
    public bool EjectKey;

    /// <summary>The key: in from anywhere on the driver with the accessories on, or out to hand with everything off.</summary>
    public bool Ignition;

    /// <summary>The start/stop button: cranks the engine with the key in, or stops it.</summary>
    public bool StartStop;

    public WFVehicleControlRequest(NetEntity vehicle, bool brake, bool accessories = false, bool ejectKey = false)
    {
        Vehicle = vehicle;
        Brake = brake;
        Accessories = accessories;
        EjectKey = ejectKey;
    }
}
