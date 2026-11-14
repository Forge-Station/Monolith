using Content.Shared.Humanoid;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>The crew starting conditions; Normal is reserved and cannot create a scenario lobby.</summary>
[Serializable, NetSerializable]
public enum WFRoundScenario : byte { Normal, Crashlanded, Ship, FactionShip, Doomed, Formed }

/// <summary>How hard a scenario is to survive, shown on its card.</summary>
[Serializable, NetSerializable]
public enum WFScenarioDifficulty : byte { Easy, Normal, Hard, VeryHard, Impossible }

/// <summary>Where a purchased crew ship begins its journey.</summary>
[Serializable, NetSerializable]
public enum WFShipStartLocation : byte { RandomSpace, CentralDock, FactionBase }

/// <summary>How a player can enter a scenario lobby.</summary>
[Serializable, NetSerializable]
public enum WFLobbyAccess : byte { Public, Requests, Closed, Code, Invite, CompanyOnly, CompanyFirst }

/// <summary>Authenticated operations on the round setup service.</summary>
[Serializable, NetSerializable]
public enum WFRoundSetupAction : byte
{
    Refresh, Create, Configure, Join, Leave, AcceptRequest, RejectRequest,
    Invite, AcceptInvite, DeclineInvite, SelectJob, SetReady, Launch, RegenerateCode, Kick, TransferHost,
    SelectVessel, UpsertRole, RemoveRole, SelectRole, ConfirmJoin, ChangeScenario, SelectShipStart, ApplyRoster, SetTitle, SaveSetup, LoadSetup, DeleteSetup, SetShipName, AssignShip, ApproveShip, CancelShip, SetContribution, Watch, SetRecruiting, Post, SetCompany,
}

/// <summary>A request whose actor is always taken from its network session.</summary>
[Serializable, NetSerializable]
public sealed class WFRoundSetupRequest : EntityEventArgs
{
    public WFRoundSetupAction Action;
    public int LobbyId;
    public WFRoundScenario Scenario;
    public WFCrewFaction Faction;
    public NetEntity? Ship;
    public int ShipChangeId;
    public int Contribution;
    public int? PaymentRevision;
    public string ShipName = string.Empty;
    public WFShipStartLocation ShipStart;
    public WFLobbyAccess Access = WFLobbyAccess.Requests;
    public bool AllowLateJoin;
    public bool Ready;
    public NetUserId? Target;
    public string? Code;
    public string? Job;
    public NetEntity? Station;
    public string? Vessel;
    public int? RoleId;
    public string? RoleName;
    public string? Title;
    public string? Description;
    public int Capacity = 1;
    public WFCrewRoster Roster;
    public int SavedId;
    public string? SaveName;
    public bool Recruiting;
    public string? Posting;
    public int? CompanyId;
    /// <summary>Join: a company member joins their company's crew as their company rank instead of an opening.</summary>
    public bool AsRank;

    public WFRoundSetupRequest(WFRoundSetupAction action) { Action = action; }
}

/// <summary>A player's public identity in the setup interface.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupPlayer(NetUserId UserId, string Name, int? RoleId = null);

/// <summary>A member's own job and readiness, with no character profile data.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupMember(NetUserId UserId, string Name, string? Job, NetEntity? Station, bool Ready, bool Spawned, bool Connected, int? RoleId = null, string Title = "", int Contribution = 0, WFCrewMemberStatus Status = WFCrewMemberStatus.Waiting, bool Live = false, int RankId = 0, bool RankLocked = false);

/// <summary>A public crew roster entry containing only character identity and readiness.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupCrewmate(string Name, string? Job, string Title, bool Ready, bool Spawned, WFCrewMemberStatus Status = WFCrewMemberStatus.Waiting, int RankId = 0);

/// <summary>The recipient's exact purchase quote; bank balances remain private.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupPayment(int Revision, int Price, int Contribution, long Allocated, bool AllApproved);

/// <summary>A job choice offered by the server for this player's scenario.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupJob(string Id, NetEntity? Station, string StationName);

/// <summary>A named crew role with server-checked capacity and eligibility for the recipient.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupRole(int Id, string Name, string Job, NetEntity? Station, int Capacity, int Occupied, bool CanSelect);

/// <summary>A ship offered for purchase when the host launches their crew.</summary>
/// <summary>A player company a host may run their crew under.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupCompany(int Id, string Name, string Tag);

[Serializable, NetSerializable]
public sealed record WFRoundSetupVessel(string Id, string Name, string Description, int Price);

/// <summary>A live ship's identity, without ownership account details.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupShip(NetEntity Entity, string Name, string? Vessel);

/// <summary>A crew member's deed candidate or a pending request for that owner's approval.</summary>
[Serializable, NetSerializable]
public sealed record WFRoundSetupShipChoice(NetUserId Owner, string OwnerName, WFRoundSetupShip Ship, string? Error = null, int RequestId = 0);

