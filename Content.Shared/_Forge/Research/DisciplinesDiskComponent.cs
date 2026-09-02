using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared.Research.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DisciplinesDiskComponent : Component
{
    [AutoNetworkedField]
    [DataField(customTypeSerializer: typeof(ProtoId<TechDisciplinePrototype>))]
    public List<string> Disciplines = new();

    [AutoNetworkedField]
    [DataField(customTypeSerializer: typeof(ProtoId<TechnologyPrototype>))]
    public List<string> UnlockedTechnologies = new();
}
