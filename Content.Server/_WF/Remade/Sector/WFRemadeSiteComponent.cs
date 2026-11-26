using Content.Shared._WF.Remade.Sector;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Sector;

/// <summary>On the encounter entity of a site: which site it is and the Scion put aboard, once its hull is real.</summary>
[RegisterComponent]
public sealed partial class WFRemadeSiteComponent : Component
{
    [ViewVariables]
    public ProtoId<WFRemadeSitePrototype> Site;

    [ViewVariables]
    public bool Populated;

    [ViewVariables]
    public EntityUid? Scion;
}
