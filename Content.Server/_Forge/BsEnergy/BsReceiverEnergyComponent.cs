using Content.Shared._Forge.BsEnergy;
using Robust.Shared.Audio;

namespace Content.Server._Forge.BsEnergy;

[RegisterComponent]
public sealed partial class BsReceiverEnergyComponent : Component
{
    [ViewVariables]
    public EntityUid ConnectedTransmitter;

    [ViewVariables]
    public bool OldEnableState;

    [DataField]
    public bool Enabled;

    [DataField]
    public float Money;

    [DataField]
    public int RequestedPower;

    [DataField]
    public int ReceivedPower;

    [DataField("stepSize")]
    public int StepSize { get; private set; } = 50 * BsEnergySettings.KvtConst;

    [DataField("maxValue")]
    public int MaxValue { get; private set; } = 1 * BsEnergySettings.MvtConst;

    [DataField("sound")]
    public SoundPathSpecifier SoundOnWithdraw = new("/Audio/Effects/Cargo/ping.ogg");

    [DataField("soundClick")]
    public SoundPathSpecifier SoundClick = new("/Audio/_FarHorizons/Machines/relay_click.ogg");
}
