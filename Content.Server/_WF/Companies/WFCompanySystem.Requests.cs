using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private BankSystem _bank = default!;

    private void OnRequest(WFCompanyRequest msg, EntitySessionEventArgs args) => _ = HandleRequest(msg, args.SenderSession);

    /// <summary>Rate-limits, resolves the acting character and runs one operation.</summary>
    public async Task HandleRequest(WFCompanyRequest msg, ICommonSession session)
    {
        if (!Enum.IsDefined(msg.Action) || session.Status != SessionStatus.InGame)
            return;
        // Art fetches are cheap reads that come in bursts, such as a list of logos, so they skip the rate limit.
        if (msg.Action == WFCompanyAction.RequestRankIcon)
        {
            if (Loaded && FindRank(msg.RankId) is { } found)
                RaiseNetworkEvent(new WFCompanyRankIconEvent(found.Id, found.IconVersion, found.Icon), session.Channel);
            return;
        }
        if (msg.Action == WFCompanyAction.RequestLogo)
        {
            if (Loaded && _companies.TryGetValue(msg.CompanyId, out var shown))
                RaiseNetworkEvent(new WFCompanyLogoEvent(shown.Id, shown.LogoVersion, shown.Logo), session.Channel);
            return;
        }
        if (_nextRequest.TryGetValue(session.UserId, out var next) && next > _timing.CurTime)
            return;
        _nextRequest[session.UserId] = _timing.CurTime + TimeSpan.FromMilliseconds(150);
        if (!Loaded)
        {
            SendState(session, "wf-company-error-loading");
            return;
        }
        if (await GetCharacter(session) is not { } character)
        {
            SendState(session, msg.Action == WFCompanyAction.Refresh ? null : "wf-company-error-character");
            return;
        }
        if (msg.Action == WFCompanyAction.Refresh)
        {
            SendState(session);
            return;
        }
        // An invitee's character id may not be known yet; look it up before the invitation needs it.
        if (msg.Action == WFCompanyAction.Invite && msg.TargetUser is { } invitedUser && _players.TryGetSessionById(invitedUser, out var invited))
            await GetCharacter(invited);
        var own = CompanyOf(character.ProfileId);
        var you = own?.Members[character.ProfileId];
        _companies.TryGetValue(msg.CompanyId, out var target);
        string? error = "wf-company-error-denied";
        string? success = null;
        Company? touched = own;
        var extra = new List<Robust.Shared.Network.NetUserId>();
        switch (msg.Action)
        {
            case WFCompanyAction.Create when own == null:
                (error, touched) = await Create(character, msg);
                success = error == null ? "wf-company-created" : null;
                break;
            case WFCompanyAction.AcceptInvite when own == null && target != null && target.Invitations.Contains(character.ProfileId):
                error = Join(target, character.ProfileId, character.User, character.Profile.Name, character.Slot, character.Profile);
                touched = target;
                break;
            case WFCompanyAction.DeclineInvite when target != null:
                target.Invitations.Remove(character.ProfileId);
                error = null;
                break;
            case WFCompanyAction.Apply when own == null && target != null && !IsIronman(character.Profile):
                if (target.Applications.Count >= WFCompanyPolicy.MaximumMembers && !target.Applications.ContainsKey(character.ProfileId))
                    break;
                if (HasPlayer(target, character.User))
                {
                    error = "wf-company-error-same-player";
                    break;
                }
                if (!WFCompanyPolicy.TryNormalizeApplication(msg.Description, out var reason))
                {
                    error = "wf-company-error-application";
                    break;
                }
                var wanted = msg.RankId is { } wantedId && target.Ranks.Any(rank => rank.Id == wantedId) ? msg.RankId : null;
                var isNew = !target.Applications.ContainsKey(character.ProfileId);
                target.Applications[character.ProfileId] = new Applicant(character.User, character.Profile.Name, character.Slot, wanted, reason);
                Track(_db.SaveCompanyApplicationAsync(target.Id, character.ProfileId, wanted, reason));
                // Edits and cancel-and-reapply loops don't ping the officers again.
                if (isNew && (!_applicationNotified.TryGetValue((target.Id, character.ProfileId), out var notified) ||
                              _timing.RealTime - notified >= ApplicationNotifyCooldown))
                {
                    _applicationNotified[(target.Id, character.ProfileId)] = _timing.RealTime;
                    NotifyApplication(target, character.Profile.Name, wanted);
                }
                error = null;
                success = "wf-company-applied";
                touched = target;
                break;
            case WFCompanyAction.JoinOpen when own == null && target != null && OpenRankOf(target) is { } openRank:
                error = Join(target, character.ProfileId, character.User, character.Profile.Name, character.Slot, character.Profile, openRank);
                success = error == null ? "wf-company-joined" : null;
                touched = target;
                break;
            case WFCompanyAction.RequestProfile when target != null:
                RaiseNetworkEvent(Profile(target, HasPlayer(target, character.User)), session.Channel);
                return;
            case WFCompanyAction.CancelApplication when target != null:
                error = DropApplication(target, character.ProfileId) ? null : error;
                touched = target;
                break;
            case WFCompanyAction.Leave when own != null && you != null:
                if (you.Profile == own.Owner && own.Members.Count > 1)
                {
                    error = "wf-company-error-owner-leave";
                    break;
                }
                RemoveMember(own, you);
                error = null;
                extra.Add(character.User);
                break;
            default:
                if (own != null && you != null && own.Id == msg.CompanyId)
                    error = MemberRequest(own, you, character, msg, extra, out success);
                break;
        }
        if (error != null)
        {
            SendState(session, error);
            return;
        }
        extra.Add(session.UserId);
        Touch(touched, extra.ToArray());
        if (success != null)
            SendState(session, success);
    }

    /// <summary>Operations a member performs on their own company; null on success, otherwise the error to show.</summary>
    private string? MemberRequest(Company company, Member you, Character character, WFCompanyRequest msg,
        List<Robust.Shared.Network.NetUserId> extra, out string? success)
    {
        success = null;
        var owner = you.Profile == company.Owner;
        Member? member = msg.Target is { } profile && company.Members.TryGetValue(profile, out var found) ? found : null;
        switch (msg.Action)
        {
            case WFCompanyAction.Invite when Has(company, you, WFCompanyPermissions.Invite) && msg.TargetUser is { } user:
                if (company.Members.Count >= WFCompanyPolicy.MaximumMembers)
                    return "wf-company-error-full";
                if (!_players.TryGetSessionById(user, out var invitee) || CurrentCharacter(invitee) is not { } guest ||
                    CompanyOf(guest.ProfileId) != null || IsIronman(guest.Profile))
                    return "wf-company-error-invite";
                if (HasPlayer(company, user))
                    return "wf-company-error-same-player";
                company.Invitations.Add(guest.ProfileId);
                _chat.DispatchServerMessage(invitee, Loc.GetString("wf-company-invited", ("company", company.Name),
                    ("name", you.Name), ("character", guest.Profile.Name)));
                extra.Add(user);
                success = "wf-company-invite-sent";
                return null;
            case WFCompanyAction.AcceptApplication when Has(company, you, WFCompanyPermissions.Invite) && msg.Target is { } applicantId &&
                                                       company.Applications.TryGetValue(applicantId, out var applicant):
                DropApplication(company, applicantId);
                var profileRef = _preferences.GetPreferencesOrNull(applicant.User)?.Characters.GetValueOrDefault(applicant.Slot) as Content.Shared.Preferences.HumanoidCharacterProfile;
                // The asked-for rank, when the accepter may hand it out; otherwise the lowest.
                var granted = company.Ranks.FindIndex(rank => rank.Id == applicant.RankId) is >= 0 and var grantedOrder &&
                              Has(company, you, WFCompanyPermissions.SetRanks) && WFCompanyPolicy.Outranks(RankOrder(company, you), grantedOrder)
                    ? applicant.RankId
                    : null;
                return Join(company, applicantId, applicant.User, applicant.Name, applicant.Slot, profileRef, granted);
            case WFCompanyAction.RejectApplication when Has(company, you, WFCompanyPermissions.Invite) && msg.Target is { } rejected:
                return DropApplication(company, rejected) ? null : "wf-company-error-denied";
            case WFCompanyAction.SetPrivate when owner:
                company.Private = msg.Private;
                Track(_db.SetCompanyPrivateAsync(company.Id, msg.Private));
                return null;
            case WFCompanyAction.SetOpenRank when owner:
                if (msg.RankId is { } open && company.Ranks.All(rank => rank.Id != open))
                    return "wf-company-error-rank";
                company.OpenRank = msg.RankId;
                Track(_db.SetCompanyOpenRankAsync(company.Id, msg.RankId));
                return null;
            case WFCompanyAction.SetRankLoadout when FindRank(msg.RankId) is { } loadoutRank && CanEditLoadout(company, you, loadoutRank):
                return SetRankLoadout(company, loadoutRank, msg.Loadouts);
            case WFCompanyAction.Kick when member != null && Has(company, you, WFCompanyPermissions.Kick) &&
                                          WFCompanyPolicy.Outranks(RankOrder(company, you), RankOrder(company, member)):
                RemoveMember(company, member);
                extra.Add(member.User);
                return null;
            case WFCompanyAction.SetMemberRank when member != null && Has(company, you, WFCompanyPermissions.SetRanks) &&
                                                   company.Ranks.FindIndex(rank => rank.Id == msg.RankId) is >= 0 and var newOrder &&
                                                   WFCompanyPolicy.Outranks(RankOrder(company, you), RankOrder(company, member)) &&
                                                   WFCompanyPolicy.Outranks(RankOrder(company, you), newOrder):
                member.Rank = msg.RankId;
                PersistMember(member);
                return null;
            case WFCompanyAction.UpsertRank when owner && msg.RankId == company.OwnerRank.Id:
                if (!WFCompanyPolicy.TryNormalizeRankTitle(msg.Name, out var ownerTitle))
                    return "wf-company-error-rank-title";
                if (msg.BaseJob == null || !WFShipCrewJobs.Jobs.Contains(msg.BaseJob))
                    return "wf-company-error-rank";
                company.OwnerRank.Title = ownerTitle;
                company.OwnerRank.BaseJob = msg.BaseJob;
                PersistOwnerRank(company);
                return null;
            case WFCompanyAction.SetRankIcon when owner && msg.RankId == company.OwnerRank.Id:
                if (company.ArtLocked)
                    return "wf-company-error-art-locked";
                if (msg.Logo != null && msg.Logo.Length != WFCompanyPolicy.IconBytes)
                    return "wf-company-error-icon";
                company.OwnerRank.Icon = msg.Logo;
                company.OwnerRank.IconVersion++;
                PersistOwnerRank(company);
                RaiseNetworkEvent(new WFCompanyRankIconEvent(company.OwnerRank.Id, company.OwnerRank.IconVersion, company.OwnerRank.Icon));
                success = "wf-company-icon-saved";
                return null;
            case WFCompanyAction.UpsertRank when Has(company, you, WFCompanyPermissions.EditRanks):
                return UpsertRank(company, you, msg);
            case WFCompanyAction.RemoveRank when Has(company, you, WFCompanyPermissions.EditRanks) &&
                                                company.Ranks.FindIndex(rank => rank.Id == msg.RankId) is >= 0 and var removeOrder &&
                                                WFCompanyPolicy.Outranks(RankOrder(company, you), removeOrder):
                if (company.Ranks.Count <= 1 || company.Members.Values.Any(entry => entry.Rank == msg.RankId))
                    return "wf-company-error-rank-in-use";
                company.Ranks.RemoveAt(removeOrder);
                Track(_db.DeleteCompanyRankAsync(msg.RankId!.Value));
                if (company.OpenRank == msg.RankId)
                {
                    company.OpenRank = null;
                    Track(_db.SetCompanyOpenRankAsync(company.Id, null));
                }
                PersistRanks(company);
                return null;
            case WFCompanyAction.MoveRank when Has(company, you, WFCompanyPermissions.EditRanks) &&
                                              company.Ranks.FindIndex(rank => rank.Id == msg.RankId) is >= 0 and var from:
                var to = msg.Up ? from - 1 : from + 1;
                if (to < 0 || to >= company.Ranks.Count || !WFCompanyPolicy.Outranks(RankOrder(company, you), Math.Min(from, to)))
                    return "wf-company-error-denied";
                (company.Ranks[from], company.Ranks[to]) = (company.Ranks[to], company.Ranks[from]);
                PersistRanks(company);
                return null;
            case WFCompanyAction.EditDetails when Has(company, you, WFCompanyPermissions.EditDetails):
                if (!WFCompanyPolicy.TryNormalizeDescription(msg.Description, out var description))
                    return "wf-company-error-description";
                company.Description = description;
                company.Colour = msg.Colour.WithAlpha(1f);
                company.Recruiting = msg.Recruiting;
                PersistCompany(company);
                return null;
            case WFCompanyAction.Rename when owner:
                if (!WFCompanyPolicy.TryNormalizeName(msg.Name, out var name) || !WFCompanyPolicy.TryNormalizeTag(msg.Tag, out var tag))
                    return "wf-company-error-name";
                if (_companies.Values.Any(other => other != company &&
                        (string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase) || other.Tag == tag)))
                    return "wf-company-error-name-taken";
                company.Name = name;
                company.Tag = tag;
                PersistCompany(company);
                return null;
            case WFCompanyAction.SetCut when owner:
                if (msg.Amount is < 0 or > WFCompanyPolicy.MaximumCut)
                    return "wf-company-error-amount";
                company.Cut = msg.Amount;
                PersistCompany(company);
                return null;
            case WFCompanyAction.Transfer when owner && member != null && member != you:
                var top = company.Ranks[0];
                you.Rank = top.Id;
                member.Rank = null;
                company.Owner = member.Profile;
                PersistMember(you);
                PersistMember(member);
                PersistCompany(company);
                return null;
            case WFCompanyAction.Disband when owner:
                var users = company.Members.Values.Select(entry => entry.User).ToArray();
                RemoveCompany(company);
                extra.AddRange(users);
                return null;
            case WFCompanyAction.Deposit:
                return Deposit(company, you, character, msg.Amount, out success);
            case WFCompanyAction.Withdraw when Has(company, you, WFCompanyPermissions.Withdraw):
                return Withdraw(company, you, character, msg.Amount, out success);
            case WFCompanyAction.SetLogo when Has(company, you, WFCompanyPermissions.Design):
                if (company.ArtLocked)
                    return "wf-company-error-art-locked";
                if (msg.Logo != null && msg.Logo.Length != WFCompanyPolicy.LogoBytes)
                    return "wf-company-error-logo";
                SetLogo(company, msg.Logo, company.ArtLocked);
                success = "wf-company-logo-saved";
                return null;
            case WFCompanyAction.SetRankIcon when Has(company, you, WFCompanyPermissions.Design) &&
                                                 company.Ranks.FindIndex(rank => rank.Id == msg.RankId) is >= 0 and var iconOrder &&
                                                 (you.Profile == company.Owner || WFCompanyPolicy.Outranks(RankOrder(company, you), iconOrder)):
                if (company.ArtLocked)
                    return "wf-company-error-art-locked";
                if (msg.Logo != null && msg.Logo.Length != WFCompanyPolicy.IconBytes)
                    return "wf-company-error-icon";
                var iconRank = company.Ranks[iconOrder];
                iconRank.Icon = msg.Logo;
                iconRank.IconVersion++;
                PersistRanks(company);
                // Everyone may see this icon on a HUD, so every client's copy is replaced.
                RaiseNetworkEvent(new WFCompanyRankIconEvent(iconRank.Id, iconRank.IconVersion, iconRank.Icon));
                success = "wf-company-icon-saved";
                return null;
            case WFCompanyAction.Pay when owner && member != null:
                return Pay(company, you, member, msg.Amount, msg.Description, out success);
            case WFCompanyAction.BuyLicence when owner:
                return BuyLicence(company, you, msg.Vessel, out success);
            case WFCompanyAction.SellLicence when owner:
                return SellLicence(company, msg.LicenceId);
            case WFCompanyAction.SetLaunchFee when owner:
                if (msg.Amount is < 0 or > WFCompanyPolicy.MaximumWage)
                    return "wf-company-error-amount";
                company.LaunchFee = msg.Amount;
                Track(_db.SetCompanyLaunchFeeAsync(company.Id, company.LaunchFee));
                return null;
            case WFCompanyAction.SetLicenceName when Has(company, you, WFCompanyPermissions.Launch):
                return SetLicenceName(company, msg.LicenceId, msg.Name);
            case WFCompanyAction.Launch when Has(company, you, WFCompanyPermissions.Launch) && _players.TryGetSessionById(character.User, out var launcher):
                return LaunchLicence(company, launcher, msg.LicenceId, out success);
            case WFCompanyAction.JoinAsRank when _players.TryGetSessionById(character.User, out var joiner):
                return JoinAsRank(joiner, company, you, character);
            default:
                return "wf-company-error-denied";
        }
    }

    private async Task<(string? Error, Company? Company)> Create(Character character, WFCompanyRequest msg)
    {
        if (IsIronman(character.Profile))
            return ("wf-company-error-ironman", null);
        if (!WFCompanyPolicy.TryNormalizeName(msg.Name, out var name) || !WFCompanyPolicy.TryNormalizeTag(msg.Tag, out var tag))
            return ("wf-company-error-name", null);
        if (!WFCompanyPolicy.TryNormalizeDescription(msg.Description, out var description))
            return ("wf-company-error-description", null);
        if (_companies.Values.Any(other => string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase) || other.Tag == tag))
            return ("wf-company-error-name-taken", null);
        if (!_players.TryGetSessionById(character.User, out var session) || PlayedHours(session) is not { } played ||
            played < _config.GetCVar(WFCompanyCVars.CreateHours))
            return ("wf-company-error-hours", null);
        // The fee is taken first and refunded if the company can't be made.
        var fee = Math.Max(0, _config.GetCVar(WFCompanyCVars.CreateFee));
        if (fee > 0 && (_preferences.GetPreferencesOrNull(character.User) is not { } prefs ||
                        !_bank.TryBankWithdraw(session, prefs, character.Profile, fee, out _)))
            return ("wf-company-error-create-fee", null);
        var (error, made) = await CreateRow(character, name, tag, description, msg);
        if (error != null && fee > 0 && _preferences.GetPreferencesOrNull(character.User) is { } refund &&
            refund.Characters.GetValueOrDefault(character.Slot) is Content.Shared.Preferences.HumanoidCharacterProfile current)
            _bank.TryBankDeposit(session, refund, current, fee, out _);
        return (error, made);
    }

    private async Task<(string? Error, Company? Company)> CreateRow(Character character, string name, string tag, string description,
        WFCompanyRequest msg)
    {
        var now = DateTime.UtcNow;
        var row = new WolfgateCompany
        {
            Name = name, Tag = tag, Description = description, Colour = msg.Colour.WithAlpha(1f).ToHex(), OwnerProfileId = character.ProfileId,
            Cut = WFCompanyPolicy.DefaultCut, CreatedAt = now,
            Ranks = WFCompanyPolicy.DefaultRanks.Select((rank, order) => new WolfgateCompanyRank
            {
                Order = order, Title = rank.Title, Permissions = (int) rank.Permissions, BaseJob = rank.BaseJob,
            }).ToList(),
        };
        WolfgateCompany? created;
        try
        {
            created = await _db.CreateCompanyAsync(row, character.ProfileId);
        }
        catch (Exception e)
        {
            _log.Error($"Could not create company {name}: {e}");
            return ("wf-company-error-failed", null);
        }
        // The await let other requests run; recheck the name and membership they could have taken.
        if (created == null || CompanyOf(character.ProfileId) != null)
            return ("wf-company-error-name-taken", null);
        var company = new Company
        {
            Id = created.Id, Name = name, Tag = tag, Description = description, Colour = msg.Colour.WithAlpha(1f),
            Owner = character.ProfileId, Cut = created.Cut,
        };
        company.OwnerRank = new Rank { Id = -created.Id, Title = Loc.GetString("wf-company-owner"), BaseJob = "Contractor",
            Permissions = WFCompanyPermissions.All };
        foreach (var rank in created.Ranks.OrderBy(rank => rank.Order))
        {
            company.Ranks.Add(new Rank { Id = rank.Id, Title = rank.Title, Permissions = (WFCompanyPermissions) rank.Permissions,
                BaseJob = rank.BaseJob });
        }
        company.Members[character.ProfileId] = new Member
        {
            Profile = character.ProfileId, User = character.User, Name = character.Profile.Name, Slot = character.Slot,
            WithdrawDay = now.Date,
        };
        _companies[company.Id] = company;
        _memberCompany[character.ProfileId] = company.Id;
        ClearInvitations(character.ProfileId);
        _adminLog.Add(Shared.Database.LogType.Action, Shared.Database.LogImpact.Low,
            $"{character.Profile.Name} ({character.User}) created company {name} [{tag}] ({company.Id})");
        return (null, company);
    }

    /// <summary>Adds a character at the lowest rank; null on success.</summary>
    private string? Join(Company company, int profile, Robust.Shared.Network.NetUserId user, string name, int slot,
        Content.Shared.Preferences.HumanoidCharacterProfile? character, int? rankId = null)
    {
        company.Invitations.Remove(profile);
        DropApplication(company, profile);
        if (CompanyOf(profile) != null)
            return "wf-company-error-member";
        if (character != null && IsIronman(character))
            return "wf-company-error-ironman";
        if (company.Members.Count >= WFCompanyPolicy.MaximumMembers)
            return "wf-company-error-full";
        if (HasPlayer(company, user))
            return "wf-company-error-same-player";
        var member = new Member { Profile = profile, User = user, Name = name, Slot = slot, Rank = rankId ?? company.Ranks[^1].Id,
            WithdrawDay = DateTime.UtcNow.Date };
        company.Members[profile] = member;
        _memberCompany[profile] = company.Id;
        ClearInvitations(profile);
        Stats(company).Joined++;
        Track(AddMemberRow(company, member));
        _adminLog.Add(Shared.Database.LogType.Action, Shared.Database.LogImpact.Low, $"{name} ({user}) joined company {company.Name} ({company.Id})");
        return null;
    }

    /// <summary>Undoes the membership in memory if the database refuses it, such as a character deleted meanwhile.</summary>
    private async Task AddMemberRow(Company company, Member member)
    {
        var added = await _db.AddCompanyMemberAsync(new Content.Server.Database.WolfgateCompanyMember
        {
            ProfileId = member.Profile, CompanyId = company.Id, RankId = member.Rank, JoinedAt = DateTime.UtcNow,
            WithdrawDay = member.WithdrawDay,
        });
        if (added || !company.Members.Remove(member.Profile))
            return;
        _memberCompany.Remove(member.Profile);
        Touch(company, member.User);
    }

    private void ClearInvitations(int profile)
    {
        var applied = false;
        foreach (var other in _companies.Values)
        {
            other.Invitations.Remove(profile);
            applied |= other.Applications.Remove(profile);
        }
        if (applied)
            Track(_db.DeleteCompanyApplicationsAsync(profile, null));
    }

    /// <summary>How long before the same character's application to the same company notifies its officers again.</summary>
    private static readonly TimeSpan ApplicationNotifyCooldown = TimeSpan.FromMinutes(5);

    private readonly Dictionary<(int Company, int Profile), TimeSpan> _applicationNotified = new();

    /// <summary>Who is in a company, owner first then by rank; empty for outsiders when it is private.</summary>
    private WFCompanyProfileEvent Profile(Company company, bool member)
    {
        if (company.Private && !member)
            return new WFCompanyProfileEvent(company.Id, true, Array.Empty<WFCompanyProfileMember>(), ProfileShips(company));
        var members = company.Members.Values.OrderBy(entry => RankOrder(company, entry) ?? -1).ThenBy(entry => entry.Name)
            .Select(entry =>
            {
                var rank = IconRankOf(company, entry);
                return new WFCompanyProfileMember(entry.Name, TitleOf(company, entry), rank?.Id ?? 0,
                    rank?.Icon == null ? 0 : rank.IconVersion, BaseJob(company, entry));
            }).ToArray();
        return new WFCompanyProfileEvent(company.Id, false, members, ProfileShips(company));
    }

    /// <summary>Whether one of the player's characters is already a member; a player has one character per company.</summary>
    private static bool HasPlayer(Company company, Robust.Shared.Network.NetUserId user) =>
        company.Members.Values.Any(member => member.User == user);

    /// <summary>Members counted by player, so alternate characters joined before the one-per-player rule count once.</summary>
    private static int PlayerCount(Company company) => company.Members.Values.Select(member => member.User).Distinct().Count();

    /// <summary>Removes one application from memory and the database; false if there was none.</summary>
    private bool DropApplication(Company company, int profile)
    {
        if (!company.Applications.Remove(profile))
            return false;
        Track(_db.DeleteCompanyApplicationsAsync(profile, company.Id));
        return true;
    }

    /// <summary>Tells online members who can accept applications that one arrived.</summary>
    private void NotifyApplication(Company company, string name, int? rankId)
    {
        var rank = company.Ranks.FirstOrDefault(entry => entry.Id == rankId)?.Title ?? Loc.GetString("wf-company-apply-any-rank");
        foreach (var member in company.Members.Values)
        {
            if (Has(company, member, WFCompanyPermissions.Invite) && _players.TryGetSessionById(member.User, out var session) &&
                CurrentCharacterProfile(member.User) == member.Profile)
                _chat.DispatchServerMessage(session, Loc.GetString("wf-company-application-received", ("name", name), ("rank", rank)));
        }
    }

    private void RemoveMember(Company company, Member member)
    {
        company.Members.Remove(member.Profile);
        _memberCompany.Remove(member.Profile);
        if (company.Members.Count == 0)
        {
            RemoveCompany(company);
            return;
        }
        Track(_db.RemoveCompanyMemberAsync(member.Profile));
        _adminLog.Add(Shared.Database.LogType.Action, Shared.Database.LogImpact.Low,
            $"{member.Name} ({member.User}) left company {company.Name} ({company.Id})");
    }

    private string? UpsertRank(Company company, Member you, WFCompanyRequest msg)
    {
        if (!WFCompanyPolicy.TryNormalizeRankTitle(msg.Name, out var title))
            return "wf-company-error-rank-title";
        if (msg.Wage is < 0 or > WFCompanyPolicy.MaximumWage || msg.WithdrawCap is < 0 or > WFCompanyPolicy.MaximumWage ||
            msg.BaseJob == null || !WFShipCrewJobs.Jobs.Contains(msg.BaseJob) || (msg.Permissions & ~WFCompanyPermissions.All) != 0)
            return "wf-company-error-rank";
        // Nobody grants a permission they don't hold themselves.
        if ((msg.Permissions & ~Permissions(company, you)) != 0)
            return "wf-company-error-denied";
        Rank rank;
        if (msg.RankId is not { } id)
        {
            if (company.Ranks.Count >= WFCompanyPolicy.MaximumRanks)
                return "wf-company-error-ranks-full";
            rank = new Rank();
            company.Ranks.Add(rank);
        }
        else
        {
            var order = company.Ranks.FindIndex(existing => existing.Id == id);
            if (order < 0 || !WFCompanyPolicy.Outranks(RankOrder(company, you), order))
                return "wf-company-error-denied";
            rank = company.Ranks[order];
        }
        rank.Title = title;
        rank.Permissions = msg.Permissions;
        rank.Wage = msg.Wage;
        rank.WithdrawCap = msg.WithdrawCap;
        rank.BaseJob = msg.BaseJob;
        if (rank.Id == 0)
            Track(AddRankRow(company, rank));
        else
            PersistRanks(company);
        return null;
    }

    private Rank? FindRank(int? id) => id switch
    {
        null => null,
        < 0 => _companies.TryGetValue(-id.Value, out var owned) ? owned.OwnerRank : null,
        _ => _companies.Values.SelectMany(company => company.Ranks).FirstOrDefault(rank => rank.Id == id),
    };

    private async Task AddRankRow(Company company, Rank rank)
    {
        rank.Id = await _db.SaveCompanyRankAsync(new Content.Server.Database.WolfgateCompanyRank
        {
            CompanyId = company.Id, Order = company.Ranks.IndexOf(rank), Title = rank.Title, Permissions = (int) rank.Permissions,
            Wage = rank.Wage, WithdrawCap = rank.WithdrawCap, BaseJob = rank.BaseJob,
        });
        Touch(company);
    }

    private string? Deposit(Company company, Member you, Character character, int amount, out string? success)
    {
        success = null;
        if (amount <= 0)
            return "wf-company-error-amount";
        if (company.Frozen)
            return "wf-company-error-frozen";
        if (!_players.TryGetSessionById(character.User, out var session) || _preferences.GetPreferencesOrNull(character.User) is not { } prefs ||
            !_bank.TryBankWithdraw(session, prefs, character.Profile, amount, out _))
            return "wf-company-error-funds";
        Transact(company, WFCompanyLedgerKind.Deposit, amount, you.Profile, you.Name);
        success = "wf-company-deposited";
        return null;
    }

    private string? Withdraw(Company company, Member you, Character character, int amount, out string? success)
    {
        success = null;
        if (amount <= 0)
            return "wf-company-error-amount";
        if (company.Frozen)
            return "wf-company-error-frozen";
        if (company.Balance < amount)
            return "wf-company-error-balance";
        var today = DateTime.UtcNow.Date;
        if (you.WithdrawDay != today)
        {
            you.WithdrawDay = today;
            you.WithdrawnToday = 0;
        }
        var owner = you.Profile == company.Owner;
        if (!WFCompanyPolicy.WithinWithdrawCap(owner, RankOf(company, you)?.WithdrawCap ?? 0, you.WithdrawnToday, amount))
            return "wf-company-error-cap";
        if (!_players.TryGetSessionById(character.User, out var session) || _preferences.GetPreferencesOrNull(character.User) is not { } prefs ||
            !_bank.TryBankDeposit(session, prefs, character.Profile, amount, out _))
            return "wf-company-error-failed";
        Transact(company, WFCompanyLedgerKind.Withdraw, -amount, you.Profile, you.Name);
        if (!owner)
        {
            you.WithdrawnToday += amount;
            PersistMember(you);
        }
        success = "wf-company-withdrew";
        return null;
    }

    /// <summary>The owner pays a member straight from the account into that member's character bank; they must be online.</summary>
    private string? Pay(Company company, Member owner, Member member, int amount, string? note, out string? success)
    {
        success = null;
        if (amount <= 0)
            return "wf-company-error-amount";
        if (company.Frozen)
            return "wf-company-error-frozen";
        if (company.Balance < amount)
            return "wf-company-error-balance";
        var text = (note ?? string.Empty).Trim();
        if (text.Length > WFCompanyPolicy.MaximumPaymentNote || text.Any(char.IsControl))
            return "wf-company-error-note";
        if (!_players.TryGetSessionById(member.User, out var session) || _preferences.GetPreferencesOrNull(member.User) is not { } prefs ||
            prefs.Characters.GetValueOrDefault(member.Slot) is not Content.Shared.Preferences.HumanoidCharacterProfile profile)
            return "wf-company-error-offline";
        var paid = prefs.SelectedCharacterIndex == member.Slot && session.AttachedEntity is { } body &&
                   HasComp<Content.Shared._NF.Bank.Components.BankAccountComponent>(body)
            ? _bank.TryBankDeposit(body, amount, false)
            : _bank.TryBankDeposit(session, prefs, profile, amount, out _);
        if (!paid)
            return "wf-company-error-failed";
        Transact(company, WFCompanyLedgerKind.Payment, -amount, owner.Profile, owner.Name, text.Length > 0 ? $"{member.Name}: {text}" : member.Name);
        _chat.DispatchServerMessage(session, Loc.GetString(text.Length > 0 ? "wf-company-paid-note" : "wf-company-paid",
            ("company", company.Name), ("amount", amount), ("name", owner.Name), ("note", text)));
        success = "wf-company-payment-sent";
        return null;
    }
}
