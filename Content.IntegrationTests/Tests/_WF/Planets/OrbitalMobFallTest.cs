using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets.Flight;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class OrbitalMobFallTest
{
    [Test]
    public async Task OrbitalFallSeversOneArmAndLegAndLeavesSurvivableCrit()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-8, -8), new Vector2i(16, 16));
        var em = pair.Server.EntMan;
        EntityUid mob = default;
        var landed = false;
        await pair.Server.WaitPost(() =>
        {
            mob = em.SpawnEntity("MobHuman", new EntityCoordinates(layers[^1], new Vector2(0.5f)));
            em.RunMapInit(mob, em.GetComponent<MetaDataComponent>(mob));
            pair.Server.System<SharedTransformSystem>().SetCoordinates(mob,
                new EntityCoordinates(layers[^1], new Vector2(3.5f)));
            pair.Server.System<CESharedZLevelsSystem>().SetZVelocity(mob, -2);
        });
        for (var i = 0; i < 150 && !landed; i++)
        {
            await pair.RunTicksSync(2);
            await pair.Server.WaitPost(() =>
                landed = em.GetComponent<TransformComponent>(mob).MapUid == layers[0]
                    && em.GetComponent<MobStateComponent>(mob).CurrentState != MobState.Alive);
        }
        await pair.Server.WaitAssertion(() =>
        {
            var z = em.GetComponent<CEZPhysicsComponent>(mob);
            Assert.That(landed, Is.True, $"map={em.GetComponent<TransformComponent>(mob).MapUid}, ground={layers[0]}, v={z.Velocity}, h={z.LocalPosition}, cached={z.CachedGroundHeight}, state={em.GetComponent<MobStateComponent>(mob).CurrentState}, marked={em.HasComponent<WFOrbitalMobFallComponent>(mob)}");
            Assert.That(em.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Critical));
            var body = pair.Server.System<SharedBodySystem>();
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(1));
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(1));
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(mob), Is.False);
            var audio = em.EntityQueryEnumerator<AudioComponent>();
            var tears = 0;
            while (audio.MoveNext(out _, out var clip))
            {
                var file = clip.FileName;
                if (file.StartsWith("/Audio/Effects/gib")) tears++;
            }
            // The wound system tears each limb off with its own noise; the impact adds no splat on top.
            Assert.That(tears, Is.EqualTo(2), "One orbital impact should play one tear per severed limb.");
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(pair.Server.System<SharedBodySystem>().GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(1));
            Assert.That(pair.Server.System<SharedBodySystem>().GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(1));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A fall from orbit that ends without an impact, set down gently or caught under power, is over: the next
    /// ordinary hard landing costs no limbs.
    /// </summary>
    [Test]
    public async Task AnArrestedOrbitalFallIsForgotten()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        var server = pair.Server;
        var em = server.EntMan;
        var zLevels = server.System<CESharedZLevelsSystem>();
        EntityUid landed = default;
        EntityUid flying = default;

        await server.WaitPost(() =>
        {
            // Came down from orbit and was set down too gently to count as an impact.
            landed = em.SpawnEntity("MobHuman", new EntityCoordinates(layers[0], new Vector2(0.5f)));
            em.AddComponent<WFOrbitalMobFallComponent>(landed).Ground = layers[0];

            // Still in the air, a layer up, with a lit atmospheric jetpack holding it there.
            em.GetComponent<Content.Shared._WF.Planets.WFPlanetLayerComponent>(layers[1]).Gravity = 1f;
            flying = em.SpawnEntity("MobHuman", new EntityCoordinates(layers[1], new Vector2(0.5f)));
            var pack = em.SpawnEntity("WFJetpackAtmospheric", new EntityCoordinates(layers[1], new Vector2(0.5f)));
            Assert.That(server.System<Content.Shared.Inventory.InventorySystem>().TryEquip(flying, pack, "back", force: true), Is.True);
            server.System<Content.Server.Movement.Systems.JetpackSystem>()
                .SetEnabled(pack, em.GetComponent<Content.Shared.Movement.Components.JetpackComponent>(pack), true, flying);
            em.AddComponent<WFOrbitalMobFallComponent>(flying).Ground = layers[0];
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<Content.Shared.Movement.Components.JetpackUserComponent>(flying), Is.True,
                "Precondition: the jetpack did not light.");
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(landed), Is.False,
                "A mob at rest on the ground is still marked as falling from orbit.");
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(flying), Is.False,
                "A mob flying under power is still marked as falling from orbit.");

            // A later, ordinary hard landing.
            zLevels.SetZVelocity(landed, -5f);
            var hit = new CEZLevelHitEvent(5f);
            em.EventBus.RaiseLocalEvent(landed, ref hit);
            var body = server.System<SharedBodySystem>();
            Assert.That(body.GetBodyChildrenOfType(landed, BodyPartType.Arm).Count(), Is.EqualTo(2), "An ordinary fall cost an arm.");
            Assert.That(body.GetBodyChildrenOfType(landed, BodyPartType.Leg).Count(), Is.EqualTo(2), "An ordinary fall cost a leg.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SurfaceImpactWithoutOrbitalFallDoesNotSeverLimbs()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-4, -4), new Vector2i(8, 8));
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var mob = em.SpawnEntity("MobHuman", new EntityCoordinates(layers[0], new Vector2(0.5f)));
            var hit = new CEZLevelHitEvent(1f);
            em.EventBus.RaiseLocalEvent(mob, ref hit);
            var body = pair.Server.System<SharedBodySystem>();
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Arm).Count(), Is.EqualTo(2));
            Assert.That(body.GetBodyChildrenOfType(mob, BodyPartType.Leg).Count(), Is.EqualTo(2));
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(mob), Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
