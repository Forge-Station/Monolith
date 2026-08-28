using Content.Shared.Preferences;
using Robust.Shared.Serialization;

namespace Content.Shared._EE.Contractors;

[Serializable, NetSerializable]
public enum PassportUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum PassportConsoleUiKey : byte
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
