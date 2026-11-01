using System.Linq;
using Content.Client._WF.RoundSetup;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Companies;

public sealed partial class WFCompanyWindow
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private Robust.Shared.Network.NetUserId? _inviteTarget;

    private static bool Has(WFCompanyDetail own, WFCompanyPermissions permission) => (own.YourPermissions & permission) == permission;

    private static bool IsOwner(WFCompanyDetail own) => own.YourProfile == own.OwnerProfile;

    /// <summary>Null for the owner; otherwise the rank's place, highest first.</summary>
    private static int? Order(WFCompanyDetail own, int profile, int? rank) =>
        profile == own.OwnerProfile ? null : own.Ranks.FirstOrDefault(entry => entry.Id == rank)?.Order ?? int.MaxValue;

    private static int? YourOrder(WFCompanyDetail own) => Order(own, own.YourProfile, own.YourRank);

    private static string RankTitle(WFCompanyDetail own, int profile, int? rank) => profile == own.OwnerProfile
        ? own.OwnerRank?.Title ?? Text("owner") : own.Ranks.FirstOrDefault(entry => entry.Id == rank)?.Title ?? string.Empty;

    private Control BuildOverview(WFCompanyState state, WFCompanyDetail own)
    {
        var page = Column(10);
        var identity = Column(4);
        identity.AddChild(new Label { Text = $"[{own.Tag}] {own.Name}", FontColorOverride = own.Colour, StyleClasses = { "LabelHeading" } });
        identity.AddChild(new Label { Text = Text("owned-by", ("name", own.OwnerName)) });
        identity.AddChild(new Label { Text = Text("your-rank", ("rank", RankTitle(own, own.YourProfile, own.YourRank))) });
        if (own.Ranks.FirstOrDefault(rank => rank.Id == own.YourRank) is { Wage: > 0 } paid)
        {
            var minutes = Math.Max(1, (WFCompanyPolicy.PayrollHourSeconds - own.UnpaidSeconds + 59) / 60);
            identity.AddChild(new Label { Text = Text("your-wage", ("amount", Credits(paid.Wage)), ("minutes", minutes)) });
        }
        if (own.Description.Length > 0)
            identity.AddChild(Paragraph(own.Description));
        if (state.CanJoinRound)
        {
            var join = new Button { Text = Text("join-as-rank", ("rank", RankTitle(own, own.YourProfile, own.YourRank))), StyleClasses = { "ButtonAccept" } };
            join.OnPressed += _ => Send(WFCompanyAction.JoinAsRank, own.Id);
            identity.AddChild(join);
        }
        page.AddChild(Card(Row(Logo(own.Id, own.LogoVersion, 96), identity)));

        var crews = Column(6);
        crews.AddChild(Heading("crews"));
        crews.AddChild(Paragraph(Text("crews-help")));
        var setup = UserInterfaceManager.GetUIController<WFRoundSetupUIController>();
        var crewButtons = Row(Button("crews-browse", setup.OpenCrews));
        if (Has(own, WFCompanyPermissions.FieldCrew))
            crewButtons.AddChild(Button("crews-start", setup.OpenCreate));
        crews.AddChild(crewButtons);
        page.AddChild(Card(crews));

        var money = Column(6);
        money.AddChild(Heading("account"));
        money.AddChild(new Label { Text = Text("balance", ("amount", Credits(own.Balance))) });
        if (own.Frozen)
            money.AddChild(Paragraph(Text("frozen")));
        money.AddChild(new Label { Text = Text("your-bank", ("amount", Credits(state.CharacterBalance ?? 0))) });
        var deposit = Number(Text("amount"));
        money.AddChild(Row(deposit, Button("deposit", () =>
            Send(new WFCompanyRequest(WFCompanyAction.Deposit) { CompanyId = own.Id, Amount = ParseNumber(deposit) }), own.Frozen)));
        if (Has(own, WFCompanyPermissions.Withdraw))
        {
            var withdraw = Number(Text("amount"));
            money.AddChild(Row(withdraw, Button("withdraw", () =>
                Send(new WFCompanyRequest(WFCompanyAction.Withdraw) { CompanyId = own.Id, Amount = ParseNumber(withdraw) }), own.Frozen)));
            if (!IsOwner(own) && own.Ranks.FirstOrDefault(rank => rank.Id == own.YourRank) is { } yours)
                money.AddChild(new Label { Text = Text("withdraw-left", ("amount", Credits(Math.Max(0, yours.WithdrawCap - own.WithdrawnToday)))) });
        }
        page.AddChild(Card(money));

        if (Has(own, WFCompanyPermissions.Invite))
            page.AddChild(Card(BuildInvite(state, own)));

        page.AddChild(IsOwner(own) && own.Members.Length > 1
            ? Paragraph(Text("owner-leave-help"))
            : Confirm(own.Members.Length == 1 ? "leave-disband" : "leave", () => Send(WFCompanyAction.Leave, own.Id)));
        return page;
    }

    private Control BuildInvite(WFCompanyState state, WFCompanyDetail own)
    {
        var invite = Column(6);
        invite.AddChild(Heading("invite"));
        var targets = state.InviteTargets;
        if (targets.Length == 0)
            invite.AddChild(Paragraph(Text("invite-none")));
        else
        {
            var picker = new OptionButton { HorizontalExpand = true };
            for (var i = 0; i < targets.Length; i++)
                picker.AddItem(targets[i].Name, i);
            var selected = Math.Max(0, Array.FindIndex(targets, target => target.User == _inviteTarget));
            picker.SelectId(selected);
            _inviteTarget = targets[selected].User;
            picker.OnItemSelected += args =>
            {
                picker.SelectId(args.Id);
                _inviteTarget = targets[args.Id].User;
            };
            invite.AddChild(Row(picker, Button("invite-send", () =>
                Send(new WFCompanyRequest(WFCompanyAction.Invite) { CompanyId = own.Id, TargetUser = _inviteTarget }))));
        }
        if (own.Applications.Length > 0)
        {
            invite.AddChild(Heading("applications"));
            foreach (var applicant in own.Applications)
            {
                var wanted = own.Ranks.FirstOrDefault(rank => rank.Id == applicant.RankId)?.Title ?? Text("apply-any-rank");
                var entry = Column(4);
                entry.AddChild(Row(new Label { Text = Text("application-header", ("name", applicant.Name), ("rank", wanted)), HorizontalExpand = true },
                    Button("accept", () => Send(WFCompanyAction.AcceptApplication, own.Id, applicant.Profile)),
                    Button("reject", () => Send(WFCompanyAction.RejectApplication, own.Id, applicant.Profile))));
                entry.AddChild(applicant.Message.Length > 0
                    ? Paragraph(applicant.Message)
                    : new Label { Text = Text("application-no-message"), FontColorOverride = Color.Gray });
                invite.AddChild(Card(entry));
            }
        }
        return invite;
    }

    private Control BuildMembers(WFCompanyDetail own)
    {
        var page = Column(6);
        page.AddChild(Heading("members-count", ("count", own.Members.Length)));
        LineEdit? note = null;
        if (IsOwner(own))
        {
            note = new LineEdit { PlaceHolder = Text("pay-note"), HorizontalExpand = true, IsValid = text => text.Length <= WFCompanyPolicy.MaximumPaymentNote };
            page.AddChild(Paragraph(Text("pay-help")));
            page.AddChild(note);
        }
        foreach (var member in own.Members)
        {
            var row = Row(new Label
            {
                Text = Text(member.Online ? "member-online" : "member-offline", ("name", member.Name)),
                HorizontalExpand = true, FontColorOverride = member.Online ? Color.LightGreen : null,
            });
            var memberOrder = Order(own, member.Profile, member.Rank);
            var canManage = member.Profile != own.YourProfile && WFCompanyPolicy.Outranks(YourOrder(own), memberOrder);
            if (canManage && Has(own, WFCompanyPermissions.SetRanks))
            {
                var ranks = own.Ranks.Where(rank => WFCompanyPolicy.Outranks(YourOrder(own), rank.Order)).ToArray();
                var picker = new OptionButton { MinWidth = 150 };
                foreach (var rank in ranks)
                    picker.AddItem(rank.Title, rank.Id);
                if (member.Rank is { } current && ranks.Any(rank => rank.Id == current))
                    picker.SelectId(current);
                picker.OnItemSelected += args => Send(new WFCompanyRequest(WFCompanyAction.SetMemberRank)
                    { CompanyId = own.Id, Target = member.Profile, RankId = args.Id });
                row.AddChild(picker);
            }
            else
                row.AddChild(new Label { Text = RankTitle(own, member.Profile, member.Rank), MinWidth = 150 });
            if (canManage && Has(own, WFCompanyPermissions.Kick))
                row.AddChild(Confirm("kick", () => Send(WFCompanyAction.Kick, own.Id, member.Profile)));
            if (IsOwner(own) && member.Profile != own.YourProfile)
            {
                var amount = Number(Text("amount"));
                row.AddChild(amount);
                row.AddChild(Button("pay", () => Send(new WFCompanyRequest(WFCompanyAction.Pay)
                {
                    CompanyId = own.Id, Target = member.Profile, Amount = ParseNumber(amount), Description = note?.Text,
                }), !member.Online || own.Frozen));
                row.AddChild(Confirm("transfer", () => Send(WFCompanyAction.Transfer, own.Id, member.Profile)));
            }
            page.AddChild(row);
        }
        return page;
    }

    private Control BuildRanks(WFCompanyDetail own)
    {
        var page = Column(8);
        if (own.OwnerRank is { } ownerRank)
        {
            page.AddChild(Heading("rank-owner"));
            page.AddChild(Card(OwnerRankEditor(own, ownerRank)));
        }
        page.AddChild(Paragraph(Text("ranks-help")));
        var editable = Has(own, WFCompanyPermissions.EditRanks);
        foreach (var rank in own.Ranks)
            page.AddChild(Card(RankEditor(own, rank, editable && WFCompanyPolicy.Outranks(YourOrder(own), rank.Order))));
        if (editable && own.Ranks.Length < WFCompanyPolicy.MaximumRanks)
        {
            page.AddChild(Heading("rank-new"));
            page.AddChild(Card(RankEditor(own, null, true)));
        }
        return page;
    }

    /// <summary>The owner's own title, base job and job icon; only the owner edits it.</summary>
    private Control OwnerRankEditor(WFCompanyDetail own, WFCompanyRank rank)
    {
        var editor = Column(6);
        var editable = IsOwner(own);
        var title = new LineEdit { Text = rank.Title, PlaceHolder = Text("rank-title"), HorizontalExpand = true, Editable = editable };
        var job = new OptionButton { MinWidth = 180, Disabled = !editable };
        var jobs = WFShipCrewJobs.Jobs.Where(id => _prototypes.HasIndex<JobPrototype>(id)).ToArray();
        for (var i = 0; i < jobs.Length; i++)
            job.AddItem(_prototypes.Index<JobPrototype>(jobs[i]).LocalizedName, i);
        job.SelectId(Math.Max(0, Array.IndexOf(jobs, rank.BaseJob)));
        job.OnItemSelected += args => job.SelectId(args.Id);
        var custom = _system.GetRankIcon(rank.Id, rank.IconVersion);
        Control icon = custom != null
            ? new TextureRect { Texture = custom, SetSize = new System.Numerics.Vector2(24), Stretch = TextureRect.StretchMode.Scale, VerticalAlignment = VAlignment.Center }
            : WFRoundSetupCrewRow.JobIcon(rank.BaseJob);
        editor.AddChild(Row(icon, title, job));
        editor.AddChild(Paragraph(Text("rank-owner-help")));
        editor.AddChild(LoadoutRow(own, rank, editable));
        if (!editable)
            return editor;
        var actions = Row(Button("rank-save", () => Send(new WFCompanyRequest(WFCompanyAction.UpsertRank)
        {
            CompanyId = own.Id, RankId = rank.Id, Name = title.Text, BaseJob = jobs[job.SelectedId],
        })));
        if (!own.ArtLocked)
        {
            actions.AddChild(Button("rank-icon-edit", () => new WFCompanyLogoWindow(Text("rank-icon-title", ("rank", rank.Title)),
                WFCompanyPolicy.IconSize, _system.GetRankIconPixels(rank.Id),
                pixels => Send(new WFCompanyRequest(WFCompanyAction.SetRankIcon) { CompanyId = own.Id, RankId = rank.Id, Logo = pixels }))
                .OpenCentered()));
        }
        editor.AddChild(actions);
        return editor;
    }

    /// <summary>What the rank is issued at each spawn and what it costs, with an editor for those who may change it.</summary>
    private Control LoadoutRow(WFCompanyDetail own, WFCompanyRank rank, bool editable)
    {
        var loadouts = rank.Loadouts ?? Array.Empty<string>();
        var row = Row(new Label
        {
            Text = loadouts.Length == 0 ? Text("loadout-empty")
                : Text("loadout-summary", ("count", loadouts.Length), ("cost", Credits(WFCompanyLoadoutWindow.Cost(_prototypes, loadouts)))),
            HorizontalExpand = true, ToolTip = Text("loadout-tooltip"),
        });
        if (editable)
        {
            row.AddChild(Button("loadout-edit", () => new WFCompanyLoadoutWindow(Text("loadout-title", ("rank", rank.Title)), loadouts,
                picked => Send(new WFCompanyRequest(WFCompanyAction.SetRankLoadout) { CompanyId = own.Id, RankId = rank.Id, Loadouts = picked }))
                .OpenCentered()));
        }
        return row;
    }

    /// <summary>One rank's fields; read-only unless the member may edit it.</summary>
    private Control RankEditor(WFCompanyDetail own, WFCompanyRank? rank, bool editable)
    {
        var editor = Column(6);
        var title = new LineEdit { Text = rank?.Title ?? string.Empty, PlaceHolder = Text("rank-title"), HorizontalExpand = true, Editable = editable };
        var wage = Number(Text("rank-wage"), rank?.Wage ?? 0);
        var cap = Number(Text("rank-cap"), rank?.WithdrawCap ?? 0);
        wage.Editable = editable;
        cap.Editable = editable;
        var job = new OptionButton { MinWidth = 180, Disabled = !editable };
        var jobs = WFShipCrewJobs.Jobs.Where(id => _prototypes.HasIndex<JobPrototype>(id)).ToArray();
        for (var i = 0; i < jobs.Length; i++)
            job.AddItem(_prototypes.Index<JobPrototype>(jobs[i]).LocalizedName, i);
        job.SelectId(Math.Max(0, Array.IndexOf(jobs, rank?.BaseJob ?? "Contractor")));
        job.OnItemSelected += args => job.SelectId(args.Id);
        var custom = rank != null ? _system.GetRankIcon(rank.Id, rank.IconVersion) : null;
        Control icon = custom != null
            ? new TextureRect { Texture = custom, SetSize = new System.Numerics.Vector2(24), Stretch = TextureRect.StretchMode.Scale,
                VerticalAlignment = VAlignment.Center, ToolTip = Text("rank-icon-custom") }
            : WFRoundSetupCrewRow.JobIcon(jobs[job.SelectedId]);
        editor.AddChild(Row(icon, title, job));
        editor.AddChild(Row(new Label { Text = Text("rank-wage") }, wage, new Label { Text = Text("rank-cap") }, cap));

        var permissions = rank?.Permissions ?? WFCompanyPermissions.None;
        var grid = new GridContainer { Columns = 3, HorizontalExpand = true };
        foreach (var flag in Enum.GetValues<WFCompanyPermissions>())
        {
            if (flag is WFCompanyPermissions.None or WFCompanyPermissions.All)
                continue;
            var box = new CheckBox
            {
                Text = Text("permission-" + flag.ToString().ToLowerInvariant()), Pressed = (permissions & flag) != 0,
                Disabled = !editable || !Has(own, flag), ToolTip = Text("permission-" + flag.ToString().ToLowerInvariant() + "-help"),
            };
            box.OnToggled += args => permissions = args.Pressed ? permissions | flag : permissions & ~flag;
            grid.AddChild(box);
        }
        editor.AddChild(grid);
        if (rank != null)
            editor.AddChild(LoadoutRow(own, rank, editable));
        if (rank != null && Has(own, WFCompanyPermissions.Design) && !own.ArtLocked && WFCompanyPolicy.Outranks(YourOrder(own), rank.Order))
        {
            editor.AddChild(Button("rank-icon-edit", () => new WFCompanyLogoWindow(Text("rank-icon-title", ("rank", rank.Title)),
                WFCompanyPolicy.IconSize, _system.GetRankIconPixels(rank.Id),
                pixels => Send(new WFCompanyRequest(WFCompanyAction.SetRankIcon) { CompanyId = own.Id, RankId = rank.Id, Logo = pixels }))
                .OpenCentered()));
        }
        if (!editable)
            return editor;
        var actions = Row(Button(rank == null ? "rank-add" : "rank-save", () => Send(new WFCompanyRequest(WFCompanyAction.UpsertRank)
        {
            CompanyId = own.Id, RankId = rank?.Id, Name = title.Text, Wage = ParseNumber(wage), WithdrawCap = ParseNumber(cap),
            BaseJob = jobs[job.SelectedId], Permissions = permissions,
        })));
        if (rank != null)
        {
            actions.AddChild(Button("rank-up", () => Send(new WFCompanyRequest(WFCompanyAction.MoveRank) { CompanyId = own.Id, RankId = rank.Id, Up = true }),
                !WFCompanyPolicy.Outranks(YourOrder(own), rank.Order - 1) || rank.Order == 0));
            actions.AddChild(Button("rank-down", () => Send(new WFCompanyRequest(WFCompanyAction.MoveRank) { CompanyId = own.Id, RankId = rank.Id }),
                rank.Order == own.Ranks.Length - 1));
            actions.AddChild(Confirm("rank-remove", () => Send(new WFCompanyRequest(WFCompanyAction.RemoveRank) { CompanyId = own.Id, RankId = rank.Id })));
        }
        editor.AddChild(actions);
        return editor;
    }
}
