#nullable enable annotations

using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.CCVar;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks radar overlay controls survive console styling and cockpit reparenting with their live bindings.</summary>
[TestFixture]
public sealed class WFRadarOverlayTest
{
    [Test]
    public async Task RadarControlsRemainVisibleAndBoundAcrossConsoleAndCockpit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            var maps = pair.Client.ResolveDependency<IMapManager>();
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            var map = maps.CreateMap();
            var grid = maps.CreateGrid(map);
            var console = em.SpawnEntity(null, new EntityCoordinates(grid.Owner, new Vector2(2, 3)));
            var defaults = em.AddComponent<RadarConsoleComponent>(console);
#pragma warning disable RA0002 // Configure the fixture's grid-following radar default.
            defaults.RelativePanning = true;
#pragma warning restore RA0002
            var state = new NavInterfaceState(512,
                em.GetNetCoordinates(new EntityCoordinates(grid.Owner, Vector2.Zero)), Angle.Zero, new(), default);
            using var helm = new ShuttleConsoleWindow();
            using var guns = new FireControlWindow();
            try
            {
                helm.OpenCentered();
                guns.OpenCentered();
                helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Nav);
                var navigation = helm.FindControl<NavScreen>("NavContainer");
                var helmRadar = navigation.FindControl<ShuttleNavControl>("NavRadar");
                navigation.SetConsole(console);
                guns.Radar.SetConsole(console);
                foreach (var radar in new ShuttleNavControl[] { helmRadar, guns.Radar })
                    radar.UpdateState(state);
                var helmButtons = OverlayButtons(helmRadar);
                var gunButtons = OverlayButtons(guns.Radar);
                var home = helmRadar.Parent;
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                {
                    var before = State(helmRadar);
                    var gunBefore = State(guns.Radar);
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    Assert.That(State(helmRadar), Is.EqualTo(before), "Changing the skin must preserve radar modes and panning.");
                    Assert.That(State(guns.Radar), Is.EqualTo(gunBefore));
                    foreach (var (window, radar, buttons) in new[]
                    {
                        ((Control) helm, helmRadar, helmButtons),
                        ((Control) guns, (ShuttleNavControl) guns.Radar, gunButtons),
                    })
                    {
                        radar.UpdateState(state);
                        radar.UpdateState(state);
                        Layout(window, new Vector2(1180, 780));
                        AssertOverlay(radar, buttons, skin);
                        AssertCommands(radar);
                    }
                    for (var entry = 0; entry < 2; entry++)
                    {
                        var lease = new WFCockpitLease();
                        var cockpit = navigation.WfCockpitRadar(lease);
                        ui.WindowRoot.AddChild(cockpit);
                        try
                        {
                            Layout(cockpit, new Vector2(380, 400));
                            AssertOverlay(helmRadar, helmButtons, $"{skin}, cockpit entry {entry}");
                            Assert.That(Descendants(helm).Any(control => helmButtons.Contains(control)), Is.False,
                                "Borrowed radar buttons must leave the old console with their radar.");
                            AssertCommands(helmRadar);
                            helmRadar.UpdateState(state);
                            Layout(cockpit, new Vector2(700, 700));
                            AssertOverlay(helmRadar, helmButtons, $"{skin}, expanded cockpit");
                        }
                        finally
                        {
                            lease.Restore();
                            cockpit.Parent?.RemoveChild(cockpit);
                            cockpit.Dispose();
                        }
                        Layout(helm, new Vector2(1180, 780));
                        Assert.That(helmRadar.Parent, Is.SameAs(home));
                        AssertOverlay(helmRadar, helmButtons, $"{skin}, cockpit exit {entry}");
                        AssertCommands(helmRadar);
                    }
                }
            }
            finally
            {
                helm.Close();
                guns.Close();
                maps.DeleteMap(map);
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    private static BaseButton[] OverlayButtons(ShuttleNavControl radar) => new[]
    {
        "WfRadarAzimuth", "WfRadarRotation", "WfRadarAnchor", "WfRadarReset", "WfRadarTerrain",
    }.Select(name => Descendants(radar.Parent!).OfType<BaseButton>().Single(button => button.Name == name)).ToArray();

    /// <summary>Checks the clickable controls remain above and within the actual radar face.</summary>
    private static void AssertOverlay(ShuttleNavControl radar, BaseButton[] buttons, string context)
    {
        Assert.That(OverlayButtons(radar), Is.EqualTo(buttons), $"{context}: reparenting must reuse the original controls.");
        var host = radar.Parent!;
        var bank = Descendants(host).Single(control => control.Name == "WfRadarModes");
        Assert.That(bank.Parent, Is.SameAs(host), context);
        Assert.That(buttons[^1].Parent, Is.SameAs(host), context);
        var glass = host.Children.OfType<WFCrtGlass>().Single();
        Assert.That(bank.GetPositionInParent(), Is.GreaterThan(glass.GetPositionInParent()), context);
        Assert.That(buttons[^1].GetPositionInParent(), Is.GreaterThan(glass.GetPositionInParent()), context);
        foreach (var button in buttons)
        {
            Assert.That(button.VisibleInTree, Is.True, $"{context}: {button.Name} must be visible.");
            Assert.That(button.MouseFilter, Is.Not.EqualTo(Control.MouseFilterMode.Ignore), context);
            Assert.That(button.ToolTip, Is.Not.Null.And.Not.Empty, context);
            Assert.That(button.Width, Is.GreaterThan(0), context);
            Assert.That(button.Height, Is.GreaterThan(0), context);
            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(radar.GlobalPosition.X - 1), context);
            Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(radar.GlobalPosition.Y - 1), context);
            Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(radar.GlobalPosition.X + radar.Width + 1), context);
            Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(radar.GlobalPosition.Y + radar.Height + 1), context);
        }
    }

    /// <summary>Uses the live button callbacks to verify modes, terrain visibility and recentering.</summary>
    private static void AssertCommands(ShuttleNavControl radar)
    {
        var buttons = OverlayButtons(radar);
        var azimuth = Convert.ToInt32(Field(radar, "_azimuthMode"));
        for (var step = 1; step <= 3; step++)
        {
            Press(buttons[0]);
            Assert.That(Convert.ToInt32(Field(radar, "_azimuthMode")), Is.EqualTo((azimuth + step) % 3));
        }
        var rotation = (bool) Field(radar, "_angleFollow");
        Press(buttons[1]);
        Assert.That(Field(radar, "_angleFollow"), Is.EqualTo(!rotation));
        Press(buttons[1]);
        Assert.That(Field(radar, "_angleFollow"), Is.EqualTo(rotation));
        var anchored = (bool) Field(radar, "_relativePanning");
        Press(buttons[2]);
        Assert.That(Field(radar, "_relativePanning"), Is.EqualTo(!anchored));
        Press(buttons[2]);
        Assert.That(Field(radar, "_relativePanning"), Is.EqualTo(anchored));
        var terrain = radar.ShowPlanetTerrain;
        Toggle(buttons[4], !terrain);
        Assert.That(radar.ShowPlanetTerrain, Is.EqualTo(!terrain));
        Toggle(buttons[4], terrain);
        Assert.That(radar.ShowPlanetTerrain, Is.EqualTo(terrain));
        radar.Offset = new Vector2(24, -12);
        Press(buttons[1]);
        Press(buttons[2]);
        Press(buttons[3]);
        Assert.That(radar.Offset, Is.EqualTo(Vector2.Zero), "Reset must recenter the same live radar.");
        Assert.That(Field(radar, "_angleFollow"), Is.True, "Reset must restore the console's rotation setting.");
        Assert.That(Field(radar, "_relativePanning"), Is.True, "Reset must reattach the view to the console's grid.");
    }

    private static object Field(ShuttleNavControl radar, string name) =>
        typeof(ShuttleNavControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(radar)!;

    private static (object, object, object, bool, Vector2) State(ShuttleNavControl radar) =>
        (Field(radar, "_azimuthMode"), Field(radar, "_angleFollow"), Field(radar, "_relativePanning"), radar.ShowPlanetTerrain, radar.Offset);

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);

    private static void Toggle(BaseButton button, bool pressed) => WFButtonTestInput.Toggle(button, pressed);

    private static void Layout(Control root, Vector2 size)
    {
        root.SetSize = size;
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
