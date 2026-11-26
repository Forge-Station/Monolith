using JetBrains.Annotations;
using Content.Shared._WF.Remade.Limbs;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>What an ability did when asked to start.</summary>
public enum WFAbilityResult : byte
{
    /// <summary>It didn't start.</summary>
    None,

    /// <summary>It happened at once.</summary>
    Done,

    /// <summary>It's under way and gets <see cref="WFAbility.Update"/> until it ends.</summary>
    Running,
}

/// <summary>Why an ability is being asked to react.</summary>
public enum WFAbilityTrigger : byte
{
    /// <summary>The mob was hurt by something that wasn't beside it.</summary>
    Shot,

    /// <summary>A gun was fired along a line that passes close to the mob.</summary>
    NearMiss,
}

/// <summary>What an ability gets to work with.</summary>
/// <param name="Sighted">The mob's target is new this tick: it just saw it.</param>
public readonly record struct WFAbilityContext(WFAbilitySystem System, EntityUid Owner, EntityUid? Target, bool Sighted, TimeSpan Now);

/// <summary>
/// One thing a Remade does by itself. Subclass it, add data fields, and write what it does; a mob lists its abilities
/// in YAML on <see cref="WFRemadeAbilitiesComponent"/>. One runs at a time, and each runs on its own cooldown.
/// </summary>
[ImplicitDataDefinitionForInheritors, MeansImplicitUse]
public abstract partial class WFAbility
{
    /// <summary>Seconds after it starts (or ends, for one that runs) before it can start again.</summary>
    [DataField]
    public float Cooldown = 10f;

    /// <summary>The target must be at least this far for it to start.</summary>
    [DataField]
    public float MinRange;

    /// <summary>The target must be no further than this for it to start.</summary>
    [DataField]
    public float Range = 12f;

    /// <summary>The sound it makes when it starts.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Limb stats it needs above 0, such as Voice for a shout.</summary>
    [DataField]
    public List<WFRemadeLimbStat> Needs = new();

    [ViewVariables]
    public TimeSpan NextUse;

    /// <summary>What it does to the mob's movement speed while it runs.</summary>
    public virtual float SpeedModifier => 1f;

    /// <summary>Called each tick the mob has a target and nothing is running. Return what happened.</summary>
    public virtual WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        return WFAbilityResult.None;
    }

    /// <summary>Called when something happens to the mob. Return what happened.</summary>
    public virtual WFAbilityResult React(in WFAbilityContext ctx, WFAbilityTrigger trigger, EntityUid? origin)
    {
        return WFAbilityResult.None;
    }

    /// <summary>Called each tick while running. Return false when it's over.</summary>
    public virtual bool Update(in WFAbilityContext ctx, float frameTime)
    {
        return false;
    }

    /// <summary>Called when it ends or is cut short: the mob died, lost its target or lost the ability.</summary>
    public virtual void Stop(in WFAbilityContext ctx)
    {
    }

    /// <summary>Whether the target is in the range this ability starts at.</summary>
    protected bool InRange(in WFAbilityContext ctx, out EntityUid target)
    {
        target = default;
        if (ctx.Target is not { } found || !ctx.System.IsLive(found))
            return false;

        target = found;
        var distance = ctx.System.Distance(ctx.Owner, found);
        return distance >= MinRange && distance <= Range;
    }
}
