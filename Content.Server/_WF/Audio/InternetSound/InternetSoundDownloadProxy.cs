using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>
/// Per-download SOCKS5 tunnel. Every connection (including redirects and media fragments) is
/// resolved here and connected to a checked IP, never resolved a second time by the socket.
/// Only native HTTPS yt-dlp downloaders may use it; FFmpeg is restricted to local protocols.
/// </summary>
internal sealed class InternetSoundDownloadProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop;
    private readonly Task _accept;
    private readonly List<Task> _connections = new();
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, int, CancellationToken, Task<Stream>> _connect;
    private readonly long _maxDownloadBytes;
    private readonly Action? _onDownloadLimitExceeded;
    private string? _deniedHost;
    private int _rejectedInsecureTransport;
    private long _downloadBytes;
    private int _downloadLimitExceeded;

    public string Url { get; }
    public string? DeniedHost => Volatile.Read(ref _deniedHost);
    public bool RejectedInsecureTransport => Volatile.Read(ref _rejectedInsecureTransport) != 0;
    public bool DownloadLimitExceeded => Volatile.Read(ref _downloadLimitExceeded) != 0;

    internal InternetSoundDownloadProxy(CancellationToken cancel,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, int, CancellationToken, Task<Stream>>? connect = null,
        long maxDownloadBytes = 64L * 1024 * 1024,
        Action? onDownloadLimitExceeded = null)
    {
        if (maxDownloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDownloadBytes));
        _resolve = resolve ?? ((host, token) => Dns.GetHostAddressesAsync(host, token));
        _connect = connect ?? ConnectSocket;
        _maxDownloadBytes = maxDownloadBytes;
        _onDownloadLimitExceeded = onDownloadLimitExceeded;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        _listener.Start();
        Url = $"socks5://127.0.0.1:{((IPEndPoint) _listener.LocalEndpoint).Port}";
        _accept = Accept();
    }

    private async Task Accept()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.RemoveAll(t => t.IsCompleted);
                if (_connections.Count >= 32)
                {
                    client.Dispose();
                    continue;
                }
                _connections.Add(Serve(client));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Cancellation closes both the listener and all tunnels.
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            var token = lifetime.Token;
            var downstream = client.GetStream();
            try
            {
                var greeting = await Read(downstream, 2, token);
                if (greeting[0] != 5 || greeting[1] == 0)
                    return;
                var methods = await Read(downstream, greeting[1], token);
                if (!methods.Contains((byte) 0))
                {
                    await downstream.WriteAsync(new byte[] { 5, 255 }, token);
                    return;
                }
                await downstream.WriteAsync(new byte[] { 5, 0 }, token);
                var request = await Read(downstream, 4, token);
                if (request[0] != 5 || request[1] != 1 || request[2] != 0)
                {
                    await Reply(downstream, 7, token);
                    return;
                }
                string host;
                switch (request[3])
                {
                    case 1:
                        host = new IPAddress(await Read(downstream, 4, token)).ToString();
                        break;
                    case 4:
                        host = new IPAddress(await Read(downstream, 16, token)).ToString();
                        break;
                    case 3:
                        var length = (await Read(downstream, 1, token))[0];
                        if (length == 0)
                            return;
                        host = Encoding.ASCII.GetString(await Read(downstream, length, token));
                        break;
                    default:
                        await Reply(downstream, 8, token);
                        return;
                }
                var portBytes = await Read(downstream, 2, token);
                var port = (portBytes[0] << 8) | portBytes[1];
                if (port != 443)
                {
                    Interlocked.Exchange(ref _rejectedInsecureTransport, 1);
                    await Reply(downstream, 2, token);
                    return;
                }
                var addresses = IPAddress.TryParse(host, out var literal)
                    ? new[] { literal }
                    : await _resolve(host, token);
                if (addresses.Length == 0 || addresses.Any(IsNonPublic))
                {
                    Interlocked.CompareExchange(ref _deniedHost, host, null);
                    await Reply(downstream, 2, token);
                    return;
                }
                await Reply(downstream, 0, token);
                // Port 443 alone does not imply HTTPS: redirects can use http://host:443.
                // yt-dlp must begin TLS before any request bytes reach the remote destination.
                // Pass TLS through unchanged; yt-dlp still authenticates the server certificate.
                var hello = await Read(downstream, 6, token);
                if (hello[0] != 22 || hello[1] != 3 || hello[2] is < 1 or > 3
                    || ((hello[3] << 8) | hello[4]) < 4 || hello[5] != 1)
                {
                    Interlocked.Exchange(ref _rejectedInsecureTransport, 1);
                    return;
                }
                using var upstream = await ConnectChecked(addresses, port, token);
                await upstream.WriteAsync(hello, token);
                var sending = downstream.CopyToAsync(upstream, token);
                var receiving = CopyIncomingAsync(upstream, downstream, token);
                await Task.WhenAny(sending, receiving);
                // A failed/closed direction cannot leave the other waiting until the whole fetch times out.
                await lifetime.CancelAsync();
                await Task.WhenAll(sending, receiving);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException
                                      or ObjectDisposedException or ArgumentException)
            {
                // Invalid requests and failed tunnels are closed; never fall back to an unchecked route.
            }
        }
    }

    private async Task CopyIncomingAsync(Stream upstream, Stream downstream, CancellationToken token)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = await upstream.ReadAsync(buffer, token);
            if (read == 0)
                return;

            var reserved = Reserve(read);
            if (!reserved)
            {
                Interlocked.Exchange(ref _downloadLimitExceeded, 1);
                _onDownloadLimitExceeded?.Invoke();
                await _stop.CancelAsync();
                return;
            }

            await downstream.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private bool Reserve(int count)
    {
        while (true)
        {
            var current = Volatile.Read(ref _downloadBytes);
            if (current > _maxDownloadBytes || count > _maxDownloadBytes - current)
                return false;
            if (Interlocked.CompareExchange(ref _downloadBytes, current + count, current) == current)
                return true;
        }
    }

    private async Task<Stream> ConnectChecked(IPAddress[] addresses, int port, CancellationToken token)
    {
        foreach (var address in addresses)
        {
            try
            {
                return await _connect(address, port, token);
            }
            catch (SocketException)
            {
                // Try another already-validated address from this resolution, without another DNS lookup.
            }
        }
        throw new IOException("No public destination could be reached.");
    }

    private static async Task<Stream> ConnectSocket(IPAddress address, int port, CancellationToken token)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> Read(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, token);
        return buffer;
    }

    private static ValueTask Reply(Stream stream, byte status, CancellationToken token) =>
        stream.WriteAsync(new byte[] { 5, status, 0, 1, 0, 0, 0, 0, 0, 0 }, token);

    internal static bool IsNonPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return true;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Only global unicast, excluding special-purpose space, documentation and 6to4.
            // This also excludes NAT64/IPv4 translation routes into private networks.
            return (b[0] & 0xe0) != 0x20 || address.ScopeId != 0
                   || b[0] == 0x20 && b[1] == 0x02
                   || b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8)
                   || b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0;
        }
        return b[0] is 0 or 10 or 127 || b[0] >= 224
               || b[0] == 100 && b[1] is >= 64 and <= 127
               || b[0] == 169 && b[1] == 254
               || b[0] == 172 && b[1] is >= 16 and <= 31
               || b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2)
               || b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)
               || b[0] == 203 && b[1] == 0 && b[2] == 113
               || address.Equals(IPAddress.Parse("168.63.129.16")); // Azure host services.
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accept;
        await Task.WhenAll(_connections);
        _stop.Dispose();
    }
}
