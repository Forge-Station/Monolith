using Content.Shared._DV.CartridgeLoader.Cartridges; // DeltaV
using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class LogProbeUiState : BoundUserInterfaceState
{
    /// <summary>
    /// The name of the scanned entity.
    /// </summary>
    public readonly string EntityName;

    /// <summary>
    /// The list of pulled access logs.
    /// </summary>
    public readonly List<PulledAccessLog> PulledLogs;

    /// <summary>
    /// NanoChat data if a NanoChat card was scanned.
    /// </summary>
    public readonly NanoChatData? NanoChatData;

    public LogProbeUiState( 
        string entityName,
        List<PulledAccessLog> pulledLogs,
        NanoChatData? nanoChatData = null) // DeltaV - NanoChat support
    {
        EntityName = entityName;
        PulledLogs = pulledLogs;
        NanoChatData = nanoChatData;
    }
}

[Serializable, NetSerializable, DataRecord]
public sealed partial class PulledAccessLog
{
    public readonly TimeSpan Time;
    public readonly string Accessor;

    public PulledAccessLog(TimeSpan time, string accessor)
    {
        Time = time;
        Accessor = accessor;
    }
}