using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Companies;

/// <summary>Operations on the company service; the acting character always comes from the sender's session.</summary>
[Serializable, NetSerializable]
public enum WFCompanyAction : byte
{
    Refresh, Create, Invite, AcceptInvite, DeclineInvite, Apply, CancelApplication, AcceptApplication, RejectApplication,
    Leave, Kick, SetMemberRank, UpsertRank, RemoveRank, MoveRank, EditDetails, Rename, SetCut, Transfer, Disband,
    Deposit, Withdraw, JoinAsRank, SetLogo, RequestLogo, SetRankIcon, RequestRankIcon, BuyLicence, SellLicence, Launch, SetLaunchFee, Pay,
    SetRankLoadout, SetOpenRank, JoinOpen, SetPrivate, RequestProfile, SetLicenceName,
}

/// <summary>A request from the Company window.</summary>
[Serializable, NetSerializable]
public sealed class WFCompanyRequest(WFCompanyAction action) : EntityEventArgs
{
    public WFCompanyAction Action = action;
    public int CompanyId;
    /// <summary>A member or applicant, by character profile id.</summary>
    public int? Target;
    /// <summary>An online player to invite.</summary>
    public NetUserId? TargetUser;
    public int? RankId;
    public string? Name;
    public string? Tag;
    public string? Description;
    public Color Colour = Color.White;
    public bool Recruiting;
    public WFCompanyPermissions Permissions;
    public int Wage;
    public int WithdrawCap;
    public string? BaseJob;
    public int Amount;
    /// <summary>MoveRank direction: true moves the rank up one place.</summary>
    public bool Up;
    /// <summary>SetLogo: raw RGBA pixels, or null to clear the logo.</summary>
    public byte[]? Logo;
    /// <summary>BuyLicence: the vessel prototype id.</summary>
    public string? Vessel;
    /// <summary>SellLicence and Launch: the licence.</summary>
    public int LicenceId;
    /// <summary>SetRankLoadout: loadout prototype ids bought for the rank at each spawn.</summary>
    public string[]? Loadouts;
    /// <summary>SetPrivate: whether only members see who is in the company.</summary>
    public bool Private;
}

[Serializable, NetSerializable]
public sealed record WFCompanyListing(int Id, string Name, string Tag, string Description, Color Colour, string OwnerName,
    int Members, bool Recruiting, int LogoVersion = 0, long Value = 0, WFCompanyOpenRank[]? Ranks = null, int? OpenRankId = null, bool Private = false);

/// <summary>A rank an applicant can ask for.</summary>
[Serializable, NetSerializable]
public sealed record WFCompanyOpenRank(int Id, string Title, string BaseJob);

[Serializable, NetSerializable]
public sealed record WFCompanyRank(int Id, int Order, string Title, WFCompanyPermissions Permissions, int Wage, int WithdrawCap,
    string BaseJob, int IconVersion = 0, string[]? Loadouts = null);

[Serializable, NetSerializable]
public sealed record WFCompanyMember(int Profile, string Name, int? Rank, bool Online);

[Serializable, NetSerializable]
public sealed record WFCompanyApplicant(int Profile, string Name, int? RankId = null, string Message = "");

[Serializable, NetSerializable]
public sealed record WFCompanyPlayer(NetUserId User, string Name);

/// <summary>A company one of the player's characters belongs to, for the character creator.</summary>
[Serializable, NetSerializable]
public sealed record WFCompanyCharacter(int Slot, string Company, string Tag, string Rank, int CompanyId = 0, int LogoVersion = 0,
    int RankId = 0, int IconVersion = 0);

/// <summary>A fleet licence and whether it has been launched this round.</summary>
[Serializable, NetSerializable]
public sealed record WFCompanyLicence(int Id, string Vessel, string Name, int ShipyardPrice, bool Launched, string ShipName = "");

/// <summary>A design the owner can license, with what the licence costs.</summary>
[Serializable, NetSerializable]
public sealed record WFCompanyLicenceOffer(string Vessel, string Name, long Price);

[Serializable, NetSerializable]
public sealed record WFCompanyLedgerEntry(DateTime Time, WFCompanyLedgerKind Kind, long Amount, long BalanceAfter,
    string Actor, string Note);

