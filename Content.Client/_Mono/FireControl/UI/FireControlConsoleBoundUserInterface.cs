// Copyright Rane (elijahrane@gmail.com) 2025
// All rights reserved. Relicensed under AGPL with permission

using System.Linq;
using Content.Shared._Mono.FireControl;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client._Mono.FireControl.UI;

[UsedImplicitly]
public sealed class FireControlConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FireControlWindow? _window;

    // Forge-Change: client-side cap mirrors server MaxWeapons.
    private int _maxActiveWeapons = int.MaxValue;

    public FireControlConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<FireControlWindow>();

        _window.OnServerRefresh += OnRefreshServer;
        // Forge-Change-Start: weapon preset UI callbacks.
        _window.OnPresetNameChanged += OnPresetNameChanged;
        _window.OnPresetSaveRequested += OnPresetSaveRequested;
        // Forge-Change-End

        _window.Radar.OnRadarClick += (coords) =>
        {
            var netCoords = EntMan.GetNetCoordinates(coords);

            // Send empty list of weapons for cursor tracking only when not clicking
            // This allows guided missiles to follow the cursor without firing weapons
            if (!_window.Radar.IsMouseDown())
            {
                SendCursorUpdateMessage(netCoords);
            }
            else
            {
                // Normal fire message when actually clicking
                SendFireMessage(netCoords);
            }
        };

        _window.Radar.DefaultCursorShape = Control.CursorShape.Crosshair;

        // Add event handler for when weapons are selected/deselected
        _window.OnWeaponSelectionChanged += UpdateSelectedWeapons;
    }

    private void OnRefreshServer()
    {
        SendMessage(new FireControlConsoleRefreshServerMessage());
    }

    // Forge-Change-Start
    private void OnPresetNameChanged(int presetIndex, string name)
    {
        SendMessage(new FireControlConsoleSetPresetNameMessage(presetIndex, name));
    }

    private void OnPresetSaveRequested(int presetIndex, string name, List<string> weaponNames)
    {
        SendMessage(new FireControlConsoleSavePresetMessage(presetIndex, name, weaponNames));
    }
    // Forge-Change-End

    private void UpdateSelectedWeapons()
    {
        if (_window?.Radar is not FireControlNavControl navControl)
            return;

        var selectedWeapons = new HashSet<NetEntity>();
        foreach (var (netEntity, button) in _window.WeaponsList)
        {
            if (button.Pressed)
                selectedWeapons.Add(netEntity);
        }

        navControl.UpdateSelectedWeapons(selectedWeapons);
    }

    private void SendFireMessage(NetCoordinates coordinates)
    {
        if (_window == null)
            return;

        var selected = new List<NetEntity>();
        foreach (var button in _window.WeaponsList)
        {
            if (button.Value.Pressed)
                selected.Add(button.Key);
        }

        if (selected.Count > 0)
        {
            // Forge-Change: trim selection before sending fire message.
            if (_maxActiveWeapons > 0 && selected.Count > _maxActiveWeapons)
                selected = selected.Take(_maxActiveWeapons).ToList();

            SendMessage(new FireControlConsoleFireMessage(selected, coordinates));
        }
    }

    private void SendCursorUpdateMessage(NetCoordinates coordinates)
    {
        // Send an empty weapon list to indicate this is just a cursor update, not a firing action
        SendMessage(new FireControlConsoleFireMessage(new List<NetEntity>(), coordinates));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FireControlConsoleBoundInterfaceState castState)
            return;

        _maxActiveWeapons = castState.MaxActiveWeapons;

        // Forge-Change: feed the shield bar with the console's grid so it can resolve the local emitter.
        EntityUid? grid = null;
        if (EntMan.TryGetComponent<TransformComponent>(Owner, out var consoleXform))
            grid = consoleXform.GridUid;
        _window?.SetShuttle(grid);

        _window?.UpdateStatus(castState);
        if (_window?.Radar is FireControlNavControl navControl)
        {
            navControl.SetConsole(Owner);
            navControl.UpdateControllables(Owner, castState.FireControllables);

            // Update selected weapons when state updates
            UpdateSelectedWeapons();
        }
    }
}
