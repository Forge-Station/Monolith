using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._WF.Audio.InternetSound;

/// <summary>
/// Fetches audio from a link with yt-dlp and converts it to Ogg Vorbis with ffmpeg. Runs off the main thread.
/// Ogg because it's what the engine's audio loader reads, which is what lets the result be mounted as an
/// ordinary resource and played through speakers like any other sound.
/// </summary>
public static class InternetSoundDownloader
{
    /// <summary>Tool paths and the limits and output format a fetch runs with.</summary>
    public sealed record Settings(
        string YtDlpPath,
        string FfmpegPath,
        int MaxDurationSeconds,
        int TimeoutSeconds,
        int MaxSizeMb,
        int SampleRate,
        int Channels,
        int BitrateKbps,
        long MaxDownloadBytes = 64L * 1024 * 1024);

    /// <summary>A finished fetch: the source's title and the Ogg Vorbis bytes.</summary>
    public sealed record Result(string Title, byte[] Audio);

    /// <summary>
    /// A fetch failure with a loc key; <see cref="Exception.Message"/> is passed to it as "detail".
    /// </summary>
    public sealed class FetchException(string locKey, string detail = "") : Exception(detail)
    {
        public readonly string LocKey = locKey;
    }

    private const string TitlePrefix = "WFTITLE ";
    private const string PathPrefix = "WFPATH ";

    /// <summary>
    /// Downloads and converts one link. Throws <see cref="FetchException"/> on failure and
    /// <see cref="OperationCanceledException"/> on cancel or timeout.
    /// </summary>
    public static async Task<Result> Fetch(string url, Settings settings, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443)
            throw new FetchException("wf-internet-sound-error-https");
        using var downloadCancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using var proxy = new InternetSoundDownloadProxy(downloadCancel.Token,
            maxDownloadBytes: settings.MaxDownloadBytes, onDownloadLimitExceeded: downloadCancel.Cancel);

