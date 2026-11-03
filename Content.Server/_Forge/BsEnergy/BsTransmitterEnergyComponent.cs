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

    // -1 means the limit was never changed and resolves to MaxConnected.
    // It must stay untouched on spawn: writing it fails UninitializedSaveTest.
    // 0 is a real limit.
    [DataField]
    public int ConnectingLimit = -1;

    public int EffectiveConnectingLimit => ConnectingLimit < 0
        ? MaxConnected
        : Math.Clamp(ConnectingLimit, 0, MaxConnected);

    [DataField]
    public string Password = string.Empty;

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
