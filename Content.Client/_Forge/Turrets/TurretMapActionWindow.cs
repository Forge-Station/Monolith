using System.Numerics;
using Content.Client.ContextMenu.UI;
using Content.Shared._Forge.Turrets;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._Forge.Turrets;

/// <summary>
/// Context menu for one turret. The mode list slides out beside it.
/// A popup, so opening it does not move the command window.
/// </summary>
public sealed class TurretMapActionPopup : Popup
{
    public event Action<bool>? OnToggled;
    public event Action<TurretDoctrine>? OnDoctrineSelected;

    private readonly ContextMenuElement _title;
    private readonly ContextMenuElement _power;
    private readonly ContextMenuElement _neutral;
    private readonly ContextMenuElement _hostile;
    private readonly ContextMenuElement _friendly;
    private readonly PanelContainer _modePanel;
    private readonly BoxContainer _modes;
    private bool _enabled;

    public NetEntity Turret { get; private set; }

    public TurretMapActionPopup()
    {
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 2,
        };

        var mainPanel = MenuPanel();
        var main = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _title = new ContextMenuElement();
        var mode = new ContextMenuElement { Text = Loc.GetString("turret-command-map-mode") + "  ›" };
        _power = new ContextMenuElement();
        main.AddChild(_title);
        main.AddChild(mode);
        main.AddChild(_power);
        mainPanel.AddChild(main);

        _modePanel = MenuPanel();
        _modePanel.Visible = false;
        _modes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _neutral = ModeRow(TurretDoctrine.Neutral);
        _hostile = ModeRow(TurretDoctrine.Hostile);
        _friendly = ModeRow(TurretDoctrine.Friendly);
        _modes.AddChild(_neutral);
        _modes.AddChild(_hostile);
        _modes.AddChild(_friendly);
        _modePanel.AddChild(_modes);

        root.AddChild(mainPanel);
        root.AddChild(_modePanel);
        AddChild(root);

        mode.OnPressed += _ => ShowModes();
        mode.OnMouseEntered += _ => ShowModes();
        _power.OnPressed += _ =>
        {
            OnToggled?.Invoke(!_enabled);
            Close();
        };
        _power.OnMouseEntered += _ => _modePanel.Visible = false;

        OnPopupHide += () =>
        {
            _modePanel.Visible = false;
            if (Parent != null)
                Orphan();
        };
    }

    public void OpenFor(NetEntity turret, string name, bool enabled, TurretDoctrine doctrine, Vector2 screenPosition)
    {
        Turret = turret;
        Apply(name, enabled, doctrine);
        _modePanel.Visible = false;

        var root = UserInterfaceManager.ModalRoot;
        if (Parent == null)
            root.AddChild(this);

        // Marker position is in UI units. Place the menu beside that point and keep it on screen,
        // so a turret on the right opens the menu to its left instead of sliding to the corner.
        var anchor = screenPosition - root.GlobalPosition;
        Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var size = DesiredSize;
        if (size.X < 8f || size.Y < 8f)
            size = new Vector2(220, 96);

        var pos = new Vector2(anchor.X + 14, anchor.Y - 8);
        if (pos.X + size.X > root.Size.X)
            pos.X = anchor.X - size.X - 8;
        if (pos.Y + size.Y > root.Size.Y)
            pos.Y = anchor.Y - size.Y + 8;
        if (pos.X < 0)
            pos.X = 0;
        if (pos.Y < 0)
            pos.Y = 0;

        Open(UIBox2.FromDimensions(pos, size));
    }

    public void Apply(string name, bool enabled, TurretDoctrine doctrine)
    {
        _enabled = enabled;
        _title.Text = name;
        _power.Text = Loc.GetString(enabled
            ? "turret-command-window-stand-down"
            : "turret-command-window-activate");

        Mark(_neutral, doctrine == TurretDoctrine.Neutral, "turret-command-mode-neutral");
        Mark(_hostile, doctrine == TurretDoctrine.Hostile, "turret-command-mode-hostile");
        Mark(_friendly, doctrine == TurretDoctrine.Friendly, "turret-command-mode-friendly");
    }

    private void ShowModes()
    {
        _modePanel.Visible = true;
        InvalidateMeasure();
    }

    private ContextMenuElement ModeRow(TurretDoctrine doctrine)
    {
        var row = new ContextMenuElement();
        row.OnPressed += _ =>
        {
            OnDoctrineSelected?.Invoke(doctrine);
            Close();
        };
        return row;
    }

    private static PanelContainer MenuPanel()
    {
        var panel = new PanelContainer();
        panel.SetOnlyStyleClass(ContextMenuPopup.StyleClassContextMenuPopup);
        return panel;
    }

    private static void Mark(ContextMenuElement row, bool selected, string loc)
    {
        var text = Loc.GetString(loc);
        row.Text = selected ? $"[bold]{text}[/bold]" : text;
    }
}
