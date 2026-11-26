using Content.Server._WF.Remade.Monolith;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Remade;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Timing;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Gifts;

/// <summary>
/// The Reach gift: a ship with a Scion aboard jumps faster and sees further on radar. Every jump adds resonance, whether the dial is
/// up or not.
/// </summary>
public sealed partial class WFReachSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private SharedRadarConsoleSystem _radar = default!;
    [Dependency] private WFMonolithSystem _monolith = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);

    private readonly HashSet<EntityUid> _convoy = new();
    private readonly Dictionary<EntityUid, (EntityUid Scion, float Factor)> _grids = new();
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        UpdateSensors();
    }

    /// <summary>A jump with a Scion aboard, on the ship or anything docked to it, adds resonance and loses some of its travel time.</summary>
    private void OnFtlStarted(ref FTLStartedEvent ev)
    {
        _convoy.Clear();
        _shuttle.GetAllDockedShuttles(ev.Entity, _convoy);

        var factor = 1f;
        var monoliths = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (xform.GridUid is not { } grid || !_convoy.Contains(grid) || _monolith.IsBarred(uid))
                continue;

            var scion = (uid, monolith);
            var dial = _monolith.GetDial(scion, WFGift.Reach);
            factor = Math.Min(factor, WFMonolithMath.ReachTravelFactor(dial, monolith.Resonance));
            _monolith.AddResonance(scion, WFMonolithMath.JumpGain(dial));
        }

        if (factor >= 1f || !TryComp<FTLComponent>(ev.Entity, out var ftl))
            return;

        // The travel clock was set just before this event; the arrival time is the floor.
        var arrival = _shuttle.DefaultArrivalTime;
        var travel = MathF.Max(ftl.TravelTime * factor, arrival);
        if (travel < ftl.TravelTime)
            ftl.StateTime = StartEndTime.FromCurTime(_timing, travel - arrival);
    }

    /// <summary>Stretches the radar range of every console on a ship with a Scion on it, and puts it back when the Scion stops.</summary>
    private void UpdateSensors()
    {
        _grids.Clear();
        var monoliths = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            var factor = WFMonolithMath.ReachSensorFactor(_monolith.GetDial((uid, monolith), WFGift.Reach), monolith.Resonance);
            if (factor > 1f && (!_grids.TryGetValue(grid, out var current) || factor > current.Factor))
                _grids[grid] = (uid, factor);
        }

        var consoles = EntityQueryEnumerator<RadarConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out var radar, out var xform))
        {
            var has = TryComp<WFReachSensorComponent>(uid, out var sensor);
            if (xform.GridUid is { } grid && _grids.TryGetValue(grid, out var want))
            {
                if (sensor == null)
                {
                    sensor = AddComp<WFReachSensorComponent>(uid);
                    sensor.BaseRange = radar.MaxRange;
                }

                sensor.Source = want.Scion;
                var target = sensor.BaseRange * want.Factor;
                if (MathF.Abs(radar.MaxRange - target) > 0.5f)
                    _radar.SetRange(uid, target, radar);
            }
            else if (has && sensor != null)
            {
                _radar.SetRange(uid, sensor.BaseRange, radar);
                RemCompDeferred<WFReachSensorComponent>(uid);
            }
        }
    }
}

/// <summary>A radar console whose range a Scion has stretched, and the range it had before.</summary>
[RegisterComponent]
public sealed partial class WFReachSensorComponent : Component
{
    [ViewVariables]
    public float BaseRange;

    [ViewVariables]
    public EntityUid? Source;
}
