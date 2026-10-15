namespace Content.Server._Forge.KIAS.Controllers;

public sealed class KiasRuntimeQueueMetrics
{
    public long Enqueued, Consumed, Rejected;
    public int HighWater;
    public long BackloggedTicks;
    public uint MaxAgeTicks;
    public readonly long[] AgeTicks = new long[4097];

    public void Enqueue(int depth) { Enqueued++; HighWater = Math.Max(HighWater, depth); }
    public void Consume(uint queued, uint now)
    {
        Consumed++;
        var age = now - queued;
        MaxAgeTicks = Math.Max(MaxAgeTicks, age);
        AgeTicks[Math.Min(age, 4096)]++;
    }
    public uint Percentile(double fraction)
    {
        if (Consumed == 0) return 0;
        var threshold = (long) Math.Ceiling(Consumed * fraction);
        long count = 0;
        for (var age = 0; age < AgeTicks.Length; age++)
            if ((count += AgeTicks[age]) >= threshold) return (uint) age;
        return MaxAgeTicks;
    }
}
