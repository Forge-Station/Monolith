#nullable enable annotations

using Content.Shared._Mono.FireControl;
using Content.Shared.Shuttles.BUIStates;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._WF.ChatTabs;
using Content.Client._WF.Cockpit;
using Content.Client._WF.ShipAccess;
using Content.IntegrationTests.Tests._WF.ShipAccess;
using Content.Client._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Client._WF.ShipShields;
using Content.Shared.CCVar;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Info;
using Content.Client._WF.Shuttles.UI;
using Content.Client.UserInterface.Systems.Inventory;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.Alerts.Controls;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Ghost.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Content.Shared._WF.ShipShields;
using Content.Shared.Buckle;
using Content.Shared.Inventory;
using Content.Shared.Buckle.Components;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises seated entry, live HUD reparenting and automatic restoration after unbuckling.</summary>
public sealed class WFCockpitTest : InteractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: InteractionTestMob
  id: WFCockpitTestMob
  components:
  - type: ContentEye
  - type: Sprite
    sprite: Mobs/Species/Human/parts.rsi
    layers:
    - state: torso_m
    - map: [""jumpsuit""]
  - type: Buckle
  - type: Alerts
  - type: Inventory
  - type: InventorySlots
  - type: ContainerContainer
- type: entity
  id: WFCockpitTestWall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
            bounds: ""-0.5,-0.5,0.5,0.5""
        mask:
        - WallLayer
        layer:
        - WallLayer
