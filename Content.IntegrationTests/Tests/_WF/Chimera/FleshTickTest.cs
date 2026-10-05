#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Chimera;
using Content.Server.Body.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Melee.Events;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Chimera;

[TestFixture, NonParallelizable]
public sealed class FleshTickTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PlanetLeapDoesNotKillTheTick(bool misses)
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var layers = await PlanetFixture.BuildStandalone(pair);
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid tick = default, host = default;
        await server.WaitPost(() =>
        {
            var ground = layers[0];
            var maps = server.System<SharedMapSystem>();
            var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"];
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                maps.SetTile((ground, em.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(ground)), new Vector2i(x, y), new Tile(floor.TileId));
            host = em.SpawnEntity("MobHuman", new EntityCoordinates(ground, new Vector2(3.3f, 0.5f)));
            tick = em.SpawnEntity("WFMobFleshTick", new EntityCoordinates(ground, new Vector2(0.5f, 0.5f)));
        });
        await pair.RunSeconds(0.25f);
        await server.WaitAssertion(() => Assert.That(em.GetComponent<WFFleshTickComponent>(tick).Leaping, Is.True, "The tick must actually be in flight before testing a miss."));
        if (misses)
            await server.WaitPost(() => em.DeleteEntity(host));
        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            var damage = em.GetComponent<DamageableComponent>(tick);
            Assert.That(server.System<MobStateSystem>().IsAlive(tick), Is.True,
                $"Tick died after jumping: {string.Join(", ", damage.Damage.DamageDict)}");
            Assert.That(damage.TotalDamage.Float(), Is.Zero,
                $"Jump damaged tick: {string.Join(", ", damage.Damage.DamageDict)}");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MeleeBiteLatchesButCannotAttackWhileAttachedOrJustRemoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(Vector2.Zero, mapId);
            var host = em.SpawnEntity("MobHuman", coords);
            var tick = em.SpawnEntity("WFMobFleshTick", coords);
            var hit = new MeleeHitEvent(new() { host }, tick, tick, new DamageSpecifier(), null);
            hit.IsHit = false;
            em.EventBus.RaiseLocalEvent(tick, hit);
            Assert.That(em.GetComponent<WFFleshTickComponent>(tick).Host, Is.Null);
            hit.IsHit = true;
            em.EventBus.RaiseLocalEvent(tick, hit);
            Assert.That(em.GetComponent<WFFleshTickComponent>(tick).Host, Is.EqualTo(host));
            var attempt = new AttemptMeleeEvent();
            em.EventBus.RaiseLocalEvent(tick, ref attempt);
            Assert.That(attempt.Cancelled, Is.True);
            server.System<WFFleshTickSystem>().RemoveFromHost((tick, em.GetComponent<WFFleshTickComponent>(tick)), host);
            attempt = new AttemptMeleeEvent();
            em.EventBus.RaiseLocalEvent(tick, ref attempt);
            Assert.That(attempt.Cancelled, Is.True);
            em.EventBus.RaiseLocalEvent(tick, hit);
            Assert.That(em.GetComponent<WFFleshTickComponent>(tick).Host, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NearbyTickLeapsAndRemovalAllowsTimeToReact()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid map = default, tick = default, host = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            map = maps.CreateMap(out var mapId);
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
            var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"];
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                maps.SetTile(grid, new Vector2i(x, y), new Tile(floor.TileId));
            host = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
            tick = em.SpawnEntity("WFMobFleshTick", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<EmbeddableProjectileComponent>(tick).EmbeddedIntoUid, Is.EqualTo(host), "The real movement/collision path did not latch.");
            var click = new InteractHandEvent(host, tick);
            em.EventBus.RaiseLocalEvent(tick, click);
            Assert.That(click.Handled, Is.True);
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() => Assert.That(em.GetComponent<EmbeddableProjectileComponent>(tick).EmbeddedIntoUid, Is.Null));
        await server.WaitPost(() => em.DeleteEntity(map));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LatchPulseRemovalAndDetachedNoOp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid tick = default;
        EntityUid host = default;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coordinates = new MapCoordinates(Vector2.Zero, mapId);
            host = em.SpawnEntity("MobHuman", coordinates);
            tick = em.SpawnEntity("WFMobFleshTick", coordinates);
            em.GetComponent<WFFleshTickComponent>(tick).NextLeap = TimeSpan.MaxValue;
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var system = server.System<WFFleshTickSystem>();
            var component = em.GetComponent<WFFleshTickComponent>(tick);
            Assert.That(system.TryLatch((tick, component), host), Is.True);
            Assert.That(em.GetComponent<EmbeddableProjectileComponent>(tick).EmbeddedIntoUid, Is.EqualTo(host));

            var bloodstream = em.GetComponent<BloodstreamComponent>(host);
            var solutions = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.ResolveSolution(host,
                bloodstream.ChemicalSolutionName,
                ref bloodstream.ChemicalSolution,
                out var chemicals), Is.True);
            Assert.That(solutions.ResolveSolution(host,
                bloodstream.BloodSolutionName,
                ref bloodstream.BloodSolution,
                out var blood), Is.True);

            var target = component.TargetPart!.Value;
            var (type, symmetry) = server.System<SharedBodySystem>().ConvertTargetBodyPart(target);
            var part = server.System<SharedBodySystem>()
                .GetBodyChildrenOfType(host, type, symmetry: symmetry)
                .First().Id;
            var partDamage = em.GetComponent<DamageableComponent>(part);

            var chemicalBefore = chemicals!.GetReagentQuantity(new ReagentId("NaturalLetoferol", null));
            var bloodBefore = blood!.Volume;
            var damageBefore = partDamage.TotalDamage;

            Assert.That(system.Pulse((tick, component)), Is.True);
            Assert.That(chemicals.GetReagentQuantity(new ReagentId("NaturalLetoferol", null)), Is.EqualTo(chemicalBefore + 1));
            Assert.That(blood.Volume, Is.EqualTo(bloodBefore - 1));
            // Native limb modifiers scale the pulse, but it must injure the chosen attachment site.
            Assert.That(partDamage.TotalDamage, Is.GreaterThan(damageBefore));

            var remove = new InteractHandEvent(host, tick);
            em.EventBus.RaiseLocalEvent(tick, remove);
            Assert.That(remove.Handled, Is.True);
            Assert.That(em.GetComponent<EmbeddableProjectileComponent>(tick).EmbeddedIntoUid, Is.Null);

            chemicalBefore = chemicals.GetReagentQuantity(new ReagentId("NaturalLetoferol", null));
            bloodBefore = blood.Volume;
            damageBefore = partDamage.TotalDamage;
            Assert.That(system.Pulse((tick, component)), Is.False);
            Assert.That(chemicals.GetReagentQuantity(new ReagentId("NaturalLetoferol", null)), Is.EqualTo(chemicalBefore));
            Assert.That(blood.Volume, Is.EqualTo(bloodBefore));
            Assert.That(partDamage.TotalDamage, Is.EqualTo(damageBefore));
            Assert.That(system.TryLatch((tick, component), host), Is.True);
            server.System<MobStateSystem>().ChangeMobState(tick, MobState.Dead);
            Assert.That(em.GetComponent<EmbeddableProjectileComponent>(tick).EmbeddedIntoUid, Is.Null);
            Assert.That(em.GetComponent<EmbeddedContainerComponent>(host).EmbeddedObjects.Contains(tick), Is.False);
            Assert.That(component.DeathSoundPlayed, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChimeraTargetsAreImmuneAndHostCapIsThree()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;

        await server.WaitAssertion(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coordinates = new MapCoordinates(Vector2.Zero, mapId);
            var host = em.SpawnEntity("MobHuman", coordinates);
            var system = server.System<WFFleshTickSystem>();

            server.System<TagSystem>().AddTag(host, new ProtoId<TagPrototype>("Chimera"));
            var immuneTick = em.SpawnEntity("WFMobFleshTick", coordinates);
            var immuneComponent = em.GetComponent<WFFleshTickComponent>(immuneTick);
            Assert.That(system.TryLatch((immuneTick, immuneComponent), host), Is.False);

            server.System<TagSystem>().RemoveTag(host, new ProtoId<TagPrototype>("Chimera"));
            for (var i = 0; i < 3; i++)
            {
                var tick = em.SpawnEntity("WFMobFleshTick", coordinates);
                Assert.That(system.TryLatch((tick, em.GetComponent<WFFleshTickComponent>(tick)), host), Is.True);
            }

            var fourth = em.SpawnEntity("WFMobFleshTick", coordinates);
            Assert.That(system.TryLatch((fourth, em.GetComponent<WFFleshTickComponent>(fourth)), host), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
