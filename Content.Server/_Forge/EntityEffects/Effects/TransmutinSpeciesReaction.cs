using System.Linq;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class TransmutinSpeciesReaction : EntityEffect
{
    [DataField]
    public string ProductPrefix = "Transmutin";

    [DataField]
    public string BloodReagent = "Blood";

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs reagentArgs || reagentArgs.Source == null)
            return;

        var proto = IoCManager.Resolve<IPrototypeManager>();
        var source = reagentArgs.Source;

        for (var i = 0; i < source.Contents.Count; i++)
        {
            var (reagent, _) = source.Contents[i];
            if (reagent.Prototype != BloodReagent)
                continue;

            var species = reagent.Data?.OfType<SpeciesData>().FirstOrDefault()?.Species;
            if (string.IsNullOrWhiteSpace(species))
                continue;

            var product = $"{ProductPrefix}{species}";
            if (!proto.HasIndex<ReagentPrototype>(product))
                continue;

            source.RemoveReagent(reagent, reagentArgs.Quantity);
            source.AddReagent(product, reagentArgs.Quantity);
            return;
        }
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-guidebook-transmutin-species-reaction");
    }
}
