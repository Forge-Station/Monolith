#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared._WF.Administration;
using Content.Shared.Parallax.Biomes;
using Robust.Client.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The wfcavern admin command: list, tp, stats, mouths and open, planet names and open's refusal when the cavern is gone.</summary>
[TestFixture]
[TestOf(typeof(WFCavernCommand))]
public sealed class CavernCommandTest
{
    /// <summary>How far east of the gate the admin carves a mouth by hand.</summary>
    private static readonly Vector2i OpenOffset = new(16, 0);

    /// <summary>A sector body's name, given to the Asclepiu network to check that planets match by it.</summary>
    private const string BodyName = "Wfcavernbody";

    /// <summary>A build name unlike the surface id, to check that planets match by the name they were built under.</summary>
    private const string BuiltName = "Wfcavernbuilt";

    /// <summary>Text only the finished stats line holds.</summary>
    private const string StatsDone = "% open";

    /// <summary>Ticks stats may take to sample its square a few rows a tick.</summary>
    private const int MaxStatsTicks = 3000;

    /// <summary>list shows every world, tp lands on the gate pad, stats samples around the caller or the gate, mouths lists the gate under every planet name, and open carves a mouth or refuses without a cavern.</summary>
    [Test]
    public async Task ListTpMouthsOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var biomes = server.System<BiomeSystem>();
        var console = pair.Client.ResolveDependency<IClientConsoleHost>();
        var output = new List<string>();

        EventHandler<AddStringArgs> capture = (_, args) =>
        {
            // Local lines are the client's own echo; the server's replies arrive as acks.
            if (!args.Local)
                output.Add(args.Text.ToString());
        };

        await EnableCaverns(pair);
        await DisableClaims(pair);

        var worlds = new List<World>();
        var body = EntityUid.Invalid;
        await pair.Client.WaitPost(() => console.AddString += capture);

        try
        {
            foreach (var surfaceId in Surfaces)
            {
                worlds.Add(await BuildWorld(pair, surfaceId));
            }

            var list = await Run(pair, output, "list");
            Assert.That(list.Count(line => line.Contains("cavern:")), Is.EqualTo(Surfaces.Length),
                $"list should print one row per world:\n{string.Join('\n', list)}");
            Assert.That(list.Where(line => line.Contains("cavern:")).All(line => line.Contains("mouths: 1")), Is.True,
                $"Every freshly built world should have exactly its gate:\n{string.Join('\n', list)}");

            var world = worlds[0];
            var gate = await Gate(pair, world);
            var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(ClimbTile(gate)));

            var tp = await Run(pair, output, "tp Asclepiu");
            await server.WaitAssertion(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(viewer);
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);

