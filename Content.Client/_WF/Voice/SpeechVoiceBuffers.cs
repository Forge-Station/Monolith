namespace Content.Client._WF.Voice;

/// <summary>Buffers kept between renders, so each line doesn't allocate on the large object heap.</summary>
public sealed class SpeechVoiceBuffers
{
    private float[]? _dry;

    /// <summary>The line as it is built.</summary>
    public readonly float[] Samples = new float[SpeechVoiceSynth.MaxSamples];

    /// <summary>The finished line.</summary>
    public readonly short[] Pcm = new short[SpeechVoiceSynth.MaxSamples];

    /// <summary>A copy of the line for effects to mix back in. Most voices never need it.</summary>
    public float[] Dry => _dry ??= new float[SpeechVoiceSynth.MaxSamples];
}
