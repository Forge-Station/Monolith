using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Popups;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A roar that marks the mob and hardens every other Remade within sight of it: it takes less damage, and they take less
/// still. It moves faster while it lasts. It ends on its own, or once nothing stands against it for a while. Numbers from
/// DS13-1.0's hunter taunt.
/// </summary>
public sealed partial class WFTauntAbility : WFAbility
{
    [DataField]
    public float Duration = 20f;

    /// <summary>How far the taunt reaches allies.</summary>
    [DataField]
    public float Radius = 10f;

    /// <summary>The share of damage the taunter takes.</summary>
    [DataField]
    public float SelfMultiplier = 0.85f;

    /// <summary>The share of damage its allies take.</summary>
    [DataField]
    public float AllyMultiplier = 0.7f;

    /// <summary>The taunter's speed as a multiple of normal while it lasts.</summary>
    [DataField]
    public float Speed = 1.2f;

    /// <summary>Seconds it can go without a living target in sight before it ends.</summary>
    [DataField]
    public float Patience = 6f;

    /// <summary>Times it has been used.</summary>
    [ViewVariables]
    public int Taunts;

    private bool _active;
    private TimeSpan _end;
    private TimeSpan _lastSeen;
    private TimeSpan _nextPulse;

    public WFTauntAbility()
    {
        Cooldown = 30f;
        Range = 12f;
        Needs.Add(WFRemadeLimbStat.Voice);
    }

    public override float SpeedModifier => _active ? Speed : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out _))
            return WFAbilityResult.None;

        Taunts++;
        _active = true;
        _end = ctx.Now + TimeSpan.FromSeconds(Duration);
        _lastSeen = ctx.Now;
        _nextPulse = ctx.Now;
        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Frighten(ctx.Owner, Radius, 2f, Loc.GetString("wf-remade-elite-taunt-hear", ("mob", ctx.Owner)));
        ctx.System.Popup(Loc.GetString("wf-remade-elite-taunt", ("mob", ctx.Owner)), ctx.Owner, PopupType.LargeCaution);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (ctx.Now >= _end)
            return false;

        if (ctx.Now < _nextPulse)
            return true;

        _nextPulse = ctx.Now + TimeSpan.FromSeconds(1);
        if (ctx.System.Prey(ctx.Owner, Range).Count > 0)
            _lastSeen = ctx.Now;
        else if ((ctx.Now - _lastSeen).TotalSeconds >= Patience)
            return false;

        ctx.System.Taunt.Buff(ctx.Owner, SelfMultiplier, 2f);
        foreach (var ally in ctx.System.Allies(ctx.Owner, Radius))
        {
            ctx.System.Taunt.Buff(ally, AllyMultiplier, 2f);
        }

        return true;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        _active = false;
        ctx.System.Taunt.Clear(ctx.Owner);
    }
}
