using Content.Shared.Access;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Access;

[Serializable, NetSerializable]
public sealed class JobPresetIdCardConsoleApplyMessage : BoundUserInterfaceMessage
{
    public readonly ProtoId<JobPrototype> JobPrototype;

    public JobPresetIdCardConsoleApplyMessage(ProtoId<JobPrototype> jobPrototype)
    {
        JobPrototype = jobPrototype;
    }
}

[Serializable, NetSerializable]
public sealed class JobPresetIdCardConsoleCreateInjectorMessage : BoundUserInterfaceMessage
{
    public readonly ProtoId<JobPrototype> JobPrototype;

    public JobPresetIdCardConsoleCreateInjectorMessage(ProtoId<JobPrototype> jobPrototype)
    {
        JobPrototype = jobPrototype;
    }
}

[Serializable, NetSerializable]
public sealed class JobPresetIdCardConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly string PrivilegedIdName;
    public readonly bool IsPrivilegedIdPresent;
    public readonly bool IsPrivilegedIdAuthorized;
    public readonly bool IsTargetIdPresent;
    public readonly string TargetIdName;
    public readonly List<ProtoId<AccessLevelPrototype>>? TargetIdAccessList;
    public readonly List<ProtoId<AccessLevelPrototype>>? AllowedModifyAccessList;
    public readonly ProtoId<JobPrototype> TargetIdJobPrototype;

    public JobPresetIdCardConsoleBoundUserInterfaceState(
        bool isPrivilegedIdPresent,
        bool isPrivilegedIdAuthorized,
        bool isTargetIdPresent,
        List<ProtoId<AccessLevelPrototype>>? targetIdAccessList,
        List<ProtoId<AccessLevelPrototype>>? allowedModifyAccessList,
        ProtoId<JobPrototype> targetIdJobPrototype,
        string privilegedIdName,
        string targetIdName)
    {
        IsPrivilegedIdPresent = isPrivilegedIdPresent;
        IsPrivilegedIdAuthorized = isPrivilegedIdAuthorized;
        IsTargetIdPresent = isTargetIdPresent;
        TargetIdAccessList = targetIdAccessList;
        AllowedModifyAccessList = allowedModifyAccessList;
        TargetIdJobPrototype = targetIdJobPrototype;
        PrivilegedIdName = privilegedIdName;
        TargetIdName = targetIdName;
    }
}

[Serializable, NetSerializable]
public enum JobPresetIdCardConsoleUiKey : byte
{
    Key,
}
