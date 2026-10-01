using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Wolfmed.Body;

/// <summary>
/// Answers a body part's lowest destruction threshold from its Destructible triggers, for the ambient per-part
/// ceiling (M1b, plan §6.1). Only plain damage and damage-type triggers count; they are all a limb carries.
/// </summary>
public sealed class WolfmedPartCeilingSystem : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestructibleComponent, WolfmedPartDestructionThresholdEvent>(OnThreshold);
    }

    private void OnThreshold(Entity<DestructibleComponent> part, ref WolfmedPartDestructionThresholdEvent args)
    {
        foreach (var threshold in part.Comp.Thresholds)
        {
            int? damage = threshold.Trigger switch
            {
                DamageTypeTrigger typed => typed.Damage,
                DamageTrigger total => total.Damage,
                _ => null,
            };

            if (damage is not { } value || value <= 0)
                continue;

            var candidate = FixedPoint2.New(value);
            if (args.Threshold is not { } lowest || candidate < lowest)
                args.Threshold = candidate;
        }
    }
}
