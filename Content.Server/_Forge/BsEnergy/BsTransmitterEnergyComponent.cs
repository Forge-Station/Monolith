using Content.Shared._Forge.BsEnergy;
using Robust.Shared.Audio;

namespace Content.Server._Forge.BsEnergy;

[RegisterComponent]
public sealed partial class BsTransmitterEnergyComponent : Component
{
    [ViewVariables]
    public List<EntityUid> Receivers = [];

    [ViewVariables]
    public float LastDrawnPower;

    [ViewVariables]
    public int AvailablePower;

    [ViewVariables]
    public float Income;

    [ViewVariables]
    public readonly Dictionary<NetEntity, UpdateHistoryData> HistoryData = [];

    [DataField]
    public bool Enabled;

    [DataField]
    public int TargetPower;

    [DataField]
    public int Price;

    [DataField]
    public float Money;

    [DataField]
    public int ConnectingLimit;

    [DataField("enablePassiveIncome")]
    public bool EnablePassiveIncome { get; private set; }

    [DataField("stepSize")]
    public int StepSize { get; private set; } = 50 * BsEnergySettings.KvtConst;

    [DataField("maxValue")]
    public int MaxValue { get; private set; } = 1 * BsEnergySettings.MvtConst;

    [DataField("maxConnected")]
    public int MaxConnected { get; private set; } = 5;

    [DataField("sound")]
    public SoundPathSpecifier SoundOnWithdraw = new("/Audio/Effects/Cargo/ping.ogg");

    [DataField("soundClick")]
    public SoundPathSpecifier SoundClick = new("/Audio/_FarHorizons/Machines/relay_click.ogg");
}
