using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class ForgeIffTransferListEntry
{
    public string Id = string.Empty;
    public string Label = string.Empty;
}
