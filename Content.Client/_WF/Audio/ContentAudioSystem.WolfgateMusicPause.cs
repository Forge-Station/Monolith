using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>What is holding the music paused; it resumes once none are left.</summary>
[Flags]
public enum WfMusicPauseReason : byte
{
    None = 0,
    InternetSound = 1 << 0,
    SoundtrackMute = 1 << 1,
}

/// <summary>
/// Lets internet sounds and the soundtrack mute pause ambient, combat and lobby music, then resume it.
/// A partial so the music systems stay untouched.
/// </summary>
public sealed partial class ContentAudioSystem
{
    private readonly HashSet<EntityUid> _wfPausedMusic = new();
    private WfMusicPauseReason _wfMusicPauseReasons;

    /// <summary>
    /// Pauses current music and keeps any new track paused until every reason is resumed.
    /// </summary>
    public void PauseMusicWolfgate(WfMusicPauseReason reason)
    {
        _wfMusicPauseReasons |= reason;
        EnforceMusicPauseWolfgate();
    }

    /// <summary>
    /// Pauses tracks the music loops started since the last call. Run every frame while paused.
    /// </summary>
    public void EnforceMusicPauseWolfgate()
    {
        if (_wfMusicPauseReasons == WfMusicPauseReason.None)
            return;

        PauseMusicStream(_ambientMusicStream);
        PauseMusicStream(_lobbySoundtrackInfo?.MusicStreamEntityUid);
    }

    /// <summary>
    /// Drops a pause reason and, once none are left, resumes every track that still exists.
    /// </summary>
    public void ResumeMusicWolfgate(WfMusicPauseReason reason)
    {
        _wfMusicPauseReasons &= ~reason;
        if (_wfMusicPauseReasons != WfMusicPauseReason.None)
            return;

        foreach (var uid in _wfPausedMusic)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Paused)
                _audio.SetState(uid, AudioState.Playing, component: audio);
        }

        _wfPausedMusic.Clear();
    }

    private void PauseMusicStream(EntityUid? uid)
    {
        if (uid == null || !TryComp<AudioComponent>(uid, out var audio))
            return;

        // The engine starts a new track on its first audio frame even if we paused it before then,
        // leaving it marked paused but audible. Combat and biome changes hit this, so re-pause those too.
        var restarted = audio.State == AudioState.Paused && audio.Playing;
        if (audio.State != AudioState.Playing && !restarted)
            return;

        _audio.SetState(uid, AudioState.Paused, force: restarted, component: audio);
        _wfPausedMusic.Add(uid.Value);
    }
}
