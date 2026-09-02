// Forge-Change-full: passport registry console UI and round-wide issuance tracking.
using Content.Shared.Preferences;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Contractors;

[Serializable, NetSerializable]
public enum PassportConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class PassportConsoleListEntry
{
    public Guid Id;
    public string Pid = string.Empty;
    public string Name = string.Empty;
    public string JobId = string.Empty;
    public string Nationality = string.Empty;
}

[Serializable, NetSerializable]
public sealed class PassportConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly List<PassportConsoleListEntry> Entries;
    public readonly Guid? SelectedId;
    public readonly HumanoidCharacterProfile? SelectedProfile;
    public readonly string SelectedPid;

    public PassportConsoleBoundUserInterfaceState(
        List<PassportConsoleListEntry> entries,
        Guid? selectedId,
        HumanoidCharacterProfile? selectedProfile,
        string selectedPid)
    {
        Entries = entries;
        SelectedId = selectedId;
        SelectedProfile = selectedProfile;
        SelectedPid = selectedPid;
    }
}

[Serializable, NetSerializable]
public sealed class PassportConsoleSelectMessage : BoundUserInterfaceMessage
{
    public readonly Guid Id;

    public PassportConsoleSelectMessage(Guid id)
    {
        Id = id;
    }
}

[Serializable, NetSerializable]
public sealed class PassportConsolePrintMessage : BoundUserInterfaceMessage
{
    public readonly Guid Id;

    public PassportConsolePrintMessage(Guid id)
    {
        Id = id;
    }
}

/// <summary>
/// Raised when a passport is first issued to a player this round (registry reprints do not raise this).
/// </summary>
public sealed class PassportIssuedEvent : EntityEventArgs
{
    public EntityUid Mob;
    public HumanoidCharacterProfile Profile;
    public string? JobId;
    public string Pid;

    public PassportIssuedEvent(EntityUid mob, HumanoidCharacterProfile profile, string? jobId, string pid)
    {
        Mob = mob;
        Profile = profile;
        JobId = jobId;
        Pid = pid;
    }
}
