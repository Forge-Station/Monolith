using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets.Flight;
using Content.Shared.Damage;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class GroundContactTest
{
    [Test]
    public async Task WornFragmentExposesLatticeButRetainsASolidCore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-16, -16), new Vector2i(48, 48));
        var hull = await BuildDebris(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        await server.WaitPost(() =>
        {
            var skid = em.EnsureComponent<WFSkidComponent>(hull);
            skid.Debris = true;
            for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++)
                    skid.TileDamage[new Vector2i(x, y)] = WFFlightSystem.SkidTileThreshold;
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(4f, 0f));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitAssertion(() =>
        {
            var lattice = server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId;
            var solid = 0;
            var frame = 0;
            var tiles = server.System<SharedMapSystem>().GetAllTilesEnumerator(hull, em.GetComponent<MapGridComponent>(hull));
            while (tiles.MoveNext(out var tile))
                if (tile.Value.Tile.TypeId == lattice) frame++;
                else solid++;
            Assert.That(solid, Is.EqualTo(4));
            Assert.That(frame, Is.EqualTo(5));
            Assert.That(em.GetComponent<PhysicsComponent>(hull).Mass, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(em.GetComponent<PhysicsComponent>(hull).LinearVelocity.LengthSquared()), Is.True);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DetachedSectionGrindsTakesLightRockDamageAndStopsItsSounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        var wall = EntityUid.Invalid;
        var rock = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            wall = em.SpawnEntity("WallSolid", new EntityCoordinates(hull, new Vector2(14.5f, 7.5f)));
            rock = em.SpawnEntity("AsteroidRock", new EntityCoordinates(ground, new Vector2(15.5f, 7.5f)));
            em.EnsureComponent<WFSkidComponent>(hull).Debris = true;
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(4f, 0f));
            server.System<CEZLevelsSystem>().WfClearLandingObstacles(hull, reportImpacts: true);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        var loop = EntityUid.Invalid;
        var impact = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(rock), Is.False, "The terrain obstacle should yield to the stronger hull.");
            Assert.That(em.GetComponent<DamageableComponent>(wall).TotalDamage.Float(), Is.InRange(0.1f, 20f));
            var skid = em.GetComponent<WFSkidComponent>(hull);
            Assert.That(skid.TileDamage.Values.Sum(), Is.GreaterThan(0f));
            Assert.That(skid.ScarredTiles.Count, Is.GreaterThan(0));
            Assert.That(skid.Loop, Is.Not.Null);
            Assert.That(skid.ImpactStream, Is.Not.Null);
            loop = skid.Loop!.Value;
            impact = skid.ImpactStream!.Value;
            Assert.That(em.GetComponent<AudioComponent>(loop).FileName, Does.EndWith("ground_grind_loop.ogg"));
            Assert.That(em.GetComponent<AudioComponent>(loop).Params.Volume, Is.GreaterThan(0f));
            Assert.That(em.GetComponent<AudioComponent>(loop).Params.MaxDistance, Is.GreaterThanOrEqualTo(80f));
            var allowed = new[] { 10, 11, 12, 3, 4 }.Select(i => $"/Audio/_WF/Planets/Flight/crash{i}.ogg");
            Assert.That(allowed, Does.Contain(em.GetComponent<AudioComponent>(impact).FileName));
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, Vector2.Zero);
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFSkidComponent>(hull), Is.False);
            Assert.That(em.EntityExists(loop), Is.False);
            Assert.That(em.EntityExists(impact), Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
