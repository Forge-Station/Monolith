using System.Linq;
using System.Numerics;
using Content.Client._WF.RoundSetup;
using Content.Shared._WF.Companies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Companies;

/// <summary>A company's profile from the Companies list: who runs it, its ranks and, unless it is private, its members.</summary>
public sealed class WFCompanyProfileWindow : DefaultWindow
{
    private readonly WFCompanyClientSystem _system;
    private readonly WFCompanyListing _company;
    private readonly BoxContainer _members = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly BoxContainer _ships = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };

    /// <param name="actions">Builds the Apply or Join buttons the viewer can use, if any.</param>
    public WFCompanyProfileWindow(WFCompanyListing company, Func<Control?>? actions)
    {
        _company = company;
        _system = IoCManager.Resolve<IEntityManager>().System<WFCompanyClientSystem>();
        Title = $"[{company.Tag}] {company.Name}";
        MinSize = new Vector2(480, 420);
        SetSize = new Vector2(560, 600);

        var page = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };
        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
        header.AddChild(new Label { Text = $"[{company.Tag}] {company.Name}", FontColorOverride = company.Colour, StyleClasses = { "LabelHeading" } });
        header.AddChild(new Label { Text = Loc.GetString("wf-company-owned-by", ("name", company.OwnerName)) });
        header.AddChild(new Label { Text = Loc.GetString("wf-company-directory-headline", ("count", company.Members),
            ("value", Loc.GetString("wf-company-credits", ("amount", company.Value)))), FontColorOverride = Color.FromHex("#C8C8C8") });
        var badges = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        if (company.Recruiting)
            badges.AddChild(new Label { Text = Loc.GetString("wf-company-recruiting"), FontColorOverride = Color.FromHex("#7FD3C4") });
        if (company.Ranks?.FirstOrDefault(rank => rank.Id == company.OpenRankId) is { } open)
            badges.AddChild(new Label { Text = Loc.GetString("wf-company-profile-open-rank", ("rank", open.Title)), FontColorOverride = Color.FromHex("#E3B65C") });
        header.AddChild(badges);
        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10 };
        top.AddChild(new TextureRect { Texture = _system.GetLogo(company.Id, company.LogoVersion), SetSize = new Vector2(64, 64),
            Stretch = TextureRect.StretchMode.Scale, VerticalAlignment = VAlignment.Top });
        top.AddChild(header);
        page.AddChild(top);
        if (company.Description.Length > 0)
            page.AddChild(Paragraph(company.Description));
        if (actions?.Invoke() is { } buttons)
            page.AddChild(buttons);

        if (company.Ranks is { Length: > 0 } ranks)
        {
            page.AddChild(new Label { Text = Loc.GetString("wf-company-profile-ranks"), StyleClasses = { "LabelHeading" } });
            page.AddChild(Paragraph(string.Join(" · ", ranks.Select(rank => rank.Title))));
        }
        page.AddChild(new Label { Text = Loc.GetString("wf-company-profile-hangar"), StyleClasses = { "LabelHeading" } });
        _ships.AddChild(new Label { Text = Loc.GetString("wf-company-profile-loading"), FontColorOverride = Color.Gray });
        page.AddChild(_ships);
        page.AddChild(new Label { Text = Loc.GetString("wf-company-profile-members"), StyleClasses = { "LabelHeading" } });
        _members.AddChild(new Label { Text = Loc.GetString("wf-company-profile-loading"), FontColorOverride = Color.Gray });
        page.AddChild(_members);

        Contents.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { page } });
        _system.ProfileReceived += OnProfile;
        OnClose += () => _system.ProfileReceived -= OnProfile;
        _system.Send(new WFCompanyRequest(WFCompanyAction.RequestProfile) { CompanyId = company.Id });
    }

    private void OnProfile(WFCompanyProfileEvent profile)
    {
        if (profile.CompanyId != _company.Id)
            return;
        _ships.RemoveAllChildren();
        if (profile.Ships.Length == 0)
            _ships.AddChild(new Label { Text = Loc.GetString("wf-company-profile-hangar-empty"), FontColorOverride = Color.Gray });
        foreach (var ship in profile.Ships)
        {
            var line = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            line.AddChild(new Label { HorizontalExpand = true, ClipText = true, Text = ship.Name.Length > 0
                ? Loc.GetString("wf-company-profile-ship", ("name", ship.Name), ("design", ship.Design)) : ship.Design });
            if (ship.Launched)
                line.AddChild(new Label { Text = Loc.GetString("wf-company-profile-ship-out"), FontColorOverride = Color.FromHex("#E3B65C") });
            _ships.AddChild(line);
        }
        _members.RemoveAllChildren();
        if (profile.Hidden)
        {
            _members.AddChild(Paragraph(Loc.GetString("wf-company-profile-private")));
            return;
        }
        foreach (var member in profile.Members)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            row.AddChild(_system.GetRankIcon(member.RankId, member.IconVersion) is { } icon
                ? new TextureRect { Texture = icon, SetSize = new Vector2(16, 16), Stretch = TextureRect.StretchMode.Scale, VerticalAlignment = VAlignment.Center }
                : WFRoundSetupCrewRow.JobIcon(member.BaseJob));
            row.AddChild(new Label { Text = member.Name, HorizontalExpand = true });
            row.AddChild(new Label { Text = member.Rank, FontColorOverride = Color.FromHex("#C8C8C8") });
            _members.AddChild(row);
        }
    }

    private static RichTextLabel Paragraph(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }
}
