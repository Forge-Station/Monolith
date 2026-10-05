#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>A hull crash stays within an audio budget, so it cannot exhaust the client's OpenAL sources.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class CrashAudioTest
{
    /// <summary>Explosion clips one crash may play.</summary>
    private const int ExplosionBudget = 4;

    /// <summary>Every sound the whole crash is allowed to make, wreckage included.</summary>
    private const int TotalBudget = 30;

    /// <summary>The path prefix every explosion clip shares.</summary>
    private const string ExplosionClips = "/Audio/Effects/explosion";

    /// <summary>A hull dropped from the air layer onto laid ground crashes with one bang and few sounds.</summary>
    [Test]
    public async Task OneHullCrashIsOneBang()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var lowAir = layers[1];
        var lowAirMapId = await MapIdOf(pair, lowAir);

        // A landing only counts as a crash over solid ground, and no viewer means no biome tiles.
        await LayTiles(pair, ground, new Vector2i(-4, -4), new Vector2i(20, 20));

        var hull = await BuildHull(pair, lowAirMapId);
        await MapInitHull(pair, hull);

        var seen = new HashSet<EntityUid>();
        var clips = new Dictionary<string, int>();
        var crashed = false;
        var peakAudio = 0;

        // Covers the fall grace and the drop; every other tick is enough, as every clip far outlives two ticks.
        for (var i = 0; i < 450; i++)
        {
            await server.WaitRunTicks(2);
            await server.WaitPost(() =>
            {
                var alive = 0;
                var query = entMan.EntityQueryEnumerator<AudioComponent>();
                while (query.MoveNext(out var uid, out var audio))
                {
                    alive++;
                    if (seen.Add(uid))
                        clips[audio.FileName] = clips.GetValueOrDefault(audio.FileName) + 1;
                }

                peakAudio = Math.Max(peakAudio, alive);
                if (entMan.GetComponent<TransformComponent>(hull).MapUid == ground)
                    crashed = true;
            });
        }

        var bangs = 0;
        foreach (var (clip, count) in clips)
        {
            if (clip.StartsWith(ExplosionClips))
                bangs += count;
        }

        var breakdown = string.Join(", ", clips.Select(clip => $"{clip.Key} x{clip.Value}"));

        TestContext.Out.WriteLine($"Crash audio: {bangs} explosion clips, {seen.Count} audio entities. {breakdown}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(crashed, Is.True,
                    "The hull never reached the ground layer, so no crash was measured at all.");
                // A free fall lands above the hard-landing threshold, so this is the crash path.
                Assert.That(clips.Where(c => c.Key.StartsWith("/Audio/_WF/Planets/Flight/crash")).Sum(c => c.Value), Is.EqualTo(1),
                    "Structural breakup must play one impact sound, not a ship-wide explosion.");
                Assert.That(bangs, Is.LessThanOrEqualTo(ExplosionBudget),
                    $"One hull crash played {bangs} explosion clips. Every hull tile queues its own crater and each " +
                    $"blast that sounds costs two OpenAL sources on every client that can hear it, which is what killed " +
                    $"the live client. Everything heard: {breakdown}");
                // Concurrent sources exhaust the client; successive ambient loops do not.
                Assert.That(peakAudio, Is.LessThanOrEqualTo(TotalBudget),
                    $"One hull crash peaked at {peakAudio} concurrent audio entities ({seen.Count} over the whole test). Everything heard: {breakdown}");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The map id of a z-layer, for the spawners that want one.</summary>
    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }
}
