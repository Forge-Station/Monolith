using Robust.Shared.Serialization;

namespace Content.Shared.Power
{
    [Serializable, NetSerializable]
    public enum ChargeState : byte
    {
        Still = 0,
        Charging = 1,
        Discharging = 2,
    }

    [Serializable, NetSerializable]
    public enum PowerWireActionKey : byte
    {
        Key,
        Status,
        Pulsed,
        Electrified,
        PulseCancel,
        ElectrifiedCancel,
        MainWire,
        WireCount,
        CutWires
    }

    [Serializable, NetSerializable]
    public enum CableType
    {
        HighVoltage,
        MediumVoltage,
        Apc,
        // #Forge-change START (KIAS): DATA получает отдельный enum; значения существующих силовых типов сохраняются.
        Data,
        // #Forge-change END (KIAS)
    }
}
