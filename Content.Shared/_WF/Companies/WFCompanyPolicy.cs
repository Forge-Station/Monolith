using System.Globalization;

namespace Content.Shared._WF.Companies;

/// <summary>What a company rank may do. The owner holds all of them.</summary>
[Flags]
public enum WFCompanyPermissions
{
    None = 0,
    Invite = 1 << 0,
    Kick = 1 << 1,
    SetRanks = 1 << 2,
    EditRanks = 1 << 3,
    Withdraw = 1 << 4,
    FieldCrew = 1 << 5,
    Launch = 1 << 6,
    Design = 1 << 7,
    EditDetails = 1 << 8,
    All = Invite | Kick | SetRanks | EditRanks | Withdraw | FieldCrew | Launch | Design | EditDetails,
}

/// <summary>Kinds of credit movement recorded in a company's ledger.</summary>
public enum WFCompanyLedgerKind : byte
{
    Deposit, Withdraw, Wage, Cut, LicenceBuy, LicenceUpkeep, RegistryFee, LaunchFee, AdminAdjust, AdminRefund, Payment, Loadout,
}

/// <summary>Limits and validation shared by the company server and window.</summary>
public static class WFCompanyPolicy
{
    public const int MaximumNameLength = 32;
    public const int MinimumTagLength = 2;
    public const int MaximumTagLength = 5;
    public const int MaximumDescriptionLength = 1024;
    public const int MaximumRankTitleLength = 32;
    public const int MaximumRanks = 12;
    public const int MaximumMembers = 200;
    public const int MaximumCut = 50;
    public const int DefaultCut = 10;
    public const int MaximumWage = 1_000_000;
    public const int LedgerPage = 50;

    /// <summary>Logos are square, this many pixels a side.</summary>
    public const int LogoSize = 32;

    /// <summary>A logo's raw RGBA size in bytes.</summary>
    public const int LogoBytes = LogoSize * LogoSize * 4;

    /// <summary>Rank job icons match the HUD's job icons, this many pixels a side.</summary>
    public const int IconSize = 8;

    /// <summary>A rank icon's raw RGBA size in bytes.</summary>
    public const int IconBytes = IconSize * IconSize * 4;

    /// <summary>Members a company needs before it can buy or launch fleet licences.</summary>
    public const int MinimumFleetMembers = 3;

    /// <summary>The longest message on an application to join.</summary>
    public const int MaximumApplicationLength = 500;

    /// <summary>The most loadouts a rank can be issued.</summary>
    public const int MaximumRankLoadouts = 20;

    /// <summary>The longest note on a direct payment.</summary>
    public const int MaximumPaymentNote = 64;

    /// <summary>Ship access tags whose designs only companies can license, on top of the civilian shipyard.</summary>
    public static readonly string[] CompanyVesselAccess = { "Mercenary", "Mieyo", "MieyoSecurity" };

    /// <summary>The job with no role of its own, shown as Vagrant; a member spawning in it shows their rank instead.</summary>
    public const string OverflowJob = "Contractor";

    /// <summary>The most fleet licences a company may hold.</summary>
    public const int MaximumLicences = 3;

    /// <summary>A licence costs this many times the design's shipyard price.</summary>
    public const int LicenceMultiplier = 200;

    /// <summary>The least any licence costs.</summary>
    public const long LicenceMinimum = 20_000_000;

    /// <summary>Registry fee per launch as a percent of the shipyard price, by how many company ships are already out.</summary>
    public static readonly int[] RegistryFeePercents = { 5, 15, 30 };

    /// <summary>What a fleet licence for a design of this shipyard price costs.</summary>
    public static long LicencePrice(int shipyardPrice) => Math.Max(LicenceMinimum, (long) shipyardPrice * LicenceMultiplier);

    /// <summary>The registry fee for launching another ship when <paramref name="launched"/> are already out this round.</summary>
    public static int RegistryFee(int shipyardPrice, int launched) =>
        (int) ((long) shipyardPrice * RegistryFeePercents[Math.Clamp(launched, 0, RegistryFeePercents.Length - 1)] / 100);

    /// <summary>Played seconds per wage.</summary>
    public const int PayrollHourSeconds = 3600;

