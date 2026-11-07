using System.Numerics;
using Content.Shared.Nyanotrasen.Kitchen.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Traits.Assorted;

/// <summary>
/// This is used for the fried trait.
/// </summary>
[RegisterComponent, Access(typeof(FriedTraitSystem))]
public sealed partial class FriedTraitComponent : Component
{
    // Which crispiness type to use for visualization
    [DataField(customTypeSerializer: typeof(ProtoId<CrispinessLevelSetPrototype>))]
    public string CrispinessLevelSet = "Crispy";
}
