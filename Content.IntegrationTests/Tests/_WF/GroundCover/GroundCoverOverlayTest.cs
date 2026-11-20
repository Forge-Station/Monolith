#nullable enable
using System.Numerics;
using Content.Client._WF.GroundCover;
using Content.Shared._WF.GroundCover;
using Content.Shared._WF.WolfWeather;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>The client's wet ground overlay: its shader loads, and it draws in rain at each weather quality.</summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverOverlay))]
public sealed class GroundCoverOverlayTest
{
    // A plain string: the server-side linter checks ProtoId fields and has no client shader prototypes.
    private const string Shader = "WFGroundCover";

    [Test]
    public async Task OverlayLoadsItsShaderAndDrawsInRain()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            server.System<SharedWolfWeatherSystem>().SetWeather(map.MapUid, "WFRainHeavy", null, 0);

            var ground = entMan.EnsureComponent<WFGroundWeatherComponent>(map.MapUid);
            ground.WetnessExact = 1f;
            ground.PuddlesExact = 0.6f;
            entMan.System<Content.Server._WF.GroundCover.WFGroundCoverSystem>().StepMap((map.MapUid, ground), 0f);
            ground.NextUpdate = TimeSpan.MaxValue;
        });

        // Past the weather's fade-in, so it rains at full strength.
        await pair.RunTicksSync(pair.SecondsToTicks(16));

        var cfg = client.ResolveDependency<IConfigurationManager>();
        await client.WaitAssertion(() =>
        {
            var overlays = client.ResolveDependency<IOverlayManager>();
            Assert.That(overlays.HasOverlay<WFGroundCoverOverlay>(), Is.True, "The ground overlay isn't registered.");
            Assert.That(overlays.HasOverlay<WFGroundReflectionOverlay>(), Is.True, "The reflection overlay isn't registered.");

            // Indexing and instancing the shader parses its source, which throws on a broken shader.
            var proto = client.ResolveDependency<IPrototypeManager>().Index<ShaderPrototype>(Shader);
            Assert.DoesNotThrow(() => proto.InstanceUnique().Dispose());

            var overlay = overlays.GetOverlay<WFGroundCoverOverlay>();
            var mapId = client.EntMan.GetComponent<TransformComponent>(
                client.EntMan.GetEntity(entMan.GetNetEntity(map.MapUid))).MapID;

            cfg.SetCVar(WolfWeatherCVars.WeatherQuality, 2);
            Assert.That(overlay.UpdateWeather(mapId), Is.True, "The overlay doesn't draw wet ground in rain.");
            Assert.That(overlay.Rain, Is.EqualTo(1f).Within(0.01f), "The overlay doesn't see the rain falling.");
            Assert.That(overlay.Detail, Is.True, "Full quality left out ripples and reflections.");
            Assert.That(overlay.Tint.X, Is.EqualTo(WFGroundWeatherComponent.Water.R).Within(0.05f), "Plain rain isn't water coloured.");

            cfg.SetCVar(WolfWeatherCVars.WeatherQuality, 1);
            Assert.That(overlay.UpdateWeather(mapId), Is.True, "Reduced quality dropped the wet ground.");
            Assert.That(overlay.Detail, Is.False, "Reduced quality kept ripples and reflections.");

            cfg.SetCVar(WolfWeatherCVars.WeatherQuality, 0);
            Assert.That(overlay.UpdateWeather(mapId), Is.False, "Weather quality Off still drew wet ground.");

            cfg.SetCVar(WolfWeatherCVars.WeatherQuality, 2);
        });

        // The look's tuning is set on the server and reaches the client, which draws with it.
        var serverCfg = server.ResolveDependency<IConfigurationManager>();
        await server.WaitPost(() => serverCfg.SetCVar(WFGroundCoverCVars.PuddleCoverage, 0.5f));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(WFGroundCoverCVars.PuddleCoverage), Is.EqualTo(0.5f),
                "The puddle coverage cvar didn't reach the client.");
        });
        await server.WaitPost(() => serverCfg.SetCVar(WFGroundCoverCVars.PuddleCoverage, 1f));
        await pair.RunTicksSync(10);

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }
}
