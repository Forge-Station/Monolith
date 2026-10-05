#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server.Parallax;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;
using ClimbDoAfter = Content.Shared.DoAfter.DoAfter;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Climbing: up from a climb point onto solid ground, down a shaft unhurt, refused under a hull, slower in high gravity.</summary>
[TestFixture]
[TestOf(typeof(WFCavernClimbSystem))]
public sealed class CavernClimbTest
{
    /// <summary>A breathable world with a 2x2 gate whose climb side is south.</summary>
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>The 3 g world, whose climb is clamped to 2.5 times the base.</summary>
    private const string HeavySurface = "WFSurfaceAerumna";

    /// <summary>How long a climber must stay put on the exit after arriving.</summary>
    private const int StayTicks = 120;

    /// <summary>How long a hostile cavern may take to start hurting an unequipped human.</summary>
    private const float HurtWithinSeconds = 60f;

    /// <summary>A hard wall a player could build on the pad.</summary>
    private const string BuiltWall = "WallSolid";

    /// <summary>Climbs up from beside the gate's climb point: lands on the solid lip, at rest, and stays there.</summary>
    [Test]
    public async Task ClimbUpLandsOnSolidExitAndStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var climb = await ClimbPointOf(pair, world, gate);
            var mob = await SpawnSettled(pair, world.Cavern, Beside(gate));
            var delay = await DelayOf(pair, climb);

