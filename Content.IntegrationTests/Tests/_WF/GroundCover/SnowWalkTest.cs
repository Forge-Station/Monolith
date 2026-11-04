#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.GroundCover;
using Content.Shared._WF.GroundCover;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using ClientGroundCoverSystem = Content.Client._WF.GroundCover.WFGroundCoverSystem;
using ClientSnowSlowSystem = Content.Client._WF.GroundCover.WFSnowSlowSystem;
using ClientInputSystem = Robust.Client.GameObjects.InputSystem;
using ClientSnowPrintsSystem = Content.Client._WF.GroundCover.WFSnowPrintsSystem;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>Walking in snow cover: predicted slowdown, crunchy footsteps and footprints.</summary>
[TestFixture]
[TestOf(typeof(SharedWFSnowSlowSystem))]
public sealed class SnowWalkTest
{
    private const int Deep = 180;
    private const int Middling = 100;
    private const int Thin = 30;

    [Test]
    public async Task WalkingThroughPatchySnowIsPredicted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            Setup(pair, map.Grid);
            var cover = server.System<WFGroundCoverSystem>();
            for (var x = -8; x <= 8; x++)
            {
                for (var y = -2; y <= 2; y++)
                    cover.SetDepth(map.Grid, new Vector2i(x, y), PatchyDepth(x));
            }

