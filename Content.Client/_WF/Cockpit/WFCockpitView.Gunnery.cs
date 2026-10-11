using System.Linq;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.Cockpit;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

public sealed partial class WFCockpitView
{
    private Control _gunneryModes = default!;
    private Control _gunneryDeck = default!;
    private Control _gunneryContents = default!;
    private Button _flightMode = default!;
    private Button _gunsMode = default!;
    private WFCockpitGunneryPanel? _gunneryPanel;
    private WFCockpitFireInput _gunneryInput = default!;
    private WFCockpitAimPreview _aimPreview = default!;
    private WFCockpitAimLines _aimLines = default!;
    private NetEntity? _gunneryConsole;
    private bool _showGunnery;
    private bool _gunneryControlling;

    private void InitializeGunnery(MainViewport viewport)
    {
        var modes = new ButtonGroup();
        _flightMode = Button("wf-cockpit-mode-flight", true);
        _flightMode.Name = "CockpitModeFlight";
        _gunsMode = Button("wf-cockpit-mode-guns", true);
        _gunsMode.Name = "CockpitModeGuns";
        _gunsMode.ToolTip = Loc.GetString("wf-cockpit-mode-guns-help");
        _flightMode.Group = _gunsMode.Group = modes;
        _flightMode.AddStyleClass("WfCompact");
        _gunsMode.AddStyleClass("WfCompact");
        _flightMode.Pressed = true;
        _flightMode.OnPressed += _ => SelectGunnery(false);
        _gunsMode.OnPressed += _ => SelectGunnery(true);
        _gunneryModes = Row(_flightMode, _gunsMode);
        _gunneryModes.Name = "CockpitGunneryModes";
        _gunneryModes.Visible = false;
        _gunneryContents = new Control { HorizontalExpand = true, VerticalExpand = true };
        _gunneryDeck = Panel("wf-cockpit-gunnery", _gunneryContents, true);
        _gunneryDeck.Name = "CockpitGunnery";
        _gunneryDeck.Visible = false;
        AddChild(_gunneryModes);
        AddChild(_gunneryDeck);
        var navigation = _console.FindControl<NavScreen>("NavContainer").FindControl<ShuttleNavControl>("NavRadar");
        _gunneryInput = new WFCockpitFireInput(viewport, navigation, () => _gunneryControlling && !_restored,
            () => _gunneryPanel?.HasSelectedWeapons == true, AimGunnery, _lease);
        AddChild(_gunneryInput);
        _aimPreview = new WFCockpitAimPreview(() => _gunneryControlling ? _gunneryInput.AimTarget : null,
            weapon => _gunneryPanel?.IsSelected(weapon) == true);
        _aimLines = new WFCockpitAimLines(navigation, _aimPreview) { Name = "CockpitAimLines" };
        navigation.AddChild(_aimLines);
        LayoutContainer.SetAnchorPreset(_aimLines, LayoutContainer.LayoutPreset.Wide);
        _lease.Remember(() =>
        {
            _aimLines.Orphan();
            _aimLines.Dispose();
        });
        var overlays = IoCManager.Resolve<IOverlayManager>();
        var aimOverlay = new WFCockpitAimOverlay(_aimPreview, viewport.Viewport);
        overlays.RemoveOverlay<WFCockpitAimOverlay>();
        overlays.AddOverlay(aimOverlay);
        _lease.Remember(() =>
        {
            if (overlays.TryGetOverlay<WFCockpitAimOverlay>(out var current) && current == aimOverlay)
                overlays.RemoveOverlay(aimOverlay);
        });
        _console.WfCockpitGunneryUpdated += UpdateGunnery;
        _lease.Remember(() =>
        {
            _console.WfCockpitGunneryUpdated -= UpdateGunnery;
            _console.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(false));
        });
    }

    /// <summary>Updates the reachable gun bank, clears selection when its console changes and re-evaluates gun control.</summary>
    public void UpdateGunnery(WFCockpitGunneryStateMessage message)
    {
        if (_restored)
            return;
        var available = message.Console != null && message.State is { Connected: true };
        var console = available ? message.Console : null;
        if (_gunneryConsole != console)
        {
            _gunneryInput.Cancel();
            _gunneryContents.DisposeAllChildren();
            _gunneryPanel = null;
            _gunneryConsole = console;
        }
        if (available)
        {
            if (_gunneryPanel == null)
            {
                _gunneryPanel = new WFCockpitGunneryPanel();
                _gunneryPanel.Command += SendGunnery;
                _gunneryContents.AddChild(_gunneryPanel);
            }
            _gunneryPanel.UpdateState(message.State);
        }
        _aimPreview.UpdateWeapons(available ? message.State!.FireControllables : null);
        var visibilityChanged = _gunneryModes.Visible != available;
        _gunneryModes.Visible = available;
        if (!available && _showGunnery)
            SelectGunnery(false);
        else
        {
            SyncGunneryControl();
            if (visibilityChanged)
                InvalidateMeasure();
        }
    }

    /// <summary>Shows the weapon bank or the flight instruments; selected weapons stay armed on FLIGHT.</summary>
    public void SelectGunnery(bool selected)
    {
        var showing = selected && _gunneryConsole != null;
        if (_showGunnery != showing)
            _gunneryInput.Cancel();
        _showGunnery = showing;
        _flightMode.Pressed = !_showGunnery;
        _gunsMode.Pressed = _showGunnery;
        _left.Visible = _camera.Visible = _flight.Visible = !_showGunnery;
        _gunneryDeck.Visible = _showGunnery;
        SyncGunneryControl();
        InvalidateMeasure();
    }

    /// <summary>Holds gun control while GUNS is open or any weapon is selected, and tells the server when that changes.</summary>
    private void SyncGunneryControl()
    {
        if (_restored)
            return;
        var controlling = _gunneryConsole != null && (_showGunnery || _gunneryPanel?.HasSelectedWeapons == true);
        if (_gunneryControlling == controlling)
            return;
        _gunneryControlling = controlling;
        _gunneryInput.Cancel();
        _console.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true, controlling));
    }

    private void SendGunnery(BoundUserInterfaceMessage command)
    {
        if (!_restored && _gunneryControlling && _gunneryConsole is { } console)
            _console.WfSendCockpitGunnery(new WFCockpitGunneryCommandMessage(console, command));
    }

    private void AimGunnery(EntityCoordinates coordinates, bool fire)
    {
        if (!_gunneryControlling || _gunneryConsole == null || _gunneryPanel == null || !_gunneryPanel.HasSelectedWeapons)
            return;
        var entities = IoCManager.Resolve<IEntityManager>();
        var selected = fire ? _gunneryPanel.SelectedWeapons : new List<NetEntity>();
        SendGunnery(new FireControlConsoleFireMessage(selected, entities.GetNetCoordinates(coordinates)));
    }

    private float MeasureGunnery(float width, float height)
    {
        if (!_gunneryModes.Visible)
            return 0;
        _gunneryModes.Measure(new Vector2(width, height));
        var modesHeight = _gunneryModes.DesiredSize.Y + 6;
        if (_showGunnery)
            _gunneryDeck.Measure(new Vector2(width, Math.Max(80, height - modesHeight)));
        return modesHeight;
    }

    private float ArrangeGunnery(float width, float top, float height)
    {
        if (!_gunneryModes.Visible)
            return 0;
        var modesHeight = _gunneryModes.DesiredSize.Y;
        _gunneryModes.Arrange(UIBox2.FromDimensions(new Vector2(8, top), new Vector2(width, modesHeight)));
        if (_showGunnery)
            _gunneryDeck.Arrange(UIBox2.FromDimensions(new Vector2(8, top + modesHeight + 6),
                new Vector2(width, Math.Max(80, height - modesHeight - 6))));
        return modesHeight + 6;
    }
}
