namespace Content.Shared._Forge.Company;

public readonly record struct CompanyRolePreset(
    string Name,
    CompanyPermission Permissions,
    CompanyAccessTier Tier,
    bool Default,
    bool Leader);

/// <summary>
/// Roles created for every company. Names are locale ids.
/// </summary>
public static class CompanyRolePresets
{
    public const string Leader = "company-role-leader";
    public const string Officer = "company-role-officer";
    public const string Treasurer = "company-role-treasurer";
    public const string Member = "company-role-member";

    public static readonly CompanyRolePreset[] All =
    [
        new(
            Leader,
            CompanyPermission.All,
            CompanyAccessTier.High,
            Default: false,
            Leader: true),
        new(
            Officer,
            CompanyPermission.EditBulletin
            | CompanyPermission.Invite
            | CompanyPermission.Kick
            | CompanyPermission.AssignAccess
            | CompanyPermission.BankDeposit
            | CompanyPermission.DeclareWar
            | CompanyPermission.DeclareAlliance
            | CompanyPermission.ViewLogs
            | CompanyPermission.ConfigureDoors,
            CompanyAccessTier.Medium,
            Default: false,
            Leader: false),
        new(
            Treasurer,
            CompanyPermission.BankDeposit
            | CompanyPermission.BankWithdraw
            | CompanyPermission.ViewLogs
            | CompanyPermission.Warehouse
            | CompanyPermission.OrderSupplies
            | CompanyPermission.OrderShips
            |             CompanyPermission.FundProjects
            | CompanyPermission.PostContracts
            | CompanyPermission.Vault,
            CompanyAccessTier.Medium,
            Default: false,
            Leader: false),
        new(
            Member,
            CompanyPermission.MemberDefault | CompanyPermission.Warehouse,
            CompanyAccessTier.Low,
            Default: true,
            Leader: false),
    ];

    public static bool IsPreset(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var preset in All)
        {
            if (preset.Name == name)
                return true;
        }

        return false;
    }
}
