using Content.Shared._WF.Remade.Limbs;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>A cry the moment it first sees a target. Everyone living nearby shakes and has their view kicked.</summary>
public sealed partial class WFShoutAbility : WFAbility
{
    /// <summary>How far the cry shakes people.</summary>
    [DataField]
    public float Radius = 8f;

    /// <summary>How long people shake at the edge. Closer is longer.</summary>
    [DataField]
    public float ShakeTime = 3f;

    public WFShoutAbility()
    {
        Cooldown = 8f;
        Range = 20f;
        Needs.Add(WFRemadeLimbStat.Voice);
    }

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!ctx.Sighted || !InRange(ctx, out _))
            return WFAbilityResult.None;

        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Frighten(ctx.Owner, Radius, ShakeTime, Loc.GetString("wf-remade-shout-hear", ("mob", ctx.Owner)));
        return WFAbilityResult.Done;
    }
}
