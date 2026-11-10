using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>
/// Settings for admin internet sounds.
/// </summary>
[CVarDefs]
public sealed class InternetSoundCVars
{
    /// <summary>
    /// Whether admins can play internet sounds at all.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.internet_sound.enabled", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether crew can queue a track from a shuttle console, rather than only admins from the admin tab.
    /// Turning this off leaves the admin side working.
    /// </summary>
    public static readonly CVarDef<bool> PlayerRequests =
        CVarDef.Create("wf.internet_sound.player_requests", true, CVar.SERVERONLY);

    /// <summary>
    /// How many tracks may be in play at once across the whole server, ship PA tracks included. PA recipients
    /// retain assets until release, so this bounds both server transfers and client memory: a decoded
    /// seven-minute mono track at the default rate costs about 37 MB.
    /// </summary>
    public static readonly CVarDef<int> MaxConcurrent =
        CVarDef.Create("wf.internet_sound.max_concurrent", 6, CVar.SERVERONLY);

    /// <summary>
    /// Extra tiles beyond a ship's bounds and maximum speaker range in which PA audio is prefetched.
    /// Gives approaching listeners time to download; actual audibility is still determined by speakers.
    /// </summary>
    public static readonly CVarDef<float> PaPrefetchMargin =
        CVarDef.Create("wf.internet_sound.pa_prefetch_margin", 64f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a ship must wait between console requests. Cutting its current track allows an immediate replacement.
    /// </summary>
    public static readonly CVarDef<int> RequestCooldown =
        CVarDef.Create("wf.internet_sound.request_cooldown", 60, CVar.SERVERONLY);

    /// <summary>
    /// yt-dlp executable. A bare name is looked up on PATH.
    /// </summary>
    public static readonly CVarDef<string> YtDlpPath =
        CVarDef.Create("wf.internet_sound.ytdlp_path", "yt-dlp", CVar.SERVERONLY);

    /// <summary>
    /// ffmpeg executable used to convert downloads for sending. A bare name is looked up on PATH.
    /// </summary>
    public static readonly CVarDef<string> FfmpegPath =
        CVarDef.Create("wf.internet_sound.ffmpeg_path", "ffmpeg", CVar.SERVERONLY);

    /// <summary>
    /// Longest sound in seconds. Longer links are refused.
    /// </summary>
    public static readonly CVarDef<int> MaxDuration =
        CVarDef.Create("wf.internet_sound.max_duration", 420, CVar.SERVERONLY);

    /// <summary>
    /// Sample rate the audio is sent at, 8000 to 48000. This is the setting that matters for client memory:
    /// the engine decodes to 16-bit PCM up front, so a track costs rate * channels * 2 bytes a second while
    /// it plays, regardless of how small the Ogg was. 22050 was enough for a loudspeaker but sounded rough
    /// on the radio.
    /// </summary>
    public static readonly CVarDef<int> SampleRate =
        CVarDef.Create("wf.internet_sound.sample_rate", 44100, CVar.SERVERONLY);

    /// <summary>
    /// Ogg Vorbis bitrate in kbps, 8 to 192. Sets how much is sent to each client: 64 kbps is about
    /// 480 KB a minute, cheap now transfers ride the fast connection.
    /// </summary>
    public static readonly CVarDef<int> Bitrate =
        CVarDef.Create("wf.internet_sound.bitrate", 64, CVar.SERVERONLY);

    /// <summary>
    /// Send stereo instead of mono. Doubles client memory and wants a higher bitrate to be worth it.
    /// Ship PA tracks are positional, so mono is the right choice for them either way.
    /// </summary>
    public static readonly CVarDef<bool> Stereo =
        CVarDef.Create("wf.internet_sound.stereo", false, CVar.SERVERONLY);

    /// <summary>
    /// Seconds to wait for download and conversion before giving up.
    /// </summary>
    public static readonly CVarDef<int> Timeout =
        CVarDef.Create("wf.internet_sound.timeout", 120, CVar.SERVERONLY);

    /// <summary>
    /// Largest converted file in megabytes that will be sent to clients. At the default 64 kbps a full-length
    /// track is about 3.4 MB. A bitrate past about 150 kbps needs this raised, or long tracks are refused.
    /// </summary>
    public static readonly CVarDef<int> MaxSizeMb =
        CVarDef.Create("wf.internet_sound.max_size_mb", 8, CVar.SERVERONLY);

    /// <summary>
    /// Largest encrypted input download in megabytes, shared across all HTTPS tunnels and fragments.
    /// </summary>
    public static readonly CVarDef<int> MaxDownloadMb =
        CVarDef.Create("wf.internet_sound.max_download_mb", 64, CVar.SERVERONLY);

    /// <summary>
    /// Seconds to wait for clients before starting a global admin sound. PA tracks do not wait;
    /// each listener joins their shared timeline when the file arrives.
    /// </summary>
    public static readonly CVarDef<int> ReadyTimeout =
        CVarDef.Create("wf.internet_sound.ready_timeout", 45, CVar.SERVERONLY);

    /// <summary>
    /// Client volume for internet sounds, 0 to 1. Set from the radio popup. Ship PA tracks ignore this;
    /// they're diegetic and follow the normal ambience sliders.
    /// </summary>
    public static readonly CVarDef<float> Volume =
        CVarDef.Create("wf.internet_sound.volume", 0.5f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
