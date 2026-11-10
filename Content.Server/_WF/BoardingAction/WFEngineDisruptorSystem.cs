using Content.Server._WF.BoardingAction.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Emp;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Server.Audio;
using Robust.Shared.Timing;

namespace Content.Server._WF.BoardingAction;

/// <summary>
/// Ion disruptor rounds. A hit on a hull EMPs every thruster and gyroscope on it, so the ship drifts dead until they
/// come back. Shields stop the round.
/// </summary>
public sealed partial class WFEngineDisruptorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedEmpSystem _emp = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFFtlDisruptionSystem _disruption = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextSweep;
    private readonly List<EntityUid> _thrusters = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEngineDisruptorComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnHit(Entity<WFEngineDisruptorComponent> ent, ref ProjectileHitEvent args)
    {
        if (HasComp<ShipShieldComponent>(args.Target) || Transform(args.Target).GridUid is not { } grid)
            return;

        Disrupt(grid, ent.Comp.Duration);
    }

    /// <summary>Knocks out a hull's thrusters and gyroscopes. False if none were left running to knock out.</summary>
    public bool Disrupt(EntityUid grid, TimeSpan duration)
    {
        if (!_disruption.IsTackleable(grid))
            return false;

        _thrusters.Clear();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<ThrusterComponent>(child))
                _thrusters.Add(child);
        }

        var hit = false;
        foreach (var thruster in _thrusters)
        {
            hit |= _emp.TryEmpEffects(thruster, 0f, duration);
        }

        if (!hit)
            return false;

        EnsureComp<WFEngineDisruptedComponent>(grid).Until = _timing.CurTime + duration;
        _audio.PlayPvs(SharedEmpSystem.EmpSound, grid);
        var consoles = AllEntityQuery<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var console, out _, out var xform))
        {
            if (xform.GridUid == grid)
                _popup.PopupEntity(Loc.GetString("wf-boarding-engines-disrupted"), console, PopupType.LargeCaution);
        }

        return true;
    }

    /// <summary>Whether a disruptor has a hull's engines down.</summary>
    public bool IsDisrupted(EntityUid grid)
    {
        return TryComp<WFEngineDisruptedComponent>(grid, out var comp) && comp.Until > _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;
        var query = EntityQueryEnumerator<WFEngineDisruptedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Until <= now)
                RemCompDeferred<WFEngineDisruptedComponent>(uid);
        }
    }
}
