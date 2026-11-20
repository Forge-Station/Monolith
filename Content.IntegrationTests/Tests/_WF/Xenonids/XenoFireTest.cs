#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Xenonids.Fire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Weeds burn away and spread fire, xenos pat out burning structures, and RMC's caste fire immunities hold.</summary>
[TestFixture]
public sealed class XenoFireTest
{
    private static async Task<TestMapData> AirMap(TestPair pair, int width)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var maps = server.EntMan.System<SharedMapSystem>();
            for (var x = 0; x < width; x++)
                maps.SetTile(map.Grid, new Vector2i(x, 0), map.Tile.Tile);

            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));
        });
        return map;
    }

    private static void Light(IEntityManager entities, EntityUid uid, float stacks)
    {
        var flammable = entities.GetComponent<FlammableComponent>(uid);
        var fire = entities.System<FlammableSystem>();
        fire.SetFireStacks(uid, stacks, flammable);
        fire.Ignite(uid, uid, flammable);
    }

    [Test]
    public async Task BurningWeedsSpreadAndBurnAway()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await AirMap(pair, 4);

        var weeds = new EntityUid[4];
        await server.WaitPost(() =>
        {
            for (var x = 0; x < weeds.Length; x++)
                weeds[x] = entities.SpawnEntity("XenoWeeds", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => Light(entities, weeds[0], 1));
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<FlammableComponent>(weeds[0]).OnFire, "The weeds should catch fire."));

        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FlammableComponent>(weeds[1]).OnFire, "The fire should spread to the next weeds.");
            Assert.That(entities.GetComponent<FlammableComponent>(weeds[3]).OnFire, Is.False, "The fire spreads one tile at a time.");
        });

        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
            Assert.That(entities.Deleted(weeds[0]), "The first weeds should burn away within eight seconds."));

        await pair.CleanReturnAsync();
    }

    [TestCase("XenoWeeds")]
    [TestCase("XenoHiveWeeds")]
    [TestCase("XenoEgg")]
    public async Task DronePatsOutBurningStructure(string proto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await AirMap(pair, 2);

        EntityUid drone = default, target = default;
        await server.WaitPost(() =>
        {
            target = entities.SpawnEntity(proto, new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            drone = entities.SpawnEntity("CMXenoDrone", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var flammable = entities.GetComponent<FlammableComponent>(target);
            Light(entities, target, MathF.Min(flammable.MaximumFireStacks, 3));
            Assert.That(flammable.OnFire, $"The {proto} should catch fire.");

            Assert.That(entities.System<SharedInteractionSystem>().InteractHand(drone, target), $"The drone should pat the {proto}.");
            Assert.That(flammable.OnFire, Is.False, $"One pat puts out the {proto}.");
            Assert.That(flammable.FireStacks, Is.EqualTo(0f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueenCantBeLitAndRavagerBurnsSlower()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await AirMap(pair, 12);

        EntityUid queen = default, ravager = default, control = default;
        await server.WaitPost(() =>
        {
            queen = entities.SpawnEntity("CMXenoQueen", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            ravager = entities.SpawnEntity("CMXenoRavager", new EntityCoordinates(map.Grid, 5.5f, 0.5f));
            control = entities.SpawnEntity("CMXenoRavager", new EntityCoordinates(map.Grid, 10.5f, 0.5f));
            entities.RemoveComponent<RMCImmuneToFireTileDamageComponent>(control);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Light(entities, queen, 5);
            Assert.That(entities.GetComponent<FlammableComponent>(queen).OnFire, Is.False, "The queen can't be set alight.");

            entities.System<FlammableSystem>().AdjustFireStacks(queen, 5, ignite: true);
            Light(entities, ravager, 5);
            Light(entities, control, 5);
        });

        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<FlammableComponent>(queen).OnFire, Is.False, "A fire forced onto the queen goes out.");

            var ravagerDamage = entities.GetComponent<DamageableComponent>(ravager).TotalDamage;
            var controlDamage = entities.GetComponent<DamageableComponent>(control).TotalDamage;
            Assert.That(controlDamage, Is.GreaterThan(FixedPoint2.Zero), "A burning ravager should take damage.");
            Assert.That(ravagerDamage.Float(), Is.EqualTo(controlDamage.Float() / 2).Within(controlDamage.Float() * 0.1f),
                "The ravager's tile fire immunity halves its burn damage.");
        });

        await pair.CleanReturnAsync();
    }
}
