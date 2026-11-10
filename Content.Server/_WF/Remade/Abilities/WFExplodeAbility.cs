using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Popups;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>A cry, a few seconds of swelling and shaking while it keeps coming, then it bursts. It needs its pustule (<see cref="WFRemadeLimbStat.Burst"/>); lose it mid-fuse and the fuse is cut.</summary>
public sealed partial class WFExplodeAbility : WFAbility
{
    /// <summary>Seconds between the cry and the blast.</summary>
    [DataField]
    public float Fuse = 2.2f;

    /// <summary>How fast it moves while the fuse burns, as a multiple of normal.</summary>
    [DataField]
    public float WalkSpeed = 0.6f;

    /// <summary>Times it has lit the fuse.</summary>
    [ViewVariables]
    public int Lit;

    private bool _burning;
    private TimeSpan _end;

    public WFExplodeAbility()
    {
        Cooldown = 12f;
        Range = 3.5f;
        Needs.Add(WFRemadeLimbStat.Burst);
    }

    public override float SpeedModifier => _burning ? WalkSpeed : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WFAbilityResult.None;

        Lit++;
        _burning = true;
        _end = ctx.Now + TimeSpan.FromSeconds(Fuse);
        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Swell(ctx.Owner, Fuse);
        ctx.System.Popup(Loc.GetString("wf-remade-explode-start", ("mob", ctx.Owner)), ctx.Owner, PopupType.MediumCaution);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (!ctx.System.Roster.Can(ctx.Owner, WFRemadeLimbStat.Burst))
            return false;

        if (ctx.Now < _end)
            return true;

        ctx.System.Exploder.Detonate(ctx.Owner);
        return false;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        _burning = false;
    }
}
