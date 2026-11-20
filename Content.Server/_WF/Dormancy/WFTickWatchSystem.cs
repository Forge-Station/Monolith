using System.Linq;
using System.Reflection;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Dormancy;

/// <summary>Server cvars for <see cref="WFTickWatchSystem"/>.</summary>
[CVarDefs]
public sealed class WFTickWatchCVars
{
    /// <summary>A tick longer than this many milliseconds is logged with what it spent its time on; 0 turns it off.</summary>
    public static readonly CVarDef<float> SlowTickMs =
        CVarDef.Create("wf.slow_tick_ms", 0f, CVar.SERVERONLY);
}

/// <summary>
/// Logs each tick that runs past <see cref="WFTickWatchCVars.SlowTickMs"/>, with the systems and engine stages that took
/// the most of it, read from the engine's own tick timing. Off by default; a diagnostic for finding hitches.
/// </summary>
public sealed class WFTickWatchSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Histogram? SystemUsage = typeof(EntitySystemManager)
        .GetField("_tickUsageHistogram", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Histogram;

    private static readonly Histogram? EngineUsage = typeof(Robust.Server.Physics.GridFixtureSystem).Assembly
        .GetType("Robust.Server.BaseServer")?.GetField("TickUsage", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Histogram;

    private float _threshold;
    private bool _enabledMetrics;
    private Dictionary<string, double> _last = new();
    private Dictionary<string, double> _now = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WFTickWatchCVars.SlowTickMs, SetThreshold, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetThreshold(0f);
    }

    private void SetThreshold(float ms)
    {
        _threshold = ms;
        // Per-system timing is only kept while the system manager's metrics are on.
        if (ms > 0f && !_systems.MetricsEnabled)
        {
            _systems.MetricsEnabled = true;
            _enabledMetrics = true;
        }
        else if (ms <= 0f && _enabledMetrics)
        {
            _systems.MetricsEnabled = false;
            _enabledMetrics = false;
        }

        _last.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_threshold <= 0f || EngineUsage == null || SystemUsage == null)
            return;

        // One tick's worth: everything since this point in the tick before.
        _now.Clear();
        var total = 0.0;
        foreach (var labels in EngineUsage.GetAllLabelValues())
        {
            var sum = EngineUsage.WithLabels(labels).Sum;
            _now["engine " + labels[0]] = sum;
            if (labels[0] != "EntitySystems")
                total += sum - _last.GetValueOrDefault("engine " + labels[0], sum);
        }

        foreach (var labels in SystemUsage.GetAllLabelValues())
        {
            var sum = SystemUsage.WithLabels(labels).Sum;
            _now[labels[0]] = sum;
            total += sum - _last.GetValueOrDefault(labels[0], sum);
        }

        if (_last.Count > 0 && total * 1000 >= _threshold)
        {
            var top = _now
                .Where(p => p.Key != "engine EntitySystems")
                .Select(p => (p.Key, Ms: (p.Value - _last.GetValueOrDefault(p.Key, p.Value)) * 1000))
                .Where(p => p.Ms >= 1)
                .OrderByDescending(p => p.Ms)
                .Take(6)
                .Select(p => $"{p.Key} {p.Ms:F0}");
            Log.Warning($"Slow tick {_timing.CurTick.Value}: {total * 1000:F0} ms ({string.Join(", ", top)}).");
        }

        (_last, _now) = (_now, _last);
    }
}
