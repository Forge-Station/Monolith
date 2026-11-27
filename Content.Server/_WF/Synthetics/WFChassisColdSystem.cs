using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Synthetics;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// A frozen chassis seizes up: from <see cref="StiffTemperature"/> down, cold pushes on its consciousness until it is
/// Downed, but never out. Wolfmed's own cold input skips machines, so an unsuited synthetic walked through space.
/// </summary>
public sealed class WFChassisColdSystem : EntitySystem
{
    /// <summary>The consciousness pressure key.</summary>
    public const string FreezePressure = "chassisfreeze";

    /// <summary>Above this the cold does nothing.</summary>
    public const float StiffTemperature = 180f;

    /// <summary>At or below this the pressure is at its cap.</summary>
    public const float SeizeTemperature = 60f;

    /// <summary>The highest pressure: past the Downed line (0.7), short of unconscious (1).</summary>
    public const float MaxPressure = 0.8f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;

    private TimeSpan _nextPoll;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _nextPoll)
            return;

        _nextPoll = now + PollInterval;
        var query = EntityQueryEnumerator<WFChassisComponent, TemperatureComponent>();
        while (query.MoveNext(out var uid, out _, out var temperature))
        {
            _consciousness.SetExternalPressure(uid, FreezePressure, Pressure(temperature.CurrentTemperature));
        }
    }

    /// <summary>The freeze pressure at a chassis temperature.</summary>
    public static float Pressure(float temperature)
    {
        var cold = (StiffTemperature - temperature) / (StiffTemperature - SeizeTemperature);
        return MaxPressure * Math.Clamp(cold, 0f, 1f);
    }
}
