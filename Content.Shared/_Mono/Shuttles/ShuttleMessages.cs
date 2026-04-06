using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Shuttles;

/// <summary>
/// Raised on the client when it wishes to travel somewhere via autopilot.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsoleAutopilotPositionMessage : BoundUserInterfaceMessage
{
    public MapCoordinates Coordinates;
    public Angle Angle;
}

/// <summary>
/// Forge-Change - BioScan
/// Raised on the client when it wishes to scan a shuttle for biological threats.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleConsoleBioScanPositionMessage : BoundUserInterfaceMessage
{
    public MapCoordinates Coordinates;
}
