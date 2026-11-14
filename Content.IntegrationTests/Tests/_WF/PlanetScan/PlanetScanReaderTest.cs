#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._WF.PlanetMap;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetScan;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetScan;

/// <summary>
/// The radar's planet scan finds the world under a point and reads its climate, air, hazards, sites and landing zone,
/// on the server and on a connected client from what the client was sent.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetScanReader))]
public sealed class PlanetScanReaderTest
{
    private const string System = "SystemKyphrus";

    [Test]
    public async Task HoverReadsTheWorldUnderThePoint()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();
        var mapId = testMap.MapId;

        EntityUid verda = default, fervidus = default, carcinoma = default;
        Vector2 verdaCentre = default;
        float verdaRadius = 0f;

        await server.WaitPost(() =>
        {
            var stars = entMan.EnsureComponent<StarSystemMapComponent>(testMap.MapUid);
            server.System<StarSystemMapSystem>().SetSystem((testMap.MapUid, stars), System);

            var reader = new WFPlanetScanReader(entMan, proto);
            foreach (var (body, planet, _) in reader.Bodies(mapId))
            {
                switch (planet.Name)
                {
                    case "Verda":
                        verda = body;
                        PlanetFixture.ApplySurfaceTo(pair, body, "WFSurfaceAsclepiu");
                        break;
                    case "Fervidus":
                        fervidus = body;
                        PlanetFixture.ApplySurfaceTo(pair, body, "WFSurfaceFervidus");
                        break;
                    case "Carcinoma":
                        carcinoma = body;
                        PlanetFixture.ApplySurfaceTo(pair, body, "WFSurfaceCarcinoma");
                        break;
                }
            }

            Assert.That(verda, Is.Not.EqualTo(EntityUid.Invalid), "The sector has no Verda body.");
            Assert.That(fervidus, Is.Not.EqualTo(EntityUid.Invalid), "The sector has no Fervidus body.");
            Assert.That(carcinoma, Is.Not.EqualTo(EntityUid.Invalid), "The sector has no Carcinoma body.");

            // Verda's orbit layer has been mapped: a city, a house, a site nobody has identified and a cargo depot.
            var orbit = server.System<SharedMapSystem>().CreateMap(out _);
            var map = entMan.EnsureComponent<WFPlanetMapComponent>(orbit);
            map.LandingRadius = 24f;
            map.Sites = new List<WFPlanetMapSite>
            {
                new(WFPlanetMapSiteKind.City, new Vector2i(10, 10), new Box2i(0, 0, 20, 20), "wf-test-city"),
                new(WFPlanetMapSiteKind.House, new Vector2i(-40, 5), new Box2i(-44, 0, -36, 10), "wf-test-house"),
                new(WFPlanetMapSiteKind.Unknown, new Vector2i(60, -30), new Box2i(56, -34, 64, -26), string.Empty),
                new(WFPlanetMapSiteKind.Depot, new Vector2i(-80, -60), new Box2i(-120, -90, -40, -30), "wf-planet-cargo-hub-a"),
            };
            entMan.Dirty(orbit, map);

            var sector = entMan.GetComponent<WFSectorPlanetComponent>(verda);
            sector.OrbitMap = entMan.GetNetEntity(orbit);
            entMan.Dirty(verda, sector);

            var surface = proto.Index<WFPlanetSurfacePrototype>("WFSurfaceAsclepiu");
            var verdaPlanet = reader.Bodies(mapId).First(b => b.Body == verda).Planet;
            verdaCentre = verdaPlanet.Position;
            verdaRadius = WFPlanetScanReader.ScopeRadius(verdaPlanet, surface);
        });

