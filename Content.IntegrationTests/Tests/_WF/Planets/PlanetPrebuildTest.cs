#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>A network built ahead of its body is taken over by the body that spawns at its centre under its name, and only that one.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetNetworkSystem))]
public sealed class PlanetPrebuildTest
{
    [Test]
    public async Task TheBodyAtTheCentreAdoptsThePrebuiltNetwork()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var meta = server.System<MetaDataSystem>();

        await PlanetFixture.EnableFeature(pair);
        var map = await pair.CreateTestMap();
        var prebuilt = EntityUid.Invalid;
        var layers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            Assert.That(networks.Prebuild(surface, new Vector2(300, -200), "Asclepiu"), Is.True, "The network failed to prebuild.");

            var query = entMan.EntityQueryEnumerator<WFPlanetNetworkComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                prebuilt = uid;
                layers.AddRange(comp.Layers);
            }

            Assert.That(entMan.GetComponent<WFPlanetNetworkComponent>(prebuilt).Planet, Is.Null, "A prebuilt network has a body.");
        });

        await server.WaitRunTicks(1);

        try
        {
            await server.WaitPost(() =>
            {
                // A body elsewhere under the same name builds its own.
                var stranger = entMan.SpawnEntity(PlanetFixture.PlanetBodyProto, new MapCoordinates(new Vector2(-300, 200), map.MapId));
                meta.SetEntityName(stranger, "Asclepiu");
                PlanetFixture.ApplySurfaceTo(pair, stranger, PlanetFixture.SurfaceProto);
                var strangerSector = new Entity<WFSectorPlanetComponent>(stranger, entMan.GetComponent<WFSectorPlanetComponent>(stranger));

                Assert.That(networks.TryBuildNetwork(strangerSector, out var own), Is.True, "The body elsewhere failed to build.");
                Assert.That(own, Is.Not.EqualTo(prebuilt), "A body elsewhere took the prebuilt network.");
                layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(own).Layers);

                // The body at the centre takes the prebuilt one.
                var body = entMan.SpawnEntity(PlanetFixture.PlanetBodyProto, new MapCoordinates(new Vector2(300, -200), map.MapId));
                meta.SetEntityName(body, "Asclepiu");
                PlanetFixture.ApplySurfaceTo(pair, body, PlanetFixture.SurfaceProto);
                var sector = new Entity<WFSectorPlanetComponent>(body, entMan.GetComponent<WFSectorPlanetComponent>(body));

                Assert.That(networks.TryBuildNetwork(sector, out var adopted), Is.True, "The body at the centre failed to build.");

                var comp = entMan.GetComponent<WFPlanetNetworkComponent>(adopted);
                var orbit = entMan.GetComponent<WFOrbitLayerComponent>(comp.OrbitMap);

                Assert.Multiple(() =>
                {
                    Assert.That(adopted, Is.EqualTo(prebuilt), "The body at the centre built a fresh network instead of adopting.");
                    Assert.That(comp.Planet, Is.EqualTo(body), "The adopted network does not know its body.");
                    Assert.That(orbit.Planet, Is.EqualTo(entMan.GetNetEntity(body)), "The orbit layer does not know its body.");
                    Assert.That(sector.Comp.Network, Is.EqualTo(entMan.GetNetEntity(prebuilt)), "The body does not record the adopted network.");
                    Assert.That(networks.ClearPrebuilt(), Is.Zero, "An adopted network was still listed as prebuilt.");
                });
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