/// <summary>The acting character's own company, with what their rank may see.</summary>
[Serializable, NetSerializable]
public sealed class WFCompanyDetail
{
    public int Id;
    public string Name = string.Empty;
    public string Tag = string.Empty;
    public string Description = string.Empty;
    public Color Colour = Color.White;
    public int OwnerProfile;
    public string OwnerName = string.Empty;
    public long Balance;
    public int Cut;
    public bool Recruiting;
    public bool Frozen;
    public bool ArtLocked;
    public int LogoVersion;
    public int LaunchFee;
    /// <summary>The rank anyone may join at directly, if open access is on.</summary>
    public int? OpenRankId;
    /// <summary>Only members see who is in the company.</summary>
    public bool Private;
    /// <summary>How many company ships are out this round, which sets the next registry fee.</summary>
    public int LaunchedThisRound;
    public WFCompanyLicence[] Licences = Array.Empty<WFCompanyLicence>();
    /// <summary>Owner only: designs that can be licensed.</summary>
    public WFCompanyLicenceOffer[] LicenceOffers = Array.Empty<WFCompanyLicenceOffer>();
    public WFCompanyRank[] Ranks = Array.Empty<WFCompanyRank>();
    /// <summary>The owner's own rank: title, base job and icon, with the company id negated as its id.</summary>
    public WFCompanyRank? OwnerRank;
    public WFCompanyMember[] Members = Array.Empty<WFCompanyMember>();
    public WFCompanyApplicant[] Applications = Array.Empty<WFCompanyApplicant>();
    public int YourProfile;
    public int? YourRank;
    public WFCompanyPermissions YourPermissions;
    public int WithdrawnToday;
    /// <summary>Played seconds counted towards the next wage.</summary>
    public int UnpaidSeconds;
    /// <summary>Newest first: the whole ledger for ranks that handle money, otherwise only the member's own rows.</summary>
    public WFCompanyLedgerEntry[] Ledger = Array.Empty<WFCompanyLedgerEntry>();
}

/// <summary>A company's logo pixels, sent when a client asks for a version it lacks.</summary>
[Serializable, NetSerializable]
public sealed class WFCompanyLogoEvent(int companyId, int version, byte[]? logo) : EntityEventArgs
{
    public int CompanyId = companyId;
    public int Version = version;
    public byte[]? Logo = logo;
}

/// <summary>A rank's job icon pixels, sent on request and to everyone when it changes.</summary>
[Serializable, NetSerializable]
public sealed class WFCompanyRankIconEvent(int rankId, int version, byte[]? icon) : EntityEventArgs
{
    public int RankId = rankId;
    public int Version = version;
    public byte[]? Icon = icon;
}

/// <summary>One member in a company's profile, with the rank icon to draw.</summary>
[Serializable, NetSerializable]
public sealed record WFCompanyProfileMember(string Name, string Rank, int RankId, int IconVersion, string BaseJob);

/// <summary>A ship in a company's hangar, as anyone can see it.</summary>
[Serializable, NetSerializable]
public sealed record WFCompanyProfileShip(string Design, string Name, bool Launched);

/// <summary>A company's public profile; members are left out when the company is private and the viewer isn't in it.</summary>
[Serializable, NetSerializable]
public sealed class WFCompanyProfileEvent(int companyId, bool hidden, WFCompanyProfileMember[] members, WFCompanyProfileShip[] ships) : EntityEventArgs
{
    public int CompanyId = companyId;
    public bool Hidden = hidden;
    public WFCompanyProfileMember[] Members = members;
    public WFCompanyProfileShip[] Ships = ships;
}

/// <summary>A personal snapshot of the company service for the sender's current character.</summary>
[Serializable, NetSerializable]
public sealed class WFCompanyState : EntityEventArgs
{
    public bool Loaded;
    /// <summary>The character the window acts as; null when the player has none loaded.</summary>
    public string? CharacterName;
    public bool CanJoin;
    /// <summary>The member can late-join the round as their rank now.</summary>
    public bool CanJoinRound;
    public int? CharacterBalance;
    /// <summary>What creating a company costs, and the hours of play it needs.</summary>
    public int CreateFee;
    public int CreateHours;
    /// <summary>The player's total hours played, once loaded.</summary>
    public int? PlayedHours;
    public WFCompanyListing[] Directory = Array.Empty<WFCompanyListing>();
    public WFCompanyListing[] Invitations = Array.Empty<WFCompanyListing>();
    public int[] Applications = Array.Empty<int>();
    public WFCompanyDetail? Own;
    public WFCompanyPlayer[] InviteTargets = Array.Empty<WFCompanyPlayer>();
    /// <summary>Memberships of all the player's characters, by slot.</summary>
    public WFCompanyCharacter[] Characters = Array.Empty<WFCompanyCharacter>();
    public string? Message;
}
