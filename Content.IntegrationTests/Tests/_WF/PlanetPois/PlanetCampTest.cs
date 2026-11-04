#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Every world has a Reaver encampment and an ADS control site, guarded on the deck and round the hull.</summary>
[TestFixture]
public sealed class PlanetCampTest
{
    [Test]
    public async Task MerakHasItsCamps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        const int radius = 320;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisMerak");
            var surface = prototypes.Index<WFPlanetSurfacePrototype>("WFSurfaceMerak");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "WFSurfaceMerak", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        // No try/finally: a failed check leaves the server mid-assertion, and tearing down then would throw over it.
        await PlanetBoundsTest.WaitPreloaded(pair, layers[0]);
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var record = entities.GetComponent<WFPlanetPoiComponent>(layers[0]);
            Assert.That(record.GenerationError, Is.Null);
            foreach (var (name, camp, faction) in new[]
                     {
                         ("wf-poi-reaver-encampment", "WFCampReavers", "PirateNF"),
                         ("wf-poi-ads-control-site", "WFCampAds", "WFAds"),
                     })
            {
                Assert.That(record.Sites.Count(site => site.Name == name), Is.EqualTo(1), $"Merak has no {name}.");
                var guards = entities.EntityQuery<WFCrewComponent>().Where(crew => crew.Group.StartsWith(camp + "-")).ToList();
                Assert.That(guards, Has.Count.GreaterThanOrEqualTo(4), $"The {camp} is barely guarded.");
                var factions = server.System<Content.Shared.NPC.Systems.NpcFactionSystem>();
                Assert.That(guards.All(guard => factions.IsMember(guard.Owner, faction)), Is.True, $"A {camp} guard is on the wrong side.");
                var outside = guards.Count(guard => entities.GetComponent<TransformComponent>(guard.Owner).GridUid == layers[0]);
                Assert.That(outside, Is.GreaterThanOrEqualTo(1), $"No {camp} guard stands outside the hull.");
                Assert.That(record.PendingCamps, Is.Empty, "The camps' outside guards were never posted.");
            }

