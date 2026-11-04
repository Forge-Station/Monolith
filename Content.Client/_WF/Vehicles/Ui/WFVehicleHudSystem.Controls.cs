using System.Linq;
using System.Numerics;
using Content.Client.Actions;
using Content.Client._WF.CombatConsole;
using Content.Client.UserInterface.Controls;
using Content.Shared.Actions;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Vehicles.Ui;

public sealed partial class WFVehicleHudSystem
{
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    private static readonly HashSet<string> VehicleActions = new()
    {
        "WFActionVehicleOvercharge", "ActionVehicleToggleView", "ActionVehicleLock", "WFActionVehicleScanner",
        "WFActionVehicleSteeringMode", "CMUActionVehicleEmergencyLights", "CMUActionCargoVehicleReturn",
        "CMUActionCargoVehicleToggleBay", "CMUActionCargoVehicleSelfDestruct",
    };

    /// <summary>Round buttons in a row: the driver's switches, the seat's actions, then the equipment tabs.</summary>
    private const int ButtonColumns = 8;

    private readonly Dictionary<EntityUid, WFVehicleRoundButton> _vehicleButtons = new();
    private GridContainer _controls = default!;
    private BoxContainer _parts = default!;
    private BoxContainer _detailHost = default!;
    private Label _frameHealth = default!;
    private float _statusElapsed;

    private void InitializeVehicleControls(params Control[] switches)
    {
        _controls = new GridContainer { Columns = ButtonColumns, HSeparationOverride = 4, VSeparationOverride = 4,
            HorizontalAlignment = Control.HAlignment.Left };
        foreach (var control in switches)
        {
            _controls.AddChild(control);
        }

        _parts = WFInstrumentTheme.Column();
        _detailHost = _panel.Details;
        var scroll = new ScrollContainer { SetHeight = 220, HorizontalExpand = true, HScrollEnabled = false, Visible = false };
        scroll.AddChild(_parts);
        _detailHost.AddChild(scroll);

        var condition = new WFVehicleRoundButton(WFVehicleGlyph.Condition, "wf-vehicle-tab-parts");
        condition.OnPressed += _ => ShowDetail(scroll);
        _controls.AddChild(condition);

        _panel.RightBank.AddChild(_controls);
    }

    /// <summary>Shows one panel in the tray, or closes the tray if that panel is already open.</summary>
    private void ShowDetail(Control panel)
    {
        var show = !panel.Visible;
        foreach (var child in _detailHost.Children)
        {
            child.Visible = child == panel && show;
        }

        if (show)
            InvalidateArrangeTree(panel);
    }

    /// <summary>Children rebuilt while a panel was hidden keep a zero-height arrange unless it is redone on showing.</summary>
    private static void InvalidateArrangeTree(Control control)
    {
        control.InvalidateArrange();
        foreach (var child in control.Children)
        {
            InvalidateArrangeTree(child);
        }
    }

    /// <summary>Hosts the existing authorized equipment UI in the tray, opened from a round tab on the cluster.</summary>
    public bool TryEmbedMenu(FancyWindow menu, string title)
    {
        if (_player.LocalEntity is not { } user ||
            !(HasComp<RMCVehicleOperatorComponent>(user) || HasComp<VehicleWeaponsOperatorComponent>(user) ||
              HasComp<VehicleLockActionComponent>(user) || HasComp<RMCVehicleComponent>(user)))
            return false;
        var content = menu.ContentsContainer;
        content.Orphan();
        menu.DisposeAllChildren();
        menu.AddChild(content);
        menu.MinSize = Vector2.Zero;
        menu.SetSize = new Vector2(float.NaN, float.NaN);
        menu.MaxHeight = 240;
        menu.SetHeight = menu is VehicleWeaponsMenu ? 150 : 240;
        menu.HorizontalExpand = true;
        menu.Resizable = false;
        FlattenPanels(content);
        foreach (var child in _detailHost.Children)
        {
            child.Visible = false;
        }

        _detailHost.AddChild(menu);
        menu.Visible = false;
        var glyph = menu switch
        {
            VehicleWeaponsMenu => WFVehicleGlyph.Crosshair,
            VehicleAmmoLoaderMenu => WFVehicleGlyph.Ammo,
            _ => WFVehicleGlyph.Parts,
        };
        var tab = new WFVehicleRoundButton(glyph) { ToolTip = title };
        tab.OnPressed += _ => ShowDetail(menu);
        _controls.AddChild(tab);
        menu.OnClose += () => { tab.Orphan(); tab.Dispose(); };
        WFInstrumentTheme.Install(content);
        return true;
    }

