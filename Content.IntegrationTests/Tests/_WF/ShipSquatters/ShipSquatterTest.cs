#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.NpcIntents;
using Content.Server._WF.PlanetNpcGear;
using Content.Server._WF.ShipSquatters;
using Content.Server.Atmos.Components;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Planets;
using Content.Shared._WF.ShipSquatters;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Cuffs;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Decals;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Station.Components;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using TestPair = Content.IntegrationTests.Pair.TestPair;
using static Robust.UnitTesting.RobustIntegrationTest;

namespace Content.IntegrationTests.Tests._WF.ShipSquatters;

/// <summary>
/// Squatters move into unguarded landed ships and nowhere else, make a mess that touches nothing, bark and talk but
/// attack nobody until forced, stop on a downed target, calm down, and go with the ship.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipSquatterSystem))]
public sealed class ShipSquatterTest
{
    private const int Size = 9;
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    /// <summary>A planet's ground: the layer marks a ship landed on it.</summary>
    private static async Task<TestMapData> Ground(TestPair pair)
    {
        var map = await pair.CreateTestMap();
        await pair.Server.WaitPost(() =>
        {
            pair.Server.EntMan.EnsureComponent<WFPlanetLayerComponent>(map.MapUid);
            pair.Server.EntMan.EnsureComponent<CEZGroundLayerComponent>(map.MapUid);
        });
        return map;
    }

    /// <summary>
    /// A 9x9 hull with an airlock in the west wall (0,4) and an inner wall at x=4 with a shut door at (4,4), so there
    /// are two rooms, 1-3 and 5-7. A player's ship unless told otherwise.
    /// </summary>
    private static async Task<EntityUid> Ship(TestPair pair, TestMapData map, Vector2 origin, bool deed = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid grid = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var created = maps.CreateGridEntity(map.MapId);
            grid = created.Owner;
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
                tiles.Add((new Vector2i(x, y), plating));
            maps.SetTiles(grid, created.Comp, tiles);
            server.System<SharedTransformSystem>().SetLocalPosition(grid, origin);

            for (var x = 0; x < Size; x++)
            for (var y = 0; y < Size; y++)
            {
                var edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1;
                var inner = x == 4;
                if (!edge && !inner)
                    continue;
                var proto = (x, y) is (0, 4) or (4, 4) ? "Airlock" : "WallSolid";
                entMan.SpawnEntity(proto, new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
            }

            entMan.EnsureComponent<GridAtmosphereComponent>(grid);
            var gravity = entMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(grid);
            gravity.Enabled = true;
            gravity.Inherent = true;
            entMan.Dirty(grid, gravity);
            if (!deed)
                return;
            entMan.EnsureComponent<ShuttleDeedComponent>(grid);
            entMan.EnsureComponent<ShuttleComponent>(grid);
        });

