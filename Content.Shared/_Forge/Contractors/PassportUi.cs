// Forge-Change-full: in-hand passport bound UI state.
using Content.Shared.Preferences;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Contractors;

[Serializable, NetSerializable]
public enum PassportUiKey : byte
{
    Key,
}

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
