namespace Content.Server._WF.Remade.Abilities;

/// <summary>Lets a player drive a Remade's abilities by hand: the usual loop only runs them for awake NPCs.</summary>
public sealed partial class WFAbilitySystem
{
    /// <summary>
    /// Starts an ability on a target by hand, if the mob is up, nothing else is running and the ability is off cooldown with its
    /// limbs. An ability that only answers to being shot at or shot past is asked as that.
    /// </summary>
    public bool TryUse(Entity<WFRemadeAbilitiesComponent> ent, WFAbility ability, EntityUid target)
    {
        var now = _timing.CurTime;
        if (ent.Comp.Active != null || IsDead(ent) || now < ability.NextUse || !Allowed(ent, ability))
            return false;

        var ctx = new WFAbilityContext(this, ent, target, true, now);
        var result = ability.TryStart(ctx);
        if (result == WFAbilityResult.None)
            result = ability.React(ctx, WFAbilityTrigger.NearMiss, target);

        return Begin(ent, ability, result, ctx);
    }

    /// <summary>Runs the ability a driven mob has going, and ends it when it's done or the mob is dead.</summary>
    public void UpdateDriven(Entity<WFRemadeAbilitiesComponent> ent, EntityUid? target, float frameTime)
    {
        if (ent.Comp.Active is not { } active)
            return;

        var ctx = new WFAbilityContext(this, ent, target, false, _timing.CurTime);
        if (IsDead(ent) || !active.Update(ctx, frameTime))
            End(ent, ctx);
    }

    /// <summary>Cuts short whatever the mob has going.</summary>
    public void StopActive(Entity<WFRemadeAbilitiesComponent> ent)
    {
        End(ent, new WFAbilityContext(this, ent, null, false, _timing.CurTime));
    }
}