            // Reavers and crazies camp where the road goes by: just off the shoulder, the road outside their ring.
            foreach (var name in new[] { "wf-poi-reaver-encampment", "wf-poi-crazy-camp" })
            foreach (var site in record.Sites.Where(site => site.Name == name))
            {
                var spot = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
                var camped = (site.Bounds.Width - 2) / 2f;
                var road = record.Centreline.Min(tile => Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), spot));
                TestContext.Out.WriteLine($"{name} at {spot}: ring {camped:F1}, {road:F1} from the road's middle");
                Assert.That(road, Is.LessThanOrEqualTo(camped + 8f), $"The {name} is out in the open, not by a road.");
                Assert.That(record.RoadTiles.Concat(record.ShoulderTiles)
                    .Any(tile => Vector2.Distance(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), spot) < camped - 1f), Is.False,
                    $"The {name}'s ring covers the road.");
            }

            // The camps' own dressing: a fire at the Reavers', floodlights at both.
            var map = entities.GetComponent<TransformComponent>(layers[0]).MapID;
            int Count(string id) => entities.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID == id && entities.GetComponent<TransformComponent>(meta.Owner).MapID == map);
            Assert.That(Count("Bonfire"), Is.GreaterThanOrEqualTo(1), "The Reavers have no campfire.");
            Assert.That(Count("AU14TallFloodlight"), Is.GreaterThanOrEqualTo(3), "The camps aren't lit.");

            // No two tents, fires or other footprinted props of a camp lie on each other.
            var footprints = prototypes.EnumeratePrototypes<WFPlanetCampPrototype>()
                .SelectMany(camp => camp.Props)
                .SelectMany(prop => prop.Ids.Select(id => (id.Id, Box: prop.Footprint ?? new Box2(-0.5f, -0.5f, 0.5f, 0.5f))))
                .GroupBy(pair => pair.Id)
                .ToDictionary(group => group.Key, group => group.First().Box);
            var placed = entities.EntityQuery<MetaDataComponent>()
                .Where(meta => meta.EntityPrototype is { } proto && footprints.ContainsKey(proto.ID)
                               && entities.GetComponent<TransformComponent>(meta.Owner).ParentUid == layers[0])
                .Select(meta => (Id: meta.EntityPrototype!.ID, Position: entities.GetComponent<TransformComponent>(meta.Owner).LocalPosition))
                .ToList();
            var tents = placed.Count(pair => pair.Id.StartsWith("AU14PropTent"));
            TestContext.Out.WriteLine($"{tents} tents on Merak");
            Assert.That(tents, Is.GreaterThanOrEqualTo(2), "Merak's camps have no tents to check.");
            for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
            {
                if (!placed[i].Id.StartsWith("AU14PropTent") && !placed[j].Id.StartsWith("AU14PropTent"))
                    continue;
                var a = footprints[placed[i].Id].Translated(placed[i].Position).Enlarged(-0.01f);
                var b = footprints[placed[j].Id].Translated(placed[j].Position).Enlarged(-0.01f);
                Assert.That(a.Intersects(b), Is.False,
                    $"{placed[i].Id} at {placed[i].Position} overlaps {placed[j].Id} at {placed[j].Position}.");
            }

            // Crazy camps: a fire with tents, and strangers round it; more strangers roam the open ground.
            Assert.That(record.Sites.Count(site => site.Name == "wf-poi-crazy-camp"), Is.InRange(1, 2), "Merak has no crazy camp.");
            var strangers = entities.EntityQuery<Content.Server._WF.Strangers.WFStrangerComponent>()
                .Where(stranger => entities.GetComponent<TransformComponent>(stranger.Owner).MapID == map)
                .ToList();
            Assert.That(strangers, Has.Count.GreaterThanOrEqualTo(6), "Merak should have its strangers, at camp and wandering.");
            var campers = strangers.Where(stranger => stranger.Home is { } home
                && entities.GetComponent<MetaDataComponent>(home).EntityPrototype?.ID == "WFStrangerCampHome").ToList();
            Assert.That(campers, Has.Count.GreaterThanOrEqualTo(2), "The crazy camp's strangers don't live there.");
            Assert.That(campers.All(stranger => entities.GetComponent<MetaDataComponent>(stranger.Owner).EntityPrototype?.ID
                is "WFStrangerMugger" or "WFStrangerPreacher" or "WFStrangerBeggar"), Is.True, "Only camp types live at a camp.");
            Assert.That(strangers.Any(stranger => stranger.Home == null), Is.True, "Some strangers should roam.");

            // Working vehicles parked about, and a Reaver patrol driving the roads there and back.
            var parked = entities.EntityQuery<Content.Shared._WF.Vehicles.Core.VehicleEnterComponent>()
                .Count(enter => entities.GetComponent<TransformComponent>(enter.Owner).MapID == map
                                && !entities.HasComponent<Content.Server._WF.NpcVehicleCrew.WFVehicleCrewComponent>(enter.Owner));
            Assert.That(parked, Is.GreaterThanOrEqualTo(2), "Merak has no working vehicles left about.");
            var patrols = entities.EntityQuery<Content.Server._WF.NpcVehicleCrew.WFVehicleCrewComponent>()
                .Where(crew => crew.Prototype == "WFVehicleCrewReaverRoadPatrol").ToList();
            Assert.That(patrols, Has.Count.EqualTo(1), "Merak's roads have no Reaver patrol.");
            Assert.That(patrols[0].Waypoints, Has.Count.GreaterThanOrEqualTo(4), "The patrol has no stretch of road to drive.");
            Assert.That(patrols[0].Waypoints.All(point =>
                {
                    var ground = entities.GetComponent<MapGridComponent>(layers[0]);
                    var tile = server.System<SharedMapSystem>().WorldToTile(layers[0], ground, point.Position);
                    return record.RoadTiles.Contains(tile);
                }), Is.True, "A patrol point is off the road.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
