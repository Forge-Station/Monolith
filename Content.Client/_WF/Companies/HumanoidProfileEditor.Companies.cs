using System.Linq;
using System.Numerics;
using Content.Client._WF.Companies;
using Content.Shared._WF.Companies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>The Company tab shows the edited character's player company in place of the Mono company picker.</summary>
public sealed partial class HumanoidProfileEditor
{
    private Label? _companyLabel;
    private TextureRect? _companyLogo;
    private TextureRect? _companyRankIcon;
    private BoxContainer? _companyRoster;
    private Button? _companyCreate;

    private void InitializeCompanyCard()
    {
        // The Mono picker's cards stay in the XAML for merges; hide them and add ours to the same column.
        if (CompanyButton.Parent?.Parent is not { Parent: { } column } pickerCard)
            return;
        pickerCard.Visible = false;
        if (CompanyDescriptionLabel.Parent != null)
            CompanyDescriptionLabel.Parent.Visible = false;
        if (CompanyImage.Parent != null)
            CompanyImage.Parent.Visible = false;
        var companies = IoCManager.Resolve<IEntityManager>().System<WFCompanyClientSystem>();
        var card = new PanelContainer { StyleClasses = { "CreatorCard" } };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        _companyLogo = new TextureRect { SetSize = new Vector2(64, 64), Stretch = TextureRect.StretchMode.Scale, Visible = false };
        _companyRankIcon = new TextureRect { SetSize = new Vector2(24, 24), Stretch = TextureRect.StretchMode.Scale, Visible = false,
            VerticalAlignment = VAlignment.Center, ToolTip = Loc.GetString("wf-company-rank-icon-custom") };
        _companyLabel = new Label { VerticalAlignment = VAlignment.Center };
        var identity = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        identity.AddChild(_companyRankIcon);
        identity.AddChild(_companyLabel);
        body.AddChild(identity);
        var help = new RichTextLabel();
        help.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(Loc.GetString("wf-company-creator-help")));
        body.AddChild(help);
        var open = new Button { Text = Loc.GetString("wf-company-creator-open"), HorizontalAlignment = HAlignment.Left };
        open.OnPressed += _ => companies.OpenWindow();
        _companyCreate = new Button { Text = Loc.GetString("wf-company-creator-create"), StyleClasses = { "ButtonAccept" } };
        _companyCreate.OnPressed += _ => companies.OpenWindow(true);
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        buttons.AddChild(_companyCreate);
        buttons.AddChild(open);
        body.AddChild(buttons);
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10 };
        row.AddChild(_companyLogo);
        row.AddChild(body);
        card.AddChild(row);
        column.AddChild(card);
        card.SetPositionInParent(0);
        var rosterCard = new PanelContainer { StyleClasses = { "CreatorCard" } };
        var rosterBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        rosterBody.AddChild(new Label { Text = Loc.GetString("wf-company-creator-roster"), StyleClasses = { "CreatorFieldLabel" } });
        _companyRoster = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        rosterBody.AddChild(_companyRoster);
        rosterCard.AddChild(rosterBody);
        column.AddChild(rosterCard);
        rosterCard.SetPositionInParent(1);
        companies.Received += OnCompanyState;
        companies.LogosChanged += UpdateCompanyCard;
        companies.Send(new WFCompanyRequest(WFCompanyAction.Refresh));
        UpdateCompanyCard();
    }

    private void OnCompanyState(WFCompanyState state) => UpdateCompanyCard();

    private void UpdateCompanyCard()
    {
        var companies = IoCManager.Resolve<IEntityManager>().System<WFCompanyClientSystem>();
        // The lobby disposes the editor when the round is joined; stop listening then.
        if (Disposed)
        {
            companies.Received -= OnCompanyState;
            companies.LogosChanged -= UpdateCompanyCard;
            return;
        }
        if (_companyLabel == null || _companyLogo == null || _companyRankIcon == null)
            return;
        var membership = companies.State?.Characters.FirstOrDefault(entry => entry.Slot == CharacterSlot);
        _companyLabel.Text = membership == null
            ? Loc.GetString("wf-company-creator-none")
            : Loc.GetString("wf-company-creator-member", ("company", membership.Company), ("tag", membership.Tag), ("rank", membership.Rank));
        _companyLogo.Texture = membership == null ? null : companies.GetLogo(membership.CompanyId, membership.LogoVersion);
        _companyLogo.Visible = _companyLogo.Texture != null;
        _companyRankIcon.Texture = membership == null ? null : companies.GetRankIcon(membership.RankId, membership.IconVersion);
        _companyRankIcon.Visible = _companyRankIcon.Texture != null;
        if (_companyCreate != null)
            _companyCreate.Visible = membership == null;
        if (_companyRoster == null)
            return;
        _companyRoster.RemoveAllChildren();
        var directory = companies.State?.Directory ?? Array.Empty<WFCompanyListing>();
        if (directory.Length == 0)
            _companyRoster.AddChild(new Label { Text = Loc.GetString("wf-company-directory-empty") });
        foreach (var company in directory)
        {
            var line = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, Margin = new Thickness(6, 4) };
            line.AddChild(new TextureRect { SetSize = new Vector2(24, 24), Stretch = TextureRect.StretchMode.Scale,
                Texture = companies.GetLogo(company.Id, company.LogoVersion) });
            line.AddChild(new Label { Text = $"[{company.Tag}] {company.Name}", FontColorOverride = company.Colour, HorizontalExpand = true, ClipText = true });
            line.AddChild(new Label { Text = Loc.GetString("wf-company-directory-headline", ("count", company.Members),
                ("value", Loc.GetString("wf-company-credits", ("amount", company.Value)))) });
            if (company.Recruiting)
                line.AddChild(new Label { Text = Loc.GetString("wf-company-recruiting"), FontColorOverride = Color.FromHex("#7FD3C4") });
            var entry = new ContainerButton { HorizontalExpand = true, StyleClasses = { "CreatorSlot", ContainerButton.StyleClassButton },
                ToolTip = Loc.GetString("wf-company-profile-open"), Children = { line } };
            entry.OnPressed += _ => new WFCompanyProfileWindow(company, () => WFCompanyJoinActions.Build(companies, companies.State, company)).OpenCentered();
            _companyRoster.AddChild(entry);
        }
    }
}
