#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The hull guard: with a cavern below, every hull path still stops at the ground, over a mouth too.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class CavernHullTest
{
    /// <summary>A hull with no lift over unloaded ground churns on the ground and never sinks into the cavern.</summary>
    [Test]
    public async Task UnsupportedHullNeverDescends()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        // The 15x15 test hull, away from the planet centre. Its chunks are never loaded: a hull parked on loaded
        // terrain can keep a tile or two through an unload, because biome entities it touched pin theirs.
        var from = new Vector2i(200, 200);
        var to = new Vector2i(214, 214);

        var hull = await PlanetFixture.BuildHull(pair, await MapIdOf(pair, world.Ground), new Vector2(from.X, from.Y));
        await PlanetFixture.MapInitHull(pair, hull);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the hull is not on the ground map."));
        await AssertNoGroundUnder(pair, world, from, to);

        await AssertNeverBelowGround(pair, world, hull, seconds: 10);

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>A pilot holding descend over unloaded ground goes nowhere below it.</summary>
    [Test]
    public async Task PilotCannotDescendFromGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        // The 7x9 lander, hovering on its landing thrusters over ground whose chunks are not loaded.
        var from = new Vector2i(200, 200);
        var to = new Vector2i(206, 208);

        var lander = await PlanetFixture.BuildLander(pair, await MapIdOf(pair, world.Ground), new Vector2(from.X, from.Y));
        await PlanetFixture.MapInitHull(pair, lander);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(lander).MapUid, Is.EqualTo(world.Ground),
                "Precondition: the lander is not on the ground map."));
        await AssertNoGroundUnder(pair, world, from, to);

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryGetLiftRatio(lander, out var ratio) && ratio >= 1f, Is.True,
                "Precondition: the lander can't hover, so its pilot's descend key is never read."));

        // Counts transit gaps as they are made: an unguarded descend enters and leaves one within a tick.
        var transits = 0;
        Action<AddedComponentEventArgs> onAdded = args =>
        {
            if (args.BaseArgs.Component is CEZTransitMapComponent)
                transits++;
        };

        await server.WaitPost(() => entMan.ComponentAdded += onAdded);

        try
        {
            await PlanetFixture.HoldDescend(pair, lander);

            // Its landing thrusters hold it up, so a refused descend leaves it where it is instead of churning.
            await AssertNeverBelowGround(pair, world, lander, seconds: 10, stayOn: world.Ground);

            await server.WaitAssertion(() =>
                Assert.That(transits, Is.Zero, "A refused descend still sent the lander through a transit gap."));
        }
        finally
        {
            await server.WaitPost(() => entMan.ComponentAdded -= onAdded);
        }

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>With a cavern below, a lander still lifts off into the first air layer and lands back on the ground.</summary>
    [Test]
    public async Task LiftoffAndLandingUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");
        var air = world.Layers[1];

        // A laid pad to lift off from and land back on; with nobody near, no biome chunk loads under it.
        await PlanetFixture.LayTiles(pair, world.Ground, new Vector2i(-4, -4), new Vector2i(12, 14));

        var lander = await PlanetFixture.BuildLander(pair, await MapIdOf(pair, world.Ground), Vector2.Zero);
        await PlanetFixture.MapInitHull(pair, lander);
        var pilot = await PlanetFixture.HoldVertical(pair, lander, ShuttleButtons.None);
        var console = PlanetFixture.FindShuttleConsole(entMan, lander);

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryBeginLiftoff(lander, console, pilot, out var reason), Is.True,
                $"A grounded lander was refused liftoff with caverns on: {reason}"));

        var airborne = await WaitForMap(pair, world, lander, air, seconds: 45);
        Assert.That(airborne, Is.True, "The lander never lifted off into the first air layer with caverns on.");

        await server.WaitPost(() => entMan.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.DescendZ);

        var landed = await WaitForMap(pair, world, lander, world.Ground, seconds: 40);
        Assert.That(landed, Is.True, "The lander never landed back on the ground with caverns on.");

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>A 3x3 hull parked over the gate stands on its pinned lip and never leaves the ground.</summary>
    [Test]
    public async Task HullOverMouthStaysOnGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        try
        {
            var gate = await Gate(pair, world);

            // Centred on the hole tile over the climb tile, so it spans the hole's south edge and the lip: the lip is the only ground under it.
            var from = gate.ClimbTile - new Vector2i(1, 0);
            var covered = 0;
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
            {
                if (gate.Contains(from + new Vector2i(x, y)))
                    covered++;
            }

            var hull = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground), size: 3,
                offset: new Vector2(from.X, from.Y));

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(world.Ground),
                        "Precondition: the hull is not on the ground map.");
                    Assert.That(covered, Is.GreaterThan(0), "Precondition: the hull covers none of the hole.");
                    Assert.That(SolidTiles(entMan, maps, world.Ground, from, from + new Vector2i(2, 2)),
                        Is.EqualTo(9 - covered), "Precondition: the ground under the hull is not just the lip.");
                }
            });

            // Past the three-second grace, so gravity has had its say.
            await AssertNeverBelowGround(pair, world, hull, seconds: 5, stayOn: world.Ground);
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A debris grid as big as the largest square of hole in the Asclepiu gate (holes reach 30 tiles, so a pod fits
    /// wholly inside one) may churn with no ground under it, but never enters the cavern.
    /// </summary>
    [Test]
    public async Task DebrisInsideMouthNeverEntersCavern()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        try
        {
            var gate = await Gate(pair, world);

            // The largest square of hole tiles, up to 4x4, and its lowest corner.
            var size = 0;
            var corner = gate.Origin;
            for (var edge = 4; edge >= 2 && size == 0; edge--)
            {
                foreach (var tile in gate.Hole.OrderBy(t => t.Y).ThenBy(t => t.X))
                {
                    var fits = true;
                    for (var x = 0; x < edge && fits; x++)
                    for (var y = 0; y < edge && fits; y++)
                    {
                        fits = gate.Contains(tile + new Vector2i(x, y));
                    }

                    if (!fits)
                        continue;

                    size = edge;
                    corner = tile;
                    break;
                }
            }

            Assert.That(size, Is.AtLeast(2), "Precondition: the Asclepiu gate has no 2x2 square of hole.");

            var debris = await PlanetFixture.BuildDebris(pair, await MapIdOf(pair, world.Ground), size: size,
                offset: new Vector2(corner.X, corner.Y));

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(debris).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the debris is not on the ground map.");
                Assert.That(SolidTiles(entMan, maps, world.Ground, corner, corner + new Vector2i(size - 1, size - 1)), Is.Zero,
                    $"Precondition: the {size}x{size} debris has ground under it.");
            });

            await AssertNeverBelowGround(pair, world, debris, seconds: 10);
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Fails unless every ground tile under the hull's footprint is empty.</summary>
    private static async Task AssertNoGroundUnder(TestPair pair, World world, Vector2i from, Vector2i to)
    {
        var server = pair.Server;
        var maps = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
            Assert.That(SolidTiles(server.EntMan, maps, world.Ground, from, to), Is.Zero,
                "Precondition: there is ground under the hull."));
    }

    /// <summary>Ticks one at a time, failing if the hull or any transit ever opens onto the cavern, or the hull leaves <paramref name="stayOn"/>.</summary>
    // Every tick, so a transit gap entered and left between samples is still caught.
    private static async Task AssertNeverBelowGround(TestPair pair, World world, EntityUid hull, int seconds, EntityUid? stayOn = null)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticks = pair.SecondsToTicks(seconds);
        EntityUid? last = null;
        var trail = "";

        for (var tick = 0; tick < ticks; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var map = entMan.GetComponent<TransformComponent>(hull).MapUid;

                if (map != last)
                    trail += $" t{tick}:{entMan.ToPrettyString(map)}";
                last = map;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(TouchesCavern(entMan, map, world.Cavern), Is.False,
                        $"The hull went below the ground into {entMan.ToPrettyString(map)}.{trail}");
                    Assert.That(TransitsTouchingCavern(entMan, world.Cavern), Is.Empty,
                        $"A transit gap opened onto the cavern.{trail}");

                    if (stayOn is { } expected)
                        Assert.That(map, Is.EqualTo(expected), $"The hull left {entMan.ToPrettyString(expected)}.{trail}");
                }
            });
        }
    }

    /// <summary>Ticks a second at a time until the hull is on the map, failing if it ever goes below ground.</summary>
    private static async Task<bool> WaitForMap(TestPair pair, World world, EntityUid hull, EntityUid target, int seconds)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var reached = false;

        for (var second = 0; second < seconds && !reached; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitAssertion(() =>
            {
                var map = entMan.GetComponent<TransformComponent>(hull).MapUid;

                Assert.That(TouchesCavern(entMan, map, world.Cavern), Is.False,
                    $"The hull went below the ground into {entMan.ToPrettyString(map)}.");
                reached = map == target;
            });
        }

        return reached;
    }
}
