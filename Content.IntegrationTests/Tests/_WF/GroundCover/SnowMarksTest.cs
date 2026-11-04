#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.GroundCover;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using ClientGroundCoverSystem = Content.Client._WF.GroundCover.WFGroundCoverSystem;
using ClientSnowPrintsSystem = Content.Client._WF.GroundCover.WFSnowPrintsSystem;
using ClientSnowTracksSystem = Content.Client._WF.GroundCover.WFSnowTracksSystem;
using ClientPrintKind = Content.Client._WF.GroundCover.WFSnowPrintKind;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>Marks drawn in snow cover that follow the snow: footprints and tread marks.</summary>
[TestFixture]
public sealed class SnowMarksTest
{
    private const int Deep = 180;

    /// <summary>A footprint fades as the snow under it thins, and goes when the tile is bare.</summary>
    [Test]
    public async Task FootprintsFadeWithTheSnow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var tile = new Vector2i(0, 0);
        await SetUp(pair, map.Grid, tile, Deep);

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        var cGrid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(map.Grid.Owner));
        Content.Client._WF.GroundCover.WFSnowPrint print = default!;
        await client.WaitPost(() =>
        {
            var prints = client.System<ClientSnowPrintsSystem>();
            prints.Clear();
            print = prints.Lay(cGrid, tile, new Vector2(0.5f, 0.5f), Angle.Zero, ClientPrintKind.Shoes, Deep);
        });

        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientSnowPrintsSystem>().Visibility(print), Is.GreaterThan(0.95f), "A fresh print in deep snow wasn't clear."));

        // Melt down to half of the depth a print needs to show in full: the print fades to about half.
        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, ClientSnowPrintsSystem.FullDepth / 2));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() =>
        {
            var visibility = client.System<ClientSnowPrintsSystem>().Visibility(print);
            Assert.That(visibility, Is.InRange(0.4f, 0.6f), "The print didn't fade with the thinner snow.");
        });

        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, 0));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientSnowPrintsSystem>().Prints.TryGetValue(cGrid, out var tiles) && tiles.ContainsKey(tile), Is.False,
                "A print outlived the snow."));

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    /// <summary>A tread mark stays when the treads press the snow flat, fills in under new snow, and goes when the tile is bare.</summary>
    [Test]
    public async Task TreadMarksFollowTheSnow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var tile = new Vector2i(0, 0);
        await SetUp(pair, map.Grid, tile, Deep);

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        var cGrid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(map.Grid.Owner));
        Content.Client._WF.GroundCover.WFSnowTread mark = default!;
        await client.WaitPost(() =>
        {
            var tracks = client.System<ClientSnowTracksSystem>();
            tracks.Clear();
            mark = tracks.Lay(cGrid, tile, new Vector2(0.5f, 0.5f), Angle.Zero, Deep);
        });

        // The treads press a level out of the snow: the mark is still there, and is not filled in.
        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, Deep - WFGroundCoverSystem.TrackCompression));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() =>
        {
            var tracks = client.System<ClientSnowTracksSystem>();
            Assert.That(mark.Gone, Is.False, "The mark went when the treads pressed the snow.");
            Assert.That(tracks.Visibility(mark), Is.GreaterThan(0.95f), "Pressing the snow faded its own mark.");
        });

        // Ten units of fresh snow fill it halfway; a third of a level fills it in.
        var pressed = Deep - WFGroundCoverSystem.TrackCompression;
        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, pressed + ClientSnowPrintsSystem.FillUnits / 2));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientSnowTracksSystem>().Visibility(mark), Is.InRange(0.4f, 0.6f), "Fresh snow didn't half fill the mark."));

        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, Deep));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() => Assert.That(mark.Gone, Is.True, "Fresh snow didn't fill the mark in."));

        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, 0));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientSnowTracksSystem>().Marks.TryGetValue(cGrid, out var tiles) && tiles.ContainsKey(tile), Is.False,
                "A mark outlived the snow."));

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    /// <summary>A vehicle driven over snow lays two lines of marks along its path, and none on bare ground.</summary>
    [Test]
    public async Task DrivingLaysTwoLinesOfTreadMarks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default;
        await SetUp(pair, map.Grid, new Vector2i(0, 0), 100);

        // Bare ground west of x = 0, snow cover from there east.
        await server.WaitPost(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            for (var x = 0; x <= 8; x++)
            {
                for (var y = -2; y <= 2; y++)
                    cover.SetDepth(map.Grid, new Vector2i(x, y), 100);
            }

            vehicle = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Grid, new Vector2(-5.5f, 0.5f)));
            server.EntMan.AddComponent<Content.Shared._WF.Vehicles.Core.WFVehicleEngineComponent>(vehicle);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitPost(() => client.System<ClientSnowTracksSystem>().Clear());
        for (var x = -5.5f; x <= 6.5f; x += 0.25f)
        {
            var at = x;
            await server.WaitPost(() =>
                server.System<SharedTransformSystem>().SetCoordinates(vehicle, new EntityCoordinates(map.Grid, new Vector2(at, 0.5f))));
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        var cGrid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(map.Grid.Owner));
        await client.WaitAssertion(() =>
        {
            var tracks = client.System<ClientSnowTracksSystem>();
            Assert.That(tracks.Marks.TryGetValue(cGrid, out var tiles), Is.True, "Driving over snow left no tread marks.");
            Assert.That(tiles!.Keys.Where(t => t.X < 0), Is.Empty, "Bare ground took tread marks.");
            Assert.That(tiles.Keys.Count(t => t.X >= 0), Is.GreaterThanOrEqualTo(4), "Too few tiles of snow took marks.");
            var lines = tiles.Values.SelectMany(l => l).Select(m => MathF.Round(m.Position.Y, 1)).Distinct().ToList();
            Assert.That(lines, Has.Count.EqualTo(2), "The marks weren't two lines along the path.");
            Assert.That(MathF.Abs(lines[0] - lines[1]), Is.GreaterThan(0.4f), "The two lines ran together.");
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    private static async Task SetUp(TestPair pair, Entity<Robust.Shared.Map.Components.MapGridComponent> grid, Vector2i tile, int depth)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, grid);
            var gravity = server.EntMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(grid);
            gravity.Enabled = true;
            server.EntMan.Dirty(grid, gravity);
            var mapUid = server.EntMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
            server.EntMan.EnsureComponent<WFGroundWeatherComponent>(mapUid);
            server.System<WFGroundCoverSystem>().SetDepth(grid, tile, depth);

            // Snow is sent to the players near it, so the player needs a body on the ground.
            if (pair.Player!.AttachedEntity != null)
                return;

            var mob = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 6.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });
    }
}
