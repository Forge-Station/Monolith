#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared.Alert;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    private static ProtoId<WFEncounterPrototype> WFTestStealthBoarderProto => "WFTestStealthBoarder";

    [TestPrototypes]
    private const string ClampPrototypes = @"
- type: wfEncounter
  id: WFTestStealthBoarder
  name: wf-encounter-name-convoy
  start: Manual
  hidden: true
  ships:
  - key: boarder
    vessel: WFDredger
    side: pirates
    deckhands: 1
    guards: 1
    hunt: true
    stealth: true
";

    /// <summary>A bare deck with a port and a helm that needs no power.</summary>
    private (EntityUid Grid, EntityUid Dock, EntityUid Helm) ClampDeck(Vector2 at)
    {
        var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < 3; x++)
        {
            tiles.Add((new Vector2i(x, 0), new Tile(1)));
        }

        Server.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
        Server.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, at);
        var dock = SEntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
        var helm = SEntMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid.Owner, new Vector2(2.5f, 0.5f)));
        var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(helm);
        power.NeedsPower = false;
        power.Powered = true;
        return (grid.Owner, dock, helm);
    }

    /// <summary>
    /// A raider's clamps take hold of the ship it docks with: that ship's helm cannot undock it and it cannot jump, the
    /// raider's own helm can. Released at the raider's helm they let go and stay open; with no powered helm they let go.
    /// </summary>
    [Test]
    public async Task DockingClampsHoldAShipUntilReleasedOrUnpowered()
    {
        (EntityUid Grid, EntityUid Dock, EntityUid Helm) raider = default, prey = default;
        var docking = Server.System<DockingSystem>();
        var clampSystem = Server.System<WFDockingClampSystem>();
        await Server.WaitAssertion(() =>
        {
            raider = ClampDeck(new Vector2(52000, -52000));
            prey = ClampDeck(new Vector2(52040, -52000));
            SEntMan.AddComponent<WFDockingClampComponent>(raider.Grid);
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            var clamps = SEntMan.GetComponent<WFDockingClampComponent>(raider.Grid);
            var raiderDock = (raider.Dock, SEntMan.GetComponent<DockingComponent>(raider.Dock));
            var preyDock = (prey.Dock, SEntMan.GetComponent<DockingComponent>(prey.Dock));
            docking.Dock(raiderDock, preyDock);
            Assert.That(clamps.Held, Does.Contain(prey.Grid), "Docked, the clamps bite.");
            Assert.That(clampSystem.IsHeld(prey.Grid), Is.True);
            Assert.That(clampSystem.IsHeld(raider.Grid), Is.False, "The raider is held by nothing.");
            Assert.That(clampSystem.Refuses(prey.Helm, preyDock), Is.True, "The held ship cannot let go of its own port.");
            Assert.That(clampSystem.Refuses(prey.Helm, raiderDock), Is.True, "Nor of the raider's.");
            Assert.That(clampSystem.Refuses(raider.Helm, raiderDock), Is.False, "The raider casts off when it likes.");

            var jump = new ConsoleFTLAttemptEvent(prey.Grid, false, string.Empty);
            SEntMan.EventBus.RaiseLocalEvent(prey.Grid, ref jump, true);
            Assert.That(jump.Cancelled, Is.True, "A held ship cannot jump.");

            // Released at the raider's helm: free, and not bitten again on the next docking.
            clampSystem.Release((raider.Grid, clamps), SPlayer);
            Assert.That(clampSystem.Refuses(prey.Helm, preyDock), Is.False);
            docking.Undock(preyDock);
            docking.Dock(raiderDock, preyDock);
            Assert.That(clamps.Held, Is.Empty, "Opened by hand, the clamps stay open a while.");

            // Shut again, they bite on the next docking, and let go when the raider's helm goes dark.
            docking.Undock(preyDock);
            clamps.OpenUntil = TimeSpan.Zero;
            docking.Dock(raiderDock, preyDock);
            Assert.That(clampSystem.IsHeld(prey.Grid), Is.True);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(raider.Helm).Powered = false;
            Assert.That(clampSystem.Refuses(prey.Helm, preyDock), Is.False, "No power, no clamps.");
            Assert.That(clamps.Held, Is.Empty);

            docking.Undock(preyDock);
            SEntMan.DeleteEntity(raider.Grid);
            SEntMan.DeleteEntity(prey.Grid);
        });
        await RunTicks(5);
    }

    /// <summary>
    /// Everyone aboard a held ship carries the clamped alert, which clears when the clamps let go or they step off it,
    /// and the held ship's helm says who holds it.
    /// </summary>
    [Test]
    public async Task DockingClampsAlertEveryoneAboard()
    {
        (EntityUid Grid, EntityUid Dock, EntityUid Helm) raider = default, prey = default;
        EntityCoordinates home = default;
        var alerts = Server.System<AlertsSystem>();
        var xforms = Server.System<SharedTransformSystem>();
        var clampSystem = Server.System<WFDockingClampSystem>();
        await Server.WaitAssertion(() =>
        {
            raider = ClampDeck(new Vector2(52000, -52100));
            prey = ClampDeck(new Vector2(52040, -52100));
            SEntMan.AddComponent<WFDockingClampComponent>(raider.Grid);
            SEntMan.EnsureComponent<AlertsComponent>(SPlayer);
            home = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            xforms.SetCoordinates(SPlayer, new EntityCoordinates(prey.Grid, new Vector2(1.5f, 0.5f)));
        });
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            Server.System<DockingSystem>().Dock((raider.Dock, SEntMan.GetComponent<DockingComponent>(raider.Dock)),
                (prey.Dock, SEntMan.GetComponent<DockingComponent>(prey.Dock)));
            Assert.That(clampSystem.HeldBy(prey.Grid), Is.EqualTo(raider.Grid));
            Assert.That(ClampedBy(prey.Dock), Is.EqualTo(SEntMan.GetNetEntity(raider.Grid)), "The docking screen flags the held port.");
            Assert.That(ClampedBy(raider.Dock), Is.EqualTo(SEntMan.GetNetEntity(raider.Grid)), "And the raider's.");
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsShowingAlert(SPlayer, "WFDockingClamped"), Is.True, "Aboard a held ship, the alert shows.");
            clampSystem.Release((raider.Grid, SEntMan.GetComponent<WFDockingClampComponent>(raider.Grid)), SPlayer);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsShowingAlert(SPlayer, "WFDockingClamped"), Is.False, "Released, the alert clears.");
            Assert.That(ClampedBy(prey.Dock), Is.Null, "Released, the docking screen clears.");
            xforms.SetCoordinates(SPlayer, home);
            Server.System<DockingSystem>().Undock((prey.Dock, SEntMan.GetComponent<DockingComponent>(prey.Dock)));
            SEntMan.DeleteEntity(raider.Grid);
            SEntMan.DeleteEntity(prey.Grid);
        });
        await RunTicks(5);
    }

    /// <summary>The raider the docking screen says holds a port's joint.</summary>
    private NetEntity? ClampedBy(EntityUid dock)
    {
        var net = SEntMan.GetNetEntity(dock);
        foreach (var ports in Server.System<ShuttleConsoleSystem>().GetAllDocks().Values)
        {
            foreach (var port in ports)
            {
                if (port.Entity == net)
                    return port.WfClampedBy;
            }
        }

        return null;
    }

    /// <summary>
    /// A stealth boarder flies with its IFF label off and its clamps fitted, and its encounter stays off the sector
    /// markers whatever happens to it, unless an admin shows it.
    /// </summary>
    [Test]
    public async Task StealthShipsStayUnlabelledAndOffTheMarkers()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(WFTestStealthBoarderProto);
            var system = Server.System<WFEncounterSystem>();
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(-52000, 52000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var ship = comp.Ships["boarder"];
            var flags = SEntMan.GetComponent<IFFComponent>(ship.Grid).Flags;
            Assert.That((flags & IFFFlags.HideLabel) != 0, Is.True, "No label on any scope.");
            Assert.That(SEntMan.HasComponent<WFDockingClampComponent>(ship.Grid), Is.True, "A boarder carries clamps.");

            system.Reveal(encounter);
            Assert.That(comp.Hidden, Is.True, "Nothing its crew get into shows it.");
            system.Reveal(encounter, true);
            Assert.That(comp.Hidden, Is.False, "An admin can.");
            system.End(encounter);
        });
        await RunTicks(10);
    }
}
