using Robust.Shared.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;
using Content.Server._Forge.Body.Syndromes;

namespace Content.Server._Forge.EntityEffects.Conditions;

public sealed partial class ToleranceCondition : EntityEffectCondition
{
    [DataField(required: true)]
    public string Reagent = string.Empty;

    [DataField]
    public float Min = 0f;

    [DataField]
    public float Max = 1f;

    [DataField]
    public Organ Source = Organ.Body;

    [DataField]
    public ToleranceReadMethod Method = ToleranceReadMethod.Average;

    public override string GuidebookExplanation(IPrototypeManager prototype)
        => Loc.GetString("reagent-condition-tolerance-guidebook");

    public override bool Condition(EntityEffectBaseArgs args)
    {
        var toleranceSys = args.EntityManager.System<ToleranceSystem>();
        var uid = args.TargetEntity;

        var tolerance = Method switch
        {
            ToleranceReadMethod.Average => toleranceSys.GetAverageTolerance(uid, Reagent, Source),
            ToleranceReadMethod.Max => toleranceSys.GetMaxTolerance(uid, Reagent, Source),
            ToleranceReadMethod.Total => toleranceSys.GetTotalTolerance(uid, Reagent, Source),
            ToleranceReadMethod.Direct => toleranceSys.GetTolerance(uid, Reagent),
        };

        return tolerance >= Min && tolerance <= Max;
    }
}

public enum ToleranceReadMethod : byte
{
    Average,
    Max,
    Total,
    Direct,
}
