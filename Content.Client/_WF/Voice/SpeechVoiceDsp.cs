namespace Content.Client._WF.Voice;

/// <summary>Band-pass filter with a peak gain of 1, for hiss and plosive bursts.</summary>
internal struct NoiseBand
{
    private float _b0;
    private float _a1;
    private float _a2;
    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;

    /// <summary>Sets the centre frequency and bandwidth, both in Hz.</summary>
    public void Tune(float frequency, float bandwidth, int sampleRate)
    {
        var w = MathF.Tau * frequency / sampleRate;
        var alpha = MathF.Sin(w) * bandwidth / (2f * frequency);
        var a0 = 1f + alpha;
        _b0 = alpha / a0;
        _a1 = -2f * MathF.Cos(w) / a0;
        _a2 = (1f - alpha) / a0;
    }

    /// <summary>Filters one sample.</summary>
    public float Process(float x)
    {
        var y = _b0 * (x - _x2) - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>Xorshift white noise, seeded so a line renders the same for every listener.</summary>
internal struct NoiseSource
{
    private uint _state;

    /// <summary>Starts the sequence from a seed.</summary>
    public NoiseSource(int seed)
    {
        _state = (uint) seed | 1u;
    }

    /// <summary>A uniform value from -1 to 1.</summary>
    public float Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state / (float) uint.MaxValue * 2f - 1f;
    }
}
