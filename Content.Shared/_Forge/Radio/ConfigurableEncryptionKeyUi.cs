using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Radio;

[Serializable, NetSerializable]
public enum ConfigurableEncryptionKeyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ConfigurableEncryptionKeyBoundUIState : BoundUserInterfaceState
{
    public int Frequency;
    public int MinFrequency;
    public int MaxFrequency;

    public ConfigurableEncryptionKeyBoundUIState(int frequency, int minFrequency, int maxFrequency)
    {
        Frequency = frequency;
        MinFrequency = minFrequency;
        MaxFrequency = maxFrequency;
    }
}

[Serializable, NetSerializable]
public sealed class SelectConfigurableEncryptionKeyFrequencyMessage : BoundUserInterfaceMessage
{
    public int Frequency;

    public SelectConfigurableEncryptionKeyFrequencyMessage(int frequency)
    {
        Frequency = frequency;
    }
}
