using System.Linq;
using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._WF.NpcCrew;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Radio;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.NpcCrew;

public sealed partial class WFCrewSetupWindow
{
    private readonly OptionButton _order = new();
    private readonly OptionButton _target = new() { Filterable = true };
    private readonly LineEdit _x = new() { Text = "0" };
    private readonly LineEdit _y = new() { Text = "0" };
    private readonly LineEdit _range = new() { Text = "60" };
    private BoxContainer _targetLine = default!;
    private BoxContainer _destinationLine = default!;
    private BoxContainer _manualRangeLine = default!;
    private readonly BoxContainer _removeConfirm = Column();
    private readonly BoxContainer _manualConfirm = Column();

    private void BuildManagement()
    {
        var tabs = new TabContainer { VerticalExpand = true };
        _manageView.AddChild(tabs);
        BoxContainer Page(string key)
        {
            var page = Column(12);
            page.Margin = new Thickness(10);
            tabs.AddChild(new ScrollContainer { HScrollEnabled = false, ReserveScrollbarSpace = true, Children = { page } });
            tabs.SetTabTitle(tabs.ChildCount - 1, Text(key));
            return page;
        }
        BuildObjectives(Page("tab-objectives"));
        var rules = Page("settings-tab");
        rules.AddChild(Help("settings-help"));
        _crewSettings = new SettingsForm(_prototypes);
        rules.AddChild(_crewSettings.Body);
        rules.AddChild(Button("save-settings", () => SendCrew(WFCrewSetupAction.Rules)));

        var admin = Page("advanced-tab");
        admin.AddChild(Heading("immediate-heading"));
        admin.AddChild(Help("immediate-help"));
        foreach (var order in Enum.GetValues<WFPilotOrder>())
            _order.AddItem(Text($"order-{order.ToString().ToLowerInvariant()}"), (int) order);
        SelectOnClick(_order);
        SelectOnClick(_target);
        _order.OnItemSelected += _ => { _manualConfirm.Visible = false; UpdateOrderFields(); };
        admin.AddChild(Line("immediate-order", _order));
        _targetLine = Line("target-grid", _target);
        _destinationLine = Line("destination", _x, _y);
        _manualRangeLine = Line("range-metres", _range);
        admin.AddChild(_targetLine);
        admin.AddChild(_destinationLine);
        admin.AddChild(_manualRangeLine);
        admin.AddChild(Button("issue-order", () => _manualConfirm.Visible = true));
        _manualConfirm.AddChild(Help("confirm-order-help"));
        _manualConfirm.AddChild(Buttons(Button("confirm-order", () => { SendCrew(WFCrewSetupAction.Orders); _manualConfirm.Visible = false; }),
            Button("cancel", () => _manualConfirm.Visible = false)));
        admin.AddChild(_manualConfirm);
        admin.AddChild(Heading("remove-heading"));
        admin.AddChild(Help("remove-help"));
        admin.AddChild(Button("clear", () => _removeConfirm.Visible = true));
        _removeConfirm.AddChild(Help("confirm-remove-help"));
        _removeConfirm.AddChild(Buttons(Button("confirm-remove", () => { SendCrew(WFCrewSetupAction.Clear); _removeConfirm.Visible = false; }),
            Button("cancel", () => _removeConfirm.Visible = false)));
        admin.AddChild(_removeConfirm);
        UpdateOrderFields();
    }

    private void UpdateOrderFields()
    {
        var order = (WFPilotOrder) _order.SelectedId;
        _targetLine.Visible = order is WFPilotOrder.Dock or WFPilotOrder.Follow;
        _destinationLine.Visible = order is WFPilotOrder.GoTo or WFPilotOrder.Loiter;
        _manualRangeLine.Visible = order is WFPilotOrder.Loiter or WFPilotOrder.Follow;
    }

    private void RefreshTargets()
    {
        FillTargets(_target);
        FillTargets(_objectiveTarget);
    }

    private void FillTargets(OptionButton button)
    {
        var previous = button.ItemCount > 0 ? button.SelectedMetadata as WFCrewSetupGrid : null;
        RefreshGridChoices(button, "select-target", previous?.Id, _selectedCrew?.Grid);
    }

