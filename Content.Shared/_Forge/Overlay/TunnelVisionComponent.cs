using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._Forge.Overlay;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TunnelVisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Intensity { get; set; } = 0f;

    [DataField, AutoNetworkedField]
    public float PulseRate { get; set; } = 3f;

    [DataField, AutoNetworkedField]
    public float DarknessAlpha { get; set; } = 0.8f;

    [DataField, AutoNetworkedField]
    public Color Color { get; set; } = Color.Red;
}
