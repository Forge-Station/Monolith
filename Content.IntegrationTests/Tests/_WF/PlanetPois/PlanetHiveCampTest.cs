#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Aerumna has a small xenonid hive: a core, eggs and resin set out round a site.</summary>
[TestFixture]
public sealed class PlanetHiveCampTest
{
    [Test]
    public async Task AerumnaHasItsHive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await server.WaitPost(() =>
        {
            var surface = prototypes.Index<WFPlanetSurfacePrototype>("WFSurfaceAerumna");
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, (int) surface.Radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "WFSurfaceAerumna", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        await PlanetBoundsTest.WaitPreloaded(pair, layers[0]);
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var record = entities.GetComponent<WFPlanetPoiComponent>(layers[0]);
            Assert.That(record.GenerationError, Is.Null);
            var sites = record.Sites.Where(site => site.Name == "wf-poi-xeno-hive").ToList();
            Assert.That(sites, Has.Count.EqualTo(1), "Aerumna has no xenonid hive.");
            Assert.That(record.PendingCamps, Is.Empty, "The hive was never set out.");

            var site = sites[0];
            var spot = new Vector2((site.Bounds.Left + site.Bounds.Right) / 2f, (site.Bounds.Bottom + site.Bounds.Top) / 2f);
            var camped = (site.Bounds.Width - 2) / 2f;
            var map = entities.GetComponent<TransformComponent>(layers[0]).MapID;
            var counts = new Dictionary<string, int>();
            foreach (var (meta, xform) in entities.EntityQuery<MetaDataComponent, TransformComponent>())
            {
                if (xform.MapID != map || meta.EntityPrototype is not { } proto)
                    continue;
                if (Vector2.Distance(xform.LocalPosition, spot) > camped)
                    continue;
                counts[proto.ID] = counts.GetValueOrDefault(proto.ID) + 1;
            }

            TestContext.Out.WriteLine($"hive at {spot}: " + string.Join(", ", counts.Where(kv => kv.Key.Contains("Xeno")).Select(kv => $"{kv.Key} {kv.Value}")));
            Assert.That(counts.GetValueOrDefault("HiveCoreXeno"), Is.EqualTo(1), "The hive has no core.");
            Assert.That(counts.GetValueOrDefault("WFXenoEggWild"), Is.GreaterThanOrEqualTo(3), "The hive has no eggs.");
            Assert.That(counts.GetValueOrDefault("WallXenoResin") + counts.GetValueOrDefault("WallXenoResinThick"), Is.GreaterThanOrEqualTo(8),
                "The hive has no resin ring.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
