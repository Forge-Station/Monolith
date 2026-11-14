using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Explosion;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ProjectileLimitHitsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntityUid> IgnoredEntities = new();

    [DataField, AutoNetworkedField]
    public int OriginEntityId;

    [DataField, AutoNetworkedField]
    public int Limit = 1;

    [DataField, AutoNetworkedField]
    public int? ExtraId;
}
