using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Parallax;
using Content.Server._WF.Planets;
using Robust.Shared.Timing;
using System.Linq;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetAmbiencePlaybackTest
{
    [Test]
    public async Task CarcinomaAccentsFollowNightAndDay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var server = pair.Server;
        var client = pair.Client;
        var layers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var profile = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceCarcinoma");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(profile, Vector2.Zero, "Carcinoma", null)!.Value;
            layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            server.System<BiomeSystem>().SetEnabled((layers[0], server.EntMan.GetComponent<BiomeComponent>(layers[0])), false);
        });
        var viewer = await AttachViewer(pair, layers[0], Vector2.Zero);
        await server.WaitPost(() => server.EntMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        foreach (var hour in new[] { 0.5, 12.0 })
        {
            await server.WaitPost(() =>
            {
                var network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
                var state = server.EntMan.GetComponent<WFPlanetWeatherComponent>(network);
                var weather = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>().Index(state.Profile);
                state.Epoch = server.ResolveDependency<IGameTiming>().CurTime -
                    TimeSpan.FromSeconds((hour - weather.InitialHour) / 24 * weather.DaySeconds);
            });
            await pair.RunTicksSync(pair.SecondsToTicks(3));
            var heard = false;
            for (var seconds = 0; seconds < 16; seconds++)
            {
                await pair.RunTicksSync(pair.SecondsToTicks(1));
                await client.WaitAssertion(() =>
                {
                    var clips = client.EntMan.EntityQueryEnumerator<AudioComponent>();
                    while (clips.MoveNext(out _, out var clip))
                    {
                        var filename = clip.FileName;
                        if (!filename.Contains("carcinoma_random_sound_")) continue;
                        Assert.That(filename.Contains("_night_"), Is.EqualTo(hour < 6), "Wrong time-of-day accent survived the transition.");
                        heard = true;
                    }
                });
            }
            Assert.That(heard, Is.True, $"No accent played at local hour {hour}.");
        }
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MovingBetweenWorldsChangesBedsAndPlaysTheirRandomAccents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var server = pair.Server;
        var client = pair.Client;
        var worlds = new List<(string Name, List<EntityUid> Layers)>();
        await server.WaitPost(() =>
        {
            var prototypes = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            foreach (var name in new[] { "Asclepiu", "Fervidus", "Merak", "Aerumna", "Thrascias", "Carcinoma" })
            {
                var profile = prototypes.Index<WFPlanetSurfacePrototype>("WFSurface" + name);
                var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(profile, Vector2.Zero, name, null);
                Assert.That(network, Is.Not.Null);
                var layers = server.EntMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers.ToList();
                server.System<BiomeSystem>().SetEnabled((layers[0], server.EntMan.GetComponent<BiomeComponent>(layers[0])), false);
                worlds.Add((name, layers));
            }
        });
        var viewer = await AttachViewer(pair, worlds[0].Layers[0], Vector2.Zero);
        await server.WaitPost(() => server.EntMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var oldGain = 0f;
        await client.WaitPost(() => { oldGain = cfg.GetCVar(CCVars.AmbienceVolume); cfg.SetCVar(CCVars.AmbienceVolume, 1f); });
        List<string> PlanetFiles()
        {
            var files = new List<string>();
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out _, out var audio))
            {
                var file = audio.FileName;
                if (file.StartsWith("/Audio/_WF/Planets/Worlds/")) files.Add(file);
            }
            return files;
        }
        foreach (var (name, layers) in worlds.Append(worlds[0]))
        {
            await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(viewer,
                new EntityCoordinates(layers[0], Vector2.Zero)));
            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await client.WaitAssertion(() => Assert.That(PlanetFiles().All(file =>
                file.Contains("/" + name.ToLowerInvariant() + "/")), Is.True,
                "Previous planet audio survived crossing to " + name));
            var sawAccent = false;
            for (var seconds = 0; seconds < 10; seconds++)
            {
                await pair.RunTicksSync(pair.SecondsToTicks(1));
                await client.WaitAssertion(() => sawAccent |= PlanetFiles().Any(file =>
                    file.Contains("/" + name.ToLowerInvariant() + "/") && file.Contains("_random_sound_")));
            }
            await client.WaitAssertion(() =>
            {
                var beds = PlanetFiles().Where(file => file.Contains("_ambience_")).ToList();
                Assert.That(beds, Has.Count.EqualTo(1), name);
                Assert.That(beds[0], Does.Contain("/" + name.ToLowerInvariant() + "/"), name);
            });
            if (name is not ("Aerumna" or "Carcinoma")) continue;
            for (var seconds = 0; seconds < 6 && !sawAccent; seconds++)
            {
                await pair.RunTicksSync(pair.SecondsToTicks(1));
                await client.WaitAssertion(() => sawAccent = PlanetFiles().Any(file => file.Contains("_random_sound_")));
            }
            Assert.That(sawAccent, Is.True, name + " did not play its supplied arrival accent within 16 seconds.");
        }
        await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, oldGain));
        foreach (var (_, layers) in worlds) await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrdinaryMusicIsSuppressedOnPlanetAndResumesInOrbit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var client = pair.Client;
        await server.WaitPost(() => server.System<BiomeSystem>().SetEnabled(
            (layers[0], server.EntMan.GetComponent<BiomeComponent>(layers[0])), false));
        var viewer = await AttachViewer(pair, layers[^1], Vector2.Zero);
        await server.WaitPost(() => server.EntMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        var music = client.System<Content.Client.Audio.ContentAudioSystem>();
        // Exercise the shared playback entry used by biome, grid and replay-timer requests.
        var play = typeof(Content.Client.Audio.ContentAudioSystem).GetMethod("PlayMusicTrack",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        const string track = "/Audio/Ambience/ambigen9.ogg";
        int CountTrack()
        {
            var count = 0;
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out _, out var audio))
                if (audio.FileName == track) count++;
            return count;
        }
        await client.WaitPost(() => play.Invoke(music, new object[] { track, -20f, 0f, false }));
        await client.WaitAssertion(() => Assert.That(CountTrack(), Is.EqualTo(1)));
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(viewer,
            new EntityCoordinates(layers[0], Vector2.Zero)));
        await pair.RunTicksSync(pair.SecondsToTicks(5));
        await client.WaitAssertion(() => Assert.That(CountTrack(), Is.Zero,
            "Music already playing in orbit must fade away on arrival."));
        await client.WaitPost(() => play.Invoke(music, new object[] { track, -20f, 0f, false }));
        await client.WaitAssertion(() => Assert.That(CountTrack(), Is.Zero,
            "A new ordinary music request must be refused on the planet."));
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(viewer,
            new EntityCoordinates(layers[^1], Vector2.Zero)));
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await client.WaitPost(() => play.Invoke(music, new object[] { track, -20f, 0f, false }));
        await client.WaitAssertion(() => Assert.That(CountTrack(), Is.EqualTo(1),
            "Returning to orbit must allow ordinary music again."));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LoopPlaysOnSurfaceAndStopsInOrbitAndWhenMuted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var client = pair.Client;
        await server.WaitPost(() => server.System<BiomeSystem>().SetEnabled(
            (layers[0], server.EntMan.GetComponent<BiomeComponent>(layers[0])), false));
        var viewer = await AttachViewer(pair, layers[0], Vector2.Zero);
        // Suspend the test mob's z-physics so it stays in orbit while the fade completes.
        await server.WaitPost(() => server.EntMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var oldGain = 0f;
        await client.WaitPost(() => { oldGain = cfg.GetCVar(CCVars.AmbienceVolume); cfg.SetCVar(CCVars.AmbienceVolume, 1f); });
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        void AssertWind(int expected)
        {
            var count = 0;
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out _, out var audio))
                if (audio.FileName == "/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_day_loop_1.ogg")
                    count++;
            Assert.That(count, Is.EqualTo(expected));
        }
        await client.WaitAssertion(() => AssertWind(1));
        var oldStreams = new List<EntityUid>();
        await client.WaitPost(() =>
        {
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out var uid, out var audio))
            {
                var file = audio.FileName;
                if (file.StartsWith("/Audio/_WF/Planets/Worlds/")) oldStreams.Add(uid);
            }
            var ambience = client.System<Content.Client._WF.Planets.WFPlanetAmbienceSystem>();
            var stop = ambience.GetType().GetMethod("StopAll", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;
            using (client.ResolveDependency<Robust.Client.Timing.IClientGameTiming>().StartStateApplicationArea())
                stop.Invoke(ambience, null);
        });
        await pair.RunTicksSync(3);
        await client.WaitAssertion(() => Assert.That(oldStreams.All(uid => !client.EntMan.EntityExists(uid)), Is.True,
            "Cleanup during player state application leaked untracked global ambience."));
        await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, 0f));
        await pair.RunTicksSync(pair.SecondsToTicks(5));
        await client.WaitAssertion(() => AssertWind(0));
        await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, 1f));
        await pair.RunTicksSync(pair.SecondsToTicks(3));
        await client.WaitAssertion(() => AssertWind(1));
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(layers[^1], Vector2.Zero)));
        await pair.RunTicksSync(pair.SecondsToTicks(5));
        await server.WaitAssertion(() => Assert.That(server.EntMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(layers[^1])));
        await client.WaitAssertion(() => AssertWind(0));
        await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, oldGain));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task DayNightAndNumberedLoopsCrossfadeWithoutAccumulatingSources()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var client = pair.Client;
        await server.WaitPost(() => server.System<BiomeSystem>().SetEnabled(
            (layers[0], server.EntMan.GetComponent<BiomeComponent>(layers[0])), false));
        var viewer = await AttachViewer(pair, layers[0], Vector2.Zero);
        await server.WaitPost(() => server.EntMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var oldGain = 0f;
        await client.WaitPost(() => { oldGain = cfg.GetCVar(CCVars.AmbienceVolume); cfg.SetCVar(CCVars.AmbienceVolume, 1f); });
        List<string> Beds()
        {
            var paths = new List<string>();
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out _, out var audio))
            {
                var file = audio.FileName;
                if (file.StartsWith("/Audio/_WF/Planets/Worlds/") && file.Contains("_ambience_"))
                {
                    Assert.That(float.IsNaN(audio.Params.Volume), Is.False, "Crossfade produced invalid gain.");
                    paths.Add(file);
                }
            }
            Assert.That(paths.Count, Is.LessThanOrEqualTo(2), "Crossfade allocated more than two beds.");
            return paths;
        }
        await pair.RunTicksSync(pair.SecondsToTicks(6));
        await client.WaitAssertion(() => Assert.That(Beds().Single(), Does.Contain("_day_loop_1")));
        await server.WaitPost(() =>
        {
            var network = server.EntMan.GetComponent<CEZMapComponent>(layers[0]).NetworkUid;
            server.EntMan.GetComponent<WFPlanetWeatherComponent>(network).Epoch =
                server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromMinutes(28);
        });
        var overlap = false;
        for (var i = 0; i < 10; i++)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await client.WaitAssertion(() => overlap |= Beds().Count == 2);
        }
        await client.WaitAssertion(() =>
        {
            Assert.That(overlap, Is.True, "Day/night changed abruptly without overlapping beds.");
            Assert.That(Beds().Single(), Does.Contain("_night_loop_1"));
        });
        // Switch to the four-track all-day playlist, then let real clip length trigger its next numbered bed.
        await server.WaitPost(() =>
        {
            var ambience = server.EntMan.GetComponent<WFPlanetAmbienceComponent>(layers[0]);
            ambience.Profile = "WFPlanetAmbienceFervidus";
            server.EntMan.Dirty(layers[0], ambience);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(8));
        await client.WaitAssertion(() => Assert.That(Beds().Single(), Does.Contain("fervidus_ambience_loop_1")));
        var sawSecond = false;
        var sawNumberedOverlap = false;
        for (var i = 0; i < 36; i++)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(1));
            await client.WaitAssertion(() =>
            {
                var beds = Beds();
                sawSecond |= beds.Any(path => path.Contains("fervidus_ambience_loop_2"));
                sawNumberedOverlap |= beds.Count == 2;
            });
        }
        await client.WaitAssertion(() =>
        {
            Assert.That(sawSecond, Is.True, "Numbered playlist never advanced.");
            Assert.That(sawNumberedOverlap, Is.True, "Numbered loops did not overlap during transition.");
        });
        await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, 0));
        await pair.RunTicksSync(pair.SecondsToTicks(6));
        await client.WaitAssertion(() => Assert.That(Beds(), Is.Empty));
        await client.WaitPost(() => cfg.SetCVar(CCVars.AmbienceVolume, oldGain));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

}
