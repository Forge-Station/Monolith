using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// A harvested gland. Its actions belong to whoever is hosting it:
/// the xenomorph that grew it, or a person holding it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ForgeXenoOrganComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public List<EntProtoId> Actions = new();

    /// <summary>Spawned action entities. Not the host's copy.</summary>
    public List<EntityUid> ActionEntities = new();

    public EntityUid? Host;
}

/// <summary>
/// Spawns a caste's glands into an internal container. Butchering rolls each one at 5%.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeXenoOrganHostComponent : Component
{
    public const string ContainerId = "xeno_organs";
    public const float ButcherChance = 0.05f;

    [DataField(required: true)]
    public List<EntProtoId> Organs = new();
}
