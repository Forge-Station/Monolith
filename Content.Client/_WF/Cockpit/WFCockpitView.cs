using System.Linq;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

/// <summary>The console controls assigned to the permanent HUD and selectable MFD pages.</summary>
public sealed record WFCockpitParts(Control Camera, Control Alerts, Control Instruments, Control Fuel, Control Translation, Control Flight,
    Control Shields, Control Tcas, (string Key, Control Content)[] Pages);

/// <summary>A fullscreen instrument surround using the player's real world viewport and chat.</summary>
public sealed partial class WFCockpitView : Control
{
    private readonly WFCockpitLease _lease;
    private readonly ShuttleConsoleWindow _console;
    private readonly Control _brand;
    private readonly Control _banner;
    private readonly Control _shieldContent;
    private readonly Control _left;
    private readonly Control _world;
    private readonly Control _translation;
    private readonly Dictionary<string, Button> _selectors = new();
    private readonly Dictionary<string, Control> _pageHosts = new();
    private readonly Control _right;
    private readonly Control _comms;
    private readonly Control _flight;
    private readonly Control _camera;
    private readonly Control _tcas;
    private readonly Control _shields;
    private readonly Button _swap;
    private readonly Button _expand;
    private readonly Button _maximise;
    private readonly (string Key, Control Content)[] _pages;
    private readonly (Control Dial, float Full)[] _dials;
    private bool _restored;
    private bool _compactDials;

    /// <summary>Whether the dial column is too short for full-size dials and shows the smaller faces.</summary>
    public bool CompactDials => _compactDials;

