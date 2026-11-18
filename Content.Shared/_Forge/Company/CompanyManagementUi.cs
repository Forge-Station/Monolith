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
    /// <summary>Leader opened free joining; otherwise invite / admin whitelist only.</summary>
    public bool RecruitingOpen;
    public CompanyPermission Permissions;
    public CompanyAccessTier AccessTier;
    public string Bulletin = string.Empty;
    public int BankBalance;
    public int SpendableBalance;
    public bool AtWar;
    public int WarReserve;
    public int PersonalBalance;
    public int MemberCount;
    public List<CompanyRelationData> Relations = new();
    public List<CompanyMemberData> Roster = new();
    public List<CompanyLogData> Logs = new();
    public List<CompanyInvitationData> Invitations = new();
    public List<CompanyRoleData> Roles = new();
    public List<CompanyPickerEntry> InviteableCompanies = new();
    public bool CanFoundOutpost;
    public string OutpostName = string.Empty;
    public string BlueprintText = string.Empty;
    public int WarCost;
    public int AllianceCost;
    public int BreakAllianceCost;
    public bool AutoJoinAllyWars = true;
    public List<CompanySpendEntry> Spends = new();
    public string? ActiveContractId;
    public string ActiveContractTitle = string.Empty;
    public string ActiveContractItem = string.Empty;
    public int ActiveContractCount;
    public int ActiveContractMemberPay;
    public int ActiveContractTreasuryPay;
    public bool ActiveContractFunded;
    public long ResearchPoints;
    public int VaultWidth = CompanyOrgLimits.VaultGrid;
    public int VaultHeight = CompanyOrgLimits.VaultGrid;
    public List<CompanyJobData> Jobs = new();
}

[Serializable, NetSerializable]
public sealed class CompanyJobData
{
    public string Id = string.Empty;
    public string PosterId = string.Empty;
    public string PosterName = string.Empty;
    public string Description = string.Empty;
    public int RewardKind;
    public int RewardAmount;
    public string RewardProto = string.Empty;
    public bool Ours;
    public bool TakenByUs;
}

public static class CompanyJobReward
{
    public const int Credits = 0;
    public const int Items = 1;
}

[Serializable, NetSerializable]
public sealed class CompanySpendEntry
{
    public string Id = string.Empty;
    public string Kind = string.Empty;
    public string Title = string.Empty;
    public string Purpose = string.Empty;
    public int Cost;
    public int Arg1;
    public int Arg2;
    public bool Owned;
    public bool Discounted;
    public bool CanBuy;
    public bool CanFund;
    public string Detail = string.Empty;
}

public static class CompanySpendKind
{
    public const string Project = "project";
    public const string Supply = "supply";
    public const string Ship = "ship";
    public const string Contract = "contract";
    public const string Research = "research";
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
    FoundOutpost,
    BuyProject,
    BuySupply,
    BuyShip,
    AcceptContract,
    FundContract,
    TurnInContract,
    CancelContract,
    OfferFreePeace,
    SetPeaceRansom,
    AcceptPeaceRansom,
    OfferAlliance,
    WithdrawAlliance,
    SetAutoJoinAllyWars,
    OpenVault,
    PostCreditJob,
    PostItemJob,
    AcceptJob,
    AbandonJob,
    CancelJob,
    SetRecruitingOpen,
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
