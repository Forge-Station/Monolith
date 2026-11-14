#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.Xenonids.Acid;
using Content.Shared._WF.Xenonids.Burrow;
using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Construction.Events;
using Content.Shared._WF.Xenonids.Construction.Tunnel;
using Content.Server.Mind;
using Content.Server.Parallax;
using Content.Shared._WF.Xenonids.Egg;
using Content.Shared._WF.Xenonids.Eye;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared._WF.Xenonids.Weeds;
using Content.Shared.Actions;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A hive works on a planet's terrain grid: weeds, resin, nodes, eggs, tunnels, burrowing and the air.</summary>
[TestFixture]
public sealed partial class XenoPlanetTest
{
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>Half the edge of the clear block the tests build in.</summary>
    private const int Reach = 3;

    /// <summary>Half the edge of the block weeds must cover on every world.</summary>
    private const int Spread = 2;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CMXenoLarva
  id: WFTestPlanetResinBuilder
  components:
  - type: XenoConstruction
    buildDelay: 0.5
    canBuild:
    - WallXenoResin
    - DoorXenoResin
    buildChoice: WallXenoResin
  - type: XenoPlasma
    plasma: 500
    maxPlasma: 500
    plasmaRegenOnWeeds: 0
    plasmaRegenOffWeeds: 0
";

    /// <summary>A planet world and the layer a test builds on: the ground, or the cavern under it.</summary>
    private sealed record Planet(TestPair Pair, World World, IEntityManager Entities, EntityUid? Layer = null)
    {
        public EntityUid Ground => Layer ?? World.Ground;

        public Entity<MapGridComponent> Grid => (Ground, Entities.GetComponent<MapGridComponent>(Ground));

        public EntityCoordinates At(Vector2i tile) => new(Ground, tile.X + 0.5f, tile.Y + 0.5f);
    }

    /// <summary>Builds an Asclepiu stack with its cavern, loads the ground round the origin and clears a block to build in.</summary>
    private static async Task<(Planet Planet, Vector2i Centre)> Create(TestPair pair, string surface = Surface, int reach = Reach, bool underground = false)
    {
        var server = pair.Server;
        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, surface);
        var planet = new Planet(pair, world, server.EntMan, underground ? world.Cavern : null);

