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
// Modified for Wolfgate on 2026-11-01: based on ManagedDoom's SilkSound.

using System.Numerics;
using Content.Client._WF.DoomArcade.Engine;
using Content.Client._WF.DoomArcade.Engine.Audio;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;

namespace Content.Client._WF.DoomArcade;

/// <summary>Plays the WAD's sound effects on the client's own audio device, panned around the player.</summary>
public sealed class DoomArcadeSound : ISound, IDisposable
{
    private const int ChannelCount = 8;
    private const float ClipDistance = 1200;
    private const float CloseDistance = 160;
    private const float Attenuator = ClipDistance - CloseDistance;

    private static readonly float FastDecay = (float) Math.Pow(0.5, 1.0 / (35 / 5));
    private static readonly float SlowDecay = (float) Math.Pow(0.5, 1.0 / 35);

    private readonly Config _config;
    private readonly IAudioManager _audio;
    private readonly IConfigurationManager _cfg;
    private readonly AudioStream?[] _clips;
    private readonly float[] _amplitudes;
    private readonly string[] _lumps;
    private readonly DoomRandom _random = new();
    private readonly Channel[] _channels = new Channel[ChannelCount];

    private IAudioSource? _uiSource;
    private Sfx _uiReserved = Sfx.NONE;
    private Mobj? _listener;
    private bool _paused;

    /// <summary>A sound started: its WAD lump and its loudness for the player, from 0 to 1.</summary>
    public event Action<string, float>? NoiseMade;

    public int MaxVolume => 15;

    public int Volume
    {
        get => _config.audio_soundvolume;
        set => _config.audio_soundvolume = value;
    }

    public DoomArcadeSound(Config config, GameContent content, IAudioManager audio, IConfigurationManager cfg)
    {
        _config = config;
        _audio = audio;
        _cfg = cfg;

        _clips = new AudioStream?[DoomInfo.SfxNames.Length];
        _amplitudes = new float[DoomInfo.SfxNames.Length];

        for (var i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new Channel();
        }

        _lumps = new string[_clips.Length];
        for (var i = 0; i < _clips.Length; i++)
        {
            var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpper();
            _lumps[i] = name;
            if (content.Wad.GetLumpNumber(name) != -1)
                LoadClip(i, content.Wad.ReadLump(name));
        }
    }

    public void SetListener(Mobj listener) => _listener = listener;

    public void Update()
    {
        foreach (var channel in _channels)
        {
            if (channel.Playing != Sfx.NONE)
            {
                if (channel.Source is { } source && (_paused || source.Playing))
                {
                    channel.Priority *= channel.Type == SfxType.Diffuse ? SlowDecay : FastDecay;
                    Apply(source, channel);
                }
                else
                {
                    channel.StopSource();
                    channel.Playing = Sfx.NONE;
                    if (channel.Reserved == Sfx.NONE)
                        channel.Mobj = null;
                }
            }

            if (channel.Reserved == Sfx.NONE)
                continue;

            channel.StopSource();
            channel.Source = CreateSource(_clips[(int) channel.Reserved]);
            if (channel.Source != null)
            {
                Apply(channel.Source, channel);
                channel.Source.Pitch = GetPitch(channel.Type, channel.Reserved);
                channel.Source.StartPlaying();
                channel.Playing = channel.Reserved;
                NoiseMade?.Invoke(_lumps[(int) channel.Reserved], channel.Source.Gain / MathF.Max(GetMasterGain(), 0.01f));
            }

            channel.Reserved = Sfx.NONE;
        }

        if (_uiReserved == Sfx.NONE)
            return;

        _uiSource?.Dispose();
        _uiSource = CreateSource(_clips[(int) _uiReserved]);
        if (_uiSource != null)
        {
            _uiSource.Gain = GetMasterGain();
            _uiSource.StartPlaying();
            NoiseMade?.Invoke(_lumps[(int) _uiReserved], 1f);
        }

        _uiReserved = Sfx.NONE;
    }

    public void StartSound(Sfx sfx)
    {
        if (_clips[(int) sfx] != null)
            _uiReserved = sfx;
    }

    public void StartSound(Mobj mobj, Sfx sfx, SfxType type) => StartSound(mobj, sfx, type, 100);

    public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
    {
        if (_clips[(int) sfx] == null || _listener == null)
            return;

        var x = (mobj.X - _listener.X).ToFloat();
        var y = (mobj.Y - _listener.Y).ToFloat();
        var priority = type == SfxType.Diffuse
            ? volume
            : _amplitudes[(int) sfx] * GetDistanceDecay(MathF.Sqrt(x * x + y * y)) * volume;

        foreach (var channel in _channels)
        {
            if (channel.Mobj == mobj && channel.Type == type)
            {
                channel.Reserve(sfx, priority, mobj, type, volume);
                return;
            }
        }

        foreach (var channel in _channels)
        {
            if (channel.Reserved == Sfx.NONE && channel.Playing == Sfx.NONE)
            {
                channel.Reserve(sfx, priority, mobj, type, volume);
                return;
            }
        }

        var quietest = _channels[0];
        foreach (var channel in _channels)
        {
            if (channel.Priority < quietest.Priority)
                quietest = channel;
        }

        if (priority >= quietest.Priority)
            quietest.Reserve(sfx, priority, mobj, type, volume);
    }

