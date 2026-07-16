using Robust.Shared.Serialization;

namespace Content.Shared._NF.Storage;

[Serializable, NetSerializable]
public enum AnchorableStorageVisuals : byte
{
    Anchored,
    Layer,
}