    private void SendCrew(WFCrewSetupAction action)
    {
        if (CurrentCrew is not { } crew)
            return;
        var mission = _crewSettings.Read(crew.Group);
        if (action is WFCrewSetupAction.Rules or WFCrewSetupAction.Orders && !ValidateNavigation(mission))
            return;
        if (action == WFCrewSetupAction.Orders)
        {
            mission.Order = (WFPilotOrder) _order.SelectedId;
            mission.Target = (_target.SelectedMetadata as WFCrewSetupGrid)?.Id;
            if (_targetLine.Visible && mission.Target == null)
            {
                Plain(_status, Text("choose-target"));
                return;
            }
            mission.Destination = _destinationLine.Visible ? new Vector2(Number(_x), Number(_y)) : Vector2.Zero;
            mission.Range = _manualRangeLine.Visible ? Number(_range) : 60;
            if (!InBounds(mission.Destination)
                || !float.IsFinite(mission.Range) || mission.Range is < 1 or > WFCrewLimits.MaxRange)
            {
                Plain(_status, Text("bad-numbers"));
                return;
            }
        }
        Request(new WFCrewSetupRequest { Action = action, Grid = crew.Grid, Mission = mission });
    }

    /// <summary>Whether two missions agree on every setting the form edits.</summary>
    private static bool SameSettings(WFCrewMission a, WFCrewMission b)
    {
        return a.Callsign == b.Callsign && a.Battlegroup == b.Battlegroup && a.Company == b.Company && a.Faction == b.Faction
            && a.LocalChannel == b.LocalChannel && a.AlertChannel == b.AlertChannel && a.HeaveTo == b.HeaveTo
            && a.BoardingResponse == b.BoardingResponse && a.DockingResponse == b.DockingResponse
            && WFCrewNavigationSettings.Fields.All(field => field.Get(a.Navigation) == field.Get(b.Navigation));
    }

    /// <summary>A finite point within the coordinate bound the server enforces.</summary>
    private static bool InBounds(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y)
        && MathF.Abs(point.X) <= WFCrewLimits.MaxCoordinate && MathF.Abs(point.Y) <= WFCrewLimits.MaxCoordinate;

    private static string Cap(string text, int max) => text.Length > max ? text.Substring(0, max) : text;

    /// <summary>Independent setup and live settings prevent draft creation from retargeting an existing crew.</summary>
    private sealed class SettingsForm
    {
        public readonly BoxContainer Body = Column(12);
        private readonly LineEdit _callsign = new();
        private readonly LineEdit _battlegroup = new();
        private readonly OptionButton _company = new();
        private readonly OptionButton _faction = new();
        private readonly OptionButton _local = new();
        private readonly OptionButton _alert = new();
        private readonly OptionButton _boarding = new();
        private readonly OptionButton _docking = new();
        private readonly OptionButton _disengage = new();
        private readonly OptionButton _skill = new();
        private readonly CheckBox _heave = new() { Pressed = true };

        /// <summary>Not edited here; kept from the loaded crew so applying settings never turns a raider's radio on.</summary>
        private bool _callsForHelp = true;

        /// <summary>Not edited here; kept from the loaded crew so applying settings never turns a hub's guns off.</summary>
        private bool _faceThreats;
        private readonly List<string> _companies;
        private readonly List<string> _factions;
        private readonly List<string> _channels;
        private readonly NavigationForm _navigation;

