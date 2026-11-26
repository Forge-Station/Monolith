using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private string _shipName = string.Empty;
    private bool _shipNameDirty;
    private int? _shipNameLobby;
    private LobbyPage? _pendingShipPage;

    private void BuildShipName(BoxContainer section, WFRoundSetupLobby lobby, bool host)
    {
        if (!host || lobby.Started)
        {
            if (lobby.ShipName.Length > 0)
                section.AddChild(PlainDescription(Text("ship-named", ("name", lobby.ShipName))));
            return;
        }
        if (_shipNameLobby != lobby.Id || !_shipNameDirty)
        {
            _shipNameLobby = lobby.Id;
            _shipName = lobby.ShipName;
            _shipNameDirty = false;
        }
        var name = new LineEdit
        {
            Name = "RoundSetupShipName", Text = _shipName, HorizontalExpand = true,
            PlaceHolder = Text("ship-name-placeholder"), ToolTip = Text("ship-name-help"),
            IsValid = value => WFRoundSetupPolicy.TryNormalizeTitle(value, out _),
        };
        name.OnTextChanged += _ => { _shipName = name.Text; _shipNameDirty = true; };
        name.OnTextEntered += _ => SaveShipName(lobby.Id);
        section.AddChild(Heading("ship-name"));
        section.AddChild(name);
    }

    private void SaveShipName(int lobby, LobbyPage? next = null)
    {
        if (!WFRoundSetupPolicy.TryNormalizeTitle(_shipName, out var name))
            return;
        _shipName = name;
        _pendingShipPage = next;
        SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetShipName) { LobbyId = lobby, ShipName = name });
    }

    private void ReceiveShipName(WFRoundSetupState state)
    {
        if (state.Message != null)
            _pendingShipPage = null;
        if (!_shipNameDirty || state.Lobbies.FirstOrDefault(lobby => lobby.Id == state.OwnLobby)?.ShipName != _shipName)
            return;
        _shipNameDirty = false;
        if (_pendingShipPage is not { } page)
            return;
        _pendingShipPage = null;
        _lobbyPage = page;
        _pageScroll.VScroll = 0;
    }
}
