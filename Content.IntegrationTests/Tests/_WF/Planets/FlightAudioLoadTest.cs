#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.ShipPa;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>A hull full of air alarms, each a fallback PA speaker, stays in an audio budget falling.</summary>
[TestFixture]
[TestOf(typeof(WFFlightSystem))]
public sealed class FlightAudioLoadTest
{
    /// <summary>Fallback PA speakers bolted to the hull; a mid-sized shipyard vessel carries about this many.</summary>
    private const int AlarmCount = 30;

    /// <summary>Loose items the skid throws about, each of which can make its own noise.</summary>
    private const int LooseItems = 12;

    /// <summary>Most audio entities allowed to exist at once at any point in the flight.</summary>
    private const int PeakBudget = 48;

    /// <summary>Most audio entities allowed to still exist five seconds after the hull is down.</summary>
    private const int SettledBudget = 8;

    /// <summary>A hull covered in air alarms falls and lands within the peak and settled audio budgets.</summary>
    [Test]
    public async Task PopulatedHullFallDoesNotFloodTheAudioPool()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();
        var pa = server.System<ShipPaSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        // Solid tiles under the footprint, so it lands.
        await LayTiles(pair, ground, new Vector2i(-24, -24), new Vector2i(48, 48));

        var hull = await BuildHull(pair, orbitMapId);
        await MapInitHull(pair, hull);

        // Rows 4 and 5 of the hull are clear: thirty tiles.
        await server.WaitPost(() =>
        {
            var placed = 0;

            for (var y = 4; y <= 5; y++)
            for (var x = 0; x < 15 && placed < AlarmCount; x++)
            {
                var uid = entMan.SpawnEntity("AirAlarm", new EntityCoordinates(hull, new Vector2(x + 0.5f, y + 0.5f)));

                // No cabling on a code-built hull.
                receiver.SetNeedsPower(uid, false);

                // Wallmounts spawn anchored; anchoring twice trips the snap grid assert.
                if (!entMan.GetComponent<TransformComponent>(uid).Anchored)
                    transform.AnchorEntity(uid);

                placed++;
            }

            for (var i = 0; i < LooseItems; i++)
            {
                entMan.SpawnEntity("Crowbar", new EntityCoordinates(hull, new Vector2(i + 0.5f, 6.5f)));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var (online, total, fallback) = pa.CountSpeakers(hull);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(total, Is.EqualTo(AlarmCount), "The air alarms did not join the hull's PA network.");
                Assert.That(online, Is.EqualTo(AlarmCount), "The air alarms are on the network but not working.");
                Assert.That(fallback, Is.True, "The air alarms are not standing in as fallback speakers.");
            }
        });

        // Streams an earlier test on this pair left playing are not this flight's.
        var existing = new HashSet<EntityUid>();
        var baseline = 0;
        await server.WaitPost(() => baseline = CountAudio(entMan, null, existing));

        // No settle, so the caution chime inside the call is counted.
        var refusal = await EnterAtmosphere(pair, hull, settle: 0f);
        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        var peak = 0;
        var peakClips = new Dictionary<string, int>();
        var samples = new List<string>();
        var final = new Dictionary<string, int>();
        var landed = false;
        var settle = 0;

        // Ninety seconds covers a full plummet out of orbit.
        for (var second = 0; second < 90; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));

            var clips = new Dictionary<string, int>();

            await server.WaitPost(() =>
            {
                CountAudio(entMan, clips, existing);

                if (!entMan.Deleted(hull) && entMan.GetComponent<TransformComponent>(hull).MapUid == ground)
                    landed = true;
            });

            var total = clips.Values.Sum();
            samples.Add($"t+{second + 1}s: {total}");

            if (total > peak)
            {
                peak = total;
                peakClips = clips;
            }

            if (!landed)
                continue;

            settle++;
            final = clips;

            if (settle >= 5)
                break;
        }

        var peakBreakdown = string.Join(", ", peakClips.OrderByDescending(clip => clip.Value).Select(clip => $"{clip.Key} x{clip.Value}"));
        var finalBreakdown = string.Join(", ", final.OrderByDescending(clip => clip.Value).Select(clip => $"{clip.Key} x{clip.Value}"));
        var finalTotal = final.Values.Sum();

        TestContext.Out.WriteLine($"Flight audio: baseline {baseline}, peak {peak}, final {finalTotal}.");
        TestContext.Out.WriteLine($"Peak: {peakBreakdown}");
        TestContext.Out.WriteLine($"Final: {finalBreakdown}");
        TestContext.Out.WriteLine($"Per second: {string.Join(" | ", samples)}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(landed, Is.True, "The hull never reached the ground, so no landing was measured at all.");
                Assert.That(peak, Is.LessThanOrEqualTo(PeakBudget),
                    $"A hull carrying {AlarmCount} air alarms held {peak} audio streams open at once on the way down. " +
                    $"Every one of those is an OpenAL source on every client aboard. Peak: {peakBreakdown}");
                Assert.That(finalTotal, Is.LessThanOrEqualTo(SettledBudget),
                    $"{finalTotal} audio streams were still alive five seconds after the hull was down, so the flight " +
                    $"leaks them rather than merely making a lot of noise. Still playing: {finalBreakdown}");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Audio entities alive right now, tallied by clip into the given map when there is one.</summary>
    /// <summary>Counts live audio streams; the first call fills <paramref name="existing"/>, later calls skip them.</summary>
    private static int CountAudio(IEntityManager entMan, Dictionary<string, int>? clips, HashSet<EntityUid> existing)
    {
        var count = 0;
        var fill = existing.Count == 0 && clips == null;
        var query = entMan.EntityQueryEnumerator<AudioComponent>();

        while (query.MoveNext(out var uid, out var audio))
        {
            if (fill)
                existing.Add(uid);
            else if (existing.Contains(uid))
                continue;

            count++;

            if (clips != null)
                clips[audio.FileName] = clips.GetValueOrDefault(audio.FileName) + 1;
        }

        return count;
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
