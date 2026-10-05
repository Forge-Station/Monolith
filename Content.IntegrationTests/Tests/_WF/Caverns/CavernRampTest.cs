#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server.Construction;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets.Parachute;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// Stairs built on a cavern floor: they open the ground above them, a mob walks them up and down with one map change
/// each way and no fall, and the hole they leave behind is fitted out like any other. Section 3.8's gates.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernStairsSystem))]
public sealed class CavernRampTest
{
    private const string Surface = "WFSurfaceMerak";
    private const string Stairs = "WFCavernStairs";
    private const string Mob = "MobHuman";
    private const string BuiltWall = "WallSolid";
    private const string BuiltFloor = "Plating";
    private const string Steel = "SheetSteel";
    private const string Crate = "CrateGeneric";

    /// <summary>How far a walking mob is moved between looks, in tiles.</summary>
    private const float Stride = 0.05f;

    /// <summary>How far a creeping mob is moved each tick, in tiles: slow enough to come to rest on every step.</summary>
    private const float Creep = 0.01f;

    /// <summary>How far along stairs, from their foot, the height curve crosses one level.</summary>
    private const float FlipLine = 0.4737f;

    /// <summary>Where the walking tests build: east of the planet centre, clear of the gate.</summary>
    private static readonly Vector2i Site = new(200, 40);

    /// <summary>Where the natural-ground tests look for a site.</summary>
    private static readonly Vector2i SearchFrom = new(150, -120);
    private static readonly Vector2i SearchTo = new(420, 120);

