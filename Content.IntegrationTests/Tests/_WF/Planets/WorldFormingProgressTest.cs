#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Planets;
using Content.Client.Lobby;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Client.State;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A forming world's progress climbs steadily from 0 to done, and the clients get it: the lobby's bar while a
/// round-start world forms, and the in-round bar for a player over a world still forming.
/// </summary>
[TestFixture]
[TestOf(typeof(WFWorldFormingSystem))]
[TestOf(typeof(WFWorldFormingClientSystem))]
public sealed class WorldFormingProgressTest
{
    private const int Radius = 64;

    [Test]
    public async Task ProgressClimbsToDoneAndReachesTheClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var forming = server.System<WFWorldFormingSystem>();
        var clientForming = client.System<WFWorldFormingClientSystem>();

        await EnableSlowWorlds(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];

        try
        {
            var groundNet = server.EntMan.GetNetEntity(ground);

            var samples = new List<float>();
            var stages = new List<WFWorldFormingStage>();
            var seenByClient = false;
            var hudShown = false;

            for (var tick = 0; tick < 20000 && !server.EntMan.HasComponent<WFPlanetPreloadedComponent>(ground); tick++)
            {
                // A player over the world, in orbit, sees the in-round bar.
                if (tick == 5)
                    await AttachViewer(pair, layers[^1], new Vector2(0f, 0f));

                await pair.RunTicksSync(1);

                await server.WaitPost(() =>
                {
                    var entry = forming.Measure().Worlds.FirstOrDefault(world => world.Ground == groundNet);
                    if (entry == null)
                        return;

                    samples.Add(entry.Progress);
                    if (stages.Count == 0 || stages[^1] != entry.Stage)
                    {
                        stages.Add(entry.Stage);
                        TestContext.Out.WriteLine($"{entry.Stage} from sample {samples.Count - 1} at {entry.Progress:P0}");
                    }
                });

                await client.WaitPost(() =>
                {
                    seenByClient |= clientForming.State.Worlds.Any(world => world.Ground == groundNet && world.Progress > 0f);
                    hudShown |= clientForming.HudVisible;
                });
            }

            Assert.That(server.EntMan.HasComponent<WFPlanetPreloadedComponent>(ground), Is.True, "The world never formed.");
            TestContext.Out.WriteLine($"{samples.Count} samples; stages {string.Join(", ", stages)}");
            for (var decile = 1; decile <= 9; decile++)
            {
                var first = samples.FindIndex(sample => sample >= decile / 10f);
                TestContext.Out.WriteLine($"{decile * 10}% at sample {first}");
            }

            var drops = samples.Zip(samples.Skip(1), (a, b) => a - b).Where(drop => drop > 0f).ToList();
            var biggestJump = samples.Zip(samples.Skip(1), (a, b) => b - a).DefaultIfEmpty(0f).Max();

            Assert.Multiple(() =>
            {
                Assert.That(samples, Has.Count.GreaterThan(20), "The world formed too fast to watch.");
                // A small test world places its few sites within a tick or two; the terrain starts at 40%.
                Assert.That(samples[0], Is.LessThan(0.45f), "The progress started past the terrain.");
                Assert.That(drops, Is.Empty, "The progress went backwards.");
                Assert.That(samples.Distinct().Count(), Is.GreaterThan(10), "The progress moved in a few big steps.");
                Assert.That(biggestJump, Is.LessThan(0.3f), "The progress sat still and then jumped.");
                Assert.That(stages, Does.Contain(WFWorldFormingStage.Terrain));
                Assert.That(stages, Does.Contain(WFWorldFormingStage.Boundary));
                Assert.That(stages.IndexOf(WFWorldFormingStage.Terrain), Is.LessThan(stages.IndexOf(WFWorldFormingStage.Boundary)));
                Assert.That(seenByClient, Is.True, "The client never got the world's progress.");
                Assert.That(hudShown, Is.True, "The player over the forming world never saw the in-round bar.");
            });

            // Done: the world drops out, and one empty state clears the client.
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
            await server.WaitPost(() => Assert.That(forming.Measure().Worlds, Is.Empty));
            await client.WaitPost(() =>
            {
                Assert.That(clientForming.State.Worlds, Is.Empty, "The client still shows a world forming.");
                Assert.That(clientForming.HudVisible, Is.False, "The in-round bar outlived the forming.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheLobbyShowsARoundStartWorldForming()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, InLobby = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var clientForming = client.System<WFWorldFormingClientSystem>();
        var states = client.ResolveDependency<IStateManager>();

        await EnableSlowWorlds(pair);
        var layers = await Prebuild(pair);
        var ground = layers[0];

        try
        {
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await client.WaitPost(() =>
            {
                var world = clientForming.State.Worlds.FirstOrDefault();
                Assert.That(world, Is.Not.Null, "The lobby never got the forming world.");
                Assert.That(world!.RoundStart, Is.True, "The prebuilt world is not marked as a round-start world.");

                var bar = LobbyBar(states);
                Assert.That(bar, Is.Not.Null, "The lobby has no world-forming bar.");
                Assert.That(bar!.Visible, Is.True, "The lobby's bar is hidden while a world forms.");
                Assert.That(bar.Title, Does.Contain("Asclepiu"));
            });

            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(PlanetCVars.PreloadBudgetLobby, 1000f);
                server.CfgMan.SetCVar(PlanetCVars.GenerationBudgetLobby, 200f);
            });
            await PlanetBoundsTest.WaitPreloaded(pair, ground);
            await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await client.WaitPost(() =>
            {
                Assert.That(clientForming.State.Worlds, Is.Empty);
                Assert.That(LobbyBar(states)?.Visible, Is.False, "The lobby's bar outlived the forming.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static WFWorldFormingBar? LobbyBar(IStateManager states)
    {
        if (states.CurrentState is not LobbyState { Lobby: { } lobby })
            return null;

        return lobby.HeaderContainer.Children.OfType<WFWorldFormingBar>().FirstOrDefault();
    }

    /// <summary>Bounded, preloading worlds on a small budget, so forming takes long enough to watch.</summary>
    private static async Task EnableSlowWorlds(TestPair pair)
    {
        await EnableFeature(pair);
        await pair.Server.WaitPost(() =>
        {
            var cfg = pair.Server.CfgMan;
            cfg.SetCVar(PlanetCVars.Bounds, true);
            cfg.SetCVar(PlanetCVars.Radius, Radius);
            cfg.SetCVar(PlanetCVars.Preload, true);
            cfg.SetCVar(PlanetCVars.PreloadBudget, 0.2f);
            cfg.SetCVar(PlanetCVars.PreloadBudgetLobby, 0.2f);
            cfg.SetCVar(PlanetCVars.GenerationBudget, 1f);
            cfg.SetCVar(PlanetCVars.GenerationBudgetLobby, 1f);
            cfg.SetCVar(PlanetCVars.RoundHoldMax, 600f);
        });
    }

    /// <summary>Builds an Asclepiu network in the lobby the way the lobby prebuild does, and returns its layers.</summary>
    private static async Task<List<EntityUid>> Prebuild(TestPair pair)
    {
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var layers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(SurfaceProto);
            Assert.That(networks.Prebuild(surface, new Vector2(3000f, 0f), "Asclepiu"), Is.True, "The network failed to prebuild.");
            foreach (var prebuilt in networks.PrebuiltGrounds())
            {
                var layer = server.EntMan.GetComponent<WFPlanetLayerComponent>(prebuilt);
                var network = server.EntMan.GetEntity(layer.Network!.Value);
                layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            }
        });

        await server.WaitRunTicks(1);
        return layers;
    }
}
