using Content.Shared._WF.Remade.Limbs;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>Spits acid at a target it can see, in the range band it's given. A wind-up holds it still. Numbers from DS13's spitter shots.</summary>
public sealed partial class WFShootAbility : WFAbility
{
    [DataField(required: true)]
    public EntProtoId Projectile;

    /// <summary>How fast the shot flies, in tiles a second.</summary>
    [DataField]
    public float ProjectileSpeed = 16f;

    /// <summary>Seconds it stands still before it fires. 0 fires on the move.</summary>
    [DataField]
    public float WindUp;

    [DataField]
    public SoundSpecifier? FireSound;

    [ViewVariables]
    public int Shots;

    /// <summary>How far the target was when it last fired.</summary>
    [ViewVariables]
    public float LastRange;

    private bool _winding;
    private TimeSpan _fireAt;
    private EntityUid _target;

    public WFShootAbility()
    {
        Cooldown = 3f;
        Range = 11f;
        Needs.Add(WFRemadeLimbStat.Fire);
    }

    public override float SpeedModifier => _winding ? 0f : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WFAbilityResult.None;

        if (WindUp <= 0f)
        {
            Fire(ctx, target);
            return WFAbilityResult.Done;
        }

        _winding = true;
        _target = target;
        _fireAt = ctx.Now + TimeSpan.FromSeconds(WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (ctx.System.IsDead(_target))
            return false;

        if (ctx.Now < _fireAt)
            return true;

        // Lose the mouth mid wind-up and there's nothing to fire.
        if (ctx.System.Roster.Can(ctx.Owner, WFRemadeLimbStat.Fire))
            Fire(ctx, _target);

        return false;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        _winding = false;
    }

    private void Fire(in WFAbilityContext ctx, EntityUid target)
    {
        var toward = ctx.System.Toward(ctx.Owner, target);
        LastRange = toward.Length();
        Shots++;
        ctx.System.Play(ctx.Owner, FireSound ?? Sound);
        ctx.System.Roster.Fire(ctx.Owner, Projectile, toward, ProjectileSpeed);
    }
}
