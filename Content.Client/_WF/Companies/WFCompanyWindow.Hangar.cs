using Content.Client._WF.RoundSetup;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Companies;

public sealed partial class WFCompanyWindow
{
    private int _licenceOffer;

    /// <summary>The company's fleet licences: launch one copy of each design per round, buy more, set the launch fee.</summary>
    private Control BuildHangar(WFCompanyDetail own)
    {
        var page = Column(10);
        page.AddChild(Paragraph(Text("hangar-help")));
        if (own.Members.Length < WFCompanyPolicy.MinimumFleetMembers)
            page.AddChild(Paragraph(Text("hangar-members-needed", ("count", WFCompanyPolicy.MinimumFleetMembers))));
        var fleet = Column(6);
        fleet.AddChild(Heading("hangar-licences", ("count", own.Licences.Length), ("max", WFCompanyPolicy.MaximumLicences)));
        if (own.Licences.Length == 0)
            fleet.AddChild(Paragraph(Text("hangar-empty")));
        var canLaunch = Has(own, WFCompanyPermissions.Launch);
        foreach (var licence in own.Licences)
        {
            var registry = WFCompanyPolicy.RegistryFee(licence.ShipyardPrice, own.LaunchedThisRound);
            var info = Column(2);
            info.AddChild(new Label { Text = licence.ShipName.Length > 0 ? Text("profile-ship", ("name", licence.ShipName), ("design", licence.Name)) : licence.Name,
                StyleClasses = { "LabelHeading" } });
            info.AddChild(new Label { Text = licence.Launched ? Text("hangar-out") : Text("hangar-ready", ("registry", Credits(registry)),
                ("fee", Credits(own.LaunchFee))) });
            var row = Row(info);
            info.HorizontalExpand = true;
            if (canLaunch)
            {
                var launch = Button("hangar-launch", () =>
                {
                    Send(new WFCompanyRequest(WFCompanyAction.Launch) { CompanyId = own.Id, LicenceId = licence.Id });
                    UserInterfaceManager.GetUIController<WFRoundSetupUIController>().OpenCrews();
                }, licence.Launched);
                launch.AddStyleClass("ButtonAccept");
                row.AddChild(launch);
            }
            if (IsOwner(own) && !licence.Launched)
                row.AddChild(Confirm("hangar-sell", () => Send(new WFCompanyRequest(WFCompanyAction.SellLicence) { CompanyId = own.Id, LicenceId = licence.Id })));
            var entry = Column(6);
            entry.AddChild(row);
            if (canLaunch)
            {
                var name = new LineEdit { Text = licence.ShipName, PlaceHolder = Text("hangar-ship-name"), HorizontalExpand = true,
                    ToolTip = Text("hangar-ship-name-help"), IsValid = value => WFRoundSetupPolicy.TryNormalizeTitle(value, out _) };
                void SaveName() => Send(new WFCompanyRequest(WFCompanyAction.SetLicenceName) { CompanyId = own.Id, LicenceId = licence.Id, Name = name.Text });
                name.OnTextEntered += _ => SaveName();
                entry.AddChild(Row(name, Button("save", SaveName)));
            }
            fleet.AddChild(Card(entry));
        }
        page.AddChild(Card(fleet));
        if (!IsOwner(own))
            return page;

        var owner = Column(6);
        owner.AddChild(Heading("hangar-owner"));
        var fee = Number(Text("hangar-launch-fee"), own.LaunchFee);
        owner.AddChild(Row(new Label { Text = Text("hangar-launch-fee") }, fee, Button("save", () =>
            Send(new WFCompanyRequest(WFCompanyAction.SetLaunchFee) { CompanyId = own.Id, Amount = ParseNumber(fee) }))));
        owner.AddChild(Paragraph(Text("hangar-launch-fee-help")));
        if (own.Licences.Length < WFCompanyPolicy.MaximumLicences && own.LicenceOffers.Length > 0)
        {
            var picker = new OptionButton { HorizontalExpand = true };
            for (var i = 0; i < own.LicenceOffers.Length; i++)
                picker.AddItem(Text("hangar-offer", ("name", own.LicenceOffers[i].Name), ("price", Credits(own.LicenceOffers[i].Price))), i);
            _licenceOffer = Math.Clamp(_licenceOffer, 0, own.LicenceOffers.Length - 1);
            picker.SelectId(_licenceOffer);
            picker.OnItemSelected += args =>
            {
                picker.SelectId(args.Id);
                _licenceOffer = args.Id;
            };
            var buy = new ConfirmButton { Text = Text("hangar-buy"), ConfirmationText = Text("confirm") };
            buy.OnPressed += _ => Send(new WFCompanyRequest(WFCompanyAction.BuyLicence)
            {
                CompanyId = own.Id, Vessel = own.LicenceOffers[_licenceOffer].Vessel,
            });
            owner.AddChild(Row(picker, buy));
            owner.AddChild(Paragraph(Text("hangar-buy-help", ("multiplier", WFCompanyPolicy.LicenceMultiplier),
                ("minimum", Credits(WFCompanyPolicy.LicenceMinimum)))));
        }
        page.AddChild(Card(owner));
        return page;
    }
}
