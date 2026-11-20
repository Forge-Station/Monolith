using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A crouch, then a great leap over everything to where the target stood. It lands hard: everyone close is hurt and knocked
/// down. It needs its legs (<see cref="WFRemadeLimbStat.Leap"/>). Numbers from DS13-1.0's tripod high leap.
/// </summary>
public sealed partial class WFPounceAbility : WFAbility
{
    [DataField]
    public float WindUp = 0.8f;

    /// <summary>How fast it flies, in tiles a second.</summary>
    [DataField]
    public float Speed = 7f;

    /// <summary>It hurts everyone this close to where it lands.</summary>
    [DataField]
    public float Radius = 2.2f;

    [DataField]
    public float KnockdownTime = 2.5f;

    /// <summary>How far the landing shakes people's view.</summary>
    [DataField]
    public float ShakeRadius = 7f;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? LandSound;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Flight,
    }

    /// <summary>Leaps it has made.</summary>
    [ViewVariables]
    public int Pounces;

    /// <summary>People the last landing hit.</summary>
    [ViewVariables]
    public int Hits;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private EntityUid _target;

    public WFPounceAbility()
    {
        Cooldown = 6f;
        MinRange = 3f;
        Range = 12f;
        Needs.Add(WFRemadeLimbStat.Leap);
    }

    public override float SpeedModifier => _phase == Phase.WindUp ? 0f : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range) || ctx.System.Roster.Airborne(ctx.Owner))
            return WFAbilityResult.None;

        _phase = Phase.WindUp;
        _target = target;
        Hits = 0;
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (_phase == Phase.WindUp)
        {
            if (ctx.System.IsDead(_target))
                return false;

            ctx.System.KeepTarget(ctx.Owner, _target);
            if (ctx.Now < _phaseEnd)
                return true;

            var toward = ctx.System.Toward(ctx.Owner, _target);
            var distance = toward.Length();
            if (distance < 0.1f)
                return false;

            Pounces++;
            _phase = Phase.Flight;
            _phaseEnd = ctx.Now + TimeSpan.FromSeconds(distance / Speed + 0.4f);
            ctx.System.Roster.Leap(ctx.Owner, toward, Speed);
            return true;
        }

        if (ctx.Now < _phaseEnd && ctx.System.Roster.Airborne(ctx.Owner))
            return true;

        Land(ctx);
        return false;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        if (_phase == Phase.Flight)
            ctx.System.Roster.Land(ctx.Owner);

        _phase = Phase.Idle;
    }

    private void Land(in WFAbilityContext ctx)
    {
        ctx.System.Roster.Land(ctx.Owner);
        ctx.System.Play(ctx.Owner, LandSound);
        ctx.System.Frighten(ctx.Owner, ShakeRadius, 1f, Loc.GetString("wf-remade-elite-pounce-land", ("mob", ctx.Owner)));
        foreach (var prey in ctx.System.Prey(ctx.Owner, Radius))
        {
            Hits++;
            ctx.System.Roster.Strike(ctx.Owner, prey, Damage);
            ctx.System.Knockdown(prey, KnockdownTime);
            ctx.System.Popup(Loc.GetString("wf-remade-elite-pounce-hit", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
        }
    }
}
