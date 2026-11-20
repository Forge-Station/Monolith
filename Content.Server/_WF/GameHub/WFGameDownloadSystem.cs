using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WF.GameHub;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.GameHub;

/// <summary>Streams a game's data to the players who ask for it, at a capped rate.</summary>
public sealed partial class WFGameDownloadSystem : EntitySystem
{
    private const int ChunkSize = 8192;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private ITransferManager _transfer = default!;

    private readonly Dictionary<string, Task<(long Size, string Version)>> _manifests = new();
    private readonly HashSet<NetUserId> _active = new();
    private SemaphoreSlim _slots = default!;

    public override void Initialize()
    {
        base.Initialize();

        _transfer.RegisterTransferMessage(WFGameHubProtocol.TransferKey);
        _slots = new SemaphoreSlim(Math.Max(1, _cfg.GetCVar(WFGameHubCVars.MaxDownloads)));

        SubscribeNetworkEvent<WFGameManifestRequestEvent>(OnManifestRequest);
        SubscribeNetworkEvent<WFGameDownloadRequestEvent>(OnDownloadRequest);
    }

    private async void OnManifestRequest(WFGameManifestRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_protos.TryIndex<WFGamePrototype>(ev.Game, out var game))
            return;

        var channel = args.SenderSession.Channel;
        try
        {
            var (size, version) = await GetManifest(game);
            SendIfConnected(new WFGameManifestEvent(game.ID, size, version), channel);
        }
        catch (Exception e)
        {
            Log.Error($"Can't read the data for game {game.ID}: {e}");
            SendIfConnected(new WFGameDownloadFailedEvent(game.ID), channel);
        }
    }

    // The player may have left while the worker read the file.
    private void SendIfConnected(EntityEventArgs message, INetChannel channel)
    {
        _task.RunOnMainThread(() =>
        {
            if (channel.IsConnected)
                RaiseNetworkEvent(message, channel);
        });
    }

    private void OnDownloadRequest(WFGameDownloadRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_protos.TryIndex<WFGamePrototype>(ev.Game, out var game) || !_active.Add(args.SenderSession.UserId))
            return;

        Send(args.SenderSession, game);
    }

    private Task<(long Size, string Version)> GetManifest(WFGamePrototype game)
    {
        if (!_manifests.TryGetValue(game.ID, out var manifest))
        {
            var path = game.DataPath;
            manifest = Task.Run(() =>
            {
                using var file = _resources.ContentFileRead(path);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long size = 0;
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                return (size, Convert.ToHexString(hash.GetHashAndReset()));
            });
            _manifests[game.ID] = manifest;
        }

        return manifest;
    }

    private async void Send(ICommonSession session, WFGamePrototype game)
    {
        var channel = session.Channel;
        var acquired = false;
        try
        {
            var (size, version) = await GetManifest(game);

            await _slots.WaitAsync();
            acquired = true;

            await using var stream = await OpenTransfer(channel);
            await using var file = _resources.ContentFileRead(game.DataPath);

            await stream.WriteAsync(EncodeHeader(game.ID, size, version));

            var bytesPerSecond = Math.Max(1, _cfg.GetCVar(WFGameHubCVars.DownloadRate)) * 1024L;
            var chunk = new byte[ChunkSize];
            var timer = Stopwatch.StartNew();
            long sent = 0;
            int read;
            while ((read = await file.ReadAsync(chunk)) > 0)
            {
                await stream.WriteAsync(chunk.AsMemory(0, read));
                sent += read;

                var ahead = sent * 1000 / bytesPerSecond - timer.ElapsedMilliseconds;
                if (ahead > 0)
                    await Task.Delay((int) ahead);
            }
        }
        catch (Exception e)
        {
            // Usually a client that disconnected mid-download.
            Log.Warning($"Game download to {session.Name} failed: {e.Message}");
            _task.RunOnMainThread(() =>
            {
                if (channel.IsConnected)
                    RaiseNetworkEvent(new WFGameDownloadFailedEvent(game.ID), channel);
            });
        }
        finally
        {
            if (acquired)
                _slots.Release();

            _task.RunOnMainThread(() => _active.Remove(session.UserId));
        }
    }

    private Task<Stream> OpenTransfer(INetChannel channel)
    {
        // This can resume on a worker thread, and connection state belongs to the game thread.
        var completion = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        _task.RunOnMainThread(() =>
        {
            try
            {
                completion.SetResult(_transfer.StartTransfer(channel, WFGameHubProtocol.TransferKey));
            }
            catch (Exception e)
            {
                completion.SetException(e);
            }
        });
        return completion.Task;
    }

    /// <summary>The bytes that come before a game's data on its transfer stream.</summary>
    public static byte[] EncodeHeader(string game, long size, string version)
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body))
        {
            writer.Write(game);
            writer.Write(size);
            writer.Write(version);
        }

        var header = body.ToArray();
        var bytes = new byte[header.Length + 4];
        BitConverter.TryWriteBytes(bytes, header.Length);
        header.CopyTo(bytes, 4);
        return bytes;
    }
}
