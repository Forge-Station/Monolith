using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.ShipShields;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipShields;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks the helm's Shields page puts the shunt bearing dial at the centre of the page, in both skins.</summary>
[TestFixture]
public sealed class WFShipShieldCentreTest
{
    private static readonly Vector2 DefaultSize = new(1240, 900);

    /// <summary>The dial sits at the page's horizontal centre, with the sector and power banks and the coverage readout mirrored beside it.</summary>
    [Test]
    public async Task BearingDialIsTheCentrePointOfTheShieldPage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            helm.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(true, true, 0.6f, 0.8f, 0.2f, MathF.PI / 2f));
            var screen = Field<WFShipShieldShuntScreen>(helm, "_shieldScreen");
            var dial = Field<Control>(screen, "_dial");
            var contents = helm.FindControl<Control>("Contents");
            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { DefaultSize, helm.MinSize })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Shields);
                    helm.SetSize = size;
                    Layout(helm, size);
                    var context = $"{skin}, {size}";
                    // Below the default window the page falls back to scroll bars, so only its horizontal layout is exact there.
                    var scrolls = size == helm.MinSize;
                    var body = Tree(screen).Single(control => control.GetType().Name == "ShieldColumns");
                    var panels = Tree(screen).Single(control => control.GetType().Name == "ShieldSettings")
                        .Children.Where(child => child.Visible).ToArray();
                    Assert.That(panels, Has.Length.EqualTo(3), context);

                    var pageMiddle = screen.GlobalPosition.X + screen.Width / 2f;
                    Assert.That(dial.GlobalPosition.X + dial.Width / 2f, Is.EqualTo(pageMiddle).Within(2f),
                        $"{context}: the bearing dial is centred on the page.");
                    var bodyMiddle = body.GlobalPosition.Y + body.Height / 2f;
                    if (!scrolls)
                        Assert.That(dial.GlobalPosition.Y + dial.Height / 2f, Is.EqualTo(bodyMiddle).Within(30f),
                            $"{context}: the dial is level with the middle of the page, allowing for its caption.");

                    Assert.That(panels[1].GlobalPosition.X, Is.EqualTo(panels[0].GlobalPosition.X).Within(1f), context);
                    Assert.That(panels[0].GlobalPosition.X + panels[0].Width, Is.LessThanOrEqualTo(dial.GlobalPosition.X), context);
                    Assert.That(panels[1].GlobalPosition.X + panels[1].Width, Is.LessThanOrEqualTo(dial.GlobalPosition.X), context);
                    Assert.That(panels[2].GlobalPosition.X, Is.GreaterThanOrEqualTo(dial.GlobalPosition.X + dial.Width), context);
                    var leftGap = panels[0].GlobalPosition.X - screen.GlobalPosition.X;
                    var rightGap = screen.GlobalPosition.X + screen.Width - (panels[2].GlobalPosition.X + panels[2].Width);
                    Assert.That(rightGap, Is.EqualTo(leftGap).Within(2f), $"{context}: the side columns mirror each other.");
                    Assert.That(panels[2].Width, Is.EqualTo(panels[0].Width).Within(1f), context);
                    if (!scrolls)
                    {
                        var leftMiddle = (panels[0].GlobalPosition.Y + panels[1].GlobalPosition.Y + panels[1].Height) / 2f;
                        var rightMiddle = panels[2].GlobalPosition.Y + panels[2].Height / 2f;
                        Assert.That(leftMiddle, Is.EqualTo(rightMiddle).Within(2f), $"{context}: both columns are centred on the dial.");
                        Assert.That(leftMiddle, Is.EqualTo(bodyMiddle).Within(2f), context);
                    }

                    Assert.That(dial.Width, Is.GreaterThan(panels[0].Width), $"{context}: the dial is the widest element of the page body.");
                    Assert.That(dial.Height, Is.GreaterThan(180f), context);

                    foreach (var name in new[] { "_enabled", "_stats", "_reset", "_health", "_direction", "_arc", "_concentration" })
                        AssertWithin(Field<Control>(screen, name), screen);
                    AssertWithin(dial, screen);
                    if (scrolls)
                        continue;
                    foreach (var panel in panels)
                        AssertWithin(panel, screen);
                    Assert.That(Tree(screen).OfType<ScrollContainer>().Where(scroll => scroll.VisibleInTree && NeedsScroll(scroll)), Is.Empty,
                        $"{context}: every shield control fits without a scroll bar.");
                    AssertNothingClipped(contents, context);
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

    /// <summary>Without a generator the dial stays centred and the settings stay hidden.</summary>
    [Test]
    public async Task UnavailableShieldsKeepTheDialCentred()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var screen = new WFShipShieldShuntScreen();
            screen.WfRefitInstruments();
            var dial = Field<Control>(screen, "_dial");
            var size = new Vector2(1100, 600);
            foreach (var available in new[] { false, true, false })
            {
                screen.UpdateState(available
                    ? new WFShipShieldShuntState(true, true, 0.6f, 0.8f, 0.2f, MathF.PI / 2f)
                    : new WFShipShieldShuntState(false, false, 0f, 0f, 0f, MathF.PI / 2f), 0f);
                Layout(screen, size);
                Assert.That(Tree(screen).Single(control => control.GetType().Name == "ShieldSettings").Visible, Is.EqualTo(available));
                Assert.That(dial.GlobalPosition.X + dial.Width / 2f, Is.EqualTo(screen.GlobalPosition.X + size.X / 2f).Within(2f));
                Assert.That(dial.Height, Is.GreaterThan(150f));
            }
        });
        await pair.CleanReturnAsync();
    }

    private static T Field<T>(object target, string name) => (T) target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Layout(Control root, Vector2 size)
    {
        foreach (var control in Tree(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
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
        foreach (var control in Tree(page).Skip(1).Where(control => control.VisibleInTree && control.Width > 0 && control.Height > 0))
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
        Assert.That(control.VisibleInTree, Is.True, $"{control.Name ?? control.GetType().Name} must remain accessible without scrolling.");
        Assert.That(control.Width, Is.GreaterThan(0), control.Name);
        Assert.That(control.Height, Is.GreaterThan(0), control.Name);
        Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(parent.GlobalPosition.X - 1), control.Name);
        Assert.That(control.GlobalPosition.Y, Is.GreaterThanOrEqualTo(parent.GlobalPosition.Y - 1), control.Name);
        Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(parent.GlobalPosition.X + parent.Width + 1), control.Name);
        Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(parent.GlobalPosition.Y + parent.Height + 1), control.Name);
    }

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Tree(child))
            yield return descendant;
    }
}
