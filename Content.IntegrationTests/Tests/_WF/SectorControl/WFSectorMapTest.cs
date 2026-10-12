#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._WF.Encounters;
using Content.Client._WF.SectorControl;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>The sector map: the client keeps the sector map with nothing held, outlines and names every cell, marks the grids a MAP scan would as blips with hover cards, and the window lays out and fits them.</summary>
[TestOf(typeof(WFSectorMapControl))]
public sealed class WFSectorMapTest : WFSectorTestBase
{
    private static readonly WFSectorCell Held = new(4, -2);
    private static readonly WFSectorCell Open = new(-6, 3);
    private static readonly Vector2 Outpost = new(4000f, 1500f);
    private static readonly Vector2 Depot = new(-9000f, 6000f);

    private void SpawnStations()
    {
        SpawnStation(Outpost, "Test Outpost");
        SpawnStation(Depot, "Test Depot");
    }

    /// <summary>A player is sent the sector map with nothing held and keeps it, every cell in the radius outlined once and named; grids come from the client, as the MAP tab lists them.</summary>
    [Test]
    public async Task SectorMapNamesTheGridsTheMapTabWould()
    {
        await StartRound();
        var hidden = new List<NetEntity>();
        await Server.WaitAssertion(() =>
        {
            var shuttles = Server.System<SharedShuttleSystem>();
            var outpost = SpawnStation(Outpost, "Test Outpost");
            SEntMan.EnsureComponent<IFFComponent>(outpost);
            SpawnStation(Depot, "Test Depot");
            var flags = new[] { IFFFlags.Hide, IFFFlags.HideLabel, IFFFlags.HideLabelAlways };
            for (var i = 0; i < flags.Length; i++)
            {
                var grid = SpawnStation(new Vector2(2000f + 1000f * i, -3000f), $"Test Hidden {flags[i]}");
                shuttles.AddIFFFlag(grid, flags[i]);
                hidden.Add(SEntMan.GetNetEntity(grid));
            }

            var sync = Server.System<WFSectorSyncSystem>();
            var ev = sync.Build(MapData.MapId);
            Assert.That(ev.Claims, Is.Empty);
            Assert.That(ev.Sector, Is.True, "The round's map is the sector map.");
            sync.SendTo(ServerSession);
        });

        await RunTicks(30);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out var view), Is.True, "The sector map is kept with nothing held.");
            Assert.That(view.Sector, Is.True);
            Assert.That(view.Owners, Is.Empty);
            Assert.That(view.Extent, Is.Null, "Nothing held or contested.");

            // Every cell inside the radius, each edge once.
            var cells = WFSectorHex.CellsWithin(WFSectorClientSystem.SectorRadius, view.CellSize);
            var edges = new HashSet<(int, int)>();
            foreach (var cell in cells)
            {
                for (var i = 0; i < 6; i++)
                {
                    edges.Add(Key((WFSectorHex.Corner(cell, view.CellSize, i) + WFSectorHex.Corner(cell, view.CellSize, i + 1)) / 2f));
                }
            }

            Assert.That(view.Grid.Length, Is.EqualTo(edges.Count * 2), "Each shared edge is drawn once.");
            var drawn = new HashSet<(int, int)>();
            for (var i = 0; i < view.Grid.Length; i += 2)
            {
                drawn.Add(Key((view.Grid[i] + view.Grid[i + 1]) / 2f));
            }

            Assert.That(drawn.SetEquals(edges), Is.True, "The outline covers every edge of every cell.");
            Assert.That(view.CellLabels, Has.Count.EqualTo(cells.Count), "Every cell is named, held or not.");
            var open = view.CellLabels.Single(label => label.Text == WFSectorHex.Callsign(Open));
            Assert.That(open.Color, Is.EqualTo(WFSectorClientSystem.OpenCellColor));
            Assert.That(open.Centre, Is.EqualTo(CentreOf(Open)));

            // The hidden grids reached the client; the map leaves them out.
            foreach (var net in hidden)
            {
                Assert.That(CEntMan.GetComponent<IFFComponent>(CEntMan.GetEntity(net)).Flags, Is.Not.EqualTo(IFFFlags.None));
            }

            var map = new WFSectorMapControl();
            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.True, "The player stands on the test grid.");
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(MapData.MapId, landmarks);
            Assert.That(landmarks.Select(landmark => landmark.Name), Is.EqualTo(new[] { "Test Outpost", "Test Depot" }),
                "Plain and IFF-less grids are named, nearest the origin first; hidden ones and the player's own are not.");
            Assert.That((landmarks[0].Position - Outpost).Length(), Is.LessThan(2f), "At the grid's centre of mass.");

            var all = new List<WFSectorLandmarkView>();
            client.CollectLandmarks(MapData.MapId, null, all);
            Assert.That(all.Select(landmark => landmark.Grid), Does.Contain(MapData.CGridUid),
                "The player's own grid is only left out because it is theirs.");
            Assert.That(landmarks.Select(landmark => landmark.Grid), Does.Not.Contain(MapData.CGridUid));
        });
    }

    /// <summary>Rounds an edge midpoint to a key that matches across the cells sharing it.</summary>
    private static (int, int) Key(Vector2 point)
    {
        return ((int) MathF.Round(point.X), (int) MathF.Round(point.Y));
    }

    /// <summary>The map window opens, lays out with the map above its bar, fits the grids and the held cell, zooms about a point and reads out cells with their distance out.</summary>
    [Test]
    public async Task MapWindowLaysOutAndFitsTheSector()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            SpawnStations();
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Held, Holder, null), Is.True);
        });

        await RunTicks(90);
        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<WFSectorMapUIController>();
        await Client.WaitPost(() => controller.OpenWindow());
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(controller.Window, Is.Not.Null);
            var panel = controller.Window!.Panel;
            var map = panel.MapControl;
            Assert.That(map.TryGetView(out var view), Is.True, "The window shows the player's map.");
            Assert.That(view.Map, Is.EqualTo(MapData.MapId));
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(view.Map, landmarks);
            Assert.That(landmarks, Has.Count.EqualTo(2));

            // A held cell's callsign is in its faction's colour, an open one's in grey.
            Assert.That(view.CellLabels.Single(label => label.Text == WFSectorHex.Callsign(Held)).Color, Is.EqualTo(Color.Red));
            Assert.That(view.CellLabels.Single(label => label.Text == WFSectorHex.Callsign(Open)).Color, Is.EqualTo(WFSectorClientSystem.OpenCellColor));

            Assert.That(map.PixelSize.X, Is.GreaterThan(300));
            Assert.That(map.PixelSize.Y, Is.GreaterThan(200));
            Assert.That(panel.CentreButton.Size.X, Is.GreaterThan(0f));
            Assert.That(panel.WholeSectorButton.Size.X, Is.GreaterThan(0f));
            Assert.That(panel.CentreButton.GlobalPosition.Y, Is.GreaterThanOrEqualTo(map.GlobalPosition.Y + map.Size.Y),
                "The bar sits under the map.");
            Assert.That(panel.ScaleLabel.Text, Does.EndWith("km across"));

            // Fitted to every named grid and the held cell, inside the zoom limits.
            var fitted = map.Range;
            Assert.That(fitted, Is.InRange(WFSectorMapControl.MinRange, WFSectorMapControl.MaxRange));
            var toView = map.WorldToView();
            var box = new UIBox2(Vector2.Zero, map.PixelSize);
            foreach (var point in new[] { Outpost, Depot, CentreOf(Held) })
            {
                Assert.That(box.Contains(Vector2.Transform(point, toView)), Is.True, $"{point} is on the map.");
            }

            var depot = Vector2.Transform(Depot, toView);
            Assert.That((map.ViewToWorld(depot) - Depot).Length(), Is.LessThan(1f), "Pixels map back to the same place.");

            Assert.That(map.Readout(view, CentreOf(Held)), Is.EqualTo($"{WFSectorHex.Callsign(Held)} ({Km(CentreOf(Held))} km): Faction id"));
            Assert.That(map.Readout(view, CentreOf(Open)), Is.EqualTo($"{WFSectorHex.Callsign(Open)} ({Km(CentreOf(Open))} km): open space"));

            // Zooming keeps the point under the cursor where it is, and stops at the limits.
            map.ZoomAt(depot, 0.5f);
            Assert.That(map.Range, Is.EqualTo(MathF.Max(fitted * 0.5f, WFSectorMapControl.MinRange)).Within(1f));
            Assert.That((map.ViewToWorld(depot) - Depot).Length(), Is.LessThan(1f));
            map.ZoomAt(depot, 1000f);
            Assert.That(map.Range, Is.EqualTo(WFSectorMapControl.MaxRange));
            map.ZoomAt(depot, 0.0001f);
            Assert.That(map.Range, Is.EqualTo(WFSectorMapControl.MinRange));
            map.ShowWholeSector();
            Assert.That(map.Range, Is.EqualTo(fitted).Within(1f));

            // The test player stands on the test map's grid.
            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.True, "The player's own grid is marked.");
            Assert.That(map.CentreOnPlayer(), Is.True);
        });

        await Client.WaitPost(() => controller.ToggleWindow());
        await RunTicks(2);
        Assert.That(controller.Window, Is.Null, "Toggling closes the window.");
    }

    /// <summary>The distance of a point from the origin as the readout prints it.</summary>
    private static string Km(Vector2 point)
    {
        return (point.Length() / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>The map marks each encounter once, at its first ship, however many ships it has, and leaves out those on other maps.</summary>
    [Test]
    public async Task OneMarkerPerEncounter()
    {
        await Client.WaitAssertion(() =>
        {
            var raiders = new NetEntity(900001);
            var convoy = new NetEntity(900002);
            var elsewhere = new NetEntity(900003);
            var markers = new List<WFEncounterMarker>
            {
                Marker(raiders, "Raiders", WFEncounterCategory.Threat, MapData.MapId, new Vector2(100f, 0f)),
                Marker(raiders, "Raiders", WFEncounterCategory.Threat, MapData.MapId, new Vector2(200f, 0f)),
                Marker(convoy, "Convoy", WFEncounterCategory.Traffic, MapData.MapId, new Vector2(-500f, 0f), Color.Orange),
                Marker(raiders, "Raiders", WFEncounterCategory.Threat, MapData.MapId, new Vector2(300f, 0f)),
                Marker(convoy, "Convoy", WFEncounterCategory.Traffic, MapData.MapId, new Vector2(-600f, 0f), Color.Orange),
                Marker(elsewhere, "Elsewhere", WFEncounterCategory.Patrol, MapId.Nullspace, new Vector2(0f, 0f)),
            };

            var map = new WFSectorMapControl();
            var encounters = new List<WFSectorEncounterView>();
            map.CollectEncounters(MapData.MapId, markers, encounters);
            Assert.That(encounters, Has.Count.EqualTo(2), "Two encounters on this map, five ships between them.");
            Assert.That(encounters[0], Is.EqualTo(new WFSectorEncounterView(raiders, "Raiders", WFEncounterCategory.Threat,
                WFEncounterColors.Category(WFEncounterCategory.Threat), new Vector2(100f, 0f))), "With no side colour, the category's.");
            Assert.That(encounters[1], Is.EqualTo(new WFSectorEncounterView(convoy, "Convoy", WFEncounterCategory.Traffic, Color.Orange, new Vector2(-500f, 0f))),
                "In its first ship's side colour.");
        });
    }

    private static WFEncounterMarker Marker(NetEntity encounter, string name, WFEncounterCategory category, MapId map, Vector2 position, Color? color = null)
    {
        return new WFEncounterMarker
        {
            Encounter = encounter,
            Name = name,
            Category = category,
            Map = map,
            Position = position,
            Color = color,
        };
    }

    /// <summary>A saved file that left the sector map key unbound, or on the old plain N, gets Shift+N back once; a key the player chose, or a later unbind, is left alone.</summary>
    [Test]
    public async Task StaleSectorMapKeyIsRepairedOnce()
    {
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        var cfg = Client.ResolveDependency<IConfigurationManager>();
        var function = ContentKeyFunctions.OpenSectorMap;
        var repaired = false;

        try
        {
            await Client.WaitPost(() =>
            {
                // Unbound, as a saved leaveEmpty entry would leave it.
                foreach (var binding in InputManager.GetKeyBindings(function).ToArray())
                {
                    InputManager.RemoveBinding(binding);
                }

                cfg.SetCVar(SectorControlCVars.MapKeyRepaired, false);
                repaired = controller.RepairKeyBinding();
            });

            Assert.That(repaired, Is.True, "An unbound key is repaired.");
            AssertShiftN(function);
            Assert.That(InputManager.IsKeyFunctionModified(function), Is.False, "The repaired key is the default again.");
            Assert.That(cfg.GetCVar(SectorControlCVars.MapKeyRepaired), Is.True);

            await Client.WaitPost(() =>
            {
                // A deliberate unbind after the repair stays.
                foreach (var binding in InputManager.GetKeyBindings(function).ToArray())
                {
                    InputManager.RemoveBinding(binding);
                }

                repaired = controller.RepairKeyBinding();
            });

            Assert.That(repaired, Is.False, "The repair runs once.");
            Assert.That(InputManager.GetKeyBindings(function), Is.Empty);

            await Client.WaitPost(() =>
            {
                // The old default, saved as a modified key, is replaced.
                InputManager.ResetBindingsFor(function);
                foreach (var binding in InputManager.GetKeyBindings(function).ToArray())
                {
                    InputManager.RemoveBinding(binding);
                }

                InputManager.RegisterBinding(new KeyBindingRegistration { Function = function, BaseKey = Keyboard.Key.N, Type = KeyBindingType.State });
                cfg.SetCVar(SectorControlCVars.MapKeyRepaired, false);
                repaired = controller.RepairKeyBinding();
            });

            Assert.That(repaired, Is.True, "The old plain N is repaired.");
            AssertShiftN(function);

            await Client.WaitPost(() =>
            {
                // A key the player chose is left alone.
                InputManager.ResetBindingsFor(function);
                foreach (var binding in InputManager.GetKeyBindings(function).ToArray())
                {
                    InputManager.RemoveBinding(binding);
                }

                InputManager.RegisterBinding(new KeyBindingRegistration { Function = function, BaseKey = Keyboard.Key.M, Type = KeyBindingType.State });
                cfg.SetCVar(SectorControlCVars.MapKeyRepaired, false);
                repaired = controller.RepairKeyBinding();
            });

            Assert.That(repaired, Is.False, "A chosen key is not undone.");
            Assert.That(InputManager.GetKeyBindings(function).Single().BaseKey, Is.EqualTo(Keyboard.Key.M));
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                InputManager.ResetAllBindings();
                cfg.SetCVar(SectorControlCVars.MapKeyRepaired, true);
            });
        }
    }

    private void AssertShiftN(BoundKeyFunction function)
    {
        var binding = InputManager.GetKeyBindings(function).Single();
        Assert.That(binding.BaseKey, Is.EqualTo(Keyboard.Key.N));
        Assert.That(binding.Mod1, Is.EqualTo(Keyboard.Key.Shift));
    }

    /// <summary>The top bar has a sector map button with an icon, a tooltip and the key; clicking it, the key and the command all open and close the window, and the button stays pressed while the window is open.</summary>
    [Test]
    public async Task TopBarButtonAndKeyToggleTheWindow()
    {
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        var bar = GetWidget<GameTopMenuBar>();
        var button = GetControlFromField<MenuButton>("SectorMapButton", bar);
        await Client.WaitAssertion(() =>
        {
            Assert.That(button.Visible, Is.True);
            Assert.That(button.Icon, Is.Not.Null, "The icon comes from menu.rsi.");
            Assert.That(button.ToolTip, Is.EqualTo("Open the sector map."));
            Assert.That(button.BoundKey, Is.EqualTo(ContentKeyFunctions.OpenSectorMap));
            Assert.That(button.Pressed, Is.False);
            Assert.That(controller.Window, Is.Null);

            // Every context a player can be in, ghosts included, has the key; Shift+N is bound by default.
            foreach (var context in new[] { "common", "human", "ghost", "aghost" })
            {
                Assert.That(InputManager.Contexts.GetContext(context).FunctionExistsHierarchy(ContentKeyFunctions.OpenSectorMap), Is.True, context);
            }

            Assert.That(InputManager.AllBindings.Any(binding => binding.Function == ContentKeyFunctions.OpenSectorMap && binding.BaseKey == Keyboard.Key.N && binding.Mod1 == Keyboard.Key.Shift),
                Is.True, "Shift+N opens the sector map by default.");
        });

        await ClickControl(button);
        Assert.That(controller.Window, Is.Not.Null, "Clicking the button opens the window.");
        Assert.That(button.Pressed, Is.True);

        await ClickControl(button);
        Assert.That(controller.Window, Is.Null, "Clicking it again closes the window.");
        Assert.That(button.Pressed, Is.False);

        await PressClientKey(ContentKeyFunctions.OpenSectorMap);
        Assert.That(controller.Window, Is.Not.Null, "The key opens the window.");
        Assert.That(button.Pressed, Is.True, "The button follows the window.");

        await PressClientKey(ContentKeyFunctions.OpenSectorMap);
        Assert.That(controller.Window, Is.Null, "The key closes it.");
        Assert.That(button.Pressed, Is.False);

        await Client.WaitPost(() => controller.OpenWindow());
        Assert.That(button.Pressed, Is.True, "A window opened another way presses the button.");
        await Client.WaitPost(() => controller.Window!.Close());
        Assert.That(controller.Window, Is.Null);
        Assert.That(button.Pressed, Is.False, "Closing the window lets the button up.");
    }

    /// <summary>A ghost is sent the sector map when it attaches, with no claim and no earlier broadcast, and the window centres on the ghost, over open space or over a grid.</summary>
    [Test]
    public async Task GhostGetsTheSectorMapAndCentresOnItself()
    {
        await StartRound();
        var station = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            station = SpawnStation(Outpost, "Test Outpost");
            SpawnStation(Depot, "Test Depot");
        });

        await RunTicks(30);
        await Client.WaitAssertion(() =>
            Assert.That(Client.System<WFSectorClientSystem>().TryGetMap(MapData.MapId, out _), Is.False, "Nothing has been sent yet."));

        var open = new Vector2(6000f, 2500f);
        var ghost = EntityUid.Invalid;
        var netGhost = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(ServerSession.ContentData()?.Mind);
            ghost = SEntMan.SpawnEntity("MobObserver", new EntityCoordinates(MapData.MapUid, open));
            netGhost = SEntMan.GetNetEntity(ghost);
            minds.TransferTo(minds.CreateMind(ServerSession.UserId).Owner, ghost);
        });

        await RunTicks(30);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "The player is the ghost.");
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        await Client.WaitPost(() => controller.OpenWindow());
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<GhostComponent>(CEntMan.GetEntity(netGhost)), Is.True);
            var map = controller.Window!.Panel.MapControl;
            Assert.That(map.TryGetView(out var view), Is.True, "The ghost is sent the sector map when it attaches.");
            Assert.That(view.Map, Is.EqualTo(MapData.MapId));
            Assert.That(view.Sector, Is.True);
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(view.Map, landmarks);
            Assert.That(landmarks.Select(landmark => landmark.Name), Is.SupersetOf(new[] { "Test Outpost", "Test Depot" }));

            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.False, "A ghost is aboard nothing.");
            Assert.That(map.CentreOnPlayer(), Is.True);
            Assert.That((map.Centre - open).Length(), Is.LessThan(1f), "Centre on me centres on the ghost.");
        });

        // Over a grid the ghost is parented to it, but still centres on itself and not on the grid.
        var above = new Vector2(3f, 2f);
        await Server.WaitPost(() => Server.System<SharedTransformSystem>().SetCoordinates(ghost, new EntityCoordinates(station, above)));
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var map = controller.Window!.Panel.MapControl;
            Assert.That(CEntMan.GetComponent<TransformComponent>(CEntMan.GetEntity(netGhost)).GridUid, Is.Not.Null, "The ghost is over the station's grid.");
            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.False);
            map.ShowWholeSector();
            Assert.That(map.CentreOnPlayer(), Is.True);
            Assert.That((map.Centre - (Outpost + above)).Length(), Is.LessThan(1f));
        });

        await Client.WaitPost(() => controller.Window!.Close());
    }

    /// <summary>
    /// The right button pans the map and the left one does not, a left double-click still centres on the player, and the hover
    /// readout shows only once the cursor has rested on one cell for half a second, never while panning.
    /// </summary>
    [Test]
    public async Task RightButtonPansAndTheReadoutWaitsForTheCursor()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            SpawnStations();
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Held, Holder, null), Is.True);
        });

        await RunTicks(90);
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        await Client.WaitPost(() => controller.OpenWindow());
        await RunTicks(10);

        var rest = WFSectorMapControl.ReadoutDelay + TimeSpan.FromMilliseconds(100);
        var map = controller.Window!.Panel.MapControl;
        var here = Vector2.Zero;
        var elsewhere = Vector2.Zero;
        var centre = Vector2.Zero;
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.TryGetView(out var view), Is.True);
            here = Vector2.Transform(CentreOf(Held), map.WorldToView());
            Assert.That(WFSectorHex.CellOf(map.ViewToWorld(here), view.CellSize), Is.EqualTo(Held));
            Assert.That(WFSectorHex.CellOf(map.ViewToWorld(here + new Vector2(2f, 0f)), view.CellSize), Is.EqualTo(Held));

            // A pixel on the map over some other cell.
            for (var step = 20f; step < 400f && elsewhere == Vector2.Zero; step += 20f)
            {
                foreach (var sign in new[] { 1f, -1f })
                {
                    var pixel = here + new Vector2(step * sign, 0f);
                    if (pixel.X > 0f && pixel.X < map.PixelSize.X
                        && WFSectorHex.CellOf(map.ViewToWorld(pixel), view.CellSize) != Held)
                    {
                        elsewhere = pixel;
                        break;
                    }
                }
            }

            Assert.That(elsewhere, Is.Not.EqualTo(Vector2.Zero), "Another cell is in view.");
            centre = map.Centre;

            // The left button does nothing to the view, held or dragged.
            var down = Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Down, here);
            Invoke(map, "KeyBindDown", down);
            Assert.That(down.Handled, Is.True);
            Invoke(map, "MouseMove", Move(map, here + new Vector2(60f, -40f)));
            Invoke(map, "KeyBindUp", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Up, here + new Vector2(60f, -40f)));
            Assert.That(map.Centre, Is.EqualTo(centre), "A left drag leaves the view where it is.");
            Invoke(map, "MouseExited");
            Assert.That(map.ReadoutShown, Is.False);
        });

        // Not at once: the cursor has to rest first.
        await Client.WaitAssertion(() =>
        {
            Invoke(map, "MouseMove", Move(map, here));
            Assert.That(map.ReadoutShown, Is.False, "Not as soon as the cursor arrives.");
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.ReadoutShown, Is.True, "Half a second on one cell shows it.");
            Invoke(map, "MouseMove", Move(map, here + new Vector2(2f, 0f)));
            Assert.That(map.ReadoutShown, Is.True, "A few pixels' drift within the cell keeps it.");
            Invoke(map, "MouseMove", Move(map, elsewhere));
            Assert.That(map.ReadoutShown, Is.False, "Another cell hides it at once.");
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.ReadoutShown, Is.True, "It shows again after resting on the new cell.");
            Invoke(map, "MouseExited");
            Assert.That(map.ReadoutShown, Is.False, "Leaving the control hides it.");
            Invoke(map, "MouseMove", Move(map, elsewhere));
            Assert.That(map.ReadoutShown, Is.False, "Coming back starts the wait again.");
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.ReadoutShown, Is.True);
            Invoke(map, "KeyBindDown", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Down, elsewhere));
            Assert.That(map.ReadoutShown, Is.False, "A click hides it.");
            Invoke(map, "KeyBindUp", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Up, elsewhere));

            // The right button pans, a step at a time as the cursor moves.
            var pan = new Vector2(60f, -40f);
            var down = Key(map, EngineKeyFunctions.UIRightClick, BoundKeyState.Down, elsewhere);
            Invoke(map, "KeyBindDown", down);
            Assert.That(down.Handled, Is.True);
            Invoke(map, "MouseMove", Move(map, elsewhere + pan));
            var expected = centre - new Vector2(pan.X, -pan.Y) / map.PixelsPerMetre;
            Assert.That((map.Centre - expected).Length(), Is.LessThan(1f), "The map follows the cursor.");
            Invoke(map, "MouseMove", Move(map, elsewhere + pan * 2f));
            Assert.That((map.Centre - (expected - new Vector2(pan.X, -pan.Y) / map.PixelsPerMetre)).Length(), Is.LessThan(1f));
            Assert.That(map.ReadoutShown, Is.False);
        });

        // Held down it stays hidden, however long the cursor stays where it is.
        await Task.Delay(rest);
        var panned = Vector2.Zero;
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.ReadoutShown, Is.False, "Never while a pan is in progress.");
            panned = map.Centre;
            var end = elsewhere + new Vector2(120f, -80f);
            var up = Key(map, EngineKeyFunctions.UIRightClick, BoundKeyState.Up, end);
            Invoke(map, "KeyBindUp", up);
            Assert.That(up.Handled, Is.True);
            Assert.That(map.ReadoutShown, Is.False, "Releasing starts the wait again.");
            Invoke(map, "MouseMove", Move(map, end + new Vector2(30f, 0f)));
            Assert.That(map.Centre, Is.EqualTo(panned), "Released, the button no longer pans.");
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.ReadoutShown, Is.True, "Rested again, it shows.");
            Invoke(map, "MouseWheel", Wheel(map, new Vector2(0f, 1f), elsewhere));
            Assert.That(map.ReadoutShown, Is.False, "Zooming hides it.");
        });

        // The left button double-clicked still centres on the player's grid.
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.TryGetOwnShip(MapData.MapId, out var ship, out _), Is.True);
            Assert.That((map.Centre - ship).Length(), Is.GreaterThan(100f), "The view is off the ship after panning.");
            Invoke(map, "KeyBindDown", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Down, here));
            Invoke(map, "KeyBindUp", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Up, here));
            Assert.That((map.Centre - ship).Length(), Is.GreaterThan(100f), "One click does not.");
            Invoke(map, "KeyBindDown", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Down, here));
            Invoke(map, "KeyBindUp", Key(map, EngineKeyFunctions.UIClick, BoundKeyState.Up, here));
            Assert.That((map.Centre - ship).Length(), Is.LessThan(1f), "A double-click does.");
        });

        await Client.WaitPost(() => controller.Window!.Close());
    }

    /// <summary>
    /// Grids come through as blips in their IFF colour, a station member as a station unless a deed makes it a bought ship;
    /// resting on a blip shows its card, resting away from one the cell's line, and only the hovered blip is named zoomed out.
    /// </summary>
    [Test]
    public async Task BlipsShowCardsAndNamesWhenHovered()
    {
        await StartRound();
        var outpostColor = Color.FromHex("#3fd2ff");
        var outpostAt = CentreOf(Held) + new Vector2(400f, 300f);
        var haulerAt = CentreOf(Open) + new Vector2(-300f, 200f);
        await Server.WaitAssertion(() =>
        {
            var outpost = SpawnStation(outpostAt, "Test Outpost");
            Server.System<SharedShuttleSystem>().SetIFFColor(outpost, outpostColor);
            SpawnGrid(haulerAt, "Test Hauler");
            var charter = SpawnStation(Depot, "Test Charter");
            SEntMan.EnsureComponent<ShuttleDeedComponent>(charter);
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(territory.TryClaim(MapData.MapId, Held, Holder, null), Is.True);
            Assert.That(territory.Contest(MapData.MapId, Open, Rival, STiming.CurTime + TimeSpan.FromMinutes(10)), Is.True);
        });

        await RunTicks(90);
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        await Client.WaitPost(() => controller.OpenWindow());
        await RunTicks(10);

        var rest = WFSectorMapControl.ReadoutDelay + TimeSpan.FromMilliseconds(100);
        var map = controller.Window!.Panel.MapControl;
        WFSectorMapView view = default!;
        var outpost = default(WFSectorLandmarkView);
        var hauler = default(WFSectorLandmarkView);
        var from = Vector2.Zero;
        var away = Vector2.Zero;
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.TryGetView(out view), Is.True);
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(view.Map, landmarks);
            outpost = landmarks.Single(landmark => landmark.Name == "Test Outpost");
            hauler = landmarks.Single(landmark => landmark.Name == "Test Hauler");
            var charter = landmarks.Single(landmark => landmark.Name == "Test Charter");
            Assert.That(outpost.Kind, Is.EqualTo(WFSectorBlipKind.Station), "A station member is a station.");
            Assert.That(outpost.Color, Is.EqualTo(outpostColor), "In its own IFF colour.");
            Assert.That(hauler.Kind, Is.EqualTo(WFSectorBlipKind.Ship), "A plain grid is a ship.");
            Assert.That(hauler.Color, Is.EqualTo(IFFComponent.IFFColor), "With no IFF, in the radar's default colour.");
            Assert.That(charter.Kind, Is.EqualTo(WFSectorBlipKind.Ship), "A station member with a deed is a bought ship.");

            Assert.That(WFSectorMapControl.Bearing(Vector2.Zero, new Vector2(0f, 5f)), Is.EqualTo("000"));
            Assert.That(WFSectorMapControl.Bearing(Vector2.Zero, new Vector2(5f, 0f)), Is.EqualTo("090"));
            Assert.That(WFSectorMapControl.Bearing(Vector2.Zero, new Vector2(0f, -5f)), Is.EqualTo("180"));
            Assert.That(WFSectorMapControl.Bearing(Vector2.Zero, new Vector2(-5f, 0f)), Is.EqualTo("270"));

            Assert.That(map.TryGetOwnShip(view.Map, out from, out _), Is.True, "Ranges are from the player's own ship.");
            var pixel = Vector2.Transform(outpost.Position, map.WorldToView());
            Assert.That(map.BlipAt(pixel + new Vector2(3f, -3f)), Is.EqualTo(WFSectorBlip.Of(outpost)), "A few pixels off still points at it.");
            away = pixel + Vector2.Normalize((Vector2) map.PixelSize / 2f - pixel) * 40f;
            Assert.That(map.BlipAt(away), Is.Null, "Away from any blip.");

            Invoke(map, "MouseMove", Move(map, pixel));
            Assert.That(map.ShownReadout(), Is.Empty, "Not as soon as the cursor arrives.");
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            var card = map.ShownReadout().ToList();
            Assert.That(card.Select(line => line.Text), Is.EqualTo(new[]
            {
                "Test Outpost",
                "Station",
                $"{Kilometres(from, outpost.Position)} km, bearing {WFSectorMapControl.Bearing(from, outpost.Position)}",
                $"{WFSectorHex.Callsign(Held)}: held by Faction id",
            }));
            Assert.That(card[1].Swatch, Is.EqualTo(outpostColor), "The kind comes with a swatch of its IFF colour.");
            Invoke(map, "MouseMove", Move(map, Vector2.Transform(hauler.Position, map.WorldToView())));
            Assert.That(map.ShownReadout(), Is.Empty, "Another blip starts the wait again.");
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            var card = map.ShownReadout().ToList();
            Assert.That(card.Select(line => line.Text).Take(4), Is.EqualTo(new[]
            {
                "Test Hauler",
                "Ship",
                $"{Kilometres(from, hauler.Position)} km, bearing {WFSectorMapControl.Bearing(from, hauler.Position)}",
                $"{WFSectorHex.Callsign(Open)}: open space",
            }));
            Assert.That(card[1].Swatch, Is.EqualTo(IFFComponent.IFFColor));
            Assert.That(card, Has.Count.EqualTo(5));
            Assert.That(card[4].Text, Does.StartWith("Contested by Subcommand, "), "A contested cell's countdown.");
            Invoke(map, "MouseMove", Move(map, away));
            Assert.That(map.ShownReadout(), Is.Empty);
        });

        await Task.Delay(rest);
        await Client.WaitAssertion(() =>
        {
            Assert.That(map.ShownReadout().Select(line => line.Text), Is.EqualTo(new[] { map.Readout(view, map.ViewToWorld(away)) }),
                "Away from any blip, the cell's line as before.");

            // Zoomed out, only the hovered blip is named.
            map.ZoomAt((Vector2) map.PixelSize / 2f, 1000f);
            var pixel = Vector2.Transform(outpost.Position, map.WorldToView());
            Invoke(map, "MouseMove", Move(map, pixel));
            Assert.That(map.HoveredBlip, Is.EqualTo(WFSectorBlip.Of(outpost)));
            Assert.That(map.LabelShown(WFSectorBlip.Of(outpost)), Is.True, "The hovered blip is named.");
            Assert.That(map.LabelShown(WFSectorBlip.Of(hauler)), Is.False, "The rest are not.");

            // Zoomed in, still only the hovered one.
            map.ZoomAt(pixel, 0.0001f);
            pixel = Vector2.Transform(outpost.Position, map.WorldToView());
            Invoke(map, "MouseMove", Move(map, pixel));
            Assert.That(map.LabelShown(WFSectorBlip.Of(outpost)), Is.True);
            Assert.That(map.LabelShown(WFSectorBlip.Of(hauler)), Is.False, "Zoom never names the rest.");
            Invoke(map, "MouseExited");
            Assert.That(map.HoveredBlip, Is.Null);
        });

        await Client.WaitPost(() => controller.Window!.Close());
    }

    /// <summary>The distance between two points as the card prints it.</summary>
    private static string Kilometres(Vector2 from, Vector2 to)
    {
        return (Vector2.Distance(from, to) / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static GUIBoundKeyEventArgs Key(WFSectorMapControl map, BoundKeyFunction function, BoundKeyState state, Vector2 pixel)
    {
        return new GUIBoundKeyEventArgs(function, state, default, default, pixel / map.UIScale, pixel);
    }

    private static GUIMouseMoveEventArgs Move(WFSectorMapControl map, Vector2 pixel)
    {
        return new GUIMouseMoveEventArgs(Vector2.Zero, map, pixel / map.UIScale, default, pixel / map.UIScale, pixel);
    }

    private static GUIMouseWheelEventArgs Wheel(WFSectorMapControl map, Vector2 delta, Vector2 pixel)
    {
        return new GUIMouseWheelEventArgs(delta, map, pixel / map.UIScale, default, pixel / map.UIScale, pixel);
    }

    /// <summary>Calls one of a control's input handlers, which the engine keeps out of reach of content.</summary>
    private static void Invoke(Control control, string method, params object[] args)
    {
        var types = args.Select(arg => arg.GetType()).ToArray();
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, types, null)!
            .Invoke(control, args);
    }

    /// <summary>Presses and releases a key function in the client's own input, as a keypress outside the game world.</summary>
    private async Task PressClientKey(BoundKeyFunction function)
    {
        await Client.WaitPost(() =>
        {
            InputManager.ViewportKeyEvent(null, new BoundKeyEventArgs(function, BoundKeyState.Down, default, false));
            InputManager.ViewportKeyEvent(null, new BoundKeyEventArgs(function, BoundKeyState.Up, default, false));
        });
        await RunTicks(2);
    }
}
