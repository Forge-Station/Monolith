using System.Reflection;
using Content.Client._WF.RoundSetup;
using Content.IntegrationTests.Pair;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Shared._WF.RoundSetup;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class WFRoundSetupLobbyTest
{
    /// <summary>An idle lobby sends nothing between changes; an edit still reaches viewers.</summary>
    [Test]
    public async Task IdleLobbySendsNoSnapshots()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var id = OwnLobby(state).Id;
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        // The 30-second safety snapshot runs on whatever clock the pooled server has left, so it could fall in the
        // window; a flush now puts it a full interval away. Its own snapshot lands before counting starts.
        await pair.Server.WaitPost(() => typeof(WFRoundSetupSystem)
            .GetMethod("FlushBroadcast", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(pair.Server.System<WFRoundSetupSystem>(), null));
        await pair.RunTicksSync(10);
        // A late preference load can still swap the host's profile once and queue one snapshot; let it pass.
        await CountSnapshots(pair, 2);
        var count = await CountSnapshots(pair, 7);
        Assert.That(count, Is.Zero, "Nothing changed, so no snapshot should be sent.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure) { LobbyId = id, Title = "Quiet" });
        Assert.That(OwnLobby(state).Title, Is.EqualTo("Quiet"));
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>An in-round viewer outside any crew follows snapshots only while its lease is renewed.</summary>
    [Test]
    public async Task InRoundViewerLeaseLapsesUntilRenewed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var ticker = server.System<GameTicker>();
            ticker.StartRound(force: true);
            ticker.JoinAsObserver(pair.Player!);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1));
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(await CountSnapshots(pair, 32), Is.Positive, "A fresh lease receives the periodic refresh.");
        await pair.RunTicksSync(pair.SecondsToTicks(30));
        Assert.That(await CountSnapshots(pair, 32), Is.Zero, "A lapsed lease must stop live snapshots.");
        await pair.Client.WaitPost(() =>
            pair.Client.System<WFRoundSetupClientSystem>().Send(new WFRoundSetupRequest(WFRoundSetupAction.Watch)));
        Assert.That(await CountSnapshots(pair, 32), Is.Positive, "Renewing the lease resumes live snapshots.");
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>Counts snapshots the client receives over the given simulated time.</summary>
    private static async Task<int> CountSnapshots(TestPair pair, float seconds)
    {
        var system = pair.Client.System<WFRoundSetupClientSystem>();
        var count = 0;
        void Received(WFRoundSetupState _) => count++;
        await pair.Client.WaitPost(() => system.Received += Received);
        await pair.RunTicksSync(pair.SecondsToTicks(seconds));
        await pair.Client.WaitPost(() => system.Received -= Received);
        return count;
    }
}
