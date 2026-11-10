using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A crouch and a leap at someone's face. If it lands it clings: it rides the victim, tearing at them every second, and
/// slows them, until it's killed, shaken off after a while or the victim drops. Parts of a divider use it. A head that
/// holds on until its victim dies turns the body (<see cref="TurnsBody"/>).
/// </summary>
public sealed partial class WFLatchAbility : WFAbility
{
    [DataField]
    public float WindUp = 0.8f;

    [DataField]
    public float Speed = 10f;

    /// <summary>How far past the target it aims, so it lands on it.</summary>
    [DataField]
    public float Overshoot = 0.6f;

    /// <summary>It clings to anyone this close while it flies.</summary>
    [DataField]
    public float HitRange = 1.1f;

    /// <summary>The longest it clings, in seconds.</summary>
    [DataField]
    public float Hold = 12f;

    /// <summary>Seconds between bites while it clings.</summary>
    [DataField]
    public float Interval = 1f;

    /// <summary>The victim's speed as a multiple of normal while it clings.</summary>
    [DataField]
    public float Slow = 0.7f;

    /// <summary>If the victim dies in its grip it stays on the body and turns it, instead of letting go.</summary>
    [DataField]
    public bool TurnsBody;

    /// <summary>What each bite does.</summary>
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? BiteSound;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Flight,
        Latched,
        Turning,
    }

    /// <summary>Times it has latched on.</summary>
    [ViewVariables]
    public int Latches;

    /// <summary>Bites it has made while clinging.</summary>
    [ViewVariables]
    public int Bites;

    /// <summary>Who it's clinging to now, if anyone.</summary>
    [ViewVariables]
    public EntityUid? Victim => _phase == Phase.Latched ? _victim : null;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private TimeSpan _nextBite;
    private TimeSpan _clingEnd;
    private EntityUid _target;
    private EntityUid _victim;

    public WFLatchAbility()
    {
        Cooldown = 10f;
        MinRange = 1.5f;
        Range = 6f;
    }

    public override float SpeedModifier => _phase is Phase.WindUp or Phase.Latched or Phase.Turning ? 0f : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range) || ctx.System.Roster.Airborne(ctx.Owner))
            return WFAbilityResult.None;

        _phase = Phase.WindUp;
        _target = target;
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (_phase is Phase.Latched or Phase.Turning)
            return Cling(ctx);

        if (ctx.System.IsDead(_target))
            return false;

        if (_phase == Phase.WindUp)
        {
            ctx.System.KeepTarget(ctx.Owner, _target);
            if (ctx.Now < _phaseEnd)
                return true;

            var toward = ctx.System.Toward(ctx.Owner, _target);
            var distance = toward.Length();
            if (distance < 0.1f)
                return false;

            _phase = Phase.Flight;
            _phaseEnd = ctx.Now + TimeSpan.FromSeconds((distance + Overshoot) / Speed + 0.3f);
            ctx.System.Roster.Leap(ctx.Owner, toward / distance * (distance + Overshoot), Speed);
            return true;
        }

        foreach (var prey in ctx.System.Prey(ctx.Owner, HitRange))
        {
            ctx.System.Roster.Land(ctx.Owner);
            Latches++;
            _victim = prey;
            _phase = Phase.Latched;
            _clingEnd = ctx.Now + TimeSpan.FromSeconds(Hold);
            _nextBite = ctx.Now;
            ctx.System.Elite.SetSolid(ctx.Owner, false);
            ctx.System.Popup(Loc.GetString("wf-remade-elite-latch", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
            return true;
        }

        return ctx.Now < _phaseEnd && ctx.System.Roster.Airborne(ctx.Owner);
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        if (_phase == Phase.Flight)
            ctx.System.Roster.Land(ctx.Owner);

        if (_phase is Phase.Latched or Phase.Turning)
            ctx.System.Elite.SetSolid(ctx.Owner, true);

        _phase = Phase.Idle;
    }

    private bool Cling(in WFAbilityContext ctx)
    {
        // The body is turning under it: it holds on until the body bursts.
        if (_phase == Phase.Turning)
        {
            if (!ctx.System.IsTurning(_victim))
                return false;

            ctx.System.Elite.Cling(ctx.Owner, _victim);
            return true;
        }

        if (TurnsBody && ctx.System.IsDead(_victim) && ctx.System.Turn(_victim, ctx.Owner))
        {
            _phase = Phase.Turning;
            ctx.System.Popup(Loc.GetString("wf-remade-head-takes-body", ("head", ctx.Owner), ("body", _victim)), _victim, PopupType.LargeCaution);
            return true;
        }

        if (ctx.System.IsDead(_victim) || ctx.Now >= _clingEnd)
            return false;

        ctx.System.Elite.Cling(ctx.Owner, _victim);
        if (ctx.Now < _nextBite)
            return true;

        Bites++;
        _nextBite = ctx.Now + TimeSpan.FromSeconds(Interval);
        ctx.System.Roster.Strike(ctx.Owner, _victim, Damage);
        ctx.System.Roster.Slow(_victim, Interval * 2f, Slow);
        ctx.System.Swell(_victim, Interval * 2f);
        ctx.System.Play(_victim, BiteSound);
        return true;
    }
}