        Assert.That(planet.Ground, Is.Not.EqualTo(EntityUid.Invalid), $"{surface} has no cavern.");
        await LoadChunks(pair, planet.Ground, new Vector2i(-24, -24), new Vector2i(24, 24));
        var centre = Vector2i.Zero;
        await server.WaitPost(() => centre = underground ? DigOutBlock(planet, surface, reach) : FindOpenBlock(planet, reach));
        return (planet, centre);
    }

    /// <summary>A bare block in the cavern: a natural one if the rock has any near the origin, else the origin dug out.</summary>
    private static Vector2i DigOutBlock(Planet planet, string surface, int reach)
    {
        if (TryFindOpenBlock(planet, reach) is { } found)
            return found;

        var maps = planet.Entities.System<SharedMapSystem>();
        var tiles = planet.Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var floor = new Tile(tiles[Floors[surface].Chamber].TileId);
        var grid = planet.Grid;
        var anchored = new List<EntityUid>();

        for (var x = -reach; x <= reach; x++)
        for (var y = -reach; y <= reach; y++)
        {
            var index = new Vector2i(x, y);
            anchored.Clear();
            maps.GetAnchoredEntities(grid, index, anchored);

            foreach (var uid in anchored)
            {
                planet.Entities.DeleteEntity(uid);
            }

            maps.SetTile(grid, grid, index, floor);
        }

        return Vector2i.Zero;
    }

    /// <summary>The first tile, searching outward from the origin, whose block of the given reach is natural, solid and bare.</summary>
    private static Vector2i FindOpenBlock(Planet planet, int reach)
    {
        if (TryFindOpenBlock(planet, reach) is { } found)
            return found;

        Assert.Fail("The layer has no bare block near its origin to build in.");
        return default;
    }

    private static Vector2i? TryFindOpenBlock(Planet planet, int reach)
    {
        var maps = planet.Entities.System<SharedMapSystem>();
        var grid = planet.Grid;

        for (var ring = 0; ring < 24; ring++)
        for (var x = -ring; x <= ring; x++)
        for (var y = -ring; y <= ring; y++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                continue;

            var open = true;

            for (var dx = -reach; dx <= reach && open; dx++)
            for (var dy = -reach; dy <= reach && open; dy++)
            {
                var index = new Vector2i(x + dx, y + dy);
                open = maps.TryGetTileRef(grid, grid, index, out var tile)
                       && !tile.Tile.IsEmpty
                       && !maps.GetAnchoredEntitiesEnumerator(grid, grid, index).MoveNext(out _);
            }

            if (open)
                return new Vector2i(x, y);
        }

        return null;
    }

    /// <summary>Gives a do-after event the finished do-after its handlers read.</summary>
    private static void Finish(Planet planet, Content.Shared.DoAfter.DoAfterEvent ev, EntityUid user, EntityUid? target)
    {
        var args = new Content.Shared.DoAfter.DoAfterArgs(planet.Entities, user, TimeSpan.FromSeconds(1), ev, user, target);
        ev.DoAfter = new Content.Shared.DoAfter.DoAfter(0, args, TimeSpan.Zero);
    }

    private static EntityUid SpawnHive(Planet planet)
    {
        return planet.Entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
    }

    private static void SetHive(Planet planet, EntityUid hive, EntityUid member)
    {
        planet.Entities.System<SharedXenoHiveSystem>().SetHive(member, hive);
    }

    private static HashSet<Vector2i> WeededTiles(Planet planet)
    {
        var maps = planet.Entities.System<SharedMapSystem>();
        var tiles = new HashSet<Vector2i>();
        var query = planet.Entities.EntityQueryEnumerator<XenoWeedsComponent, TransformComponent>();

        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == planet.Ground)
                tiles.Add(maps.CoordinatesToTile(planet.Ground, planet.Grid, xform.Coordinates));
        }

        return tiles;
    }

    /// <summary>A node on bare terrain spreads weeds over the whole block, whatever tiles are under it.</summary>
    [Test]
    public async Task WeedsSpreadOverTerrain([ValueSource(typeof(Content.IntegrationTests.Tests._WF.Caverns.CavernFixture), nameof(Surfaces))] string surface, [Values] bool underground)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair, surface, Spread, underground);
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            var hive = SpawnHive(planet);
            var node = planet.Entities.SpawnEntity("XenoWeedsSource", planet.At(centre));
            SetHive(planet, hive, node);
        });

        await pair.RunSeconds(45);

        await server.WaitAssertion(() =>
        {
            var covered = WeededTiles(planet);
            var missing = new List<Vector2i>();
            var kinds = new HashSet<string>();

            for (var x = -Spread; x <= Spread; x++)
            for (var y = -Spread; y <= Spread; y++)
            {
                var index = centre + new Vector2i(x, y);
                kinds.Add(server.ResolveDependency<ITileDefinitionManager>()[maps.GetTileRef(planet.Grid, planet.Grid, index).Tile.TypeId].ID);

                if (!covered.Contains(index))
                    missing.Add(index);
            }

            Assert.That(missing, Is.Empty, $"Weeds did not reach these tiles of {string.Join(", ", kinds)} on {surface} (underground {underground}).");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Every tile a surface's biome lays, and every cavern floor, is weedable, solid and diggable.</summary>
    [Test]
    public async Task EveryTerrainTileTakesAHive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            var ids = new HashSet<string>();

            foreach (var surface in Surfaces)
            {
                var proto = prototypes.Index<Content.Shared._WF.Planets.WFPlanetSurfacePrototype>(surface);
                var planet = prototypes.Index<Content.Shared._DV.Planet.PlanetPrototype>(proto.Ground);
                CollectTiles(prototypes, planet.Biome, ids, new HashSet<string>());
                ids.Add(Floors[surface].Substrate);
                ids.Add(Floors[surface].Chamber);
            }

            Assert.That(ids, Does.Contain("FloorPlanetGrass").And.Contain("FloorSnow").And.Contain("FloorBasalt"),
                "The scan missed the biomes' tiles.");

            var bad = new List<string>();

            foreach (var id in ids)
            {
                var def = (ContentTileDefinition) tiles[id];

                if (!def.WeedsSpreadable || !def.Sturdy || !def.CanPlaceTunnel || def.MapAtmosphere)
                    bad.Add($"{id} (weeds {def.WeedsSpreadable}, sturdy {def.Sturdy}, tunnel {def.CanPlaceTunnel}, open {def.MapAtmosphere})");
            }

            Assert.That(bad, Is.Empty, "These terrain tiles refuse part of a hive.");
        });

        await pair.CleanReturnAsync();
    }

    private static void CollectTiles(IPrototypeManager prototypes, string template, HashSet<string> ids, HashSet<string> seen)
    {
        if (!seen.Add(template))
            return;

        foreach (var layer in prototypes.Index<BiomeTemplatePrototype>(template).Layers)
        {
            switch (layer)
            {
                case BiomeTileLayer tile:
                    ids.Add(tile.Tile);
                    break;
                case BiomeMetaLayer meta:
                    CollectTiles(prototypes, meta.Template, ids, seen);
                    break;
            }
        }
    }

    /// <summary>A builder secretes a resin wall on hive weeds, and a queen orders a hive core and its nodes there.</summary>
    [Test]
    public async Task ResinAndNodesGoUpOnTerrain([Values] bool underground)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair, underground: underground);
        var entities = planet.Entities;

        EntityUid hive = default;
        EntityUid builder = default;
        EntityUid queen = default;
        var wallTile = centre + new Vector2i(1, 0);

        await server.WaitPost(() =>
        {
            hive = SpawnHive(planet);

            for (var x = -Reach; x <= Reach; x++)
            for (var y = -Reach; y <= Reach; y++)
            {
                SetHive(planet, hive, entities.SpawnEntity("XenoWeeds", planet.At(centre + new Vector2i(x, y))));
            }

            builder = entities.SpawnEntity("WFTestPlanetResinBuilder", planet.At(centre));
            SetHive(planet, hive, builder);
            queen = entities.SpawnEntity("CMXenoQueen", planet.At(centre + new Vector2i(-2, 0)));
            SetHive(planet, hive, queen);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new XenoSecreteStructureActionEvent { Performer = builder, Target = planet.At(wallTile) };
            entities.EventBus.RaiseLocalEvent(builder, ev);
            Assert.That(ev.Handled, "Secreting a wall on weeds over terrain should start building.");
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            var built = 0;
            var query = entities.EntityQueryEnumerator<XenoConstructComponent, MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var meta, out var xform))
            {
                if (meta.EntityPrototype?.ID == "WallXenoResin" && xform.GridUid == planet.Ground)
                    built++;
            }

            Assert.That(built, Is.EqualTo(1), "The resin wall should stand on the planet grid.");
        });

        // The hive core node, then plasma to finish it.
        const string coreNode = "HiveCoreXenoConstructionNode";
        var coreTile = centre + new Vector2i(-2, 1);
        EntityUid core = default;

        await server.WaitAssertion(() =>
        {
            entities.System<XenoPlasmaSystem>().SetPlasma((queen, entities.GetComponent<XenoPlasmaComponent>(queen)), FixedPoint2.New(1000));
            var construction = entities.GetComponent<XenoConstructionComponent>(queen);
            var system = entities.System<SharedXenoConstructionSystem>();

            Assert.That(system.CanSecreteOnTilePopup((queen, construction), "WallXenoResin", planet.At(coreTile), true, true, false),
                Is.True, "The queen can secrete on a weeded terrain tile.");
            Assert.That(system.CanOrderConstructionPopup((queen, construction), planet.At(coreTile), coreNode, false),
                Is.True, "The queen can order a hive core on weeded terrain.");

            var ev = new XenoOrderConstructionDoAfterEvent(coreNode, entities.GetNetCoordinates(planet.At(coreTile)));
            Finish(planet, ev, queen, null);
            entities.EventBus.RaiseLocalEvent(queen, ev);
            Assert.That(ev.Handled, "The order event ran.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var query = entities.EntityQueryEnumerator<HiveConstructionNodeComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == coreNode)
                    core = uid;
            }

            Assert.That(core, Is.Not.EqualTo(default(EntityUid)), "The hive core node was not placed on the planet.");
            Assert.That(entities.GetComponent<TransformComponent>(core).GridUid, Is.EqualTo(planet.Ground));
        });

        // Feeding the node its plasma turns it into the core.
        await server.WaitAssertion(() =>
        {
            entities.System<XenoPlasmaSystem>().SetPlasma((queen, entities.GetComponent<XenoPlasmaComponent>(queen)), FixedPoint2.New(1000));
            var ev = new XenoConstructionAddPlasmaDoAfterEvent();
            Finish(planet, ev, queen, core);
            entities.EventBus.RaiseLocalEvent(queen, ev);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var built = false;
            var query = entities.EntityQueryEnumerator<HiveCoreComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                built |= xform.GridUid == planet.Ground;
            }

            Assert.That(built, "The hive core was not built on the planet.");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>A drone plants an egg on hive weeds over terrain and it grows there.</summary>
    [Test]
    public async Task EggPlantsOnHiveWeeds([Values] bool underground)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair, underground: underground);
        var entities = planet.Entities;
        var hands = entities.System<SharedHandsSystem>();
        var interaction = entities.System<SharedInteractionSystem>();

        EntityUid egg = default;
        EntityUid drone = default;
        var eggTile = centre + new Vector2i(1, 0);

        await server.WaitPost(() =>
        {
            var hive = SpawnHive(planet);

            for (var x = -Reach; x <= Reach; x++)
            for (var y = -Reach; y <= Reach; y++)
            {
                SetHive(planet, hive, entities.SpawnEntity("XenoHiveWeeds", planet.At(centre + new Vector2i(x, y))));
            }

            drone = entities.SpawnEntity("CMXenoDrone", planet.At(centre));
            SetHive(planet, hive, drone);
            entities.System<XenoPlasmaSystem>().SetPlasma((drone, entities.GetComponent<XenoPlasmaComponent>(drone)), FixedPoint2.New(100));
            egg = entities.SpawnEntity("XenoEgg", planet.At(centre));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(hands.TryPickupAnyHand(drone, egg), "The drone picks up the egg.");
            interaction.UserInteraction(drone, planet.At(eggTile), null, checkCanInteract: false);
        });

        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<XenoEggComponent>(egg);
            Assert.That(entities.GetComponent<TransformComponent>(egg).Anchored, "The egg is planted.");
            Assert.That(comp.State, Is.EqualTo(XenoEggState.Growing));
        });

        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<XenoFragileEggComponent>(egg), Is.False, "The egg on hive weeds stays healthy."));

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Tunnels and burrowing work on the ground and never reach the cavern under it.</summary>
    [Test]
    public async Task TunnelsAndBurrowingStayOnTheGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair);
        var entities = planet.Entities;
        var tunnels = entities.System<XenoTunnelSystem>();
        var maps = entities.System<SharedMapSystem>();

        Assert.That(planet.World.Cavern, Is.Not.EqualTo(EntityUid.Invalid), "The world has no cavern.");

        EntityUid hive = default;
        EntityUid burrower = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid below = default;
        var tunnelTile = centre + new Vector2i(-2, 0);
        var burrowTile = centre + new Vector2i(2, 2);
        EntityCoordinates cavernCoords = default;

        await server.WaitPost(() =>
        {
            hive = SpawnHive(planet);

            var cavern = planet.World.Cavern;
            var cavernGrid = entities.GetComponent<MapGridComponent>(cavern);
            maps.SetTile(cavern, cavernGrid, centre, new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId));
            cavernCoords = new EntityCoordinates(cavern, centre.X + 0.5f, centre.Y + 0.5f);

            Assert.That(tunnels.TryPlaceTunnel(hive, "ground one", planet.At(tunnelTile), out var one), "A tunnel on the ground registers.");
            Assert.That(tunnels.TryPlaceTunnel(hive, "ground two", planet.At(centre + new Vector2i(0, -3)), out var two), "A second ground tunnel registers.");
            Assert.That(tunnels.TryPlaceTunnel(hive, "cavern", cavernCoords, out var under), "A cavern tunnel registers.");
            first = one!.Value;
            second = two!.Value;
            below = under!.Value;

            burrower = entities.SpawnEntity("CMXenoBurrower", planet.At(centre));
            SetHive(planet, hive, burrower);
            var burrowed = new BurrowedEvent(true);
            entities.EventBus.RaiseLocalEvent(burrower, ref burrowed, false);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tunnels.GetTravelDestinations(first).Keys, Is.EquivalentTo(new[] { "ground two" }),
                "Only the ground tunnel is a destination from the ground.");
            Assert.That(tunnels.SameGrid(first, below), Is.False);

            var toCavern = new XenoBurrowActionEvent { Performer = burrower, Target = cavernCoords };
            entities.EventBus.RaiseLocalEvent(burrower, toCavern);
            Assert.That(entities.HasComponent<Content.Shared.DoAfter.ActiveDoAfterComponent>(burrower), Is.False,
                "Burrowing down into the cavern is refused.");

            var toTile = new XenoBurrowActionEvent { Performer = burrower, Target = planet.At(burrowTile) };
            entities.EventBus.RaiseLocalEvent(burrower, toTile);
            Assert.That(entities.HasComponent<Content.Shared.DoAfter.ActiveDoAfterComponent>(burrower),
                "Burrowing across the ground starts.");
        });

        await pair.RunSeconds(20);

        await server.WaitAssertion(() =>
        {
            var xform = entities.GetComponent<TransformComponent>(burrower);
            Assert.That(xform.GridUid, Is.EqualTo(planet.Ground));
            Assert.That(maps.CoordinatesToTile(planet.Ground, planet.Grid, xform.Coordinates), Is.EqualTo(burrowTile),
                "The burrower surfaced where it tunnelled to.");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>A queen standing on the surface takes no pressure or temperature harm in the planet's air.</summary>
    [Test]
    public async Task QueenLivesInPlanetAir([ValueSource(typeof(Content.IntegrationTests.Tests._WF.Caverns.CavernFixture), nameof(Surfaces))] string surface, [Values] bool underground)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair, surface, 0, underground);
        var entities = planet.Entities;

        EntityUid queen = default;

        await server.WaitPost(() =>
        {
            var hive = SpawnHive(planet);
            SetHive(planet, hive, entities.SpawnEntity("XenoWeedsSource", planet.At(centre)));
            queen = entities.SpawnEntity("CMXenoQueen", planet.At(centre));
            SetHive(planet, hive, queen);
        });

        var worst = FixedPoint2.Zero;

        for (var i = 0; i < 30; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var total = entities.GetComponent<DamageableComponent>(queen).TotalDamage;
                if (total > worst)
                    worst = total;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(queen), Is.False);
            Assert.That(entities.GetComponent<TransformComponent>(queen).MapUid, Is.EqualTo(planet.Ground), "The queen fell or rose off her layer.");
            Assert.That(worst, Is.EqualTo(FixedPoint2.Zero), $"The queen took damage standing on {surface} (underground {underground}).");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Weeds, walls, tunnels and cores stay put when their chunk unloads, and are still there when it loads again.</summary>
    [Test]
    public async Task StructuresSurviveChunkUnload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair);
        var entities = planet.Entities;
        var maps = entities.System<SharedMapSystem>();
        var from = centre - new Vector2i(Reach + 8, Reach + 8);
        var to = centre + new Vector2i(Reach + 8, Reach + 8);

        EntityUid hive = default;
        var wall = EntityUid.Invalid;
        var tunnel = EntityUid.Invalid;
        var core = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            hive = SpawnHive(planet);
            SetHive(planet, hive, entities.SpawnEntity("XenoWeedsSource", planet.At(centre)));
            wall = entities.SpawnEntity("WallXenoResin", planet.At(centre + new Vector2i(4, 0)));
            core = entities.SpawnEntity("HiveCoreXeno", planet.At(centre + new Vector2i(0, -4)));
            SetHive(planet, hive, wall);
            SetHive(planet, hive, core);
            Assert.That(entities.System<XenoTunnelSystem>().TryPlaceTunnel(hive, "unload", planet.At(centre + new Vector2i(-4, 0)), out var t));
            tunnel = t!.Value;
        });

        await pair.RunSeconds(30);

        var weeds = new HashSet<Vector2i>();
        await server.WaitPost(() => weeds = WeededTiles(planet));
        Assert.That(weeds, Has.Count.GreaterThan(10), "Precondition: the node spread.");

        await UnloadChunks(pair, planet.Ground, from, to);
        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in new[] { wall, core, tunnel })
            {
                Assert.That(entities.Deleted(uid), Is.False, $"{entities.ToPrettyString(uid)} was deleted with its chunk.");
                Assert.That(entities.GetComponent<TransformComponent>(uid).MapUid, Is.EqualTo(planet.World.Ground),
                    "A structure left the planet layer when its chunk unloaded.");
            }

            var left = WeededTiles(planet);
            Assert.That(left, Is.EquivalentTo(weeds), "Weeds were lost or gained when the chunk unloaded.");

            foreach (var tile in left)
            {
                Assert.That(maps.TryGetTileRef(planet.Ground, planet.Grid, tile, out var t) && !t.Tile.IsEmpty,
                    $"The ground under weeds at {tile} was unloaded.");
            }
        });

        await LoadChunks(pair, planet.Ground, from, to);
        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(WeededTiles(planet), Is.EquivalentTo(weeds), "Weeds changed when the chunk loaded again.");

            var cores = 0;
            var query = entities.EntityQueryEnumerator<HiveCoreComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == planet.Ground)
                    cores++;
            }

            Assert.That(cores, Is.EqualTo(1));
            Assert.That(entities.GetComponent<TransformComponent>(tunnel).Anchored, "The tunnel came back loose.");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>A drone's acid melts a wall built on terrain.</summary>
    [Test]
    public async Task AcidMeltsWallOnTerrain()
    {
        const string targetProto = "WallSolid";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair);
        var entities = planet.Entities;

        EntityUid drone = default, target = default;
        await server.WaitPost(() =>
        {
            target = entities.SpawnEntity(targetProto, planet.At(centre + new Vector2i(1, 0)));
            drone = entities.SpawnEntity("CMXenoDrone", planet.At(centre));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var plasma = entities.GetComponent<XenoPlasmaComponent>(drone);
            entities.System<XenoPlasmaSystem>().SetPlasma((drone, plasma), plasma.MaxPlasma);

            var action = entities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoCorrosiveAcidEvent>(drone).First();
            var targetAction = entities.GetComponent<EntityTargetActionComponent>(action);
            var proto = (XenoCorrosiveAcidEvent) targetAction.Event!;
            var ev = new XenoCorrosiveAcidEvent
            {
                Performer = drone,
                Target = target,
                Action = action,
                AcidId = proto.AcidId,
                Strength = proto.Strength,
                PlasmaCost = proto.PlasmaCost,
                Dps = proto.Dps,
                Time = TimeSpan.FromSeconds(1),
            };
            entities.EventBus.RaiseLocalEvent(drone, ev);
            Assert.That(ev.Handled, "The drone should start applying acid on the planet.");
        });

        await pair.RunSeconds(4);
        await server.WaitAssertion(() => Assert.That(entities.Deleted(target), $"The acid should melt the {targetProto}."));

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>A xeno set alight on the surface burns and goes out without breaking the terrain's air.</summary>
    [Test]
    public async Task FireOnTerrainBurnsAndGoesOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair);
        var entities = planet.Entities;

        EntityUid drone = default;
        await server.WaitPost(() =>
        {
            drone = entities.SpawnEntity("CMXenoDrone", planet.At(centre));
            entities.System<FlammableSystem>().AdjustFireStacks(drone, 5, ignite: true);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            var flammable = entities.GetComponent<FlammableComponent>(drone);
            Assert.That(flammable.OnFire, "The drone should be burning.");
            Assert.That(entities.GetComponent<DamageableComponent>(drone).TotalDamage, Is.GreaterThan(FixedPoint2.Zero),
                "Fire should hurt a drone on the surface.");
        });

        await pair.RunSeconds(60);

        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<FlammableComponent>(drone).OnFire, Is.False, "The fire should burn out."));

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Liquid and chasm tiles take no resin, nodes or tunnels, as RMC's water takes none.</summary>
    [TestCase("MonoFloorWaterEntity")]
    [TestCase("FloorLavaEntity")]
    [TestCase("FloorChasmEntity")]
    public async Task HiveStaysOffLiquidAndChasmTiles(string liquid)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair);
        var entities = planet.Entities;
        var constructionSystem = entities.System<SharedXenoConstructionSystem>();

        EntityUid queen = default;
        var wet = centre + new Vector2i(1, 0);
        var dry = centre + new Vector2i(-1, 0);

        await server.WaitPost(() =>
        {
            var hive = SpawnHive(planet);
            queen = entities.SpawnEntity("CMXenoQueen", planet.At(centre));
            SetHive(planet, hive, queen);
            entities.System<XenoPlasmaSystem>().SetPlasma((queen, entities.GetComponent<XenoPlasmaComponent>(queen)), FixedPoint2.New(1000));
            entities.SpawnEntity(liquid, planet.At(wet));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var construction = entities.GetComponent<XenoConstructionComponent>(queen);
            const string core = "HiveCoreXenoConstructionNode";

            Assert.That(constructionSystem.CanOrderConstructionPopup((queen, construction), planet.At(dry), core, false),
                Is.True, "Precondition: the queen can order a hive core on dry ground.");
            Assert.That(constructionSystem.CanPlaceXenoStructure(queen, planet.At(dry), out _, false),
                Is.True, "Precondition: a tunnel can go on dry ground.");

            Assert.That(constructionSystem.CanOrderConstructionPopup((queen, construction), planet.At(wet), core, false),
                Is.False, $"A hive core was allowed on {liquid}.");
            Assert.That(constructionSystem.CanPlaceXenoStructure(queen, planet.At(wet), out _, false),
                Is.False, $"A tunnel was allowed on {liquid}.");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>The queen's eye flies over the terrain, loads the ground it looks at and stays on the surface.</summary>
    [Test]
    public async Task QueenEyeLoadsTerrainItLooksAt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var (planet, centre) = await Create(pair);
        var entities = planet.Entities;
        var biomes = server.System<BiomeSystem>();
        var session = server.PlayerMan.Sessions[0];

        EntityUid queen = default;
        EntityUid eye = default;
        // Past the hand-loaded area, along a line of weed nodes the eye can follow.
        var far = new Vector2i(centre.X + 90, centre.Y);

        await server.WaitPost(() =>
        {
            var hive = SpawnHive(planet);
            queen = entities.SpawnEntity("CMXenoQueen", planet.At(centre));
            SetHive(planet, hive, queen);
            SetHive(planet, hive, entities.SpawnEntity("XenoWeedsSource", planet.At(centre)));
            entities.System<MindSystem>().TransferTo(entities.System<MindSystem>().GetOrCreateMind(session.UserId), queen);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            entities.EventBus.RaiseLocalEvent(queen, new QueenEyeActionEvent { Performer = queen });
            eye = entities.GetComponent<QueenEyeActionComponent>(queen).Eye!.Value;
        });

        await server.WaitAssertion(() =>
            Assert.That(biomes.WfIsChunkLoaded((planet.Ground, entities.GetComponent<BiomeComponent>(planet.Ground)), far), Is.False,
                "Precondition: the queen's body already loads the ground far away."));

        // Walk the eye out a node at a time, as a player's cursor does.
        for (var step = 1; step <= 30; step++)
        {
            var target = new Vector2i(centre.X + step * 3, centre.Y);
            await server.WaitPost(() =>
            {
                entities.System<SharedTransformSystem>().SetCoordinates(eye, planet.At(target));
                // A node under the eye keeps it inside its weed range.
                if (biomes.WfIsChunkLoaded((planet.Ground, entities.GetComponent<BiomeComponent>(planet.Ground)), target))
                    entities.SpawnEntity("XenoWeedsSource", planet.At(target));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
        }

        await server.WaitAssertion(() =>
        {
            var xform = entities.GetComponent<TransformComponent>(eye);
            Assert.That(xform.MapUid, Is.EqualTo(planet.Ground), "The eye left the surface.");
            Assert.That(biomes.WfIsChunkLoaded((planet.Ground, entities.GetComponent<BiomeComponent>(planet.Ground)), far), Is.True,
                "The terrain the queen's eye looks at was not loaded.");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }
}
