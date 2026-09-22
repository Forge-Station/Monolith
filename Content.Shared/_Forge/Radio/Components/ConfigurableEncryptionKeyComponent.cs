using Content.Shared.Radio;
using Content.Shared._Forge.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Radio.Components;

[RegisterComponent]
public sealed partial class ConfigurableEncryptionKeyComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int Frequency = 1330;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int MinFrequency = RadioFrequencyPresetListPrototype.DefaultMinFrequency;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int MaxFrequency = RadioFrequencyPresetListPrototype.DefaultMaxFrequency;

    [DataField(customTypeSerializer: typeof(ProtoId<RadioChannelPrototype>))]
    [ViewVariables(VVAccess.ReadWrite)]
    public string Channel = "Handheld";
}