            await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
            await server.WaitRunTicks(pair.SecondsToTicks(delay) + 10);

            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var index = TileOf(pair, world.Ground, mob);

                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                    "The climber never reached the ground.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(maps.TryGetTileRef(world.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        $"The climber came out over empty ground at {index}.");
                    Assert.That(ground.Shades.ContainsKey(index), Is.False, $"The climber came out over the hole at {index}.");
                    Assert.That(index, Is.EqualTo(ClimbTile(gate)), "The nearest exit is the lip over the climb point.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.LessThan(0.1f),
                        "The climber arrived above the ground instead of standing on it.");
                }
            });

            var slip = await FirstTickOff(pair, mob, world.Ground, StayTicks);
            Assert.That(slip, Is.Null, $"The climber left the ground or rose off it: {slip}");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A crate pulled by a climber with a tool in the other hand comes up the climb and back down the shaft with it,
    /// and is still pulled each time.
    /// </summary>
    [Test]
    public async Task ClimbingBringsWhatIsPulled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = server.System<SharedHandsSystem>();
        var pulling = server.System<PullingSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var climb = await ClimbPointOf(pair, world, gate);
            var mob = EntityUid.Invalid;
            var crate = EntityUid.Invalid;
            var ready = false;

            await server.WaitPost(() =>
            {
                mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(world.Cavern, TileCentre(Beside(gate))));
                crate = entMan.SpawnEntity("CrateGeneric", new EntityCoordinates(world.Cavern, TileCentre(ClimbTile(gate))));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(1f));

            await server.WaitPost(() =>
            {
                var tool = entMan.SpawnEntity("Crowbar", entMan.GetComponent<TransformComponent>(mob).Coordinates);
                ready = hands.TryPickupAnyHand(mob, tool) && pulling.TryStartPull(mob, crate);
            });
            Assert.That(ready, Is.True, "Precondition: the climber could not take a tool in one hand and the crate in the other.");

            async Task AssertBrought(EntityUid map, string what)
            {
                await server.WaitAssertion(() =>
                {
                    var mobXform = entMan.GetComponent<TransformComponent>(mob);
                    var crateXform = entMan.GetComponent<TransformComponent>(crate);

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(mobXform.MapUid, Is.EqualTo(map), $"{what}: the climber did not arrive.");
                        Assert.That(crateXform.MapUid, Is.EqualTo(map), $"{what}: the crate was left behind.");
                        Assert.That(entMan.GetComponent<PullerComponent>(mob).Pulling, Is.EqualTo(crate), $"{what}: the crate is no longer pulled.");
                        Assert.That((crateXform.LocalPosition - mobXform.LocalPosition).Length(), Is.LessThan(1.5f),
                            $"{what}: the crate is not beside the climber.");
                    }
                });
            }

            await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
            await server.WaitRunTicks(pair.SecondsToTicks(await DelayOf(pair, climb)) + 10);
            await AssertBrought(world.Ground, "Climbing up");

            // The finished DoAfter has to clear before the next one is counted.
            await server.WaitRunTicks(pair.SecondsToTicks(2f));

            var shade = EntityUid.Invalid;
            await server.WaitPost(() => shade = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades[ShadeBeside(gate)]);
            await StartVerb(pair, shade, mob, "wf-cavern-verb-climb-down");
            await server.WaitRunTicks(pair.SecondsToTicks(SharedWFCavernClimbSystem.ClimbDownSeconds) + 10);
            await AssertBrought(world.Cavern, "Climbing down");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Climbs down the gate from its lip: no damage, no knockdown, standing on the pad at the climb point.</summary>
    [Test]
    public async Task ClimbDownIsHarmless()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var shade = EntityUid.Invalid;
            await server.WaitPost(() => shade = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades[ShadeBeside(gate)]);

            var mob = await SpawnSettled(pair, world.Ground, ClimbTile(gate));

            await StartVerb(pair, shade, mob, "wf-cavern-verb-climb-down");
            await server.WaitRunTicks(pair.SecondsToTicks(SharedWFCavernClimbSystem.ClimbDownSeconds) + 10);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var biome = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
                var index = TileOf(pair, world.Cavern, mob);
                var fromClimb = index - ClimbTile(gate);

                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                    "The climber never reached the cavern.");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                        "Climbing down hurt the climber.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(MobState.Alive));
                    Assert.That(entMan.HasComponent<KnockedDownComponent>(mob), Is.False, "Climbing down knocked the climber down.");
                    Assert.That(Math.Max(Math.Abs(fromClimb.X), Math.Abs(fromClimb.Y)), Is.LessThanOrEqualTo(1),
                        $"The climber arrived at {index}, not at or next to the climb point at {ClimbTile(gate)}.");
                    Assert.That(maps.TryGetTileRef(world.Cavern, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.True,
                        "The climber arrived over empty cavern floor.");
                    Assert.That(biomes.WfIsPinned(biome, index), Is.True, "The climber arrived off the pinned pad.");
                    Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.LessThan(0.1f),
                        "The climber arrived above the cavern floor instead of standing on it.");
                }
            });

            var slip = await FirstTickOff(pair, mob, world.Cavern, StayTicks);
            Assert.That(slip, Is.Null, $"The climber did not stay on the pad: {slip}");

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                    "The climber was hurt after arriving."));
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>With a wall built on the pad over the climb point, climbing down lands on the nearest free pad tile, not inside the wall.</summary>
    [Test]
    public async Task ClimbDownAvoidsBuiltPad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var shade = EntityUid.Invalid;
            var wall = EntityUid.Invalid;

            await server.WaitPost(() =>
            {
                shade = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Shades[ShadeBeside(gate)];
                wall = entMan.SpawnEntity(BuiltWall, new EntityCoordinates(world.Cavern, TileCentre(ClimbTile(gate))));
            });

            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<TransformComponent>(wall).Anchored, Is.True, "Precondition: the wall is not anchored on the pad."));

            var mob = await SpawnSettled(pair, world.Ground, ClimbTile(gate));

            await StartVerb(pair, shade, mob, "wf-cavern-verb-climb-down");

            // The tile on the tick of arrival, before physics can shove a climber out of a wall it was put inside.
            Vector2i? arrival = null;
            for (var tick = 0; tick < pair.SecondsToTicks(SharedWFCavernClimbSystem.ClimbDownSeconds) + 10 && arrival == null; tick++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() =>
                {
                    if (entMan.GetComponent<TransformComponent>(mob).MapUid == world.Cavern)
                        arrival = TileOf(pair, world.Cavern, mob);
                });
            }

            Assert.That(arrival, Is.Not.Null, "The climber never reached the cavern.");

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var index = arrival!.Value;
                var fromClimb = index - ClimbTile(gate);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(index, Is.Not.EqualTo(ClimbTile(gate)), "The climber landed inside the wall built over the climb point.");
                    Assert.That(Math.Abs(fromClimb.X) + Math.Abs(fromClimb.Y), Is.EqualTo(1),
                        $"The climber arrived at {index}, not on a pad tile beside the blocked climb point at {ClimbTile(gate)}.");
                    Assert.That(maps.GetAnchoredEntities(world.Cavern, grid, index), Does.Not.Contain(wall));
                    Assert.That(entMan.GetComponent<DamageableComponent>(mob).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                        "Climbing down beside the wall hurt the climber.");
                }
            });

            var slip = await FirstTickOff(pair, mob, world.Cavern, StayTicks);
            Assert.That(slip, Is.Null, $"The climber did not stay on the pad: {slip}");
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>With a grid over every exit tile, the climb fails with the hull popup and the climber stays below.</summary>
    [Test]
    public async Task ClimbRefusedUnderHull()
    {
        // Connected, so the refusal popup reaches a client that can record it.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var probe = pair.Client.System<WFCavernPopupProbeSystem>();
        var hullText = pair.Client.ResolveDependency<ILocalizationManager>().GetString("wf-cavern-climb-blocked-hull");

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var gate = await Gate(pair, world);
            var climb = await ClimbPointOf(pair, world, gate);
            var mob = await PlanetFixture.AttachViewer(pair, world.Cavern, TileCentre(Beside(gate)));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            // Every tile within reach of the climb point, the hole's included.
            var reach = new Vector2i(WFCavernClimbSystem.ExitReach, WFCavernClimbSystem.ExitReach);
            var from = ClimbTile(gate) - reach;
            var hull = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground),
                size: 2 * WFCavernClimbSystem.ExitReach + 1, offset: new Vector2(from.X, from.Y));

            // Parked, as a stopped hull is static: a dynamic one is shoved out of the outcrops the viewer's load put there.
            await server.WaitPost(() =>
            {
                server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);
                server.System<SharedTransformSystem>().SetLocalPosition(hull, new Vector2(from.X, from.Y));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await server.WaitAssertion(() =>
            {
                var hullXform = entMan.GetComponent<TransformComponent>(hull);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(hullXform.MapUid, Is.EqualTo(world.Ground), "Precondition: the hull is not on the ground map.");
                    Assert.That(hullXform.LocalPosition, Is.EqualTo(new Vector2(from.X, from.Y)),
                        "Precondition: the hull moved off the exit tiles.");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "Precondition: the climber is not in the cavern.");
                }
            });

            var delay = await DelayOf(pair, climb);
            await pair.Client.WaitPost(probe.Start);
            var doAfter = await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
            await pair.RunTicksSync(pair.SecondsToTicks(delay) + 10);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(doAfter.Completed && !doAfter.Cancelled, Is.True,
                        "Precondition: the climb never finished, so nothing was refused.");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Cavern),
                        "The climber went up through a hull parked over the exit.");
                }
            });

            await pair.Client.WaitAssertion(() =>
                Assert.That(probe.Received, Does.Contain(hullText),
                    $"The refused climb did not show the hull popup. Received: {string.Join(" | ", probe.Received)}"));
        }
        finally
        {
            await pair.Client.WaitPost(probe.Stop);
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An unequipped human already hurt by a hostile cavern's air, heat or cold still climbs out: ambient damage never breaks the climb.</summary>
    // Carcinoma is left out: its ammonia did not hurt an unequipped human within HurtWithinSeconds, so nothing there can break the climb.
    [TestCase("WFSurfaceAerumna")]
    [TestCase("WFSurfaceFervidus")]
    [TestCase("WFSurfaceThrascias")]
    public async Task UnequippedClimberEscapesHostileAir(string surfaceId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, surfaceId);

        try
        {
            var gate = await Gate(pair, world);
            var climb = await ClimbPointOf(pair, world, gate);
            var mob = await SpawnSettled(pair, world.Cavern, Beside(gate));
            var delay = await DelayOf(pair, climb);

            // Wait for the air to start hurting, so its damage ticks land during the climb.
            var hurtAt = FixedPoint2.Zero;
            for (var tick = 0; tick < pair.SecondsToTicks(HurtWithinSeconds) && hurtAt == FixedPoint2.Zero; tick++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() => hurtAt = entMan.GetComponent<DamageableComponent>(mob).TotalDamage);
            }

            Assert.That(hurtAt, Is.GreaterThan(FixedPoint2.Zero),
                $"Precondition: {surfaceId}'s cavern did not hurt an unequipped human within {HurtWithinSeconds} s.");

            var doAfter = await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
            await server.WaitRunTicks(pair.SecondsToTicks(delay) + 10);

            await server.WaitAssertion(() =>
            {
                var damage = entMan.GetComponent<DamageableComponent>(mob);
                var taken = string.Join(", ", damage.Damage.DamageDict.Where(d => d.Value > 0).Select(d => $"{d.Key} {d.Value}"));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(damage.TotalDamage, Is.GreaterThan(hurtAt),
                        $"Precondition: {surfaceId}'s air did not hurt the climber during the climb ({taken}).");
                    Assert.That(doAfter.Cancelled, Is.False, $"{surfaceId}: the climb was broken ({taken}).");
                    Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(world.Ground),
                        $"{surfaceId}: the unequipped climber never reached the ground ({taken}).");
                }
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The climb takes the base time times the clamped surface gravity: 4 s on Asclepiu, 10 s at Aerumna's 3 g.</summary>
    [Test]
    public async Task DelayScalesWithGravity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var tick = (float) server.Timing.TickPeriod.TotalSeconds;

        await EnableCaverns(pair);

        foreach (var (surfaceId, expected) in new[] { (Surface, 4f), (HeavySurface, 10f) })
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var gate = await Gate(pair, world);
                var climb = await ClimbPointOf(pair, world, gate);
                var delay = await DelayOf(pair, climb);

                Assert.That(delay, Is.EqualTo(expected).Within(tick), $"{surfaceId}: the climb point's delay is {delay} s.");

                if (surfaceId != HeavySurface)
                    continue;

                var mob = await SpawnSettled(pair, world.Cavern, Beside(gate));

                var doAfter = await StartVerb(pair, climb, mob, "wf-cavern-verb-climb-up");
                Assert.That(doAfter.Args.Delay.TotalSeconds, Is.EqualTo(expected).Within(tick),
                    $"{surfaceId}: the climb DoAfter lasts {doAfter.Args.Delay.TotalSeconds} s.");

                // By the game clock each tick runs at, not a tick count: the verb runs between ticks, and a 1/30 s tick
                // rounds down to whole TimeSpan ticks, so a count reads up to two ticks long.
                var ticks = 0;
                var arrived = false;
                var arrivedAt = TimeSpan.Zero;
                var budget = pair.SecondsToTicks(expected) + 30;

                while (!arrived && ticks < budget)
                {
                    var tickTime = TimeSpan.Zero;
                    await server.WaitPost(() => tickTime = server.Timing.CurTime);
                    await server.WaitRunTicks(1);
                    ticks++;
                    await server.WaitPost(() => arrived = entMan.GetComponent<TransformComponent>(mob).MapUid == world.Ground);
                    arrivedAt = tickTime;
                }

                Assert.That(arrived, Is.True, $"{surfaceId}: the climber never reached the ground within {budget} ticks.");

                var elapsed = arrivedAt - doAfter.StartTime;
                Assert.That(elapsed, Is.GreaterThanOrEqualTo(doAfter.Args.Delay).And.LessThan(doAfter.Args.Delay + server.Timing.TickPeriod),
                    $"{surfaceId}: the climb finished {elapsed.TotalSeconds} s after it started; its delay is {doAfter.Args.Delay.TotalSeconds} s.");
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The pad tile south of the climb point, away from the hole; the test worlds climb out on the south side.</summary>
    private static Vector2i Beside(WFCavernMouth gate)
    {
        return ClimbTile(gate) + new Vector2i(0, -1);
    }

    /// <summary>The hole tile just north of the climb tile, in reach of a climber standing on it.</summary>
    private static Vector2i ShadeBeside(WFCavernMouth gate)
    {
        return ClimbTile(gate) + new Vector2i(0, 1);
    }

    /// <summary>The gate's climb point in the cavern.</summary>
    private static async Task<EntityUid> ClimbPointOf(TestPair pair, World world, WFCavernMouth gate)
    {
        var climb = EntityUid.Invalid;

        await pair.Server.WaitPost(() =>
            climb = pair.Server.EntMan.GetComponent<WFCavernGroundComponent>(world.Ground).ClimbPoints[gate.ClimbTile]);
        return climb;
    }

    /// <summary>A climb point's stamped delay in seconds.</summary>
    private static async Task<float> DelayOf(TestPair pair, EntityUid climb)
    {
        var delay = 0f;

        await pair.Server.WaitPost(() => delay = pair.Server.EntMan.GetComponent<WFCavernClimbComponent>(climb).Delay);
        return delay;
    }

    /// <summary>Spawns a human on a tile and lets it settle, failing unless it stands there at rest.</summary>
    private static async Task<EntityUid> SpawnSettled(TestPair pair, EntityUid map, Vector2i tile)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mob = EntityUid.Invalid;

        await server.WaitPost(() => mob = entMan.SpawnEntity(PlanetFixture.ViewerProto, new EntityCoordinates(map, TileCentre(tile))));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(map),
                    $"Precondition: the climber fell off {entMan.ToPrettyString(map)} before climbing.");
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition, Is.LessThan(0.05f),
                    "Precondition: the climber is not standing on the floor.");
            }
        });

        return mob;
    }

    /// <summary>Runs a climb verb as the user and returns the DoAfter it started, failing if it isn't offered or starts none.</summary>
    private static async Task<ClimbDoAfter> StartVerb(TestPair pair, EntityUid target, EntityUid user, string key)
    {
        var server = pair.Server;
        var verbs = server.System<SharedVerbSystem>();
        var text = server.ResolveDependency<ILocalizationManager>().GetString(key);
        ClimbDoAfter? started = null;

        await server.WaitAssertion(() =>
        {
            var verb = verbs.GetLocalVerbs(target, user, typeof(AlternativeVerb)).FirstOrDefault(v => v.Text == text);

            Assert.That(verb, Is.Not.Null, $"The climber was not offered \"{text}\".");
            verbs.ExecuteVerb(verb!, user, target);
            started = server.EntMan.GetComponent<DoAfterComponent>(user).DoAfters.Values.SingleOrDefault();
            Assert.That(started, Is.Not.Null, $"\"{text}\" did not start exactly one DoAfter.");
        });

        return started!;
    }

    /// <summary>Ticks one at a time; describes the first tick the mob is off the map or off its floor, or null if it never is.</summary>
    private static async Task<string?> FirstTickOff(TestPair pair, EntityUid mob, EntityUid map, int ticks)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        string? off = null;

        for (var tick = 0; tick < ticks && off == null; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                var at = entMan.GetComponent<TransformComponent>(mob).MapUid;
                var height = entMan.GetComponent<CEZPhysicsComponent>(mob).LocalPosition;

                if (at != map || height >= 0.1f)
                    off = $"tick {tick}: on {entMan.ToPrettyString(at)} at height {height}";
            });
        }

        return off;
    }

    /// <summary>The tile an entity stands on in a map.</summary>
    private static Vector2i TileOf(TestPair pair, EntityUid map, EntityUid uid)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();

        return maps.TileIndicesFor(map, entMan.GetComponent<MapGridComponent>(map), entMan.GetComponent<TransformComponent>(uid).Coordinates);
    }
}

/// <summary>Records the entity popups a connected test client receives from the server, only while a test asks it to.</summary>
public sealed partial class WFCavernPopupProbeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    /// <summary>Whether popups are being recorded; off except between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool Recording { get; private set; }

    /// <summary>Every popup message received while recording, oldest first.</summary>
    public readonly List<string> Received = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        if (!_net.IsClient)
            return;

        SubscribeNetworkEvent<PopupEntityEvent>(OnPopup);
    }

    /// <summary>Clears what was recorded and starts recording.</summary>
    public void Start()
    {
        Received.Clear();
        Recording = true;
    }

    /// <summary>Stops recording and drops what was recorded.</summary>
    public void Stop()
    {
        Recording = false;
        Received.Clear();
    }

    private void OnPopup(PopupEntityEvent ev)
    {
        if (Recording)
            Received.Add(ev.Message);
    }
}
