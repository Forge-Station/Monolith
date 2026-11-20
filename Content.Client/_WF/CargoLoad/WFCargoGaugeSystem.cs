namespace Content.Client._WF.CargoLoad;

/// <summary>Refreshes the cargo strips on screen, which can't poll themselves while hidden.</summary>
public sealed class WFCargoGaugeSystem : EntitySystem
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.25);

    private readonly HashSet<WFCargoGauge> _gauges = new();
    private readonly List<WFCargoGauge> _polled = new();
    private TimeSpan _poll;

    public void Track(WFCargoGauge gauge)
    {
        _gauges.Add(gauge);
    }

    public void Untrack(WFCargoGauge gauge)
    {
        _gauges.Remove(gauge);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _poll += TimeSpan.FromSeconds(frameTime);
        if (_poll < PollInterval)
            return;

        _poll = TimeSpan.Zero;
        _polled.Clear();
        _polled.AddRange(_gauges);
        foreach (var gauge in _polled)
        {
            gauge.Refresh();
        }
    }
}
