using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lattice;

/// <summary>Grounds a person against the Unmooring for a while: a share of the Drift they feel is kept off, and their head clears.</summary>
public sealed partial class WFGround : EntityEffect
{
    /// <summary>How long each tick keeps them grounded.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);

    /// <summary>Share of the Drift they feel kept off.</summary>
    [DataField]
    public float Share = 0.5f;

    /// <summary>Drift taken out of their head each tick.</summary>
    [DataField]
    public float Clear = 1.5f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("wf-reagent-effect-guidebook-ground", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var ev = new WFGroundEvent(args.TargetEntity, Duration, Share, Clear);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref ev, true);
    }
}

/// <summary>Raised when a grounding drug works on someone.</summary>
[ByRefEvent]
public readonly record struct WFGroundEvent(EntityUid Target, TimeSpan Duration, float Share, float Clear);
