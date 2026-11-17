namespace Content.Shared._Forge.Company;

/// <summary>
/// Hard caps for company organization features to limit grief / DB growth.
/// </summary>
public static class CompanyOrgLimits
{
    public const int MaxLogsPerCompany = 500;
    public const int MaxLogMessageLength = 256;

    public const int MaxPendingInvitesPerCompany = 20;
    public const int InviteCooldownSeconds = 15;

    public const int MaxBulletinLength = 1024;

    public const int MaxRolesPerCompany = 24;
    public const int MaxRoleNameLength = 32;

    public const int RelationCooldownSeconds = 120;

    public const int MaxBankBalance = 2_000_000_000;
    public const int MaxBankTransaction = 100_000_000;

    public const int MaxMembersPerCompany = 64;

    public const int MaxDoorsPerSetMessage = 64;
    public const float DoorConfigureCooldownSeconds = 1f;
}
