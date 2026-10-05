using System.Numerics;
using Content.Server._WF.Planets.Flight;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Planets.Parachute;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class ParachuteTest
{
    /// <summary>A drop that maims an unprotected mob is survived under a canopy, and the pack is returned.</summary>
    [Test]
    public async Task AParachutedMobLandsUnhurtFromOrbit()
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
            em.EnsureComponent<WFParachutedComponent>(mob);
            pair.Server.System<SharedTransformSystem>().SetCoordinates(mob,
                new EntityCoordinates(layers[^1], new Vector2(3.5f)));
            pair.Server.System<CESharedZLevelsSystem>().SetZVelocity(mob, -2);
        });

        for (var i = 0; i < 600 && !landed; i++)
        {
            await pair.RunTicksSync(2);
            await pair.Server.WaitPost(() =>
                landed = em.GetComponent<TransformComponent>(mob).MapUid == layers[0]
                    && !em.HasComponent<WFParachutedComponent>(mob));
        }

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(landed, Is.True, "The parachuted mob never touched down and shed its canopy.");
            Assert.That(em.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(em.GetComponent<DamageableComponent>(mob).TotalDamage.Float(), Is.EqualTo(0f),
                "A canopy landing still hurt.");
            Assert.That(em.HasComponent<WFOrbitalMobFallComponent>(mob), Is.False);

            var packs = 0;
            var query = em.EntityQueryEnumerator<WFParachuteComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapUid == layers[0])
                    packs++;
            }

            Assert.That(packs, Is.EqualTo(1), "The pack was not left at the landing site.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A packed parachute comes off again into the hands of whoever takes it; an open one does not.</summary>
    [Test]
    public async Task APackedParachuteIsTakenOffByHand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var chutes = server.System<WFParachuteSystem>();
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var crate = em.SpawnEntity("CrateGenericSteel", map.GridCoords);
            var worn = em.EnsureComponent<WFParachutedComponent>(crate);

            worn.Deployed = true;
            Assert.That(chutes.TryRemove((crate, worn), user), Is.False, "An open canopy was unstrapped mid-fall.");
            Assert.That(em.HasComponent<WFParachutedComponent>(crate), Is.True);

            worn.Deployed = false;
            Assert.That(chutes.TryRemove((crate, worn), user), Is.True);
            Assert.That(em.HasComponent<WFParachutedComponent>(crate), Is.False, "The parachute stayed on.");

            var held = false;
            foreach (var item in server.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().EnumerateHeld(user))
            {
                held |= em.HasComponent<WFParachuteComponent>(item);
            }

            Assert.That(held, Is.True, "The pack did not go to the hands of whoever took it off.");
        });

        await pair.CleanReturnAsync();
    }
}
