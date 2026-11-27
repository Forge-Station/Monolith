using Content.Shared.DeviceNetwork;
using Content.Shared.Medical.SuitSensors;
using Content.Shared.Medical.CrewMonitoring;

namespace Content.Server.Medical.CrewMonitoring;

/// <summary>
/// Stores suit sensor status received by a crew monitoring console.
/// </summary>
[RegisterComponent]
[Access(typeof(CrewMonitoringConsoleSystem))]
public sealed partial class CrewMonitoringConsoleComponent : Component
{
    /// <summary>
    /// List of all currently connected sensors to this console.
    /// </summary>
    public Dictionary<DeviceAddress, SuitSensorStatus> ConnectedSensors = new();

    /// <summary>
    ///     SOS signals received by this console during the current round.
    /// </summary>
    public List<CrewMonitoringSosSignal> SosSignals = new();

    /// <summary>
    ///     After what time sensor consider to be lost.
    /// </summary>
    [DataField]
    public float SensorTimeout = 10f;
}
