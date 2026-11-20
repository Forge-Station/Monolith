#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>Checks the windowed helm's FTL page says when the ship has no working drive and keeps the FTL button off.</summary>
[TestFixture]
public sealed class WFFtlDriveWindowTest
{
    [Test]
    public async Task FtlPageWarnsAndLocksTheButtonWithoutAWorkingDrive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            var map = helm.FindControl<MapScreen>("MapContainer");
            var button = map.FindControl<Button>("MapFTLButton");
            var autopilot = map.FindControl<Button>("MapAutopilotButton");
            var scan = map.FindControl<Button>("MapRebuildButton");
            var radar = map.FindControl<ShuttleMapControl>("MapRadar");
            var contents = helm.FindControl<Control>("Contents");
            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { ShuttleConsoleWindow.WfDefaultSize, new Vector2(1896, 1056) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Map);
                    helm.SetSize = size;
                    var context = $"{skin}, {size}";

                    void Update(FTLState ftl, WFFtlDriveStatus drive)
                    {
                        map.UpdateState(WFFtlDriveTestPages.State(ftl, drive));
                        WFFtlDriveTestPages.Layout(helm, size);
                    }

                    // A powered drive: no notice, and the button works.
                    Update(FTLState.Available, WFFtlDriveStatus.Ready);
                    WFFtlDriveTestPages.AssertDrive(map, WFFtlDriveStatus.Ready, context);
                    Assert.That(button.Disabled, Is.False, context);
                    Assert.That(map.FindControl<Label>("MapFTLState").Text, Is.EqualTo(Loc.GetString("shuttle-console-ftl-state-Available")), context);
                    WFButtonTestInput.Toggle(button, true);
                    Assert.That(radar.FtlMode, Is.True, context);

                    // Losing the drive with the button on releases it, and the page says why.
                    var scanWithDrive = scan.Disabled;
                    Update(FTLState.Available, WFFtlDriveStatus.Missing);
                    WFFtlDriveTestPages.AssertDrive(map, WFFtlDriveStatus.Missing, $"{context}, drive lost");
                    Assert.That(map.FindControl<Label>("MapFTLState").Text, Is.EqualTo(Loc.GetString("wf-ftl-state-offline")), context);
                    Assert.That(scan.Disabled, Is.EqualTo(scanWithDrive), $"{context}: Scan feeds autopilot too, so a missing drive must leave it alone.");
                    WFFtlDriveTestPages.AssertFits(helm, contents, map.WfDriveNotice, context);

                    // The disabled button can't be pressed, so no preview or jump request can follow.
                    WFButtonTestInput.Click(button);
                    Assert.That(button.Pressed, Is.False, $"{context}: the FTL button must not toggle on without a drive.");
                    Assert.That(radar.FtlMode, Is.False, context);

                    // A drive that is there but unpowered has its own wording.
                    Update(FTLState.Available, WFFtlDriveStatus.Unpowered);
                    WFFtlDriveTestPages.AssertDrive(map, WFFtlDriveStatus.Unpowered, context);
                    Assert.That(map.WfDriveNotice.Text, Is.Not.EqualTo(Loc.GetString("shuttle-console-no-powered-ftl-drive")), context);
                    WFFtlDriveTestPages.AssertFits(helm, contents, map.WfDriveNotice, context);
                    WFButtonTestInput.Click(button);
                    Assert.That(button.Pressed, Is.False, context);

                    // Autopilot doesn't need a drive, so a state update must leave its targeting on.
                    WFButtonTestInput.Toggle(autopilot, true);
                    Assert.That(radar.FtlMode, Is.True, context);
                    Update(FTLState.Available, WFFtlDriveStatus.Missing);
                    Assert.That(autopilot.Pressed && radar.FtlMode, Is.True, $"{context}: the warning must not cancel autopilot targeting.");
                    WFButtonTestInput.Toggle(autopilot, false);

                    // With the drive back the page returns to normal.
                    Update(FTLState.Available, WFFtlDriveStatus.Ready);
                    WFFtlDriveTestPages.AssertDrive(map, WFFtlDriveStatus.Ready, $"{context}, drive back");
                    Assert.That(button.Disabled, Is.False, context);
                    Assert.That(map.FindControl<Label>("MapFTLState").Text, Is.EqualTo(Loc.GetString("shuttle-console-ftl-state-Available")), context);

                    // Every other phase keeps its behaviour with a working drive.
                    foreach (var phase in new[] { FTLState.Starting, FTLState.Travelling, FTLState.Arriving, FTLState.Cooldown })
                    {
                        Update(phase, WFFtlDriveStatus.Ready);
                        WFFtlDriveTestPages.AssertDrive(map, WFFtlDriveStatus.Ready, $"{context}, {phase}");
                        Assert.That(button.Disabled, Is.True, $"{context}, {phase}: the button is off while the jump is under way.");
                        Assert.That(map.FindControl<Label>("MapFTLState").Text, Is.EqualTo(Loc.GetString($"shuttle-console-ftl-state-{phase}")), context);
                    }
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
}

