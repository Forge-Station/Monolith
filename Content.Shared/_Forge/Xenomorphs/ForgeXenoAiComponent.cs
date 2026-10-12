using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Combat actions an AI xenomorph will use on its own. Building and resting are not included.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeXenoAiComponent : Component
{
    [DataField]
    public List<EntProtoId> Actions = new();

    /// <summary>How often the caste checks whether to spend an ability.</summary>
    [DataField]
    public float UpdateInterval = 0.5f;

    public List<EntityUid> ActionEntities = new();

    public TimeSpan NextUpdate;
}
