using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Server._Forge.KIAS;

public readonly struct KiasUpdateMeasurement : IDisposable
{
    private readonly KiasSystem? _system;
    private readonly long _started;
    private readonly long _bytes;

    public KiasUpdateMeasurement(KiasSystem system)
    {
        _system = system.MeasureUpdates ? system : null;
        _started = _system != null ? Stopwatch.GetTimestamp() : 0;
        _bytes = _system != null ? GC.GetAllocatedBytesForCurrentThread() : 0;
    }

    public void Dispose()
    {
        if (_system == null) return;
        _system.MeasuredUpdateMilliseconds += Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        _system.MeasuredUpdateBytes += GC.GetAllocatedBytesForCurrentThread() - _bytes;
    }
}
