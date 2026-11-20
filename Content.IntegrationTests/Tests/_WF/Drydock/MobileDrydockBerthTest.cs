using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.Drydock;
using Content.Server._WF.GridSettle;
using Content.Shared._WF.Drydock;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Checks a ship parked in a mobile drydock's berth stays still, keeps its console UI open, and is not re-sent or re-created.</summary>
[TestFixture]
public sealed class MobileDrydockBerthTest
{
    private const int WarmupTicks = 20;
    private const int MeasuredTicks = 300;
    private const float MaxJitter = 0.01f;

    /// <summary>Per-tick samples of one entity: its world position, parent, grid and components.</summary>
    private sealed class Track
    {
        public readonly List<Vector2> Positions = new();
        public readonly List<EntityUid> Parents = new();
        public readonly List<uint> GridTicks = new();
        public readonly List<string> ComponentSets = new();
        public readonly List<uint> CraneTicks = new();
    }

    /// <summary>Spawns a host grid with a mobile crane and console, and a ship parked over the berth.</summary>
    private static (EntityUid Host, EntityUid Crane, EntityUid Console, EntityUid Ship) SpawnScene(TestPair pair, MapId map, bool againstCrane)
    {
        var (host, crane, console) = MobileDrydockTest.SpawnMobileYard(pair, map);
        var em = pair.Server.EntMan;
        Assert.That(pair.Server.System<WFDrydockSystem>().TryGetBerth(console, em.GetComponent<WFDrydockConsoleComponent>(console), out var berth),
            "The crane should make a berth.");

        var center = berth.Area.Center;
        if (againstCrane)
        {
            // A metre from the crane's centre toward the bay, so the hull overlaps the crane body.
            var craneLocal = em.GetComponent<TransformComponent>(crane).LocalPosition;
            center = craneLocal + Vector2.Normalize(center - craneLocal);
        }

        var ship = DrydockTest.CreateDamagedShip(pair, map);
        DrydockTest.PlaceOver(pair, ship, host, center);
        return (host, crane, console, ship);
    }

    /// <summary>Samples one entity's transform, grid and components into a track.</summary>
    private static void Sample(TestPair pair, Track track, EntityUid uid)
    {
        var em = pair.Server.EntMan;
        var xforms = pair.Server.System<SharedTransformSystem>();
        track.Positions.Add(xforms.GetWorldPosition(uid));
        track.Parents.Add(em.GetComponent<TransformComponent>(uid).ParentUid);
        if (em.TryGetComponent(uid, out MapGridComponent grid))
            track.GridTicks.Add(grid.LastModifiedTick.Value);
        // The grid settle marker and the cargo load are server-only and come and go on their own once-a-second scans.
        track.ComponentSets.Add(string.Join(",", em.GetComponents(uid).Where(c => c is not (WFGridSettleComponent or WFCargoLoadComponent))
            .Select(c => c.GetType().Name).OrderBy(n => n)));
    }

    /// <summary>The largest distance any sample sits from the first one.</summary>
    private static float MaxDrift(IReadOnlyList<Vector2> positions)
    {
        var first = positions[0];
        return positions.Max(p => Vector2.Distance(p, first));
    }

    /// <summary>How many consecutive samples differ from the one before.</summary>
    private static int Changes<T>(IReadOnlyList<T> values) where T : notnull
    {
        var changes = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (!values[i].Equals(values[i - 1]))
                changes++;
        }

        return changes;
    }

    /// <summary>Runs the scene with a ship parked in the berth and checks it stays put, its UI open, and its grids not churned.</summary>
    [TestCase(false, TestName = "ParkedShipInMobileBerthStaysStable")]
    [TestCase(true, TestName = "ShipTouchingMobileCraneStaysStable")]
    public async Task ParkedShipInMobileBerthStaysStable(bool againstCrane)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var ui = server.System<SharedUserInterfaceSystem>();

        EntityUid host = default, crane = default, console = default, ship = default, mob = default;
        await server.WaitAssertion(() =>
        {
            (host, crane, console, ship) = SpawnScene(pair, map.MapId, againstCrane);
            mob = em.SpawnEntity("MobHuman", new EntityCoordinates(host, 4.5f, 0.5f));
            Assert.That(ui.TryOpenUi(console, WFDrydockConsoleUiKey.Key, mob), Is.True, "The mob should open the console.");
        });

        // Let the ship and the cranes settle before measuring.
        await server.WaitRunTicks(WarmupTicks);

        var craneTrack = new Track();
        var consoleTrack = new Track();
        var shipTrack = new Track();
        var hostTrack = new Track();
        var uiClosedTicks = 0;

        for (var i = 0; i < MeasuredTicks; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                if (!ui.IsUiOpen(console, WFDrydockConsoleUiKey.Key, mob))
                    uiClosedTicks++;

                Sample(pair, craneTrack, crane);
                Sample(pair, consoleTrack, console);
                Sample(pair, shipTrack, ship);
                Sample(pair, hostTrack, host);
                craneTrack.CraneTicks.Add(em.GetComponent<WFDrydockCraneComponent>(crane).LastModifiedTick.Value);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(uiClosedTicks, Is.EqualTo(0), "The console UI should stay open while a ship sits in the berth.");
            Assert.That(MaxDrift(shipTrack.Positions), Is.LessThanOrEqualTo(MaxJitter), "The parked ship should not jitter.");
            Assert.That(MaxDrift(hostTrack.Positions), Is.LessThanOrEqualTo(MaxJitter), "The host grid should not jitter.");
            Assert.That(MaxDrift(craneTrack.Positions), Is.LessThanOrEqualTo(MaxJitter), "The crane should not be pushed by the parked ship.");
            Assert.That(Changes(shipTrack.Parents), Is.EqualTo(0), "The parked ship should keep its parent.");
            Assert.That(Changes(craneTrack.Parents), Is.EqualTo(0), "The crane should keep its parent.");
            Assert.That(Changes(shipTrack.GridTicks), Is.EqualTo(0), "The parked ship's grid should not be dirtied every tick.");
            Assert.That(Changes(consoleTrack.ComponentSets), Is.EqualTo(0), "The console should not gain or lose components while a ship sits in the berth.");
            Assert.That(Changes(shipTrack.ComponentSets), Is.EqualTo(0), "The parked ship should not gain or lose components while sitting in the berth.");
            Assert.That(Changes(craneTrack.ComponentSets), Is.EqualTo(0), "The crane should not gain or lose components while a ship sits in the berth.");
            Assert.That(Changes(craneTrack.CraneTicks), Is.EqualTo(0), "The crane component should not be dirtied every tick.");
        });

        await pair.CleanReturnAsync();
    }
}
