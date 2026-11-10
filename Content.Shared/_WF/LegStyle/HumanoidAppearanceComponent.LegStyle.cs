using Content.Shared._WF.LegStyle;
using Robust.Shared.Prototypes;

namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// The leg style loaded from the profile, null on the species' own legs. Clothing reads its displacement maps.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<LegStylePrototype>? LegStyle;
}
