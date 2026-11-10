using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;

namespace Content.Client.Audio;

/// <summary>
/// Lets internet sounds pause station event music (nuke countdown, round end), then resume it.
/// </summary>
public sealed partial class ClientGlobalSoundSystem
{
    private readonly HashSet<EntityUid> _wfPausedEventMusic = new();
    private bool _wfEventMusicPaused;

    /// <summary>
    /// Pauses event music and keeps any new event track paused until <see cref="ResumeEventMusicWolfgate"/>.
    /// </summary>
    public void PauseEventMusicWolfgate()
    {
        _wfEventMusicPaused = true;
        EnforceEventMusicPauseWolfgate();
    }

    /// <summary>
    /// Pauses event tracks started since the last call. Run every frame while paused.
    /// </summary>
    public void EnforceEventMusicPauseWolfgate()
    {
        if (!_wfEventMusicPaused)
            return;

        foreach (var uid in _eventAudio.Values)
        {
            if (uid == null || !TryComp<AudioComponent>(uid, out var audio))
                continue;

            // The engine starts a new track on its first audio frame even if it was paused before then.
            var restarted = audio.State == AudioState.Paused && audio.Playing;
            if (audio.State != AudioState.Playing && !restarted)
                continue;

            _audio.SetState(uid, AudioState.Paused, force: restarted, component: audio);
            _wfPausedEventMusic.Add(uid.Value);
        }
    }

    /// <summary>
    /// Resumes every event track paused by <see cref="PauseEventMusicWolfgate"/> that still exists.
    /// </summary>
    public void ResumeEventMusicWolfgate()
    {
        _wfEventMusicPaused = false;

        foreach (var uid in _wfPausedEventMusic)
        {
            if (TryComp<AudioComponent>(uid, out var audio) && audio.State == AudioState.Paused)
                _audio.SetState(uid, AudioState.Playing, component: audio);
        }

        _wfPausedEventMusic.Clear();
    }
}
