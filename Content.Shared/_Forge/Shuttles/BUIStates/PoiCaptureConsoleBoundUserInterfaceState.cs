using System.Collections.Generic;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class PoiCaptureConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public bool CaptureInProgress;
    public TimeSpan CaptureStartTime;
    public TimeSpan CaptureEndTime;
    public int CaptureDurationSeconds;
    public string CurrentOwnerCompanyId = "None";
    public string CurrentOwnerFactionId = "None";
    public string AttackerCompanyId = "None";
    public string AttackerFactionId = "None";
    public string LastCapturedByName = "None";
    public NetUserId? CaptureLeaderUserId;
    public bool CanTransfer;
    /// <summary>Game time when a new capture may start; zero if not locked.</summary>
    public TimeSpan RecaptureAvailableTime;
    public List<ForgeIffTransferListEntry> TransferCompanies = new();
}

[Serializable, NetSerializable]
public enum PoiCaptureConsoleUiKey : byte
{
    Key,
}
