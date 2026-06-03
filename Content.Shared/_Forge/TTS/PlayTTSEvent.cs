using Robust.Shared.Serialization;

namespace Content.Shared._Forge.TTS;

[Serializable, NetSerializable]
// ReSharper disable once InconsistentNaming
public sealed class PlayTTSEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public NetEntity? SourceUid { get; }
    public bool IsWhisper { get; }
    public bool IsRadio { get; } // Forge-Change

    public PlayTTSEvent(byte[] data, NetEntity? sourceUid = null, bool isWhisper = false, bool isRadio = false) // Forge-Change
    {
        Data = data;
        SourceUid = sourceUid;
        IsWhisper = isWhisper;
        IsRadio = isRadio; // Forge-Change
    }
}
