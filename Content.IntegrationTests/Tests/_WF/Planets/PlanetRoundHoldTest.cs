#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A round-start world still forming holds the round start, for at most the cap, and a world still forming refuses
/// descent until it is done.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetRoundHoldSystem))]
[TestOf(typeof(WFOrbitEntrySystem))]
public sealed class PlanetRoundHoldTest
{
    /// <summary>Small enough to preload in a few seconds once let go.</summary>
    private const int Radius = 64;

    [Test]
    public async Task TheRoundWaitsForTheWorldsToForm()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, InLobby = true, Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var hold = server.System<WFPlanetRoundHoldSystem>();

        await EnableSlowWorlds(pair, cap: 600f);
        var layers = await Prebuild(pair);

        try
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
            await server.WaitPost(() =>
            {
                Assert.That(hold.WorldsForming(), Is.True, "The prebuilt world is not forming.");
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
            });
            await pair.RunTicksSync(5);

            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby), "The round started on a world still forming.");
            Assert.That(hold.Holding, Is.True, "Nothing holds the round start.");

            // Let the world finish; the held start then goes ahead by itself.
            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PreloadBudgetLobby, 1000f));
            for (var i = 0; i < 3000 && ticker.RunLevel == GameRunLevel.PreRoundLobby; i++)
            {
                await pair.RunTicksSync(1);
            }

            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "The round never started once the worlds formed.");
            Assert.That(server.EntMan.HasComponent<WFPlanetPreloadedComponent>(layers[0]), Is.True, "The round started before the world formed.");
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCapLetsTheRoundStartAnyway()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, InLobby = true, Dirty = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var hold = server.System<WFPlanetRoundHoldSystem>();
        var preload = server.System<WFPlanetPreloadSystem>();

        await EnableSlowWorlds(pair, cap: 1f);
        var layers = await Prebuild(pair);

        try
        {
            await server.WaitPost(() =>
            {
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
            });
            await pair.RunTicksSync(5);
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby), "The round started on a world still forming.");

            await pair.RunTicksSync(pair.SecondsToTicks(2f));

            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "The cap did not let the round start.");
            Assert.That(preload.IsPreloading(layers[0]), Is.True, "The world finished first, so the cap was never tested.");
            Assert.That(hold.Holding, Is.False);
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AWorldStillFormingRefusesDescent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();

        await EnableSlowWorlds(pair, cap: 600f);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];

        try
        {
            var orbitMap = MapId.Nullspace;
            await server.WaitPost(() => orbitMap = entMan.GetComponent<MapComponent>(layers[^1]).MapId);
            var hull = await BuildHull(pair, orbitMap);
            await MapInitHull(pair, hull);

            var forming = loc.GetString("wf-orbit-world-forming", ("planet", entMan.GetComponent<MetaDataComponent>(ground).EntityName));
            Assert.That(server.System<WFPlanetPreloadSystem>().IsPreloading(ground), Is.True, "The world is not forming.");
            var refusal = await EnterAtmosphere(pair, hull, settle: 0f);
            Assert.That(refusal, Is.EqualTo(forming), "A hull descended onto a world still forming.");

            await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.PreloadBudgetLobby, 1000f));
            await PlanetBoundsTest.WaitPreloaded(pair, ground);

            refusal = await EnterAtmosphere(pair, hull, settle: 0f);
            Assert.That(refusal, Is.Not.EqualTo(forming), "A formed world still refuses descent as forming.");
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Bounded, preloading worlds that load one chunk a tick until a test lets them go.</summary>
    private static async Task EnableSlowWorlds(TestPair pair, float cap)
    {
        await EnableFeature(pair);
        await pair.Server.WaitPost(() =>
        {
            var cfg = pair.Server.CfgMan;
            cfg.SetCVar(PlanetCVars.Bounds, true);
            cfg.SetCVar(PlanetCVars.Radius, Radius);
            cfg.SetCVar(PlanetCVars.Preload, true);
            cfg.SetCVar(PlanetCVars.PreloadBudgetLobby, 0.01f);
            cfg.SetCVar(PlanetCVars.PreloadBudget, 0.01f);
            cfg.SetCVar(PlanetCVars.RoundHoldMax, cap);
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
            foreach (var ground in networks.PrebuiltGrounds())
            {
                var layer = server.EntMan.GetComponent<WFPlanetLayerComponent>(ground);
                var network = server.EntMan.GetEntity(layer.Network!.Value);
                layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            }
        });

        await server.WaitRunTicks(1);
        return layers;
    }
}