";
    protected override string PlayerPrototype => "WFCockpitTestMob";

    [Test]
    public async Task SeatedCockpitRestoresHudAndBindings()
    {
        EntityUid console = default;
        EntityUid chair = default;
        EntityUid uniform = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            uniform = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", MapData.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(SPlayer, uniform, "jumpsuit"), Is.True);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            var cockpit = SEntMan.System<SharedWFCockpitSystem>();
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.False, "Standing pilots cannot enter cockpit mode.");
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.True);
            Assert.That(cockpit.CanEnter(SPlayer, chair), Is.False, "Another console cannot borrow this piloting session.");
            SEntMan.RemoveComponent<WFCockpitSeatComponent>(chair);
            Assert.That(cockpit.CanEnter(SPlayer, console), Is.False, "A bare strap without seat classification must remain insufficient.");
            SEntMan.AddComponent<WFCockpitSeatComponent>(chair);
        });
        await RunTicks(10);
        ShuttleConsoleWindow window = default!;
        InGameScreen screen = default!;
        Control[] normalHud = default!;
        ChatBox chat = default!;
        MainViewport viewport = default!;
        Control viewportParent = default!;
        Control chatParent = default!;
        InventoryGui inventory = default!;
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            screen = (InGameScreen) ui.ActiveScreen!;
            inventory = screen.GetWidget<InventoryGui>()!;
            Assert.That(inventory.InventoryButton.Visible, Is.True, "The fixture must exercise a populated character inventory.");
            if (!inventory.InventoryHotbar.Visible)
                ui.GetUIController<InventoryUIController>().ToggleInventoryBar();
            Assert.That(inventory.InventoryHotbar.TryGetButton("jumpsuit", out var clothing), Is.True);
            Assert.That(clothing!.Entity, Is.EqualTo(ToClient(SEntMan.GetNetEntity(uniform))));
            Layout(screen, new Vector2(1130, 636));
            Assert.That(inventory.InventoryHotbar.Height, Is.GreaterThan(150), "The open clothing grid must contribute to the resize case.");
            normalHud = screen.Children.ToArray();
            chat = screen.ChatBox;
            viewport = screen.GetWidget<MainViewport>()!;
            viewportParent = viewport.Parent!;
            chatParent = chat.Parent!;
            var alerts = screen.GetWidget<AlertsUI>()!;
            var alertsParent = alerts.Parent;
            var votes = Named<BoxContainer>(screen, "VoteMenu");
            var votesParent = votes.Parent;
            var speech = (LayoutContainer) typeof(ChatUIController).GetField("_speechBubbleRoot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(ui.GetUIController<ChatUIController>())!;
            var speechParent = speech.Parent;
            chat.ChatInput.Input.Text = "unfinished cockpit transmission";
            window = new ShuttleConsoleWindow();
            var gunMessages = new List<BoundUserInterfaceMessage>();
            window.WfCockpitGunneryCommand += gunMessages.Add;
            window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
            window.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(true, true, 0.76f, MathF.PI / 2f, 0.65f, MathF.PI / 2f));
            var cameraRequests = new List<(ShuttleCameraView View, float Zoom, bool LowLight)>();
            window.ShipCameraRequested += (view, zoom, lowLight) => cameraRequests.Add((view, zoom, lowLight));
            window.OpenCentered();
            var controller = ui.GetUIController<WFCockpitUIController>();
            var plots = Descendants(window).OfType<MapGridControl>().ToArray();
            var originalPlotControls = plots.Select(plot => plot.WfCockpitControls).ToArray();
            var networkPorts = Named<GridContainer>(window, "NetworkPortsBox");
            var originalNetworkLayout = (networkPorts.LimitedDimension, networkPorts.Rows, networkPorts.Columns);
            Assert.That(AimOverlayPresent(), Is.False, "Without a cockpit the ship cam has no aim lines.");
            Assert.That(controller.Enter(window), Is.True);
            Assert.That(AimOverlayPresent(), Is.True, "An open cockpit draws its aim lines on the ship cam.");
            Assert.That(cameraRequests.Single(), Is.EqualTo((ShuttleCameraView.External, 1.5f, false)),
                "Cockpit entry must request the external camera through the normal helm binding.");
            Assert.That(window.Visible, Is.False);
            Assert.That(screen.GetWidget<MainViewport>(), Is.SameAs(viewport));
            Assert.That(screen.GetWidget<ChatBox>() ?? screen.GetWidget<ResizableChatBox>(), Is.SameAs(chat));
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
            Assert.That(normalHud.Where(control => control != chat && control != viewport && control is not AlertsUI)
                .All(control => !control.VisibleInTree), Is.True, "Character HUD controls must stay hidden.");
            var hud = screen.Children.OfType<WFCockpitView>().Single();
            Assert.That(alerts.VisibleInTree, Is.True, "Crew status alerts must remain available in cockpit.");
            Assert.That(votes.VisibleInTree, Is.True, "Live votes must not be hidden with character hotkeys.");
            Assert.That(speech.VisibleInTree, Is.True, "Speech bubbles must continue updating in cockpit.");
            Assert.That(speech.Parent!.Name, Is.EqualTo("CockpitSpeechBubbles"));
            var recentWindows = ui.GetUIController<CloseRecentWindowUIController>();
            using (var popup = new DefaultWindow())
            {
                popup.OpenCentered();
                recentWindows.SetMostRecentlyInteractedWindow(window);
                Assert.That(recentWindows.HasClosableWindow(), Is.True);
                recentWindows.CloseMostRecentWindow();
                Assert.That(popup.IsOpen, Is.False, "Escape must close a real popup behind the hidden helm entry.");
                Assert.That(window.IsOpen, Is.True);
                Assert.That(controller.Active, Is.True, "Escape must not close the hidden helm that supplies cockpit controls.");
                Assert.That(recentWindows.HasClosableWindow(), Is.False, "The hidden helm alone is not an Escape-closeable window.");
            }
            Layout(hud, new Vector2(1130, 636));
            var crewAlerts = Named<ScrollContainer>(hud, "CockpitCrewAlerts");
            var alertControls = alerts.AlertContainer.Children.OfType<AlertControl>().ToArray();
            Assert.That(alertControls, Is.Not.Empty, "The buckled pilot must have a live alert for the bank to show.");
            Assert.That(crewAlerts.Width, Is.GreaterThan(0), "The docked alert bank must not collapse to nothing.");
            Assert.That(crewAlerts.Height, Is.GreaterThan(0), "The docked alert bank must not collapse to nothing.");
            Assert.That(crewAlerts.GlobalPosition.X, Is.GreaterThanOrEqualTo(viewport.GlobalPosition.X - 1));
            Assert.That(crewAlerts.GlobalPosition.Y, Is.GreaterThanOrEqualTo(viewport.GlobalPosition.Y - 1));
            Assert.That(Right(crewAlerts), Is.LessThanOrEqualTo(Right(viewport) + 1));
            Assert.That(Bottom(crewAlerts), Is.LessThanOrEqualTo(Bottom(viewport) + 1));
            Assert.That(alertControls[0].Height, Is.GreaterThan(0), "A docked alert must be drawn and clickable.");
            Assert.That(alertControls[0].GlobalPosition.Y, Is.GreaterThanOrEqualTo(crewAlerts.GlobalPosition.Y - 1));
            Assert.That(Bottom(alertControls[0]), Is.LessThanOrEqualTo(Bottom(crewAlerts) + 1),
                "The first alert must lie inside the bank's clip rectangle.");
            Assert.That(inventory.InventoryHotbar.Visible, Is.True, "Cockpit entry must retain the open clothing panel's state.");
            Assert.That(inventory.InventoryHotbar.VisibleInTree, Is.False);
            var actions = screen.GetWidget<ActionsBar>()!;
            Assert.That(actions.ActionsContainer.Rows, Is.GreaterThanOrEqualTo(1),
                "A world viewport shorter than the retained inventory must retain a usable action-grid row limit.");
            Assert.That(float.IsFinite(viewport.Height) && viewport.Height > 0, Is.True,
                "Resizing with open clothing must complete with a valid world viewport.");
            Assert.That(viewport.Height, Is.LessThan(inventory.Height + 40),
                "The fixture must actually exercise the small-viewport/open-inventory resize case.");
            var navigation = Named<NavScreen>(window, "NavContainer");
            var velocity = Named<WFVelocityVectorInstrument>(hud, "CockpitVelocity");
            Assert.That(velocity.Reading, Is.Null, "An unbound helm must not invent a stopped velocity sample.");
            var coordinatesBox = Named<WFCockpitCoordinates>(hud, "CockpitCoordinates");
            Assert.That(coordinatesBox.Reading, Is.Null, "An unbound helm must not invent a position.");
            var ship = ToClient(SEntMan.GetNetEntity(MapData.Grid.Owner));
            var physics = CEntMan.System<SharedPhysicsSystem>();
            var transform = CEntMan.System<SharedTransformSystem>();
            var body = CEntMan.GetComponent<PhysicsComponent>(ship);
            var originalBodyType = body.BodyType;
            var originalVelocity = body.LinearVelocity;
            var originalCenter = body.LocalCenter;
            var originalRotation = transform.GetWorldRotation(ship);
            try
            {
                navigation.SetShuttle(ship);
                physics.SetBodyType(ship, BodyType.Dynamic);
                var rotation = Angle.FromDegrees(61);
                transform.SetWorldRotation(ship, rotation);
                physics.SetLinearVelocity(ship, rotation.RotateVec(new Vector2(3, -4)));
                var reading = velocity.Reading;
                Assert.That(reading, Is.Not.Null);
                Assert.That(reading!.Value.Speed, Is.EqualTo(5).Within(0.0001f));
                Assert.That(reading.Value.ScreenDirection!.Value.X, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(reading.Value.ScreenDirection.Value.Y, Is.EqualTo(0.8f).Within(0.0001f),
                    "The live NavScreen source must report aft/starboard drift in the ship bow frame.");
                physics.SetLinearVelocity(ship, Vector2.Zero);
                Assert.That(velocity.Reading!.Value.ScreenDirection, Is.Null, "The same live instrument must clear its pointer when stopped.");
                physics.SetLocalCenter(ship, body, new Vector2(3, 2));
                navigation.WfCockpitRefresh();
                var center = Vector2.Transform(body.LocalCenter, transform.GetWorldMatrix(ship));
                Assert.That(navigation.FindControl<Label>("GridPosition").Text,
                    Does.Contain($"{center.X:0.0}").And.Contain($"{center.Y:0.0}"),
                    "The position readout must report the centre of mass, as the windowed helm does.");
                Assert.That(coordinatesBox.Reading, Is.Not.Null);
                Assert.That(coordinatesBox.Reading!.Value.X, Is.EqualTo(center.X).Within(0.001f),
                    "The coordinates box must show the same centre-of-mass position as the helm.");
                Assert.That(coordinatesBox.Reading.Value.Y, Is.EqualTo(center.Y).Within(0.001f));
            }
            finally
            {
                physics.SetLocalCenter(ship, body, originalCenter);
                physics.SetLinearVelocity(ship, originalVelocity);
                physics.SetBodyType(ship, originalBodyType);
                transform.SetWorldRotation(ship, originalRotation);
            }
            Assert.That(networkPorts.Columns, Is.EqualTo(2), "Auxiliary device buttons must fit the compact MFD.");
            Assert.That(plots, Has.Length.GreaterThanOrEqualTo(5));
            Assert.That(plots.All(plot => plot.WfCockpitControls), Is.True,
                "Navigation, hull, strategic, docking and access plots must share cockpit interactions.");
            var pages = new[] { "wf-cockpit-nav", "wf-cockpit-ship", "wf-cockpit-map", "wf-cockpit-dock", "wf-cockpit-access", "wf-cockpit-shield-page", "wf-cockpit-systems", "wf-cockpit-alarms" };
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                AssertFuelBinding(Named<ShipScreen>(hud, "ShipContainer"), hud);
                foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900) })
                {
                    var swap = Named<Button>(hud, "CockpitMfdSwap");
                    var maximise = Named<Button>(hud, "CockpitMfdMaximise");
                    SetArrangement(hud, Arrangement.Normal);
                    hud.SelectPage("wf-cockpit-nav");
                    Layout(hud, size);
                    var normal = Snapshot(hud, viewport);
                    foreach (var arrangement in Enum.GetValues<Arrangement>())
                    {
                        SetArrangement(hud, arrangement);
                        foreach (var page in pages)
                        {
                            hud.SelectPage(page);
                            Layout(hud, size);
                            var context = $"{theme}, {size}, {arrangement}, {page}";
                            AssertCockpitLayout(hud, viewport, chat, ui, size, theme, page);
                            AssertPanelsFitScreen(hud, viewport, size, context);
                            AssertLabelFits(swap, ui, context);
                            AssertLabelFits(maximise, ui, context);
                            AssertMfdHeaderFits(hud, ui, context);
                            if (page == "wf-cockpit-access")
                                AssertAccessPage(hud, ui, context);
                            if (page == "wf-cockpit-ship")
                                AssertShipPage(hud, ui, context);
                        }
                        hud.SelectPage("wf-cockpit-nav");
                        Layout(hud, size);
                        AssertArrangement(hud, viewport, alerts, votes, size, arrangement, normal, $"{theme}, {size}, {arrangement}");
                    }
                    SetArrangement(hud, Arrangement.Swapped);
                    Layout(hud, size);
                    var swappedBoxes = Snapshot(hud, viewport);
                    Toggle(maximise, true);
                    Layout(hud, size);
                    Toggle(maximise, false);
                    Layout(hud, size);
                    Assert.That(hud.PanelsSwapped, Is.True, $"{theme}, {size}: restoring must keep the earlier swap.");
                    AssertSameBoxes(Snapshot(hud, viewport), swappedBoxes, $"{theme}, {size}: restoring must return to the swapped arrangement");
                    SetArrangement(hud, Arrangement.Normal);
                    Layout(hud, size);
                    AssertSameBoxes(Snapshot(hud, viewport), normal, $"{theme}, {size}: swapping back must return to the first arrangement");
                }
            }
            hud.SelectPage("wf-cockpit-nav");
            foreach (var narrow in new[] { new Vector2(1090, 636), new Vector2(1097, 617) })
            foreach (var arrangement in Enum.GetValues<Arrangement>())
            {
                SetArrangement(hud, arrangement);
                Layout(hud, narrow);
                AssertPanelsFitScreen(hud, viewport, narrow, $"{narrow}, {arrangement}");
            }
            SetArrangement(hud, Arrangement.Normal);
            Layout(hud, new Vector2(1130, 636));
            var initialSession = gunMessages.OfType<WFCockpitGunnerySessionMessage>().Single();
            Assert.That(initialSession.Active, Is.True);
            Assert.That(initialSession.Controlling, Is.False, "Entering FLIGHT must discover guns without claiming them.");
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.False,
                "Cockpits without an authorized nearby gun console keep the original flight layout.");
            var weapon = new FireControllableEntry(new NetEntity(910), default, "Battery", 10, true);
            var gunState = new FireControlConsoleBoundInterfaceState(true, new[] { weapon },
                new NavInterfaceState(250, null, null, new(), default));
            var linked = new NetEntity(911);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, gunState));
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.True);
            hud.SelectPage("wf-cockpit-nav");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900), new Vector2(1920, 1080) })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                Layout(hud, size);
                AssertCockpitLayout(hud, viewport, chat, ui, size, theme, "wf-cockpit-nav");
                AssertPanelsFitScreen(hud, viewport, size, $"{theme}, {size}, FLIGHT");
                var fuel = Named<WFCockpitFuelBank>(hud, "CockpitFuelBank");
                var fixedPosition = fuel.GlobalPosition;
                var scroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
                AssertInstrumentScrollInput(hud, ui, size, theme);
                scroll.SetScrollValue(new Vector2(0, 10000));
                Layout(hud, size);
                AssertFuelVisible(hud);
                Assert.That(fuel.GlobalPosition, Is.EqualTo(fixedPosition),
                    "Scrolling the large dials must never move the fuel gauge or its warning lamp.");
                Assert.That(Bottom(Named<WFGlassGauge>(hud, "CockpitHull")), Is.LessThanOrEqualTo(fuel.GlobalPosition.Y + 1),
                    "The hull strip remains the last scrolling instrument immediately above fixed fuel.");
                scroll.SetScrollValue(Vector2.Zero);
                if (size.Y >= 1080)
                    AssertFitsWithoutScrolling(hud, size, theme);
            }
            hud.SelectGunnery(true);
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Last().Controlling, Is.True,
                "GUNS must explicitly request firing control.");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900) })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                Layout(hud, size);
                var guns = Named<Control>(hud, "CockpitGunnery");
                Assert.That(guns.VisibleInTree, Is.True);
                Assert.That(guns.Height, Is.GreaterThan(350), "The weapon bank retains usable height above the permanent TCAS bank.");
                var tcas = Named<WFCockpitTcasPanel>(hud, "CockpitTcas");
                Assert.That(tcas.VisibleInTree, Is.True, "Collision warning lamps must stay available while operating guns.");
                Assert.That(Bottom(guns), Is.LessThanOrEqualTo(tcas.GlobalPosition.Y));
                Assert.That(Bottom(tcas), Is.LessThanOrEqualTo(size.Y - 8));
                Assert.That(guns.Width, Is.GreaterThanOrEqualTo(320));
                Assert.That(guns.GlobalPosition.X + guns.Width, Is.LessThan(viewport.GlobalPosition.X));
                Assert.That(Named<Control>(hud, "CockpitFlight").Visible, Is.False);
                Assert.That(Named<Control>(hud, "CockpitMfd").VisibleInTree, Is.True);
                Assert.That(chat.VisibleInTree, Is.True);
                AssertPanelsFitScreen(hud, viewport, size, $"{theme}, {size}, GUNS");
                WFWeaponListChecks.NoPaging(guns, $"{theme}, {size}, GUNS");
                var (gunList, gunGrid) = WFWeaponListChecks.OneList(guns, $"{theme}, {size}, GUNS");
                var gunEntry = gunGrid.Children.OfType<Button>().Single();
                Assert.That(gunGrid.Entries(), Is.EqualTo(new[] { gunEntry }), $"{theme}, {size}: the one weapon is listed.");
                Assert.That(gunEntry.GlobalPosition.Y + gunEntry.Height, Is.LessThanOrEqualTo(gunList.GlobalPosition.Y + gunList.Height + 1), $"{theme}, {size}");
                Assert.That(gunList.Height, Is.EqualTo(gunGrid.ContentHeight).Within(1), $"{theme}, {size}: the list takes only the room it needs.");
                Assert.That(Bottom(gunList), Is.LessThanOrEqualTo(Bottom(guns)), $"{theme}, {size}: the list stays inside the GUNS deck.");
                foreach (var arrangement in new[] { Arrangement.Swapped, Arrangement.Maximised })
                {
                    SetArrangement(hud, arrangement);
                    Layout(hud, size);
                    AssertPanelsFitScreen(hud, viewport, size, $"{theme}, {size}, GUNS, {arrangement}");
                    Assert.That(Bottom(guns), Is.LessThanOrEqualTo(tcas.GlobalPosition.Y),
                        "The weapon bank stays in the left column whatever the MFD is doing.");
                    Assert.That(Named<Control>(hud, "CockpitMfd").VisibleInTree, Is.True);
                }
                SetArrangement(hud, Arrangement.Normal);
                Layout(hud, size);
            }
            Press(Named<Button>(hud, "CockpitGunneryRefresh"));
            var refresh = gunMessages.OfType<WFCockpitGunneryCommandMessage>().Single();
            Assert.That(refresh.Console, Is.EqualTo(linked));
            Assert.That(refresh.Command, Is.InstanceOf<FireControlConsoleRefreshServerMessage>());
            var battery = Descendants(hud).OfType<WFCockpitGunneryPanel>().Single();
            var weaponButton = Descendants(battery).OfType<WFWeaponGrid>().Single().Children.OfType<Button>().Single();
            Toggle(weaponButton, true);
            Assert.That(battery.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }));
            var sessionsOnGuns = gunMessages.OfType<WFCockpitGunnerySessionMessage>().Count();
            hud.SelectGunnery(false);
            Assert.That(battery.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }),
                "Returning to FLIGHT must retain the selection for the next GUNS session.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Count(), Is.EqualTo(sessionsOnGuns),
                "FLIGHT with weapons selected keeps gun control without a redundant session message.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Last().Controlling, Is.True,
                "FLIGHT with weapons selected must keep firing control.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(new NetEntity(912), gunState));
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>().Single().SelectedWeapons, Is.Empty,
                "A newly linked console cannot inherit the previous console's firing selection.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Last().Controlling, Is.False,
                "Changing the link on FLIGHT empties the selection and must release firing control at once.");
            hud.SelectGunnery(true);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(null, null));
            Assert.That(Named<Control>(hud, "CockpitGunneryModes").Visible, Is.False);
            Assert.That(Named<Control>(hud, "CockpitFlight").Visible, Is.True);
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>(), Is.Empty);
            hud.SelectPage("wf-cockpit-nav");
            var shield = Descendants(hud).OfType<WFShipShieldShuntScreen>().Single();
            var arc = Named<FloatSpinBox>(hud, "CockpitShieldArc");
            var previousArc = arc.Value;
            var allocations = new List<(float Direction, float Amount, float Arc)>();
            window.ShieldShuntRequested += (direction, amount, width) => allocations.Add((direction, amount, width));
            Press(arc.Children.OfType<Button>().Single(button => button.Text == "+"));
            typeof(WFShipShieldShuntScreen).GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shield, new object[] { new FrameEventArgs(0.11f) });
            Assert.That(arc.Value, Is.GreaterThan(previousArc), "Arc width must be adjustable without opening the shield MFD.");
            Assert.That(allocations, Has.Count.EqualTo(1));
            Assert.That(allocations[0].Arc, Is.EqualTo(arc.Value * MathF.PI / 180f).Within(0.0001f),
                "The permanent arc control must retain its live helm command binding.");
            Assert.That(allocations[0].Amount, Is.EqualTo(0.65f).Within(0.0001f),
                "Changing the arc must preserve the selected concentration.");
            foreach (var plot in plots)
            {
                var wheel = new GUIMouseWheelEventArgs(new Vector2(0, 1), plot, Vector2.Zero, default, Vector2.Zero, Vector2.Zero);
                typeof(MapGridControl).GetMethod("MouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(plot, new object[] { wheel });
                Assert.That(wheel.Handled, Is.True, "Zooming an MFD plot must consume the wheel event.");
            }
            hud.SelectPage("wf-cockpit-shield-page");
            window.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(false, false, 0, 0, 0, 0));
            Assert.That(Descendants(hud).OfType<WFShipShieldShuntScreen>().Single().VisibleInTree, Is.True,
                "Losing the generator must leave cockpit controls available.");
            var cockpitShip = Named<ShipScreen>(hud, "ShipContainer");
            cockpitShip.UpdateStatus(new ShipStatusMessage(null, new List<ShipTileStatus>(),
                new ShipStatusSummary { DamagedTiles = 3, WorstIntegrity = 0.5f }));
            Assert.That(cockpitShip.FindControl<Label>("DamagedLabel").Text, Is.EqualTo("3"));
            Assert.That(chat, Is.InstanceOf<ResizableChatBox>());
            var chatClamp = typeof(ResizableChatBox).GetField("_clampIn", BindingFlags.Instance | BindingFlags.NonPublic)!;
            chatClamp.SetValue(chat, (byte) 0);
            hud.SetExpanded(true);
            SetArrangement(hud, Arrangement.MaximisedFromSwapped);
            Layout(hud, new Vector2(1130, 636));
            Assert.That(viewport.VisibleInTree, Is.False, "Every exit below must work with the world view gone.");
            Assert.That(recentWindows.HasClosableWindow(), Is.False);
            typeof(EscapeContextUIController).GetMethod("CloseWindowOrOpenGameMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ui.GetUIController<EscapeContextUIController>(), null);
            Assert.That(controller.Active, Is.False, "Escape with no other window open must leave the cockpit.");
            Assert.That(AimOverlayPresent(), Is.False, "Leaving with Escape must take the aim lines off the ship cam.");
            Assert.That(window.IsOpen, Is.True, "Leaving the cockpit with Escape keeps the helm open.");
            Assert.That((byte) chatClamp.GetValue(chat)!, Is.GreaterThan((byte) 0),
                "A chat box resized or rescaled while docked must be re-clamped when it leaves the cockpit.");
            Assert.That(cockpitShip.FindControl<Label>("DamagedLabel").Text, Is.EqualTo("-"),
                "Leaving the cockpit must drop its last hull sweep so the windowed SHIP page and the next entry start empty.");
            Assert.That(gunMessages.OfType<WFCockpitGunnerySessionMessage>().Select(message => (message.Active, message.Controlling)),
                Is.EqualTo(new[] { (true, false), (true, true), (true, false), (true, true), (true, false), (false, false) }),
                "GUNS claims guns; FLIGHT with a selection sends nothing; an emptied selection, a lost link or exiting release them.");
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud));
            Assert.That((networkPorts.LimitedDimension, networkPorts.Rows, networkPorts.Columns), Is.EqualTo(originalNetworkLayout),
                "Exiting must restore the helm's original network button rows and columns.");
            Assert.That(plots.Select(plot => plot.WfCockpitControls), Is.EqualTo(originalPlotControls),
                "Exiting must restore the console's normal plot interactions and annotation sizing.");
            Assert.That(viewport.Parent, Is.SameAs(viewportParent));
            Assert.That(viewport.Visible, Is.True, "Leaving with the MFD maximised must bring the world view back.");
            Assert.That(chat.Parent, Is.SameAs(chatParent));
            Assert.That(alerts.Parent, Is.SameAs(alertsParent));
            Assert.That(votes.Parent, Is.SameAs(votesParent));
            Assert.That(speech.Parent, Is.SameAs(speechParent));
            var restoredCamera = window.FindControl<ShuttleCameraBar>("CameraBar");
            Assert.That(restoredCamera.FindControl<Button>("HelmButton").Pressed, Is.True,
                "Leaving cockpit must restore the camera controls shown before temporary EXT mode.");
            Assert.That(restoredCamera.FindControl<Button>("ExternalButton").Pressed, Is.False);
            Assert.That(window.Visible, Is.True);
            Layout(screen, new Vector2(1130, 636));
            Assert.That(inventory.InventoryHotbar.VisibleInTree, Is.True, "Exiting must restore the still-open clothing grid.");
            Assert.That(inventory.InventoryHotbar.TryGetButton("jumpsuit", out var restoredClothing), Is.True);
            Assert.That(restoredClothing, Is.SameAs(clothing), "Inventory restoration must retain the live equipment binding.");
            Assert.That(restoredClothing!.Entity, Is.EqualTo(ToClient(SEntMan.GetNetEntity(uniform))));
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
            Assert.That(controller.Enter(window), Is.True, "Repeated entry must not leak or duplicate UI widgets.");
            var reentered = screen.Children.OfType<WFCockpitView>().Single();
            Assert.That(reentered, Is.Not.SameAs(hud));
            Assert.That(AimOverlayPresent(), Is.True, "Entering again must draw the aim lines again.");
            Assert.That((reentered.PanelsSwapped, reentered.MfdExpanded, reentered.MfdMaximised), Is.EqualTo((false, false, false)),
                "Panel arrangements last one cockpit session.");
            reentered.SetMaximised(true);
            Assert.That(viewport.Visible, Is.False);
            window.Close();
            Assert.That(controller.Active, Is.False, "Closing the helm must return the character HUD.");
            Assert.That(AimOverlayPresent(), Is.False, "Closing the helm must take the aim lines off the ship cam.");
            Assert.That(viewport.Visible, Is.True, "Closing the helm with the MFD maximised must bring the world view back.");
            Assert.That(viewport.Parent, Is.SameAs(viewportParent));
            window.OpenCentered();
            Assert.That(controller.Enter(window), Is.True);
            var layoutHud = screen.Children.OfType<WFCockpitView>().Single();
            layoutHud.SetSwapped(true);
            layoutHud.SetMaximised(true);
            settings.SetCVar(CCVars.UILayout, "Separated");
            Assert.That(controller.Active, Is.False, "Changing HUD layout must first restore the old screen.");
            Assert.That(AimOverlayPresent(), Is.False, "Changing HUD layout must take the aim lines off the ship cam.");
            Assert.That(viewport.Visible, Is.True, "Changing HUD layout with the MFD maximised must bring the world view back first.");
        });
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var controller = ui.GetUIController<WFCockpitUIController>();
            screen = (InGameScreen) ui.ActiveScreen!;
            normalHud = screen.Children.ToArray();
            chat = screen.ChatBox;
            chat.ChatInput.Input.Text = "unfinished cockpit transmission";
            Assert.That(controller.Enter(window), Is.True);
            controller.Exit();
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud), "Both chat layouts must restore their original widgets.");
            Assert.That(controller.Enter(window), Is.True);
            screen.Children.OfType<WFCockpitView>().Single().SetMaximised(true);
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer), Is.True);
            Assert.That(SEntMan.System<SharedWFCockpitSystem>().CanEnter(SPlayer, console), Is.False);
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<WFCockpitUIController>();
            controller.FrameUpdate(new FrameEventArgs(0.1f));
            Assert.That(controller.Active, Is.False, "Replicated unbuckling must restore the character HUD.");
            Assert.That(AimOverlayPresent(), Is.False, "Losing the seat must take the aim lines off the ship cam.");
            Assert.That(screen.GetWidget<MainViewport>()!.Visible, Is.True, "Losing the seat with the MFD maximised must bring the world view back.");
            Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud));
            Assert.That(controller.Enter(window), Is.False);
            Assert.That(chat.ChatInput.Input.Text, Is.EqualTo("unfinished cockpit transmission"));
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True));
        await RunTicks(5);
        GhostGui ghostHud = default!;
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            ghostHud = screen.GetWidget<GhostGui>()!;
            Assert.That(ghostHud.Visible, Is.False);
            Assert.That(ui.GetUIController<WFCockpitUIController>().Enter(window), Is.True);
            screen.Children.OfType<WFCockpitView>().Single().SetMaximised(true);
        });
        EntityUid ghost = default;
        await Server.WaitAssertion(() =>
        {
            ghost = SEntMan.SpawnEntity("MobObserver", MapData.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession, ghost);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<WFCockpitUIController>();
            controller.FrameUpdate(new FrameEventArgs(0.1f));
            Assert.That(controller.Active, Is.False, "Changing bodies must end the old cockpit session.");
            Assert.That(AimOverlayPresent(), Is.False, "Changing bodies must take the aim lines off the ship cam.");
            Assert.That(screen.GetWidget<MainViewport>()!.Visible, Is.True, "A body change with the MFD maximised must bring the world view back.");
            Assert.That(screen.GetWidget<GhostGui>(), Is.SameAs(ghostHud));
            Assert.That(ghostHud.VisibleInTree, Is.True,
                "Restoring the HUD must preserve the new ghost body's visibility update.");
            Assert.That(screen.GetWidget<InventoryGui>()!.InventoryButton.Visible, Is.False,
                "The departed pilot's clothing controls must not be restored over the ghost HUD.");
        });
        await Server.WaitAssertion(() =>
        {
            Server.PlayerMan.SetAttachedEntity(ServerSession, SPlayer);
            SEntMan.DeleteEntity(ghost);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            window.Dispose();
            settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
    }

    [Test]
    public async Task GunneryControlFollowsGunsTabAndSelection()
    {
        EntityUid console = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            var chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
        });
        await RunTicks(10);
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var screen = (InGameScreen) ui.ActiveScreen!;
            var world = screen.GetWidget<MainViewport>()!;
            var worldParent = world.Parent;
            var controller = ui.GetUIController<WFCockpitUIController>();
            var messages = new List<BoundUserInterfaceMessage>();
            var window = new ShuttleConsoleWindow();
            window.WfCockpitGunneryCommand += messages.Add;
            window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
            window.OpenCentered();
            Assert.That(controller.Enter(window), Is.True);
            ui.ReleaseKeyboardFocus();
            var hud = screen.Children.OfType<WFCockpitView>().Single();
            var size = new Vector2(1130, 636);
            var nav = window.FindControl<NavScreen>("NavContainer").FindControl<ShuttleNavControl>("NavRadar");
            nav.SetMatrix(new EntityCoordinates(ToClient(MapData.MapUid), Vector2.Zero), Angle.Zero);
            var input = Descendants(hud).OfType<WFCockpitFireInput>().Single();
            var weapon = new FireControllableEntry(new NetEntity(910), default, "Battery", 10, true);
            var linked = new NetEntity(911);

            FireControlConsoleBoundInterfaceState Bank(params FireControllableEntry[] weapons) =>
                new(true, weapons, new NavInterfaceState(250, null, null, new(), default));
            ScreenCoordinates At(Vector2 offset = default) => WFCockpitFireInputTest.Pointer(nav, nav.Size / 2 + offset);
            IEnumerable<WFCockpitGunnerySessionMessage> Sessions() => messages.OfType<WFCockpitGunnerySessionMessage>();
            List<FireControlConsoleFireMessage> Fired() => messages.OfType<WFCockpitGunneryCommandMessage>()
                .Select(message => message.Command).OfType<FireControlConsoleFireMessage>().ToList();
            GUIBoundKeyEventArgs Click() => WFCockpitFireInputTest.Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Down, At());
            void Release() => WFCockpitFireInputTest.Key(nav, EngineKeyFunctions.UIClick, BoundKeyState.Up, At());
            void Tick(float delta, bool leftDown, Vector2 offset = default) =>
                WFCockpitFireInputTest.Tick(input, delta, At(offset), leftDown);
            void Select(bool guns)
            {
                hud.SelectGunnery(guns);
                Layout(hud, size);
            }
            void SelectWeapon()
            {
                var panel = Descendants(hud).OfType<WFCockpitGunneryPanel>().Single();
                Toggle(Descendants(panel).OfType<WFWeaponGrid>().Single().Children.OfType<Button>().Single(), true);
                Assert.That(panel.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }));
            }

            Layout(hud, size);
            Assert.That(ui.MouseGetControl(At()), Is.SameAs(nav), "The NAV plot must be the hovered control for the pointer checks.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            Assert.That(Sessions().Select(message => (message.Active, message.Controlling)), Is.EqualTo(new[] { (true, false) }),
                "Discovering a gun bank on FLIGHT must not claim it.");

            Assert.That(Click().Handled, Is.False, "FLIGHT without a selection leaves ordinary NAV clicks alone.");
            Release();
            Tick(0.5f, false);
            Tick(0.5f, false, new Vector2(3, 0));
            Assert.That(messages.OfType<WFCockpitGunneryCommandMessage>(), Is.Empty, "FLIGHT without a selection sends no fire or aim.");
            Assert.That(nav.CustomCursorShape, Is.Null, "FLIGHT without a selection keeps the normal pointer.");

            Select(true);
            Assert.That(Sessions().Last().Controlling, Is.True, "GUNS claims control.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Assert.That(Sessions().Count(), Is.EqualTo(2), "A state update on GUNS must not repeat the claim.");
            Select(false);
            Assert.That(Sessions().Last().Controlling, Is.False, "Leaving GUNS with nothing selected releases control.");
            Assert.That(Sessions().Count(), Is.EqualTo(3));
            Assert.That(Click().Handled, Is.False, "Released control returns NAV clicks to normal.");
            Release();
            Assert.That(Fired(), Is.Empty);

            Select(true);
            SelectWeapon();
            Select(false);
            Assert.That(Sessions().Count(), Is.EqualTo(4), "Leaving GUNS with a selection must not send a release.");
            Assert.That(Sessions().Last().Controlling, Is.True, "FLIGHT with a selection keeps control.");
            Tick(0, false);
            Assert.That(nav.CustomCursorShape, Is.Not.Null, "The aiming reticle shows on FLIGHT while weapons are selected.");
            Assert.That(Click().Handled, Is.True, "A FLIGHT click with a selection is consumed by firing.");
            Assert.That(Fired(), Has.Count.EqualTo(1));
            Assert.That(Fired()[0].Selected, Is.EquivalentTo(new[] { weapon.NetEntity }), "A press fires the selected weapons.");
            Tick(0.11f, true);
            Assert.That(Fired(), Has.Count.EqualTo(2), "Holding the button repeats fire on FLIGHT.");
            Assert.That(Fired()[1].Selected, Is.EquivalentTo(new[] { weapon.NetEntity }));
            Release();
            Tick(0.11f, false, new Vector2(3, 0));
            Assert.That(Fired(), Has.Count.EqualTo(3), "Pointer movement keeps updating missile aim on FLIGHT.");
            Assert.That(Fired()[2].Selected, Is.Empty, "Aim updates carry no weapons to fire.");

            Assert.That(Click().Handled, Is.True);
            Assert.That(Fired(), Has.Count.EqualTo(4));
            Select(true);
            Tick(0.11f, true);
            Assert.That(Fired(), Has.Count.EqualTo(5));
            Assert.That(Fired()[4].Selected, Is.Empty, "Switching tabs cancels a held trigger.");
            Release();
            Select(false);
            Assert.That(Click().Handled, Is.True, "A fresh press fires again after the tab change.");
            Assert.That(Fired()[^1].Selected, Is.EquivalentTo(new[] { weapon.NetEntity }));
            Release();

            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank()));
            Assert.That(Descendants(hud).OfType<WFCockpitGunneryPanel>().Single().HasSelectedWeapons, Is.False);
            Assert.That(Sessions().Last().Controlling, Is.False, "A pruned selection on FLIGHT releases control at once.");
            var firedBefore = Fired().Count;
            Assert.That(Click().Handled, Is.False, "Input returns to normal once the selection is gone.");
            Release();
            Tick(0.5f, false, new Vector2(5, 0));
            Assert.That(Fired(), Has.Count.EqualTo(firedBefore));
            Assert.That(nav.CustomCursorShape, Is.Null, "The reticle goes away with the selection.");

            Select(true);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            SelectWeapon();
            Select(false);
            Assert.That(Sessions().Last().Controlling, Is.True);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(null, null));
            Assert.That(Sessions().Last().Controlling, Is.False, "A lost link on FLIGHT releases control at once.");
            Assert.That(Click().Handled, Is.False);
            Release();
            Assert.That(Fired(), Has.Count.EqualTo(firedBefore), "Nothing fires once control is released.");

            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            Select(true);
            SelectWeapon();
            Select(false);
            Assert.That(Sessions().Last().Controlling, Is.True, "Re-linked and armed on FLIGHT holds control.");

            var worldPointer = WFCockpitFireInputTest.Pointer(world.Viewport, world.Viewport.Size / 2);
            hud.SetMaximised(true);
            Layout(hud, size);
            Assert.That(world.VisibleInTree, Is.False, "A maximised MFD must stop drawing the world view.");
            Assert.That(ui.MouseGetControl(At()), Is.SameAs(nav), "The NAV plot must still be the hovered control while maximised.");
            var firedBeforeMaximised = Fired().Count;
            Assert.That(Click().Handled, Is.True, "Aiming through the NAV plot keeps working with the world view gone.");
            Assert.That(Fired(), Has.Count.EqualTo(firedBeforeMaximised + 1));
            Release();
            Assert.That(ui.MouseGetControl(worldPointer), Is.Not.SameAs(world.Viewport), "A hidden world view must not capture clicks.");
            hud.SetMaximised(false);
            Layout(hud, size);
            Assert.That(world.VisibleInTree, Is.True, "Restoring the MFD must bring the world view back.");
            Assert.That(Click().Handled, Is.True, "NAV aiming survives restoring the world view.");
            Release();
            hud.SetMaximised(true);

            controller.Exit();
            Assert.That(AimOverlayPresent(), Is.False, "Exiting while armed must take the aim lines off the ship cam.");
            Assert.That(world.Visible, Is.True, "Leaving with the MFD maximised must bring the world view back.");
            Assert.That(world.Parent, Is.SameAs(worldParent));
            Assert.That(Sessions().Select(message => (message.Active, message.Controlling)).TakeLast(2),
                Is.EqualTo(new[] { (true, true), (false, false) }),
                "Exiting while armed on FLIGHT ends the session without a trailing release.");
            window.Dispose();
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
    }

    [Test]
    public async Task EveryClientExitBringsTheWorldViewBackFromAnyArrangement()
    {
        EntityUid console = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            var chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
        });
        await RunTicks(10);
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var screen = (InGameScreen) ui.ActiveScreen!;
            var controller = ui.GetUIController<WFCockpitUIController>();
            var window = new ShuttleConsoleWindow();
            window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
            window.OpenCentered();
            var world = screen.GetWidget<MainViewport>()!;
            var worldParent = world.Parent;
            var normalHud = screen.Children.ToArray();
            var size = new Vector2(1130, 636);
            foreach (var arrangement in Enum.GetValues<Arrangement>())
            foreach (var exit in new[] { "button", "escape", "controller", "helm closed" })
            {
                var context = $"{arrangement}, leaving by {exit}";
                Assert.That(controller.Enter(window), Is.True, context);
                Assert.That(AimOverlayPresent(), Is.True, $"{context}: the cockpit must draw its aim lines on the ship cam");
                var hud = screen.Children.OfType<WFCockpitView>().Single();
                Layout(hud, size);
                SetArrangement(hud, arrangement);
                Layout(hud, size);
                Assert.That(world.VisibleInTree, Is.EqualTo(arrangement is Arrangement.Normal or Arrangement.Swapped), context);
                switch (exit)
                {
                    case "button":
                        Press(Named<Button>(hud, "CockpitExit"));
                        break;
                    case "escape":
                        Assert.That(controller.ExitOnEscape(), Is.True, context);
                        break;
                    case "controller":
                        controller.Exit();
                        break;
                    default:
                        window.Close();
                        break;
                }
                Assert.That(controller.Active, Is.False, context);
                Assert.That(AimOverlayPresent(), Is.False, $"{context}: the aim lines must leave the ship cam");
                Assert.That(world.Visible, Is.True, $"{context}: the world view must be drawn again");
                Assert.That(world.VisibleInTree, Is.True, context);
                Assert.That(world.Parent, Is.SameAs(worldParent), $"{context}: the world view must return to its screen slot");
                Assert.That(screen.Children.ToArray(), Is.EqualTo(normalHud), $"{context}: the normal HUD must come back exactly");
                if (exit == "helm closed")
                    window.OpenCentered();
            }
            window.Dispose();
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
    }

    [Test]
    public async Task APoppedOutChatLogFollowsTheChatIntoTheCockpitAndBack()
    {
        var (console, chair) = await SpawnHelm();
        await MakePilot(console);
        await Buckle(chair);
        await RunTicks(10);
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        var ui = Client.ResolveDependency<IUserInterfaceManager>();
        var popout = ui.GetUIController<ChatLogPopoutUIController>();
        var controller = ui.GetUIController<WFCockpitUIController>();
        ChatBox chat = default!;
        ShuttleConsoleWindow window = default!;
        Vector2 minSize = default;
        float marginBottom = 0, shrunkBottom = 0;
        float Bottom() => chat.GetValue<float>(LayoutContainer.MarginBottomProperty);
        bool NoticeShown() => chat.ChatInput.Parent!.Children
            .Any(child => child != chat.ChatInput && child != chat.WfTabBar && child is not OutputPanel);
        try
        {
            await Client.WaitAssertion(() =>
            {
                chat = ((InGameScreen) ui.ActiveScreen!).ChatBox;
                Assert.That(chat, Is.InstanceOf<ResizableChatBox>());
                minSize = chat.MinSize;
                marginBottom = Bottom();
                popout.PopOut(chat);
                Assert.That(chat.WfLogPoppedOut, Is.True);
                shrunkBottom = Bottom();
                Assert.That(shrunkBottom, Is.LessThan(marginBottom), "The floating chat box shrinks to its input line.");
                window = OpenHelm(console);
                Assert.That(controller.Enter(window), Is.True);
            });
            await RunTicks(5);
            await Client.WaitAssertion(() =>
            {
                Assert.That(controller.Active, Is.True);
                Assert.That(chat.WfLogPoppedOut, Is.True, "The log window must pick the chat up again in the cockpit.");
                Assert.That(popout.Window, Is.Not.Null);
                Assert.That(NoticeShown(), Is.True, "The cockpit's chat panel must say where the log went, not sit empty.");
                Assert.That(chat.VisibleInTree, Is.True);
                controller.Exit();
                Assert.That(chat.WfLogPoppedOut, Is.False, "The log docks while the chat moves back.");
                Assert.That(NoticeShown(), Is.False);
                Assert.That(chat.MinSize, Is.EqualTo(minSize));
                Assert.That(Bottom(), Is.EqualTo(marginBottom));
            });
            await RunTicks(8);
            await Client.WaitAssertion(() =>
            {
                Assert.That(chat.WfLogPoppedOut, Is.True, "Back on the normal HUD the log pops out again.");
                Assert.That(NoticeShown(), Is.False);
                Assert.That(Bottom(), Is.EqualTo(shrunkBottom).Within(1f),
                    "The re-clamp on leaving the cockpit must not stretch the shrunken chat box back to full height.");
                popout.Toggle(chat);
                Assert.That(chat.WfLogPoppedOut, Is.False);
                Assert.That(popout.Window, Is.Null);
                Assert.That(chat.MinSize, Is.EqualTo(minSize));
                Assert.That(Bottom(), Is.EqualTo(marginBottom), "Docking the log must give the chat box its size back.");
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                if (controller.Active)
                    controller.Exit();
                if (chat is { WfLogPoppedOut: true })
                    popout.Toggle(chat);
                window?.Dispose();
                settings.SetCVar(CCVars.UILayout, originalLayout);
            });
        }
    }

    [Test]
    public async Task ExpandSharesTheWidthInEitherArrangementAndPutsItBack()
    {
        await InCockpit(cockpit =>
        {
            var (ui, _, viewport, chat, window, _, hud) = cockpit;
            var settings = Client.ResolveDependency<IConfigurationManager>();
            window.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(true, true, 0.76f, MathF.PI / 2f, 0.65f, MathF.PI / 2f));
            var pages = new[] { "wf-cockpit-nav", "wf-cockpit-ship", "wf-cockpit-map", "wf-cockpit-dock", "wf-cockpit-access", "wf-cockpit-shield-page", "wf-cockpit-systems", "wf-cockpit-alarms" };
            var expand = Named<Button>(hud, "CockpitMfdExpand");
            foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
            {
                settings.SetCVar(WolfgateCVars.UiStyle, theme);
                foreach (var size in new[] { new Vector2(1130, 636), new Vector2(1600, 900), new Vector2(1920, 1080) })
                foreach (var swapped in new[] { false, true })
                {
                    SetArrangement(hud, swapped ? Arrangement.Swapped : Arrangement.Normal);
                    hud.SelectPage("wf-cockpit-nav");
                    Layout(hud, size);
                    var context = $"{theme}, {size}, {(swapped ? "swapped" : "normal")}";
                    var before = Snapshot(hud, viewport);
                    Assert.That(expand.Disabled, Is.False, $"{context}: Expand is on offer in a window this wide.");
                    Assert.That(expand.Pressed, Is.False, context);
                    Toggle(expand, true);
                    Layout(hud, size);
                    Assert.That(hud.MfdExpanded, Is.True, context);
                    Assert.That(expand.Text, Is.EqualTo(Loc.GetString("wf-cockpit-collapse")), context);
                    AssertExpanded(hud, viewport, size, swapped, before, context);
                    if (size.X < 1920)
                    {
                        foreach (var page in pages)
                        {
                            hud.SelectPage(page);
                            Layout(hud, size);
                            var pageContext = $"{context}, expanded, {page}";
                            AssertCockpitLayout(hud, viewport, chat, ui, size, theme, page);
                            AssertPanelsFitScreen(hud, viewport, size, pageContext);
                            AssertMfdHeaderFits(hud, ui, pageContext);
                            if (page == "wf-cockpit-access")
                                AssertAccessPage(hud, ui, pageContext);
                            if (page == "wf-cockpit-ship")
                                AssertShipPage(hud, ui, pageContext);
                        }
                        hud.SelectPage("wf-cockpit-nav");
                        Layout(hud, size);
                    }
                    Toggle(expand, false);
                    Layout(hud, size);
                    Assert.That(expand.Text, Is.EqualTo(Loc.GetString("wf-cockpit-expand")), context);
                    AssertSameBoxes(Snapshot(hud, viewport), before, $"{context}: collapsing must restore the normal widths");
                    AssertMfdHeaderFits(hud, ui, $"{context}, collapsed");
                }
            }
        });
    }

    [Test]
    public async Task ExpandIsOffWhenMaximisedOrTooNarrowKeepsItsStateWhenSwappedAndStartsOffInANewSession()
    {
        await InCockpit(cockpit =>
        {
            var (_, screen, viewport, _, window, controller, hud) = cockpit;
            var expand = Named<Button>(hud, "CockpitMfdExpand");
            var swap = Named<Button>(hud, "CockpitMfdSwap");
            var maximise = Named<Button>(hud, "CockpitMfdMaximise");
            var wide = new Vector2(1130, 636);
            var narrow = new Vector2(1090, 636);
            Layout(hud, wide);
            Assert.That(expand.Disabled, Is.False, "Expand is on offer once the window is wide enough to change something.");

            Toggle(maximise, true);
            Layout(hud, wide);
            Assert.That(expand.Disabled, Is.True, "A maximised MFD already has all the room, so Expand must not be offered.");
            var maximised = Snapshot(hud, viewport);
            Toggle(maximise, false);
            Layout(hud, wide);
            Assert.That(expand.Disabled, Is.False, "Expand returns when Maximise is released.");

            Toggle(expand, true);
            Layout(hud, wide);
            var expanded = Snapshot(hud, viewport);
            Toggle(maximise, true);
            Layout(hud, wide);
            Assert.That(expand.Disabled, Is.True);
            Assert.That(expand.Pressed && hud.MfdExpanded, Is.True, "Maximise must not forget that Expand was on.");
            AssertSameBoxes(Snapshot(hud, viewport), maximised, "An expanded MFD maximises exactly as a normal one does");
            Toggle(maximise, false);
            Layout(hud, wide);
            Assert.That(expand.Disabled, Is.False);
            AssertSameBoxes(Snapshot(hud, viewport), expanded, "Releasing Maximise must bring the expanded arrangement back");

            Toggle(expand, false);
            Layout(hud, narrow);
            Assert.That(expand.Disabled, Is.True, "Expand cannot widen anything in a window this narrow, so it must not be offered.");
            var narrowNormal = Snapshot(hud, viewport);
            Layout(hud, wide);
            Assert.That(expand.Disabled, Is.False, "Expand returns once it can widen the MFD.");
            Toggle(expand, true);
            Layout(hud, narrow);
            Assert.That(expand.Disabled, Is.False, "A pressed Expand stays on offer in a narrow window so it can be released.");
            AssertSameBoxes(Snapshot(hud, viewport), narrowNormal, "Expand changes nothing in a window this narrow");
            Toggle(expand, false);
            Layout(hud, narrow);
            Assert.That(expand.Disabled, Is.True);

            var size = new Vector2(1600, 900);
            Layout(hud, size);
            var normalSplit = Snapshot(hud, viewport);
            Toggle(expand, true);
            Layout(hud, size);
            var expandedSplit = Snapshot(hud, viewport);

            Toggle(swap, true);
            Layout(hud, size);
            Assert.That(expand.Pressed && hud.MfdExpanded, Is.True, "Swapping the panels must not release Expand.");
            Assert.That(expand.Disabled, Is.False);
            var swappedBoxes = Snapshot(hud, viewport);
            AssertBox(swappedBoxes["world"], expandedSplit["CockpitMfd"], "The world view takes the side column the MFD had");
            AssertBox(swappedBoxes["CockpitMfd"], expandedSplit["world"], "The MFD takes the centre the world view had");

            Toggle(swap, false);
            Layout(hud, size);
            Assert.That(hud.MfdExpanded, Is.True);
            AssertSameBoxes(Snapshot(hud, viewport), expandedSplit, "Swapping back must keep the expanded split");
            Toggle(expand, false);
            Layout(hud, size);
            AssertSameBoxes(Snapshot(hud, viewport), normalSplit, "Collapsing must return to the normal split");

            Toggle(expand, true);
            Toggle(swap, true);
            controller.Exit();
            Assert.That(controller.Enter(window), Is.True);
            var fresh = screen.Children.OfType<WFCockpitView>().Single();
            Assert.That(fresh, Is.Not.SameAs(hud));
            Assert.That((fresh.PanelsSwapped, fresh.MfdExpanded, fresh.MfdMaximised), Is.EqualTo((false, false, false)),
                "Panel arrangements last one cockpit session.");
        });
    }

    [Test]
    public async Task AutoEnterEntersOnceForABuckledPilotAndNeverWhenOffOrWhenBusy()
    {
        var (console, chair) = await SpawnHelm();
        var (otherConsole, _) = await SpawnHelm();
        await MakePilot(console);
        await Buckle(chair);
        await RunTicks(10);
        await WithAutoEnter(true, () => Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var settings = Client.ResolveDependency<IConfigurationManager>();
            var controller = ui.GetUIController<WFCockpitUIController>();
            var screen = (InGameScreen) ui.ActiveScreen!;
            var window = OpenHelm(console);
            try
            {
                controller.AutoEnterOnOpen(window);
                Assert.That(controller.Active, Is.False, "Entering waits for the next UI frame.");
                controller.FrameUpdate(new FrameEventArgs(0.05f));
                Assert.That(controller.Active, Is.True, "A helm opened while buckled and piloting enters the cockpit.");
                Assert.That(screen.Children.OfType<WFCockpitView>().Count(), Is.EqualTo(1));

                var hud = screen.Children.OfType<WFCockpitView>().Single();
                Layout(hud, new Vector2(1130, 636));
                Press(Named<Button>(hud, "CockpitExit"));
                Assert.That(controller.Active, Is.False);
                AssertStaysOut("the Exit button");

                Assert.That(controller.Enter(window), Is.True, "The pilot can still enter by hand.");
                Assert.That(controller.ExitOnEscape(), Is.True);
                AssertStaysOut("Escape");

                Assert.That(controller.Enter(window), Is.True);
                controller.Exit();
                AssertStaysOut("a controller exit");

                window.Close();
                window.OpenCentered();
                controller.AutoEnterOnOpen(window);
                controller.FrameUpdate(new FrameEventArgs(0.05f));
                Assert.That(controller.Active, Is.True, "Opening the helm again is a new opening and enters again.");

                // Another session is active, so a second opening neither replaces it nor opens a second cockpit.
                var other = OpenHelm(otherConsole);
                try
                {
                    controller.AutoEnterOnOpen(other);
                    controller.FrameUpdate(new FrameEventArgs(0.05f));
                    Assert.That(screen.Children.OfType<WFCockpitView>().Count(), Is.EqualTo(1));
                    Assert.That((window.WfCockpitActive, other.WfCockpitActive), Is.EqualTo((true, false)));
                    controller.Exit();

                    // The pilot is not flying this helm, so it never enters, however long it waits.
                    controller.AutoEnterOnOpen(other);
                    for (var i = 0; i < 4; i++)
                        controller.FrameUpdate(new FrameEventArgs(0.3f));
                    Assert.That(controller.Active, Is.False, "A helm the pilot cannot use never opens the cockpit.");
                }
                finally
                {
                    other.Dispose();
                }

                settings.SetCVar(WolfgateCVars.CockpitAutoEnter, false);
                window.Close();
                window.OpenCentered();
                controller.AutoEnterOnOpen(window);
                for (var i = 0; i < 4; i++)
                    controller.FrameUpdate(new FrameEventArgs(0.1f));
                Assert.That(controller.CanEnter(window.WfCockpitConsole), Is.True);
                Assert.That(controller.Active, Is.False, "With the option off, the helm opens as a window as it always did.");
            }
            finally
            {
                controller.Exit();
                window.Dispose();
            }

            void AssertStaysOut(string how)
            {
                for (var i = 0; i < 6; i++)
                    controller.FrameUpdate(new FrameEventArgs(0.3f));
                Assert.That(controller.Active, Is.False, $"After leaving with {how} the same window must not put the pilot back in.");
                Assert.That(window.IsOpen, Is.True);
            }
        }));
    }

    [Test]
    public async Task AutoEnterWaitsBrieflyForThePilotStateGivesUpAndIgnoresLateBuckling()
    {
        var (console, chair) = await SpawnHelm();
        await Buckle(chair);
        await RunTicks(10);
        await WithAutoEnter(true, async () =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var controller = ui.GetUIController<WFCockpitUIController>();
            ShuttleConsoleWindow window = default!;
            await Client.WaitAssertion(() =>
            {
                window = OpenHelm(console);
                controller.AutoEnterOnOpen(window);
                controller.FrameUpdate(new FrameEventArgs(0.2f));
                Assert.That(controller.Active, Is.False, "Without the piloting state there is nothing to enter yet.");
            });
            await MakePilot(console);
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                controller.FrameUpdate(new FrameEventArgs(0.05f));
                Assert.That(controller.Active, Is.True, "The pilot state arrived within the wait, so the cockpit opens.");
                controller.Exit();
                window.Dispose();
            });

            // A second helm never gets its pilot state in time, so it stops waiting.
            await Server.WaitAssertion(() => SEntMan.System<ShuttleConsoleSystem>().RemovePilot(SPlayer));
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                window = OpenHelm(console);
                controller.AutoEnterOnOpen(window);
                controller.FrameUpdate(new FrameEventArgs(0.6f));
                controller.FrameUpdate(new FrameEventArgs(0.6f));
                Assert.That(controller.Active, Is.False);
            });
            await MakePilot(console);
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                controller.FrameUpdate(new FrameEventArgs(0.05f));
                Assert.That(controller.CanEnter(window.WfCockpitConsole), Is.True, "The pilot state has arrived by now.");
                Assert.That(controller.Active, Is.False, "Roughly a second after opening, the helm no longer tries to enter by itself.");
                window.Dispose();
            });

            // The pilot must be seated when the helm opens; buckling later does not count.
            await Server.WaitAssertion(() =>
                Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(SPlayer, SPlayer), Is.True));
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                window = OpenHelm(console);
                controller.AutoEnterOnOpen(window);
                controller.FrameUpdate(new FrameEventArgs(0.1f));
                Assert.That(controller.Active, Is.False, "A standing pilot has no seat to enter from.");
            });
            await Buckle(chair);
            await MakePilot(console);
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                controller.FrameUpdate(new FrameEventArgs(0.05f));
                Assert.That(controller.CanEnter(window.WfCockpitConsole), Is.True, "The pilot is seated now, so entering by hand would work.");
                Assert.That(controller.Active, Is.False, "Buckling after the helm opened must not enter the cockpit.");
                window.Dispose();
            });
        });
    }

    [Test]
    public async Task OpeningTheHelmBuckledThroughItsInterfaceEntersTheCockpit()
    {
        var (console, chair) = await SpawnHelm();
        await MakePilot(console);
        await Buckle(chair);
        await RunTicks(10);
        await WithAutoEnter(true, async () =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var controller = ui.GetUIController<WFCockpitUIController>();
            await Server.WaitAssertion(() =>
                SEntMan.System<SharedUserInterfaceSystem>().OpenUi(console, ShuttleConsoleUiKey.Key, SPlayer));
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                Assert.That(Descendants(ui.WindowRoot).OfType<ShuttleConsoleWindow>().Count(), Is.EqualTo(1),
                    "The server opened the helm through its interface.");
                Assert.That(controller.Active, Is.True, "A helm opened through its interface while buckled enters the cockpit by itself.");
                controller.Exit();
            });
            await RunTicks(10);
            await Client.WaitAssertion(() =>
                Assert.That(controller.Active, Is.False, "Leaving the cockpit must not put the pilot back in while the helm stays open."));
            await Server.WaitAssertion(() =>
                SEntMan.System<SharedUserInterfaceSystem>().CloseUi(console, ShuttleConsoleUiKey.Key, SPlayer));
            await RunTicks(10);
        });
    }

    [Test]
    public async Task AimLinesFollowSelectionAndPointerOnTheNavPlotAndTheShipCam()
    {
        EntityUid console = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            var chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
        });
        await RunTicks(10);
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var screen = (InGameScreen) ui.ActiveScreen!;
            var world = screen.GetWidget<MainViewport>()!;
            var controller = ui.GetUIController<WFCockpitUIController>();
            var window = new ShuttleConsoleWindow();
            window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
            window.OpenCentered();
            Assert.That(controller.Enter(window), Is.True);
            ui.ReleaseKeyboardFocus();
            var hud = screen.Children.OfType<WFCockpitView>().Single();
            var size = new Vector2(1130, 636);
            var nav = window.FindControl<NavScreen>("NavContainer").FindControl<ShuttleNavControl>("NavRadar");
            var map = ToClient(MapData.MapUid);
            nav.SetMatrix(new EntityCoordinates(map, Vector2.Zero), Angle.Zero);
            var input = Descendants(hud).OfType<WFCockpitFireInput>().Single();
            var plotLines = Descendants(hud).OfType<WFCockpitAimLines>().Single();
            var overlay = Client.ResolveDependency<IOverlayManager>().GetOverlay<WFCockpitAimOverlay>();
            var transforms = CEntMan.System<SharedTransformSystem>();
            var mount = new Vector2(4, 1);
            var weapon = new FireControllableEntry(WFCockpitFireInputTest.UnknownWeapon(31),
                CEntMan.GetNetCoordinates(new EntityCoordinates(map, mount)), "Battery", 10, true, ignoresLos: true);
            var linked = new NetEntity(911);

            FireControlConsoleBoundInterfaceState Bank(params FireControllableEntry[] weapons) =>
                new(true, weapons, new NavInterfaceState(250, null, null, new(), default));
            void Tick() => WFCockpitFireInputTest.Tick(input, 0, WFCockpitFireInputTest.Pointer(nav, nav.Size / 2), false);
            void Select(bool guns)
            {
                hud.SelectGunnery(guns);
                Layout(hud, size);
            }
            void SelectWeapon()
            {
                var panel = Descendants(hud).OfType<WFCockpitGunneryPanel>().Single();
                Toggle(Descendants(panel).OfType<WFWeaponGrid>().Single().Children.OfType<Button>().Single(), true);
                Assert.That(panel.SelectedWeapons, Is.EquivalentTo(new[] { weapon.NetEntity }));
            }
            void Arm()
            {
                window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
                Layout(hud, size);
                Select(true);
                SelectWeapon();
            }

            Layout(hud, size);
            Assert.That(ui.MouseGetControl(WFCockpitFireInputTest.Pointer(nav, nav.Size / 2)), Is.SameAs(nav));
            var eye = world.Viewport.Eye;
            Assert.That(eye, Is.Not.Null, "The world view must have the pilot's eye for the ship cam lines.");
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank(weapon)));
            Layout(hud, size);
            Tick();
            Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out _), Is.False, "Nothing is selected, so there is nothing to draw.");
            Assert.That(plotLines.Lines(), Is.Empty);

            Arm();
            Select(false);
            Tick();
            Assert.That(input.AimTarget, Is.Not.Null, "Selected weapons on FLIGHT aim at the pointer.");
            foreach (var guns in new[] { false, true })
            {
                Select(guns);
                Tick();
                var tab = guns ? "GUNS" : "FLIGHT";
                // Each tab lays the plot out differently, so the pointer reaches a different point on each.
                Assert.That(nav.WfCockpitTryGetWorldToView(out var worldToView, out _), Is.True, tab);
                Assert.That(input.AimTarget, Is.Not.Null, tab);
                var aimAt = transforms.ToMapCoordinates(input.AimTarget!.Value).Position;
                Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out var cam), Is.True, $"{tab}: the ship cam draws the line.");
                Assert.That(cam, Has.Count.EqualTo(1), tab);
                Assert.That(Vector2.Distance(cam[0].From, mount), Is.LessThan(0.01f), $"{tab}: the line starts at the weapon.");
                Assert.That(Vector2.Distance(cam[0].To, aimAt), Is.LessThan(0.01f), $"{tab}: the line ends at the pointer.");
                var plot = plotLines.Lines();
                Assert.That(plot, Has.Count.EqualTo(1), $"{tab}: the NAV plot draws the same line.");
                Assert.That(Vector2.Distance(plot[0].From, Vector2.Transform(mount, worldToView)), Is.LessThan(0.5f), tab);
                Assert.That(Vector2.Distance(plot[0].To, Vector2.Transform(aimAt, worldToView)), Is.LessThan(0.5f), tab);
            }

            Select(false);
            hud.SetMaximised(true);
            Layout(hud, size);
            Tick();
            Assert.That(world.VisibleInTree, Is.False);
            Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out _), Is.False, "A maximised MFD hides the world view and its lines.");
            Assert.That(plotLines.Lines(), Has.Count.EqualTo(1), "The NAV plot keeps its line with the world view hidden.");
            hud.SetMaximised(false);
            Layout(hud, size);
            Tick();
            Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out _), Is.True);

            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(linked, Bank()));
            Tick();
            Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out _), Is.False, "Without a selection gun control is released and nothing draws.");
            Assert.That(plotLines.Lines(), Is.Empty);

            Arm();
            Select(false);
            Tick();
            Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out _), Is.True);
            window.WfReceiveCockpitGunnery(new WFCockpitGunneryStateMessage(null, null));
            Tick();
            Assert.That(overlay.TryGetSegments(eye, MapData.MapId, out _), Is.False, "A lost link releases gun control and clears the lines.");

            controller.Exit();
            Assert.That(AimOverlayPresent(), Is.False);
            window.Dispose();
            settings.SetCVar(CCVars.UILayout, originalLayout);
        });
    }

    [Test]
    public async Task AimLinesStopAtTheWeaponsOwnGridButIgnoreOtherGrids()
    {
        var weaponAt = new Vector2(0.5f, 0.5f);
        EntityUid mount = default;
        EntityUid ownWall = default;
        EntityUid foreignGrid = default;
        EntityUid foreignWall = default;
        await Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 8; x++)
                MapSystem.SetTile(MapData.Grid, new Vector2i(x, 0), MapData.Tile.Tile);
            mount = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.Grid.Owner, weaponAt));
            ownWall = SEntMan.SpawnEntity("WFCockpitTestWall", new EntityCoordinates(MapData.Grid.Owner, new Vector2(3.5f, 0.5f)));
            var foreign = MapMan.CreateGridEntity(MapId);
            Transform.SetLocalPosition(foreign.Owner, new Vector2(0, 5));
            MapSystem.SetTile(foreign.Owner, foreign.Comp, Vector2i.Zero, MapData.Tile.Tile);
            foreignGrid = foreign.Owner;
            foreignWall = SEntMan.SpawnEntity("WFCockpitTestWall", new EntityCoordinates(foreign.Owner, new Vector2(0.5f, 0.5f)));
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var map = ToClient(MapData.MapUid);
            var physics = CEntMan.System<SharedPhysicsSystem>();
            Assert.That(CEntMan.EntityExists(ToClient(ownWall)) && CEntMan.EntityExists(ToClient(foreignWall)), Is.True,
                "The client must see both walls.");
            const int impassable = (int) Content.Shared.Physics.CollisionGroup.Impassable;
            Assert.That(physics.IntersectRay(MapId, new CollisionRay(weaponAt, Vector2.UnitX, impassable), 10f).Any(), Is.True,
                "The client's physics must see the weapon's own wall.");
            Assert.That(physics.IntersectRay(MapId, new CollisionRay(weaponAt, Vector2.UnitY, impassable), 10f).Any(), Is.True,
                "The client's physics must see the other grid's wall.");

            var stale = CEntMan.GetNetCoordinates(new EntityCoordinates(map, new Vector2(40, 40)));
            var onOwnGrid = CEntMan.GetNetCoordinates(new EntityCoordinates(ToClient(MapData.Grid.Owner), weaponAt));
            var known = new FireControllableEntry(SEntMan.GetNetEntity(mount), stale, "Known", ignoresLos: false);
            var lost = new FireControllableEntry(WFCockpitFireInputTest.UnknownWeapon(41), onOwnGrid, "Lost", ignoresLos: false);
            var blind = new FireControllableEntry(WFCockpitFireInputTest.UnknownWeapon(42), onOwnGrid, "Blind", ignoresLos: true);
            var idle = new FireControllableEntry(WFCockpitFireInputTest.UnknownWeapon(43), onOwnGrid, "Idle", ignoresLos: true);
            var selection = new HashSet<NetEntity> { known.NetEntity, lost.NetEntity, blind.NetEntity };
            EntityCoordinates? aim = null;
            var preview = new WFCockpitAimPreview(() => aim, selection.Contains);
            preview.UpdateWeapons(new[] { known, lost, blind, idle });

            // The server's fire check ignores everything off the weapon's grid, so only the weapon's own hull blocks a shot.
            var eastAim = new Vector2(6.5f, 0.5f);
            var northAim = new Vector2(0.5f, 9.5f);
            aim = new EntityCoordinates(map, eastAim);
            var east = preview.Refresh();
            Assert.That(east, Has.Count.EqualTo(1),
                "A wall on the weapon's own grid blocks every selected weapon that cannot fire through cover.");
            Assert.That(Vector2.Distance(east[0].From, weaponAt), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(east[0].To, eastAim), Is.LessThan(0.01f));
            selection.Remove(blind.NetEntity);
            Assert.That(preview.Refresh(), Is.Empty, "Without the weapon that ignores line of sight, nothing draws through the wall.");
            selection.Add(blind.NetEntity);

            aim = new EntityCoordinates(map, northAim);
            var north = preview.Refresh();
            Assert.That(north, Has.Count.EqualTo(3),
                "Another grid in the way never blocks a line; an unselected weapon never draws one.");
            foreach (var line in north)
            {
                Assert.That(Vector2.Distance(line.From, weaponAt), Is.LessThan(0.01f),
                    "A weapon the client knows is drawn from its live position, not the state's stale coordinates.");
                Assert.That(Vector2.Distance(line.To, northAim), Is.LessThan(0.01f));
            }
            Assert.That(north[0].Color, Is.EqualTo(WFInstrumentTheme.Accent));
            selection.Add(idle.NetEntity);
            Assert.That(preview.Refresh(), Has.Count.EqualTo(4), "Selecting the idle weapon adds its line.");

            // Which entity the radar tracks (the map, the weapon's grid or another grid) must not change a line.
            using var nav = new ShuttleNavControl { WfFitInstrument = true, SetSize = new Vector2(float.NaN), WorldRange = 40 };
            using var plot = new WFCockpitAimLines(nav, preview);
            nav.Measure(new Vector2(400));
            nav.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400)));
            foreach (var tracking in new[] { map, ToClient(MapData.Grid.Owner), ToClient(foreignGrid) })
            {
                nav.SetMatrix(new EntityCoordinates(tracking, Vector2.Zero), Angle.Zero);
                aim = new EntityCoordinates(map, northAim);
                Assert.That(plot.Lines(), Has.Count.EqualTo(4), "A plot tracking another entity draws every weapon with a clear line.");
                aim = new EntityCoordinates(map, eastAim);
                Assert.That(plot.Lines(), Has.Count.EqualTo(2), "The weapon's own wall blocks the same weapons whatever the plot tracks.");
            }
        });
    }

    private bool AimOverlayPresent() => Client.ResolveDependency<IOverlayManager>().HasOverlay<WFCockpitAimOverlay>();

    private sealed record CockpitSession(IUserInterfaceManager Ui, InGameScreen Screen, MainViewport Viewport, ChatBox Chat,
        ShuttleConsoleWindow Window, WFCockpitUIController Controller, WFCockpitView Hud);

    /// <summary>Spawns an unpowered helm and a seat; piloting and buckling are separate steps so they can come in any order.</summary>
    private async Task<(EntityUid Console, EntityUid Chair)> SpawnHelm()
    {
        EntityUid console = default;
        EntityUid chair = default;
        await Server.WaitAssertion(() =>
        {
            console = SEntMan.SpawnEntity("ComputerShuttle", MapData.GridCoords);
            chair = SEntMan.SpawnEntity("Chair", MapData.GridCoords);
            SEntMan.RemoveComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(console);
            SEntMan.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(console);
        });
        return (console, chair);
    }

    private async Task MakePilot(EntityUid console) => await Server.WaitAssertion(() =>
    {
        SEntMan.EnsureComponent<PilotComponent>(SPlayer);
        SEntMan.System<ShuttleConsoleSystem>().AddPilot(console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(console));
    });

    private async Task Buckle(EntityUid chair) => await Server.WaitAssertion(() =>
        Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True));

    /// <summary>Opens a helm window bound to the console, without going through the bound interface.</summary>
    private ShuttleConsoleWindow OpenHelm(EntityUid console)
    {
        var window = new ShuttleConsoleWindow();
        window.WfSetCockpitConsole(ToClient(SEntMan.GetNetEntity(console)));
        window.OpenCentered();
        return window;
    }

    /// <summary>Runs the body with the auto-enter option set, then puts back the player's own setting and HUD layout.</summary>
    private async Task WithAutoEnter(bool enabled, Func<Task> body)
    {
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalOption = settings.GetCVar(WolfgateCVars.CockpitAutoEnter);
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitAssertion(() =>
        {
            settings.SetCVar(WolfgateCVars.CockpitAutoEnter, enabled);
            settings.SetCVar(CCVars.UILayout, "Default");
        });
        await RunTicks(3);
        try
        {
            await body();
        }
        finally
        {
            await Client.WaitAssertion(() =>
            {
                settings.SetCVar(WolfgateCVars.CockpitAutoEnter, originalOption);
                settings.SetCVar(CCVars.UILayout, originalLayout);
            });
        }
    }

    /// <summary>Seats the player at a helm, enters the cockpit and runs the body, then restores the HUD and the settings it changed.</summary>
    private async Task InCockpit(Action<CockpitSession> body)
    {
        var (console, chair) = await SpawnHelm();
        await MakePilot(console);
        await Buckle(chair);
        await RunTicks(10);
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var originalLayout = settings.GetCVar(CCVars.UILayout);
        var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
        await Client.WaitAssertion(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var screen = (InGameScreen) ui.ActiveScreen!;
            var controller = ui.GetUIController<WFCockpitUIController>();
            var viewport = screen.GetWidget<MainViewport>()!;
            var chat = screen.ChatBox;
            var window = OpenHelm(console);
            try
            {
                Assert.That(controller.Enter(window), Is.True);
                body(new CockpitSession(ui, screen, viewport, chat, window, controller, screen.Children.OfType<WFCockpitView>().Single()));
            }
            finally
            {
                controller.Exit();
                window.Dispose();
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
                settings.SetCVar(CCVars.UILayout, originalLayout);
            }
        });
    }

    /// <summary>The panel in the side column grew, the one in the centre gave way to it, and nothing else moved.</summary>
    private static void AssertExpanded(WFCockpitView hud, MainViewport viewport, Vector2 size, bool swapped,
        Dictionary<string, UIBox2> before, string context)
    {
        var now = Snapshot(hud, viewport);
        var sideName = swapped ? "world" : "CockpitMfd";
        var centreName = swapped ? "CockpitMfd" : "world";
        var side = now[sideName];
        var centre = now[centreName];
        Assert.That(side.Width, Is.GreaterThan(before[sideName].Width + 1), $"{context}: the panel in the side column must grow");
        Assert.That(centre.Width, Is.LessThan(before[centreName].Width - 1), $"{context}: the panel in the centre must give way");
        Assert.That(centre.Width, Is.GreaterThanOrEqualTo(499), $"{context}: the centre keeps its room");
        Assert.That(side.Right, Is.EqualTo(size.X - 8).Within(1), $"{context}: the side column stays at the screen margin");
        if (size.X >= 1600)
            Assert.That(side.Width, Is.EqualTo(centre.Width).Within(1.5f), $"{context}: a wide window shares the width half and half");
        foreach (var name in new[] { "CockpitBrand", "CockpitInstruments", "CockpitTcas", "CockpitCamera", "CockpitFlight" })
            AssertBox(now[name], before[name], $"{context}: {name} must not move");
        Assert.That(now["CockpitTranslation"].Width, Is.EqualTo(centre.Width).Within(1), $"{context}: the motion gauges follow the centre column");
        AssertPanelsFitScreen(hud, viewport, size, context);
    }

    /// <summary>The caption and the three header toggles sit side by side inside the MFD header, each showing its whole label.</summary>
    private static void AssertMfdHeaderFits(WFCockpitView hud, IUserInterfaceManager ui, string context)
    {
        var swap = Named<Button>(hud, "CockpitMfdSwap");
        var expand = Named<Button>(hud, "CockpitMfdExpand");
        var maximise = Named<Button>(hud, "CockpitMfdMaximise");
        var row = swap.Parent!;
        var caption = row.Children.OfType<Label>().Single();
        foreach (var button in new[] { swap, expand, maximise })
        {
            Assert.That(button.VisibleInTree, Is.True, $"{context}: {button.Name}");
            AssertLabelFits(button, ui, $"{context}, {button.Name}");
        }
        Assert.That(caption.GlobalPosition.X + caption.DesiredSize.X, Is.LessThanOrEqualTo(swap.GlobalPosition.X + 1),
            $"{context}: the caption must not run under the first toggle");
        Assert.That(Right(swap), Is.LessThanOrEqualTo(expand.GlobalPosition.X + 1), $"{context}: Swap and Expand must not overlap");
        Assert.That(Right(expand), Is.LessThanOrEqualTo(maximise.GlobalPosition.X + 1), $"{context}: Expand and Maximise must not overlap");
        Assert.That(Right(maximise), Is.LessThanOrEqualTo(Right(row) + 1), $"{context}: the toggles must stay inside the MFD header");
    }

    private static void AssertFuelVisible(WFCockpitView hud)
    {
        var instruments = Named<Control>(hud, "CockpitInstruments");
        var fuel = Named<WFCockpitFuelBank>(hud, "CockpitFuelBank");
        var scroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        Assert.That(fuel.VisibleInTree, Is.True);
        Assert.That(Ancestors(fuel, hud).OfType<ScrollContainer>(), Is.Empty,
            "Fuel and FUEL LOW must remain fixed outside the dial scroll region.");
        Assert.That(Descendants(instruments).OfType<ScrollContainer>().Count(), Is.EqualTo(1));
        Assert.That(scroll.Height, Is.GreaterThan(0), "The fixed reserve strip must leave the flight dials accessible.");
        Assert.That(fuel.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(scroll) - 1));
        Assert.That(fuel.GlobalPosition.Y, Is.GreaterThanOrEqualTo(instruments.GlobalPosition.Y));
        Assert.That(Bottom(fuel), Is.LessThanOrEqualTo(Bottom(instruments) + 1),
            "The complete fuel gauge must stay inside the visible panel, including with FLIGHT/GUNS selection present.");
        Assert.That(Right(fuel), Is.LessThanOrEqualTo(Right(instruments) + 1));
    }

    private static void AssertFuelBinding(ShipScreen ship, WFCockpitView hud)
    {
        var gauge = Named<WFGlassGauge>(hud, "CockpitFuel");
        var lamp = Named<WFCockpitFuelLamp>(hud, "CockpitFuelLow");
        ship.ClearStatus();
        Assert.That(gauge.Reading.Value, Is.Null);
        Assert.That(lamp.LowFuel, Is.Null, "Missing ship telemetry must not look like an empty tank.");
        Assert.That(lamp.CriticalFuel, Is.Null);
        foreach (var (summary, expected, low, critical) in new (ShipFuelSummary, double?, bool?, bool?)[]
        {
            (default, null, null, null),
            (new() { Sources = 2, Fraction = 0 }, 0, true, true),
            (new() { Sources = 2, Fraction = 0.2f }, 20, true, false),
            (new() { Sources = 2, Fraction = 0.21f }, 21, false, false),
            (new() { Sources = 2, Fraction = 1 }, 100, false, false),
            (new() { Sources = 2, UnknownSources = 1, Fraction = 0 }, null, null, null),
        })
        {
            ship.UpdateStatus(new ShipStatusMessage(null, new List<ShipTileStatus>(), new ShipStatusSummary { Fuel = summary }));
            if (expected is { } value)
                Assert.That(gauge.Reading.Value, Is.EqualTo(value).Within(0.0001));
            else
                Assert.That(gauge.Reading.Value, Is.Null, "Absent or partially measured fuel sources must show NO SIGNAL.");
            Assert.That(lamp.LowFuel, Is.EqualTo(low));
            Assert.That(lamp.CriticalFuel, Is.EqualTo(critical));
        }
        ship.ClearStatus();
        Assert.That(gauge.Reading.Value, Is.Null, "Clearing the ship must discard its previous reserve.");
        Assert.That(lamp.LowFuel, Is.Null);
        Assert.That(lamp.CriticalFuel, Is.Null);
    }

    private static void AssertCockpitLayout(WFCockpitView hud, MainViewport viewport, ChatBox chat,
        IUserInterfaceManager ui, Vector2 size, string theme, string page)
    {
        var instruments = Named<Control>(hud, "CockpitInstruments");
        var flight = Named<Control>(hud, "CockpitFlight");
        var camera = Named<Control>(hud, "CockpitCamera");
        var mfd = Named<Control>(hud, "CockpitMfd");
        var comms = Named<Control>(hud, "CockpitComms");
        var shields = Named<Control>(hud, "CockpitShields");
        var translation = Named<Control>(hud, "CockpitTranslation");
        var selectors = Named<Control>(hud, "CockpitMfdSelectors");
        var arc = Named<FloatSpinBox>(hud, "CockpitShieldArc");
        var brand = Named<Control>(hud, "CockpitBrand");
        var logo = Named<TextureRect>(hud, "CockpitLogo");
        var exit = Named<Button>(hud, "CockpitExit");
        var maximised = hud.MfdMaximised;
        var swapped = hud.PanelsSwapped && !maximised;
        // The panel in the large centre area, and the one in the full-height column beside it (none while maximised).
        Control big = swapped || maximised ? mfd : viewport;
        Control? side = maximised ? null : swapped ? viewport : mfd;
        Assert.That(logo.VisibleInTree && logo.Texture != null, Is.True);
        Assert.That(logo.Width, Is.GreaterThan(60), "The wordmark must stay readable beside the exit button.");
        Assert.That(brand.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1),
            "The brand row belongs to the left column.");
        Assert.That(brand.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(brand.GlobalPosition.Y, Is.EqualTo(8).Within(1), "With no header row the brand row starts at the screen margin.");
        Assert.That(brand.Height, Is.LessThanOrEqualTo(34), "The brand row must stay slim.");
        Assert.That(instruments.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(brand)));
        Assert.That(exit.VisibleInTree, Is.True, "Exit Cockpit must always be reachable.");
        Assert.That(Right(logo), Is.LessThanOrEqualTo(exit.GlobalPosition.X + 1));
        Assert.That(exit.GlobalPosition.X, Is.GreaterThanOrEqualTo(brand.GlobalPosition.X));
        Assert.That(Right(exit), Is.LessThanOrEqualTo(Right(brand) + 1));
        Assert.That(Bottom(exit), Is.LessThanOrEqualTo(Bottom(brand) + 1));
        AssertLabelFits(exit, ui, $"{theme}, {size}, {page}");
        var tcas = Named<WFCockpitTcasPanel>(hud, "CockpitTcas");
        Assert.That(tcas.VisibleInTree, Is.True);
        Assert.That(tcas.Height, Is.EqualTo(108).Within(1));
        Assert.That(tcas.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1));
        Assert.That(tcas.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(tcas.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(instruments)));
        Assert.That(Bottom(tcas), Is.LessThanOrEqualTo(camera.GlobalPosition.Y));
        Assert.That(Ancestors(tcas, hud).OfType<ScrollContainer>(), Is.Empty,
            "TCAS lamps must never scroll with the instrument bank.");
        var speedLimit = Named<Control>(hud, "MaximumShuttleSpeedBox");
        Assert.That(Ancestors(speedLimit, hud), Does.Not.Contain(flight));
        Assert.That(Ancestors(speedLimit, hud), Does.Contain(mfd), "The speed limiter belongs on SYS.");
        Assert.That(speedLimit.VisibleInTree, Is.EqualTo(page == "wf-cockpit-systems"));
        if (page == "wf-cockpit-systems")
        {
            var ports = Named<GridContainer>(hud, "NetworkPortsBox");
            var portButtons = ports.Children.OfType<Button>().Where(button => button.VisibleInTree).ToArray();
            Assert.That(portButtons, Has.Length.EqualTo(8), "All wired auxiliary ports must remain available on SYS.");
            foreach (var button in portButtons)
            {
                Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(ports.GlobalPosition.X));
                Assert.That(Right(button), Is.LessThanOrEqualTo(Right(ports) + 1));
                Assert.That(Right(button), Is.LessThanOrEqualTo(Right(mfd) + 1));
                AssertLabelFits(button, ui, $"{theme}, {size}, SYS auxiliary port");
            }
            for (var index = 0; index < portButtons.Length; index += 2)
                Assert.That(Right(portButtons[index]), Is.LessThanOrEqualTo(portButtons[index + 1].GlobalPosition.X),
                    "The fixed-width auxiliary buttons must not overlap at compact MFD widths.");
        }
        Assert.That(flight.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1));
        Assert.That(flight.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(camera.VisibleInTree, Is.True, "Camera selection must stay available on every MFD page.");
        Assert.That(camera.GlobalPosition.X, Is.EqualTo(instruments.GlobalPosition.X).Within(1));
        Assert.That(camera.Width, Is.EqualTo(instruments.Width).Within(1));
        Assert.That(camera.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(instruments)));
        Assert.That(flight.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(camera)), "Camera controls belong directly above flight controls.");
        foreach (var button in Descendants(camera).OfType<Button>().Where(control => control.VisibleInTree))
        {
            Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(camera.GlobalPosition.Y));
            Assert.That(Bottom(button), Is.LessThanOrEqualTo(Bottom(camera)), "Camera buttons must fit in their permanent panel.");
            Assert.That(Right(button), Is.LessThanOrEqualTo(Right(camera)));
        }
        Assert.That(translation.GlobalPosition.Y, Is.EqualTo(8).Within(1), "With no header row the motion gauges start at the screen margin.");
        Assert.That(Bottom(translation), Is.LessThanOrEqualTo(big.GlobalPosition.Y));
        Assert.That(translation.GlobalPosition.X, Is.EqualTo(big.GlobalPosition.X).Within(1));
        if (side != null)
        {
            Assert.That(side.GlobalPosition.Y, Is.EqualTo(8).Within(1), "The side column must start at the screen margin.");
            Assert.That(Bottom(side), Is.EqualTo(size.Y - 8).Within(1), "The side column must use the full height beside the lower controls.");
        }
        else
            Assert.That(Bottom(mfd), Is.LessThanOrEqualTo(comms.GlobalPosition.Y), "A maximised MFD stops above the lower row.");
        Assert.That(flight.GlobalPosition.Y, Is.EqualTo(comms.GlobalPosition.Y).Within(1), "The lower control panels must align.");
        Assert.That(flight.Height, Is.EqualTo(comms.Height).Within(1));
        Assert.That(flight.Height, Is.EqualTo(shields.Height).Within(1));
        var status = Named<WFCockpitStatusLights>(hud, "CockpitStatus");
        Assert.That(status.VisibleInTree, Is.True);
        Assert.That(status.ChildCount, Is.EqualTo(6));
        Assert.That(status.GlobalPosition.Y, Is.GreaterThanOrEqualTo(flight.GlobalPosition.Y));
        Assert.That(Bottom(status), Is.LessThanOrEqualTo(Bottom(flight)));
        Assert.That(Ancestors(status, flight).OfType<ScrollContainer>(), Is.Empty,
            "Flight-status lamps must remain visible when flight controls need to scroll.");
        var heading = Named<WFHeadingInstrument>(hud, "CockpitHeading");
        var dialScroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        var headingHeight = hud.CompactDials ? WFCockpitInstrumentSizing.CompactHeight(176) : 176;
        var dialHeight = hud.CompactDials ? WFCockpitInstrumentSizing.CompactHeight(160) : 160;
        if (hud.CompactDials)
            Assert.That(dialScroll.Height, Is.LessThan(WFCockpitInstrumentSizing.FullColumnHeight + 8),
                $"{theme}, {size}: compact dials are only for columns too short to show the full-size ones.");
        else
            Assert.That(dialScroll.Height, Is.GreaterThanOrEqualTo(WFCockpitInstrumentSizing.FullColumnHeight - 8),
                $"{theme}, {size}: full-size dials need a column tall enough to show them whole.");
        Assert.That(heading.Height, Is.EqualTo(headingHeight).Within(1));
        Assert.That(Descendants(hud).Any(control => control.Name == "CockpitSpeedometer"), Is.False,
            "The velocity instrument already supplies speed; its duplicate strip must not displace fuel telemetry.");
        var hull = Named<WFGlassGauge>(hud, "CockpitHull");
        var fuel = Named<WFGlassGauge>(hud, "CockpitFuel");
        var fuelBank = Named<WFCockpitFuelBank>(hud, "CockpitFuelBank");
        var fuelLow = Named<WFCockpitFuelLamp>(hud, "CockpitFuelLow");
        Assert.That(hull.Strip && fuel.Strip, Is.True);
        Assert.That(fuel.VisibleInTree && fuelLow.VisibleInTree, Is.True);
        AssertFuelVisible(hud);
        var instrumentScroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        Assert.That(Ancestors(hull, instruments), Does.Contain(instrumentScroll));
        Assert.That(fuelBank.Width, Is.GreaterThanOrEqualTo(hull.Width - 1));
        Assert.That(fuel.Width, Is.GreaterThanOrEqualTo(104), "The fuel scale must remain readable at compact side-panel widths.");
        Assert.That(fuel.CompactStrip, Is.True);
        Assert.That(fuel.Height, Is.EqualTo(44).Within(1));
        foreach (var name in new[] { "CockpitForward", "CockpitLateral" })
        {
            var strip = Named<WFGlassGauge>(hud, name);
            Assert.That(strip.Strip && strip.CompactStrip, Is.True, name);
            Assert.That(strip.Height, Is.EqualTo(44).Within(1), name);
        }
        var coordinates = Named<WFCockpitCoordinates>(hud, "CockpitCoordinates");
        var forwardStrip = Named<WFGlassGauge>(hud, "CockpitForward");
        var lateralStrip = Named<WFGlassGauge>(hud, "CockpitLateral");
        Assert.That(coordinates.VisibleInTree, Is.True, $"{theme}, {size}: the coordinates box belongs to the motion row.");
        Assert.That(coordinates.Height, Is.EqualTo(44).Within(1));
        Assert.That(coordinates.Width, Is.GreaterThanOrEqualTo(87), $"{theme}, {size}: the coordinates box needs room for its figures.");
        Assert.That(coordinates.GlobalPosition.Y, Is.EqualTo(forwardStrip.GlobalPosition.Y).Within(1));
        Assert.That(coordinates.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(forwardStrip) - 1),
            $"{theme}, {size}: the coordinates box sits after the longitudinal strip.");
        Assert.That(Right(coordinates), Is.LessThanOrEqualTo(lateralStrip.GlobalPosition.X + 1),
            $"{theme}, {size}: the coordinates box sits before the lateral strip.");
        Assert.That(fuelLow.Height, Is.EqualTo(fuel.Height).Within(1));
        Assert.That(fuelLow.GlobalPosition.Y, Is.EqualTo(fuel.GlobalPosition.Y).Within(1));
        Assert.That(fuelLow.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(fuel)));
        Assert.That(Right(fuelLow), Is.LessThanOrEqualTo(Right(fuelBank) + 1));
        var velocity = Named<WFVelocityVectorInstrument>(hud, "CockpitVelocity");
        Assert.That(velocity.VisibleInTree, Is.True);
        var yaw = Named<WFGlassGauge>(hud, "CockpitYaw");
        Assert.That(yaw.GlobalPosition.X - Right(velocity), Is.GreaterThanOrEqualTo(11),
            "The velocity and turn-rate captions need a clear gap between their dial faces.");
        Assert.That(yaw.CaptionInset, Is.GreaterThanOrEqualTo(8));
        Assert.That(velocity.Height, Is.EqualTo(dialHeight).Within(1),
            "Digital and mechanical vector instruments must retain the same readable footprint.");
        Assert.That(velocity.Width, Is.GreaterThanOrEqualTo(88));
        Assert.That(velocity.GlobalPosition.X, Is.GreaterThanOrEqualTo(instruments.GlobalPosition.X));
        Assert.That(Right(velocity), Is.LessThanOrEqualTo(Right(instruments)));
        Assert.That(velocity.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(heading)));
        Assert.That(Bottom(velocity), Is.LessThanOrEqualTo(Bottom(velocity.Parent!)),
            "The vector dial must fit the original speed/yaw row even when the instrument bank needs to scroll.");
        foreach (var gauge in Descendants(instruments).OfType<WFGlassGauge>().Where(gauge => !gauge.Strip))
            Assert.That(gauge.Height, Is.EqualTo(dialHeight).Within(1),
                "Both instrument themes need the same readable full-size face.");
        Assert.That(comms.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(big)));
        Assert.That(shields.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(big)));
        Assert.That(shields.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(comms)));
        Assert.That(Right(shields), Is.LessThanOrEqualTo(side?.GlobalPosition.X ?? size.X - 8 + 1));
        if (maximised)
            Assert.That(Right(shields), Is.EqualTo(size.X - 8).Within(1), "With the world view gone the lower row spans the whole deck.");
        else
        {
            Assert.That(viewport.Width, Is.GreaterThan(300));
            Assert.That(viewport.Height, Is.GreaterThan(200));
        }
        Assert.That(viewport.VisibleInTree, Is.EqualTo(!maximised), "Only a maximised MFD may hide the world view.");
        var speechClip = Named<WFCockpitSpeechClip>(hud, "CockpitSpeechClip");
        var speechRoot = Named<Control>(hud, "CockpitSpeechBubbles");
        Assert.That(speechClip.RectClipContent, Is.True, "Speech bubbles must be cut off at the world view's edge.");
        if (maximised)
        {
            Assert.That(speechClip.Width, Is.EqualTo(0).Within(1), "Speech bubbles have no world view to appear over.");
            Assert.That(speechClip.Height, Is.EqualTo(0).Within(1));
        }
        else
        {
            Assert.That(speechClip.GlobalPosition.X, Is.EqualTo(viewport.GlobalPosition.X).Within(1));
            Assert.That(speechClip.GlobalPosition.Y, Is.EqualTo(viewport.GlobalPosition.Y).Within(1));
            Assert.That(speechClip.Width, Is.EqualTo(viewport.Width).Within(1));
            Assert.That(speechClip.Height, Is.EqualTo(viewport.Height).Within(1));
        }
        Assert.That(speechRoot.GlobalPosition.X, Is.EqualTo(0).Within(1), "Bubble coordinates are in screen space.");
        Assert.That(speechRoot.GlobalPosition.Y, Is.EqualTo(0).Within(1), "Bubble coordinates are in screen space.");
        Assert.That(speechRoot.Width, Is.EqualTo(size.X).Within(1));
        Assert.That(speechRoot.Height, Is.EqualTo(size.Y).Within(1));
        Assert.That(chat.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(big)));
        Assert.That(Right(chat), Is.LessThanOrEqualTo(size.X));
        var lowerRow = Right(shields) - comms.GlobalPosition.X - 6;
        Assert.That(comms.Width + shields.Width, Is.EqualTo(lowerRow).Within(1), "Communications and shields fill the lower row.");
        Assert.That(comms.Width, Is.GreaterThanOrEqualTo(lowerRow * 0.43f - 1), "Communications never get less than they had.");
        Assert.That(shields.Width, Is.LessThanOrEqualTo(lowerRow * 0.57f + 1), "The shield panel never takes more than it had.");
        var shieldContent = Descendants(shields).OfType<WFShipShieldShuntScreen>().Single();
        if (lowerRow >= 700)
        {
            Assert.That(shields.Width, Is.GreaterThanOrEqualTo(shieldContent.DesiredSize.X + 24 - 1),
                $"{theme}, {size}, {page}: the shield panel must be wide enough for every shield control.");
            Assert.That(shields.Width, Is.LessThanOrEqualTo(shieldContent.DesiredSize.X + 24 + 20),
                $"{theme}, {size}, {page}: the shield panel must be no wider than its controls need.");
            Assert.That(comms.Width, Is.GreaterThan(shields.Width), $"{theme}, {size}, {page}: communications must be the larger panel.");
        }
        foreach (var part in Descendants(shields).Where(control => control.VisibleInTree && control is Slider or FloatSpinBox or Button))
            Assert.That(Right(part), Is.LessThanOrEqualTo(Right(shields) - 6),
                $"{theme}, {size}, {page}: {part.Name ?? part.GetType().Name} must not be clipped by the shield panel.");
        foreach (var shieldButton in Descendants(shields).OfType<Button>().Where(control => control.VisibleInTree))
            AssertLabelFits(shieldButton, ui, $"{theme}, {size}, {page}, shield control");
        Assert.That(arc.VisibleInTree, Is.True, "Shield arc width must remain available on every MFD page.");
        Assert.That(arc.GlobalPosition.Y, Is.GreaterThanOrEqualTo(shields.GlobalPosition.Y));
        Assert.That(Bottom(arc), Is.LessThanOrEqualTo(Bottom(shields)));
        Assert.That(Descendants(shields).OfType<Slider>().Single().Width, Is.GreaterThan(100),
            "The permanent shield power slider must remain usable.");
        var buttons = selectors.Children.OfType<Button>().ToArray();
        Assert.That(buttons, Has.Length.EqualTo(8));
        foreach (var button in buttons)
        {
            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(mfd.GlobalPosition.X));
            Assert.That(Right(button), Is.LessThanOrEqualTo(Right(mfd)), "All MFD contexts must fit in one selector row.");
            Assert.That(button.ToolTip, Is.Not.Null.And.Not.Empty, "Abbreviated selectors need their complete names on hover.");
            AssertLabelFits(button, ui, $"{theme}, {size}, {page}");
        }
        foreach (var scroll in Descendants(mfd).OfType<ScrollContainer>().Where(control => control.VisibleInTree))
            Assert.That(Ancestors(scroll, mfd).OfType<ScrollContainer>(), Is.Empty,
                "An MFD list must not scroll inside another scrolling panel.");
        foreach (var plot in Descendants(mfd).OfType<MapGridControl>().Where(control => control.VisibleInTree))
        {
            Assert.That(Ancestors(plot, mfd).OfType<ScrollContainer>(), Is.Empty,
                "Plots must stay fixed when their separate details are scrolled.");
            var context = $"{theme}, {size.X}x{size.Y}, {page}: {plot.GetType().Name} ({plot.Name})";
            var containers = string.Join(" <- ", Ancestors(plot, mfd)
                .Select(control => $"{control.GetType().Name} ({control.Name}) {control.Size}"));
            Assert.That(plot.Height, Is.GreaterThan(100), $"{context} needs a usable plot. Containers: {containers}");
            // A short MFD (swapped or maximised on the smallest window) has no room for half its height of plot beside the
            // plot's frame and controls; it keeps the usable-plot floor above and lets the details scroll instead.
            if (page == "wf-cockpit-dock" && Descendants(mfd).OfType<WFCockpitMfdSplit>().First(split => split.VisibleInTree).Height >= WFCockpitMfdSplit.ShortHeight)
                Assert.That(plot.Height, Is.GreaterThanOrEqualTo((Bottom(mfd) - Bottom(selectors)) * 0.5f),
                    $"{context}: the approach plot must occupy most of the MFD's usable height. Containers: {containers}");
        }
    }

    /// <summary>Exercises the visible native thumb rather than only setting a scroll offset programmatically.</summary>
    private static void AssertInstrumentScrollInput(WFCockpitView hud, IUserInterfaceManager ui, Vector2 size, string theme)
    {
        var scroll = Named<ScrollContainer>(hud, "CockpitInstrumentScroll");
        scroll.SetScrollValue(Vector2.Zero);
        Layout(hud, size);
        var bar = scroll.Children.OfType<VScrollBar>().Single();
        var content = scroll.Children.Single(child => child is not ScrollBar);
        var overflow = content.DesiredSize.Y - scroll.Height;
        var context = $"{theme}, {size}: content {content.DesiredSize.Y}, viewport {scroll.Height}, range {bar.MaxValue - bar.Page}";
        Assert.That(bar.Visible, Is.EqualTo(overflow > 0.001f), context);
        Assert.That(scroll.Children.OfType<HScrollBar>().Single().Visible, Is.False, context);
        if (size.Y >= 1080)
            Assert.That(bar.Visible, Is.False, $"{context}: fitting instruments must not retain an immovable scrollbar.");
        if (!bar.Visible)
        {
            Assert.That(scroll.GetScrollValue(), Is.EqualTo(Vector2.Zero), context);
            return;
        }
        Assert.That(bar.MaxValue - bar.Page, Is.GreaterThan(1), context);
        var grabber = (UIBox2) typeof(ScrollBar).GetMethod("_getGrabberBox", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, null)!;
        var start = grabber.TopLeft + new Vector2(grabber.Width / 2, Math.Min(2, grabber.Height / 2));
        var pointer = new ScreenCoordinates(bar.GlobalPixelPosition + start, bar.Window!.Id);
        Assert.That(ui.MouseGetControl(pointer), Is.SameAs(bar), $"{context}: the visible thumb must receive pointer input.");
        var down = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true,
            start / bar.UIScale, start);
        typeof(ScrollBar).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { down });
        Assert.That(down.Handled, Is.True, $"{context}: the thumb must accept a drag.");
        var before = content.GlobalPosition.Y;
        var delta = new Vector2(0, Math.Max(8, bar.PixelHeight / 4f));
        var end = start + delta;
        var endPointer = new ScreenCoordinates(bar.GlobalPixelPosition + end, bar.Window.Id);
        var move = new GUIMouseMoveEventArgs(delta / bar.UIScale, bar,
            bar.GlobalPosition + end / bar.UIScale, endPointer, end / bar.UIScale, end);
        typeof(ScrollBar).GetMethod("MouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { move });
        var up = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, endPointer, true,
            end / bar.UIScale, end);
        typeof(ScrollBar).GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, new object[] { up });
        Layout(hud, size);
        Assert.That(scroll.VScroll, Is.GreaterThan(0), $"{context}: dragging must advance the native range.");
        Assert.That(content.GlobalPosition.Y, Is.LessThan(before), $"{context}: dragging must move the dial column.");
        scroll.SetScrollValue(Vector2.Zero);
        Layout(hud, size);
    }

    private enum Arrangement
    {
        Normal,
        Swapped,
        Maximised,
        MaximisedFromSwapped,
    }

    private static readonly string[] PanelNames =
    {
        "CockpitBrand", "CockpitInstruments", "CockpitTcas", "CockpitCamera", "CockpitFlight", "CockpitGunneryModes",
        "CockpitGunnery", "CockpitTranslation", "CockpitMfd", "CockpitComms", "CockpitShields",
    };

    /// <summary>Presses the panel buttons to reach an arrangement; Swap Panel is off while maximised, so that is undone first.</summary>
    private static void SetArrangement(WFCockpitView hud, Arrangement arrangement)
    {
        var swap = Named<Button>(hud, "CockpitMfdSwap");
        var maximise = Named<Button>(hud, "CockpitMfdMaximise");
        Toggle(maximise, false);
        Toggle(swap, arrangement is Arrangement.Swapped or Arrangement.MaximisedFromSwapped);
        Toggle(maximise, arrangement is Arrangement.Maximised or Arrangement.MaximisedFromSwapped);
    }

    private static UIBox2 Box(Control control) => UIBox2.FromDimensions(control.GlobalPosition, control.Size);

    /// <summary>Where every visible large panel sits right now, keyed by name; the world view is "world".</summary>
    private static Dictionary<string, UIBox2> Snapshot(WFCockpitView hud, MainViewport viewport)
    {
        var boxes = new Dictionary<string, UIBox2>();
        if (viewport.VisibleInTree)
            boxes["world"] = Box(viewport);
        foreach (var name in PanelNames)
        {
            var panel = Descendants(hud).SingleOrDefault(control => control.Name == name);
            if (panel is { VisibleInTree: true })
                boxes[name] = Box(panel);
        }
        return boxes;
    }

    private static void AssertPoint(Vector2 actual, Vector2 expected, string context)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(1), $"{context}: x");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(1), $"{context}: y");
    }

    private static void AssertBox(UIBox2 actual, UIBox2 expected, string context)
    {
        AssertPoint(actual.TopLeft, expected.TopLeft, $"{context}, top left");
        AssertPoint(actual.BottomRight, expected.BottomRight, $"{context}, bottom right");
    }

    private static void AssertSameBoxes(Dictionary<string, UIBox2> actual, Dictionary<string, UIBox2> expected, string context)
    {
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys), context);
        foreach (var (name, box) in expected)
            AssertBox(actual[name], box, $"{context}, {name}");
    }

    /// <summary>Every visible large panel lies inside the screen and none overlaps another.</summary>
    private static void AssertPanelsFitScreen(WFCockpitView hud, MainViewport viewport, Vector2 size, string context)
    {
        var panels = Snapshot(hud, viewport);
        foreach (var (name, box) in panels)
        {
            Assert.That(box.Left, Is.GreaterThanOrEqualTo(-1), $"{context}: {name} must not leave the screen on the left.");
            Assert.That(box.Top, Is.GreaterThanOrEqualTo(-1), $"{context}: {name} must not leave the screen at the top.");
            Assert.That(box.Right, Is.LessThanOrEqualTo(size.X + 1), $"{context}: {name} must not leave the screen on the right.");
            Assert.That(box.Bottom, Is.LessThanOrEqualTo(size.Y + 1), $"{context}: {name} must not leave the screen at the bottom.");
            Assert.That(box.Width, Is.GreaterThan(0), $"{context}: {name} must keep some width.");
            Assert.That(box.Height, Is.GreaterThan(0), $"{context}: {name} must keep some height.");
        }
        var names = panels.Keys.ToArray();
        for (var i = 0; i < names.Length; i++)
        for (var j = i + 1; j < names.Length; j++)
        {
            var a = panels[names[i]];
            var b = panels[names[j]];
            var overlap = a.Left < b.Right - 1 && b.Left < a.Right - 1 && a.Top < b.Bottom - 1 && b.Top < a.Bottom - 1;
            Assert.That(overlap, Is.False, $"{context}: {names[i]} {a} and {names[j]} {b} must not overlap.");
        }
    }

    /// <summary>Checks which panels moved for an arrangement, and where a maximised MFD docks the alert bank and votes.</summary>
    private static void AssertArrangement(WFCockpitView hud, MainViewport viewport, AlertsUI alerts, BoxContainer votes, Vector2 size,
        Arrangement arrangement, Dictionary<string, UIBox2> normal, string context)
    {
        var now = Snapshot(hud, viewport);
        var swapped = arrangement is Arrangement.Swapped or Arrangement.MaximisedFromSwapped;
        var maximised = arrangement is Arrangement.Maximised or Arrangement.MaximisedFromSwapped;
        var swap = Named<Button>(hud, "CockpitMfdSwap");
        var maximise = Named<Button>(hud, "CockpitMfdMaximise");
        var mfd = Named<Control>(hud, "CockpitMfd");
        var strip = Named<ScrollContainer>(hud, "CockpitCrewAlerts");
        Assert.That(hud.PanelsSwapped, Is.EqualTo(swapped), context);
        Assert.That(hud.MfdMaximised, Is.EqualTo(maximised), context);
        Assert.That(swap.Pressed, Is.EqualTo(swapped), context);
        Assert.That(swap.Disabled, Is.EqualTo(maximised), $"{context}: Swap Panel does nothing while the MFD is maximised.");
        Assert.That(Named<Button>(hud, "CockpitMfdExpand").Disabled, Is.EqualTo(maximised),
            $"{context}: Expand does nothing while the MFD is maximised, and is on offer otherwise in a window this wide.");
        Assert.That(maximise.Pressed, Is.EqualTo(maximised), context);
        Assert.That(maximise.Text, Is.EqualTo(Loc.GetString(maximised ? "wf-cockpit-restore" : "wf-cockpit-maximise")), context);
        Assert.That(swap.ToolTip, Is.Not.Null.And.Not.Empty, context);
        Assert.That(maximise.ToolTip, Is.Not.Null.And.Not.Empty, context);
        Assert.That(viewport.Visible, Is.EqualTo(!maximised), $"{context}: only a maximised MFD may stop drawing the world view.");
        foreach (var name in new[] { "CockpitBrand", "CockpitInstruments", "CockpitTcas", "CockpitCamera", "CockpitFlight" })
            AssertBox(now[name], normal[name], $"{context}: {name} must not move");
        AssertPoint(now["CockpitTranslation"].TopLeft, normal["CockpitTranslation"].TopLeft, $"{context}: the motion gauges must not move");
        AssertPoint(now["CockpitComms"].TopLeft, normal["CockpitComms"].TopLeft, $"{context}: communications must not move");
        if (!maximised)
        {
            AssertBox(now["CockpitTranslation"], normal["CockpitTranslation"], $"{context}: the motion gauges must not move");
            AssertBox(now["CockpitComms"], normal["CockpitComms"], $"{context}: communications must not move");
            AssertBox(now["CockpitShields"], normal["CockpitShields"], $"{context}: shields must not move");
            AssertBox(now["CockpitMfd"], swapped ? normal["world"] : normal["CockpitMfd"], $"{context}: MFD");
            AssertBox(now["world"], swapped ? normal["CockpitMfd"] : normal["world"], $"{context}: world view");
            var bank = Box(strip);
            Assert.That(bank.Left, Is.GreaterThanOrEqualTo(now["world"].Left - 1), $"{context}: the alert bank rides on the world view");
            Assert.That(bank.Right, Is.LessThanOrEqualTo(now["world"].Right + 1), context);
            Assert.That(bank.Top, Is.GreaterThanOrEqualTo(now["world"].Top - 1), context);
            Assert.That(bank.Bottom, Is.LessThanOrEqualTo(now["world"].Bottom + 1), context);
            Assert.That(votes.VisibleInTree, Is.True, context);
            return;
        }

        var box = now["CockpitMfd"];
        var world = normal["world"];
        AssertPoint(box.TopLeft, world.TopLeft, $"{context}: the MFD must start where the world view started");
        Assert.That(box.Bottom, Is.EqualTo(world.Bottom).Within(1), $"{context}: the MFD must end where the world view ended");
        Assert.That(box.Width, Is.GreaterThan(world.Width), $"{context}: the MFD must take the world view's width and more");
        Assert.That(box.Width, Is.GreaterThan(normal["CockpitMfd"].Width), $"{context}: the MFD must take the side column's width and more");
        Assert.That(box.Right, Is.LessThanOrEqualTo(size.X - 8 + 1), context);
        Assert.That(now["CockpitShields"].Right, Is.EqualTo(size.X - 8).Within(1), $"{context}: the lower row spans the deck");
        Assert.That(now["CockpitComms"].Width, Is.GreaterThan(normal["CockpitComms"].Width), $"{context}: communications use the extra width");
        var bankBox = Box(strip);
        Assert.That(strip.VisibleInTree, Is.True, $"{context}: crew alerts must stay visible with the world view gone");
        Assert.That(bankBox.Width, Is.GreaterThan(0), context);
        Assert.That(bankBox.Height, Is.GreaterThan(0), context);
        Assert.That(bankBox.Left, Is.GreaterThanOrEqualTo(box.Right - 1), $"{context}: the alert bank docks beside the MFD, not over it");
        Assert.That(bankBox.Right, Is.LessThanOrEqualTo(size.X - 8 + 1), context);
        Assert.That(bankBox.Top, Is.GreaterThanOrEqualTo(8 - 1), context);
        Assert.That(bankBox.Bottom, Is.LessThanOrEqualTo(now["CockpitComms"].Top + 1), context);
        var alert = alerts.AlertContainer.Children.OfType<AlertControl>().First();
        Assert.That(alert.VisibleInTree, Is.True, context);
        Assert.That(alert.Height, Is.GreaterThan(0), $"{context}: a docked alert must be drawn and clickable");
        Assert.That(alert.GlobalPosition.X, Is.GreaterThanOrEqualTo(bankBox.Left - 1), context);
        Assert.That(Right(alert), Is.LessThanOrEqualTo(bankBox.Right + 1), context);
        Assert.That(alert.GlobalPosition.Y, Is.GreaterThanOrEqualTo(bankBox.Top - 1), context);
        Assert.That(Bottom(alert), Is.LessThanOrEqualTo(bankBox.Bottom + 1), context);
        Assert.That(votes.VisibleInTree, Is.True, context);

        using var popup = new Control { MinSize = new Vector2(360, 90) };
        votes.AddChild(popup);
        Layout(hud, size);
        var docked = Box(popup);
        var shifted = Box(mfd);
        Assert.That(popup.VisibleInTree, Is.True, $"{context}: a vote must stay visible with the world view gone");
        Assert.That(docked.Height, Is.GreaterThanOrEqualTo(89), context);
        Assert.That(docked.Left, Is.GreaterThanOrEqualTo(-1), context);
        Assert.That(docked.Right, Is.LessThanOrEqualTo(size.X + 1), context);
        Assert.That(docked.Top, Is.GreaterThanOrEqualTo(Bottom(Named<Control>(hud, "CockpitTranslation")) - 1), context);
        Assert.That(docked.Bottom, Is.LessThanOrEqualTo(shifted.Top + 1), $"{context}: votes dock above the MFD instead of over its controls");
        Assert.That(shifted.Top, Is.GreaterThan(box.Top + 80), $"{context}: the MFD gives up height for the vote");
        Assert.That(shifted.Bottom, Is.EqualTo(box.Bottom).Within(1), context);
        votes.RemoveChild(popup);
        Layout(hud, size);
        AssertBox(Box(mfd), box, $"{context}: the MFD must take its room back when the vote ends");
    }

    /// <summary>The access page keeps its door diagram the largest panel, beside the controls when there is room.</summary>
    private static void AssertAccessPage(WFCockpitView hud, IUserInterfaceManager ui, string context)
    {
        foreach (var notice in Descendants(hud).OfType<WFWrappedLabel>().Where(label => label.VisibleInTree && !string.IsNullOrEmpty(label.Text)))
            ShipAccessTest.AssertShownInFull(notice, ui, $"{context}, {notice.Name}");
        var accessLayout = Named<Control>(hud, "WfAccessLayout");
        var accessPlot = Named<Control>(hud, "WfAccessPlot");
        var accessDetails = Named<Control>(hud, "WfAccessDetails");
        var doorMap = Named<MapGridControl>(hud, "DoorMap");
        Assert.That(Ancestors(doorMap, accessPlot).OfType<WFScreenBezel>(), Is.Not.Empty,
            $"{context}: the access diagram must retain the same instrument bezel as the other plots in both themes.");
        Assert.That(Right(accessPlot), Is.LessThanOrEqualTo(Right(accessLayout) + 1), context);
        Assert.That(Bottom(accessPlot), Is.LessThanOrEqualTo(Bottom(accessLayout) + 1), context);
        if (accessLayout.Width >= WFShipAccessLayout.WideMinWidth && accessLayout.Height >= WFShipAccessLayout.WideMinHeight)
        {
            Assert.That(accessDetails.Visible, Is.False, $"{context}: a wide access page replaces its details scroller with columns.");
            foreach (var panel in accessLayout.Children.Where(child => child.Visible && child != accessPlot))
            {
                Assert.That(panel.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(accessPlot) - 1),
                    $"{context}: a wide access MFD must use its width for controls beside the diagram.");
                Assert.That(accessPlot.Width, Is.GreaterThan(panel.Width),
                    $"{context}: the door diagram must be the largest panel in a wide access MFD.");
            }
        }
        else if (accessLayout.Width >= 680)
        {
            Assert.That(accessDetails.GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(accessPlot)),
                $"{context}: a wide access MFD must use its width for controls beside the diagram.");
            Assert.That(accessPlot.Width, Is.GreaterThan(accessDetails.Width),
                $"{context}: the door diagram must be the largest panel in a wide access MFD.");
        }
        else
        {
            Assert.That(accessDetails.GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(accessPlot)),
                $"{context}: compact access MFDs must keep their controls below the fixed diagram.");
            Assert.That(accessPlot.Height, Is.GreaterThan(accessDetails.Height * 1.4f),
                $"{context}: compact access MFDs must give the door diagram most of their height.");
        }
    }

    /// <summary>The ship page puts its details beside the hull plot when wide and stacks them below when narrow.</summary>
    private static void AssertShipPage(WFCockpitView hud, IUserInterfaceManager ui, string context)
    {
        var hullLayout = Named<WFCockpitShipLayout>(hud, "CockpitShipLayout");
        var hullPlot = hullLayout.Children.First();
        var hullDetails = hullLayout.Children.Last();
        context = $"{context}, ship details={hullDetails.Size}";
        if (hullLayout.Width >= 680)
        {
            Assert.That(hullDetails.Position.X, Is.GreaterThanOrEqualTo(hullPlot.Position.X + hullPlot.Width),
                $"{context}: a wide ship MFD must use its width for details beside the plot.");
            Assert.That(hullPlot.Width / (hullPlot.Width + hullDetails.Width), Is.InRange(0.45f, 0.55f),
                $"{context}: wide ship MFDs must give the hull plot about half of their width.");
        }
        else
        {
            Assert.That(hullDetails.Position.Y, Is.GreaterThanOrEqualTo(hullPlot.Position.Y + hullPlot.Height),
                $"{context}: compact ship MFDs must stack details below the readable hull plot.");
        }
        var telemetry = Named<GridContainer>(hud, "CockpitShipTelemetry");
        Assert.That(telemetry.Columns, Is.EqualTo(2), context);
        var gauges = telemetry.Children.OfType<WFGlassGauge>().ToArray();
        Assert.That(gauges, Has.Length.EqualTo(6), context);
        foreach (var gauge in gauges)
        {
            Assert.That(gauge.GlobalPosition.X, Is.GreaterThanOrEqualTo(hullDetails.GlobalPosition.X - 1), context);
            Assert.That(Right(gauge), Is.LessThanOrEqualTo(Right(hullDetails) + 1), context);
        }
        for (var index = 0; index < gauges.Length; index += 2)
        {
            Assert.That(gauges[index + 1].GlobalPosition.Y, Is.EqualTo(gauges[index].GlobalPosition.Y).Within(1), context);
            Assert.That(gauges[index + 1].GlobalPosition.X, Is.GreaterThanOrEqualTo(Right(gauges[index]) - 1), context);
            if (index > 0)
                Assert.That(gauges[index].GlobalPosition.Y, Is.GreaterThanOrEqualTo(Bottom(gauges[index - 2]) - 1), context);
        }
        var overlays = Named<GridContainer>(hud, "CockpitShipOverlays");
        Assert.That(overlays.Columns, Is.EqualTo(4), context);
        Assert.That(overlays.Children.Select(control => control.Name),
            Is.EquivalentTo(new[] { "DamageToggle", "FireToggle", "PressureToggle", "PowerToggle" }), context);
        var mapControls = Named<Control>(hud, "CockpitShipMapControls");
        Assert.That(mapControls.Children.Select(control => control.Name),
            Is.EquivalentTo(new[] { "DepartmentToggle", "FitButton" }), context);
        foreach (var row in new Control[] { overlays, mapControls })
        {
            var controls = row.Children.ToArray();
            foreach (var control in controls)
            {
                Assert.That(control.VisibleInTree, Is.True, context);
                Assert.That(control.GlobalPosition.Y, Is.EqualTo(controls[0].GlobalPosition.Y).Within(1), context);
                Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(hullDetails.GlobalPosition.X - 1), context);
                Assert.That(Right(control), Is.LessThanOrEqualTo(Right(hullDetails) + 1), context);
                Assert.That(Bottom(control), Is.LessThanOrEqualTo(Bottom(row) + 1), context);
                if (control is CheckBox check)
                    AssertLabelFits(check.Label, ui, context);
                else
                    AssertLabelFits((Button) control, ui, context);
            }
        }
    }

    /// <summary>At 1920x1080 and UI scale 1 no scroll bar may appear on the dials, flight controls, shield panel or the MFD pages meant to fit.</summary>
    private static void AssertFitsWithoutScrolling(WFCockpitView hud, Vector2 size, string theme)
    {
        foreach (var name in new[] { "CockpitInstruments", "CockpitFlight", "CockpitShields" })
            AssertNoScrollBars(Named<Control>(hud, name), $"{theme}, {size}, {name}");
        foreach (var page in new[] { "wf-cockpit-systems", "wf-cockpit-shield-page" })
        {
            hud.SelectPage(page);
            Layout(hud, size);
            AssertNoScrollBars(Named<Control>(hud, "CockpitMfd"), $"{theme}, {size}, {page}");
        }
        hud.SelectPage("wf-cockpit-nav");
        Layout(hud, size);
    }

    private static void AssertNoScrollBars(Control panel, string context)
    {
        foreach (var scroll in Descendants(panel).OfType<ScrollContainer>().Where(scroll => scroll.VisibleInTree))
        {
            foreach (var bar in scroll.Children.OfType<ScrollBar>())
                Assert.That(bar.Visible, Is.False, $"{context}: the scroll bar must not appear when everything fits.");
        }
    }

    private static void Layout(Control root, Vector2 size)
    {
        // Visibility queues parent layout changes; this synchronous sweep cannot wait for the next UI frame.
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    private static void AssertLabelFits(Button button, IUserInterfaceManager ui, string context) =>
        AssertLabelFits(button.Label, ui, context);

    private static void AssertLabelFits(Label label, IUserInterfaceManager ui, string context)
    {
        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : ui.ThemeDefaults.LabelFont);
        var width = 0f;
        foreach (var rune in label.Text!.EnumerateRunes())
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
        Assert.That(label.PixelSize.X + 1, Is.GreaterThanOrEqualTo(width),
            $"{context}: the {label.Text} button must display its complete label.");
    }

    private static void Toggle(BaseButton button, bool pressed) => WFButtonTestInput.Toggle(button, pressed);

    private static T Named<T>(Control root, string name) where T : Control =>
        Descendants(root).OfType<T>().Single(control => control.Name == name);

    private static float Bottom(Control control) => control.GlobalPosition.Y + control.Height;

    private static float Right(Control control) => control.GlobalPosition.X + control.Width;

    private static IEnumerable<Control> Ancestors(Control control, Control boundary)
    {
        for (var parent = control.Parent; parent != null && parent != boundary; parent = parent.Parent)
            yield return parent;
    }

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