    /// <summary>Creates the cockpit, borrowing every control through the caller's lease so a failed build can be undone.</summary>
    public WFCockpitView(ShuttleConsoleWindow console, InGameScreen screen, MainViewport viewport, Action exit, WFCockpitLease lease)
    {
        _lease = lease;
        _console = console;
        Name = "WFCockpitView";
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;
        var chat = screen.ChatBox;
        var hiddenHud = new Control { Visible = false };
        hiddenHud.AddStyleClass("WfNativeStyle");
        var normalHud = new List<Control>(screen.Children);
        _lease.Clear(screen);
        foreach (var control in normalHud)
            hiddenHud.AddChild(_lease.Take(control, restoreVisibility: false));
        AddChild(hiddenHud);
        var world = _lease.Take(viewport, restoreVisibility: false);
        world.VerticalExpand = true;
        _lease.Remember(() =>
        {
            if (_worldHidden)
                viewport.Visible = true;
        });
        var communications = _lease.Take(chat, restoreVisibility: false);
        communications.VerticalExpand = true;
        if (chat is ResizableChatBox resizable)
        {
            resizable.WfCockpitDocked = true;
            _lease.Remember(() => resizable.WfCockpitDocked = false);
        }
        chat.AddStyleClass("WfNativeStyle");
        viewport.AddStyleClass("WfNativeStyle");
        _lease.Remember(() =>
        {
            chat.RemoveStyleClass("WfNativeStyle");
            viewport.RemoveStyleClass("WfNativeStyle");
        });
        var wasVisible = console.Visible;
        _lease.Remember(() => console.Visible = wasVisible);
        console.Visible = false;
        var parts = console.WfBuildCockpit(_lease);
        _pages = parts.Pages;
        _dials = WFCockpitLease.Descendants(parts.Instruments)
            .Where(control => control.Name is "CockpitHeading" or "CockpitVelocity" or "CockpitYaw")
            .Select(control => (control, control.SetHeight)).ToArray();
        _brand = BuildBrand(exit);
        _banner = parts.Alerts;
        _tcas = parts.Tcas;
        var dials = Column(Label("wf-cockpit-instruments", Accent), parts.Instruments);
        dials.SeparationOverride = 3;
        var instrumentScroll = Scroll(dials);
        instrumentScroll.Name = "CockpitInstrumentScroll";
        var instruments = Column(instrumentScroll, parts.Fuel);
        instruments.SeparationOverride = 3;
        instruments.VerticalExpand = true;
        _left = new WFInstrumentPanel
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new WFConsoleFrameStyleBox(6, housing: true),
        };
        _left.AddChild(instruments);
        _world = world;
        _translation = parts.Translation;
        _comms = Panel("wf-cockpit-comms", communications, true);
        console.WfHailJoinComms(_comms, _lease); // WOLFGATE(Hailing): the hail strip rides the communications caption row.
        var flight = Column(Scroll(parts.Flight), console.WfCockpitStatus());
        flight.VerticalExpand = true;
        _flight = Panel("wf-cockpit-flight", flight, true);
        _camera = parts.Camera;
        _shieldContent = parts.Shields;
        _shields = Panel("wf-cockpit-shields", Scroll(parts.Shields), true);
        var selectors = new BoxContainer { Name = "CockpitMfdSelectors", HorizontalExpand = true, SeparationOverride = 2 };
        var group = new ButtonGroup();
        var display = new Control { HorizontalExpand = true, VerticalExpand = true, RectClipContent = true };
        foreach (var (key, content) in _pages)
        {
            var button = Button(key + "-short", true);
            button.AddStyleClass("WfCompact");
            button.ToolTip = Loc.GetString(key);
            button.Group = group;
            button.OnPressed += _ => SelectPage(key);
            button.Pressed = key == "wf-cockpit-nav";
            selectors.AddChild(button);
            _selectors.Add(key, button);
            content.HorizontalExpand = content.VerticalExpand = true;
            _pageHosts.Add(key, content);
            display.AddChild(content);
        }
        // Short captions, with the full names in the tooltips, so the caption and the three toggles fit the narrowest MFD header.
        _swap = PanelButton("wf-cockpit-swap", "wf-cockpit-swap-help", "CockpitMfdSwap", 60);
        _swap.OnToggled += args => SetSwapped(args.Pressed);
        _expand = PanelButton("wf-cockpit-expand", "wf-cockpit-expand-help", "CockpitMfdExpand", 80);
        _expand.OnToggled += args => SetExpanded(args.Pressed);
        _maximise = PanelButton("wf-cockpit-maximise", "wf-cockpit-maximise-help", "CockpitMfdMaximise", 84);
        _maximise.OnToggled += args => SetMaximised(args.Pressed);
        var caption = Label("wf-cockpit-mfd", Accent);
        caption.HorizontalExpand = true;
        var mfd = Column(Row(caption, _swap, _expand, _maximise), selectors, display);
        mfd.VerticalExpand = true;
        _right = new WFInstrumentPanel { HorizontalExpand = true, VerticalExpand = true };
        _right.AddChild(mfd);
        _left.Name = "CockpitInstruments";
        _flight.Name = "CockpitFlight";
        var cameraName = _camera.Name;
        _lease.Remember(() => _camera.Name = cameraName);
        _camera.Name = "CockpitCamera";
        _right.Name = "CockpitMfd";
        _comms.Name = "CockpitComms";
        _shields.Name = "CockpitShields";
        _translation.Name = "CockpitTranslation";
        AddChild(_brand);
        AddChild(_banner);
        AddChild(_left);
        AddChild(_tcas);
        AddChild(_world);
        InitializeHud(screen, hiddenHud);
        AddChild(_translation);
        AddChild(_right);
        AddChild(_comms);
        AddChild(_flight);
        AddChild(_camera);
        AddChild(_shields);
        InitializeGunnery(viewport);
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
        screen.AddChild(this);
        SelectPage("wf-cockpit-nav");
        console.WfSendCockpitGunnery(new Content.Shared._WF.Cockpit.WFCockpitGunnerySessionMessage(true));
        console.WfSetCockpitActive(true);
        Install(this);
    }

    /// <summary>Switches only the MFD; cameras, flight controls, shield controls and chat stay in place.</summary>
    public void SelectPage(string key)
    {
        foreach (var (page, _) in _pages)
            _pageHosts[page].Visible = page == key;
        foreach (var (page, button) in _selectors)
            button.Pressed = page == key;
        _console.WfCockpitShipVisible(key == "wf-cockpit-ship");
        _console.WfCockpitPageSelected(key);
    }

    /// <summary>Restores the existing HUD and console before disposing the temporary surround.</summary>
    public void Restore()
    {
        if (_restored)
            return;
        _restored = true;
        Parent?.RemoveChild(this);
        try
        {
            IoCManager.Resolve<IEntityManager>().System<Content.Client._WF.Shuttles.Systems.ShuttleExternalCameraSystem>().WfEndCockpitInput();
        }
        finally
        {
            _lease.Restore();
            _console.WfSetCockpitActive(false);
            Dispose();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var margin = WFCockpitPanelLayout.Margin;
        _banner.Measure(new Vector2(availableSize.X - 2 * margin, availableSize.Y));
        var (top, left, side, bottom) = Edges(availableSize);
        UpdateExpand(availableSize, left);
        var flight = WFCockpitPanelLayout.FlightHeight(availableSize);
        var height = WFCockpitPanelLayout.Height(availableSize, top);
        PreMeasureDocked(availableSize, left, side, height, bottom);
        var frame = CurrentFrame(availableSize);
        _brand.Measure(new Vector2(left, availableSize.Y));
        var leftTop = top + _brand.DesiredSize.Y + 6;
        var leftSpan = availableSize.Y - margin - leftTop;
        _tcas.Measure(new Vector2(left, leftSpan));
        var modesHeight = MeasureGunnery(left, leftSpan - _tcas.DesiredSize.Y - 6);
        _camera.Measure(new Vector2(left, leftSpan - flight));
        var leftHeight = Math.Max(80, leftSpan - flight - _camera.DesiredSize.Y - modesHeight - _tcas.DesiredSize.Y - 18);
        if (!_showGunnery)
            FitDials(leftHeight);
        _left.Measure(new Vector2(left, leftHeight));
        _flight.Measure(new Vector2(left, flight));
        _right.Measure(frame.Mfd.Size);
        if (frame.WorldShown)
            _world.Measure(frame.World.Size);
        MeasureHud(frame, availableSize);
        _shieldContent.Measure(new Vector2(frame.Row.Width, float.PositiveInfinity));
        _shieldNeed = _shieldContent.DesiredSize.X + ShieldChrome;
        var (comms, shields) = WFCockpitPanelLayout.SplitRow(frame.Row.Width, _shieldNeed);
        _comms.Measure(new Vector2(comms, bottom));
        _shields.Measure(new Vector2(shields, bottom));
        return availableSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 size)
    {
        var margin = WFCockpitPanelLayout.Margin;
        var (top, left, _, bottom) = Edges(size);
        var frame = CurrentFrame(size);
        if (_banner.Visible)
            _banner.Arrange(UIBox2.FromDimensions(new Vector2(margin), new Vector2(size.X - 2 * margin, _banner.DesiredSize.Y)));
        var brandHeight = _brand.DesiredSize.Y;
        _brand.Arrange(UIBox2.FromDimensions(new Vector2(margin, top), new Vector2(left, brandHeight)));
        var leftTop = top + brandHeight + 6;
        var leftSpan = size.Y - margin - leftTop;
        var flight = WFCockpitPanelLayout.FlightHeight(size);
        var flightY = size.Y - flight - margin;
        var cameraHeight = _camera.DesiredSize.Y;
        var cameraY = flightY - cameraHeight - 6;
        var tcasHeight = _tcas.DesiredSize.Y;
        var tcasY = (_showGunnery ? size.Y - margin : cameraY - 6) - tcasHeight;
        var instrumentsTop = leftTop + ArrangeGunnery(left, leftTop, leftSpan - tcasHeight - 6);
        _left.Arrange(UIBox2.FromDimensions(new Vector2(margin, instrumentsTop), new Vector2(left, Math.Max(1, tcasY - instrumentsTop - 6))));
        _tcas.Arrange(UIBox2.FromDimensions(new Vector2(margin, tcasY), new Vector2(left, tcasHeight)));
        _camera.Arrange(UIBox2.FromDimensions(new Vector2(margin, cameraY), new Vector2(left, cameraHeight)));
        _flight.Arrange(UIBox2.FromDimensions(new Vector2(margin, flightY), new Vector2(left, flight)));
        _translation.Arrange(frame.Motion);
        _right.Arrange(frame.Mfd);
        if (frame.WorldShown)
            _world.Arrange(frame.World);
        ArrangeHud(frame);
        var (comms, shields) = WFCockpitPanelLayout.SplitRow(frame.Row.Width, _shieldNeed);
        _comms.Arrange(UIBox2.FromDimensions(frame.Row.TopLeft, new Vector2(comms, bottom)));
        _shields.Arrange(UIBox2.FromDimensions(frame.Row.TopLeft + new Vector2(comms + WFCockpitPanelLayout.Gap, 0), new Vector2(shields, bottom)));
        return size;
    }

    /// <summary>The top of the centre and side columns, the left and side column widths, and the communications and shield row's height.</summary>
    private (float Top, float Left, float Side, float Bottom) Edges(Vector2 size)
    {
        var top = WFCockpitPanelLayout.Margin + (_banner.Visible ? _banner.DesiredSize.Y + 6 : 0);
        var left = _showGunnery ? Math.Clamp(size.X * 0.27f, 320, 420) : Math.Clamp(size.X * 0.21f, 220, 320);
        var side = WFCockpitPanelLayout.SideWidth(size, left, _expanded && !_maximised);
        var bottom = WFCockpitPanelLayout.RowHeight(size);
        return (top, left, side, bottom);
    }

    /// <summary>Measures the motion gauges and, when maximised, the alert strip and votes first; their sizes decide the MFD's room.</summary>
    private void PreMeasureDocked(Vector2 size, float left, float side, float height, float bottom)
    {
        var strip = 0f;
        if (_maximised)
        {
            var room = Math.Max(1, height - bottom - 6);
            _crewAlerts.Measure(new Vector2(AlertBankWidth, room));
            strip = _crewAlerts.DesiredSize.X;
            var main = WFCockpitPanelLayout.MainWidth(size, left, side, strip, true);
            _votes.Measure(new Vector2(Math.Min(VoteWidth, main), Math.Max(1, room * 0.3f)));
        }
        var motionWidth = WFCockpitPanelLayout.MainWidth(size, left, side, strip, _maximised);
        _translation.Measure(new Vector2(motionWidth, height));
    }

    /// <summary>The panel rectangles for the current arrangement, from the sizes the last measure left.</summary>
    private WFCockpitPanelLayout.Frame CurrentFrame(Vector2 size)
    {
        var (top, left, side, bottom) = Edges(size);
        var strip = _maximised ? _crewAlerts.DesiredSize.X : 0;
        var votes = _maximised ? Math.Min(_votes.DesiredSize.Y, WFCockpitPanelLayout.Height(size, top) * 0.3f) : 0;
        return WFCockpitPanelLayout.Compute(new WFCockpitPanelLayout.Metrics(size, top, left, side, bottom,
            _translation.DesiredSize.Y, strip, votes), _swapped, _maximised);
    }

    /// <summary>Swaps the large dials for compact ones while the instrument column is too short to show them whole.</summary>
    private void FitDials(float columnHeight)
    {
        var compact = WFCockpitInstrumentSizing.UseCompact(columnHeight);
        if (compact == _compactDials)
            return;
        _compactDials = compact;
        foreach (var (dial, full) in _dials)
        {
            dial.SetHeight = compact ? WFCockpitInstrumentSizing.CompactHeight(full) : full;
            for (var parent = dial.Parent; parent != null && parent != this; parent = parent.Parent)
                parent.InvalidateMeasure();
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _console.WfCockpitRefresh();
    }
}
