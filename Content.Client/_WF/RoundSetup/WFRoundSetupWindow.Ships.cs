using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Network;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private NetUserId? _shipCandidateOwner;
    private VesselClass _shipClass = VesselClass.All;

    private static string ShipStartKey(WFShipStartLocation location) => location switch
    {
        WFShipStartLocation.CentralDock => "ship-start-central",
        WFShipStartLocation.FactionBase => "ship-start-faction",
        _ => "ship-start-random",
    };

    private static string Price(int amount) => BankSystemExtensions.ToSpesoString(amount);

    private RichTextLabel VesselSummary(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        if (lobby.Started)
            return lobby.CurrentShip is { } current
                ? PlainDescription(Text("crew-ship-current", ("ship", current.Name))) : Description("crew-ship-missing");
        if (lobby.Vessel == null)
            return Description("ship-unselected");
        var vessel = state.Vessels.FirstOrDefault(choice => choice.Id == lobby.Vessel);
        if (vessel != null)
            return PlainDescription(Text("ship-selected", ("ship", string.IsNullOrEmpty(lobby.ShipName) ? vessel.Name : lobby.ShipName), ("price", Price(vessel.Price))));
        return _prototypes.TryIndex<VesselPrototype>(lobby.Vessel, out var prototype)
            ? PlainDescription(Text("ship-selected", ("ship", string.IsNullOrEmpty(lobby.ShipName) ? prototype.Name : lobby.ShipName), ("price", Price(prototype.Price))))
            : Description("ship-selection-unavailable");
    }

    /// <summary>Keeps vessel selection, departure and the preview together in the available page.</summary>
    private void BuildShipSettings(WFRoundSetupState state, WFRoundSetupLobby lobby, bool host)
    {
        var section = Column(8);
        var preview = GetShipPreview();
        section.AddChild(Heading("ship"));
        if (lobby.Started)
        {
            section.AddChild(VesselSummary(state, lobby));
            section.AddChild(Description("assign-ship-help"));
            if (host)
                BuildShipAssignment(section, state, lobby);
            else
                section.AddChild(Description("assign-ship-leader"));
            var current = PreviewVessel(lobby.CurrentShip?.Vessel);
            preview.SetVessel(current);
            preview.ToolTip = current?.Description;
            AddShipPreviewLayout(section, preview);
            return;
        }
        BuildShipName(section, lobby, host);
        if (!host || lobby.Started)
        {
            var vessel = PreviewVessel(lobby.Vessel);
            var summary = VesselSummary(state, lobby);
            summary.ToolTip = vessel?.Description;
            section.AddChild(summary);
            section.AddChild(Heading("ship-start"));
            section.AddChild(Description(ShipStartKey(lobby.ShipStart)));
            preview.SetVessel(vessel);
            preview.ToolTip = vessel?.Description;
            AddShipPreviewLayout(section, preview);
            return;
        }

        var selector = new WFRoundSetupOptionButton
        {
            Name = "RoundSetupShipSelector",
            HorizontalExpand = true,
            Filterable = true,
        };
        var selected = Array.FindIndex(state.Vessels, vessel => vessel.Id == lobby.Vessel);
        void Populate()
        {
            selector.ClearChoices();
            selector.AddItem(Text("ship-placeholder"), -1);
            selector.DisableItem(0, Text("ship-unselected"));
            for (var i = 0; i < state.Vessels.Length; i++)
            {
                var vessel = state.Vessels[i];
                if (i != selected && !ShipMatches(vessel))
                    continue;
                selector.AddItem(Text("ship-option", ("ship", vessel.Name), ("price", Price(vessel.Price))), i);
            }
            selector.SelectId(selected);
        }
        Populate();
        var choice = selected >= 0 ? state.Vessels[selected] : null;
        selector.ToolTip = choice?.Description;
        preview.SetVessel(PreviewVessel(choice?.Id));
        preview.ToolTip = choice?.Description;
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            selected = args.Id;
            var vessel = args.Id < 0 ? null : state.Vessels[args.Id];
            selector.ToolTip = vessel?.Description;
            preview.ToolTip = vessel?.Description;
            preview.SetVessel(PreviewVessel(vessel?.Id));
            if (vessel != null)
                SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel) { LobbyId = lobby.Id, Vessel = vessel.Id });
        };
        section.AddChild(ShipFilters(state, Populate));
        section.AddChild(selector);
        section.AddChild(PlainDescription(state.HostBalance is { } balance
            ? Text("ship-balance", ("balance", Price(balance))) : Text("ship-bank-help")));
        if (state.Vessels.Length == 0)
            section.AddChild(Description("ship-empty"));
        section.AddChild(Heading("ship-start"));
        var location = new WFRoundSetupOptionButton { Name = "RoundSetupShipStart", HorizontalExpand = true };
        location.AddItem(Text("ship-start-random"), (int) WFShipStartLocation.RandomSpace);
        var stationStart = lobby.Scenario == WFRoundScenario.FactionShip ? WFShipStartLocation.FactionBase : WFShipStartLocation.CentralDock;
        location.AddItem(Text(ShipStartKey(stationStart)), (int) stationStart);
        location.SelectId((int) lobby.ShipStart);
        location.ToolTip = Text(ShipStartKey(lobby.ShipStart) + "-help");
        location.OnItemSelected += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectShipStart)
        {
            LobbyId = lobby.Id,
            ShipStart = (WFShipStartLocation) args.Id,
        });
        section.AddChild(location);

        AddShipPreviewLayout(section, preview);
    }

    /// <summary>The leader chooses a crew member's current deed and explicitly requests its use.</summary>
    private void BuildShipAssignment(BoxContainer section, WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        section.AddChild(Heading("assign-ship-heading"));
        var choices = state.CrewShips;
        var selected = Array.FindIndex(choices, choice => choice.Owner == _shipCandidateOwner && choice.Error == null);
        if (selected < 0)
            selected = Array.FindIndex(choices, choice => choice.Error == null);
        if (selected < 0 && choices.Length > 0)
            selected = 0;
        var candidate = selected >= 0 ? choices[selected] : null;
        if (choices.Length > 0)
        {
            var selector = new WFRoundSetupOptionButton { Name = "RoundSetupAssignedShipSelector", HorizontalExpand = true };
            for (var i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                selector.AddItem(Text("assign-ship-option", ("owner", choice.OwnerName), ("ship", choice.Ship.Name)), i);
                if (choice.Error != null)
                    selector.DisableItem(i, choice.Error);
            }
            selector.SelectId(selected);
            selector.OnItemSelected += args => { _shipCandidateOwner = choices[args.Id].Owner; Redraw(); };
            section.AddChild(selector);
        }
        var reason = candidate?.Error ?? (candidate == null ? Text("assign-ship-deed") : null);
        var self = candidate?.Owner == _players.LocalSession?.UserId;
        var assign = new ConfirmButton
        {
            Name = "RoundSetupAssignShip", Text = Text(self ? "assign-ship" : "assign-ship-request"),
            ConfirmationText = Text(self ? "assign-ship-confirm" : "assign-ship-request-confirm"), MinHeight = 44, HorizontalExpand = true,
            Disabled = reason != null, ToolTip = reason ?? Text("assign-ship-help"), ResetTime = TimeSpan.FromSeconds(5),
        };
        assign.Label.ClipText = false;
        assign.AddStyleClass("ButtonAccept");
        assign.OnPressed += _ => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.AssignShip)
        {
            LobbyId = lobby.Id, Target = candidate?.Owner, Ship = candidate?.Ship.Entity,
        });
        section.AddChild(assign);
        if (reason != null)
            section.AddChild(PlainDescription(reason));
        if (state.PendingShip is { } pending)
        {
            section.AddChild(PlainDescription(Text("assign-ship-waiting", ("owner", pending.OwnerName), ("ship", pending.Ship.Name))));
            section.AddChild(Button("assign-ship-cancel", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.CancelShip)
            { LobbyId = lobby.Id, Ship = pending.Ship.Entity, ShipChangeId = pending.RequestId })));
        }
    }

    /// <summary>Owners can approve or decline a leader's request from any page of crew setup.</summary>
    private Control ShipApproval(WFRoundSetupLobby lobby, WFRoundSetupShipChoice pending)
    {
        var content = Column(6);
        content.AddChild(PlainDescription(Text("assign-ship-approval", ("leader", lobby.HostName), ("ship", pending.Ship.Name))));
        var approve = Button("assign-ship-approve", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.ApproveShip)
        { LobbyId = lobby.Id, Ship = pending.Ship.Entity, ShipChangeId = pending.RequestId }));
        approve.Name = "RoundSetupApproveShip";
        approve.AddStyleClass("ButtonAccept");
        approve.ToolTip = Text("assign-ship-help");
        var decline = Button("assign-ship-decline", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.CancelShip)
        { LobbyId = lobby.Id, Ship = pending.Ship.Entity, ShipChangeId = pending.RequestId }));
        decline.Name = "RoundSetupDeclineShip";
        decline.AddStyleClass("ButtonColorRed");
        content.AddChild(FlowRow(approve, decline));
        return Panel(content);
    }

    /// <summary>A class filter that narrows the ship selector.</summary>
    private Control ShipFilters(WFRoundSetupState state, Action changed)
    {
        var classes = state.Vessels.SelectMany(vessel => PreviewVessel(vessel.Id)?.Classes ?? new List<VesselClass>())
            .Where(shipClass => shipClass != VesselClass.All).Distinct().Order().Prepend(VesselClass.All).ToList();
        if (!classes.Contains(_shipClass))
            _shipClass = VesselClass.All;
        var filter = new OptionButton { Name = "RoundSetupShipClass", HorizontalExpand = true };
        for (var i = 0; i < classes.Count; i++)
            filter.AddItem(classes[i] == VesselClass.All ? Text("ship-class-all") : Loc.GetString($"shipyard-console-class-{classes[i]}"), i);
        filter.SelectId(classes.IndexOf(_shipClass));
        filter.OnItemSelected += args =>
        {
            filter.SelectId(args.Id);
            _shipClass = classes[args.Id];
            changed();
        };
        return filter;
    }

    private bool ShipMatches(WFRoundSetupVessel vessel) =>
        _shipClass == VesselClass.All || PreviewVessel(vessel.Id)?.Classes.Contains(_shipClass) == true;

    private VesselPrototype? PreviewVessel(string? id) =>
        id != null && _prototypes.TryIndex<VesselPrototype>(id, out var vessel) ? vessel : null;
}
