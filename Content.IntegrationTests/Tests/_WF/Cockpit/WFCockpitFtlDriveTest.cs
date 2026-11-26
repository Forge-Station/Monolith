#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.Shuttles;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.Systems;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks the cockpit's strategic map page and FTL lamp say when the ship has no working drive.</summary>
[TestFixture]
public sealed class WFCockpitFtlDriveTest
{
    [Test]
    public async Task MapPageWarnsAboveItsScrollingDetailsAndLocksTheButton()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(640, 700), new Vector2(560, 260) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    var context = $"{skin}, {size}";
                    var lease = new WFCockpitLease();
                    using var screen = new MapScreen();
                    screen.WfCockpitMap(lease);
                    screen.Visible = true;
                    ui.WindowRoot.AddChild(screen);
                    var button = screen.FindControl<Button>("MapFTLButton");
                    var radar = screen.FindControl<ShuttleMapControl>("MapRadar");
                    var notice = screen.WfDriveNotice;

                    void Update(WFFtlDriveStatus drive)
                    {
                        screen.UpdateState(WFFtlDriveTestPages.State(FTLState.Available, drive));
                        WFFtlDriveTestPages.Layout(screen, size);
                    }

                    void AssertNotice(WFFtlDriveStatus drive)
                    {
                        WFFtlDriveTestPages.AssertDrive(screen, drive, context);
                        if (drive == WFFtlDriveStatus.Ready)
                            return;

                        WFFtlDriveTestPages.AssertWithin(notice, screen, context);
                        Assert.That(notice.Height, Is.GreaterThan(0), context);
                        Assert.That(WFCockpitLease.Descendants(screen).OfType<ScrollContainer>()
                                .Any(scroll => WFCockpitLease.Descendants(scroll).Contains(notice)), Is.False,
                            $"{context}: the notice must stay outside the scrolling details.");
                        Assert.That(radar.PixelHeight, Is.GreaterThan(0), $"{context}: the plot must keep room.");
                        Assert.That(notice.GlobalPosition.Y + notice.Height, Is.LessThanOrEqualTo(radar.GlobalPosition.Y + 1),
                            $"{context}: the notice sits above the plot.");
                    }

                    Update(WFFtlDriveStatus.Ready);
                    AssertNotice(WFFtlDriveStatus.Ready);
                    Assert.That(button.Disabled, Is.False, context);
                    WFButtonTestInput.Toggle(button, true);
                    Assert.That(radar.FtlMode, Is.True, context);

                    Update(WFFtlDriveStatus.Missing);
                    AssertNotice(WFFtlDriveStatus.Missing);
                    WFButtonTestInput.Click(button);
                    Assert.That(button.Pressed, Is.False, $"{context}: the FTL button must not toggle on without a drive.");
                    Assert.That(radar.FtlMode, Is.False, context);

                    Update(WFFtlDriveStatus.Unpowered);
                    AssertNotice(WFFtlDriveStatus.Unpowered);
                    Assert.That(notice.Text, Is.Not.EqualTo(Loc.GetString("shuttle-console-no-powered-ftl-drive")), context);
                    WFButtonTestInput.Click(button);
                    Assert.That(button.Pressed, Is.False, context);

                    Update(WFFtlDriveStatus.Ready);
                    AssertNotice(WFFtlDriveStatus.Ready);
                    Assert.That(button.Disabled, Is.False, context);

                    ui.SetHovered(null);
                    lease.Restore();
                    screen.Orphan();
                }
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeavingTheCockpitReturnsTheNoticeToTheHelmsDrivePanel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var helm = new ShuttleConsoleWindow();
            var map = helm.FindControl<MapScreen>("MapContainer");
            var notice = map.WfDriveNotice;
            var panel = notice.Parent!;
            var index = notice.GetPositionInParent();
            var readout = panel.GetChild(index + 1);
            Assert.That(index, Is.EqualTo(0), "The helm puts the notice first in its drive panel.");
            helm.OpenCentered();
            helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Map);
            try
            {
                // The drive changes while the pilot is seated, so the notice must follow the status at exit, not at entry.
                foreach (var (atEntry, atExit) in new[]
                {
                    (WFFtlDriveStatus.Missing, WFFtlDriveStatus.Ready),
                    (WFFtlDriveStatus.Ready, WFFtlDriveStatus.Unpowered),
                })
                {
                    var context = $"{atEntry} then {atExit}";
                    map.UpdateState(WFFtlDriveTestPages.State(FTLState.Available, atEntry));
                    var lease = new WFCockpitLease();
                    map.WfCockpitMap(lease);
                    ui.WindowRoot.AddChild(map);
                    Assert.That(notice.Parent, Is.Not.SameAs(panel), $"{context}: the cockpit page takes the notice.");
                    map.UpdateState(WFFtlDriveTestPages.State(FTLState.Available, atExit));
                    WFFtlDriveTestPages.AssertDrive(map, atExit, $"{context}, seated");
                    lease.Restore();

                    Assert.That(notice.Parent, Is.SameAs(panel), $"{context}: the notice returns to the helm's drive panel.");
                    Assert.That(notice.GetPositionInParent(), Is.EqualTo(index), $"{context}: at its old place.");
                    Assert.That(panel.GetChild(index + 1), Is.SameAs(readout), $"{context}: still before the state readout.");
                    WFFtlDriveTestPages.AssertDrive(map, atExit, $"{context}, back at the helm");
                }
            }
            finally
            {
                helm.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FtlLampNeverReadsReadyWithoutAWorkingDrive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            var ship = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            using var helm = new ShuttleConsoleWindow();
            var map = helm.FindControl<MapScreen>("MapContainer");
            map.SetShuttle(ship);
            var bank = helm.WfCockpitStatus();
            try
            {
                var lamp = bank.Children.OfType<WFCockpitStatusLamp>().Single(control => control.Name == "wf-cockpit-status-ftl");

                void AssertLamp(FTLState phase, WFFtlDriveStatus drive, WFCockpitLampState state, string detail)
                {
                    map.UpdateState(WFFtlDriveTestPages.State(phase, drive));
                    Assert.That(lamp.Reading, Is.EqualTo(new WFCockpitStatusReading(state, detail)), $"{phase}, {drive}");
                }

                AssertLamp(FTLState.Available, WFFtlDriveStatus.Ready, WFCockpitLampState.Good, "wf-cockpit-status-ftl-ready");
                AssertLamp(FTLState.Available, WFFtlDriveStatus.Missing, WFCockpitLampState.Fault, "shuttle-console-no-powered-ftl-drive");
                AssertLamp(FTLState.Available, WFFtlDriveStatus.Unpowered, WFCockpitLampState.Fault, "shuttle-console-ftl-drive-unpowered");
                AssertLamp(FTLState.Available, WFFtlDriveStatus.Ready, WFCockpitLampState.Good, "wf-cockpit-status-ftl-ready");
            }
            finally
            {
                bank.Dispose();
                entities.DeleteEntity(ship);
            }
        });
        await pair.CleanReturnAsync();
    }
}