    /// <summary>The percent of due wages the account can pay: 100 when it covers them all, otherwise the same share for everyone.</summary>
    public static int PayrollShare(long balance, long due) =>
        due <= 0 || balance >= due ? 100 : (int) Math.Max(0, balance * 100 / due);

    /// <summary>The company's cut of an amount, rounded down.</summary>
    public static int CutOf(int amount, int cut) =>
        amount <= 0 || cut <= 0 ? 0 : (int) ((long) amount * Math.Min(cut, MaximumCut) / 100);

    /// <summary>Ranks a new company starts with, highest first.</summary>
    public static readonly (string Title, WFCompanyPermissions Permissions, string BaseJob)[] DefaultRanks =
    {
        ("Officer", WFCompanyPermissions.Invite | WFCompanyPermissions.Kick | WFCompanyPermissions.SetRanks |
                    WFCompanyPermissions.FieldCrew | WFCompanyPermissions.Launch, "Contractor"),
        ("Member", WFCompanyPermissions.FieldCrew, "Contractor"),
        ("Recruit", WFCompanyPermissions.None, "Contractor"),
    };

    /// <summary>A plain name of letters, digits and simple punctuation, with single spaces.</summary>
    public static bool TryNormalizeName(string? input, out string name) =>
        TryNormalizeLabel(input, MaximumNameLength, out name) && name.Length > 0;

    /// <summary>A rank title, using the same rules as company names.</summary>
    public static bool TryNormalizeRankTitle(string? input, out string title) =>
        TryNormalizeLabel(input, MaximumRankTitleLength, out title) && title.Length > 0;

    /// <summary>Two to five upper-case letters or digits.</summary>
    public static bool TryNormalizeTag(string? input, out string tag)
    {
        tag = string.Empty;
        if (input == null)
            return false;
        var trimmed = input.Trim().ToUpperInvariant();
        if (trimmed.Length is < MinimumTagLength or > MaximumTagLength)
            return false;
        foreach (var character in trimmed)
        {
            if (character is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
                return false;
        }
        tag = trimmed;
        return true;
    }

    /// <summary>Plain text with paragraphs and no control or formatting characters.</summary>
    public static bool TryNormalizeDescription(string? input, out string description) =>
        TryNormalizeText(input, MaximumDescriptionLength, out description);

    /// <summary>An applicant's reasons for joining: plain lines of text, empty allowed.</summary>
    public static bool TryNormalizeApplication(string? input, out string message) =>
        TryNormalizeText(input, MaximumApplicationLength, out message);

    private static bool TryNormalizeText(string? input, int maximum, out string description)
    {
        description = string.Empty;
        if (input == null)
            return true;
        if (input.Length > maximum * 2)
            return false;
        var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length > maximum)
            return false;
        for (var index = 0; index < normalized.Length; index++)
        {
            if (char.IsControl(normalized[index]) && normalized[index] != '\n' ||
                char.GetUnicodeCategory(normalized, index) == UnicodeCategory.Format)
                return false;
        }
        description = normalized;
        return true;
    }

    /// <summary>Ranks are ordered highest first; a member acts only on ranks below their own, the owner on all.</summary>
    public static bool Outranks(int? actorOrder, int? targetOrder) =>
        actorOrder == null ? targetOrder != null : targetOrder != null && actorOrder < targetOrder;

    /// <summary>Whether a rank's withdrawal today stays within its daily cap; the owner has none.</summary>
    public static bool WithinWithdrawCap(bool owner, int cap, int withdrawnToday, int amount) =>
        owner || amount <= cap - withdrawnToday;

    private static bool TryNormalizeLabel(string? input, int maximum, out string label)
    {
        label = string.Empty;
        if (input == null || input.Length > maximum * 2)
            return false;
        foreach (var character in input)
        {
            if (!char.IsLetterOrDigit(character) && " -'.,&/()!".IndexOf(character) < 0)
                return false;
        }
        var cleaned = string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > maximum)
            return false;
        if (cleaned.Length > 0)
        {
            var hasText = false;
            foreach (var character in cleaned)
                hasText |= char.IsLetterOrDigit(character);
            if (!hasText)
                return false;
        }
        label = cleaned;
        return true;
    }
}
