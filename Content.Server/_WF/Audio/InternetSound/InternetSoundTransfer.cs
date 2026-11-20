using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>A track's outgoing data, released immediately on Stop even if a client is stalled.</summary>
internal sealed class InternetSoundTransfer : IDisposable
{
    private byte[]? _payload;
    private readonly CancellationTokenSource _cancel = new();
    private int _disposed;
    public CancellationToken Token { get; }

    // Keep the token valid for senders already waiting when the owner disposes the source.
    public InternetSoundTransfer(byte[] payload)
    {
        _payload = payload;
        Token = _cancel.Token;
    }

    public int CopyChunk(int offset, byte[] destination)
    {
        var data = Volatile.Read(ref _payload);
        if (data == null)
            return 0;
        var count = Math.Min(destination.Length, data.Length - offset);
        data.AsSpan(offset, count).CopyTo(destination);
        return count;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Interlocked.Exchange(ref _payload, null);
        _cancel.Cancel();
        _cancel.Dispose();
    }
}

/// <summary>
/// Only one stream per client may be writing or finishing at a time. The engine may ignore cancellation
/// inside a socket wait, so retain only a small copied chunk there, never an entire canceled payload.
/// </summary>
internal sealed class InternetSoundTransferQueue
{
    internal const int ChunkSize = 8192;
    private readonly ConditionalWeakTable<object, SemaphoreSlim> _clients = new();

    public async Task SendAsync(object client, InternetSoundTransfer transfer, Func<Task<Stream>> open)
    {
        var gate = _clients.GetValue(client, _ => new SemaphoreSlim(1));
        var cancel = transfer.Token;
        await gate.WaitAsync(cancel);
        try
        {
            cancel.ThrowIfCancellationRequested();
            await using var stream = await open();
            var chunk = new byte[ChunkSize];
            var offset = 0;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var count = transfer.CopyChunk(offset, chunk);
                if (count == 0)
                    break;
                await stream.WriteAsync(chunk.AsMemory(0, count), cancel);
                offset += count;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
