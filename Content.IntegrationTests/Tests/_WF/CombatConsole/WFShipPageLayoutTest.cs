using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.ShipShields;
using Content.Client._WF.Shuttles.UI;
using Content.Client._WF.Stylesheets;
using Content.Client.Pinpointer.UI;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipShields;
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks the helm's Ship page: the overlay panel, the telemetry bank, the hull plot and the cockpit borrowing.</summary>
[TestFixture]
public sealed class WFShipPageLayoutTest
{
    private static readonly Vector2 DefaultSize = new(1240, 900);

    private static readonly string[] ToggleNames =
        { "DamageToggle", "FireToggle", "PressureToggle", "PowerToggle", "DepartmentToggle", "FitButton" };

    private static readonly string[] Skins = { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id };

    /// <summary>The six overlay controls share one panel under the announcements, laid out two across and three down.</summary>
    [Test]
    public async Task OverlayControlsSitInTheirOwnPanelBelowTheAnnouncements()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            var ship = helm.FindControl<ShipScreen>("ShipContainer");
            helm.OpenCentered();
            try
            {
                foreach (var skin in Skins)
                foreach (var size in new[] { DefaultSize, helm.MinSize })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Ship);
                    helm.SetSize = size;
                    Layout(helm, size);
                    var context = $"{skin}, {size}";
                    var overlays = Named(ship, "WfHullControls");
                    var announcements = Named(ship, "WfHullAnnouncements");
                    var plot = Named(ship, "WfHullPlot");
                    var status = Named(ship, "WfHullStatus");
                    var buttons = Buttons(ship);

                    foreach (var button in buttons)
                    {
                        Assert.That(IsUnder(button, overlays), Is.True, $"{context}: {button.Name} belongs in the overlay panel.");
                        Assert.That(IsUnder(button, plot), Is.False, $"{context}: {button.Name} must leave the plot column.");
                        AssertWithin(button, overlays);
                        AssertCaptionFits(Caption(button), ui);
                    }
                    Assert.That(Descendants(overlays).OfType<BaseButton>().Count(), Is.EqualTo(6),
                        $"{context}: the panel holds the six overlay controls and nothing else.");

                    var rows = buttons.GroupBy(button => MathF.Round(button.GlobalPosition.Y)).OrderBy(row => row.Key).ToArray();
                    Assert.That(rows, Has.Length.EqualTo(3), $"{context}: three rows of toggles.");
                    Assert.That(rows.All(row => row.Count() == 2), Is.True, $"{context}: two toggles on each row.");
                    for (var i = 0; i < buttons.Length; i += 2)
                    {
                        Assert.That(buttons[i].GlobalPosition.Y, Is.EqualTo(buttons[i + 1].GlobalPosition.Y).Within(1f), context);
                        Assert.That(buttons[i].GlobalPosition.X, Is.LessThan(buttons[i + 1].GlobalPosition.X), context);
                        Assert.That(buttons[i].GlobalPosition.X, Is.EqualTo(buttons[0].GlobalPosition.X).Within(1f), context);
                        Assert.That(buttons[i + 1].GlobalPosition.X, Is.EqualTo(buttons[1].GlobalPosition.X).Within(1f), context);
                        Assert.That(buttons[i].Width, Is.EqualTo(buttons[i + 1].Width).Within(1f),
                            $"{context}: both columns of the grid are the same width.");
                    }
                    Assert.That(buttons[0].Width, Is.GreaterThan(overlays.Width * 0.35f), $"{context}: the grid spans the panel.");

                    Assert.That(overlays.Parent, Is.SameAs(announcements.Parent), context);
                    Assert.That(overlays.GlobalPosition.X, Is.EqualTo(announcements.GlobalPosition.X).Within(1f), context);
                    Assert.That(overlays.Width, Is.EqualTo(announcements.Width).Within(1f), context);
                    var gap = overlays.GlobalPosition.Y - (announcements.GlobalPosition.Y + announcements.Height);
                    Assert.That(gap, Is.InRange(-1f, 12f), $"{context}: the overlay panel sits directly below the announcements.");
                    Assert.That(status.GlobalPosition.X + status.Width, Is.LessThanOrEqualTo(overlays.GlobalPosition.X + 1f), context);

