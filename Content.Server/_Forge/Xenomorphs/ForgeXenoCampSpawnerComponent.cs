using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Xenomorphs;

/// <summary>
/// Drops a small hand-authored cluster, then removes itself. Used by the hive expedition generator.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeXenoCampSpawnerComponent : Component
{
    [DataField(required: true)]
    public List<ForgeXenoCampPiece> Pieces = new();
}

[DataDefinition]
public sealed partial class ForgeXenoCampPiece
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField]
    public Vector2i Offset;
}
