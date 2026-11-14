using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>Lays a post effect over the rendered line. Species filters are shaped in <see cref="Speak"/>.</summary>
    private static void ApplyEffect(Span<float> buffer,
        SpeechVoiceFilter filter,
        float strength,
        SpeechVoiceBuffers buffers)
    {
        if (filter is not (SpeechVoiceFilter.Robot or SpeechVoiceFilter.Alien or SpeechVoiceFilter.Insect
            or SpeechVoiceFilter.Ghost))
        {
            return;
        }

        var dry = buffers.Dry.AsSpan(0, buffer.Length);
        buffer.CopyTo(dry);

        switch (filter)
        {
            case SpeechVoiceFilter.Robot:
                Robot(buffer);
                break;
            case SpeechVoiceFilter.Alien:
                Alien(buffer, dry);
                break;
            case SpeechVoiceFilter.Insect:
                Insect(buffer);
                break;
            case SpeechVoiceFilter.Ghost:
                Ghost(buffer, dry);
                break;
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = dry[i] * (1f - strength) + buffer[i] * strength;
        }
    }

    /// <summary>A 300 Hz to 3 kHz band, a soft clip and a quiet hiss that stops with the speech.</summary>
    private void Radio(Span<float> buffer)
    {
        var peak = 0f;
        foreach (var sample in buffer)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        if (peak <= 0f)
            return;

        var highPass = OnePole(300f);
        var lowPass = OnePole(3000f);
        var presence = new NoiseBand();
        presence.Tune(1800f, 900f, SampleRate);

        var clip = 1f / MathF.Tanh(RadioDrive);
        var hissRelease = Smoothing(0.008f);
        var hissGate = 0f;
        var low = 0f;
        var lowA = 0f;
        var lowB = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i] / peak;
            low += (x - low) * highPass;
            x -= low;

            lowA += (x - lowA) * lowPass;
            lowB += (lowA - lowB) * lowPass;
            x = lowB + 0.6f * presence.Process(lowB);

            x = MathF.Tanh(RadioDrive * x) * clip;

            // The hiss follows the speech, so it can't sit in the gaps between words as static.
            hissGate = MathF.Max(MathF.Abs(x), hissGate * (1f - hissRelease));
            buffer[i] = x + 0.015f * MathF.Min(1f, 4f * hissGate) * _noise.Next();
        }
    }

    /// <summary>The smoothing factor of a one-pole filter with this corner frequency.</summary>
    private static float OnePole(float frequency)
    {
        return 1f - MathF.Exp(-MathF.Tau * frequency / SampleRate);
    }

    /// <summary>Bit crush and sample hold, then a ring modulator.</summary>
    private static void Robot(Span<float> buffer)
    {
        const int hold = 2;
        const float levels = 16f;
        var held = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (i % hold == 0)
                held = MathF.Round(buffer[i] * levels) / levels;

            buffer[i] = held * MathF.Sin(MathF.Tau * 90f * i / SampleRate) * 1.4f;
        }
    }

    /// <summary>A wobbling chorus mixed with a high ring modulator.</summary>
    private static void Alien(Span<float> buffer, ReadOnlySpan<float> dry)
    {
        var depth = 0.004f * SampleRate;

        for (var i = 0; i < buffer.Length; i++)
        {
            var delay = depth * (1f + MathF.Sin(MathF.Tau * 8f * i / SampleRate));
            var chorus = Read(dry, i - delay);
            var ring = dry[i] * MathF.Sin(MathF.Tau * 300f * i / SampleRate);
            buffer[i] = 0.6f * chorus + 0.6f * ring;
        }
    }

    /// <summary>A brightened signal gated by a fast square buzz.</summary>
    private static void Insect(Span<float> buffer)
    {
        var previous = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i];
            var bright = sample - 0.7f * previous;
            previous = sample;

            var buzz = MathF.Sin(MathF.Tau * 120f * i / SampleRate) > 0f ? 1f : 0.2f;
            buffer[i] = bright * buzz * 1.3f;
        }
    }

    /// <summary>A slow pitch drift with an echo trail, softened by a low-pass.</summary>
    private static void Ghost(Span<float> buffer, ReadOnlySpan<float> dry)
    {
        var drift = 0.006f * SampleRate;
        var echo = (int) (0.12f * SampleRate);
        var smoothed = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            var delay = drift * (1f + MathF.Sin(MathF.Tau * 0.5f * i / SampleRate));
            var wet = Read(dry, i - delay);

            if (i >= echo)
                wet += 0.45f * buffer[i - echo];

            smoothed += 0.5f * (wet - smoothed);
            buffer[i] = smoothed;
        }
    }

    private static float Read(ReadOnlySpan<float> buffer, float position)
    {
        if (position < 0f)
            return 0f;

        var index = (int) position;
        if (index + 1 >= buffer.Length)
            return 0f;

        var frac = position - index;
        return buffer[index] + (buffer[index + 1] - buffer[index]) * frac;
    }
}
