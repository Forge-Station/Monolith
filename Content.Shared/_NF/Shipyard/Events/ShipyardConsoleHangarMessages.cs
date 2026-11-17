using Robust.Shared.Serialization;

namespace Content.Shared._NF.Shipyard.Events;

[Serializable, NetSerializable]
public sealed class ShipyardConsoleHangarRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ShipyardConsoleHangarStoreMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ShipyardConsoleHangarRetrieveMessage(Guid vesselId) : BoundUserInterfaceMessage
{
    public Guid VesselId { get; } = vesselId;
}
