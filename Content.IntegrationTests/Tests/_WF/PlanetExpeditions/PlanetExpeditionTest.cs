#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetExpeditions;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Salvage;
using Content.Server.Station.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetExpeditions;

/// <summary>
/// An expedition console offers a world's site, claiming it arms the site in place with no jump, and clearing the
/// objectives completes the mission and marks the site cleared.
/// </summary>
[TestFixture]
[TestOf(typeof(SalvageSystem))]
public sealed class PlanetExpeditionTest
{
    private const int Radius = 128;
    private const string MapProto = "WFExpeditionTestMap";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: wfPlanetPoiTable
  id: WFPoisExpeditionTest
  minCount: 1
  maxCount: 1
  spacing: 32
  edgeMargin: 56
  centreClear: 24
  footprint: 24
  difficulty: Minor
  entries:
  - dungeon: WFPoiAsclepiuResearch
    name: wf-poi-asclepiu-research

- type: gameMap
  id: {MapProto}
  minPlayers: 0
  mapName: {MapProto}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {MapProto}
      stationProto: StandardNanotrasenStation
      components:
        - type: SalvageExpeditionData
";

    [Test]
    public async Task ASiteIsOfferedArmedAndCleared()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var maps = server.System<SharedMapSystem>();
        var stations = server.System<StationSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await PlanetFixture.EnableFeature(pair);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisExpeditionTest");
            server.CfgMan.SetCVar(PlanetCVars.PlanetExpeditions, true);
            // The site's mobs would fight each other, and Frontier's catcher unit trips a gun debug assert when it fires.
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });

        var layers = new List<EntityUid>();
        var shipMap = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        var console = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var built = networks.BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(built, Is.Not.Null, "The test world failed to build.");
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers);

            // A one-tile hull with the console, as the station the offers go to.
            shipMap = maps.CreateMap(out var shipMapId);
            var ship = maps.CreateGridEntity(shipMapId);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, new Tile(tileDefs["FloorSteel"].TileId));
            console = entMan.SpawnEntity("ComputerSalvageExpedition", new EntityCoordinates(ship.Owner, 0.5f, 0.5f));
            station = stations.InitializeNewStation(proto.Index<GameMapPrototype>(MapProto).Stations["Station"], new[] { ship.Owner }, MapProto);
        });

        var ground = layers[0];

        try
        {
            for (var i = 0; i < 600 && !entMan.HasComponent<WFPlanetPreloadedComponent>(ground); i++)
            {
                await pair.RunTicksSync(10);
            }

            Assert.That(entMan.HasComponent<WFPlanetPreloadedComponent>(ground), Is.True, "The ground never finished preloading.");

            // Offers are due now.
            await server.WaitPost(() => entMan.GetComponent<SalvageExpeditionDataComponent>(station).NextOffer = TimeSpan.Zero);
            await pair.RunTicksSync(5);

            SalvageMissionParams offer = default!;
            WFPlanetPoiSite site = default!;

            await server.WaitPost(() =>
            {
                var data = entMan.GetComponent<SalvageExpeditionDataComponent>(station);
                site = entMan.GetComponent<WFPlanetPoiComponent>(ground).Sites.Single();

                Assert.Multiple(() =>
                {
                    Assert.That(data.Missions, Has.Count.EqualTo(1), "The one site should be the one offer.");
                    offer = data.Missions.Values.Single();
                    Assert.That(offer.WfSite, Is.EqualTo("wf-poi-asclepiu-research"));
                    Assert.That(offer.WfWorld, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(ground).EntityName));
                    Assert.That(offer.Difficulty, Is.EqualTo(Content.Shared.Salvage.DifficultyRating.Minor), "The table's difficulty was not used.");
                    Assert.That(site.Expeditions, Is.True);
                });

                entMan.EventBus.RaiseLocalEvent(console, new ClaimSalvageMessage { Index = offer.Index });
            });

            await pair.RunTicksSync(5);

            List<EntityUid> objectives = new();

            await server.WaitPost(() =>
            {
                var data = entMan.GetComponent<SalvageExpeditionDataComponent>(station);
                var active = entMan.GetComponent<WFPlanetExpeditionComponent>(station).Active;

                Assert.Multiple(() =>
                {
                    Assert.That(data.ActiveMission, Is.EqualTo(offer.Index), "The claim did not take.");
                    Assert.That(data.CanFinish, Is.True);
                    Assert.That(active, Is.Not.Null, "No site mission is underway.");
                    Assert.That(site.ExpeditionClaim, Is.EqualTo(station), "The site is not marked claimed.");
                    Assert.That(entMan.GetComponent<WFPoiSignalComponent>(site.Signal).Revealed, Is.True, "The site's signal was not named.");
                    Assert.That(active!.Objectives, Is.Not.Empty, "The site got no objectives.");

                    foreach (var objective in active.Objectives)
                    {
                        var xform = entMan.GetComponent<TransformComponent>(objective);
                        Assert.That(xform.MapUid, Is.EqualTo(ground), "An objective is not on the world.");
                        var tile = xform.LocalPosition.Floored();
                        Assert.That(tile.X >= site.Bounds.Left && tile.X < site.Bounds.Right && tile.Y >= site.Bounds.Bottom && tile.Y < site.Bounds.Top,
                            Is.True, "An objective is outside the site.");
                    }
                });

                objectives.AddRange(active!.Objectives);
            });

            // No jump: the hull is still where it was.
            Assert.That(entMan.GetComponent<TransformComponent>(console).MapUid, Is.EqualTo(shipMap), "The claim moved the hull.");

            // The clock waits for the crew: nobody is at the site yet.
            await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
            await server.WaitPost(() =>
            {
                var active = entMan.GetComponent<WFPlanetExpeditionComponent>(station).Active!;
                Assert.That(active.Arrived, Is.False, "The clock started before anyone reached the site.");
            });

            // The ship sets down at the site. (Attaching the pair's player there instead races the client's game
            // state on so big a world.)
            await server.WaitPost(() =>
            {
                var middle = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
                var hull = entMan.GetComponent<TransformComponent>(console).GridUid!.Value;
                server.System<SharedTransformSystem>().SetCoordinates(hull, new EntityCoordinates(ground, middle));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1.5f));
            await server.WaitPost(() =>
            {
                var active = entMan.GetComponent<WFPlanetExpeditionComponent>(station).Active!;
                Assert.That(active.Arrived, Is.True, "The ship setting down at the site did not start the clock.");
                Assert.That(active.EndTime - server.Timing.CurTime, Is.GreaterThan(active.Mission.Duration - TimeSpan.FromSeconds(5)),
                    "The clock did not start from the arrival.");
            });

            await server.WaitPost(() =>
            {
                foreach (var objective in objectives)
                    entMan.DeleteEntity(objective);
            });

            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));

            await server.WaitPost(() =>
            {
                var data = entMan.GetComponent<SalvageExpeditionDataComponent>(station);
                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<WFPlanetExpeditionComponent>(station).Active, Is.Null, "The mission did not end.");
                    Assert.That(data.ActiveMission, Is.Zero);
                    Assert.That(data.Cooldown, Is.True);
                    Assert.That(data.CanFinish, Is.False);
                    Assert.That(site.Cleared, Is.True, "The site is not marked cleared.");
                    Assert.That(site.ExpeditionClaim, Is.Null);
                });

                // A cleared site is not offered again.
                data.NextOffer = TimeSpan.Zero;
            });

            await pair.RunTicksSync(5);

            await server.WaitPost(() =>
            {
                var data = entMan.GetComponent<SalvageExpeditionDataComponent>(station);
                Assert.That(data.Missions.Values.All(mission => mission.WfSite == null), Is.True, "The cleared site was offered again.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (entMan.EntityExists(station))
                    stations.DeleteStation(station);
                if (entMan.EntityExists(shipMap))
                    entMan.DeleteEntity(shipMap);
            });
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
