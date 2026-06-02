using System.Numerics;

namespace Content.Shared.StatusEffect;

[RegisterComponent]
public sealed partial class TunnelVisionComponent : Component
{
    [DataField]
    public float OuterRadius = 1.0f;

    [DataField]
    public float InnerRadius = 0.5f;

    [DataField]
    public float DarknessAlpha = 0.8f;

    [DataField]
    public Vector3 Color = new(1f, 0f, 0f);
}
