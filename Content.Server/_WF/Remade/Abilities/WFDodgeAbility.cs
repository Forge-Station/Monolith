using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>A quick step sideways when hurt from range, or when a shot goes past close by, out of the line of fire. <see cref="WFAbility.Range"/> is the furthest shooter it answers to.</summary>
public sealed partial class WFDodgeAbility : WFAbility
{
    /// <summary>How far the step takes it, in tiles.</summary>
    [DataField]
    public float Distance = 1.5f;

    public WFDodgeAbility()
    {
        Cooldown = 20f;
        MinRange = 2.5f;
        Range = 30f;
    }

    public override WFAbilityResult React(in WFAbilityContext ctx, WFAbilityTrigger trigger, EntityUid? origin)
    {
        if (trigger is not (WFAbilityTrigger.Shot or WFAbilityTrigger.NearMiss) || origin is not { } shooter)
            return WFAbilityResult.None;

        var range = ctx.System.Distance(ctx.Owner, shooter);
        if (range < MinRange || range > Range || !ctx.System.Sidestep(ctx.Owner, shooter, Distance))
            return WFAbilityResult.None;

        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Popup(Loc.GetString("wf-remade-dodge", ("mob", ctx.Owner)), ctx.Owner);
        return WFAbilityResult.Done;
    }
}
