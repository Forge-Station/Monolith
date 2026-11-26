using Content.Server.Body.Components; // WOLFGATE(Wolfmed): D13, BloodstreamComponent is server-only in Wolfgate.
using Content.Shared.Body.Part;
using Content.Server.Body.Systems; // WOLFGATE(Wolfmed): D13, BloodstreamSystem is server-only in Wolfgate.
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Timing; // WOLFGATE(Wolfmed): M1a

namespace Content.Shared._Onyx.Wounds;

/// <summary>
/// Drains blood directly from the bloodstream of a body while an internal bleeding
/// wound is active. Unlike regular wound bleeding, no blood leaks outside (no puddles).
/// </summary>
public sealed partial class WoundInternalBleedingSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!; // WOLFGATE(Wolfmed): M1a

    // WOLFGATE(Wolfmed): M1a: P22. A per-frame amount (0.2 u/s is 0.0067 u at 30 Hz) rounds to nothing in FixedPoint2,
    // so the bleed ticks once a second instead and every amount clears the 0.01 u step.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        SubscribeLocalEvent<WoundInternalBleedingComponent, WoundChangedEvent>(OnChanged);
        SubscribeLocalEvent<WoundInternalBleedingComponent, WoundStateChangedEvent>(OnStateChanged);
        SubscribeLocalEvent<WoundInternalBleedingComponent, WoundRemovedEvent>(OnRemoved);
    }

    private void OnChanged(Entity<WoundInternalBleedingComponent> ent, ref WoundChangedEvent args)
    {
        if (!_net.IsServer || !TryComp(ent, out WoundComponent? wound))
            return;

        ent.Comp.Severity = wound.State == WoundState.Open ? args.Severity : FixedPoint2.Zero;
        Dirty(ent);
    }

    private void OnStateChanged(Entity<WoundInternalBleedingComponent> ent, ref WoundStateChangedEvent args)
    {
        if (!_net.IsServer || !TryComp(ent, out WoundComponent? wound))
            return;

        ent.Comp.Severity = args.State == WoundState.Open ? wound.Severity : FixedPoint2.Zero;
        Dirty(ent);
    }

    private void OnRemoved(Entity<WoundInternalBleedingComponent> ent, ref WoundRemovedEvent args)
    {
        if (_net.IsServer)
        {
            ent.Comp.Severity = FixedPoint2.Zero;
            Dirty(ent);
        }
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // WOLFGATE(Wolfmed): M1a: P22, once a second; see TickInterval.
        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TickInterval;
        var seconds = (float) TickInterval.TotalSeconds;

        var query = EntityQueryEnumerator<WoundInternalBleedingComponent, WoundComponent>();
        while (query.MoveNext(out var uid, out var internalBleeding, out var core))
        {
            if (internalBleeding.Severity <= FixedPoint2.Zero || core.State != WoundState.Open)
                continue;

            if (TryGetBody(core.HoldingPart, out var body) && TryComp(body, out BloodstreamComponent? bloodstream))
            {
                var amount = FixedPoint2.New(internalBleeding.Rate * internalBleeding.Severity.Float() * seconds); // WOLFGATE(Wolfmed): M1a
                if (amount > FixedPoint2.Zero)
                    // WOLFGATE(Wolfmed): Wolfgate's TryModifyBloodLevel takes (EntityUid, amount, component?); the tuple
                    // literal would need two chained user-defined conversions, which C# does not do.
                    _bloodstream.TryModifyBloodLevel(body, -amount, bloodstream);
            }
        }
    }

    private bool TryGetBody(EntityUid part, out EntityUid body)
    {
        body = default;
        if (!TryComp(part, out BodyPartComponent? partComp) || partComp.Body is not { } attachedBody)
            return false;

        body = attachedBody;
        return true;
    }
}
