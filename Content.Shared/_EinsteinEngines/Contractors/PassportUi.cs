using Content.Shared.Preferences;
using Robust.Shared.Serialization;

namespace Content.Shared._EE.Contractors;

[Serializable, NetSerializable]
public enum PassportUiKey : byte
{
    Key,
}

/// <summary>
/// Bound UI state for the in-hand passport window.
/// The profile is used on the client to spawn a photo dummy.
/// PID is generated server-side so it stays consistent with examine text.
/// </summary>
[Serializable, NetSerializable]
public sealed class PassportBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly HumanoidCharacterProfile? Profile;
    public readonly string Pid;

    public PassportBoundUserInterfaceState(HumanoidCharacterProfile? profile, string pid)
    {
        Profile = profile;
        Pid = pid;
    }
}
