#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Carcinoma is small and keeps its sites far from the edge, so it is where a site runs out of room first.</summary>
[TestFixture]
public sealed class CarcinomaPoiTest
{
    private const string Surface = "WFSurfaceCarcinoma";
    private const string Table = "WFPoisCarcinoma";
    private const int Radius = 192;

    /// <summary>Every camp the table asks for finds a spot, and the world gets its ruins.</summary>
    [Test]
    public async Task EveryCampAndARuinFit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var holds = new List<Task>();
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, Table);
            var surface = proto.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);

            var starting = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref starting);
        });

        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);

            Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "The sites and roads were not laid.");

            await server.WaitAssertion(() =>
            {
                var record = entMan.GetComponent<WFPlanetPoiComponent>(layers[0]);
                var table = proto.Index<WFPlanetPoiTablePrototype>(Table);
                var pending = record.PendingCamps.GroupBy(camp => camp.Camp).ToDictionary(group => group.Key, group => group.Count());
                TestContext.Out.WriteLine($"sites: {string.Join(", ", record.Sites.Select(s => s.Name))}");

                foreach (var entry in table.Camps)
                {
                    Assert.That(pending.GetValueOrDefault(entry.Camp), Is.GreaterThanOrEqualTo(entry.MinCount),
                        $"{entry.Camp} found no room on {Surface}.");
                }
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
