using Robust.Shared.Prototypes;

namespace Content.Server._WF.Leviathan;

/// <summary>
/// A body of segments that trails behind this entity, each following the one ahead of it.
/// Ported from Goob Station's TailedEntity (space whales), reworked to crawl rather than hang in a line.
/// </summary>
[RegisterComponent]
public sealed partial class WFTailedEntityComponent : Component
{
    /// <summary>Segments behind the head.</summary>
    [DataField]
    public int Amount = 8;

    [DataField(required: true)]
    public EntProtoId Prototype;

    /// <summary>Distance between one segment and the next.</summary>
    [DataField]
    public float Spacing = 0.8f;

    [ViewVariables]
    public List<EntityUid> Segments = new();
}
