using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class KyphrusPlanetsTest
{
    [TestCase("PlanetFervidus", "Fervidus")]
    [TestCase("PlanetMerak", "Merak")]
    [TestCase("PlanetAerumna", "Aerumna")]
    [TestCase("PlanetThrascias", "Thrascias")]
    [TestCase("WFPlanetCarcinoma", "Carcinoma")]
    public async Task NewWorldBuilds(string planet, string name)
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var registry = server.System<WFPlanetRegistrySystem>();
            Assert.That(registry.TryGetSurface(planet, out var surface), Is.True,
                "The sector body must automatically resolve its surface.");
            Assert.That(surface!.BuildAtRoundStart, Is.True);
            Assert.That(surface.CloudLayer, Is.False, "A cloud deck would hide the surface from orbit.");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, name, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            Assert.That(layers, Has.Count.EqualTo(5));
            var orbit = em.GetComponent<WFOrbitLayerComponent>(layers[^1]);
            Assert.That(orbit.RadarLayers, Is.Not.Empty);
            Assert.That(orbit.RadarSeed, Is.EqualTo(surface.Seed));
            var radar = server.System<WFPlanetRadarSystem>();
            for (var x = -100; x <= 100; x += 25)
            for (var y = -100; y <= 100; y += 25)
                Assert.That(radar.Sample(orbit, new Vector2(x, y)), Is.Not.Null,
                    "The natural surface must not have noise gaps.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
