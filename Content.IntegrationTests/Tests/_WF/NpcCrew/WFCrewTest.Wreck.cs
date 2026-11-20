#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A ship that loses most of its hull is disabled once that has held for 10 s, crew or no; one that loses a few tiles is not.</summary>
    [Test]
    public async Task ShipThatLosesMostOfItsHullIsDisabled()
    {
        var ship = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var scratched = await CreateDeck(new Vector2(40, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WFCrewShipStatusSystem>();
            SEntMan.EnsureComponent<ShuttleComponent>(ship);
            SEntMan.EnsureComponent<ShuttleComponent>(scratched);
            Assert.That(status.IsDisabled(ship), Is.False, "Whole, the hull is judged and passes.");
            Assert.That(status.IsDisabled(scratched), Is.False);
            Chip(ship, 0.5f);
            Chip(scratched, 0.08f);
        });

        await WaitUntil(() => Server.System<WFCrewShipStatusSystem>().IsDisabled(ship), 2400, () => "a hull in pieces never counted as disabled");
        await RunTicks(Ticks(5));
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WFCrewShipStatusSystem>();
            Assert.That(status.IsDisabled(scratched), Is.False, "A few lost tiles are no wreck.");
            Assert.That(status.ShouldDisengage(scratched, ship), Is.True, "Attackers leave a wreck alone.");
        });
    }

    private void Chip(EntityUid grid, float share)
    {
        var maps = Server.System<SharedMapSystem>();
        var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
        var tiles = maps.GetAllTiles(grid, gridComp).ToList();
        var count = (int) (tiles.Count * share) + 1;
        maps.SetTiles((grid, gridComp), tiles.Take(count).Select(tile => (tile.GridIndices, Tile.Empty)).ToList());
    }

    /// <summary>A deterring crew answers a ship that fires on it out to three times its range, and no farther; an assigned target is never given up for distance.</summary>
    [Test]
    public async Task DeterringCrewChasesAShooterOnlyThreeTimesItsRange()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 3, gravity: true);
        var near = await CreateDeck(new Vector2(250, 0), 3, gravity: true);
        var far = await CreateDeck(new Vector2(400, 0), 3, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WFCrewShipStatusSystem>();
            status.SetPolicy(deck, WFCrewDisengage.Deter, 100f);
            Assert.That(status.ShouldDisengage(deck, near), Is.True, "A bystander that far is let be.");
            Assert.That(status.ShouldDisengage(deck, near, range: false, shooter: true), Is.False, "A shooter at 244 m is answered.");
            Assert.That(status.ShouldDisengage(deck, far, range: false, shooter: true), Is.True, "At 394 m it is let go.");
            Assert.That(status.ShouldDisengage(deck, far, range: false), Is.False, "An assigned target is kept at any range.");
        });
    }

    /// <summary>A pilot looking for a helm takes the console square to the hull over a nearer one turned away, which would fly the ship stern first.</summary>
    [Test]
    public async Task PilotPrefersAHelmSquareToTheHull()
    {
        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        EntityUid backward = default, forward = default;
        await Server.WaitPost(() =>
        {
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            backward = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            forward = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(7.5f, 7.5f)));
            Server.System<SharedTransformSystem>().SetLocalRotation(backward, Angle.FromDegrees(180));
        });
        await RunTicks(Ticks(3));
        await Server.WaitAssertion(() =>
        {
            var crewSystem = Server.System<WFCrewSystem>();
            var pilots = Server.System<WFPilotDutySystem>();
            var pilot = crewSystem.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(3.5f, 4.5f)), "aligned")!.Value;

            Assert.That(pilots.TryFindHelm(pilot, out var console), Is.True);
            Assert.That(console, Is.EqualTo(forward), "The nearer console is turned the wrong way.");

            Server.System<SharedTransformSystem>().SetLocalRotation(forward, Angle.FromDegrees(180));
            Assert.That(pilots.TryFindHelm(pilot, out console), Is.True);
            Assert.That(console, Is.EqualTo(backward), "With both turned away, the nearer one does.");
        });
    }
}