    public void StopSound(Mobj mobj)
    {
        foreach (var channel in _channels)
        {
            if (channel.Mobj != mobj)
                continue;

            channel.LastX = mobj.X;
            channel.LastY = mobj.Y;
            channel.Mobj = null;
            channel.Volume /= 5;
        }
    }

    public void Reset()
    {
        _random.Clear();

        foreach (var channel in _channels)
        {
            channel.Clear();
        }

        _listener = null;
        _paused = false;
    }

    public void Pause()
    {
        _paused = true;
        foreach (var channel in _channels)
        {
            channel.Source?.Pause();
        }
    }

    public void Resume()
    {
        _paused = false;
        foreach (var channel in _channels)
        {
            channel.Source?.StartPlaying();
        }
    }

    public void Dispose()
    {
        foreach (var channel in _channels)
        {
            channel.StopSource();
        }

        _uiSource?.Dispose();

        foreach (var clip in _clips)
        {
            clip?.Dispose();
        }
    }

    /// <summary>Turns a WAD sound lump into 16-bit samples, or null if it holds none.</summary>
    public static short[]? DecodeClip(byte[] data, out int sampleRate, out float amplitude)
    {
        sampleRate = 0;
        amplitude = 0;

        if (data.Length < 8)
            return null;

        sampleRate = BitConverter.ToUInt16(data, 2);
        var sampleCount = BitConverter.ToInt32(data, 4);
        var offset = 8;

        if (ContainsDmxPadding(data))
        {
            offset += 16;
            sampleCount -= 32;
        }

        if (sampleCount <= 0)
            return null;

        var samples = new short[sampleCount];
        var peak = 0;
        var peakCount = Math.Min(sampleRate / 5, sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = data[offset + i] - 128;
            samples[i] = (short) (sample << 8);
            if (i < peakCount)
                peak = Math.Max(peak, Math.Abs(sample));
        }

        amplitude = (float) peak / 128;
        return samples;
    }

    private void LoadClip(int index, byte[] data)
    {
        if (DecodeClip(data, out var sampleRate, out var amplitude) is not { } samples)
            return;

        _clips[index] = _audio.LoadAudioRaw(samples, 1, sampleRate);
        _amplitudes[index] = amplitude;
    }

    // If the first and last 16 samples repeat, they are DMX pad bytes: https://doomwiki.org/wiki/Sound
    private static bool ContainsDmxPadding(byte[] data)
    {
        var sampleCount = BitConverter.ToInt32(data, 4);
        if (sampleCount < 32)
            return false;

        for (var i = 1; i < 16; i++)
        {
            if (data[8 + i] != data[8] || data[8 + sampleCount - i - 1] != data[8 + sampleCount - 1])
                return false;
        }

        return true;
    }

    private IAudioSource? CreateSource(AudioStream? clip)
    {
        if (clip == null || _audio.CreateAudioSource(clip) is not { } source)
            return null;

        // Relative to the listener, with panning but none of the game world's distance falloff.
        source.Global = true;
        source.RolloffFactor = 0;
        return source;
    }

    private void Apply(IAudioSource source, Channel channel)
    {
        var gain = 0.01f * GetMasterGain() * channel.Volume;

        if (channel.Type == SfxType.Diffuse || _listener == null)
        {
            source.Position = Vector2.Zero;
            source.Gain = gain;
            return;
        }

        var sourceX = channel.Mobj?.X ?? channel.LastX;
        var sourceY = channel.Mobj?.Y ?? channel.LastY;
        var x = (sourceX - _listener.X).ToFloat();
        var y = (sourceY - _listener.Y).ToFloat();

        if (Math.Abs(x) < 16 && Math.Abs(y) < 16)
        {
            source.Position = Vector2.Zero;
            source.Gain = gain;
            return;
        }

        var angle = MathF.Atan2(y, x) - (float) _listener.Angle.ToRadian();
        source.Position = new Vector2(-MathF.Sin(angle), 0);
        source.Gain = gain * GetDistanceDecay(MathF.Sqrt(x * x + y * y));
    }

    private float GetMasterGain()
    {
        return (float) _config.audio_soundvolume / MaxVolume * _cfg.GetCVar(CCVars.InterfaceVolume);
    }

    private static float GetDistanceDecay(float distance)
    {
        return distance < CloseDistance ? 1f : Math.Max((ClipDistance - distance) / Attenuator, 0f);
    }

    private float GetPitch(SfxType type, Sfx sfx)
    {
        if (sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO)
            return 1f;

        var range = type == SfxType.Voice ? 0.075f : 0.025f;
        return 1f + range * (_random.Next() - 128) / 128;
    }

    private sealed class Channel
    {
        public Sfx Reserved = Sfx.NONE;
        public Sfx Playing = Sfx.NONE;
        public float Priority;
        public Mobj? Mobj;
        public SfxType Type;
        public int Volume;
        public Fixed LastX;
        public Fixed LastY;
        public IAudioSource? Source;

        public void Reserve(Sfx sfx, float priority, Mobj mobj, SfxType type, int volume)
        {
            Reserved = sfx;
            Priority = priority;
            Mobj = mobj;
            Type = type;
            Volume = volume;
        }

        public void StopSource()
        {
            Source?.Dispose();
            Source = null;
        }

        public void Clear()
        {
            StopSource();
            Reserved = Sfx.NONE;
            Playing = Sfx.NONE;
            Priority = 0;
            Mobj = null;
            Type = 0;
            Volume = 0;
            LastX = Fixed.Zero;
            LastY = Fixed.Zero;
        }
    }
}
