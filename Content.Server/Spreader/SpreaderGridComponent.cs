// Mono - file changed
using Content.Shared.Spreader;
using Robust.Shared.Prototypes;

namespace Content.Server.Spreader;

[RegisterComponent]
public sealed partial class SpreaderGridComponent : Component
{
    [DataField]
    public float UpdateAccumulator = 0f;

    [DataField]
    public float UpdateSpacing = 1f;

    /// <summary>
    /// Runtime spread work queue. Rebuilt on grid init; must not be serialized
    /// because it stores live <see cref="Entity{T}"/> handles.
    /// </summary>
    public Dictionary<ProtoId<EdgeSpreaderPrototype>, Queue<Entity<EdgeSpreaderComponent>>> SpreadQueues = new();
}
