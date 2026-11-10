using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>A long wind-up with a shout, then a fast run at the target. Whoever it runs into is knocked down. Numbers from DS13's slasher charge.</summary>
public sealed partial class WFChargeAbility : WFAbility
{
    /// <summary>Seconds it stands and shouts before it runs.</summary>
    [DataField]
    public float WindUp = 1f;

    /// <summary>The longest it runs.</summary>
    [DataField]
    public float Duration = 4f;

    /// <summary>Its speed while running, as a multiple of normal.</summary>
    [DataField]
    public float Speed = 2.2f;

    /// <summary>It hits anything living this close.</summary>
    [DataField]
    public float HitRange = 1.5f;

    [DataField]
    public float KnockdownTime = 2f;

    [DataField]
    public SoundSpecifier? HitSound;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Run,
    }

    /// <summary>Whether the last charge ran into something.</summary>
    [ViewVariables]
    public bool Hit;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private EntityUid _target;

    public override float SpeedModifier => _phase switch
    {
        Phase.WindUp => 0f,
        Phase.Run => Speed,
        _ => 1f,
    };

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WFAbilityResult.None;

        Hit = false;
        _phase = Phase.WindUp;
        _target = target;
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        // The target stays the one it picked even if the mob's own planning drops it while it stands and shouts.
        if (ctx.System.IsDead(_target))
            return false;

        ctx.System.KeepTarget(ctx.Owner, _target);
        if (_phase == Phase.WindUp)
        {
            if (ctx.Now < _phaseEnd)
                return true;

            _phase = Phase.Run;
            _phaseEnd = ctx.Now + TimeSpan.FromSeconds(Duration);
            ctx.System.RefreshSpeed(ctx.Owner);
            return true;
        }

        foreach (var prey in ctx.System.Prey(ctx.Owner, HitRange))
        {
            Hit = true;
            ctx.System.Knockdown(prey, KnockdownTime);
            ctx.System.Play(prey, HitSound);
            ctx.System.Popup(Loc.GetString("wf-remade-charge-hit", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
            return false;
        }

        return ctx.Now < _phaseEnd;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        _phase = Phase.Idle;
    }
}
