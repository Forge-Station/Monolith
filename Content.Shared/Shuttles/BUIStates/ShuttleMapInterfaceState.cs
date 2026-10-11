using Content.Shared._WF.Shuttles; // WOLFGATE(Shuttles)
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

    /// <summary>
    /// Whether the ship has an FTL drive that can jump.
    /// </summary>
    public readonly WFFtlDriveStatus FtlDrive; // WOLFGATE(Shuttles): the FTL page warns before a jump is tried

    public ShuttleMapInterfaceState(
        FTLState ftlState,
        StartEndTime ftlTime,
        List<ShuttleBeaconObject> destinations,
        List<ShuttleExclusionObject> exclusions, // WOLFGATE(Shuttles): the drive status is a required argument
        WFFtlDriveStatus ftlDrive)
    {
        FTLState = ftlState;
        FTLTime = ftlTime;
        Destinations = destinations;
        Exclusions = exclusions;
        FtlDrive = ftlDrive; // WOLFGATE(Shuttles)
    }
}
