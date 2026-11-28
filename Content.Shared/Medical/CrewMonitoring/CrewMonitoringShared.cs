using Content.Shared.Medical.SuitSensors;
using Content.Shared.Mobs;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.CrewMonitoring;

[Serializable, NetSerializable]
public enum CrewMonitoringUIKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class CrewMonitoringState : BoundUserInterfaceState
{
    public List<SuitSensorStatus> Sensors;
    /// Forge-Change-Start
    public List<CrewMonitoringSosSignal> SosSignals;

    public CrewMonitoringState(List<SuitSensorStatus> sensors, List<CrewMonitoringSosSignal> sosSignals)
    {
        Sensors = sensors;
        SosSignals = sosSignals;
    }
    /// Forge-Change-End
}

/// Forge-change-Start
[Serializable, NetSerializable]
public sealed class CrewMonitoringSosSignal
{
    public string Name;
    public MobState Status;
    public TimeSpan Timestamp;
    public int X;
    public int Y;

    public CrewMonitoringSosSignal(string name, MobState status, TimeSpan timestamp, int x, int y)
    {
        Name = name;
        Status = status;
        Timestamp = timestamp;
        X = x;
        Y = y;
    }
}

public sealed class CrewMonitoringSosEvent : EntityEventArgs
{
    public string Name { get; }
    public MobState Status { get; }
    public int X { get; }
    public int Y { get; }

    public CrewMonitoringSosEvent(string name, MobState status, int x, int y)
    {
        Name = name;
        Status = status;
        X = x;
        Y = y;
    }
}
/// Forge-Change-End