        public SettingsForm(IPrototypeManager prototypes)
        {
            _companies = new List<string> { string.Empty };
            _companies.AddRange(prototypes.EnumeratePrototypes<CompanyPrototype>().Select(item => item.ID).Order());
            _factions = prototypes.EnumeratePrototypes<NpcFactionPrototype>().Select(item => item.ID).Order().ToList();
            _channels = prototypes.EnumeratePrototypes<RadioChannelPrototype>().Select(item => item.ID).Order().ToList();
            _callsign.IsValid = text => text.Length <= WFCrewLimits.MaxCallsign;
            _battlegroup.IsValid = text => text.Length <= WFCrewLimits.MaxBattlegroup;
            Fill(_company, _companies, string.Empty);
            Fill(_faction, _factions, "WFCrew");
            Fill(_local, _channels, "Traffic");
            Fill(_alert, _channels, "Common");
            foreach (var button in new[] { _local, _alert })
            {
                for (var index = 0; index < _channels.Count; index++)
                    button.SetItemText(index, Loc.GetString(prototypes.Index<RadioChannelPrototype>(_channels[index]).Name));
            }
            foreach (var button in new[] { _boarding, _docking })
            {
                foreach (var response in Enum.GetValues<WFCrewSecurityResponse>())
                    button.AddItem(Text($"response-{response.ToString().ToLowerInvariant()}"), (int) response);
                button.SelectId((int) WFCrewSecurityResponse.Hostile);
                SelectOnClick(button);
            }
            Body.AddChild(Line("callsign", _callsign));
            Body.AddChild(Line("battlegroup", _battlegroup));
            Body.AddChild(Help("battlegroup-help"));
            Body.AddChild(Line("company", _company));
            Body.AddChild(Line("faction", _faction));
            Body.AddChild(Help("affiliation-help"));
            Body.AddChild(Heading("security-heading"));
            Body.AddChild(Line("boarding-rule", _boarding));
            Body.AddChild(Line("docking-rule", _docking));
            foreach (var rule in Enum.GetValues<WFCrewDisengage>())
                _disengage.AddItem(Text($"disengage-{rule.ToString().ToLowerInvariant()}"), (int) rule);
            _disengage.SelectId((int) WFCrewDisengage.Disable);
            SelectOnClick(_disengage);
            Body.AddChild(Line("disengage-rule", _disengage));
            Body.AddChild(Help("disengage-help"));
            foreach (var level in Enum.GetValues<WFCrewSkill>())
                _skill.AddItem(Text($"skill-{level.ToString().ToLowerInvariant()}"), (int) level);
            _skill.SelectId((int) WFCrewSkill.Veteran);
            SelectOnClick(_skill);
            Body.AddChild(Line("skill", _skill));
            Body.AddChild(Help("skill-help"));
            Body.AddChild(Help("security-detail"));
            _heave.Text = Text("heave");
            Body.AddChild(_heave);
            var radio = Column();
            radio.Visible = false;
            radio.AddChild(Line("local-channel", _local));
            radio.AddChild(Line("alert-channel", _alert));
            Body.AddChild(Button("radio-options", () => radio.Visible = !radio.Visible));
            Body.AddChild(radio);
            _navigation = new NavigationForm(prototypes);
            Body.AddChild(_navigation.Body);
        }

        public WFCrewMission Read(string group) => new()
        {
            Group = group, Callsign = _callsign.Text, Battlegroup = _battlegroup.Text.Trim(), Company = _companies[_company.SelectedId], Faction = _factions[_faction.SelectedId],
            LocalChannel = _channels[_local.SelectedId], AlertChannel = _channels[_alert.SelectedId], HeaveTo = _heave.Pressed,
            CallsForHelp = _callsForHelp, FaceThreats = _faceThreats,
            BoardingResponse = (WFCrewSecurityResponse) _boarding.SelectedId, DockingResponse = (WFCrewSecurityResponse) _docking.SelectedId, Disengage = (WFCrewDisengage) _disengage.SelectedId, Skill = (WFCrewSkill) _skill.SelectedId,
            Navigation = _navigation.Read(),
        };

        public void Load(WFCrewMission mission)
        {
            _callsign.Text = Cap(mission.Callsign, WFCrewLimits.MaxCallsign);
            _battlegroup.Text = Cap(mission.Battlegroup, WFCrewLimits.MaxBattlegroup);
            _company.TrySelectId(Math.Max(0, _companies.IndexOf(mission.Company)));
            _faction.TrySelectId(Math.Max(0, _factions.IndexOf(mission.Faction)));
            _local.TrySelectId(Math.Max(0, _channels.IndexOf(mission.LocalChannel)));
            _alert.TrySelectId(Math.Max(0, _channels.IndexOf(mission.AlertChannel)));
            _boarding.SelectId((int) mission.BoardingResponse);
            _disengage.SelectId((int) mission.Disengage);
            _skill.SelectId((int) mission.Skill);
            _docking.SelectId((int) mission.DockingResponse);
            _heave.Pressed = mission.HeaveTo;
            _callsForHelp = mission.CallsForHelp;
            _faceThreats = mission.FaceThreats;
            _navigation.Load(mission.Navigation);
        }
    }
}
