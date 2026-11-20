using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Gifts;

/// <summary>A chip of a Scion: a power cell that never runs down. Left still once its Scion has reached Growth, it takes root.</summary>
[RegisterComponent]
public sealed partial class WFMonolithShardComponent : Component
{
    /// <summary>The Scion it was cut from. When that is gone, so is the shard.</summary>
    [ViewVariables]
    public EntityUid? Parent;

    /// <summary>Rooted in corruption: it can't be lifted or pulled until it is cut free.</summary>
    [ViewVariables]
    public bool Deployed;

    [ViewVariables]
    public TimeSpan StillSince;

    [ViewVariables]
    public EntityCoordinates Last;

    /// <summary>How long it must lie still before it roots.</summary>
    [DataField]
    public TimeSpan DeployAfter = TimeSpan.FromMinutes(2);

    /// <summary>Seconds it takes to cut a rooted shard free.</summary>
    [DataField]
    public float CutSeconds = 6f;
}
