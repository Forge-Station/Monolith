using System.Diagnostics;

namespace Content.Server._Forge.KIAS;

public enum KiasPhase
{
    CrewUpdate, CrewCoverage, CrewScan, CrewPeople, CrewFauna, CrewRegistration,
    RuntimeUpdate, RuntimeBoot, RuntimeTimers, RuntimeEvents, RuntimeWork, RuntimeCommands,
    IntegrationReconcile, IntegrationControl, RoomGeometry, IoEmit, IoCommand, ProtocolTrigger,
}

public sealed class KiasPhaseMetrics
{
    public long Calls, TimestampTicks, AllocatedBytes, MaxTimestampTicks;
    public long ExclusiveTimestampTicks, ExclusiveAllocatedBytes;
    public readonly long[] DurationBuckets = new long[32];
    public long Items, MaxQueue;
}

public readonly struct KiasPhaseMeasurement : IDisposable
{
    private struct Frame { public long ChildrenTicks, ChildrenBytes; }
    [ThreadStatic] private static Frame[]? _frames;
    [ThreadStatic] private static int _depth;
    public static Action<int, long, long, long, long>? Observed;
    private readonly KiasPhaseMetrics? _metric;
    private readonly long _started, _bytes;
    private readonly int _slot, _phase;

    public KiasPhaseMeasurement(KiasSystem system, KiasPhase phase)
    {
        _metric = system.MeasureUpdates ? system.Phases[(int) phase] : null;
        _phase = (int) phase;
        _slot = -1;
        if (_metric != null)
        {
            _frames ??= new Frame[512];
            if (_depth >= _frames.Length) throw new InvalidOperationException("KIAS measurement nesting exceeded its fixed capacity.");
            _slot = _depth++;
            _frames[_slot] = default;
        }
        _bytes = _metric != null ? GC.GetAllocatedBytesForCurrentThread() : 0;
        _started = _metric != null ? Stopwatch.GetTimestamp() : 0;
    }

    public void Dispose()
    {
        if (_metric == null) return;
        var elapsed = Stopwatch.GetTimestamp() - _started;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
        if (_depth != _slot + 1) throw new InvalidOperationException("KIAS measurement scopes disposed out of order.");
        var frame = _frames![_slot];
        _depth--;
        if (_depth > 0)
        {
            _frames[_depth - 1].ChildrenTicks += elapsed;
            _frames[_depth - 1].ChildrenBytes += bytes;
        }
        var exclusive = elapsed - frame.ChildrenTicks;
        var exclusiveBytes = bytes - frame.ChildrenBytes;
        _metric.Calls++;
        _metric.TimestampTicks += elapsed;
        _metric.AllocatedBytes += bytes;
        _metric.ExclusiveTimestampTicks += exclusive;
        _metric.ExclusiveAllocatedBytes += exclusiveBytes;
        _metric.MaxTimestampTicks = Math.Max(_metric.MaxTimestampTicks, elapsed);
        var bucket = 0;
        for (var value = elapsed; value > 1 && bucket < 31; value >>= 1) bucket++;
        _metric.DurationBuckets[bucket]++;
        Observed?.Invoke(_phase, elapsed, exclusive, bytes, exclusiveBytes);
    }
}
