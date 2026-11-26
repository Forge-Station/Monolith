namespace Content.Shared._WF.MappingMode;

/// <summary>
/// A token bucket per key: each key holds up to <see cref="Burst"/> tokens and regains <see cref="Rate"/> a second.
/// </summary>
public sealed class WFMappingThrottle<TKey> where TKey : notnull
{
    /// <summary>Tokens regained per second.</summary>
    public double Rate;

    /// <summary>Tokens a key can hold.</summary>
    public double Burst;

    private readonly Dictionary<TKey, (double Tokens, TimeSpan Last)> _buckets = new();

    public WFMappingThrottle(double rate, double burst)
    {
        Rate = rate;
        Burst = burst;
    }

    /// <summary>Takes one token for <paramref name="key"/>; false when it has none left.</summary>
    public bool TryTake(TKey key, TimeSpan now)
    {
        var tokens = Burst;
        if (_buckets.TryGetValue(key, out var bucket))
        {
            var elapsed = Math.Max(0, (now - bucket.Last).TotalSeconds);
            tokens = Math.Min(Burst, bucket.Tokens + elapsed * Rate);
        }

        if (tokens < 1)
        {
            _buckets[key] = (tokens, now);
            return false;
        }

        _buckets[key] = (tokens - 1, now);
        return true;
    }

    public void Forget(TKey key)
    {
        _buckets.Remove(key);
    }

    public void Clear()
    {
        _buckets.Clear();
    }
}
