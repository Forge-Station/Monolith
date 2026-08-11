using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Company;

[Serializable, NetSerializable]
public sealed class CompanyRoleData
{
    public Guid RoleId;
    public string Name = string.Empty;
    public CompanyPermission Permissions;
    public CompanyAccessTier AccessTier;
    public bool IsDefault;
}

[Serializable, NetSerializable]
public sealed class CompanyMemberData
{
    public Guid PlayerUserId;
    public int CharacterSlot;
    public string PlayerName = string.Empty;
    public bool Owner;
    public Guid? RoleId;
    public string RoleName = string.Empty;
    public CompanyAccessTier AccessTier;
    public bool Online;
    public string? CharacterName;
}

[Serializable, NetSerializable]
public sealed class CompanyRelationData
{
    public ProtoId<CompanyPrototype> OtherCompany;
    public string OtherCompanyName = string.Empty;
    public CompanyRelationKind Kind;
    public string? IconId;
}

[Serializable, NetSerializable]
public sealed class CompanyPickerEntry
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string? IconId;
}

[Serializable, NetSerializable]
public sealed class CompanyInvitationData
{
    public Guid InviteId;
    public ProtoId<CompanyPrototype> Company;
    public string CompanyName = string.Empty;
    public Guid FromUserId;
    public string FromUserName = string.Empty;
    public Guid TargetUserId;
    public string TargetUserName = string.Empty;
    public TimeSpan CreatedAt;
    public bool Incoming;
}

[Serializable, NetSerializable]
public sealed class CompanyLogData
{
    public long Id;
    public CompanyLogType LogType;
    public string Message = string.Empty;
    public TimeSpan Timestamp;
    public string? ActorName;
}
