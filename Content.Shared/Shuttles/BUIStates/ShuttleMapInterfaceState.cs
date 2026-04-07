using Content.Shared.Shuttles.Systems;
using Content.Shared.Shuttles.UI.MapObjects;
using Content.Shared.Timing;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

/// <summary>
/// Handles BUI data for Map screen.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShuttleMapInterfaceState
{
    /// <summary>
    /// The current FTL state.
    /// </summary>
    public readonly FTLState FTLState;

    /// <summary>
    /// When the current FTL state starts and ends.
    /// </summary>
    public StartEndTime FTLTime;

    public List<ShuttleBeaconObject> Destinations;

    public List<ShuttleExclusionObject> Exclusions;

    public StartEndTime BioScanTime; // Forge-Change - BioScan
    public ShuttleBioScanStatus BioScanStatus; // Forge-Change - BioScan
    public bool BioScanAvailable; // Forge-Change - BioScan

    public ShuttleMapInterfaceState(
        FTLState ftlState,
        StartEndTime ftlTime,
        List<ShuttleBeaconObject> destinations,
        List<ShuttleExclusionObject> exclusions,
        StartEndTime bioScanTime, // Forge-Change - BioScan
        ShuttleBioScanStatus bioScanStatus, // Forge-Change - BioScan
        bool bioScanAvailable) // Forge-Change - BioScan
    {
        FTLState = ftlState;
        FTLTime = ftlTime;
        Destinations = destinations;
        Exclusions = exclusions;
        BioScanTime = bioScanTime; // Forge-Change - BioScan
        BioScanStatus = bioScanStatus; // Forge-Change - BioScan
        BioScanAvailable = bioScanAvailable; // Forge-Change - BioScan
    }
}

// Forge-Change-start - BioScan
[Serializable, NetSerializable]
public enum ShuttleBioScanStatus : byte
{
    None,
    InProgress,
    Clean,
    ThreatDetected,
    InvalidTarget,
    TargetTooFar,
    TargetMoving,
    NoAccess,
}
// Forge-Change-end - BioScan
