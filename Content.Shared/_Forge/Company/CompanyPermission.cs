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

    MemberDefault = BankDeposit | ViewLogs,
    All = EditBulletin | Invite | Kick | ManageRoles | AssignAccess
        | BankDeposit | BankWithdraw | DeclareWar | DeclareAlliance | ViewLogs | ConfigureDoors,
}
