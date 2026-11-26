//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// Modified for Wolfgate on 2026-11-01: based on ManagedDoom's SilkMusic.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Client._WF.DoomArcade.Engine;
using Content.Client._WF.DoomArcade.Engine.Audio;
using Content.Client._WF.DoomArcade.MeltySynth;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;

namespace Content.Client._WF.DoomArcade;

/// <summary>
/// Renders each MIDI track to a looping sound on a worker thread, because content can't stream audio.
/// A track starts once its render finishes, so music comes in a moment after a level does.
/// </summary>
public sealed class DoomArcadeMusic : IMusic, IDisposable
{
    // Half of CD rate: the wait before a track starts and its memory both halve, and this soundfont loses little.
    public const int SampleRate = 22050;

    private const int RenderBlock = 2048;
    private const int MaxTrackSeconds = 900;

    // The synth renders quietly; this is the headroom ManagedDoom gives its music at full volume.
    private const float SampleScale = 32767f * 2f;

    private readonly Config _config;
    private readonly Wad _wad;
    private readonly IAudioManager _audio;
    private readonly IConfigurationManager _cfg;
    private readonly ISawmill _sawmill;
    private readonly Task<SoundFont> _soundFont;

    private Bgm _current = Bgm.NONE;
    private bool _loop;
    private CancellationTokenSource? _cancel;
    private Task<short[]?>? _pending;
    private AudioStream? _clip;
    private IAudioSource? _source;

    public int MaxVolume => 15;

    public int Volume
    {
        get => _config.audio_musicvolume;
        set => _config.audio_musicvolume = value;
    }

    /// <summary>The game changed track: the WAD lump, or null for none, and whether it loops.</summary>
    public event Action<string?, bool>? TrackChanged;

    /// <summary>True once the current track has rendered and started.</summary>
    public bool Started => _source != null;

    public DoomArcadeMusic(
        Config config,
        GameContent content,
        byte[] soundFont,
        IAudioManager audio,
        IConfigurationManager cfg,
        ISawmill sawmill)
    {
        _config = config;
        _wad = content.Wad;
        _audio = audio;
        _cfg = cfg;
        _sawmill = sawmill;
        _soundFont = Task.Run(() => new SoundFont(new MemoryStream(soundFont)));
    }

    public void StartMusic(Bgm bgm, bool loop)
    {
        if (bgm == _current)
            return;

        _current = bgm;
        _loop = loop;
        Stop();

        var lump = "D_" + DoomInfo.BgmNames[(int) bgm].ToString().ToUpper();
        if (_wad.GetLumpNumber(lump) == -1)
        {
            TrackChanged?.Invoke(null, false);
            return;
        }

        TrackChanged?.Invoke(lump, loop);

        var midi = _wad.ReadLump(lump);
        var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        _cancel = cancel;
        var soundFont = _soundFont;
        var sawmill = _sawmill;
        _pending = Task.Run(async () =>
        {
            try
            {
                return Render(await soundFont, midi, token);
            }
            catch (Exception e)
            {
                sawmill.Error($"Doom music failed to render: {e}");
                return null;
            }
        });
    }

    /// <summary>Starts a finished render and follows the volume settings. Call on the main thread.</summary>
    public void Update()
    {
        if (_pending is { IsCompleted: true } finished)
        {
            _pending = null;

#pragma warning disable RA0004 // IsCompleted was checked above, so this can't block.
            if (finished.Result is { Length: > 0 } samples)
                Play(samples);
#pragma warning restore RA0004
        }

        if (_source != null)
            _source.Gain = (float) _config.audio_musicvolume / MaxVolume * _cfg.GetCVar(CCVars.InterfaceVolume);
    }

    public void Dispose() => Stop();

    /// <summary>Renders a MIDI track to interleaved 16-bit stereo. Returns null if cancelled.</summary>
    public static short[]? Render(SoundFont soundFont, byte[] midi, CancellationToken token)
    {
        var file = new MidiFile(new MemoryStream(midi));
        var synthesizer = new Synthesizer(soundFont, new SynthesizerSettings(SampleRate));
        var sequencer = new MidiFileSequencer(synthesizer);
        sequencer.Play(file, false);

        var seconds = Math.Min(file.Length.TotalSeconds, MaxTrackSeconds);
        var frames = (int) (seconds * SampleRate);
        var samples = new short[frames * 2];
        var left = new float[RenderBlock];
        var right = new float[RenderBlock];

        for (var frame = 0; frame < frames; frame += RenderBlock)
        {
            if (token.IsCancellationRequested)
                return null;

            sequencer.Render(left, right);

            var count = Math.Min(RenderBlock, frames - frame);
            for (var i = 0; i < count; i++)
            {
                samples[(frame + i) * 2] = ToSample(left[i]);
                samples[(frame + i) * 2 + 1] = ToSample(right[i]);
            }
        }

        return samples;
    }

    private static short ToSample(float value)
    {
        return (short) Math.Clamp(value * SampleScale, short.MinValue, short.MaxValue);
    }

    private void Play(short[] samples)
    {
        _clip = _audio.LoadAudioRaw(samples, 2, SampleRate);
        _source = _audio.CreateAudioSource(_clip);
        if (_source == null)
            return;

        _source.Global = true;
        _source.RolloffFactor = 0;
        _source.Looping = _loop;
        _source.Gain = 0;
        _source.StartPlaying();
    }

    private void Stop()
    {
        _cancel?.Cancel();
        _cancel = null;
        _pending = null;

        _source?.Dispose();
        _source = null;
        _clip?.Dispose();
        _clip = null;
    }
}
