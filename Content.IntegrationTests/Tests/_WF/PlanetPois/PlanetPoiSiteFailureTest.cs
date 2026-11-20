#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>One site that fails to lay costs the world that site, not every other site and road.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class PlanetPoiSiteFailureTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoiSiteFailureTest
  minCount: 2
  maxCount: 2
  spacing: 36
  edgeMargin: 24
  centreClear: 24
  footprint: 12
  entries:
  - mapPath: /Maps/_WF/PlanetPois/Tests/through_service.yml
    name: wf-poi-asclepiu-research
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Isolated
  - mapPath: /Maps/_WF/PlanetPois/Tests/no_such_site.yml
    name: wf-poi-asclepiu-clinic
    footprint: 0,0,16,12
    minCount: 1
    maxCount: 1
    connection: Isolated
";

    [Test]
    public async Task ABrokenSiteIsSkippedAndTheRestStay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        // The broken site logs its error; that error is what this test expects.
        var failureLevel = pair.ServerLogHandler.FailureLevel;
        pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, 128);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoiSiteFailureTest");
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        var ground = layers[0];
        try
        {
            for (var i = 0; i < 300 && !entMan.HasComponent<WFPlanetPreloadedComponent>(ground); i++)
                await pair.RunTicksSync(10);
            await server.WaitPost(() =>
            {
                var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null, "One bad site failed the whole world.");
                Assert.That(record.NetworkReady, Is.True);
                Assert.That(record.Sites.Select(site => site.Name), Is.EquivalentTo(new[] { "wf-poi-asclepiu-research" }));
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
            pair.ServerLogHandler.FailureLevel = failureLevel;
        }
        await pair.CleanReturnAsync();
    }
}