        var dir = Path.Combine(Path.GetTempPath(), "wolfgate-internet-sound", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // HLS can otherwise fall back to FFmpeg even with --downloader native. An empty
            // tool directory makes that fallback fail closed; only our local-only conversion may use it.
            var nativeTools = Path.Combine(dir, "native-only");
            Directory.CreateDirectory(nativeTools);
            // "--" stops the link being read as an option; the filter refuses livestreams and long videos up front.
            (int ExitCode, string Output, string Error) download;
            try
            {
                download = await Run(settings.YtDlpPath,
                    new[]
                    {
                        "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir",
                        "--proxy", proxy.Url,
                        "--downloader", "native", "--fixup", "never",
                        "--ffmpeg-location", nativeTools,
                        "--no-playlist", "--no-warnings", "--no-progress", "--no-simulate",
                        "--match-filter", $"!is_live & duration <=? {settings.MaxDurationSeconds}",
                        // Manifests and fragments must also pass the proxy's HTTPS gate.
                        "-f", "bestaudio[protocol=https]/bestaudio[protocol=http_dash_segments]/bestaudio[protocol=m3u8_native]/best[protocol=https]/best[protocol=http_dash_segments]/best[protocol=m3u8_native]",
                        "-o", Path.Combine(dir, "source.%(ext)s"),
                        "--print", $"before_dl:{TitlePrefix}%(title)s",
                        "--print", $"after_move:{PathPrefix}%(filepath)s",
                        "--", url,
                    },
                    "wf-internet-sound-error-ytdlp-missing",
                    downloadCancel.Token);
            }
            catch (OperationCanceledException) when (proxy.DownloadLimitExceeded)
            {
                throw new FetchException("wf-internet-sound-error-download-too-large",
                    DownloadMegabytes(settings.MaxDownloadBytes).ToString());
            }

            if (proxy.DownloadLimitExceeded)
                throw new FetchException("wf-internet-sound-error-download-too-large",
                    DownloadMegabytes(settings.MaxDownloadBytes).ToString());

            var lines = download.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var title = lines.FirstOrDefault(l => l.StartsWith(TitlePrefix))?[TitlePrefix.Length..] ?? url;
            var source = lines.FirstOrDefault(l => l.StartsWith(PathPrefix))?[PathPrefix.Length..];

            if (proxy.RejectedInsecureTransport)
                throw new FetchException("wf-internet-sound-error-https");
            if (proxy.DeniedHost is { } denied)
                throw new FetchException("wf-internet-sound-error-host", denied);
            if (download.ExitCode != 0)
                throw new FetchException("wf-internet-sound-error-download", LastLine(download.Error));

            // A clean exit with no file means the match filter skipped it.
            if (source == null || !File.Exists(source))
                throw new FetchException("wf-internet-sound-error-rejected", settings.MaxDurationSeconds.ToString());

            var relativeSource = Path.GetRelativePath(dir, Path.GetFullPath(source));
            if (Path.IsPathRooted(relativeSource) || relativeSource == ".."
                || relativeSource.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new FetchException("wf-internet-sound-error-download", "Invalid download path.");

            var output = Path.Combine(dir, "sound.ogg");

            // Trimming rumble and everything near the new Nyquist first stops the encoder spending its very
            // small budget on content a loudspeaker would never reproduce anyway.
            var cutoff = (int) (settings.SampleRate * 0.45f);

            var convert = await Run(settings.FfmpegPath,
                new[]
                {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                    // A downloaded manifest must not make FFmpeg open its own network connections.
                    "-protocol_whitelist", "file,pipe",
                    "-i", source,
                    "-vn", "-map_metadata", "-1",
                    "-af", $"highpass=f=70,lowpass=f={cutoff}",
                    "-ac", settings.Channels.ToString(), "-ar", settings.SampleRate.ToString(),
                    "-c:a", "libvorbis", "-b:a", $"{settings.BitrateKbps}k",
                    "-t", settings.MaxDurationSeconds.ToString(),
                    output,
                },
                "wf-internet-sound-error-ffmpeg-missing",
                timeout.Token);

            if (convert.ExitCode != 0 || !File.Exists(output))
                throw new FetchException("wf-internet-sound-error-transcode", LastLine(convert.Error));

            var maxSizeBytes = checked((long) settings.MaxSizeMb * 1024 * 1024);
            if (new FileInfo(output).Length > maxSizeBytes)
                throw new FetchException("wf-internet-sound-error-too-large",
                    (new FileInfo(output).Length / (1024 * 1024)).ToString());

            var audio = await File.ReadAllBytesAsync(output, timeout.Token);
            if (audio.Length > maxSizeBytes)
                throw new FetchException("wf-internet-sound-error-too-large", (audio.Length / (1024 * 1024)).ToString());

            return new Result(title, audio);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Leftover temp files are harmless; don't let cleanup fail a good fetch.
            }
        }
    }

    /// <summary>One playable entry of a playlist.</summary>
    public sealed record PlaylistEntry(string Url, string Title, int Seconds);

    /// <summary>A playlist read: the playable entries in order, and how many were passed over.</summary>
    public sealed record PlaylistResult(List<PlaylistEntry> Entries, int Skipped);

    private const string EntryPrefix = "WFENTRY ";

    /// <summary>Most playlist entries read when looking for playable ones.</summary>
    public const int MaxPlaylistScan = 50;

    /// <summary>
    /// True for a link that names a playlist rather than one track: a YouTube playlist page, a watch link with
    /// only a list, or a SoundCloud set. A video link that also carries a list plays just the video.
    /// </summary>
    public static bool IsPlaylistLink(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        if (host.EndsWith("youtube.com") || host == "youtu.be")
            return !string.IsNullOrEmpty(query["list"]) && string.IsNullOrEmpty(query["v"]) && !host.EndsWith("youtu.be");

        return host.EndsWith("soundcloud.com") && uri.AbsolutePath.Contains("/sets/");
    }

    /// <summary>
    /// Lists a playlist without downloading it and keeps the first <paramref name="max"/> entries that will play:
    /// public, not live, with a known length within the limit. Everything after is cut off.
    /// </summary>
    public static async Task<PlaylistResult> ExpandPlaylist(string url, Settings settings, int max, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443)
            throw new FetchException("wf-internet-sound-error-https");

        await using var proxy = new InternetSoundDownloadProxy(timeout.Token,
            maxDownloadBytes: settings.MaxDownloadBytes, onDownloadLimitExceeded: timeout.Cancel);

        // Tab separated with the title last, since titles can hold anything but a newline.
        var list = await Run(settings.YtDlpPath,
            new[]
            {
                "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir",
                "--proxy", proxy.Url,
                "--flat-playlist", "--yes-playlist", "--no-warnings", "--ignore-errors",
                "--playlist-end", MaxPlaylistScan.ToString(),
                "--print", $"{EntryPrefix}%(url)s\t%(duration)s\t%(live_status)s\t%(availability)s\t%(title)s",
                "--", url,
            },
            "wf-internet-sound-error-ytdlp-missing",
            timeout.Token);

        if (proxy.RejectedInsecureTransport)
            throw new FetchException("wf-internet-sound-error-https");
        if (proxy.DeniedHost is { } denied)
            throw new FetchException("wf-internet-sound-error-host", denied);

        var entries = new List<PlaylistEntry>();
        var skipped = 0;
        var seen = 0;

        foreach (var line in list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith(EntryPrefix))
                continue;

            seen++;
            if (entries.Count >= max)
                continue;

            if (ParseEntry(line[EntryPrefix.Length..], settings.MaxDurationSeconds) is { } entry)
                entries.Add(entry);
            else
                skipped++;
        }

        if (seen == 0)
            throw new FetchException("wf-internet-sound-error-playlist-empty", LastLine(list.Error));

        return new PlaylistResult(entries, skipped);
    }

    /// <summary>Reads one printed entry, or null if it won't play.</summary>
    public static PlaylistEntry? ParseEntry(string line, int maxSeconds)
    {
        var parts = line.Split('\t', 5);
        if (parts.Length < 5)
            return null;

        var (link, duration, live, availability, title) = (parts[0], parts[1], parts[2], parts[3], parts[4]);

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443)
            return null;

        // Private and deleted videos list without a length; so does anything live.
        if (!double.TryParse(duration, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0 || seconds > maxSeconds)
            return null;

        if (live is "is_live" or "is_upcoming" or "post_live")
            return null;

        if (availability is "private" or "premium_only" or "subscriber_only" or "needs_auth")
            return null;

        if (title is "[Private video]" or "[Deleted video]")
            return null;

        return new PlaylistEntry(uri.AbsoluteUri, title, (int) Math.Ceiling(seconds));
    }

    /// <summary>
    /// Runs a tool with arguments passed directly (no shell), killing it on cancel.
    /// </summary>
    private static async Task<(int ExitCode, string Output, string Error)> Run(string exe, IEnumerable<string> args, string missingKey, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // yt-dlp is Python; without this, non-ASCII titles break on Windows.
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUTF8"] = "1";

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new FetchException(missingKey, exe);
        }
        catch (Win32Exception)
        {
            throw new FetchException(missingKey, exe);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var error = process.StandardError.ReadToEndAsync(cancel);

            try
            {
                await process.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }

                throw;
            }

            return (process.ExitCode, await output, await error);
        }
    }

    private static string LastLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
        return line.Length > 200 ? line[..200] : line;
    }

    private static long DownloadMegabytes(long bytes) => (bytes - 1) / (1024 * 1024) + 1;
}
