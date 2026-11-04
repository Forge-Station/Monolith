#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using ClientAdminRadar = Content.Client._WF.Administration.AdminRadar.AdminRadarSystem;
using ServerAdminRadar = Content.Server._WF.Administration.Systems.AdminRadarSystem;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>
/// True IFF on the admin mass scanner shows every planet site under its real kind and name before anyone has found it;
/// the world map everyone else gets still keeps them back.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerAdminRadar))]
public sealed class AdminRadarSitesTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoiAdminRadarTest
  minCount: 2
  maxCount: 2
  spacing: 44
  edgeMargin: 24
  centreClear: 24
  footprint: 12
  # Nobody identifies a site in this test.
  revealRange: 1
  roads:
    width: 3
  entries:
  - mapPath: /Maps/_WF/PlanetPois/Tests/through_service.yml
    name: wf-poi-asclepiu-research
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Through
    sockets:
    - offset: 0,6
      direction: West
    - offset: 15,6
      direction: East
  - mapPath: /Maps/_WF/PlanetPois/Tests/through_service.yml
    name: wf-poi-asclepiu-clinic
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Roadside
    sockets:
    - offset: 0,6
      direction: West
";

    [Test]
    public async Task TrueIffNamesEveryUnfoundSite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var orbit = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 160);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoiAdminRadarTest");
            var surface = proto.Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            var comp = entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value);
            layers.AddRange(comp.Layers);
            orbit = comp.OrbitMap;
        });

        var ground = layers[0];
        await PlanetBoundsTest.WaitPreloaded(pair, ground);
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        var expected = new Dictionary<Vector2i, string>();
        await server.WaitAssertion(() =>
        {
            var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
            Assert.That(record.Sites, Is.Not.Empty, "The test world got no sites.");
            foreach (var site in record.Sites)
                expected[entMan.GetComponent<WFPoiSignalComponent>(site.Signal).Origin] = site.Name.Id;

            var map = entMan.GetComponent<WFPlanetMapComponent>(orbit);
            Assert.That(map.Sites.All(site => site.Name.Length == 0), Is.True, "The players' map names an unfound site.");

            var world = server.System<ServerAdminRadar>().GetSites().Single(w => w.Orbit == entMan.GetNetEntity(orbit));
            Assert.That(world.Ground, Is.EqualTo(entMan.GetNetEntity(ground)));
            Assert.That(world.Sites.ToDictionary(site => site.Position, site => site.Name), Is.EquivalentTo(expected));
            Assert.That(world.Sites.All(site => site.Kind != WFPlanetMapSiteKind.Unknown), Is.True, "An admin site kept its kind back.");
        });

        var radar = pair.Client.System<ClientAdminRadar>();
        await pair.Client.WaitPost(() => radar.RequestSites(true));
        await pair.RunTicksSync(10);

        var netOrbit = entMan.GetNetEntity(orbit);
        await pair.Client.WaitAssertion(() =>
        {
            var map = pair.Client.EntMan.GetComponent<WFPlanetMapComponent>(pair.Client.EntMan.GetEntity(netOrbit));
            var shown = map.Sites;
            Assert.That(shown.All(site => site.Name.Length == 0), Is.True, "The client's map names an unfound site.");

            var swapped = new Content.Client._WF.Administration.AdminRadar.AdminRadarSwappedSites();
            radar.RevealSites(swapped);
            try
            {
                Assert.That(map.Sites.ToDictionary(site => site.Position, site => site.Name), Is.EquivalentTo(expected),
                    "True IFF did not name every site on the scanner.");
            }
            finally
            {
                radar.RestoreSites(swapped);
            }

            Assert.That(map.Sites, Is.SameAs(shown), "The real sites stayed on the map after the admin's draw.");
        });

        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
