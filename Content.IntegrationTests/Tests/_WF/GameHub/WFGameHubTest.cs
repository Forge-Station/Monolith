#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Client._WF.GameHub;
using Content.Server._WF.GameHub;
using Content.Server.CartridgeLoader;
using Content.Server.Database;
using Content.Shared._WF.GameHub;
using Content.Shared.CartridgeLoader;
using Content.Shared.Construction.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;
using ServerHub = Content.Server._WF.GameHub.WFGameHubSystem;
using ClientHub = Content.Client._WF.GameHub.WFGameHubSystem;

namespace Content.IntegrationTests.Tests._WF.GameHub;

/// <summary>
/// Game data downloads, nearby sound, scores, prizes and the PDA program.
/// </summary>
[TestOf(typeof(ServerHub))]
public sealed class WFGameHubTest
{
    private const string Game = ClientHub.DoomGame;
    private const string Cabinet = "WFDoomArcade";
    private const string DataPath = "/GameData/_WF/DoomArcade/freedoom1.wad";
    private const string LowPrize = "PrizeTicket10";
    private const string HighPrize = "PrizeTicket30";

    [Test]
    public void ScoreRewardsCompletionAndSpeed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerHub.ScoreLevel(100, 100, 100, 0), Is.EqualTo(2800));
            Assert.That(ServerHub.ScoreLevel(0, 0, 0, 1000), Is.EqualTo(1000));
            Assert.That(ServerHub.ScoreLevel(5000, 5000, 5000, -50), Is.EqualTo(2800), "Reported values must be clamped");
        });
    }

    [Test]
    public async Task DataDownloadsToClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var data = client.ResolveDependency<WFGameDataManager>();

        // Integration clients have no transfer channel, so the stream is built here from what the server would send.
        await client.WaitPost(() => client.System<ClientHub>().RequestManifest(Game));
        await RunUntil(pair, () => data.GetStatus(Game) != WFGameDataStatus.Unknown, 300);
        Assert.That(data.GetStatus(Game), Is.EqualTo(WFGameDataStatus.Missing), "A new client has no game data");

        var wad = new MemoryStream();
        await using (var file = server.ResolveDependency<IResourceManager>().ContentFileRead(new ResPath(DataPath)))
        {
            await file.CopyToAsync(wad);
        }

        var header = WFGameDownloadSystem.EncodeHeader(Game, wad.Length, "testversion");
        var stream = new MemoryStream();
        stream.Write(header);
        stream.Write(wad.ToArray());
        stream.Position = 0;

        await client.WaitPost(() => data.BeginDownload(Game));
        await data.Receive(stream);
        await RunUntil(pair, () => data.GetStatus(Game) is WFGameDataStatus.Ready or WFGameDataStatus.Failed, 300);

        await client.WaitAssertion(() =>
        {
            Assert.That(data.GetStatus(Game), Is.EqualTo(WFGameDataStatus.Ready));

            var (received, total) = data.GetProgress(Game);
            Assert.That(received, Is.EqualTo(total));

            using var file = data.OpenData(Game);
            Assert.That(file.Length, Is.EqualTo(total));

            var head = new byte[4];
            file.ReadExactly(head);
            Assert.That(System.Text.Encoding.ASCII.GetString(head), Is.EqualTo("IWAD"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A manifest that finishes after its client left is dropped; sending it would throw on the game thread.</summary>
    [Test]
    public async Task ManifestForDisconnectedClientIsDropped()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        await client.WaitPost(() => client.System<ClientHub>().RequestManifest(Game));
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.PlayerMan.Sessions.Single().Channel.Disconnect("Left during the manifest"));

        // Long enough for the hash to finish and post back to the game thread.
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(100);
            await server.WaitRunTicks(1);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FinishedLevelsPayPrizesAndReachLeaderboard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var hub = server.System<ServerHub>();
        var db = server.ResolveDependency<IServerDbManager>();

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFGameHubCVars.MinLevelSeconds, 0);
            server.CfgMan.SetCVar(WFGameHubCVars.MaxPrizesPerRound, 2);

            var session = server.PlayerMan.Sessions.Single();
            var player = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, player);
            var cabinet = server.EntMan.SpawnEntity(Cabinet, map.GridCoords);
            var source = server.EntMan.GetNetEntity(cabinet);

            WFGameLevelCompleteEvent Report(int level, int percent)
            {
                return new WFGameLevelCompleteEvent(source, Game, level, percent, percent, percent, 100);
            }

            Assert.Multiple(() =>
            {
                Assert.That(hub.CompleteLevel(Report(99, 100), session), Is.False, "An unreal level must be refused");
                Assert.That(CountSpawned(server, LowPrize, HighPrize), Is.Zero);

                Assert.That(hub.CompleteLevel(Report(11, 0), session));
                Assert.That(CountSpawned(server, LowPrize), Is.EqualTo(1), "A weak clear pays the low prize");

                Assert.That(hub.CompleteLevel(Report(11, 100), session));
                Assert.That(CountSpawned(server, LowPrize, HighPrize), Is.EqualTo(1), "A level pays once per round");

                Assert.That(hub.CompleteLevel(Report(12, 100), session));
                Assert.That(CountSpawned(server, HighPrize), Is.EqualTo(1), "A strong clear pays the high prize");

                Assert.That(hub.CompleteLevel(Report(13, 100), session));
                Assert.That(CountSpawned(server, LowPrize, HighPrize), Is.EqualTo(2), "The round's prize cap holds");

                var otherGame = new WFGameLevelCompleteEvent(source, ClientHub.Doom2Game, 42, 100, 100, 100, 100);
                Assert.That(hub.CompleteLevel(otherGame, session), "Phase 2's last map is a real level");
                Assert.That(CountSpawned(server, LowPrize, HighPrize), Is.EqualTo(2), "A cabinet pays only for its own game");
            });

            server.EntMan.DeleteEntity(cabinet);
            Assert.That(hub.CompleteLevel(Report(14, 100), session), Is.False, "A report needs a real source");
        });

        await pair.RunTicksSync(5);

        // Saves run in the background.
        List<WFGameLeaderboardEntry> board = new();
        for (var i = 0; i < 100; i++)
        {
            board = await db.GetGameLeaderboardAsync(Game, 10);
            if (board.Count == 1 && board[0].Levels == 3 && board[0].Score == 3 * 2700)
                break;

            await Task.Delay(50);
        }

        Assert.That(board, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(board[0].Levels, Is.EqualTo(3));
            Assert.That(board[0].Score, Is.EqualTo(3 * 2700), "Each level keeps its best score");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NearbyPlayersHearTheGame()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        EntityUid cabinet = default;

        await server.WaitPost(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            cabinet = server.EntMan.SpawnEntity(Cabinet, map.GridCoords);

            // No player: this client is the only one connected, and it ignores broadcasts of its own game.
            var broadcast = server.EntMan.EnsureComponent<WFGameBroadcastComponent>(cabinet);
            broadcast.Game = Game;
            broadcast.Track = "D_E1M1";
            broadcast.Loop = true;
        });

        await pair.RunTicksSync(10);

        var clientCabinet = client.EntMan.GetEntity(server.EntMan.GetNetEntity(cabinet));

        var hub = client.System<ClientHub>();
        await RunUntil(pair, () => hub.NearbyTrackCount == 1, 300);

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(hub.NearbyTrackCount, Is.EqualTo(1), "Nearby music should start");
                Assert.That(hub.PlayNearbyNoise(clientCabinet, Game, "DSPISTOL", 200), "A nearby sound should play");
                Assert.That(hub.PlayNearbyNoise(clientCabinet, Game, "DSNOPE", 200), Is.False, "Unknown sounds are ignored");
            });
        });

        await server.WaitPost(() => server.EntMan.RemoveComponent<WFGameBroadcastComponent>(cabinet));
        await RunUntil(pair, () => hub.NearbyTrackCount == 0, 300);
        Assert.That(hub.NearbyTrackCount, Is.Zero, "Nearby music should stop with the game");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NearbyGamesShareTheVolume()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClientHub.GetNoiseVolume(255, 6), Is.LessThan(ClientHub.GetNoiseVolume(255, 1)),
                "More games at once should each be quieter.");
            Assert.That(ClientHub.GetNoiseVolume(255, 0), Is.EqualTo(ClientHub.GetNoiseVolume(255, 1)));
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task ARowOfCabinetsStaysWithinTheLimits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        EntityUid far = default, near = default;

        await server.WaitPost(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);

            far = server.EntMan.SpawnEntity(Cabinet, map.GridCoords.Offset(new System.Numerics.Vector2(8, 0)));
            near = server.EntMan.SpawnEntity(Cabinet, map.GridCoords.Offset(new System.Numerics.Vector2(1, 0)));

            foreach (var cabinet in new[] { far, near })
            {
                var broadcast = server.EntMan.EnsureComponent<WFGameBroadcastComponent>(cabinet);
                broadcast.Game = Game;
                broadcast.Track = "D_E1M1";
                broadcast.Loop = true;
            }
        });

        await pair.RunTicksSync(10);

        var hub = client.System<ClientHub>();
        var clientNear = client.EntMan.GetEntity(server.EntMan.GetNetEntity(near));
        var clientFar = client.EntMan.GetEntity(server.EntMan.GetNetEntity(far));

        await RunUntil(pair, () => hub.NearbyTrackSource == clientNear, 300);

        await client.WaitAssertion(() =>
        {
            Assert.That(hub.NearbyTrackSource, Is.EqualTo(clientNear), "The nearest game's music should play.");
            Assert.That(hub.NearbyTrackCount, Is.EqualTo(1), "Only one game's music should play.");

            var played = 0;
            for (var i = 0; i < ClientHub.MaxNearbyNoises * 2; i++)
            {
                if (hub.PlayNearbyNoise(clientNear, Game, "DSPISTOL", 200))
                    played++;
            }

            Assert.That(played, Is.InRange(1, ClientHub.MaxNearbyNoises), "Nearby sounds should stop at the cap.");
            Assert.That(hub.PlayNearbyNoise(clientNear, Game, "DSPISTOL", 200), Is.False, "A full pool refuses new sounds.");
        });

        // The near game ending hands the music to the other one.
        await server.WaitPost(() => server.EntMan.RemoveComponent<WFGameBroadcastComponent>(near));
        await RunUntil(pair, () => hub.NearbyTrackSource == clientFar, 300);
        Assert.That(hub.NearbyTrackSource, Is.EqualTo(clientFar), "Music should move to the game still playing.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PdasGetTheGamehub()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pda = server.EntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var loaders = server.System<CartridgeLoaderSystem>();
            var programs = loaders.GetInstalled(pda).ToList();

            Assert.That(programs.Any(program =>
                server.EntMan.GetComponent<MetaDataComponent>(program).EntityPrototype?.ID == "WFGameHubCartridge"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WolfgateSellsTheCabinet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var inventory = server.ProtoMan.Index<VendingMachineInventoryPrototype>("WFWolfgateVendInventory");
            Assert.That(inventory.StartingInventory, Does.ContainKey("WFDoomArcadeFlatpack"));

            var flatpack = server.EntMan.SpawnEntity("WFDoomArcadeFlatpack", map.GridCoords);
            var entity = server.EntMan.GetComponent<FlatpackComponent>(flatpack).Entity;
            Assert.That(entity?.Id, Is.EqualTo(Cabinet));

            Assert.That(inventory.StartingInventory, Does.ContainKey("WFFreedoom2ArcadeFlatpack"));
            var phase2Pack = server.EntMan.SpawnEntity("WFFreedoom2ArcadeFlatpack", map.GridCoords);
            var phase2 = server.EntMan.GetComponent<FlatpackComponent>(phase2Pack).Entity;
            Assert.That(phase2?.Id, Is.EqualTo("WFFreedoom2Arcade"));

            var cabinet = server.EntMan.SpawnEntity("WFFreedoom2Arcade", map.GridCoords);
            Assert.That(server.EntMan.GetComponent<WFGameCabinetComponent>(cabinet).Game, Is.EqualTo(ClientHub.Doom2Game));
        });

        await pair.CleanReturnAsync();
    }

    private static int CountSpawned(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, params string[] ids)
    {
        var count = 0;
        var query = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var meta))
        {
            if (ids.Contains(meta.EntityPrototype?.ID))
                count++;
        }

        return count;
    }

    private static async Task RunUntil(Pair.TestPair pair, Func<bool> done, int steps)
    {
        for (var i = 0; i < steps && !done(); i++)
        {
            await pair.RunTicksSync(1);
            await Task.Delay(10);
        }
    }
}
