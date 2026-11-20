#nullable enable
using System.Linq;
using Content.Client._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Weather;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A weather loop follows the ambience slider, and stops once the player has no body, as back in the lobby after a
/// respawn: the weather system left it looping there.
/// </summary>
[TestFixture]
[TestOf(typeof(WFWeatherAudioSystem))]
public sealed class WeatherAudioTest
{
    private const string Rain = "WFRainLow";

    [Test]
    public async Task RainFollowsTheSliderAndStopsWithoutABody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var mob = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
            server.System<SharedWeatherSystem>().SetWeather(map.MapId, proto.Index<WeatherPrototype>(Rain), null);
        });

        // Past the weather's start-up fade, so its own volume holds still.
        await pair.RunTicksSync(pair.SecondsToTicks((float) WeatherComponent.StartupTime.TotalSeconds + 1f));

        var mapNet = sEntMan.GetNetEntity(map.MapUid);
        EntityUid? stream = null;
        var atDefault = 0f;

        await client.WaitPost(() =>
        {
            var weather = cEntMan.GetComponent<WeatherComponent>(cEntMan.GetEntity(mapNet));
            stream = weather.Weather[Rain].Stream;
            Assert.That(stream, Is.Not.Null, "The rain never started its loop.");
            atDefault = cEntMan.GetComponent<AudioComponent>(stream!.Value).Params.Volume;
            client.CfgMan.SetCVar(CCVars.AmbienceVolume, CCVars.AmbienceVolume.DefaultValue / 2f);
        });

        await pair.RunTicksSync(3);

        await client.WaitPost(() =>
        {
            var halved = cEntMan.GetComponent<AudioComponent>(stream!.Value).Params.Volume;
            var expected = atDefault + SharedAudioSystem.GainToVolume(0.5f);
            Assert.That(halved, Is.EqualTo(expected).Within(0.1f), "The rain did not follow the ambience slider.");
            client.CfgMan.SetCVar(CCVars.AmbienceVolume, CCVars.AmbienceVolume.DefaultValue);
        });

        // Back to the lobby: the session has no body any more.
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.RunTicksSync(5);

        await client.WaitPost(() =>
        {
            Assert.That(client.ResolveDependency<IPlayerManager>().LocalEntity, Is.Null);
            var weather = cEntMan.GetComponent<WeatherComponent>(cEntMan.GetEntity(mapNet));
            Assert.That(weather.Weather[Rain].Stream, Is.Null, "The rain loop is still held without a body.");
            Assert.That(cEntMan.Deleted(stream!.Value), Is.True, "The rain loop still plays without a body.");

            var rain = cEntMan.EntityQuery<AudioComponent>(true)
                .Select(audio =>
                {
                    string name = audio.FileName;
                    return name;
                })
                .Where(name => name.Contains("/Planets/Weather/"));
            Assert.That(rain, Is.Empty, "A weather loop is left playing.");
        });

        await pair.CleanReturnAsync();
    }
}