        // Atmos builds the grid's tiles; fill them with room air.
        await server.WaitRunTicks(5);
        await server.WaitPost(() =>
        {
            var air = new GasMixture(2500) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Oxygen, 21.824779f);
            air.SetMoles(Gas.Nitrogen, 82.10312f);
            foreach (var tile in entMan.GetComponent<GridAtmosphereComponent>(grid).Tiles.Values)
            {
#pragma warning disable RA0002 // A fixture room full of air, without simulating how it got there.
                tile.Air = air.Clone();
#pragma warning restore RA0002
            }
        });
        await server.WaitRunTicks(2);
        return grid;
    }

    /// <summary>Fast timers: a ship is idle in seconds and rolls every second, for sure.</summary>
    private static void Quick(TestPair pair, float idleMinutes = 0.05f, int cap = 3)
    {
        pair.Server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, true);
        pair.Server.CfgMan.SetCVar(WFShipSquatterCVars.IdleMinutes, idleMinutes);
        pair.Server.CfgMan.SetCVar(WFShipSquatterCVars.RollSeconds, 1f);
        pair.Server.CfgMan.SetCVar(WFShipSquatterCVars.Chance, 1f);
        pair.Server.CfgMan.SetCVar(WFShipSquatterCVars.Cap, cap);
    }

    private static List<Entity<WFShipSquatterComponent>> Squatters(ServerIntegrationInstance server)
    {
        return server.EntMan.AllEntities<WFShipSquatterComponent>().ToList();
    }

    /// <summary>Everything on a grid, with where it is.</summary>
    private static Dictionary<EntityUid, EntityCoordinates> Snapshot(ServerIntegrationInstance server, EntityUid grid, params EntityUid[] except)
    {
        var xform = server.EntMan.GetComponent<TransformComponent>(grid);
        var all = new Dictionary<EntityUid, EntityCoordinates>();
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!except.Contains(child))
                all[child] = server.EntMan.GetComponent<TransformComponent>(child).Coordinates;
        }

        return all;
    }

    private static async Task<(EntityUid Squatter, EntityUid Victim)> Rigged(TestPair pair, EntityUid grid, bool attach = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid squatter = default, victim = default;
        await server.WaitPost(() =>
        {
            Assert.That(server.System<WFShipSquatterSystem>().TryMoveIn(grid, out squatter), Is.True);
            victim = entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(squatter).Coordinates);
            if (attach)
                server.PlayerMan.SetAttachedEntity(pair.Player!, victim);
        });
        await server.WaitRunTicks(5);
        return (squatter, victim);
    }

    private static void PutDown(ServerIntegrationInstance server, EntityUid body, float level = 0.75f)
    {
        server.System<SharedWolfmedConsciousnessSystem>().SetExternalPressure(body, "test", level);
    }

    private static float Hurt(ServerIntegrationInstance server, EntityUid body)
    {
        return (float) server.EntMan.GetComponent<DamageableComponent>(body).TotalDamage;
    }

    private static TimeSpan LastSwing(ServerIntegrationInstance server, EntityUid npc)
    {
        var entMan = server.EntMan;
        var latest = TimeSpan.Zero;
        foreach (var uid in server.System<SharedHandsSystem>().EnumerateHeld(npc).Append(npc))
        {
            if (entMan.TryGetComponent(uid, out Content.Shared.Weapons.Melee.MeleeWeaponComponent? melee) && melee.NextAttack > latest)
                latest = melee.NextAttack;
        }

        return latest;
    }

    private static async Task RunUntil(TestPair pair, Func<bool> done, float seconds)
    {
        for (var waited = 0f; waited < seconds; waited += 0.5f)
        {
            var finished = false;
            await pair.Server.WaitPost(() => finished = done());
            if (finished)
                return;
            await pair.RunSeconds(0.5f);
            await Task.Delay(25);
        }
    }

    [TestCase("WFSurfaceMerak", false)]
    [TestCase("WFSurfaceAerumna", true)]
    public async Task MovesIntoAnIdleShipIndoorsAwayFromTheHatch(string surface, bool unsafeAir)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        await server.WaitPost(() =>
        {
            entMan.AddComponents(map.MapUid, server.ProtoMan.Index<WFPlanetSurfacePrototype>(surface).NetworkComponents!);
            entMan.EnsureComponent<WFPlanetLayerComponent>(map.MapUid);
        });
        var ship = await Ship(pair, map, new Vector2(100, 100));
        Dictionary<EntityUid, EntityCoordinates> before = new();
        var doorStates = new Dictionary<EntityUid, DoorState>();
        await server.WaitPost(() =>
        {
            before = Snapshot(server, ship);
            foreach (var door in entMan.AllEntities<DoorComponent>())
                doorStates[door] = door.Comp.State;
        });
        Quick(pair);

        await RunUntil(pair, () => Squatters(server).Count > 0, 40);
        await pair.RunSeconds(40);

        await server.WaitAssertion(() =>
        {
            var squatters = Squatters(server);
            Assert.That(squatters, Has.Count.EqualTo(1), "Exactly one squatter a ship, however long it sits.");
            var squatter = squatters[0];
            var maps = server.System<SharedMapSystem>();
            var xform = entMan.GetComponent<TransformComponent>(squatter);
            Assert.That(squatter.Comp.Ship, Is.EqualTo(ship));
            Assert.That(entMan.HasComponent<WFSquattedShipComponent>(ship), Is.True);
            Assert.That(xform.GridUid, Is.EqualTo(ship), "He lives aboard.");

            var bed = maps.CoordinatesToTile(ship, entMan.GetComponent<MapGridComponent>(ship), squatter.Comp.Bed);
            Assert.That(server.System<WFCrewPlannerSystem>().IsSafePost(ship, bed), Is.True, "His bed is on a clear tile with air.");
            Assert.That(bed.X, Is.InRange(5, 7), "The airlock is at the west wall: the far room is the farthest from it.");
            Assert.That(bed.Y, Is.InRange(1, 7));

            var hands = server.System<SharedHandsSystem>();
            var carried = hands.EnumerateHeld(squatter.Owner).Select(held => entMan.GetComponent<MetaDataComponent>(held).EntityPrototype?.ID).ToList();
            Assert.That(carried, Does.Contain("Spear"), "A mean improvised melee weapon in hand.");
            Assert.That(server.System<InventorySystem>().GetHandOrInventoryEntities(squatter.Owner).Any(item => entMan.HasComponent<GunComponent>(item)),
                Is.False, "No gun.");

            var gear = entMan.GetComponent<WFPlanetNpcGearComponent>(squatter);
            Assert.That(gear.Tank != null, Is.EqualTo(unsafeAir), $"Dressed for the air of {surface}.");

            var sheet = entMan.AllEntities<TransformComponent>().Any(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "BedsheetBrown"
                                                                           && e.Comp.GridUid == ship);
            Assert.That(sheet, Is.True, "A bedroll of real items on his tile.");

            foreach (var (uid, at) in before)
            {
                Assert.That(entMan.EntityExists(uid), Is.True, "Nothing in the ship is deleted.");
                Assert.That(entMan.GetComponent<TransformComponent>(uid).Coordinates, Is.EqualTo(at), "Nothing moved.");
            }

            foreach (var (door, state) in doorStates)
                Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(state), "Nothing was opened.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeavesGuardedShipsAndOtherShipsAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var space = await pair.CreateTestMap();

        var control = await Ship(pair, map, new Vector2(1000, 0));
        var crewed = await Ship(pair, map, new Vector2(100, 0));
        var minded = await Ship(pair, map, new Vector2(200, 0));
        var station = await Ship(pair, map, new Vector2(300, 0));
        var npc = await Ship(pair, map, new Vector2(400, 0));
        var wreck = await Ship(pair, map, new Vector2(500, 0));
        var plain = await Ship(pair, map, new Vector2(600, 0), deed: false);
        var flying = await Ship(pair, space, new Vector2(700, 0));
        await server.WaitPost(() =>
        {
            // A player aboard one ship; an NPC crewman a stone's throw from another.
            var player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(minded, 2.5f, 2.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
            var crewman = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, new Vector2(120, 12)));
            entMan.AddComponent<WFCrewComponent>(crewman);
            entMan.AddComponent<StationMemberComponent>(station);
            entMan.AddComponent<WFCrewShipProfileComponent>(npc);
            entMan.AddComponent<WFEncounterGridComponent>(wreck);
        });
        Quick(pair);

        await RunUntil(pair, () => Squatters(server).Count > 0, 40);
        await pair.RunSeconds(40);

        await server.WaitAssertion(() =>
        {
            var squatters = Squatters(server);
            Assert.That(squatters, Has.Count.EqualTo(1), "Only the unguarded player's ship gets one.");
            Assert.That(squatters[0].Comp.Ship, Is.EqualTo(control));
            foreach (var other in new[] { crewed, minded, station, npc, wreck, plain, flying })
                Assert.That(entMan.HasComponent<WFSquattedShipComponent>(other), Is.False, $"{other} should have been left alone.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OneThatJustLeftIsNotIdleYetAndTheWorldHasACap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        var one = await Ship(pair, map, new Vector2(500, 100));
        var two = await Ship(pair, map, new Vector2(900, 100));
        var squatting = server.System<WFShipSquatterSystem>();
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            player = entMan.SpawnEntity("MobHuman", new EntityCoordinates(ship, 2.5f, 2.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        // Thirty seconds idle; the other ships are rigged out of the way by a cap of one.
        Quick(pair, 0.5f, cap: 1);
        await pair.RunSeconds(10);
        await server.WaitPost(() => entMan.DeleteEntity(player));
        await pair.RunSeconds(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFSquattedShipComponent>(ship), Is.False, "Idle for twenty seconds of thirty: not yet.");
            Assert.That(squatting.IdleSince(ship), Is.Not.Null, "The clock started when the player left.");
        });

        await pair.RunSeconds(60);
        await server.WaitAssertion(() =>
        {
            var squatters = Squatters(server);
            Assert.That(squatters, Has.Count.EqualTo(1), "Three idle ships, a cap of one for the world.");
            Assert.That(new[] { ship, one, two }.Count(s => entMan.HasComponent<WFSquattedShipComponent>(s)), Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AFiveMinuteErrandNeverCosts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        // The default twenty minutes, whatever the dice would say.
        server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, true);
        server.CfgMan.SetCVar(WFShipSquatterCVars.RollSeconds, 1f);
        server.CfgMan.SetCVar(WFShipSquatterCVars.Chance, 1f);

        await pair.RunSeconds(330);

        await server.WaitAssertion(() =>
        {
            Assert.That(Squatters(server), Is.Empty);
            Assert.That(server.EntMan.HasComponent<WFSquattedShipComponent>(ship), Is.False);
            Assert.That(server.CfgMan.GetCVar(WFShipSquatterCVars.IdleMinutes), Is.EqualTo(20f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheMessGrowsToItsCapAndTouchesNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, false);
        var (squatter, victim) = await Rigged(pair, ship, attach: false);
        Dictionary<EntityUid, EntityCoordinates> before = new();
        await server.WaitPost(() =>
        {
            before = Snapshot(server, ship, squatter, victim);
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            comp.MessMin = comp.MessMax = 1f;
            comp.MessCap = 30;
            comp.LingerMin = comp.LingerMax = 3f;
            entMan.DeleteEntity(victim);
        });

        // He only gets a go every few seconds, and a spill that finds no room is a go wasted.
        await RunUntil(pair, () => entMan.GetComponent<WFShipSquatterComponent>(squatter).MessCount >= 30, 300);

        var count = 0;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            count = comp.MessCount;
            Assert.That(comp.MessCount, Is.EqualTo(30), $"The mess grows to its cap and no further. mood {comp.Mood} rooms {comp.Rooms.Count} doors {comp.Doors.Count} talking {comp.Talking} aboard {entMan.GetComponent<TransformComponent>(squatter).GridUid == ship} mess {comp.Mess.Count} next {comp.NextMess} alive {server.System<MobStateSystem>().IsAlive(squatter)}");

            var maps = server.System<SharedMapSystem>();
            var grid = entMan.GetComponent<MapGridComponent>(ship);
            var bedTile = maps.CoordinatesToTile(ship, grid, comp.Bed);
            var doors = entMan.AllEntities<DoorComponent>().Select(door => maps.CoordinatesToTile(ship, grid, entMan.GetComponent<TransformComponent>(door).Coordinates)).ToList();
            var litter = comp.Mess.Where(entMan.EntityExists).ToList();
            Assert.That(litter, Is.Not.Empty, "Items and puddles.");
            foreach (var mess in litter)
            {
                var tile = maps.CoordinatesToTile(ship, grid, entMan.GetComponent<TransformComponent>(mess).Coordinates);
                Assert.That(tile.X < 4, Is.EqualTo(bedTile.X < 4), "Within his rooms: the shut door keeps him out of the other.");
                Assert.That(maps.GetAnchoredEntities(ship, grid, tile).Any(thing => !entMan.HasComponent<Content.Shared.Fluids.Components.PuddleComponent>(thing)), Is.False, "Never on a wall, pipe or machine's tile.");
                Assert.That(doors.Any(door => Math.Abs(door.X - tile.X) + Math.Abs(door.Y - tile.Y) <= 1), Is.False, "Never at a door.");
            }

            var decals = entMan.GetComponent<DecalGridComponent>(ship).ChunkCollection.ChunkCollection.Values.SelectMany(chunk => chunk.Decals.Values).ToList();
            Assert.That(decals, Is.Not.Empty, "Graffiti.");
            Assert.That(decals.All(decal => decal.Cleanable), Is.True, "Every decal comes off.");

            foreach (var (uid, at) in before)
            {
                Assert.That(entMan.EntityExists(uid), Is.True, "Nothing already aboard is deleted.");
                Assert.That(entMan.GetComponent<TransformComponent>(uid).Coordinates, Is.EqualTo(at), "Nothing already aboard moves.");
            }
        });

        // Dead, he makes no more.
        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFShipSquatterComponent>(squatter).MessCap = 60;
            server.System<MobStateSystem>().ChangeMobState(squatter, MobState.Dead);
        });
        await pair.RunSeconds(5);
        await server.WaitAssertion(() => Assert.That(server.System<MobStateSystem>().IsDead(squatter), Is.True));
        await pair.RunSeconds(20);
        await server.WaitAssertion(() => Assert.That(entMan.GetComponent<WFShipSquatterComponent>(squatter).MessCount, Is.EqualTo(count), "Dead stops the mess."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrowdedSpokenToAndThreatenedHeAttacksNobody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, false);
        var (squatter, victim) = await Rigged(pair, ship);
        var crowd = new List<EntityUid> { victim };
        await server.WaitPost(() =>
        {
            for (var i = 0; i < 3; i++)
                crowd.Add(entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(squatter).Coordinates));
        });
        var system = server.System<WFShipSquatterSystem>();
        TimeSpan swing = default;
        await server.WaitPost(() => swing = LastSwing(server, squatter));

        await pair.RunSeconds(20);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            Assert.That(comp.Mood, Is.EqualTo(WFSquatterMood.Sitting));
            Assert.That(comp.Barked, Does.ContainKey(victim));
            Assert.That(entMan.GetComponent<HTNComponent>(squatter).Enabled, Is.False, "His own brain is off.");
            Assert.That(entMan.TryGetComponent(squatter, out FactionExceptionComponent? exceptions) && exceptions.Hostiles.Count > 0, Is.False);
            Assert.That(LastSwing(server, squatter), Is.EqualTo(swing), "He never swings.");
            Assert.That(crowd.All(body => Hurt(server, body) == 0f), Is.True, "Nobody is hurt.");
        });

        // Spoken to: a bark.
        TimeSpan before = default;
        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFShipSquatterComponent>(squatter).NextBark = TimeSpan.Zero;
            before = entMan.GetComponent<WFShipSquatterComponent>(squatter).NextBark;
            server.System<ChatSystem>().TrySendInGameICMessage(victim, "get off my ship", InGameICChatType.Speak, false);
        });
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFShipSquatterComponent>(squatter).NextBark, Is.GreaterThan(before), "Talked at, he answers."));

        // Asked, begged, offered money and threatened: he turns down every one.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            system.StartTalk((squatter, comp), victim);
            Assert.That(comp.Node, Is.EqualTo("claim"));
            foreach (var (option, node) in new[] { (0, "ask"), (1, "please"), (2, "money"), (3, "threat") })
            {
                system.Choose((squatter, comp), victim, option);
                Assert.That(comp.Node, Is.EqualTo(node));
                system.Choose((squatter, comp), victim, 0);
                Assert.That(comp.Node, Is.EqualTo("claim"));
            }

            // Cash handed over is not taken.
            var cash = entMan.SpawnEntity("SpaceCash", entMan.GetComponent<TransformComponent>(victim).Coordinates);
            server.System<SharedStackSystem>().SetCount(cash, 500);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(victim, cash, checkActionBlocker: false), Is.True);
            server.System<Content.Shared.Interaction.SharedInteractionSystem>().InteractUsing(victim, cash, squatter,
                entMan.GetComponent<TransformComponent>(squatter).Coordinates, checkCanInteract: false, checkCanUse: false);
            Assert.That(entMan.GetComponent<TransformComponent>(cash).ParentUid, Is.EqualTo(victim), "He doesn't take a thing.");
            Assert.That(entMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(500));
            Assert.That(comp.Mood, Is.EqualTo(WFSquatterMood.Sitting), "Not one of them is a fight.");
        });
        await pair.RunSeconds(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFShipSquatterComponent>(squatter).Mood, Is.EqualTo(WFSquatterMood.Sitting));
            Assert.That(crowd.All(body => Hurt(server, body) == 0f), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("hit")]
    [TestCase("shot")]
    [TestCase("drag")]
    [TestCase("cuff")]
    [TestCase("shove")]
    public async Task ForcingHimTurnsHimOnTheForcer(string how)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, false);
        var (squatter, victim) = await Rigged(pair, ship);

        await server.WaitPost(() =>
        {
            var coordinates = entMan.GetComponent<TransformComponent>(squatter).Coordinates;
            switch (how)
            {
                case "hit":
                    server.System<DamageableSystem>().TryChangeDamage(squatter, new DamageSpecifier(server.ProtoMan.Index(Blunt), 1), origin: victim);
                    break;
                case "shot":
                    var gun = entMan.SpawnEntity("WeaponPistolMk58", coordinates);
                    Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(victim, gun, checkActionBlocker: false), Is.True);
                    server.System<DamageableSystem>().TryChangeDamage(squatter, new DamageSpecifier(server.ProtoMan.Index(Blunt), 1), origin: gun);
                    break;
                case "drag":
                    Assert.That(server.System<PullingSystem>().TryStartPull(victim, squatter), Is.True);
                    break;
                case "cuff":
                    var cuffs = entMan.SpawnEntity("Handcuffs", coordinates);
                    Assert.That(server.System<SharedCuffableSystem>().TryAddNewCuffs(squatter, victim, cuffs), Is.True);
                    break;
                default:
                    var shove = new DisarmedEvent(squatter, victim, 1f);
                    entMan.EventBus.RaiseLocalEvent(squatter, ref shove);
                    break;
            }
        });

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            Assert.That(comp.Mood, Is.EqualTo(WFSquatterMood.Angry), $"{how} should turn him.");
            Assert.That(comp.Forcers.Keys, Does.Contain(victim));
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(squatter).Hostiles, Does.Contain(victim));
            Assert.That(entMan.GetComponent<HTNComponent>(squatter).Enabled, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HeStopsOnADownedTargetCalmsDownAndDeadEndsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, false);
        var (squatter, victim) = await Rigged(pair, ship);
        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFShipSquatterComponent>(squatter).CalmSeconds = 6f;
            server.System<DamageableSystem>().TryChangeDamage(squatter, new DamageSpecifier(server.ProtoMan.Index(Blunt), 1), origin: victim);
        });

        await RunUntil(pair, () => Hurt(server, victim) > 0f, 40);
        float hurt = 0;
        await server.WaitAssertion(() =>
        {
            hurt = Hurt(server, victim);
            Assert.That(hurt, Is.GreaterThan(0f), "Forced, he fights back.");
        });

        // Down, he is left alone: no more swings, no more damage, and the forcer is dropped.
        TimeSpan swing = default;
        await server.WaitPost(() => PutDown(server, victim));
        await pair.RunSeconds(2);
        await server.WaitPost(() =>
        {
            Assert.That(server.System<WFNpcIntentSystem>().IsDowned(victim), Is.True);
            swing = LastSwing(server, squatter);
            hurt = Hurt(server, victim);
        });
        await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            Assert.That(LastSwing(server, squatter), Is.EqualTo(swing), "He never swings at a downed man.");
            Assert.That(Hurt(server, victim), Is.EqualTo(hurt), "A downed target isn't finished off.");
            Assert.That(comp.Forcers, Is.Empty);
            Assert.That(server.System<WFNpcIntentSystem>().InCooldown(squatter, victim), Is.True);
        });

        // With nobody forcing him he calms down and is back to insisting.
        await RunUntil(pair, () => entMan.GetComponent<WFShipSquatterComponent>(squatter).Mood == WFSquatterMood.Sitting, 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFShipSquatterComponent>(squatter).Mood, Is.EqualTo(WFSquatterMood.Sitting));
            Assert.That(entMan.GetComponent<HTNComponent>(squatter).Enabled, Is.False);
        });

        // Hit again, he is on them again even though he had let them be; dead, it all stops.
        await server.WaitPost(() =>
        {
            PutDown(server, victim, 0f);
            server.System<DamageableSystem>().TryChangeDamage(squatter, new DamageSpecifier(server.ProtoMan.Index(Blunt), 1), origin: victim);
        });
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            Assert.That(comp.Mood, Is.EqualTo(WFSquatterMood.Angry));
            Assert.That(server.System<WFNpcIntentSystem>().InCooldown(squatter, victim), Is.False, "Forced again, the target is fair game.");
            server.System<MobStateSystem>().ChangeMobState(squatter, MobState.Dead);
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MobStateSystem>().IsDead(squatter), Is.True);
            Assert.That(entMan.GetComponent<WFShipSquatterComponent>(squatter).Forcers, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheShipTakesHimAndHisMessAlongAndOffTheHullHeHeadsHome()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Ground(pair);
        var ship = await Ship(pair, map, new Vector2(100, 100));
        var ground = await Ship(pair, map, new Vector2(100, 120));
        server.CfgMan.SetCVar(WFShipSquatterCVars.Enabled, false);
        var (squatter, victim) = await Rigged(pair, ship, attach: false);
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(victim);
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            comp.MessMin = comp.MessMax = 1f;
            comp.MessCap = 6;
        });
        await RunUntil(pair, () => entMan.GetComponent<WFShipSquatterComponent>(squatter).MessCount >= 6, 60);

        // The ship lifts off: he and the mess are still on it.
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalPosition(ship, new Vector2(400, 400)));
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFShipSquatterComponent>(squatter);
            Assert.That(entMan.GetComponent<TransformComponent>(squatter).GridUid, Is.EqualTo(ship), "He goes with it.");
            Assert.That(comp.Mess.Where(entMan.EntityExists).All(mess => entMan.GetComponent<TransformComponent>(mess).GridUid == ship), Is.True,
                "The mess stays on the ship.");
        });

        // Carried off the hull he sets off back for his bed.
        await server.WaitPost(() =>
            server.System<SharedTransformSystem>().SetCoordinates(squatter, new EntityCoordinates(ground, 2.5f, 2.5f)));
        await RunUntil(pair, () => entMan.TryGetComponent(squatter, out NPCSteeringComponent? steering) && steering.Coordinates.Equals(entMan.GetComponent<WFShipSquatterComponent>(squatter).Bed), 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent(squatter, out NPCSteeringComponent? steering), Is.True, "He tries to walk back.");
            Assert.That(steering!.Coordinates, Is.EqualTo(entMan.GetComponent<WFShipSquatterComponent>(squatter).Bed));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThePrototypesAreWhole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index(WFShipSquatterSystem.SquatterProto);
            Assert.That(proto.TryGetComponent<NpcFactionMemberComponent>(out var faction, server.EntMan.ComponentFactory), Is.True);
            Assert.That(faction!.Factions, Is.EquivalentTo(new[] { "SimpleNeutral" }), "Other hostiles treat him as any stranger.");

            var comp = new WFShipSquatterComponent();
            foreach (var id in comp.BedItems.Concat(comp.Litter).Append(comp.Graffiti))
                Assert.That(server.ProtoMan.HasIndex(id), Is.True, $"{id} is missing.");
            foreach (var id in comp.Puddles)
                Assert.That(server.ProtoMan.HasIndex(id), Is.True, $"{id} is missing.");

            var set = server.ProtoMan.Index<Content.Shared._WF.NpcIntents.WFNpcIntentSetPrototype>("WFIntentsSquatter");
            Assert.That(set.Intents.Select(choice => choice.Kind), Is.All.EqualTo(Content.Shared._WF.NpcIntents.WFNpcIntentKind.BeatDown));

            var dialogue = server.ProtoMan.Index<Content.Shared._WF.Strangers.WFStrangerDialoguePrototype>("WFShipSquatterDialogue");
            foreach (var node in dialogue.Nodes.Values)
            {
                Assert.That(Loc.TryGetString(node.Line, out _), Is.True, $"{node.Line} has no text.");
                foreach (var option in node.Options)
                {
                    Assert.That(option.Cost, Is.Zero, "Nothing costs.");
                    Assert.That(option.Items, Is.Empty, "Nothing changes hands.");
                    Assert.That(option.Next == null || dialogue.Nodes.ContainsKey(option.Next), Is.True);
                    Assert.That(Loc.TryGetString(option.Text, out _), Is.True, $"{option.Text} has no text.");
                }
            }
        });
        await pair.CleanReturnAsync();
    }
}
