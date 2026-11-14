using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private string _postingDraft = string.Empty;

    /// <summary>Players already in the round can only form a crew around their current character.</summary>
    private void BuildFormedScenario()
    {
        _body.AddChild(Heading("formed-create"));
        _body.AddChild(Description("formed-create-help"));
        _body.AddChild(Heading("crew-title"));
        var crewName = new LineEdit { Text = _newTitle, PlaceHolder = Text("crew-title-placeholder"), HorizontalExpand = true,
            ToolTip = Text("crew-title-help") };
        crewName.OnTextChanged += _ => _newTitle = crewName.Text;
        _body.AddChild(crewName);
        _body.AddChild(DescriptionEditor(_newDescription, value => _newDescription = value));
        var card = Column(8);
        var title = PlainDescription(ScenarioName(WFRoundScenario.Formed));
        title.StyleClasses.Add("WFRoundSetupHeading");
        card.AddChild(title);
        card.AddChild(WFScenarioDifficultyLabel.Create(WFRoundScenario.Formed));
        card.AddChild(Description(ScenarioDescription(WFRoundScenario.Formed)));
        BuildCreateCompany(_state ?? new WFRoundSetupState());
        var create = Button("create", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            CompanyId = CreateCompany(_state),
            Scenario = WFRoundScenario.Formed,
            Title = _newTitle,
            Description = _newDescription,
        }));
        CheckDescription(create, () => _newDescription);
        create.Name = "RoundSetupCreate" + WFRoundScenario.Formed;
        card.AddChild(create);
        _body.AddChild(Panel(card));
    }

    /// <summary>When the posting cooldown shown ends, so the button re-enables without a new state.</summary>
    private TimeSpan? _postingReadyAt;

    /// <summary>The host's recruiting switch and open-invitation posting.</summary>
    private void BuildRecruiting(WFRoundSetupLobby lobby)
    {
        var content = Column(6);
        content.AddChild(Heading("recruiting"));
        content.AddChild(Description("recruiting-help"));
        var toggle = new CheckBox { Text = Text("recruiting-toggle"), Pressed = lobby.RecruitingOn, Name = "CrewRecruiting" };
        toggle.OnToggled += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetRecruiting)
        {
            LobbyId = lobby.Id,
            Recruiting = args.Pressed,
        });
        content.AddChild(toggle);
        if (lobby.RecruitingOn && !lobby.Recruiting)
            content.AddChild(Description("recruiting-hidden"));

        content.AddChild(Heading("posting"));
        content.AddChild(Description("posting-help"));
        var text = new LineEdit { Text = _postingDraft, PlaceHolder = Text("posting-placeholder"), HorizontalExpand = true,
            Name = "CrewPostingText" };
        text.IsValid = value => value.Length <= WFRoundSetupPolicy.MaximumPostingLength;
        text.OnTextChanged += _ => _postingDraft = text.Text;
        var wait = lobby.NextPosting is { } next && next > _watchTiming.CurTime ? next - _watchTiming.CurTime : (TimeSpan?) null;
        _postingReadyAt = lobby.NextPosting is { } ready && wait != null ? ready : null;
        var post = Button("posting-send", () =>
        {
            if (!WFRoundSetupPolicy.TryNormalizePosting(_postingDraft, out var posting))
                return;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Post) { LobbyId = lobby.Id, Posting = posting });
            _postingDraft = string.Empty;
        }, wait != null, "posting-wait");
        post.Name = "CrewPostingSend";
        post.AddStyleClass("ButtonAccept");
        content.AddChild(FlowRow(text, post));
        if (wait is { } remaining)
            content.AddChild(PlainDescription(Text("posting-cooldown", ("minutes", Math.Max(1, (int) Math.Ceiling(remaining.TotalMinutes))))));
        _body.AddChild(Panel(content));
    }

    private bool _createForCompany = true;

    /// <summary>The company a new crew flies under: the creator's, unless they untick it.</summary>
    private int? CreateCompany(WFRoundSetupState? state) =>
        _createForCompany && state?.FieldCompanies.Length > 0 ? state.FieldCompanies[0].Id : null;

    /// <summary>A tick box on the Create page for players who may run crews for their company.</summary>
    private void BuildCreateCompany(WFRoundSetupState state)
    {
        if (state.FieldCompanies.Length == 0)
            return;
        var company = state.FieldCompanies[0];
        var box = new CheckBox { Text = Text("company-crew-create", ("tag", company.Tag), ("company", company.Name)), Pressed = _createForCompany,
            Name = "CrewCreateCompany" };
        box.OnToggled += args => _createForCompany = args.Pressed;
        _body.AddChild(box);
    }

    /// <summary>A compact company picker on the crew's front page.</summary>
    private void BuildCrewCompanyRow(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        if (state.FieldCompanies.Length == 0 && lobby.CompanyId == null)
            return;
        _body.AddChild(Row(new Label { Text = Text("company-crew-heading"), VerticalAlignment = VAlignment.Center }, CompanyPicker(lobby, state)));
    }

    private WFRoundSetupOptionButton CompanyPicker(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        var picker = new WFRoundSetupOptionButton { HorizontalExpand = true, Name = "CrewCompany" };
        picker.AddItem(Text("company-crew-none"), 0);
        foreach (var company in state.FieldCompanies)
            picker.AddItem($"[{company.Tag}] {company.Name}", company.Id);
        if (lobby.CompanyId is { } current && state.FieldCompanies.All(company => company.Id != current))
            picker.AddItem($"[{lobby.CompanyTag}] {lobby.CompanyName}", current);
        picker.SelectId(lobby.CompanyId ?? 0);
        picker.OnItemSelected += args =>
        {
            picker.SelectId(args.Id);
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetCompany) { LobbyId = lobby.Id, CompanyId = args.Id == 0 ? null : args.Id });
        };
        return picker;
    }

    /// <summary>Lets the host fly the crew under their player company.</summary>
    private void BuildCrewCompany(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        if (state.FieldCompanies.Length == 0 && lobby.CompanyId == null)
            return;
        var content = Column(6);
        content.AddChild(Heading("company-crew-heading"));
        content.AddChild(Description("company-crew-help"));
        content.AddChild(CompanyPicker(lobby, state));
        _body.AddChild(Panel(content));
    }
}
