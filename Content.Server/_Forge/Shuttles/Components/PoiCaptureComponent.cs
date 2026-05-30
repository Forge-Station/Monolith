using Robust.Shared.Network;

namespace Content.Server.Shuttles.Components;

[RegisterComponent]
public sealed partial class PoiCaptureComponent : Component
{
    [DataField]
    public string CurrentOwnerCompanyId = "None";

    [DataField]
    public string CurrentOwnerFactionId = "None";

    [DataField]
    public bool CaptureInProgress;

    [DataField]
    public TimeSpan CaptureStartTime;

    [DataField]
    public TimeSpan CaptureEndTime;

    [DataField]
    public string AttackerCompanyId = "None";

    [DataField]
    public string AttackerFactionId = "None";

    [DataField]
    public NetUserId? CaptureLeaderUserId;

    [DataField]
    public string AttackerLeaderName = "None";

    [DataField]
    public string LastCapturedByName = "None";

    /// <summary>
    ///     Game time when the last hostile capture completed. Used for recapture cooldown.
    /// </summary>
    [DataField]
    public TimeSpan LastCaptureCompletedTime;
}
