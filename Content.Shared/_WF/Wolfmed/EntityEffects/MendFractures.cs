using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Reduces the severity of matching fractures on every body part of a wound host.</summary>
public sealed partial class MendFractures : EntityEffect
{
    /// <summary>Fracture wound prototypes to treat. Empty treats every fracture wound.</summary>
    [DataField] public HashSet<ProtoId<WoundPrototype>> Wounds = ["BoneFractureWound"];

    /// <summary>Lowest fracture grade this effect will act on.</summary>
    [DataField] public FractureGrade MinimumGrade = FractureGrade.Hairline;

    /// <summary>Highest fracture grade this effect will act on.</summary>
    [DataField] public FractureGrade MaximumGrade = FractureGrade.Comminuted;

    /// <summary>Severity removed per metabolism tick, before reagent scaling.</summary>
    [DataField] public FixedPoint2 Amount = 1;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        var wounds = Wounds.Count == 0
            ? Loc.GetString("reagent-effect-guidebook-all-fractures")
            : string.Join(", ", Wounds.Select(id =>
                prototype.TryIndex(id, out WoundPrototype? wound) ? Loc.GetString(wound.Name) : id.Id));
        return Loc.GetString("reagent-effect-guidebook-mend-fractures",
            ("chance", Probability),
            ("amount", Amount.Float()),
            ("wounds", wounds),
            ("minimumGrade", Loc.GetString($"fracture-grade-{MinimumGrade.ToString().ToLowerInvariant()}")),
            ("maximumGrade", Loc.GetString($"fracture-grade-{MaximumGrade.ToString().ToLowerInvariant()}")));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        var amount = Amount * scale;
        var body = args.EntityManager.System<SharedBodySystem>();
        var fractures = args.EntityManager.System<WoundFractureSystem>();
        var wounds = args.EntityManager.System<WoundSystem>();

        foreach (var (part, _) in body.GetBodyChildren(args.TargetEntity))
        {
            if (fractures.GetFracture(part) is not { } fracture ||
                Wounds.Count != 0 && !Wounds.Contains(fracture.Comp1.Prototype) ||
                fracture.Comp2.Grade < MinimumGrade ||
                fracture.Comp2.Grade > MaximumGrade)
                continue;

            wounds.ChangeSeverity(fracture.Owner, -amount);
        }
    }
}
