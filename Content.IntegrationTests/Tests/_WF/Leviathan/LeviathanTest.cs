#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Leviathan;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Leviathan;

/// <summary>Aerumna's chromite leviathan and the space whale it came from.</summary>
[TestFixture]
public sealed class LeviathanTest
{
    [Test]
    public async Task TheBodyFollowsTheHeadAndTheBiteSparesWalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid worm = default, whale = default;
        await server.WaitPost(() =>
        {
            worm = entMan.SpawnEntity("WFAerumnaLeviathan", new MapCoordinates(Vector2.Zero, map.MapId));
            whale = entMan.SpawnEntity("SpaceLeviathan", new MapCoordinates(new Vector2(100, 0), map.MapId));
        });
        await pair.RunTicksSync(5);

        var xforms = server.System<SharedTransformSystem>();
        await server.WaitPost(() => xforms.SetWorldPosition(worm, new Vector2(4, 3)));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var tail = entMan.GetComponent<WFTailedEntityComponent>(worm);
            Assert.That(tail.Segments, Has.Count.EqualTo(10));
            var ahead = xforms.GetWorldPosition(worm);
            foreach (var segment in tail.Segments)
            {
                var position = xforms.GetWorldPosition(segment);
                Assert.That(Vector2.Distance(position, ahead), Is.EqualTo(tail.Spacing).Within(0.01f), "Each segment should keep its spacing.");
                ahead = position;
            }

            var bite = entMan.GetComponent<MeleeWeaponComponent>(worm).Damage.DamageDict;
            Assert.That(bite.ContainsKey("Structural"), Is.False, "The leviathan must not chew through hulls.");
            Assert.That(entMan.GetComponent<WFTailedEntityComponent>(whale).Segments, Has.Count.EqualTo(65));

            entMan.DeleteEntity(worm);
            Assert.That(tail.Segments, Is.Empty, "The body goes with the head.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AerumnaHasLeviathans()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        const int radius = 256;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, radius);
            server.CfgMan.SetCVar(PlanetCVars.Preload, true);
            server.CfgMan.SetCVar(PlanetCVars.PreloadBudget, 1000f);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisAerumna");
            var surface = prototypes.Index<WFPlanetSurfacePrototype>("WFSurfaceAerumna");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "WFSurfaceAerumna", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
        });
        try
        {
            // Roamers come out once the whole ground exists.
            await PlanetBoundsTest.WaitPreloaded(pair, layers[0]);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.HasComponent<WFPlanetPreloadedComponent>(layers[0]), Is.True, "Aerumna never finished loading.");
                Assert.That(entities.GetComponent<WFPlanetPoiComponent>(layers[0]).GenerationError, Is.Null);
                var map = entities.GetComponent<TransformComponent>(layers[0]).MapID;
                var worms = entities.EntityQuery<WFTailedEntityComponent, MobStateComponent>()
                    .Count(pairs => entities.GetComponent<TransformComponent>(pairs.Item1.Owner).MapID == map);
                Assert.That(worms, Is.InRange(1, 2), "Aerumna should have its leviathans.");
            });
        }
        finally { await PlanetFixture.Teardown(pair, layers); }
        await pair.CleanReturnAsync();
    }
}