                Assert.That(xform.MapUid, Is.EqualTo(world.Cavern), $"tp did not move the caller into the cavern:\n{string.Join('\n', tp)}");
                Assert.That(maps.TileIndicesFor(world.Cavern, grid, xform.Coordinates), Is.EqualTo(ClimbTile(gate)),
                    "tp did not land the caller on the gate pad beside the climb point.");
            });

            // stats samples the cavern under a caller on that planet, and around the gate from anywhere else.
            var here = await RunUntil(pair, output, "stats Asclepiu", StatsDone);
            Assert.That(here.Any(line => line.Contains(StatsDone) && line.Contains(ClimbTile(gate).ToString())), Is.True,
                $"stats Asclepiu did not sample around the caller at {ClimbTile(gate)}:\n{string.Join('\n', here)}");

            var fervidusGate = await Gate(pair, worlds[1]);
            var there = await RunUntil(pair, output, "stats Fervidus", StatsDone);
            Assert.That(there.Any(line => line.Contains(StatsDone) && line.Contains(fervidusGate.Origin.ToString())), Is.True,
                $"stats Fervidus, called from Asclepiu, did not sample around the Fervidus gate at {fervidusGate.Origin}:\n{string.Join('\n', there)}");

            var mouths = await Run(pair, output, "mouths Asclepiu");
            Assert.That(mouths.Any(line => line.Contains("Gate") && line.Contains(gate.Origin.ToString())), Is.True,
                $"mouths does not list the gate at {gate.Origin}:\n{string.Join('\n', mouths)}");

            // A planet answers to its sector body's name, the name it was built under and its surface id, ignoring case.
            await server.WaitPost(() =>
            {
                body = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
                server.System<MetaDataSystem>().SetEntityName(body, BodyName);
                entMan.GetComponent<WFPlanetNetworkComponent>(world.Network).Planet = body;
                entMan.GetComponent<WFPlanetWeatherComponent>(world.Network).PlanetName = BuiltName;
            });

            foreach (var name in new[] { BodyName, BuiltName.ToLowerInvariant(), "WFSurfaceAsclepiu", "asclepiu" })
            {
                var named = await Run(pair, output, $"mouths {name}");
                Assert.That(named.Any(line => line.Contains("Gate") && line.Contains(gate.Origin.ToString())), Is.True,
                    $"mouths {name} does not list the Asclepiu gate:\n{string.Join('\n', named)}");
            }

            await Run(pair, output, "tp Asclepiu mouth");
            await server.WaitAssertion(() =>
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                    "tp ... mouth did not put the caller on the ground beside the gate."));

            // The ground around the gate is loaded by now, so the carve takes the loaded-chunk path. A patch with
            // nothing on it but what the biome grew, which open clears: outcrop rock included, since the loader rolls it.
            await pair.RunTicksSync(10);
            var spot = gate.Origin + OpenOffset;
            await server.WaitPost(() => spot = FindClearPatch(pair, world, gate.Origin + OpenOffset));
            await server.WaitPost(() =>
                server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(world.Ground, TileCentre(spot))));
            await pair.RunTicksSync(2);

            var open = await Run(pair, output, "open");
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var groundBiome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));

                Assert.That(biomes.WfIsChunkLoaded(groundBiome, spot), Is.True,
                    "Precondition: the ground under the carve was not loaded.");
                var carved = ground.Mouths.FirstOrDefault(mouth => mouth.Kind == WFCavernMouthKind.Admin && mouth.Origin == spot);
                Assert.That(carved, Is.Not.Null, $"open made no admin mouth at {spot}:\n{string.Join('\n', open)}");

                using (Assert.EnterMultipleScope())
                {
                    foreach (var index in carved!.Hole)
                    {
                        Assert.That(maps.TryGetTileRef(world.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty, Is.False,
                            $"open left hole tile {index} solid.");
                        Assert.That(biomes.WfIsPinned(groundBiome, index), Is.True, $"open left hole tile {index} unpinned.");
                        Assert.That(ground.Shades.ContainsKey(index), Is.True, $"open left hole tile {index} without a shade.");
                    }

                    // Only the mouth's own rim decor may stand on its footprint.
                    var rim = carved.Shape.Rim.Select(offset => carved.Origin + offset).ToHashSet();
                    foreach (var index in carved.Footprint)
                    {
                        if (rim.Contains(index))
                            continue;

                        Assert.That(maps.GetAnchoredEntities(world.Ground, grid, index), Is.Empty,
                            $"open left the biome's entities on footprint tile {index}.");
                    }
                }
            });

            var after = await Run(pair, output, "mouths Asclepiu");
            Assert.That(after.Count(line => line.Contains("Admin")), Is.EqualTo(1),
                $"mouths does not list the carved mouth:\n{string.Join('\n', after)}");

            // A second mouth clear of the first one's hole and climb point, but whose pad would overwrite its landing, is refused.
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var mouths = server.System<WFCavernMouthSystem>();
                var radius = server.System<WFCavernSystem>().TryGetCavern("WFSurfaceAsclepiu", out var cavern) ? cavern!.Mouths.PadRadius : 0;
                var carved = ground.Mouths.First(mouth => mouth.Kind == WFCavernMouthKind.Admin);
                var pad = carved.Pad(radius);
                Vector2i? beside = null;

                for (var dx = 1; dx <= 2 * radius + 8 && beside == null; dx++)
                {
                    var origin = new Vector2i(carved.Max.X + dx, carved.Origin.Y);
                    var shape = mouths.AdminShape((world.Ground, ground), origin)!;
                    if (shape.Hole.Concat(shape.Ring).Select(offset => origin + offset).Any(index => carved.Contains(index) || index == carved.ClimbTile))
                        continue;

                    if (shape.Pad(radius).Any(offset => pad.Contains(origin + offset)))
                        beside = origin;
                }

                Assert.That(beside, Is.Not.Null, "Precondition: no spot beside the carved mouth shares its pad.");

                var count = ground.Mouths.Count;
                var opened = mouths.TryOpenMouth((world.Ground, ground), beside!.Value, out var refusal);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(opened, Is.False, $"open carved a mouth at {beside} over the pad of the one at {carved.Origin}.");
                    Assert.That(refusal, Is.EqualTo("mouth"), "open gave the wrong reason for a shared pad.");
                    Assert.That(ground.Mouths, Has.Count.EqualTo(count), "The refused mouth was registered anyway.");
                }
            });

            // With the cavern gone, open refuses with its own reason, which has its own Fluent variant.
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var cavern = ground.Cavern;
                var loc = server.ResolveDependency<ILocalizationManager>();

                ground.Cavern = EntityUid.Invalid;
                var opened = server.System<WFCavernMouthSystem>().TryOpenMouth((world.Ground, ground), spot + OpenOffset, out var refusal);
                ground.Cavern = cavern;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(opened, Is.False, "open carved a mouth over a missing cavern.");
                    Assert.That(refusal, Is.EqualTo("cavern"), "open gave the wrong reason for a missing cavern.");
                    Assert.That(loc.GetString("cmd-wfcavern-open-refused", ("reason", "cavern")),
                        Is.Not.EqualTo(loc.GetString("cmd-wfcavern-open-refused", ("reason", "mouth"))),
                        "The cavern refusal falls through to the default Fluent variant.");
                }
            });
        }
        finally
        {
            await pair.Client.WaitPost(() => console.AddString -= capture);

            if (body.IsValid())
            {
                await server.WaitPost(() =>
                {
                    if (entMan.TryGetComponent<WFPlanetNetworkComponent>(worlds[0].Network, out var network))
                        network.Planet = null;

                    entMan.DeleteEntity(body);
                });
            }

            foreach (var world in worlds)
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The first admin mouth footprint of solid ground where everything anchored is the biome's own and whose pad is free, scanning east from a tile.</summary>
    private static Vector2i FindClearPatch(TestPair pair, World world, Vector2i from)
    {
        var entMan = pair.Server.EntMan;
        var maps = pair.Server.System<SharedMapSystem>();
        var biomes = pair.Server.System<BiomeSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(world.Ground);
        var biome = (world.Ground, entMan.GetComponent<BiomeComponent>(world.Ground));
        var ground = (world.Ground, entMan.GetComponent<WFCavernGroundComponent>(world.Ground));
        var level = (world.Cavern, entMan.GetComponent<BiomeComponent>(world.Cavern));
        var mouths = pair.Server.System<WFCavernMouthSystem>();
        var radius = pair.Server.System<WFCavernSystem>().TryGetCavern("WFSurfaceAsclepiu", out var cavern) ? cavern!.Mouths.PadRadius : 0;

        for (var dx = 0; dx < 16; dx++)
        for (var dy = -4; dy <= 4; dy++)
        {
            var origin = from + new Vector2i(dx, dy);
            var shape = mouths.AdminShape(ground, origin);
            Assert.That(shape, Is.Not.Null, "Precondition: the Asclepiu cavern is gone.");

            var clear = shape!.Hole.Concat(shape.Ring).Select(offset => origin + offset).All(index =>
                maps.TryGetTileRef(world.Ground, grid, index, out var tile) && !tile.Tile.IsEmpty
                && maps.GetAnchoredEntities(world.Ground, grid, index).All(uid => biomes.WfIsBiomeSpawned(biome, uid, index)))
                && !shape.Pad(radius).Any(offset => biomes.WfIsPinned(level, origin + offset));

            if (clear)
                return origin;
        }

        Assert.Fail($"Precondition: no clear patch of ground east of {from} to carve a mouth in.");
        return from;
    }

    /// <summary>Runs one wfcavern subcommand as the test player and ticks until the server writes a line holding <paramref name="until"/>.</summary>
    private static async Task<List<string>> RunUntil(TestPair pair, List<string> output, string args, string until)
    {
        await pair.Client.WaitPost(output.Clear);
        await pair.Server.WaitPost(() =>
            pair.Server.ConsoleHost.ExecuteCommand(pair.Player, $"{WolfgateAdminCommands.Cavern} {args}"));

        var lines = new List<string>();
        for (var ticks = 0; ticks < MaxStatsTicks && !lines.Any(line => line.Contains(until)); ticks += 10)
        {
            await pair.RunTicksSync(10);
            lines.Clear();
            await pair.Client.WaitPost(() => lines.AddRange(output));
        }

        return lines;
    }

    /// <summary>Runs one wfcavern subcommand as the test player and returns what the server wrote back.</summary>
    private static async Task<List<string>> Run(TestPair pair, List<string> output, string args)
    {
        await pair.Client.WaitPost(output.Clear);
        await pair.Server.WaitPost(() =>
            pair.Server.ConsoleHost.ExecuteCommand(pair.Player, $"{WolfgateAdminCommands.Cavern} {args}"));
        await pair.RunTicksSync(5);

        var lines = new List<string>();
        await pair.Client.WaitPost(() => lines.AddRange(output));
        return lines;
    }
}