/// <summary>Shared page checks and states for the windowed and cockpit FTL drive tests.</summary>
internal static class WFFtlDriveTestPages
{
    /// <summary>A map state for an idle or jumping ship with the given drive.</summary>
    public static ShuttleMapInterfaceState State(FTLState ftl, WFFtlDriveStatus drive) =>
        new(ftl, default, new List<ShuttleBeaconObject>(), new List<ShuttleExclusionObject>(), drive);

    /// <summary>Lays a control tree out at a size, as the engine does each frame.</summary>
    public static void Layout(Control root, Vector2 size)
    {
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    public static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var item in Descendants(child))
            yield return item;
    }

    /// <summary>What the page must show and allow for a drive status: the notice, its wording, and the FTL button.</summary>
    public static void AssertDrive(MapScreen screen, WFFtlDriveStatus drive, string context)
    {
        var notice = screen.WfDriveNotice;
        var button = screen.FindControl<Button>("MapFTLButton");
        var reason = drive switch
        {
            WFFtlDriveStatus.Missing => Loc.GetString("shuttle-console-no-powered-ftl-drive"),
            WFFtlDriveStatus.Unpowered => Loc.GetString("shuttle-console-ftl-drive-unpowered"),
            _ => null,
        };
        Assert.That(screen.WfFtlDrive, Is.EqualTo(drive), context);
        Assert.That(notice.Visible, Is.EqualTo(reason != null), $"{context}: the notice shows exactly while the ship can't jump.");
        if (reason == null)
        {
            Assert.That(button.ToolTip, Is.Null, context);
            return;
        }

        Assert.That(notice.VisibleInTree, Is.True, $"{context}: the notice must be on the page itself.");
        Assert.That(notice.Text, Is.EqualTo(reason), context);
        Assert.That(notice.Tint, Is.EqualTo(WFInstrumentTheme.Skin.Danger), $"{context}: the notice is drawn in the skin's danger colour.");
        Assert.That(button.Disabled, Is.True, $"{context}: the FTL button must be off.");
        Assert.That(button.Pressed, Is.False, $"{context}: the FTL button must be released.");
        Assert.That(button.ToolTip, Is.EqualTo(reason), $"{context}: the button gives the reason.");
        Assert.That(screen.FindControl<ShuttleMapControl>("MapRadar").FtlMode, Is.False, $"{context}: no FTL preview without a drive.");
    }

    /// <summary>The notice is laid out inside the page, and no part of the page needs a scroll bar to be reached.</summary>
    public static void AssertFits(Control window, Control page, Control notice, string context)
    {
        Assert.That(notice.VisibleInTree, Is.True, context);
        Assert.That(notice.Width, Is.GreaterThan(0), context);
        Assert.That(notice.Height, Is.GreaterThan(0), context);
        AssertWithin(notice, page, context);
        var scrolling = Descendants(window).OfType<ScrollContainer>()
            .Where(scroll => scroll.VisibleInTree && NeedsScroll(scroll))
            .Select(scroll => scroll.Name ?? scroll.GetType().Name);
        Assert.That(scrolling, Is.Empty, $"{context}: the notice must not push a page into scrolling.");
        var bottom = page.GlobalPosition.Y + page.Height + 1;
        var right = page.GlobalPosition.X + page.Width + 1;
        foreach (var control in Descendants(page).Skip(1).Where(control => control.VisibleInTree && control.Width > 0 && control.Height > 0))
        {
            var chain = new StringBuilder();
            for (var parent = control; parent != null && parent != page; parent = parent.Parent)
                chain.Append(parent.Name ?? parent.GetType().Name).Append(" < ");
            Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(bottom), $"{context}: {chain}is cut off at the bottom.");
            Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(right), $"{context}: {chain}is cut off at the right.");
        }
    }

    /// <summary>A control lies inside its container's bounds.</summary>
    public static void AssertWithin(Control control, Control parent, string context)
    {
        Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(parent.GlobalPosition.X - 1), context);
        Assert.That(control.GlobalPosition.Y, Is.GreaterThanOrEqualTo(parent.GlobalPosition.Y - 1), context);
        Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(parent.GlobalPosition.X + parent.Width + 1), context);
        Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(parent.GlobalPosition.Y + parent.Height + 1), context);
    }

    /// <summary>A scroll container needs its bar when it shows a bar or its content is taller than it is.</summary>
    public static bool NeedsScroll(ScrollContainer scroll)
    {
        var content = scroll.Children.First(child => child is not ScrollBar);
        return scroll.Children.OfType<VScrollBar>().Any(bar => bar.Visible) || content.DesiredSize.Y > scroll.Height + 1;
    }
}
