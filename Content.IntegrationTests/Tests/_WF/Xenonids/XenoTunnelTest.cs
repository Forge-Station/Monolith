#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.RMCCore.Pulling;
using Content.Shared._WF.Xenonids.Burrow;
using Content.Shared._WF.Xenonids.Construction.Tunnel;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Tunnel rules: explosives and shovels close tunnels, tunnels and burrowing stay on one grid, admin ghosts drag xenos.</summary>
[TestFixture]
public sealed class XenoTunnelTest
{
    private sealed record Setup(
        TestPair Pair,
        IEntityManager Entities,
        TestMapData Map,
        EntityUid Hive);

    private static async Task<Setup> Create()
    {
        var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        EntityUid hive = default;
        await pair.Server.WaitPost(() => hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace));
        return new Setup(pair, entities, map, hive);
    }

    private static EntityUid SpawnTunnel(Setup s, EntityCoordinates coords, string name)
    {
        Assert.That(s.Entities.System<XenoTunnelSystem>().TryPlaceTunnel(s.Hive, name, coords, out var tunnel), "The hive registers the tunnel.");
        return tunnel!.Value;
    }

    private static void PutInTunnel(Setup s, EntityUid tunnel, EntityUid xeno)
    {
        var container = s.Entities.System<SharedContainerSystem>()
            .EnsureContainer<Container>(tunnel, XenoTunnelComponent.ContainedMobsContainerId);
        Assert.That(s.Entities.System<SharedContainerSystem>().Insert(xeno, container), "The xeno enters the tunnel.");
    }

    private static void AssertEjectedAlive(Setup s, EntityUid tunnel, EntityUid xeno)
    {
        Assert.That(s.Entities.Deleted(tunnel), "The tunnel is gone.");
        Assert.That(s.Entities.Deleted(xeno), Is.False, "The xeno survives.");
        Assert.That(s.Entities.System<MobStateSystem>().IsAlive(xeno), "The xeno is alive.");
        Assert.That(s.Entities.System<SharedContainerSystem>().IsEntityInContainer(xeno), Is.False, "The xeno is back on the map.");
        Assert.That(s.Entities.GetComponent<TransformComponent>(xeno).MapID, Is.EqualTo(s.Map.MapId));
        Assert.That(s.Entities.GetComponent<HiveComponent>(s.Hive).HiveTunnels, Is.Empty, "The hive forgets the tunnel.");
    }

    /// <summary>A C4 closes the tunnel and throws the xeno inside out alive.</summary>
    [Test]
    public async Task ExplosiveClosesTunnel()
    {
        var s = await Create();
        var server = s.Pair.Server;
        var spot = new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f);

        EntityUid tunnel = default;
        EntityUid xeno = default;
        await server.WaitPost(() =>
        {
            tunnel = SpawnTunnel(s, spot, "test tunnel");
            xeno = s.Entities.SpawnEntity("CMXenoDrone", spot);
            PutInTunnel(s, tunnel, xeno);
        });
        await s.Pair.RunTicksSync(5);

        // The numbers of the C4 prototype.
        await server.WaitPost(() =>
        {
            var epicenter = s.Entities.System<SharedTransformSystem>().ToMapCoordinates(spot);
            s.Entities.System<ExplosionSystem>().QueueExplosion(epicenter, "DemolitionCharge", 325f, 50f, 175f, null);
        });
        await s.Pair.RunSeconds(5);

        await server.WaitAssertion(() => AssertEjectedAlive(s, tunnel, xeno));
        await s.Pair.CleanReturnAsync();
    }

    /// <summary>A grenade's centre closes a tunnel; a faint blast does not.</summary>
    [Test]
    public async Task GrenadeClosesTunnelButFaintBlastDoesNot()
    {
        var s = await Create();
        var server = s.Pair.Server;
        var spot = new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f);

        EntityUid tunnel = default;
        await server.WaitPost(() =>
        {
            tunnel = SpawnTunnel(s, spot, "test tunnel");
            var epicenter = s.Entities.System<SharedTransformSystem>().ToMapCoordinates(spot);
            s.Entities.System<ExplosionSystem>().QueueExplosion(epicenter, "Default", 3f, 1f, 1f, null);
        });
        await s.Pair.RunSeconds(3);
        await server.WaitAssertion(() => Assert.That(s.Entities.Deleted(tunnel), Is.False, "A faint blast leaves the tunnel."));

        await server.WaitPost(() =>
        {
            var epicenter = s.Entities.System<SharedTransformSystem>().ToMapCoordinates(spot);
            s.Entities.System<ExplosionSystem>().QueueExplosion(epicenter, "Default", 120f, 3f, 10f, null);
        });
        await s.Pair.RunSeconds(3);
        await server.WaitAssertion(() => Assert.That(s.Entities.Deleted(tunnel), "A grenade closes it."));
        await s.Pair.CleanReturnAsync();
    }

    /// <summary>A human with a shovel fills a tunnel, and a folded entrenching tool or a crowbar does not.</summary>
    [Test]
    public async Task ShovelFillsTunnel()
    {
        var s = await Create();
        var server = s.Pair.Server;
        var spot = new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f);
        var interaction = s.Entities.System<SharedInteractionSystem>();
        var hands = s.Entities.System<SharedHandsSystem>();
        var toggle = s.Entities.System<ItemToggleSystem>();

        EntityUid tunnel = default;
        EntityUid xeno = default;
        EntityUid human = default;
        EntityUid shovel = default;
        EntityUid etool = default;
        EntityUid crowbar = default;
        await server.WaitPost(() =>
        {
            tunnel = SpawnTunnel(s, spot, "test tunnel");
            xeno = s.Entities.SpawnEntity("CMXenoDrone", spot);
            PutInTunnel(s, tunnel, xeno);
            human = s.Entities.SpawnEntity("MobHuman", new EntityCoordinates(s.Map.Grid, 1.5f, 0.5f));
            shovel = s.Entities.SpawnEntity("Shovel", spot);
            etool = s.Entities.SpawnEntity("CMEntrenchingTool", spot);
            crowbar = s.Entities.SpawnEntity("Crowbar", spot);
        });
        await s.Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(hands.TryPickupAnyHand(human, crowbar), "Picks up the crowbar.");
            interaction.InteractUsing(human, crowbar, tunnel, spot);
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(human), Is.False, "A crowbar does not fill tunnels.");
            hands.TryDrop(human, crowbar);

            Assert.That(hands.TryPickupAnyHand(human, etool), "Picks up the entrenching tool.");
            interaction.InteractUsing(human, etool, tunnel, spot);
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(human), Is.False, "A folded entrenching tool does not fill tunnels.");
            hands.TryDrop(human, etool);

            Assert.That(hands.TryPickupAnyHand(human, shovel), "Picks up the shovel.");
            var used = interaction.InteractUsing(human, shovel, tunnel, spot);
            Assert.That(used, "The shovel is used on the tunnel.");
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(human), "A shovel starts the fill do-after.");
        });

        await s.Pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(s.Entities.Deleted(tunnel), Is.False, "The tunnel holds until the fill delay passes.");
        });

        await s.Pair.RunSeconds(22);
        await server.WaitAssertion(() => AssertEjectedAlive(s, tunnel, xeno));

        // An unfolded entrenching tool digs too; the first human is suffocating by now, so a fresh one does it.
        EntityUid second = default;
        EntityUid digger = default;
        await server.WaitPost(() =>
        {
            second = SpawnTunnel(s, spot, "second tunnel");
            digger = s.Entities.SpawnEntity("MobHuman", new EntityCoordinates(s.Map.Grid, 1.5f, 0.5f));
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(toggle.TryActivate(etool, digger), "Unfolds the entrenching tool.");
            Assert.That(hands.TryPickupAnyHand(digger, etool), "Picks up the unfolded entrenching tool.");
        });
        await s.Pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(interaction.InteractUsing(digger, etool, second, spot), "The entrenching tool is used on the tunnel.");
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(digger), "An unfolded entrenching tool starts the fill do-after.");
        });
        await s.Pair.RunSeconds(32);
        await server.WaitAssertion(() => Assert.That(s.Entities.Deleted(second), "The second tunnel is filled."));

        await s.Pair.CleanReturnAsync();
    }

    private static EntityUid SecondGrid(Setup s)
    {
        var grid = s.Entities.System<SharedMapSystem>().CreateGridEntity(s.Map.MapId);
        s.Entities.System<SharedTransformSystem>().SetWorldPosition(grid, new Vector2(20f, 0f));
        s.Entities.System<SharedMapSystem>().SetTile(grid, new Vector2i(0, 0), s.Map.Tile.Tile);
        return grid;
    }

    /// <summary>Only tunnels on the origin's grid are destinations, and travel to another grid is refused.</summary>
    [Test]
    public async Task TunnelsStayOnOneGrid()
    {
        var s = await Create();
        var server = s.Pair.Server;
        var tunnels = s.Entities.System<XenoTunnelSystem>();

        EntityUid origin = default;
        EntityUid sameGrid = default;
        EntityUid otherGrid = default;
        EntityUid xeno = default;
        await server.WaitPost(() =>
        {
            s.Entities.System<SharedMapSystem>().SetTile(s.Map.Grid, new Vector2i(1, 0), s.Map.Tile.Tile);
            var other = SecondGrid(s);
            origin = SpawnTunnel(s, new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f), "origin");
            sameGrid = SpawnTunnel(s, new EntityCoordinates(s.Map.Grid, 1.5f, 0.5f), "same grid");
            otherGrid = SpawnTunnel(s, new EntityCoordinates(other, 0.5f, 0.5f), "other grid");
            xeno = s.Entities.SpawnEntity("CMXenoDrone", new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f));
            PutInTunnel(s, origin, xeno);
        });
        await s.Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var destinations = tunnels.GetTravelDestinations(origin);
            Assert.That(destinations.Keys, Is.EquivalentTo(new[] { "same grid" }), "Only the same-grid tunnel is listed.");
            Assert.That(tunnels.SameGrid(origin, otherGrid), Is.False);

            var net = s.Entities.GetNetEntity(origin);
            var toOther = new TraverseXenoTunnelMessage(s.Entities.GetNetEntity(otherGrid)) { Actor = xeno, Entity = net };
            s.Entities.EventBus.RaiseLocalEvent(origin, toOther);
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(xeno), Is.False, "Travel to another grid is refused.");

            var toSame = new TraverseXenoTunnelMessage(s.Entities.GetNetEntity(sameGrid)) { Actor = xeno, Entity = net };
            s.Entities.EventBus.RaiseLocalEvent(origin, toSame);
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(xeno), "Travel on the same grid starts.");
        });

        await s.Pair.CleanReturnAsync();
    }

    /// <summary>A burrowed xeno cannot tunnel to or surface on another grid.</summary>
    [Test]
    public async Task BurrowStaysOnOneGrid()
    {
        var s = await Create();
        var server = s.Pair.Server;

        EntityUid burrower = default;
        EntityCoordinates otherCoords = default;
        EntityCoordinates sameCoords = default;
        await server.WaitPost(() =>
        {
            s.Entities.System<SharedMapSystem>().SetTile(s.Map.Grid, new Vector2i(1, 0), s.Map.Tile.Tile);
            var other = SecondGrid(s);
            otherCoords = new EntityCoordinates(other, 0.5f, 0.5f);
            sameCoords = new EntityCoordinates(s.Map.Grid, 1.5f, 0.5f);
            burrower = s.Entities.SpawnEntity("CMXenoBurrower", new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f));
            var ev = new BurrowedEvent(true);
            s.Entities.EventBus.RaiseLocalEvent(burrower, ref ev, false);
        });
        await s.Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(s.Entities.GetComponent<XenoBurrowComponent>(burrower).Active, "The burrower is under.");

            var toOther = new XenoBurrowActionEvent { Performer = burrower, Target = otherCoords };
            s.Entities.EventBus.RaiseLocalEvent(burrower, toOther);
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(burrower), Is.False, "Tunnelling to another grid is refused.");

            // Even a finished tunnel do-after aimed at another grid does not surface it there.
            var finish = new XenoBurrowMoveDoAfter(s.Entities.GetNetCoordinates(otherCoords));
            finish.DoAfter = new Content.Shared.DoAfter.DoAfter(0, new DoAfterArgs(s.Entities, burrower, TimeSpan.FromSeconds(1), finish, burrower), TimeSpan.Zero);
            s.Entities.EventBus.RaiseLocalEvent(burrower, finish);
            Assert.That(s.Entities.GetComponent<TransformComponent>(burrower).GridUid, Is.EqualTo(s.Map.Grid.Owner), "Still on the home grid.");
            Assert.That(s.Entities.GetComponent<XenoBurrowComponent>(burrower).Active, "Still burrowed.");
        });

        await s.Pair.RunSeconds(8);
        await server.WaitAssertion(() =>
        {
            var toSame = new XenoBurrowActionEvent { Performer = burrower, Target = sameCoords };
            s.Entities.EventBus.RaiseLocalEvent(burrower, toSame);
            Assert.That(s.Entities.HasComponent<ActiveDoAfterComponent>(burrower), "Tunnelling on the same grid starts.");
        });

        await s.Pair.CleanReturnAsync();
    }

    /// <summary>An admin ghost grabs a live xeno without penalty; a human is paralyzed, as in RMC.</summary>
    [Test]
    public async Task AdminGhostDragsXenos()
    {
        var s = await Create();
        var server = s.Pair.Server;
        var pulling = s.Entities.System<PullingSystem>();
        var spot = new EntityCoordinates(s.Map.Grid, 0.5f, 0.5f);

        EntityUid aghost = default;
        EntityUid ghost = default;
        EntityUid human = default;
        EntityUid drone = default;
        EntityUid other = default;
        await server.WaitPost(() =>
        {
            aghost = s.Entities.SpawnEntity("AdminObserver", spot);
            ghost = s.Entities.SpawnEntity("MobObserver", spot);
            human = s.Entities.SpawnEntity("MobHuman", spot);
            drone = s.Entities.SpawnEntity("CMXenoDrone", spot);
            other = s.Entities.SpawnEntity("CMXenoDrone", spot);
        });
        await s.Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var rmc = s.Entities.System<RMCPullingSystem>();
            Assert.That(rmc.IsAdminGhost(aghost), "The admin observer is an admin ghost.");
            Assert.That(rmc.IsAdminGhost(ghost), Is.False, "A plain ghost is not.");
            Assert.That(s.Entities.HasComponent<PullerComponent>(ghost), Is.False, "A plain ghost cannot pull anyway.");

            Assert.That(pulling.TryStartPull(human, drone), Is.False, "A human's grab on a live xeno is cancelled.");
            Assert.That(s.Entities.HasComponent<StunnedComponent>(human), "A human grabbing a live xeno is paralyzed.");

            Assert.That(pulling.TryStartPull(aghost, drone), "The admin ghost grabs the live xeno.");
            Assert.That(s.Entities.GetComponent<PullerComponent>(aghost).Pulling, Is.EqualTo(drone));
            Assert.That(s.Entities.HasComponent<StunnedComponent>(aghost), Is.False, "No paralysis.");
            pulling.TryStopPull(drone, s.Entities.GetComponent<PullableComponent>(drone), aghost);

            s.Entities.System<MobStateSystem>().ChangeMobState(other, Content.Shared.Mobs.MobState.Dead);
            Assert.That(pulling.TryStartPull(aghost, other), "The admin ghost also drags a dead xeno.");
        });

        await s.Pair.CleanReturnAsync();
    }
}