                    var caption = Loc.GetString("wf-console-hull-overlays");
                    Assert.That(Descendants(overlays).OfType<Label>().Any(label => label.Text == caption), Is.True,
                        $"{context}: the panel carries its caption.");
                    Assert.That(caption, Does.Match(@"^\d\d / "), "The caption is numbered like its neighbours.");
                    Assert.That(Loc.GetString("wf-console-hull-announcements"), Does.Match(@"^\d\d / "));

                    foreach (var panel in new[] { plot, status, announcements, overlays })
                        AssertWithin(panel, ship);
                    Assert.That(Descendants(ship).OfType<ScrollContainer>().Where(scroll => scroll.VisibleInTree && NeedsScroll(scroll)), Is.Empty,
                        $"{context}: the page must fit without a scroll bar.");
                    AssertNothingClipped(ship, context);
                }
            }
            finally
            {
                helm.Close();
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The six gauges form three rows of two, and the freed room goes to a visibly larger hull plot.</summary>
    [Test]
    public async Task TelemetryIsThreeRowsOfTwoAndTheHullPlotIsLarger()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            var ship = helm.FindControl<ShipScreen>("ShipContainer");
            helm.OpenCentered();
            try
            {
                foreach (var skin in Skins)
                foreach (var size in new[] { DefaultSize, helm.MinSize })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Ship);
                    helm.SetSize = size;
                    Layout(helm, size);
                    var context = $"{skin}, {size}";
                    var plot = Named(ship, "WfHullPlot");
                    var status = Named(ship, "WfHullStatus");
                    var side = Named(ship, "WfHullSide");
                    var mapView = ship.FindControl<ShipViewControl>("ShipView");
                    var gauges = Descendants(status).OfType<WFGlassGauge>().ToArray();

                    Assert.That(gauges, Has.Length.EqualTo(6), context);
                    Assert.That(((GridContainer) gauges[0].Parent!).Columns, Is.EqualTo(2), context);
                    var rows = gauges.GroupBy(gauge => MathF.Round(gauge.GlobalPosition.Y)).OrderBy(row => row.Key).ToArray();
                    Assert.That(rows, Has.Length.EqualTo(3), $"{context}: three rows of gauges.");
                    Assert.That(rows.All(row => row.Count() == 2), Is.True, $"{context}: two gauges on each row.");
                    Assert.That(gauges.Select(gauge => MathF.Round(gauge.GlobalPosition.X)).Distinct().Count(), Is.EqualTo(2), context);
                    for (var i = 0; i < gauges.Length; i += 2)
                    {
                        Assert.That(gauges[i].GlobalPosition.Y, Is.EqualTo(gauges[i + 1].GlobalPosition.Y).Within(1f),
                            $"{context}: gauges fill the grid in reading order.");
                        Assert.That(gauges[i].GlobalPosition.X, Is.LessThan(gauges[i + 1].GlobalPosition.X), context);
                    }
                    foreach (var gauge in gauges)
                    {
                        AssertWithin(gauge, status);
                        Assert.That(gauge.Width, Is.GreaterThanOrEqualTo(85), $"{context}: gauges keep a readable scale.");
                        Assert.That(gauge.Height, Is.GreaterThanOrEqualTo(100), context);
                    }

                    // Before the re-layout the plot took 30 percent of the page and the department toggle and a controls row ate its
                    // height: its view was about 293 x 573 at 1240 x 900 and 209 x 267 at 960 x 600. It must now clear those with room.
                    var isDefault = size == DefaultSize;
                    if (isDefault)
                    {
                        Assert.That(mapView.Width, Is.GreaterThanOrEqualTo(400f), context);
                        Assert.That(mapView.Height, Is.GreaterThanOrEqualTo(600f), context);
                    }
                    Assert.That(mapView.Width, Is.GreaterThanOrEqualTo(ship.Width * 0.31f), $"{context}: the plot is the widest column.");
                    Assert.That(mapView.Width * mapView.Height, Is.GreaterThanOrEqualTo(ship.Width * ship.Height * (isDefault ? 0.25f : 0.2f)),
                        $"{context}: the hull view must take a clearly larger share of the page than before.");
                    Assert.That(plot.Width, Is.GreaterThan(status.Width), context);
                    Assert.That(plot.Width, Is.GreaterThan(side.Width), context);
                    Assert.That(plot.Width, Is.LessThan(ship.Width * 0.5f), context);
                    Assert.That(status.GlobalPosition.X, Is.GreaterThanOrEqualTo(plot.GlobalPosition.X + plot.Width), context);
                    Assert.That(side.GlobalPosition.X, Is.GreaterThanOrEqualTo(status.GlobalPosition.X + status.Width), context);
                }
            }
            finally
            {
                helm.Close();
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Each moved control still drives what it drove before.</summary>
    [Test]
    public async Task OverlayTogglesStillRaiseTheSameOverlayChange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var helm = new ShuttleConsoleWindow();
            var ship = helm.FindControl<ShipScreen>("ShipContainer");
            var mapView = ship.FindControl<ShipViewControl>("ShipView");
            helm.OpenCentered();
            try
            {
                helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Ship);
                helm.SetSize = DefaultSize;
                Layout(helm, DefaultSize);
                var changes = 0;
                ship.OverlaysChanged += () => changes++;
                foreach (var (name, flag) in new[]
                {
                    ("DamageToggle", ShipOverlays.Damage), ("FireToggle", ShipOverlays.Fire),
                    ("PressureToggle", ShipOverlays.Pressure), ("PowerToggle", ShipOverlays.Power),
                })
                {
                    var toggle = Descendants(ship).OfType<CheckBox>().Single(control => control.Name == name);
                    var before = ship.Overlays.HasFlag(flag);
                    var count = changes;
                    WFButtonTestInput.Click(toggle);
                    Assert.That(changes, Is.EqualTo(count + 1), $"{name} raises one overlay change.");
                    Assert.That(ship.Overlays.HasFlag(flag), Is.EqualTo(!before), $"{name} flips {flag}.");
                    Assert.That(toggle.Pressed, Is.EqualTo(!before), $"{name} shows the overlay state.");
                    WFButtonTestInput.Click(toggle);
                    Assert.That(changes, Is.EqualTo(count + 2), name);
                    Assert.That(ship.Overlays.HasFlag(flag), Is.EqualTo(before), name);
                }

                var departments = Descendants(ship).OfType<CheckBox>().Single(control => control.Name == "DepartmentToggle");
                Assert.That(typeof(NavMapControl).GetField("_beacons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mapView),
                    Is.SameAs(departments), "The department toggle is still the map's own control.");
                var shown = departments.Pressed;
                var settled = changes;
                WFButtonTestInput.Click(departments);
                Assert.That(departments.Pressed, Is.EqualTo(!shown));
                Assert.That(changes, Is.EqualTo(settled), "Department labels never changed the server's telemetry request.");
                WFButtonTestInput.Click(departments);
                Assert.That(departments.Pressed, Is.EqualTo(shown));

                var fit = Descendants(ship).OfType<Button>().Single(control => control.Name == "FitButton");
                Assert.That(fit.ToggleMode, Is.False, "Fit to hull stays a one-shot action.");
                var needsFit = typeof(ShipViewControl).GetField("_needsFit", BindingFlags.Instance | BindingFlags.NonPublic)!;
                needsFit.SetValue(mapView, false);
                WFButtonTestInput.Click(fit);
                Assert.That((bool) needsFit.GetValue(mapView)!, Is.True, "Fit to hull re-frames the hull.");
                Assert.That(changes, Is.EqualTo(settled));
            }
            finally
            {
                helm.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The cockpit's SHP page borrows the moved controls and gives every one back to its slot in the panel.</summary>
    [Test]
    public async Task CockpitShipPageBorrowsAndRestoresTheOverlayControls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var helm = new ShuttleConsoleWindow();
            var ship = helm.FindControl<ShipScreen>("ShipContainer");
            var overlays = Named(ship, "WfHullControls");
            var alarms = Named(ship, "WfHullAnnouncements");
            var alarmPanel = Named(ship, "AlarmPanel");
            var buttons = Buttons(ship);
            var gauges = Descendants(ship).OfType<WFGlassGauge>().ToArray();
            var topLevel = ship.Children.ToArray();
            Assert.That(gauges, Has.Length.EqualTo(6));
            var tracked = new List<Control>(buttons);
            tracked.AddRange(gauges);
            tracked.Add(alarmPanel);
            var snapshot = tracked.ToDictionary(control => control, Snapshot);

            var lease = new WFCockpitLease();
            var announcementsPage = ship.WfCockpitAnnouncements(lease);
            var shipPage = ship.WfCockpitShip(lease);
            foreach (var button in buttons)
            {
                Assert.That(IsUnder(button, shipPage), Is.True, $"{button.Name} is borrowed by the SHP page.");
                Assert.That(IsUnder(button, overlays), Is.False, $"{button.Name} left the overlay panel.");
            }
            foreach (var gauge in gauges)
                Assert.That(IsUnder(gauge, shipPage), Is.True, "The telemetry gauges are borrowed too.");
            Assert.That(IsUnder(alarmPanel, announcementsPage), Is.True, "The alarm panel moves to its own page.");
            foreach (var toggle in buttons.OfType<CheckBox>())
                Assert.That(toggle.Label.Align, Is.EqualTo(Label.AlignMode.Left), "The MFD keeps its toggle captions left-aligned.");

            lease.Restore();
            foreach (var (control, before) in snapshot)
                Assert.That(Snapshot(control), Is.EqualTo(before), $"{control.Name ?? control.GetType().Name} must come back exactly as it left.");
            Assert.That(ship.Children.ToArray(), Is.EqualTo(topLevel), "The Ship page gets its own layout back.");
            Assert.That(buttons.All(button => IsUnder(button, overlays)), Is.True);
            Assert.That(IsUnder(alarmPanel, alarms), Is.True);
            Assert.That(overlays.Children.Count(), Is.EqualTo(1));
            Assert.That(Descendants(overlays).OfType<BaseButton>().Count(), Is.EqualTo(6));
            foreach (var toggle in buttons.OfType<CheckBox>())
                Assert.That(toggle.Label.Align, Is.EqualTo(Label.AlignMode.Center), "Back on the helm the captions are centred again.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>The cockpit's SHD pages borrow the shield controls and give every one back.</summary>
    [Test]
    public async Task CockpitShieldPagesBorrowAndRestoreTheirControls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var screen = new WFShipShieldShuntScreen();
            screen.WfRefitInstruments();
            screen.UpdateState(new WFShipShieldShuntState(true, true, 0.6f, 0.8f, 0.2f, MathF.PI / 2f), 0f);
            // The deployment row and integrity bar sit in the page's header, the draft line and reset key in its footer.
            var framing = new[] { "_enabled", "_health", "_stats", "_status", "_recovery", "_draft", "_reset" }
                .Select(name => Field<Control>(screen, name)).ToList();
            var controls = new[] { "_dial", "_amount", "_arc", "_concentration", "_direction", "_unprotected" }
                .Select(name => Field<Control>(screen, name)).ToList();
            var settings = Field<Control>(screen, "_settings");
            var body = Descendants(screen).Single(control => control.GetType().Name == "ShieldColumns");
            controls.AddRange(Descendants(settings).OfType<WFGlassGauge>());
            Assert.That(controls.OfType<WFGlassGauge>().Count(), Is.EqualTo(5), "The power strip and four coverage dials are found under the settings.");
            Assert.That(controls.All(control => IsUnder(control, body)), Is.True, "Every borrowed control starts in the page body.");
            Assert.That(framing.All(control => IsUnder(control, screen) && !IsUnder(control, body)), Is.True,
                "The deployment, integrity, status and reset controls start in the page's header and footer.");
            var bodyControls = controls.ToList();
            controls.AddRange(framing);
            var snapshot = controls.ToDictionary(control => control, Snapshot);
            var children = screen.Children.ToArray();

            var lease = new WFCockpitLease();
            var hud = screen.WfCockpitShield(lease);
            using var details = screen.WfCockpitShieldDetails(lease);
            foreach (var control in controls)
            {
                Assert.That(IsUnder(control, hud) || IsUnder(control, details), Is.True,
                    $"{control.Name ?? control.GetType().Name} is borrowed by the SHD pages.");
                Assert.That(IsUnder(control, body), Is.False, $"{control.Name ?? control.GetType().Name} left the page body.");
            }

            lease.Restore();
            foreach (var (control, before) in snapshot)
                Assert.That(Snapshot(control), Is.EqualTo(before), $"{control.Name ?? control.GetType().Name} must come back exactly as it left.");
            Assert.That(screen.Children.ToArray(), Is.EqualTo(children), "The shield screen gets its own layout back.");
            Assert.That(bodyControls.All(control => IsUnder(control, body)), Is.True, "Every borrowed control is back in the page body.");
            Assert.That(Descendants(settings).OfType<WFGlassGauge>().Count(), Is.EqualTo(5));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Where a control sits and how it is sized, so a borrow can be compared with its return.</summary>
    private static string Snapshot(Control control) =>
        $"{RuntimeHelpers.GetHashCode(control.Parent)}@{control.GetPositionInParent()} name={control.Name} visible={control.Visible} " +
        $"expand={control.HorizontalExpand}/{control.VerticalExpand} align={control.HorizontalAlignment}/{control.VerticalAlignment} " +
        $"margin={control.Margin} min={control.MinSize} set={control.SetSize} max={control.MaxSize}" +
        (control is CheckBox check ? $" text={check.Label.Align}" : string.Empty);

    private static BaseButton[] Buttons(Control ship) => ToggleNames
        .Select(name => Descendants(ship).OfType<BaseButton>().Single(control => control.Name == name)).ToArray();

    private static Label Caption(BaseButton button) => button is CheckBox check ? check.Label : ((Button) button).Label;

    private static Control Named(Control root, string name) => Descendants(root).Single(control => control.Name == name);

    private static T Field<T>(object target, string name) => (T) target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Layout(Control root, Vector2 size)
    {
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static bool IsUnder(Control control, Control root)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current == root)
                return true;
        }
        return false;
    }

    /// <summary>A scroll container needs its bar when it shows one or its content is taller than it is.</summary>
    private static bool NeedsScroll(ScrollContainer scroll)
    {
        var content = scroll.Children.First(child => child is not ScrollBar);
        return scroll.Children.OfType<VScrollBar>().Any(bar => bar.Visible) || content.DesiredSize.Y > scroll.Height + 1;
    }

    /// <summary>Nothing on a page hangs past its container, where its clip would hide it without any scroll bar to show it.</summary>
    private static void AssertNothingClipped(Control page, string context)
    {
        var bottom = page.GlobalPosition.Y + page.Height + 1;
        var right = page.GlobalPosition.X + page.Width + 1;
        foreach (var control in Descendants(page).Skip(1).Where(control => control.VisibleInTree && control.Width > 0 && control.Height > 0))
        {
            var chain = new StringBuilder();
            for (var parent = control; parent != null && parent != page; parent = parent.Parent)
                chain.Append(parent.Name ?? parent.GetType().Name).Append(" < ");
            Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(bottom),
                $"{context}: {chain}is cut off at the bottom of the page.");
            Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(right),
                $"{context}: {chain}is cut off at the right of the page.");
        }
    }

    private static void AssertWithin(Control control, Control parent)
    {
        Assert.That(control.VisibleInTree, Is.True, $"{control.Name} must remain accessible without scrolling.");
        Assert.That(control.Width, Is.GreaterThan(0), control.Name);
        Assert.That(control.Height, Is.GreaterThan(0), control.Name);
        Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(parent.GlobalPosition.X - 1), control.Name);
        Assert.That(control.GlobalPosition.Y, Is.GreaterThanOrEqualTo(parent.GlobalPosition.Y - 1), control.Name);
        Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(parent.GlobalPosition.X + parent.Width + 1), control.Name);
        Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(parent.GlobalPosition.Y + parent.Height + 1), control.Name);
    }

    private static void AssertCaptionFits(Label label, IUserInterfaceManager ui)
    {
        var caption = label.Text!;
        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : ui.ThemeDefaults.LabelFont);
        var width = 0f;
        foreach (var rune in caption.EnumerateRunes())
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
        Assert.That(label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"Console captions must fit: {caption}");
        Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(label.UIScale)));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var item in Descendants(child))
            yield return item;
    }
}
