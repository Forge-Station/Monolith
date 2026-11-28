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
    public List<CrewMonitoringSosSignal> SosSignals;

    public CrewMonitoringState(List<SuitSensorStatus> sensors, List<CrewMonitoringSosSignal> sosSignals)
    {
        Sensors = sensors;
        SosSignals = sosSignals;
    }
}

/// Forge-change-Start
[Serializable, NetSerializable]
public sealed class CrewMonitoringSosSignal
{
    public string Name;
    public MobState Status;
    public TimeSpan Timestamp;

    public CrewMonitoringSosSignal(string name, MobState status, TimeSpan timestamp)
    {
        Name = name;
        Status = status;
        Timestamp = timestamp;
    }
}

public sealed class CrewMonitoringSosEvent : EntityEventArgs
{
    public string Name { get; }
    public MobState Status { get; }

    public CrewMonitoringSosEvent(string name, MobState status)
    {
        Name = name;
        Status = status;
    }
}
/// Forge-Change-End
