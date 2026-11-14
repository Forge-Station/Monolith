#nullable enable
using System.Linq;
using Content.Server._NF.Shipyard;
using Content.Server._WF.PlanetExpeditions;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetExpeditions;

/// <summary>A bought hull with an expedition console gets landing thrusters; one without keeps its stock thrusters.</summary>
[TestFixture]
[TestOf(typeof(WFExpeditionShipSystem))]
public sealed class ExpeditionShipThrusterTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task BoughtExpeditionShipsLandByDefault(bool console)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        var map = EntityUid.Invalid;
        EntityUid linear = default;
        EntityUid gyro = default;

        await server.WaitPost(() =>
        {
            map = maps.CreateMap(out var mapId);
            var ship = server.System<SharedMapSystem>().CreateGridEntity(mapId);
            var floor = new Tile(tileDefs["FloorSteel"].TileId);
            for (var x = 0; x < 3; x++)
                maps.SetTile(ship.Owner, ship.Comp, new Vector2i(x, 0), floor);

            linear = entMan.SpawnEntity("Thruster", new EntityCoordinates(ship.Owner, 0.5f, 0.5f));
            gyro = entMan.SpawnEntity("Gyroscope", new EntityCoordinates(ship.Owner, 1.5f, 0.5f));
            if (console)
                entMan.SpawnEntity("ComputerSalvageExpedition", new EntityCoordinates(ship.Owner, 2.5f, 0.5f));

            var ev = new ShipBoughtEvent();
            entMan.EventBus.RaiseLocalEvent(ship.Owner, ev);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<ThrusterComponent>(linear).Type, Is.EqualTo(ThrusterType.Linear));
                Assert.That(entMan.HasComponent<WFLandingThrusterComponent>(linear), Is.EqualTo(console),
                    console ? "The expedition ship's thruster was not converted." : "A ship without the console was converted.");
                Assert.That(entMan.HasComponent<WFLandingThrusterComponent>(gyro), Is.False, "The gyroscope was converted.");
            });

            entMan.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }
}
