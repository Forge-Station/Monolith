using Content.Shared.Medical.SuitSensors;
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

[Serializable, NetSerializable]
public sealed class CrewMonitoringSosSignal
{
    public string Message;
    public TimeSpan Timestamp;

    public CrewMonitoringSosSignal(string message, TimeSpan timestamp)
    {
        Message = message;
        Timestamp = timestamp;
    }
}

public sealed class CrewMonitoringSosEvent : EntityEventArgs
{
    public string Message { get; }

    public CrewMonitoringSosEvent(string message)
    {
        Message = message;
    }
}