    private static readonly Direction[] Facings = { Direction.South, Direction.East, Direction.North, Direction.West };

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    /// <summary>
    /// Stairs facing each way open the tile above them. Walked up in 0.05-tile steps a mob changes map once and stands
    /// on the ground at their top; walked back down it changes map once and stands on the cavern floor, unhurt.
    /// </summary>
    [Test]
    public async Task StairsAreWalkedUpAndDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(6 * Facings.Length + 8, 8));

            for (var i = 0; i < Facings.Length; i++)
            {
                var facing = Facings[i];
                var index = Site + new Vector2i(6 * i, 0);
                var up = -facing.ToIntVec();

                await ClearSite(pair, world, index, 2);
                var stairs = await SpawnStairs(pair, world, index, facing);
                await AssertOpened(pair, world, stairs, index, index + up, $"{facing}");

                var foot = TileCentre(index - up);
                var top = TileCentre(index + up);
                var mob = await SpawnSettled(pair, world.Cavern, foot);

                var changes = await Walk(pair, mob, foot, top);
                await server.WaitRunTicks(30);
                await AssertStands(pair, mob, world.Ground, index + up, changes, $"{facing}: walking up");

                changes = await Walk(pair, mob, top, foot);
                await server.WaitRunTicks(30);
                await AssertStands(pair, mob, world.Cavern, index - up, changes, $"{facing}: walking down");

                await server.WaitPost(() => entMan.DeleteEntity(mob));
            }
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The recipe, started by someone at the foot with a stack of steel to hand, takes ten sheets and builds stairs
    /// facing the way they were placed, which open the ground and carry their builder up.
    /// </summary>
    [Test]
    public async Task TheRecipeBuildsStairs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var construction = server.System<ConstructionSystem>();
        var maps = server.System<SharedMapSystem>();

        // Facing east they climb west.
        var foot = Site + new Vector2i(1, 0);
        var exit = Site + new Vector2i(-1, 0);

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 2);

            var user = await SpawnSettled(pair, world.Cavern, TileCentre(foot));
            var steel = EntityUid.Invalid;
            var sheets = 0;

            await server.WaitPost(() =>
            {
                steel = entMan.SpawnEntity(Steel, new EntityCoordinates(world.Cavern, TileCentre(foot)));
                sheets = entMan.GetComponent<StackComponent>(steel).Count;
            });
            await server.WaitRunTicks(5);

            Task<bool>? build = null;
            await server.WaitPost(() =>
                build = construction.TryStartStructureConstruction(user, Stairs, new EntityCoordinates(world.Cavern, TileCentre(Site)), Direction.East.ToAngle()));
            await server.WaitRunTicks(pair.SecondsToTicks(10f));

            Assert.That(build is { IsCompletedSuccessfully: true }, Is.True, "The recipe was still building after ten seconds.");
            Assert.That(await build!, Is.True, "The recipe did not build stairs.");

            var stairs = EntityUid.Invalid;
            await server.WaitPost(() =>
            {
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                stairs = maps.GetAnchoredEntities(world.Cavern, levelGrid, Site).FirstOrDefault(entMan.HasComponent<WFCavernStairsComponent>);
            });

            Assert.That(stairs.IsValid(), Is.True, "The recipe left no stairs on the tile.");
            await server.WaitRunTicks(3);
            await AssertOpened(pair, world, stairs, Site, exit, "built by the recipe");

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(stairs).LocalRotation.GetCardinalDir(), Is.EqualTo(Direction.East),
                        "The stairs do not face the way they were placed.");
                    Assert.That(entMan.GetComponent<StackComponent>(steel).Count, Is.EqualTo(sheets - 10), "The recipe did not take ten sheets of steel.");
                }
            });

            var changes = await Walk(pair, user, TileCentre(foot), TileCentre(exit));
            await server.WaitRunTicks(30);
            await AssertStands(pair, user, world.Ground, exit, changes, "Walking up the built stairs");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A crate pulled up the stairs and back down by someone with a tool in the other hand changes level with its
    /// puller each time, and stays pulled.
    /// </summary>
    [Test]
    public async Task WhatIsPulledFollowsOverTheStairs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = server.System<SharedHandsSystem>();
        var pulling = server.System<PullingSystem>();
        var below = TileCentre(Site - new Vector2i(0, 1));
        var above = TileCentre(Site + new Vector2i(0, 2));

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 3);
            var stairs = await SpawnStairs(pair, world, Site, Direction.South);
            await AssertOpened(pair, world, stairs, Site, Site + new Vector2i(0, 1), "pulling");

            var mob = await SpawnSettled(pair, world.Cavern, below);
            var crate = EntityUid.Invalid;
            var started = false;

            await server.WaitPost(() => crate = entMan.SpawnEntity(Crate, new EntityCoordinates(world.Cavern, TileCentre(Site - new Vector2i(0, 2)))));
            await server.WaitRunTicks(10);
            await server.WaitPost(() =>
            {
                var tool = entMan.SpawnEntity("Crowbar", entMan.GetComponent<TransformComponent>(mob).Coordinates);
                started = hands.TryPickupAnyHand(mob, tool) && pulling.TryStartPull(mob, crate);
            });
            Assert.That(started, Is.True, "Precondition: the mob could not take a tool in one hand and the crate in the other.");

            async Task AssertFollowed(EntityUid map, string what)
            {
                await server.WaitRunTicks(30);
                await server.WaitAssertion(() =>
                {
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(map), $"{what}: the puller is on the wrong map.");
                        Assert.That(entMan.GetComponent<TransformComponent>(crate).MapUid, Is.EqualTo(map), $"{what}: the crate was left behind.");
                        Assert.That(entMan.GetComponent<PullerComponent>(mob).Pulling, Is.EqualTo(crate), $"{what}: the crate is no longer pulled.");
                        Assert.That(entMan.GetComponent<CEZPhysicsComponent>(crate).Velocity, Is.Zero, $"{what}: the crate is still falling.");
                    }
                });
            }

            await Walk(pair, mob, below, TileCentre(Site + new Vector2i(0, 1)));
            await AssertFollowed(world.Ground, "At the top of the stairs");

            await Walk(pair, mob, TileCentre(Site + new Vector2i(0, 1)), above);
            await AssertFollowed(world.Ground, "A tile past the top");

            await Walk(pair, mob, above, TileCentre(Site - new Vector2i(0, 2)));
            await AssertFollowed(world.Cavern, "Back at the bottom");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A mob left on or beside the line where the stairs cross one level changes map at most once in 120 ticks.</summary>
    [Test]
    public async Task AMobOnTheFlipLineSettles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 2);
            var stairs = await SpawnStairs(pair, world, Site, Direction.South);
            await AssertOpened(pair, world, stairs, Site, Site + new Vector2i(0, 1), "flip line");

            var mob = await SpawnSettled(pair, world.Cavern, TileCentre(Site - new Vector2i(0, 1)));

            foreach (var offset in new[] { -0.002f, 0f, 0.002f })
            {
                var changes = 0;
                EntityUid? map = null;

                await server.WaitPost(() =>
                {
                    transform.SetWorldPosition(mob, new Vector2(Site.X + 0.5f, Site.Y + FlipLine + offset));
                    map = entMan.GetComponent<TransformComponent>(mob).MapUid;
                });

                for (var tick = 0; tick < 120; tick++)
                {
                    await server.WaitRunTicks(1);
                    await server.WaitPost(() =>
                    {
                        var now = entMan.GetComponent<TransformComponent>(mob).MapUid;
                        if (now != map)
                            changes++;

                        map = now;
                    });
                }

                await server.WaitAssertion(() =>
                {
                    var physics = entMan.GetComponent<CEZPhysicsComponent>(mob);

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(changes, Is.LessThanOrEqualTo(1), $"Offset {offset}: the mob changed map {changes} times on the flip line.");
                        Assert.That(physics.Velocity, Is.Zero, $"Offset {offset}: the mob never came to rest on the flip line.");
                        Assert.That(Blunt(entMan, mob), Is.Zero, $"Offset {offset}: standing on the flip line hurt the mob.");
                    }
                });
            }
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Crept down a hundredth of a tile a tick, the stairs hand a mob down a level without a fall: the parachute it
    /// wears stays shut. Stepping into a plain hole beside them opens one.
    /// </summary>
    [Test]
    public async Task CreepingDownTheStairsIsNotAFall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var top = TileCentre(Site + new Vector2i(0, 1));
        var hole = Site + new Vector2i(4, 0);

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 2);
            await ClearSite(pair, world, hole, 2);
            var stairs = await SpawnStairs(pair, world, Site, Direction.South);
            await AssertOpened(pair, world, stairs, Site, Site + new Vector2i(0, 1), "creeping");

            var mob = await SpawnSettled(pair, world.Ground, top);
            await server.WaitPost(() => entMan.AddComponent<WFParachutedComponent>(mob));

            for (var moved = 0f; moved <= 2f; moved += Creep)
            {
                var position = top - new Vector2(0f, moved);

                await server.WaitPost(() => transform.SetWorldPosition(mob, position));
                await server.WaitRunTicks(1);
            }

            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "Creeping down the stairs did not reach the cavern.");
                    Assert.That(entMan.TryGetComponent(mob, out WFParachutedComponent? chute) && !chute.Deployed, Is.True,
                        "Creeping down the stairs counted as a fall: the parachute opened.");
                    Assert.That(Blunt(entMan, mob), Is.Zero, "Creeping down the stairs hurt the mob.");
                }
            });

            // The same parachute does open over a drop.
            await server.WaitPost(() => maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), hole, Tile.Empty));
            await server.WaitRunTicks(3);

            var faller = await SpawnSettled(pair, world.Ground, TileCentre(hole + new Vector2i(0, 1)));
            await server.WaitPost(() =>
            {
                entMan.AddComponent<WFParachutedComponent>(faller);
                transform.SetWorldPosition(faller, TileCentre(hole));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(3f));

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(faller).MapUid, Is.EqualTo(world.Cavern),
                        "Precondition: the mob did not drop down the hole.");
                    Assert.That(!entMan.TryGetComponent(faller, out WFParachutedComponent? chute) || chute.Deployed, Is.True,
                        "A drop down a plain hole no longer counts as a fall.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Stepping into the stairwell from beside it, over the low end of the stairs, sets a mob down on them unhurt; and
    /// stepping onto their high end from beside them in the cavern brings it out on the ground.
    /// </summary>
    [Test]
    public async Task SteppingInFromTheSideDoesNotHurt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 2);
            var stairs = await SpawnStairs(pair, world, Site, Direction.South);
            await AssertOpened(pair, world, stairs, Site, Site + new Vector2i(0, 1), "side entry");

            // Down the stairwell from the ground, over the stairs' low end: the longest drop there is, 0.8 of a level.
            var beside = new Vector2(Site.X - 0.5f, Site.Y + 0.2f);
            var mob = await SpawnSettled(pair, world.Ground, beside);
            await Walk(pair, mob, beside, beside + new Vector2(1f, 0f));
            await server.WaitRunTicks(60);

            await server.WaitAssertion(() =>
            {
                var physics = entMan.GetComponent<CEZPhysicsComponent>(mob);
                var blunt = Blunt(entMan, mob);
                TestContext.Out.WriteLine($"Side entry from the ground: {blunt} Blunt.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "A mob stepping into the stairwell did not come down onto the stairs.");
                    Assert.That(physics.Velocity, Is.Zero, "The mob never came to rest on the stairs.");
                    Assert.That(blunt, Is.LessThan(20), "Section 3.8: side entry must deal under 20 Blunt.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive));
                }
            });

            await server.WaitPost(() => entMan.DeleteEntity(mob));

            // Up from the cavern floor, onto the stairs' high end from beside them.
            beside = new Vector2(Site.X - 0.5f, Site.Y + 0.8f);
            mob = await SpawnSettled(pair, world.Cavern, beside);
            await Walk(pair, mob, beside, beside + new Vector2(1f, 0f));
            await server.WaitRunTicks(60);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                        "A mob stepping onto the stairs' high end did not come out on the ground.");
                    Assert.That(Blunt(entMan, mob), Is.Zero, "Stepping onto the stairs' high end hurt the mob.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// On loaded ground the stairs' hole is pinned and shaded and takes what the biome grew at their top with it, but
    /// gets no landing and no climb point: the rock beside the stairs stays. Once the stairs go, the hole gets both.
    /// </summary>
    [Test]
    public async Task StairsStandInForTheLandingAndClimb()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var index = await FindOvergrownSite(pair, world);
            var exit = index + new Vector2i(0, 1);
            var rockAt = index + new Vector2i(1, 0);
            await LoadBoth(pair, world, index - new Vector2i(8, 8), index + new Vector2i(8, 8));

            var rock = EntityUid.Invalid;
            var floor = Tile.Empty;
            var climbs = 0;
            var grown = false;

            await server.WaitPost(() =>
            {
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                grown = maps.GetAnchoredEntities(world.Ground, groundGrid, exit).Any();

                foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, levelGrid, index).ToList())
                {
                    entMan.DeleteEntity(anchored);
                }

                rock = maps.GetAnchoredEntities(world.Cavern, levelGrid, rockAt).FirstOrDefault();
                floor = maps.GetTileRef(world.Cavern, levelGrid, index).Tile;
                climbs = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).ClimbPoints.Count;
            });

            Assert.That(grown, Is.True, "Precondition: the biome grew nothing at the top of the stairs.");
            Assert.That(rock.IsValid(), Is.True, "Precondition: no rock beside the stairs.");

            var stairs = await SpawnStairs(pair, world, index, Direction.South);
            await AssertOpened(pair, world, stairs, index, exit, "overgrown ground");

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(maps.GetAnchoredEntities(world.Ground, groundGrid, exit).Any(), Is.False,
                        "What the biome grew at the top of the stairs is still in the way.");
                    Assert.That(ground.ClimbPoints, Has.Count.EqualTo(climbs), "The stairs' hole was given a climb point.");
                    Assert.That(entMan.Deleted(rock), Is.False, "The rock beside the stairs was cleared for a landing.");
                    Assert.That(maps.GetTileRef(world.Cavern, levelGrid, index).Tile.TypeId, Is.EqualTo(floor.TypeId),
                        "The floor under the stairs was relaid as a landing.");
                }
            });

            await server.WaitPost(() => entMan.DeleteEntity(stairs));
            await server.WaitRunTicks(3);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var landing = CavernOf(pair, Surface).Mouths.LandingTile.Id;
                var below = maps.GetTileRef(world.Cavern, levelGrid, index).Tile;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, index), Is.True, "The hole closed when its stairs went.");
                    Assert.That(ground.Shades.ContainsKey(index), Is.True, "The hole lost its shade with its stairs.");
                    Assert.That(below.IsEmpty ? string.Empty : tileDefs[below.TypeId].ID, Is.EqualTo(landing),
                        "The hole got no landing once its stairs went.");
                    Assert.That(ground.ClimbPoints.Keys.Any(climb => Chebyshev(climb, index) <= 1), Is.True,
                        "The hole got no climb point once its stairs went.");
                    Assert.That(biomes.WfIsPinned((world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern)), index), Is.True,
                        "The landing under the hole is not pinned.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Under ground that isn't loaded the stairs still open their hole and lay their exit, and a mob walks up them
    /// onto it; when the ground loads the hole is still a hole and the exit is solid and grows nothing.
    /// </summary>
    [Test]
    public async Task StairsOpenGroundThatIsNotLoaded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var index = await FindOvergrownSite(pair, world);
            var exit = index + new Vector2i(0, 1);
            await LoadChunks(pair, world.Cavern, index - new Vector2i(8, 8), index + new Vector2i(8, 8));

            await server.WaitAssertion(() =>
            {
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                Assert.That(IsEmpty(maps, world.Ground, groundGrid, index) && IsEmpty(maps, world.Ground, groundGrid, exit), Is.True,
                    "Precondition: the ground over the site is loaded.");
            });

            var stairs = await SpawnStairs(pair, world, index, Direction.South);
            await AssertOpened(pair, world, stairs, index, exit, "unloaded ground");

            // The hole is pinned, so it is no ceiling, though everything round it is.
            var foot = index - new Vector2i(0, 1);
            await server.WaitPost(() =>
            {
                var levelGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, levelGrid, foot).ToList())
                {
                    entMan.DeleteEntity(anchored);
                }
            });

            var mob = await SpawnSettled(pair, world.Cavern, TileCentre(foot));
            var changes = await Walk(pair, mob, TileCentre(foot), TileCentre(exit));
            await server.WaitRunTicks(30);
            await AssertStands(pair, mob, world.Ground, exit, changes, "Walking up under unloaded ground");
            await server.WaitPost(() => entMan.DeleteEntity(mob));

            await LoadChunks(pair, world.Ground, index - new Vector2i(8, 8), index + new Vector2i(8, 8));
            await server.WaitRunTicks(3);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, index), Is.True, "Loading the ground filled the stairs' hole.");
                    Assert.That(ground.Shades.ContainsKey(index), Is.True, "Loading the ground took the hole's shade.");
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, exit), Is.False, "Loading the ground emptied the stairs' exit.");
                    Assert.That(maps.GetAnchoredEntities(world.Ground, groundGrid, exit).Any(), Is.False,
                        "The biome grew something on the stairs' exit when the ground loaded.");
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, index + new Vector2i(-1, 0)), Is.False,
                        "Precondition: the ground beside the hole did not load.");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Stairs are refused under laid floor, under something built, under a hull, and where their exit is a hole or
    /// built on. Stairs standing under a hull stay shut, and open by themselves once it leaves.
    /// </summary>
    [Test]
    public async Task StairsRefuseGroundTheyCannotOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var mouths = server.System<WFCavernMouthSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var exit = Site + new Vector2i(0, 1);

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await LoadBoth(pair, world, Site - new Vector2i(8, 8), Site + new Vector2i(8, 8));
            await ClearSite(pair, world, Site, 3);

            var user = await SpawnSettled(pair, world.Cavern, TileCentre(Site - new Vector2i(0, 1)));
            var site = new WFCavernStairsSite();
            var here = new EntityCoordinates(world.Cavern, TileCentre(Site));

            async Task<WFCavernStairsRefusal> Check()
            {
                var refusal = WFCavernStairsRefusal.None;
                await server.WaitPost(() =>
                    refusal = mouths.CheckStairs((world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground)), Site, exit));
                return refusal;
            }

            async Task SetGround(Vector2i index, Tile tile)
            {
                await server.WaitPost(() => maps.SetTile(world.Ground, entMan.GetComponent<MapGridComponent>(world.Ground), index, tile));
                await server.WaitRunTicks(2);
            }

            async Task<Tile> Natural(Vector2i index)
            {
                Tile? natural = null;
                await server.WaitPost(() =>
                {
                    var biome = entMan.GetComponent<BiomeComponent>(world.Ground);
                    if (biomes.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile))
                        natural = tile.Value;
                });

                Assert.That(natural, Is.Not.Null, $"Precondition: the biome lays no ground at {index}.");
                return natural!.Value;
            }

            async Task<WFCavernStairsRefusal> CheckWithWallOn(Vector2i index)
            {
                var wall = EntityUid.Invalid;
                await server.WaitPost(() => wall = entMan.SpawnEntity(BuiltWall, new EntityCoordinates(world.Ground, TileCentre(index))));
                var refusal = await Check();
                await server.WaitPost(() => entMan.DeleteEntity(wall));
                return refusal;
            }

            Assert.That(await Check(), Is.EqualTo(WFCavernStairsRefusal.None), "Bare ground refused stairs.");

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(site.Condition(user, here, Direction.South), Is.True, "The recipe refused a cavern floor under bare ground.");
                    Assert.That(site.Condition(user, new EntityCoordinates(world.Ground, TileCentre(Site)), Direction.South), Is.False,
                        "The recipe allowed stairs on the ground.");
                }
            });

            await SetGround(Site, new Tile(tileDefs[BuiltFloor].TileId));
            Assert.That(await Check(), Is.EqualTo(WFCavernStairsRefusal.Built), "Laid floor did not refuse stairs.");
            await SetGround(Site, await Natural(Site));

            Assert.That(await CheckWithWallOn(Site), Is.EqualTo(WFCavernStairsRefusal.Built), "A wall on the ground above did not refuse stairs.");
            Assert.That(await CheckWithWallOn(exit), Is.EqualTo(WFCavernStairsRefusal.Exit), "A wall at the top of the stairs did not refuse them.");

            await SetGround(exit, Tile.Empty);
            Assert.That(await Check(), Is.EqualTo(WFCavernStairsRefusal.Exit), "A hole at the top of the stairs did not refuse them.");
            await SetGround(exit, await Natural(exit));
            Assert.That(await Check(), Is.EqualTo(WFCavernStairsRefusal.None), "Precondition: the ground is not bare again.");

            // A parked hull, as a stopped one is static.
            var hull = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground), size: 3, offset: new Vector2(Site.X - 1, Site.Y - 1));
            await server.WaitPost(() =>
            {
                server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);
                server.System<SharedTransformSystem>().SetLocalPosition(hull, new Vector2(Site.X - 1, Site.Y - 1));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(1f));

            Assert.That(await Check(), Is.EqualTo(WFCavernStairsRefusal.Hull), "A parked hull did not refuse stairs.");
            await server.WaitAssertion(() =>
                Assert.That(site.Condition(user, here, Direction.South), Is.False, "The recipe allowed stairs under a parked hull."));

            var stairs = await SpawnStairs(pair, world, Site, Direction.South);
            await server.WaitRunTicks(pair.SecondsToTicks((float) WFCavernStairsSystem.RetryInterval.TotalSeconds) + 10);

            await server.WaitAssertion(() =>
            {
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<WFCavernStairsComponent>(stairs).Open, Is.False, "Stairs opened the ground under a hull.");
                    Assert.That(IsEmpty(maps, world.Ground, groundGrid, Site), Is.False, "The ground under the hull was opened.");
                }
            });

            await server.WaitPost(() => entMan.DeleteEntity(hull));
            await server.WaitRunTicks(pair.SecondsToTicks((float) WFCavernStairsSystem.RetryInterval.TotalSeconds) + 10);
            await AssertOpened(pair, world, stairs, Site, exit, "once the hull left");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Loads the chunks over a tile rectangle on the ground and in the cavern.</summary>
    private static async Task LoadBoth(TestPair pair, World world, Vector2i from, Vector2i to)
    {
        await LoadChunks(pair, world.Ground, from, to);
        await LoadChunks(pair, world.Cavern, from, to);
    }

    /// <summary>Deletes everything anchored within a reach of a tile on both maps, so the ground and the cavern floor are bare.</summary>
    private static async Task ClearSite(TestPair pair, World world, Vector2i index, int reach)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            foreach (var map in new[] { world.Ground, world.Cavern })
            {
                var grid = entMan.GetComponent<MapGridComponent>(map);

                for (var x = -reach; x <= reach; x++)
                for (var y = -reach; y <= reach; y++)
                {
                    foreach (var anchored in maps.GetAnchoredEntities(map, grid, index + new Vector2i(x, y)).ToList())
                    {
                        entMan.DeleteEntity(anchored);
                    }
                }
            }
        });
    }

    /// <summary>Spawns stairs on a cavern tile facing a direction, as the recipe does, and lets the hole queue run.</summary>
    private static async Task<EntityUid> SpawnStairs(TestPair pair, World world, Vector2i index, Direction facing)
    {
        var server = pair.Server;
        var stairs = EntityUid.Invalid;

        await server.WaitPost(() =>
            stairs = server.EntMan.SpawnAttachedTo(Stairs, new EntityCoordinates(world.Cavern, TileCentre(index)), rotation: facing.ToAngle()));
        await server.WaitRunTicks(3);
        return stairs;
    }

    /// <summary>Spawns a mob and gives it a second to settle.</summary>
    private static async Task<EntityUid> SpawnSettled(TestPair pair, EntityUid map, Vector2 position)
    {
        var server = pair.Server;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() => mob = server.EntMan.SpawnEntity(Mob, new EntityCoordinates(map, position)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return mob;
    }

    /// <summary>Moves a mob from one spot to another a stride at a time, two ticks a stride, and counts its map changes.</summary>
    private static async Task<int> Walk(TestPair pair, EntityUid mob, Vector2 from, Vector2 to)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var strides = (int) MathF.Round((to - from).Length() / Stride);
        var changes = 0;
        EntityUid? map = null;

        await server.WaitPost(() => map = entMan.GetComponent<TransformComponent>(mob).MapUid);

        for (var i = 0; i <= strides; i++)
        {
            var position = Vector2.Lerp(from, to, (float) i / strides);

            await server.WaitPost(() => transform.SetWorldPosition(mob, position));
            await server.WaitRunTicks(2);
            await server.WaitPost(() =>
            {
                var now = entMan.GetComponent<TransformComponent>(mob).MapUid;
                if (now != map)
                    changes++;

                map = now;
            });
        }

        return changes;
    }

    /// <summary>Asserts that stairs opened the ground: a pinned, shaded hole above them and a solid, pinned exit beside it.</summary>
    private static async Task AssertOpened(TestPair pair, World world, EntityUid stairs, Vector2i index, Vector2i exit, string what)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        var maps = server.System<SharedMapSystem>();
        var mouths = server.System<WFCavernMouthSystem>();

        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
            var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(stairs).Anchored, Is.True, $"{what}: the stairs are not anchored.");
                Assert.That(entMan.GetComponent<WFCavernStairsComponent>(stairs).Open, Is.True, $"{what}: the stairs did not open the ground.");
                Assert.That(IsEmpty(maps, world.Ground, groundGrid, index), Is.True, $"{what}: the ground over the stairs is still solid.");
                Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"{what}: the stairs' hole is not pinned.");
                Assert.That(ground.Shades.ContainsKey(index), Is.True, $"{what}: the stairs' hole has no shade.");
                Assert.That(mouths.IsOpenAbove((world.Ground, ground), index), Is.True, $"{what}: the stairs do not read as open.");
                Assert.That(IsEmpty(maps, world.Ground, groundGrid, exit), Is.False, $"{what}: there is no ground at the top of the stairs.");
                Assert.That(biomes.WfIsPinned(groundBiome, exit), Is.True, $"{what}: the stairs' exit is not pinned.");
            }
        });
    }

    /// <summary>Asserts that a walk ended with one map change and the mob at rest, unhurt, on a tile of a map.</summary>
    private static async Task AssertStands(TestPair pair, EntityUid mob, EntityUid map, Vector2i tile, int changes, string what)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var physics = entMan.GetComponent<CEZPhysicsComponent>(mob);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(changes, Is.EqualTo(1), $"{what} changed map {changes} times.");
                Assert.That(xform.MapUid, Is.EqualTo(map), $"{what} ended on the wrong map.");
                Assert.That(maps.TileIndicesFor(map, entMan.GetComponent<MapGridComponent>(map), xform.Coordinates), Is.EqualTo(tile),
                    $"{what} ended on the wrong tile.");
                Assert.That(physics.LocalPosition, Is.EqualTo(0f).Within(0.01f), $"{what} left the mob off the floor.");
                Assert.That(physics.Velocity, Is.Zero, $"{what} left the mob moving.");
                Assert.That(Blunt(entMan, mob), Is.Zero, $"{what} hurt the mob.");
            }
        });
    }

    /// <summary>
    /// A tile where the ground biome grows something on the tile north of it, the stairs' exit, and the cavern grows
    /// rock on the tile east of it.
    /// </summary>
    private static async Task<Vector2i> FindOvergrownSite(TestPair pair, World world)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var biomes = server.System<BiomeSystem>();
        Vector2i? found = null;

        await server.WaitPost(() =>
        {
            var ground = entMan.GetComponent<BiomeComponent>(world.Ground);
            var level = entMan.GetComponent<BiomeComponent>(world.Cavern);

            for (var x = SearchFrom.X; x < SearchTo.X && found == null; x++)
            for (var y = SearchFrom.Y; y < SearchTo.Y && found == null; y++)
            {
                var index = new Vector2i(x, y);

                if (Grows(biomes, ground, index + new Vector2i(0, 1)) && Grows(biomes, level, index + new Vector2i(1, 0)))
                    found = index;
            }
        });

        Assert.That(found, Is.Not.Null, "Precondition: no site with something grown at the stairs' exit and rock beside them.");
        return found!.Value;
    }

    /// <summary>Whether a biome grows an entity on a tile.</summary>
    private static bool Grows(BiomeSystem biomes, BiomeComponent biome, Vector2i index)
    {
        return biomes.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile)
               && biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out _);
    }

    /// <summary>The Blunt damage a mob has taken.</summary>
    private static int Blunt(IEntityManager entMan, EntityUid mob)
    {
        return entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Blunt", FixedPoint2.Zero).Int();
    }

    /// <summary>The larger of the two axis distances between tiles.</summary>
    private static int Chebyshev(Vector2i a, Vector2i b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>Whether a map has no tile at an index.</summary>
    private static bool IsEmpty(SharedMapSystem maps, EntityUid map, MapGridComponent grid, Vector2i index)
    {
        return !maps.TryGetTileRef(map, grid, index, out var tile) || tile.Tile.IsEmpty;
    }
}
