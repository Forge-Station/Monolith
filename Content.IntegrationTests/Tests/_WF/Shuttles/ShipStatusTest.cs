using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Shuttles.Systems;
using Content.Client._WF.Shuttles.UI;
using Content.Client.Shuttles.UI;
using Content.Server.Power.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Shuttles;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Shuttles.Components;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// The whole-ship view's damage overlay is driven by a server sweep of anchored structures.
/// </summary>
public sealed class ShipStatusTest
{
    [Test]
    public async Task DamagedWallIsReported()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var protoManager = server.ResolveDependency<IPrototypeManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var damageSystem = entManager.System<DamageableSystem>();
        var statusSystem = entManager.System<ShipStatusSystem>();

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(map.Grid);

            var grid = mapManager.CreateGridEntity(map.MapId);

            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(grid.Owner, grid.Comp, tiles);

            var console = entManager.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));

            var wall = entManager.SpawnEntity("WallSolid",
                new EntityCoordinates(grid.Owner, new Vector2(2.5f, 2.5f)));

            var wallTile = mapSystem.TileIndicesFor(grid.Owner, grid.Comp,
                entManager.GetComponent<TransformComponent>(wall).Coordinates);

            // An undamaged hull should report nothing at all.
            var clean = statusSystem.GetStatus(console);
            Assert.That(clean, Is.Not.Null, "The console should resolve to its own grid.");
            Assert.That(clean!.Tiles.Any(t => (t.Flags & ShipTileFlags.Damaged) != 0), Is.False,
                "An intact hull should report no damaged tiles.");

            Assert.That(clean.Summary.HullIntegrity, Is.EqualTo(1f));

            // Hit the wall hard enough to be well past the reporting threshold but not destroy it.
            var damage = new DamageSpecifier(protoManager.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(100));
            damageSystem.TryChangeDamage(wall, damage, ignoreResistances: true);

            var damaged = statusSystem.GetStatus(console);
            Assert.That(damaged, Is.Not.Null);

            var reported = damaged!.Tiles.FirstOrDefault(t => t.Index == wallTile);
            Assert.That(reported.Flags & ShipTileFlags.Damaged, Is.Not.EqualTo(ShipTileFlags.None),
                "The damaged wall's tile should be flagged.");
            Assert.That(damaged.Summary.DamagedTiles, Is.GreaterThan(0));
            Assert.That(damaged.Summary.WorstIntegrity, Is.LessThan(1f));
            Assert.That(damaged.Summary.HullIntegrity, Is.LessThan(1f));
            Assert.That(damaged.Summary.HullIntegrity, Is.GreaterThan(damaged.Summary.WorstIntegrity),
                "The whole-hull condition averages healthy locations as well as damaged ones.");
            var hullOnly = statusSystem.GetStatus(console, hullOnly: true)!;
            Assert.That(hullOnly.Summary.HullIntegrity, Is.EqualTo(damaged.Summary.HullIntegrity));
            Assert.That(hullOnly.Tiles, Is.Empty, "A hidden SHP page must not stream per-tile overlays.");
            Assert.That(hullOnly.Summary.DamagedTiles, Is.Zero,
                "The permanent gauge requests hull condition without collecting hidden detailed readings.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HullOnlyViewerCannotReplaceOrCancelAnotherViewersDetailedSubscription()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid console = default;
        EntityUid detailedViewer = default;
        EntityUid hullViewer = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0), map.Tile.Tile);
            detailedViewer = em.SpawnEntity("MobHuman", map.GridCoords);
            hullViewer = em.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, detailedViewer);
            console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
            var ui = em.System<SharedUserInterfaceSystem>();
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, detailedViewer);
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, hullViewer);
            DamageWall(1);
        });
        await pair.RunTicksSync(5);
        // The window only reads detailed status while its Ship page is showing, as a detailed viewer's client would.
        await pair.Client.WaitPost(() => ShipWindow().SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Ship));
        await pair.Server.WaitAssertion(() =>
        {
            var ui = em.System<SharedUserInterfaceSystem>();
            Assert.That(ui.IsUiOpen(console, ShuttleConsoleUiKey.Key, detailedViewer), Is.True);
            Assert.That(ui.IsUiOpen(console, ShuttleConsoleUiKey.Key, hullViewer), Is.True);
            ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key,
                new ShipStatusRequestMessage(true, ShipOverlays.Damage) { Actor = detailedViewer });
            ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key,
                new ShipStatusRequestMessage(true, ShipOverlays.None, hullOnly: true) { Actor = hullViewer });
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() => AssertDamagedTiles("1",
            "A cockpit hull-only request must retain another viewer's detailed damage stream."));
        await pair.Server.WaitAssertion(() =>
        {
            em.System<SharedUserInterfaceSystem>().RaiseUiMessage(console, ShuttleConsoleUiKey.Key,
                new ShipStatusRequestMessage(false, ShipOverlays.None, hullOnly: true) { Actor = hullViewer });
            DamageWall(2);
        });
        await pair.RunTicksSync(70);
        await pair.Client.WaitAssertion(() => AssertDamagedTiles("2",
            "Stopping one viewer's request must leave the other viewer subscribed to later damage."));
        await pair.Server.WaitAssertion(() =>
        {
            var ui = em.System<SharedUserInterfaceSystem>();
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, hullViewer);
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, detailedViewer);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            em.DeleteEntity(hullViewer);
            em.DeleteEntity(detailedViewer);
        });
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
        return;

        void DamageWall(int x)
        {
            var wall = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(x + 0.5f, 0.5f)));
            var prototype = pair.Server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
            em.System<DamageableSystem>().TryChangeDamage(wall, new DamageSpecifier(prototype, FixedPoint2.New(100)), ignoreResistances: true);
        }

        ShuttleConsoleWindow ShipWindow() =>
            pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<ShuttleConsoleWindow>().Single();

        void AssertDamagedTiles(string expected, string message)
        {
            var ship = ShipWindow().FindControl<ShipScreen>("ShipContainer");
            Assert.That(ship.FindControl<Label>("DamagedLabel").Text, Is.EqualTo(expected), message);
        }
    }

    [Test]
    public async Task RemovingEquipmentDoesNotBecomePermanentHullDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var status = em.System<ShipStatusSystem>();
            var console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            var chair = em.SpawnEntity("ChairPilotSeat", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            var machine = em.SpawnEntity("ComputerGunneryConsole", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            Assert.That(em.GetComponent<TransformComponent>(chair).Anchored, Is.True);
            Assert.That(em.GetComponent<TransformComponent>(machine).Anchored, Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            em.DeleteEntity(chair);
            em.DeleteEntity(machine);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Removing furniture or machines must not permanently scar the structural hull survey.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DestroyedStructuresAndFloorRemainLostUntilTheirLocationsAreRepaired()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = em.System<SharedMapSystem>();
        var status = em.System<ShipStatusSystem>();
        var wall = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var wallPosition = new EntityCoordinates(map.Grid.Owner, new Vector2(3.5f, 1.5f));
        var missingFloor = new Vector2i(4, 2);

        await server.WaitAssertion(() =>
        {
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 3; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f)));
            wall = em.SpawnEntity("WallSolid", wallPosition);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));

            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(1400));
            em.System<DamageableSystem>().TryChangeDamage(wall, damage, ignoreResistances: true);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, missingFloor, Tile.Empty);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(wall), Is.True, "Native destruction must remove the wall, as an impact does.");
            var lost = status.GetStatus(console)!.Summary;
            Assert.That(lost.WorstIntegrity, Is.EqualTo(1f), "The surviving structures are intact.");
            Assert.That(lost.HullIntegrity, Is.EqualTo(13f / 15f).Within(0.001f),
                "The destroyed wall and missing deck both remain in the surveyed hull.");

            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(5, 0), map.Tile.Tile);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(14f / 16f).Within(0.001f),
                "New construction adds hull without forgetting losses at old locations.");

            var replacement = em.SpawnEntity("WallSolid", wallPosition);
            Assert.That(replacement, Is.Not.EqualTo(wall));
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(15f / 16f).Within(0.001f),
                "A replacement structure restores its original location while the deck hole remains.");
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, missingFloor, map.Tile.Tile);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Repairing every surveyed location restores the hull.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DestroyedDiagonalWallsAndWindowsLowerTheHullGauge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;
        var status = em.System<ShipStatusSystem>();
        var structures = new List<EntityUid>();
        var console = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 1; x < 4; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            console = em.SpawnEntity("ComputerShuttle", At(0));
            // These prototypes replace the Wall and Window tags with Diagonal alone.
            structures.Add(em.SpawnEntity("WallReinforcedDiagonal", At(1)));
            structures.Add(em.SpawnEntity("ReinforcedWindowDiagonal", At(2)));
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));

            var blunt = server.ProtoMan.Index<DamageTypePrototype>("Blunt");
            foreach (var structure in structures)
                em.System<DamageableSystem>().TryChangeDamage(structure,
                    new DamageSpecifier(blunt, FixedPoint2.New(5000)), ignoreResistances: true);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            foreach (var structure in structures)
                Assert.That(em.Deleted(structure), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.5f).Within(0.001f),
                "Destroyed diagonal reinforced walls and windows are hull and must stay lost.");
        });
        await pair.CleanReturnAsync();
        return;

        EntityCoordinates At(int x) => new(map.Grid.Owner, new Vector2(x + 0.5f, 0.5f));
    }

    [Test]
    public async Task PilotEntrySurveysHullBeforeAConsoleBearingFragmentSplitsAway()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, true));
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var maps = em.System<SharedMapSystem>();
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 2; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            var position = new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f));
            var console = em.SpawnEntity("ComputerShuttle", position);
            var pilot = em.SpawnEntity("MobHuman", position);
            em.EnsureComponent<PilotComponent>(pilot);
            em.System<ShuttleConsoleSystem>().AddPilot(console, pilot, em.GetComponent<ShuttleConsoleComponent>(console));

            // No status page has been opened before the impact separates the smaller, piloted section.
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(1, 0), Tile.Empty),
                (new Vector2i(1, 1), Tile.Empty),
            });
            var fragment = em.GetComponent<TransformComponent>(console).GridUid;
            Assert.That(fragment, Is.Not.Null);
            Assert.That(fragment, Is.Not.EqualTo(map.Grid.Owner), "The console must be on the newly created smaller grid.");
            var originalConsole = em.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f)));
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(5, 0), map.Tile.Tile);
            var status = em.System<ShipStatusSystem>();
            Assert.That(status.GetStatus(originalConsole)!.Summary.HullIntegrity, Is.EqualTo(7f / 11f).Within(0.001f),
                "Construction on the original grid updates its survey without modifying the fragment's inherited snapshot.");
            var summary = status.GetStatus(console)!.Summary;
            Assert.That(summary.WorstIntegrity, Is.EqualTo(1f));
            Assert.That(summary.HullIntegrity, Is.EqualTo(2f / 10f).Within(0.001f),
                "The new fragment inherits the original hull baseline instead of resetting to a healthy ship.");
        });
        await pair.CleanReturnAsync();
    }
}
