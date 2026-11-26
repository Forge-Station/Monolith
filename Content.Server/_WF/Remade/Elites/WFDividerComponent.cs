using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Elites;

/// <summary>A divider: each limb that comes off, and each one left when it dies, crawls away as a mob of its own.</summary>
[RegisterComponent]
public sealed partial class WFDividerComponent : Component
{
    /// <summary>What each limb becomes, by limb id.</summary>
    [DataField(required: true)]
    public Dictionary<string, EntProtoId> Parts = new();

    /// <summary>Tiles a part is flung from the body.</summary>
    [DataField]
    public float Fling = 2.5f;

    [DataField]
    public SoundSpecifier? SplitSound;

    /// <summary>Parts it has let go of.</summary>
    [ViewVariables]
    public List<EntityUid> Released = new();
}
