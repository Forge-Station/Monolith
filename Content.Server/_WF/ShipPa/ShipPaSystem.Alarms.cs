using Content.Shared._WF.ShipPa;
using Robust.Shared.Audio;

namespace Content.Server._WF.ShipPa;

public sealed partial class ShipPaSystem
{
    private readonly Queue<ShipPaTrackFinishedEvent> _finishedBroadcasts = new();
    private readonly List<EntityUid> _emptyBroadcastGrids = new();

    /// <summary>Starts a looping alarm on the grid's PA under a key, replacing one with the same key.</summary>
    public void StartAlarm(EntityUid grid, string key, SoundSpecifier sound, AudioParams? audioParams = null,
        string? message = null, Color? color = null, int priority = ShipPaPlaybackPolicy.DefaultAlarmPriority)
    {
        StartBroadcast(grid, key, sound, true, ShipPaBroadcastKind.Alarm, priority, audioParams, message, color);
    }

    /// <summary>Plays a track once on the grid's PA under a key, replacing one with the same key; false if the sound can't load.</summary>
    public bool StartTrack(EntityUid grid, string key, SoundSpecifier sound, AudioParams? audioParams = null, string? message = null, Color? color = null)
    {
        return StartBroadcast(grid, key, sound, false, ShipPaBroadcastKind.Track, 10, audioParams, message, color) != null;
    }

    private ShipPaBroadcast? StartBroadcast(EntityUid grid, string key, SoundSpecifier sound, bool loop,
        ShipPaBroadcastKind kind, int priority, AudioParams? audioParams = null, string? caption = null, Color? color = null)
    {
        if (!Exists(grid) || TerminatingOrDeleted(grid))
            return null;

        var resolved = _audio.ResolveSound(sound);
        var path = _audio.GetAudioPath(resolved);
        if (string.IsNullOrEmpty(path))
            return null;

        float length;
        try
        {
            length = (float) _audio.GetAudioLength(resolved).TotalSeconds;
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't load PA audio {path}: {e.Message}");
            return null;
        }

        if (!float.IsFinite(length) || length <= 0f)
            return null;

        var state = EnsureComp<ShipPaBroadcastComponent>(grid);
        // Key replacement is atomic to clients. Never replicate an intermediate empty timeline.
        state.Broadcasts.RemoveAll(b => b.Key == key);
        var start = _timing.CurTime + TimeSpan.FromSeconds(ShipPaPlaybackPolicy.StartLeadSeconds);
        var broadcast = new ShipPaBroadcast
        {
            Id = NextBroadcastId(), Key = key, Path = path, Start = start,
            Length = length, Loop = loop, Kind = kind, Priority = priority, Caption = caption, Color = color ?? Color.White,
            // Damage must never change playback rate.
            Params = (audioParams ?? sound.Params).WithLoop(loop).WithPitchScale(1f).WithVariation(null),
            RetainUntil = start + TimeSpan.FromSeconds(kind == ShipPaBroadcastKind.Announcement && caption != null ? Math.Max(8f, length) : length),
        };
        state.Broadcasts.Add(broadcast);
        Dirty(grid, state);
        RefreshCoverage(grid);
        UpdateBroadcastLights(grid, state);
        return broadcast;
    }

    /// <summary>Stops the alarm or track under a key.</summary>
    public void StopAlarm(EntityUid grid, string key)
    {
        if (!TryComp(grid, out ShipPaBroadcastComponent? state) || state.Broadcasts.RemoveAll(b => b.Key == key) == 0)
            return;

        Dirty(grid, state);
        UpdateBroadcastLights(grid, state);
        if (state.Broadcasts.Count == 0)
            RemComp<ShipPaBroadcastComponent>(grid);
    }

    /// <summary>Whether a broadcast under the key is about to start or still playing.</summary>
    public bool IsAlarmActive(EntityUid grid, string key)
    {
        return TryComp(grid, out ShipPaBroadcastComponent? state) && state.Broadcasts.Exists(b => b.Key == key && b.IsActive(_timing.CurTime));
    }

    private void UpdateBroadcasts()
    {
        // Finish callbacks can mutate these systems, so dispatch after enumeration.
        _finishedBroadcasts.Clear();
        _emptyBroadcastGrids.Clear();
        var query = EntityQueryEnumerator<ShipPaBroadcastComponent>();
        while (query.MoveNext(out var grid, out var state))
        {
            var changed = false;
            for (var i = state.Broadcasts.Count - 1; i >= 0; i--)
            {
                var broadcast = state.Broadcasts[i];
                if (broadcast.Loop || _timing.CurTime < broadcast.RetainUntil)
                    continue;

                state.Broadcasts.RemoveAt(i);
                changed = true;
                if (broadcast.Kind == ShipPaBroadcastKind.Track)
                    _finishedBroadcasts.Enqueue(new ShipPaTrackFinishedEvent(grid, broadcast.Key));
            }

            if (changed)
            {
                Dirty(grid, state);
                UpdateBroadcastLights(grid, state);
            }
            if (state.Broadcasts.Count == 0)
                _emptyBroadcastGrids.Add(grid);
        }

        foreach (var grid in _emptyBroadcastGrids)
            RemComp<ShipPaBroadcastComponent>(grid);
        _emptyBroadcastGrids.Clear();

        while (_finishedBroadcasts.TryDequeue(out var ev))
        {
            RaiseLocalEvent(ref ev);
        }
    }

    private void UpdateBroadcastLights(EntityUid grid, ShipPaBroadcastComponent state)
    {
        TimeSpan? until = null;
        foreach (var broadcast in state.Broadcasts)
        {
            if (!broadcast.IsActive(_timing.CurTime))
                continue;

            var end = broadcast.Loop ? TimeSpan.MaxValue : broadcast.Start + TimeSpan.FromSeconds(broadcast.Length);
            if (until == null || end > until)
                until = end;
        }

        _speakerBuffer.Clear();
        GatherSpeakers(grid, _speakerBuffer);
        foreach (var speaker in _speakerBuffer)
        {
            if (speaker.Comp.BroadcastingUntil == until)
                continue;

            speaker.Comp.BroadcastingUntil = until;
            UpdateAppearance(speaker);
        }
        _speakerBuffer.Clear();
    }
}