        await server.WaitAssertion(() =>
        {
            var reader = new WFPlanetScanReader(entMan, proto);

            Assert.That(verdaRadius, Is.GreaterThan(200f), "Verda is still drawn as a dot.");
            Assert.That(verdaRadius, Is.LessThanOrEqualTo(2000f * WFPlanetScanReader.LimbShare));

            Assert.That(reader.TryFind(mapId, verdaCentre + new Vector2(verdaRadius * 0.6f, 0f), 0f, 0f, out var found), Is.True,
                "A point inside Verda's disc found no world.");
            Assert.That(found, Is.EqualTo(verda));
            Assert.That(reader.TryFind(mapId, verdaCentre + new Vector2(verdaRadius + 60f, 0f), 5f, 0f, out _), Is.False,
                "A point well off Verda's limb still found a world.");
            Assert.That(reader.TryFind(mapId, verdaCentre + new Vector2(verdaRadius + 60f, 0f), 5f, verdaRadius + 70f, out found), Is.True,
                "The minimum drawn radius did not widen the hit.");
            Assert.That(found, Is.EqualTo(verda));

            var reading = new WFPlanetScanReading();
            var pilot = new MapCoordinates(verdaCentre + new Vector2(verdaRadius + 100f, 0f), mapId);
            Assert.That(reader.TryRead(verda, pilot, reading), Is.True);
            AssertVerda(reading, verdaCentre, verdaRadius);
            Assert.That(reading.Range, Is.EqualTo(100f).Within(0.5f));

            Assert.That(reader.TryRead(fervidus, null, reading), Is.True);
            Assert.That(reading.Name, Is.EqualTo("Fervidus"));
            Assert.That(reading.Range, Is.Null);
            Assert.That(reading.Breathable, Is.False);
            Assert.That(reading.Temperature, Is.EqualTo(373.15f).Within(0.01f));
            Assert.That(reading.Hazards, Does.Contain(new LocId("wf-planet-scan-hazard-air")));
            Assert.That(reading.Hazards, Does.Contain(new LocId("wf-planet-scan-hazard-heat")));
            Assert.That(reading.Hazards, Does.Contain(new LocId("wf-planet-scan-hazard-ash-storms")));
            Assert.That(reading.Surveyed, Is.False, "Fervidus has no mapped orbit layer in this test.");

            Assert.That(reader.TryRead(carcinoma, null, reading), Is.True);
            Assert.That(reading.Sanctioned, Is.False);
            Assert.That(reading.Hazards, Does.Contain(new LocId("wf-planet-scan-hazard-unsanctioned")));
            // Its air is mostly carbon dioxide (_WF/PlanetVents).
            Assert.That(reading.Breathable, Is.False, "Carcinoma's carbon dioxide reads as breathable.");
            Assert.That(reading.Hazards, Does.Contain(new LocId("wf-planet-scan-hazard-air")));
        });

        // The client reads the same from what it was sent.
        await pair.RunTicksSync(10);
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var clientEnt = client.EntMan;
            var clientMap = clientEnt.GetComponent<MapComponent>(clientEnt.GetEntity(entMan.GetNetEntity(testMap.MapUid))).MapId;
            var clientVerda = clientEnt.GetEntity(entMan.GetNetEntity(verda));
            var reader = new WFPlanetScanReader(clientEnt, client.ResolveDependency<IPrototypeManager>());

            Assert.That(reader.TryFind(clientMap, verdaCentre + new Vector2(0f, verdaRadius * 0.9f), 0f, 0f, out var found), Is.True,
                "The client found no world under a point inside Verda's disc.");
            Assert.That(found, Is.EqualTo(clientVerda));

            var reading = new WFPlanetScanReading();
            Assert.That(reader.TryRead(clientVerda, null, reading), Is.True);
            AssertVerda(reading, verdaCentre, verdaRadius);
        });

        await pair.CleanReturnAsync();
    }

    private static void AssertVerda(WFPlanetScanReading reading, Vector2 centre, float radius)
    {
        Assert.Multiple(() =>
        {
            Assert.That(reading.Name, Is.EqualTo("Verda"));
            Assert.That(reading.Centre, Is.EqualTo(centre));
            Assert.That(reading.Radius, Is.EqualTo(radius).Within(0.01f));
            Assert.That(reading.Surface, Is.True);
            Assert.That(reading.Sanctioned, Is.True);
            Assert.That(reading.Climate, Is.EqualTo(new LocId("wf-planet-scan-climate-verda")));
            Assert.That(reading.Gravity, Is.EqualTo(1f).Within(0.01f));
            Assert.That(reading.Temperature, Is.EqualTo(288.15f).Within(0.01f));
            Assert.That(reading.Pressure, Is.InRange(90f, 110f));
            Assert.That(reading.Breathable, Is.True);
            Assert.That(reading.Hazards, Does.Contain(new LocId("wf-planet-scan-hazard-thunderstorms")));
            Assert.That(reading.Hazards, Does.Not.Contain(new LocId("wf-planet-scan-hazard-air")));
            Assert.That(reading.Surveyed, Is.True);
            Assert.That(reading.Sites, Is.EqualTo(4));
            Assert.That(reading.SiteKinds[(int) WFPlanetMapSiteKind.City], Is.EqualTo(1));
            Assert.That(reading.SiteKinds[(int) WFPlanetMapSiteKind.House], Is.EqualTo(1));
            Assert.That(reading.SiteKinds[(int) WFPlanetMapSiteKind.Unknown], Is.EqualTo(1));
            Assert.That(reading.SiteKinds[(int) WFPlanetMapSiteKind.Depot], Is.EqualTo(1));
            Assert.That(reading.Depots, Is.EquivalentTo(new[] { new LocId("wf-planet-cargo-hub-a") }), "The scan doesn't name the cargo depot.");
            Assert.That(reading.LandingRadius, Is.EqualTo(24f));
        });
    }
}
