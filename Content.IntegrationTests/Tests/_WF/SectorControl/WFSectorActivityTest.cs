#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.SectorControl;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Activity: only crewed player ships that move count, a jump marks both ends, stations and encounter ships never do.</summary>
[TestOf(typeof(WFSectorActivitySystem))]
public sealed class WFSectorActivityTest : WFSectorTestBase
{
    private static readonly WFSectorCell Start = new(4, -2);
    private static readonly WFSectorCell Away = new(-4, 2);

    /// <summary>The first sighting is a baseline, a parked ship counts for nothing and a moving one marks its cell.</summary>
    [Test]
    public async Task OnlyMovingCrewedShipsMarkTheirCell()
    {
        await StartRound();
        EntityUid ship = default;
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            ship = SpawnGrid(CentreOf(Start), "Test Hauler");
            Board(ship);

            Assert.That(activity.CrewedShips(MapData.MapId), Is.EquivalentTo(new[] { ship }));
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, Start), Is.Null, "The first sighting is only a baseline.");
        });
        await RunTicks(5);
        TimeSpan marked = default;
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, Start), Is.Null, "A parked ship holds nothing.");

            Place(ship, CentreOf(Start) + new Vector2(400f, 0f));
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, Start), Is.Not.Null, "A ship that moved marks its cell.");
            marked = activity.LastActivity(MapData.MapId, Start)!.Value;
            Assert.That(activity.ActiveShips(MapData.MapId, Start), Is.EquivalentTo(new[] { ship }));
            Assert.That(activity.ActiveShips(MapData.MapId, Away), Is.Empty);
            Assert.That(activity.IsQuiet(MapData.MapId, Start, marked), Is.False);
            Assert.That(activity.IsQuiet(MapData.MapId, Start, marked + TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(activity.IsQuiet(MapData.MapId, Away, TimeSpan.Zero), Is.True, "A cell never flown is quiet.");
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, Start), Is.EqualTo(marked), "Sitting still adds nothing.");
            Assert.That(activity.ActiveShips(MapData.MapId, Start), Is.Empty);

            // A jump of more than a ring marks the cell it left as well.
            Place(ship, CentreOf(Away));
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, Away)!.Value, Is.GreaterThan(marked));
            Assert.That(activity.LastActivity(MapData.MapId, Start)!.Value, Is.GreaterThan(marked), "The cell it jumped from is marked too.");
            Assert.That(activity.ActiveShips(MapData.MapId, Away), Is.EquivalentTo(new[] { ship }));
        });
    }

    /// <summary>A jump whose flight a scan lands in the middle of still marks both ends, because the ship's track outlives the scan it missed.</summary>
    [Test]
    public async Task JumpMarksBothEndsWhenAScanLandsMidFlight()
    {
        await StartRound();
        EntityUid ship = default;
        EntityUid other = default;
        MapId otherMap = default;
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            ship = SpawnGrid(CentreOf(Start));
            Board(ship);
            activity.Scan();

            // In FTL the ship is on another map, so this scan does not see it.
            other = Server.System<SharedMapSystem>().CreateMap(out otherMap);
            Server.System<SharedTransformSystem>().SetCoordinates(ship, new EntityCoordinates(other, Vector2.Zero));
            activity.Scan();
            Assert.That(activity.CrewedShips(MapData.MapId), Is.Empty);
            Place(ship, CentreOf(Away));
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, Away), Is.Not.Null, "The cell it landed in is marked.");
            Assert.That(activity.LastActivity(MapData.MapId, Start), Is.Not.Null, "The cell it left is marked.");
            Assert.That(activity.ActiveShips(MapData.MapId, Away), Is.EquivalentTo(new[] { ship }));
            Server.System<SharedMapSystem>().DeleteMap(otherMap);
        });
    }

    /// <summary>A moved hop into a neighbouring cell marks only the cell it arrived in.</summary>
    [Test]
    public async Task ShortHopMarksOnlyTheArrivalCell()
    {
        await StartRound();
        EntityUid ship = default;
        var neighbour = Start.Neighbour(0);
        await Server.WaitAssertion(() =>
        {
            ship = SpawnGrid(CentreOf(Start));
            Board(ship);
            Server.System<WFSectorActivitySystem>().Scan();
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            Place(ship, CentreOf(neighbour));
            activity.Scan();
            Assert.That(activity.LastActivity(MapData.MapId, neighbour), Is.Not.Null);
            Assert.That(activity.LastActivity(MapData.MapId, Start), Is.Null);
        });
    }

    /// <summary>Stations and outposts, encounter ships and the player on foot count for nothing; a deeded ship does, even a station's.</summary>
    [Test]
    public async Task StationsAndEncounterShipsAreNotCounted()
    {
        await StartRound();
        EntityUid ship = default;
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            var map = MapData.MapId;
            ship = SpawnGrid(CentreOf(Start));
            Board(ship);
            Assert.That(activity.CrewedShips(map), Is.EquivalentTo(new[] { ship }));

            SEntMan.EnsureComponent<StationMemberComponent>(ship);
            Assert.That(activity.CrewedShips(map), Is.Empty, "People living on a station keep its cell no busier.");

            SEntMan.EnsureComponent<ShuttleDeedComponent>(ship);
            Assert.That(activity.CrewedShips(map), Is.EquivalentTo(new[] { ship }), "A ship someone holds a deed to is a ship.");
            SEntMan.RemoveComponent<ShuttleDeedComponent>(ship);
            SEntMan.RemoveComponent<StationMemberComponent>(ship);

            SEntMan.EnsureComponent<WFEncounterGridComponent>(ship);
            Assert.That(activity.CrewedShips(map), Is.Empty, "An encounter's ships are not players.");
            SEntMan.RemoveComponent<WFEncounterGridComponent>(ship);

            Disembark();
            Assert.That(activity.CrewedShips(map), Is.Empty, "Nobody aboard, nobody flying.");
            Board(ship);
            Assert.That(activity.CrewedShips(map), Is.EquivalentTo(new[] { ship }));
            Assert.That(activity.CrewedShips(MapId.Nullspace), Is.Empty);
        });
    }
}
