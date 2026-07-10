using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Weapons;

[RegisterComponent, NetworkedComponent]
public sealed partial class SpeciesWieldComponent : Component
{
    [DataField("exemptSpecies")]
    public HashSet<ProtoId<SpeciesPrototype>> ExemptSpecies = [];
}
