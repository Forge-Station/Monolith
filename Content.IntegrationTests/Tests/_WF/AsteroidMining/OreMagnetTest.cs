using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server.Power.Components;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.AsteroidMining;

/// <summary>Tests the ore magnet: it locks onto ore it can see, pulls it in after the lock holds, and walls block it.</summary>
[TestFixture]
public sealed class OreMagnetTest
{
    private static readonly Vector2 ShipPos = new(300f, 0f);

    /// <summary>A ship with a powered, switched-on ore magnet and an ore some tiles away, with a wall between them if asked.</summary>
    private static (EntityUid Magnet, EntityUid Ore) SetUp(TestPair pair, MapId map, bool wall)
    {
        var em = pair.Server.EntMan;
        var ship = DrydockTest.CreateShip(pair, map, ShipPos);
        DrydockTest.SetFloor(pair, ship, 10);
        var magnet = DrydockTest.SpawnAnchored(pair, ship, "MachineOreMagnet", new Vector2i(1, 1));
        em.GetComponent<ApcPowerReceiverComponent>(magnet).NeedsPower = false;
        Assert.That(pair.Server.System<ItemToggleSystem>().TryActivate(magnet, predicted: false), Is.True,
            "The magnet should switch on.");
        if (wall)
            DrydockTest.SpawnAnchored(pair, ship, "WallSolid", new Vector2i(4, 1));
        var ore = em.SpawnEntity("SteelOre1", new EntityCoordinates(ship, 7.5f, 1.5f));
        return (magnet, ore);
    }

    /// <summary>
    /// Ticks until the magnet has locked onto the ore. The power net only updates every half second, so the magnet
    /// may not count as powered for up to that long after it spawns.
    /// </summary>
    private static Task LockOn(TestPair pair, EntityUid ore)
    {
        return DrydockTest.RunUntil(pair, 40, 1, () => pair.Server.EntMan.HasComponent<WFOreMagnetPulledComponent>(ore));
    }

    private static bool Stored(IEntityManager em, EntityUid magnet, EntityUid ore)
    {
        // Stacking onto ore already in storage deletes the original.
        return em.Deleted(ore) || em.GetComponent<TransformComponent>(ore).ParentUid == magnet;
    }

    [Test]
    public async Task MagnetLocksOnThenPullsInOreItCanSee()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid magnet = default, ore = default;
        await server.WaitAssertion(() => (magnet, ore) = SetUp(pair, map.MapId, wall: false));
        await LockOn(pair, ore);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<WFOreMagnetPulledComponent>(ore, out var pulled), Is.True,
                "The magnet should lock onto ore it can see.");
            Assert.That(pulled!.Magnet, Is.EqualTo(magnet));
            Assert.That(Stored(em, magnet, ore), Is.False, "The ore should stay put until the lock has held.");
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(em, magnet, ore), Is.True, "Once the lock holds, the ore should be pulled into storage.");
            Assert.That(em.HasComponent<WFOreMagnetPulledComponent>(ore) && !em.Deleted(ore), Is.False,
                "Stored ore should no longer be marked as pulled.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PickedUpOreBreaksTheLock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid magnet = default, ore = default, holder = default;
        await server.WaitAssertion(() => (magnet, ore) = SetUp(pair, map.MapId, wall: false));
        await LockOn(pair, ore);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFOreMagnetPulledComponent>(ore), Is.True, "The magnet should lock on first.");
            // Stands in for a hand or ore bag picking the ore up mid-lock.
            holder = em.SpawnEntity(null, em.GetComponent<TransformComponent>(ore).Coordinates);
            var containers = server.System<SharedContainerSystem>();
            Assert.That(containers.Insert(ore, containers.EnsureContainer<Container>(holder, "held")), Is.True);
        });
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(ore).ParentUid, Is.EqualTo(holder),
                "The magnet should not pull ore out of whatever picked it up.");
            Assert.That(em.HasComponent<WFOreMagnetPulledComponent>(ore), Is.False, "Picking the ore up should break the lock.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FullMagnetStillTopsUpItsStacks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid magnet = default, ore = default;
        await server.WaitAssertion(() =>
        {
            (magnet, ore) = SetUp(pair, map.MapId, wall: false);
            var storage = server.System<SharedStorageSystem>();
            var at = em.GetComponent<TransformComponent>(magnet).Coordinates;
            Assert.That(storage.Insert(magnet, em.SpawnEntity("SteelOre1", at), out _, playSound: false), Is.True,
                "The magnet should take a partial steel stack.");
            // Fill every slot left with full stacks of another ore, so only the steel stack has room.
            for (var i = 0; i < 100; i++)
            {
                var filler = em.SpawnEntity("GoldOre", at);
                if (storage.Insert(magnet, filler, out _, playSound: false))
                    continue;

                em.DeleteEntity(filler);
                break;
            }
        });
        await LockOn(pair, ore);
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(em, magnet, ore), Is.True,
                "Ore that tops up a stored stack should be pulled in, even with no free slot.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallBlocksTheMagnet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid magnet = default, ore = default;
        await server.WaitAssertion(() => (magnet, ore) = SetUp(pair, map.MapId, wall: true));
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFOreMagnetPulledComponent>(ore), Is.False, "A wall between them should stop the lock.");
            Assert.That(Stored(em, magnet, ore), Is.False, "Ore behind a wall should not be pulled in.");
        });

        await pair.CleanReturnAsync();
    }
}
