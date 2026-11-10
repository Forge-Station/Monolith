using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipShields;

/// <summary>Explains why a field is offline and whether automatic recovery can be timed.</summary>
[Serializable, NetSerializable]
public enum WFShipShieldRecoveryStatus : byte
{
    None,
    Ready,
    Recharging,
    Overloaded,
    RechargingAndOverloaded,
    NoPower,
    Lowered,
    Disabled,
    FtlLocked,
}

/// <summary>Identifies the shield generator's shared control panel.</summary>
[Serializable, NetSerializable]
public enum WFShipShieldUiKey : byte
{
    Key,
}

/// <summary>Requests a manual field enable or disable from an authorized control panel.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldSetEnabledMessage : BoundUserInterfaceMessage
{
    /// <summary>The requested manual operating state.</summary>
    public bool Enabled;

    /// <summary>Creates one manual operating request.</summary>
    public WFShipShieldSetEnabledMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

/// <summary>Publishes the generator's ship-wide shield settings and health.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldGeneratorUiState : BoundUserInterfaceState
{
    /// <summary>The same authoritative shield state shown at the helm.</summary>
    public WFShipShieldShuntState ShieldShunt;

    /// <summary>Creates one generator panel snapshot.</summary>
    public WFShipShieldGeneratorUiState(WFShipShieldShuntState shieldShunt)
    {
        ShieldShunt = shieldShunt;
    }
}

/// <summary>Updates shield controls without rebuilding the helm's navigation snapshot.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldHelmUpdateMessage : BoundUserInterfaceMessage
{
    /// <summary>The current shield allocation and integrity.</summary>
    public WFShipShieldShuntState ShieldShunt;

    /// <summary>Creates one shield-only helm update.</summary>
    public WFShipShieldHelmUpdateMessage(WFShipShieldShuntState shieldShunt)
    {
        ShieldShunt = shieldShunt;
    }
}

/// <summary>Requests a ship-local target allocation from live controls.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldSetShuntMessage : BoundUserInterfaceMessage
{
    /// <summary>Requested ship-local sector direction.</summary>
    public float DirectionRadians;
    /// <summary>Fraction of capacity redirected into the sector.</summary>
    public float Concentration;
    /// <summary>Width of the selected sector.</summary>
    public float ArcRadians;

    /// <summary>Creates one target allocation request.</summary>
    public WFShipShieldSetShuntMessage(float directionRadians, float concentration, float arcRadians)
    {
        DirectionRadians = directionRadians;
        Concentration = concentration;
        ArcRadians = arcRadians;
    }
}

/// <summary>Authoritative allocation and health shared by every helm on the ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipShieldShuntState
{
    /// <summary>The ship has an emitter and accepts allocation settings.</summary>
    public bool Available;
    /// <summary>The ship's installed emitters are manually enabled.</summary>
    public bool Enabled;
    /// <summary>A shield field currently exists.</summary>
    public bool Active;
    /// <summary>The current automatic recovery or waiting condition.</summary>
    public WFShipShieldRecoveryStatus RecoveryStatus;
    /// <summary>Whole seconds until automatic startup, or minus one when recovery cannot be timed.</summary>
    public int RecoverySeconds;
    /// <summary>Runtime specifications of the emitter supplying this snapshot.</summary>
    public WFShipShieldGeneratorStats? Stats;
    /// <summary>Remaining shield capacity.</summary>
    public float Health;
    /// <summary>Current ship-local sector direction.</summary>
    public float DirectionRadians;
    /// <summary>Current redirected capacity fraction.</summary>
    public float Concentration;
    /// <summary>Current selected sector width.</summary>
    public float ArcRadians;

    /// <summary>Requested sector direction during redistribution.</summary>
    public float TargetDirectionRadians;
    /// <summary>Requested redirected capacity fraction.</summary>
    public float TargetConcentration;
    /// <summary>Requested reinforced sector width.</summary>
    public float TargetArcRadians;

    /// <summary>Creates an authoritative snapshot for all helms on the ship.</summary>
    public WFShipShieldShuntState(bool available, bool active, float health, float directionRadians, float concentration, float arcRadians, bool enabled = true)
    {
        Available = available;
        Enabled = enabled;
        Active = active;
        TargetDirectionRadians = directionRadians;
        TargetConcentration = concentration;
        TargetArcRadians = arcRadians;
        Health = health;
        DirectionRadians = directionRadians;
        Concentration = concentration;
        ArcRadians = arcRadians;
    }
}
