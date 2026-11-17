using Robust.Shared.Serialization;

namespace Content.Shared._Forge.StationRadio;

[Serializable, NetSerializable]
public enum StationRadioFrequencyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum StationRadioFrequencyMode : byte
{
    Listen,
    Music,
    Voice,
}

[Serializable, NetSerializable]
public sealed class StationRadioFrequencyBoundUIState : BoundUserInterfaceState
{
    public int Frequency;
    public int MinFrequency;
    public int MaxFrequency;
    public StationRadioFrequencyMode Mode;
    public bool ReceiverOn;

    public StationRadioFrequencyBoundUIState(
        int frequency,
        int minFrequency,
        int maxFrequency,
        StationRadioFrequencyMode mode,
        bool receiverOn)
    {
        Frequency = frequency;
        MinFrequency = minFrequency;
        MaxFrequency = maxFrequency;
        Mode = mode;
        ReceiverOn = receiverOn;
    }
}

[Serializable, NetSerializable]
public sealed class SelectStationRadioFrequencyMessage : BoundUserInterfaceMessage
{
    public int Frequency;

    public SelectStationRadioFrequencyMessage(int frequency)
    {
        Frequency = frequency;
    }
}

[Serializable, NetSerializable]
public sealed class ToggleStationRadioReceiverMessage : BoundUserInterfaceMessage;