    private static void FlattenPanels(Control control)
    {
        if (control is PanelContainer panel)
            panel.PanelOverride = WFInstrumentTheme.Face(Color.Transparent, Color.Transparent, 0);
        foreach (var child in control.Children)
        {
            FlattenPanels(child);
        }
    }

    private void RefreshVehicleControls(EntityUid vehicle, float frameTime)
    {
        var live = new HashSet<EntityUid>();
        if (_player.LocalEntity is { } user && TryComp(user, out ActionsComponent? actions))
        {
            foreach (var action in actions.Actions)
            {
                if (!TryComp(action, out MetaDataComponent? meta) || meta.EntityPrototype is not { } proto ||
                    !VehicleActions.Contains(proto.ID) || !_actions.TryGetActionData(action, out var data))
                    continue;
                live.Add(action);
                if (!_vehicleButtons.TryGetValue(action, out var button))
                {
                    button = new WFVehicleRoundButton();
                    var selectedAction = action;
                    button.OnPressed += _ =>
                    {
                        if (_actions.TryGetActionData(selectedAction, out var current))
                            _actions.TriggerAction(selectedAction, current);
                    };
                    _vehicleButtons.Add(action, button);
                    _controls.AddChild(button);
                }

                var icon = data.Toggled && data.IconOn != null ? data.IconOn : data.Icon;
                button.Icon = icon != null ? _sprites.Frame0(icon) : null;
                button.ToolTip = Name(action) + "\n" + meta.EntityDescription;
                button.Lit = data.Toggled;
                button.Disabled = !data.Enabled || data.Cooldown is { } cooldown && cooldown.End > _timing.CurTime;
            }
        }
        foreach (var (action, button) in _vehicleButtons.ToArray())
        {
            if (live.Contains(action))
                continue;
            button.Orphan();
            button.Dispose();
            _vehicleButtons.Remove(action);
        }
        _statusElapsed -= frameTime;
        if (_statusElapsed > 0f)
            return;
        _statusElapsed = 0.25f;
        _parts.DisposeAllChildren();
        _frameHealth.Text = string.Empty;
        if (!TryComp(vehicle, out HardpointSlotsComponent? slots))
            return;
        var state = slots.Ui;
        if (state.HasFrameIntegrity)
            _frameHealth.Text = Loc.GetString("wf-vehicle-hud-frame", ("percent",
                MathF.Round(100 * state.FrameIntegrity / MathF.Max(1, state.FrameMaxIntegrity))));
        foreach (var part in state.Hardpoints)
        {
            var label = new Label { Text = part.InstalledName ?? Loc.GetString("wf-vehicle-hud-empty-part"),
                ClipText = true, SetWidth = 180, ToolTip = part.InstalledName };
            var bar = new ProgressBar { MinValue = 0, MaxValue = 1, HorizontalExpand = true, SetHeight = 8,
                Value = part.HasIntegrity ? Math.Clamp(part.Integrity / MathF.Max(1, part.MaxIntegrity), 0, 1) : 0 };
            var row = WFInstrumentTheme.Row(label, bar);
            WFInstrumentTheme.Apply(row);
            _parts.AddChild(row);
        }
    }
}
