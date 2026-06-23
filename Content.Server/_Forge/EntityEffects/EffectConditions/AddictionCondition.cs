using Content.Shared._Forge.Chemistry.Addiction;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Chemistry.Addiction;

/// <summary>
/// Condition that checks whether a mob's tolerance for a given addiction
/// falls within [Min, Max]. If the mob has no addiction at all, tolerance is treated as 0.0.
/// </summary>
[UsedImplicitly]
public sealed partial class AddictionCondition : EntityEffectCondition
{
    [DataField(required: true)]
    public string AddictionId { get; set; } = default!;

    [DataField]
    public float Min { get; set; } = 0f;

    [DataField]
    public float Max { get; set; } = 1f;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        var tolerance = 0f;

        if (args.EntityManager.TryGetComponent<AddictionComponent>(args.TargetEntity, out var comp)
            && comp.Addictions.TryGetValue(AddictionId, out var data))
        {
            tolerance = data.Tolerance;
        }

        return tolerance >= Min && tolerance <= Max;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
        => Loc.GetString("addiction-condition-guidebook");
}
