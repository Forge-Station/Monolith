#nullable enable annotations
using System.Numerics;
using System.Reflection;
using Content.Server._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

/// <summary>
/// Diagnostic for the live shield/projectile crash: the same fixture pair overlaps adjacent
/// chain children. No tractor machinery, beams, or tractor fixture helpers participate.
/// </summary>
public sealed partial class ShipShieldCollisionReproductionTest
{
    [TestCase("ShipMissileASM150Unguided", false)]
    [TestCase("220mmBulletAPHE", false)]
    [TestCase("ShipMissileASM150Unguided", true)]
    [TestCase("220mmBulletAPHE", true)]
    public async Task AdjacentShieldEdgesReproduceDuplicateContactWithoutTractorBeams(string prototype, bool alreadySpent)
    {
        await ReproduceShieldContact(prototype, alreadySpent);
    }

    private static async Task ReproduceShieldContact(string prototype, bool alreadySpent,
        Func<IEntityManager, IMapManager, MapId, EntityUid, EntityUid>? addActiveTractor = null)
    {
        var engine = typeof(SharedPhysicsSystem).Assembly;
        TestContext.Out.WriteLine($"Physics engine: {engine.Location}; configuration: " +
            engine.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);
        // The intentional Debug assertion interrupts a physics step. Never recycle that world.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Destructive = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var maps = pair.Server.ResolveDependency<IMapManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var grid = maps.CreateGridEntity(map.MapId);
            entities.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp,
            [
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(1, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(1, 1), new Tile(1)),
            ]);
            var emitter = entities.SpawnEntity("ShieldGenerator", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
            entities.GetComponent<ApcPowerReceiverComponent>(emitter).Powered = true;
            var generator = entities.GetComponent<ShipShieldEmitterComponent>(emitter);
            entities.System<ShipShieldsSystem>().Update(1.5f);
            Assert.That(generator.Shield, Is.Not.Null, "Use the real powered emitter to generate its normal shield fixtures.");
            var shield = generator.Shield!.Value;
            Assert.That(entities.GetComponent<ShipShieldComponent>(shield).Source, Is.EqualTo(emitter));
            var shieldFixture = entities.GetComponent<FixturesComponent>(shield).Fixtures["shield"];
            Assert.That(shieldFixture.Shape, Is.TypeOf<ChainShape>());
            var chain = (ChainShape) shieldFixture.Shape;
            Assert.That(chain.Count, Is.GreaterThan(16));
            var tractorSource = addActiveTractor?.Invoke(entities, maps, map.MapId, grid.Owner);

            // Between the eight internal polygon corners, the outer chain is clear of
            // internalShield. Centering a normal projectile on this vertex overlaps both
            // adjacent child edges of the SAME shield fixture, without enlarging either.
            var vertex = chain.Vertices[chain.Count / 16];
            var transform = entities.System<SharedTransformSystem>();
            var position = transform.ToMapCoordinates(new EntityCoordinates(shield, vertex));
            var projectile = entities.SpawnEntity(prototype, position);
            var projectileComponent = entities.GetComponent<ProjectileComponent>(projectile);
            projectileComponent.ProjectileSpent = alreadySpent;
            var projectileFixture = entities.GetComponent<FixturesComponent>(projectile).Fixtures["projectile"];
            Assert.That(shieldFixture.Contacts.ContainsKey(projectileFixture), Is.False);
            if (addActiveTractor == null)
                AssertNoTractors(entities, pair.Server.ResolveDependency<IComponentFactory>());

            var physics = entities.System<SharedPhysicsSystem>();
            if (alreadySpent)
            {
                Assert.DoesNotThrow(() => physics.Update(1f / 60f),
                    "PreventCollide rejects every edge before a contact is inserted for an already spent projectile.");
                Assert.That(shieldFixture.Contacts.ContainsKey(projectileFixture), Is.False);
                Assert.That(generator.Damage, Is.Zero);
            }
            else
            {
#if DEBUG
                DebugAssertException? duplicate = null;
                try
                {
                    physics.Update(1f / 60f);
                }
                catch (DebugAssertException exception)
                {
                    duplicate = exception;
                }
                // Broadphase ordering determines whether internalShield consumes the projectile
                // before chain contact creation, or adjacent chain children trigger the assertion.
                // Permit those two audited outcomes; unrelated exceptions still fail the test.
                if (duplicate != null)
                {
                    Assert.That(duplicate.Message, Does.Contain("was already in contact with"));
                    Assert.That(duplicate.Message, Does.Contain("fixture shield"));
                    Assert.That(duplicate.Message, Does.Contain("fixture projectile"));
                    Assert.That(duplicate.Message, Does.Contain(prototype));
                    Assert.That(duplicate.StackTrace, Does.Contain("SharedPhysicsSystem.AddPair"));
                    TestContext.Out.WriteLine($"{prototype}, active tractor={addActiveTractor != null}: exact duplicate reproduced: {duplicate.Message}");
                }
                else
                {
                    TestContext.Out.WriteLine($"{prototype}, active tractor={addActiveTractor != null}: safely absorbed before duplicate insertion.");
                }
#else
                Assert.DoesNotThrow(() => physics.Update(1f / 60f),
                    "Without the Debug-only assertion, the now-spent projectile is rejected by PreventCollide.");
                TestContext.Out.WriteLine($"{prototype}, active tractor={addActiveTractor != null}: safely absorbed in Release.");
#endif
                Assert.That(projectileComponent.ProjectileSpent, Is.True,
                    "The first edge reaches the real shield emitter's deflection handler before the duplicate pair.");
                Assert.That(entities.IsQueuedForDeletion(projectile), Is.True);
                Assert.That(generator.Damage, Is.GreaterThan(0f));
            }

            // Remove the interrupted contact and its proxies before the disposable world shuts down.
            entities.DeleteEntity(projectile);
            entities.DeleteEntity(shield);
            if (tractorSource is { } source)
                entities.DeleteEntity(source);
            entities.DeleteEntity(grid.Owner);
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertNoTractors(IEntityManager entities, IComponentFactory factory)
    {
        // Resolve optional components by name so this diagnostic also compiles before
        // tractor beams were introduced, without depending on their source files.
        foreach (var name in new[] { "TractorBeamEmitter", "TractorBeamConsole", "TractorBeamVisual" })
        {
            if (!factory.TryGetRegistration(name, out var registration))
                continue;
            var query = entities.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out _))
                Assert.That(entities.HasComponent(uid, registration.Type), Is.False, $"Unexpected {name} entity {uid}.");
        }
    }
}