/// <summary>Only the visible character appearance used by a crew leader portrait.</summary>
[Serializable, NetSerializable]
public sealed record WFCrewPortrait(string Name, string Species, Sex Sex, HumanoidCharacterAppearance Appearance);

/// <summary>A sanitized player-lobby listing; codes and requests are sent only to its host.</summary>
[Serializable, NetSerializable]
public sealed class WFRoundSetupLobby
{
    public int Id;
    public NetUserId Host;
    public string HostName = string.Empty;
    public string? HostJob;
    /// <summary>The host's shown title, when their job isn't the whole story.</summary>
    public string HostTitle = string.Empty;
    /// <summary>The host's company rank, whose icon stands in for their job's while the crew flies under that company.</summary>
    public int HostRankId;
    public WFCrewPortrait? Portrait;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public WFRoundScenario Scenario;
    public WFCrewFaction Faction;
    public string ShipName = string.Empty;
    public WFShipStartLocation ShipStart;
    public WFLobbyAccess Access;
    public bool AllowLateJoin;
    public bool Started;
    public WFRoundSetupShip? CurrentShip;
    public WFCrewShipCondition ShipCondition;
    public int MemberCount;
    public WFRoundSetupCrewmate[] Roster = Array.Empty<WFRoundSetupCrewmate>();
    public WFRoundSetupMember[] Members = Array.Empty<WFRoundSetupMember>();
    public WFRoundSetupPlayer[] Requests = Array.Empty<WFRoundSetupPlayer>();
    public string? Code;
    public string? Vessel;
    public WFRoundSetupRole[] Roles = Array.Empty<WFRoundSetupRole>();
    /// <summary>Shown at the top of crew lists with a pulsing border.</summary>
    public bool Recruiting;
    /// <summary>Whether players already in the round can join with their current character.</summary>
    public bool LiveJoin;
    /// <summary>Host only: the host's recruiting switch, which shows only while an opening is available.</summary>
    public bool RecruitingOn;
    /// <summary>Host only: when the next posting is allowed.</summary>
    public TimeSpan? NextPosting;
    /// <summary>The player company this crew flies under, if any.</summary>
    public int? CompanyId;
    public string CompanyName = string.Empty;
    public string CompanyTag = string.Empty;
    public Color CompanyColour = Color.White;
    /// <summary>A company hangar launch: the design is fixed and the company pays.</summary>
    public bool Hangar;
    /// <summary>Recipient only: their company rank's job is one this crew can field, so they may join as it.</summary>
    public bool CanJoinAsRank;
}

/// <summary>A per-recipient snapshot of the round setup browser and their membership.</summary>
[Serializable, NetSerializable]
public sealed class WFRoundSetupState : EntityEventArgs
{
    public bool InRound;
    public bool JoinConfirmed;
    public bool CanJoin;
    /// <summary>The recipient is playing a living character and belongs to no crew.</summary>
    public bool CanJoinLive;
    /// <summary>Stations whose crew is recruiting, for the ordinary late-join picker.</summary>
    public NetEntity[] RecruitingStations = Array.Empty<NetEntity>();
    /// <summary>The recipient's own player company, so its crews sort first.</summary>
    public int? OwnCompany;
    /// <summary>The recipient's company rank title, for joining their company's crews as it.</summary>
    public string? CompanyRank;
    /// <summary>Host only: companies the crew may fly under.</summary>
    public WFRoundSetupCompany[] FieldCompanies = Array.Empty<WFRoundSetupCompany>();
    public bool CrashlandedAvailable;
    public bool DoomedAvailable;
    public WFCrewFaction[] Factions = Array.Empty<WFCrewFaction>();
    public int? OwnLobby;
    public WFRoundSetupLobby[] Lobbies = Array.Empty<WFRoundSetupLobby>();
    public WFRoundSetupLobby[] Invitations = Array.Empty<WFRoundSetupLobby>();
    public WFRoundSetupPlayer[] InviteTargets = Array.Empty<WFRoundSetupPlayer>();
    public WFRoundSetupJob[] Jobs = Array.Empty<WFRoundSetupJob>();
    public WFRoundSetupJob[] RoleJobs = Array.Empty<WFRoundSetupJob>();
    public WFSavedSetup[] SavedSetups = Array.Empty<WFSavedSetup>();
    public WFRoundSetupVessel[] Vessels = Array.Empty<WFRoundSetupVessel>();
    public int? HostBalance;
    public WFRoundSetupPayment? Payment;
    public WFRoundSetupShipChoice[] CrewShips = Array.Empty<WFRoundSetupShipChoice>();
    public WFRoundSetupShipChoice? PendingShip;
    public string? Message;
}
