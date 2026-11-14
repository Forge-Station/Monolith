#nullable enable

using Content.Shared._WF.Shuttles;

namespace Content.Client._WF.Cockpit;

/// <summary>A collision annunciator sample without predicting or acknowledging the server's warning.</summary>
public readonly record struct WFCockpitTcasReading(WFCockpitTcasState State, TimeSpan ImpactTime = default,
    string? ThreatName = null, float Bearing = 0, float ClosingSpeed = 0)
{
    /// <summary>Flashes caution at 0.5 Hz and warning at 2 Hz; warning keeps both independent indications active.</summary>
    public WFCockpitTcasLamps LampsAt(TimeSpan time)
    {
        var seconds = Math.Max(0, time.TotalSeconds);
        return new WFCockpitTcasLamps(
            (State is WFCockpitTcasState.Caution or WFCockpitTcasState.Warning) && seconds % 2 < 1,
            State == WFCockpitTcasState.Warning && seconds % 0.5 < 0.25,
            State == WFCockpitTcasState.Clear,
            State is WFCockpitTcasState.Off or WFCockpitTcasState.NoSignal);
    }

    /// <summary>Missing telemetry and disabled alerts cannot be mistaken for a clear collision forecast.</summary>
    public static WFCockpitTcasReading From(bool available, bool enabled, CollisionWarningComponent? warning)
    {
        if (!available)
            return new(WFCockpitTcasState.NoSignal);
        if (!enabled)
            return new(WFCockpitTcasState.Off);
        if (warning == null)
            return new(WFCockpitTcasState.Clear);
        var state = warning.Level switch
        {
            CollisionWarningLevel.Advisory => WFCockpitTcasState.Caution,
            CollisionWarningLevel.Imminent => WFCockpitTcasState.Warning,
            _ => WFCockpitTcasState.NoSignal,
        };
        return new(state, warning.ImpactTime, warning.ThreatName, warning.Bearing, warning.ClosingSpeed);
    }
}

/// <summary>The current illumination of the four permanent TCAS annunciators.</summary>
public readonly record struct WFCockpitTcasLamps(bool Caution, bool Warning, bool Ok, bool Fault);

/// <summary>The distinct clear, unavailable, disabled and active collision states.</summary>
public enum WFCockpitTcasState : byte
{
    NoSignal,
    Off,
    Clear,
    Caution,
    Warning,
}
