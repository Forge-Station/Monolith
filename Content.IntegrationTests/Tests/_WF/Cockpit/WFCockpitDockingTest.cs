#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.CCVar;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks crowded dock callouts, the live port actions moved out of the approach plot, and clicking ports on the plot.</summary>
public sealed class WFCockpitDockingTest
{
    [TestCase(0f, 0f)]
    [TestCase(300f, 180f)]
    [TestCase(150f, 90f)]
    public void CrowdedPortCalloutsStayInsideThePlotWithoutOverlapping(float x, float y)
    {
        var viewport = new Vector2(300, 180);
        var occupied = new List<UIBox2>();
        for (var port = 0; port < 12; port++)
        {
            var marker = WFDockMarkerLayout.Place(new Vector2(x, y), new Vector2(24, 20), viewport, occupied);
            Assert.That(marker, Is.Not.Null);
            var bounds = marker!.Value;
            Assert.That(UIBox2.FromDimensions(Vector2.Zero, viewport).Encloses(bounds), Is.True);
            Assert.That(occupied.Any(other => other.Intersects(bounds)), Is.False,
                "Adjacent and coincident airlocks must not cover each other's numbers.");
            occupied.Add(bounds);
        }
    }

    [Test]
    public async Task PortActionsFitRetainBindingsAndReturnToTheNormalConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            var owner = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var ownGrid = entities.GetNetEntity(owner);
            var own = Port(new NetEntity(710), ownGrid);
            var other = Port(new NetEntity(711), ownGrid);
            var connected = Port(new NetEntity(712), ownGrid);
            connected.GridDockedWith = ownGrid;
            using var plot = new ShuttleDockControl
            {
                GridEntity = owner,
                DockState = new DockingInterfaceState(new Dictionary<NetEntity, List<DockingPortState>>
                {
                    [ownGrid] = new() { own },
                    [new NetEntity(900)] = new() { other, connected },
                }),
            };
            var lease = new WFCockpitLease();
            plot.WfCockpitInteraction(lease);
            using var actions = plot.WfCockpitDockActions(lease);
            pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(actions);
            Assert.That(Tree(plot).OfType<Button>(), Is.Empty, "Text panels and action buttons must not cover the approach plot.");
            var buttons = Tree(actions).OfType<Button>().ToArray();
            Assert.That(buttons, Has.Length.EqualTo(3));
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                WFInstrumentTheme.Apply(actions);
                foreach (var row in actions.Children)
                    row.Visible = true;
                var size = new Vector2(280, 140);
                actions.Measure(size);
                actions.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                foreach (var button in buttons)
                {
                    var font = button.Label.FontOverride ?? (button.Label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
                        ? styled : pair.Client.ResolveDependency<IUserInterfaceManager>().ThemeDefaults.LabelFont);
                    var textWidth = button.Text!.EnumerateRunes().Sum(rune => font.GetCharMetrics(rune, button.UIScale)?.Advance ?? 0);
                    Assert.That(button.Label.PixelWidth + 1, Is.GreaterThanOrEqualTo(textWidth), $"{theme}: {button.Text} must be readable in full.");
                    Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(280));
                }
                foreach (var row in actions.Children)
                    Assert.That(row.ToolTip, Does.Contain("external airlock"), "The complete port name remains available when its row is narrow.");
            }
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var firstRow = actions.Children.First();
            var caption = firstRow.Children.OfType<Label>().Single();
            var overCaption = new ScreenCoordinates(
                caption.GlobalPixelPosition + new Vector2(caption.PixelWidth, caption.PixelHeight) / 2f, caption.Window!.Id);
            Assert.That(ui.MouseGetControl(overCaption), Is.SameAs(firstRow),
                "A caption ignores the mouse, so its row must take the hover and the tooltip.");
            ui.SetHovered(firstRow);
            Assert.That(plot.HighlightedDock, Is.EqualTo(own.Entity), "Hovering a port row highlights its marker on the plot.");
            ui.SetHovered(null);
            Assert.That(plot.HighlightedDock, Is.Null);
            ui.SetHovered(firstRow.Children.OfType<Button>().Single());
            Assert.That(plot.HighlightedDock, Is.EqualTo(own.Entity), "Hovering a port's button highlights it too.");
            ui.SetHovered(null);
            Assert.That(plot.HighlightedDock, Is.Null);
            var requests = new List<(NetEntity From, NetEntity To)>();
            var undocks = new List<NetEntity>();
            plot.DockRequest += (from, to) => requests.Add((from, to));
            plot.UndockRequest += undocks.Add;
            Press(buttons.Single(button => button.Text == Loc.GetString("shuttle-console-view")));
            Assert.That(plot.ViewedDock, Is.EqualTo(own.Entity));
            var dock = buttons.Single(button => button.Text == Loc.GetString("shuttle-console-dock"));
            Assert.That(dock.Disabled, Is.True);
            Press(dock);
            Assert.That(requests, Is.Empty, "A dock that has not passed the plot's alignment check cannot send a request.");
            // This fixture checks leased bindings; a rendered plot supplies spatial docking eligibility.
            dock.Disabled = false;
            Press(dock);
            Press(buttons.Single(button => button.Text == Loc.GetString("shuttle-console-undock")));
            Assert.That(requests, Is.EqualTo(new[] { (own.Entity, other.Entity) }));
            Assert.That(undocks, Is.EqualTo(new[] { connected.Entity }));
            plot.Offset = new Vector2(3, 4);
            plot.BuildDocks(owner);
            Assert.That(plot.ViewedDock, Is.EqualTo(own.Entity));
            Assert.That(plot.Offset, Is.EqualTo(new Vector2(3, 4)), "State refresh must preserve an ongoing approach pan.");
            Assert.That(Tree(actions).OfType<Button>().Count(), Is.EqualTo(3));
            lease.Restore();
            Assert.That(plot.Offset, Is.EqualTo(Vector2.Zero), "Leaving the cockpit drops the approach pan.");
            Assert.That(plot.WfCockpitControls, Is.False);
            Assert.That(actions.ChildCount, Is.Zero);
            Assert.That(Tree(plot).OfType<Button>().Count(), Is.EqualTo(3), "Exiting cockpit restores the normal docking UI.");
            settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            entities.DeleteEntity(owner);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RecenterButtonReturnsAPannedApproachPlotToItsDock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var screen = new DockingScreen();
            var lease = new WFCockpitLease();
            screen.WfCockpitDock(lease);
            screen.Visible = true; // the helm shows this page by selecting it; its XAML starts hidden
            ui.WindowRoot.AddChild(screen);
            var size = new Vector2(420, 520);
            screen.Measure(size);
            screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            var recenter = Tree(screen).OfType<Button>().Single(button => button.Name == "CockpitDockRecenter");
            var plot = screen.FindControl<ShuttleDockControl>("DockingControl");
            plot.Offset = new Vector2(30, -20);
            Press(recenter);
            Assert.That(plot.Offset, Is.EqualTo(Vector2.Zero),
                "A ship with one port has no other way to bring a panned plot back.");
            plot.Offset = new Vector2(5, 5);
            lease.Restore();
            Assert.That(plot.Offset, Is.EqualTo(Vector2.Zero), "The pan is not kept for the next cockpit.");
            screen.Orphan();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public void PortPickPrefersACalloutThenTheNearestPortInReach()
    {
        var targets = new[]
        {
            new WFDockTarget(new Vector2(100, 100), 20, null),
            new WFDockTarget(new Vector2(200, 100), 20, UIBox2.FromDimensions(new Vector2(92, 92), new Vector2(24, 20))),
        };
        Assert.That(WFDockMarkerLayout.Pick(new Vector2(82, 100), targets), Is.EqualTo(0), "A port body is clickable around its centre.");
        Assert.That(WFDockMarkerLayout.Pick(new Vector2(190, 100), targets), Is.EqualTo(1));
        Assert.That(WFDockMarkerLayout.Pick(new Vector2(100, 100), targets), Is.EqualTo(1),
            "A callout drawn over another port takes the click, as it is the topmost thing on the plot.");
        Assert.That(WFDockMarkerLayout.Pick(new Vector2(150, 100), targets), Is.EqualTo(-1), "Empty plot selects nothing.");
        Assert.That(WFDockMarkerLayout.Pick(Vector2.Zero, Array.Empty<WFDockTarget>()), Is.EqualTo(-1));
        var crowded = new[] { new WFDockTarget(new Vector2(100, 100), 20, null), new WFDockTarget(new Vector2(110, 100), 20, null) };
        Assert.That(WFDockMarkerLayout.Pick(new Vector2(106, 100), crowded), Is.EqualTo(1), "Overlapping ports go to the nearest centre.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClickingAPortOnThePlotViewsItLikeThePortList(bool cockpit)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            var owner = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var grid = entities.GetNetEntity(owner);
            var visitorGrid = new NetEntity(901);
            var alpha = Port(new NetEntity(720), grid, "Alpha", Vector2.Zero);
            var bravo = Port(new NetEntity(721), grid, "Bravo", new Vector2(4, 0));
            var charlie = Port(new NetEntity(722), grid, "Charlie", new Vector2(-3, 4));
            var visitor = Port(new NetEntity(723), visitorGrid, "Visitor", new Vector2(0, -4));
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                var lease = new WFCockpitLease();
                using var screen = new DockingScreen();
                if (cockpit)
                    screen.WfCockpitDock(lease);
                else
                    screen.WfRefitInstruments();
                screen.Visible = true;
                ui.WindowRoot.AddChild(screen);
                screen.UpdateState(owner, new DockingInterfaceState(new Dictionary<NetEntity, List<DockingPortState>>
                {
                    [grid] = new() { charlie, alpha, bravo },
                    [visitorGrid] = new() { visitor },
                }));
                var size = cockpit ? new Vector2(640, 700) : new Vector2(1000, 600);
                screen.Measure(size);
                screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var plot = screen.FindControl<ShuttleDockControl>("DockingControl");
                var label = $"{theme}, {(cockpit ? "cockpit" : "windowed")}";
                Assert.That(plot.PixelWidth, Is.GreaterThan(200), label);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: the first port by name starts out viewed.");
                Assert.That(ListedPort(screen), Is.EqualTo("Alpha"), label);
                var viewed = new List<NetEntity>();
                plot.OnViewDock += viewed.Add;

                Click(ui, plot, Plotted(plot, alpha, bravo, cockpit));
                Assert.That(plot.ViewedDock, Is.EqualTo(bravo.Entity), $"{label}: a click on a port's plotted position views it.");
                Assert.That(viewed, Is.EqualTo(new[] { bravo.Entity }), $"{label}: the plot raises the event the port list raises.");
                Assert.That(ListedPort(screen), Is.EqualTo("Bravo"), $"{label}: the port list follows the click.");

                // A panned and zoomed plot is clicked where it is drawn, not where it started.
                plot.WorldRange = 12;
                if (cockpit)
                    plot.Offset = new Vector2(2, -1);
                Click(ui, plot, Plotted(plot, bravo, charlie, cockpit));
                Assert.That(plot.ViewedDock, Is.EqualTo(charlie.Entity), $"{label}: panning and zooming move the click targets with the ports.");
                Assert.That(ListedPort(screen), Is.EqualTo("Charlie"), label);
                if (cockpit)
                    Assert.That(plot.Offset, Is.EqualTo(Vector2.Zero), "Viewing a port recentres the panned plot, as the port list does.");

                plot.WorldRange = 6;
                Click(ui, plot, Plotted(plot, charlie, alpha, cockpit));
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), label);
                Assert.That(viewed, Is.EqualTo(new[] { bravo.Entity, charlie.Entity, alpha.Entity }), label);

                var bravoSpot = Plotted(plot, alpha, bravo, cockpit);
                var empty = new Vector2(plot.PixelWidth - 24, plot.PixelHeight - 24);
                Click(ui, plot, bravoSpot, function: EngineKeyFunctions.UseSecondary);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: the right button pans and never selects.");
                Click(ui, plot, Plotted(plot, alpha, visitor, cockpit));
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: another ship's port is not one of ours to view.");
                Click(ui, plot, empty);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: empty plot selects nothing.");
                Click(ui, plot, bravoSpot, empty);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: releasing away from the pressed port cancels the click.");
                Click(ui, plot, empty, bravoSpot);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: a press that began on empty plot cannot select.");
                Assert.That(viewed, Has.Count.EqualTo(3), label);

                // A press waiting for its release is forgotten when the plot is hidden, as no release comes to a hidden control.
                Click(ui, plot, bravoSpot, release: false);
                plot.Visible = false;
                plot.Visible = true;
                Click(ui, plot, bravoSpot, press: false);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: a press cannot outlive the plot it began on.");

                // Another ship's callout drawn over one of our ports is what the pilot sees on top, so it takes the click.
                var callouts = (Dictionary<NetEntity, UIBox2>) typeof(ShuttleDockControl)
                    .GetField("_wfCockpitDockCallouts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
                callouts[visitor.Entity] = UIBox2.FromDimensions(bravoSpot - new Vector2(10), new Vector2(20));
                Click(ui, plot, bravoSpot);
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), $"{label}: a callout over a port is not a click on the port beneath it.");
                MoveMouse(ui, plot, bravoSpot);
                Assert.That(plot.HighlightedDock, Is.Null, $"{label}: nor does it highlight the port beneath it.");
                callouts.Clear();
                Click(ui, plot, bravoSpot);
                Assert.That(plot.ViewedDock, Is.EqualTo(bravo.Entity), $"{label}: with the callout gone the port is clickable again.");
                Click(ui, plot, Plotted(plot, bravo, alpha, cockpit));
                Assert.That(plot.ViewedDock, Is.EqualTo(alpha.Entity), label);
                Assert.That(viewed, Is.EqualTo(new[] { bravo.Entity, charlie.Entity, alpha.Entity, bravo.Entity, alpha.Entity }), label);

                MoveMouse(ui, plot, bravoSpot);
                Assert.That(plot.HighlightedDock, Is.EqualTo(bravo.Entity), $"{label}: hovering a port highlights it, as hovering its list entry does.");
                MoveMouse(ui, plot, empty);
                Assert.That(plot.HighlightedDock, Is.Null, label);

                plot.OnViewDock -= viewed.Add;
                ui.SetHovered(null);
                lease.Restore();
                screen.Orphan();
            }
            settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            entities.DeleteEntity(owner);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VelocityDialHelpReachesTheMouse()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var dial = new WFVelocityVectorInstrument(() => null);
            ui.WindowRoot.AddChild(dial);
            var size = new Vector2(160, 120);
            dial.Measure(size);
            dial.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            var centre = new ScreenCoordinates(
                dial.GlobalPixelPosition + new Vector2(dial.PixelWidth, dial.PixelHeight) / 2f, dial.Window!.Id);
            Assert.That(dial.ToolTip, Is.Not.Empty);
            Assert.That(ui.MouseGetControl(centre), Is.SameAs(dial), "The dial must take the mouse for its tooltip to show.");
            dial.Orphan();
        });
        await pair.CleanReturnAsync();
    }

    private static DockingPortState Port(NetEntity entity, NetEntity grid) => new()
    {
        Entity = entity,
        Coordinates = new NetCoordinates(grid, Vector2.Zero),
        Name = "external airlock with a deliberately long operational name",
    };

    private static DockingPortState Port(NetEntity entity, NetEntity grid, string name, Vector2 position) => new()
    {
        Entity = entity,
        Coordinates = new NetCoordinates(grid, position),
        Name = name,
    };

    /// <summary>The name on the port list's selected entry.</summary>
    private static string? ListedPort(DockingScreen screen) => Tree(screen.FindControl<BoxContainer>("DockPorts")).OfType<Button>()
        .Where(button => button.Pressed)
        .SelectMany(button => Tree(button).OfType<Label>())
        .Select(label => label.Text)
        .FirstOrDefault(text => !string.IsNullOrEmpty(text));

    /// <summary>Where a port should be drawn: its offset from the viewed port at the plot's scale, north up, less any pan.</summary>
    private static Vector2 Plotted(ShuttleDockControl plot, DockingPortState viewed, DockingPortState port, bool panned)
    {
        // The plot fits its shorter side with a 4 px margin; the clicks below are placed from this, not from the control's own maths.
        var radius = Math.Max(1, Math.Min(plot.PixelWidth, plot.PixelHeight) / 2 - (int) (4 * plot.UIScale));
        var scale = radius / plot.WorldRange;
        var offset = port.Coordinates.Position - viewed.Coordinates.Position - (panned ? plot.Offset : Vector2.Zero);
        return new Vector2(plot.PixelWidth, plot.PixelHeight) / 2f + new Vector2(offset.X, -offset.Y) * scale;
    }

    private static ScreenCoordinates Pointer(Control surface, Vector2 pixel) => new(surface.GlobalPixelPosition + pixel, surface.Window!.Id);

    /// <summary>Presses and releases through the UI manager's own dispatch: hit test, bubbling and release routing.</summary>
    private static void Click(IUserInterfaceManager ui, Control surface, Vector2 down, Vector2? up = null, BoundKeyFunction? function = null,
        bool press = true, bool release = true)
    {
        ui.ControlFocused = null;
        ui.ReleaseKeyboardFocus();
        Assert.That(ui.MouseGetControl(Pointer(surface, down)), Is.SameAs(surface), "The press must land on the plot itself.");
        foreach (var (state, pixel) in new[] { (BoundKeyState.Down, down), (BoundKeyState.Up, up ?? down) })
        {
            if (state == BoundKeyState.Down ? !press : !release)
                continue;

            var args = new BoundKeyEventArgs(function ?? EngineKeyFunctions.UIClick, state, Pointer(surface, pixel), true);
            ui.GetType().GetMethod(state == BoundKeyState.Down ? "KeyBindDown" : "KeyBindUp")!.Invoke(ui, new object[] { args });
        }
    }

    private static void MoveMouse(IUserInterfaceManager ui, Control surface, Vector2 pixel) =>
        ui.GetType().GetMethod("MouseMove")!.Invoke(ui, new object[] { new MouseMoveEventArgs(Vector2.Zero, Pointer(surface, pixel)) });

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Tree(child))
            yield return nested;
    }
}