            mob = sEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(-6.5f, 0.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        var cMob = client.EntMan.GetEntity(sEntMan.GetNetEntity(mob));
        var cGrid = client.EntMan.GetEntity(sEntMan.GetNetEntity(map.Grid.Owner));
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientGroundCoverSystem>().GetDepth(cGrid, new Vector2i(0, 0)), Is.EqualTo(PatchyDepth(0)),
                "Precondition: the client wasn't sent the snow."));

        var serverTrack = new Dictionary<GameTick, (float X, float Speed)>();
        var clientTrack = new Dictionary<GameTick, (float X, float Speed)>();
        await SetKey(pair, EngineKeyFunctions.MoveRight, BoundKeyState.Down);
        for (var i = 0; i < pair.SecondsToTicks(3); i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => serverTrack[server.Timing.CurTick] = Sample(sEntMan, mob));
            await client.WaitPost(() => clientTrack.TryAdd(client.Timing.CurTick, Sample(client.EntMan, cMob)));
        }

        await SetKey(pair, EngineKeyFunctions.MoveRight, BoundKeyState.Up);
        await pair.RunTicksSync(pair.SecondsToTicks(1));

        // The pair's client samples are a tick or so off the server's, and position and speed are set at different
        // points of a tick: line each up at whichever lead fits best. A correction after a speed change shows as a miss.
        var moved = serverTrack.Values.Max(s => s.X) - serverTrack.Values.Min(s => s.X);
        var speeds = serverTrack.Values.Select(s => s.Speed).Distinct().Count();
        var leads = Enumerable.Range(0, 4).Select(lead => Misses(serverTrack, clientTrack, lead)).ToList();
        var position = leads.MinBy(m => m.Position);
        var speed = leads.MinBy(m => m.Speed);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.GreaterThan(4f), "Precondition: the mob barely walked.");
            Assert.That(speeds, Is.GreaterThanOrEqualTo(3), "Precondition: the walk didn't cross patchy snow.");
            Assert.That(position.Compared, Is.GreaterThan(pair.SecondsToTicks(2)), "Too few ticks seen on both sides to compare.");
            Assert.That(speed.Speed, Is.Zero, "The client predicted a different snow speed than the server had.");
            Assert.That(position.Position, Is.Zero, "The client's predicted walk strayed from the server's.");
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShovelingFreesTheWalkerOnBothSides()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var tile = new Vector2i(0, 0);
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            Setup(pair, map.Grid);
            var cover = server.System<WFGroundCoverSystem>();
            for (var x = -2; x <= 2; x++)
            {
                for (var y = -2; y <= 2; y++)
                    cover.SetDepth(map.Grid, new Vector2i(x, y), Deep);
            }

            mob = sEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        var cMob = client.EntMan.GetEntity(sEntMan.GetNetEntity(mob));
        // The snow's own share: the cold map may add the warmth system's slowdown on top of it.
        await server.WaitAssertion(() =>
            Assert.That(server.System<WFSnowSlowSystem>().GetSpeed(mob), Is.EqualTo(WFSnowCover.DeepestSpeed).Within(0.001f), "Deep snow didn't slow the walker."));
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientSnowSlowSystem>().GetSpeed(cMob), Is.EqualTo(WFSnowCover.DeepestSpeed).Within(0.001f),
                "The client didn't predict the slowdown."));

        // Melt or a shovel: the tile goes bare.
        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, tile, 0));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await server.WaitAssertion(() =>
            Assert.That(server.System<WFSnowSlowSystem>().GetSpeed(mob), Is.EqualTo(1f).Within(0.001f), "The bare tile still slowed the walker."));
        await client.WaitAssertion(() =>
            Assert.That(client.System<ClientSnowSlowSystem>().GetSpeed(cMob), Is.EqualTo(1f).Within(0.001f), "The client still slowed the walker."));

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FootstepsCrunchOnSnowCover()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var covered = new Vector2i(0, 0);
        var bare = new Vector2i(3, 0);
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            Setup(pair, map.Grid);
            server.System<WFGroundCoverSystem>().SetDepth(map.Grid, covered, WFSnowCover.UnitsPerLevel);
            server.System<WFGroundCoverSystem>().SetDepth(map.Grid, new Vector2i(1, 0), WFSnowCover.CoverFrom - 1);
            mob = sEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await server.WaitAssertion(() =>
        {
            Assert.That(Step(sEntMan, mob, map.Grid, covered), Is.EqualTo("FootstepSnow"), "No crunch on snow cover.");
            Assert.That(Step(sEntMan, mob, map.Grid, new Vector2i(1, 0)), Is.Null, "A dusting crunched like cover.");
            Assert.That(Step(sEntMan, mob, map.Grid, bare), Is.Null, "Bare ground crunched.");
        });

        var cMob = client.EntMan.GetEntity(sEntMan.GetNetEntity(mob));
        var cGrid = client.EntMan.GetEntity(sEntMan.GetNetEntity(map.Grid.Owner));
        await client.WaitAssertion(() =>
        {
            Assert.That(Step(client.EntMan, cMob, cGrid, covered), Is.EqualTo("FootstepSnow"), "The client's step didn't crunch.");
            Assert.That(Step(client.EntMan, cMob, cGrid, bare), Is.Null);
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FootprintsLieOnCoverAndShovelOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid mob = default;

        // Bare ground west of x = 0, snow cover from there east.
        await server.WaitPost(() =>
        {
            Setup(pair, map.Grid);
            var cover = server.System<WFGroundCoverSystem>();
            for (var x = 0; x <= 8; x++)
            {
                for (var y = -2; y <= 2; y++)
                    cover.SetDepth(map.Grid, new Vector2i(x, y), Middling);
            }

            mob = sEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(-5.5f, 0.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitPost(() => client.System<ClientSnowPrintsSystem>().Clear());

        // Walk it east by hand, a quarter tile a tick.
        for (var x = -5.5f; x <= 6.5f; x += 0.25f)
        {
            var at = x;
            await server.WaitPost(() =>
                server.System<SharedTransformSystem>().SetCoordinates(mob, new EntityCoordinates(map.Grid, new Vector2(at, 0.5f))));
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        var cGrid = client.EntMan.GetEntity(sEntMan.GetNetEntity(map.Grid.Owner));
        var printed = new Vector2i(3, 0);
        await client.WaitAssertion(() =>
        {
            var prints = client.System<ClientSnowPrintsSystem>();
            Assert.That(prints.Prints.TryGetValue(cGrid, out var tiles), Is.True, "Walking through snow left no prints.");
            Assert.That(tiles!.Keys.Where(t => t.X < 0), Is.Empty, "Bare ground took prints.");
            Assert.That(tiles.Keys.Count(t => t.X >= 0), Is.GreaterThanOrEqualTo(5), "Too few tiles of snow took prints.");
            Assert.That(tiles.ContainsKey(printed), Is.True, "Precondition: no print on the tile to be shoveled.");
        });

        await server.WaitPost(() => server.System<WFGroundCoverSystem>().SetDepth(map.Grid, printed, 0));
        await pair.RunTicksSync(pair.SecondsToTicks(2));
        await client.WaitAssertion(() =>
        {
            var tiles = client.System<ClientSnowPrintsSystem>().Prints[cGrid];
            Assert.That(tiles.ContainsKey(printed), Is.False, "Prints outlived the shoveled snow.");
            Assert.That(tiles.ContainsKey(printed + new Vector2i(1, 0)), Is.True, "Shoveling one tile wiped its neighbour's prints.");
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    /// <summary>Ticks where the client's prediction, a lead of ticks ahead, differs from the server.</summary>
    private static (int Compared, int Position, int Speed) Misses(Dictionary<GameTick, (float X, float Speed)> server,
        Dictionary<GameTick, (float X, float Speed)> client, int lead)
    {
        var compared = 0;
        var position = 0;
        var speed = 0;
        foreach (var (tick, at) in server)
        {
            if (!client.TryGetValue(new GameTick(tick.Value + (uint) lead), out var predicted))
                continue;

            compared++;
            if (MathF.Abs(at.X - predicted.X) > 0.01f)
                position++;

            if (MathF.Abs(at.Speed - predicted.Speed) > 0.001f)
                speed++;
        }

        return (compared, position, speed);
    }

    private static int PatchyDepth(int x)
    {
        return (((x % 3) + 3) % 3) switch
        {
            0 => Deep,
            1 => Middling,
            _ => Thin,
        };
    }

    private static (float X, float Speed) Sample(IEntityManager entMan, EntityUid mob)
    {
        return (entMan.GetComponent<TransformComponent>(mob).LocalPosition.X,
            entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier);
    }

    private static string? Step(IEntityManager entMan, EntityUid mob, EntityUid grid, Vector2i tile)
    {
        var ev = new WFSnowFootstepEvent(mob, grid, tile);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return (ev.Sound as SoundCollectionSpecifier)?.Collection;
    }

    /// <summary>Open ground with gravity, on a map with weather so snow is sent.</summary>
    private static void Setup(TestPair pair, Entity<MapGridComponent> grid)
    {
        GroundCoverTest.LayOpenGround(pair, grid);
        var entMan = pair.Server.EntMan;
        var gravity = entMan.EnsureComponent<GravityComponent>(grid);
        gravity.Enabled = true;
        entMan.Dirty(grid, gravity);
        var map = entMan.GetComponent<TransformComponent>(grid).MapUid!.Value;
        entMan.EnsureComponent<WFGroundWeatherComponent>(map);
    }

    private static async Task SetKey(TestPair pair, BoundKeyFunction key, BoundKeyState state)
    {
        var client = pair.Client;
        await client.WaitPost(() =>
        {
            var input = client.ResolveDependency<Robust.Client.Input.IInputManager>();
            var session = client.ResolveDependency<IPlayerManager>().LocalSession!;
            var message = new ClientFullInputCmdMessage(client.Timing.CurTick, client.Timing.TickFraction,
                input.NetworkBindMap.KeyFunctionID(key))
            {
                State = state,
                Coordinates = EntityCoordinates.Invalid,
            };

            client.System<ClientInputSystem>().HandleInputCommand(session, key, message);
        });
    }
}
