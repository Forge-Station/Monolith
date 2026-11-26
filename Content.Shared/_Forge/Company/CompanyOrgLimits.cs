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

    /// <summary>Company safe grid. Stored per company so a later upgrade can raise it.</summary>
    public const int VaultGrid = 3;
    public const int VaultGridMax = 12;

    public const int WarCost = 5_000_000;
    public const int AllianceCost = 2_500_000;
    public const int BreakAllianceCost = 10_000;

    /// <summary>While at war, this much of the treasury cannot be spent. Personal withdrawals are blocked entirely.</summary>
    public const int WarTreasuryReserve = 1_000_000;

    /// <summary>Peace ransom, set by the side that refuses unpaid peace. Taken from the treasury of the side that accepts defeat.</summary>
    public const int MaxPeaceRansomPercent = 50;
    public const int MaxRelationStatementLength = 180;

    /// <summary>Share of a company-funded ship's sale price that returns to the treasury.</summary>
    public const int ShipSaleRefundPercent = 40;

    /// <summary>Market contract: this percent of the reward is created for the member. The rest goes to the treasury.</summary>
    public const int MarketMemberPercent = 70;

    /// <summary>Funded contract: this percent of the escrow is paid to the member. The rest returns to the treasury.</summary>
    public const int FundedMemberPercent = 80;

    public const int MaxJobDescriptionLength = 120;
    public const int MaxOpenJobsPerCompany = 3;
    public const int MinJobCredit = 1_000;
    public const int MaxJobItemCount = 100;

    public const int MaxDoorsPerSetMessage = 64;
    public const float DoorConfigureCooldownSeconds = 1f;
}
