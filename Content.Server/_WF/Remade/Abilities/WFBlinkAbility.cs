using System.Numerics;
using Content.Shared.Popups;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>A twitcher jumps a few tiles at once towards its target, or off to one side when shot at from range.</summary>
public sealed partial class WFBlinkAbility : WFAbility
{
    /// <summary>Tiles it jumps towards the target.</summary>
    [DataField]
    public float Distance = 3.2f;

    /// <summary>Tiles it jumps sideways when shot at.</summary>
    [DataField]
    public float EvadeDistance = 2.5f;

    /// <summary>Times it has blinked.</summary>
    [ViewVariables]
    public int Blinks;

    public WFBlinkAbility()
    {
        Cooldown = 6f;
        MinRange = 4f;
        Range = 12f;
    }

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WFAbilityResult.None;

        return Jump(ctx, ctx.System.Toward(ctx.Owner, target), Distance) ? WFAbilityResult.Done : WFAbilityResult.None;
    }

    public override WFAbilityResult React(in WFAbilityContext ctx, WFAbilityTrigger trigger, EntityUid? origin)
    {
        if (trigger != WFAbilityTrigger.Shot || origin is not { } shooter)
            return WFAbilityResult.None;

        var away = -ctx.System.Toward(ctx.Owner, shooter);
        var side = new Vector2(-away.Y, away.X) * (ctx.System.RandomRange(0f, 1f) < 0.5f ? 1f : -1f);
        return Jump(ctx, side, EvadeDistance) ? WFAbilityResult.Done : WFAbilityResult.None;
    }

    private bool Jump(in WFAbilityContext ctx, Vector2 direction, float distance)
    {
        if (!ctx.System.Elite.Blink(ctx.Owner, direction, distance))
            return false;

        Blinks++;
        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Popup(Loc.GetString("wf-remade-elite-blink", ("mob", ctx.Owner)), ctx.Owner);
        return true;
    }
}
