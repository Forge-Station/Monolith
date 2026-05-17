using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.Events;

[Serializable, NetSerializable]
public sealed class PoiCaptureTransferOwnershipMessage : BoundUserInterfaceMessage
{
    public string CompanyId = string.Empty;
}
