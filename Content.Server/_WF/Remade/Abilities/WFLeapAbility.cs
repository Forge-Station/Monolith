using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A crouch and a cry, then a flat-out jump at the target. Whatever it lands on is cut and knocked down. It needs its tail
/// (<see cref="WFRemadeLimbStat.Leap"/>). Numbers from DS13's leaper charge.
/// </summary>
public sealed partial class WFLeapAbility : WFAbility
{
    /// <summary>Seconds it crouches and cries before it jumps.</summary>
    [DataField]
    public float WindUp = 1.2f;

    /// <summary>How fast it flies, in tiles a second.</summary>
    [DataField]
    public float Speed = 15f;

    /// <summary>How far past the target it aims, so it lands on it.</summary>
    [DataField]
    public float Overshoot = 1f;

    /// <summary>It hits anything living this close while it flies.</summary>
    [DataField]
    public float HitRange = 1.2f;

    [DataField]
    public float KnockdownTime = 2f;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>Tiles added to the range when it leaps from against a wall.</summary>
    [DataField]
    public float WallRange;

    /// <summary>What the wind-up is multiplied by when it leaps from against a wall; under 1 is quicker.</summary>
    [DataField]
    public float WallWindUp = 1f;

    /// <summary>What the damage is multiplied by when it leaps from against a wall.</summary>
    [DataField]
    public float WallDamage = 1f;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Flight,
    }

    /// <summary>Leaps it has made.</summary>
    [ViewVariables]
    public int Leaps;

    /// <summary>Whether the last leap landed on something.</summary>
    [ViewVariables]
    public bool Hit;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private EntityUid _target;
    private bool _fromWall;

    public WFLeapAbility()
    {
        Cooldown = 14f;
        MinRange = 4f;
        Range = 12f;
        Needs.Add(WFRemadeLimbStat.Leap);
    }

    public override float SpeedModifier => _phase == Phase.WindUp ? 0f : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (ctx.Target is not { } target || !ctx.System.IsLive(target) || ctx.System.Roster.Airborne(ctx.Owner))
            return WFAbilityResult.None;

        var fromWall = ctx.System.AgainstWall(ctx.Owner);
        var reach = fromWall ? Range + WallRange : Range;
        var distance = ctx.System.Distance(ctx.Owner, target);
        if (distance < MinRange || distance > reach || !ctx.System.CanSee(ctx.Owner, target, reach))
            return WFAbilityResult.None;

        Hit = false;
        _phase = Phase.WindUp;
        _target = target;
        _fromWall = fromWall;
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(fromWall ? WindUp * WallWindUp : WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (ctx.System.IsDead(_target))
            return false;

        if (_phase == Phase.WindUp)
        {
            // It stays on the one it picked even if its own planning drops it while it crouches.
            ctx.System.KeepTarget(ctx.Owner, _target);
            if (ctx.Now < _phaseEnd)
                return true;

            var toward = ctx.System.Toward(ctx.Owner, _target);
            var distance = toward.Length();
            if (distance < 0.1f)
                return false;

            _phase = Phase.Flight;
            Leaps++;
            _phaseEnd = ctx.Now + TimeSpan.FromSeconds((distance + Overshoot) / Speed + 0.2f);
            ctx.System.Roster.Leap(ctx.Owner, toward / distance * (distance + Overshoot), Speed);
            return true;
        }

        foreach (var prey in ctx.System.Prey(ctx.Owner, HitRange))
        {
            Hit = true;
            ctx.System.Roster.Strike(ctx.Owner, prey, _fromWall ? Damage * WallDamage : Damage);
            ctx.System.Knockdown(prey, KnockdownTime);
            ctx.System.Play(prey, HitSound);
            ctx.System.Popup(Loc.GetString("wf-remade-leap-hit", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
            return false;
        }

        return ctx.Now < _phaseEnd && ctx.System.Roster.Airborne(ctx.Owner);
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        if (_phase == Phase.Flight)
            ctx.System.Roster.Land(ctx.Owner);

        _phase = Phase.Idle;
    }
}
