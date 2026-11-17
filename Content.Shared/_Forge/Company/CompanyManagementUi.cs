using Content.Shared._Mono.Company;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Company;

[Serializable, NetSerializable]
public sealed class CompanyManagementUiState : BoundUserInterfaceState
{
    public bool HasCompany;
    public ProtoId<CompanyPrototype>? CompanyId;
    public string CompanyName = string.Empty;
    public bool IsOwner;
    public string LeaderName = string.Empty;
    public CompanyPermission Permissions;
    public CompanyAccessTier AccessTier;
    public string Bulletin = string.Empty;
    public int BankBalance;
    public int PersonalBalance;
    public int MemberCount;
    public List<CompanyRelationData> Relations = new();
    public List<CompanyMemberData> Roster = new();
    public List<CompanyLogData> Logs = new();
    public List<CompanyInvitationData> Invitations = new();
    public List<CompanyRoleData> Roles = new();
    public List<CompanyPickerEntry> InviteableCompanies = new();
}

public enum CompanyManagementUiAction : byte
{
    SetBulletin,
    BankDeposit,
    BankWithdraw,
    SetRelation,
    ClearRelation,
    InvitePlayer,
    AcceptInvite,
    DeclineInvite,
    CancelInvite,
    KickMember,
    CreateRole,
    UpdateRole,
    DeleteRole,
    AssignRole,
    Refresh,
}

[Serializable, NetSerializable]
public sealed class CompanyManagementUiMessageEvent : CartridgeMessageEvent
{
    public CompanyManagementUiAction Action;
    public string? Text;
    public int Amount;
    public Guid GuidValue;
    public Guid? GuidValue2;
    public string? CompanyId;
    public CompanyPermission Permissions;
    public CompanyAccessTier AccessTier;
    public CompanyRelationKind RelationKind;
    public bool BoolValue;
    public int CharacterSlot;
}
