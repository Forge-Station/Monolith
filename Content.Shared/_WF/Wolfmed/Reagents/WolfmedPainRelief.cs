using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// Gives a reagent a painkiller tier and a strength in pain units. Refreshed every metabolism tick, so the
/// dose lasts <see cref="Duration"/> past the last tick and then lapses on its own.
/// </summary>
/// <remarks>
/// This is the whole per-reagent side of the painkiller model: the tier says what the drug is allowed to
/// lift, the strength says how much pain it discounts, and sedation is the price a strong one charges.
/// </remarks>
public sealed partial class WolfmedPainRelief : EntityEffect
{
    /// <summary>What this drug may lift. See <see cref="WolfmedPainReliefTier"/>.</summary>
    [DataField(required: true)]
    public WolfmedPainReliefTier Tier;

    /// <summary>Pain units discounted while the dose is active.</summary>
    [DataField]
    public float Strength;

    /// <summary>
    /// How long one application lasts. For an emergency pen this is the whole window, taken once; for every
    /// other tier it is a rolling refresh, so it only has to outlast the metabolism tick.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(4);

    /// <summary>
    /// M2 (plan §3.4): sedation this reagent aims for per unit in the blood. The body's sedation moves toward the
    /// sum over every sedating reagent, so a steady dose levels off instead of climbing for as long as it lasts.
    /// </summary>
    [DataField]
    public float SedationPerUnit;

    /// <summary>Dose key. Defaults to the reagent id, so one reagent never stacks with itself.</summary>
    [DataField]
    public string Identifier = string.Empty;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wolfmed-pain-relief",
            ("chance", Probability),
            ("tier", Loc.GetString($"wolfmed-pain-relief-tier-{Tier.ToString().ToLowerInvariant()}")),
            ("strength", Strength));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var key = Identifier;
        var units = 0f;
        if (args is EntityEffectReagentArgs { Reagent: { } reagent } reagentArgs)
        {
            if (key.Length == 0)
                key = reagent.ID;

            // The units still in the blood at this tick, not the ones being metabolised: the target falls as the
            // dose is used up.
            units = reagentArgs.Source?.GetTotalPrototypeQuantity(reagent.ID).Float() ?? reagentArgs.Quantity.Float();
        }

        if (key.Length == 0)
            key = Tier.ToString();

        args.EntityManager.System<WolfmedPainReliefSystem>()
            .AddDose(args.TargetEntity, key, Tier, Strength, Duration, units * SedationPerUnit);
    }
}

/// <summary>
/// M2 (OD14): an opioid antagonist. Every unit metabolised takes <see cref="SedationReversePerUnit"/> off the body's
/// sedation, and while it is in the blood the sedating painkillers cannot pull sedation back up.
/// </summary>
public sealed partial class WolfmedReverseSedation : EntityEffect
{
    /// <summary>Sedation removed per unit metabolised.</summary>
    [DataField]
    public float SedationReversePerUnit = 0.3f;

    /// <summary>How long one metabolism tick holds the sedation target at zero. A rolling refresh.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(4);

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wolfmed-reverse-sedation", ("chance", Probability),
            ("amount", MathF.Round(SedationReversePerUnit * 100f)));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var units = args is EntityEffectReagentArgs reagentArgs ? reagentArgs.Quantity.Float() : 1f;
        args.EntityManager.System<WolfmedPainReliefSystem>()
            .ReverseSedation(args.TargetEntity, units * SedationReversePerUnit, Duration);
    }
}
