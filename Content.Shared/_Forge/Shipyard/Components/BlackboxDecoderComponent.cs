using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Shipyard.Components;

[RegisterComponent]
public sealed partial class BlackboxDecoderComponent : Component
{
    [DataField]
    public TimeSpan DecodeTime = TimeSpan.FromSeconds(45);

    [DataField]
    public List<EntProtoId> T1Outputs = new();

    [DataField]
    public List<EntProtoId> T2Outputs = new();

    [ViewVariables]
    public bool Decoding;

    [ViewVariables]
    public TimeSpan DecodeEndTime;
}
