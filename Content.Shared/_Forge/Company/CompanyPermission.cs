namespace Content.Shared._Forge.Company;

/// <summary>
/// Permission flags for company roles. Leaders (owners) always have all permissions.
/// </summary>
[Flags]
public enum CompanyPermission : long
{
    None = 0,
    EditBulletin = 1L << 0,
    Invite = 1L << 1,
    Kick = 1L << 2,
    ManageRoles = 1L << 3,
    AssignAccess = 1L << 4,
    BankDeposit = 1L << 5,
    BankWithdraw = 1L << 6,
    DeclareWar = 1L << 7,
    DeclareAlliance = 1L << 8,
    ViewLogs = 1L << 9,
    ConfigureDoors = 1L << 10,
    /// <summary>
    /// Use the company warehouse at an outpost. Personal stock does not need this flag.
    /// </summary>
    Warehouse = 1L << 11,
    /// <summary>Order supply crates paid by the company treasury.</summary>
    OrderSupplies = 1L << 12,
    /// <summary>Buy company ship vouchers paid by the treasury.</summary>
    OrderShips = 1L << 13,
    /// <summary>Purchase one-time company projects.</summary>
    FundProjects = 1L << 14,
    /// <summary>Post a treasury-funded contract.</summary>
    PostContracts = 1L << 15,
    /// <summary>
    /// Put items into the company safe and take them out.
    /// Without this flag a member can still open the safe and read papers.
    /// </summary>
    Vault = 1L << 16,

    MemberDefault = BankDeposit | ViewLogs,
    All = EditBulletin | Invite | Kick | ManageRoles | AssignAccess
        | BankDeposit | BankWithdraw | DeclareWar | DeclareAlliance | ViewLogs | ConfigureDoors
        | Warehouse | OrderSupplies | OrderShips | FundProjects | PostContracts | Vault,
}
