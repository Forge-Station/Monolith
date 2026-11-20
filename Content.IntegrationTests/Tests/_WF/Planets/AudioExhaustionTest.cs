using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Content.Client._WF.Audio;
using Content.IntegrationTests.Pair;
using Content.Shared.Damage.Systems;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class AudioExhaustionTest
{
    [Test]
    public async Task NetworkSoundBurstIsBoundedBeforeAllocation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var streams = new List<EntityUid>();
        await client.WaitPost(() => client.ResolveDependency<IConfigurationManager>()
            .SetCVar(CVars.AudioDefaultConcurrent, 1024));
        await server.WaitPost(() =>
        {
            var audio = server.System<SharedAudioSystem>();
            for (var i = 0; i < 400; i++)
            {
                var stream = audio.PlayGlobal(new SoundPathSpecifier("/Audio/Effects/weak_hit2.ogg"),
                    Filter.Broadcast(), false, AudioParams.Default.WithLoop(true));
                streams.Add(stream!.Value.Entity);
            }
        });
        await pair.RunSeconds(2);
        await client.WaitAssertion(() =>
        {
            var budget = client.System<WFAudioBudgetSystem>();
            Assert.That(budget.Suppressed, Is.GreaterThanOrEqualTo(304));
            Assert.That(budget.Reserved, Is.LessThanOrEqualTo(WFAudioBudgetSystem.MaxStreams));
            var count = 0;
            var query = client.EntMan.EntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out _))
                count++;
            Assert.That(count, Is.GreaterThanOrEqualTo(400), "Excess sounds must remain valid network entities.");
            var engineAudio = client.System<Robust.Client.Audio.AudioSystem>();
            var counts = (Dictionary<string, int>) typeof(Robust.Client.Audio.AudioSystem)
                .GetField("_playingCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engineAudio)!;
            Assert.That(counts.Values.Sum(), Is.InRange(1, WFAudioBudgetSystem.MaxStreams),
                "Check actual engine source admissions, not just the content budget's bookkeeping.");
        });
        await server.WaitPost(() =>
        {
            foreach (var stream in streams)
                server.EntMan.DeleteEntity(stream);
        });
        await pair.RunSeconds(1);
        // The reservation delay uses real time, matching deferred backend disposal rather than simulation ticks.
        await Task.Delay(350);
        var before = 0;
        await client.WaitPost(() => before = client.System<WFAudioBudgetSystem>().Suppressed);
        await server.WaitPost(() =>
        {
            streams.Clear();
            streams.Add(server.System<SharedAudioSystem>().PlayGlobal(
                new SoundPathSpecifier("/Audio/Effects/weak_hit2.ogg"), Filter.Broadcast(), false,
                AudioParams.Default.WithLoop(true))!.Value.Entity);
        });
        await pair.RunSeconds(1);
        await client.WaitAssertion(() =>
            Assert.That(client.System<WFAudioBudgetSystem>().Suppressed, Is.EqualTo(before),
                "A fresh sound must be admitted after the burst is cleaned up."));
        await server.WaitPost(() => server.EntMan.DeleteEntity(streams[0]));
        await pair.RunSeconds(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ImpactBudgetDoesNotRefillEveryTick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var system = server.System<DamageOnHighSpeedImpactSystem>();
        var allow = typeof(DamageOnHighSpeedImpactSystem).GetMethod("WfImpactSoundAllowed",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var map = await pair.CreateTestMap();

        // The budget is counted where the body is: the grid here, and the map itself for a body off every grid.
        var onGrid = new object[] { map.Grid.Owner };
        var offGrid = new object[] { map.MapUid };
        var admitted = 0;
        for (var tick = 0; tick < 10; tick++)
        {
            await server.WaitPost(() =>
            {
                for (var hit = 0; hit < 30; hit++)
                    if ((bool) allow.Invoke(system, onGrid)!)
                        admitted++;
            });
            await server.WaitRunTicks(1);
        }
        Assert.That(admitted, Is.LessThanOrEqualTo(6), "A sustained collision burst must not refill its sound budget each tick.");
        await server.WaitAssertion(() => Assert.That((bool) allow.Invoke(system, offGrid)!, Is.True,
            "A burst on one grid used up the impact sounds of everywhere else."));
        await server.WaitRunTicks(pair.SecondsToTicks(1.1f));
        await server.WaitAssertion(() => Assert.That((bool) allow.Invoke(system, onGrid)!, Is.True));
        await pair.CleanReturnAsync();
    }
}
