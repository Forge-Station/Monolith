using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Alert;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Makes a wound host's suffocation alert say why it can't breathe: no air, or no lungs.</summary>
// "No air" replaces the stock low-gas alert (one marked line in RespiratorSystem asks SuffocationAlert). "No lungs"
// covers a body with no working lungs, which the respirator never alerts because it raises its alert per lung.
// Everything else keeps the stock alert.
public sealed class WolfmedBreathingAlertSystem : EntitySystem
{
    public static readonly ProtoId<AlertPrototype> NoAir = "WFWolfmedCantBreatheAir";
    public static readonly ProtoId<AlertPrototype> NoLungs = "WFWolfmedCantBreatheLungs";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WolfmedBreathingSystem _breathing = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;

    /// <summary>Bodies this system has put the no-lungs alert on.</summary>
    private readonly HashSet<EntityUid> _noLungs = new();
    private readonly List<EntityUid> _gone = new();
    private TimeSpan _next;

    /// <summary>
    /// The marked respirator line's hand-off: the alert to show for a body that is suffocating with lungs in it. A
    /// wound host gets "Can't breathe: no air"; anything else keeps its lung's own gas alert.
    /// </summary>
    public ProtoId<AlertPrototype> SuffocationAlert(EntityUid body, ProtoId<AlertPrototype> stock) =>
        _consciousness.OwnsMobState(body) ? NoAir : stock;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + TickInterval;

        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var body, out _))
        {
            if (!_mobState.IsDead(body) && _breathing.Assess(body).Source == WolfmedBreathingSource.Lungs)
            {
                if (_noLungs.Add(body))
                    _alerts.ShowAlert(body, NoLungs);
            }
        }

        _gone.Clear();
        foreach (var body in _noLungs)
        {
            if (TerminatingOrDeleted(body))
            {
                _gone.Add(body);
                continue;
            }

            if (!_mobState.IsDead(body) && _breathing.Assess(body).Source == WolfmedBreathingSource.Lungs)
                continue;

            _gone.Add(body);
            _alerts.ClearAlert(body, NoLungs);
        }

        foreach (var body in _gone)
            _noLungs.Remove(body);
    }
}
