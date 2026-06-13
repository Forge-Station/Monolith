using Content.Shared._NF.Shipyard;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Shipyard.Events;

[Serializable, NetSerializable]
public sealed class ShipyardConsoleStoreHangarMessage : BoundUserInterfaceMessage
{
    public readonly ShipyardConsoleUiKey UiKey;

    public ShipyardConsoleStoreHangarMessage(ShipyardConsoleUiKey uiKey)
    {
        UiKey = uiKey;
    }
}

[Serializable, NetSerializable]
public sealed class ShipyardConsoleRetrieveHangarMessage : BoundUserInterfaceMessage
{
    public readonly Guid VesselGuid;
    public readonly ShipyardConsoleUiKey UiKey;

    public ShipyardConsoleRetrieveHangarMessage(Guid vesselGuid, ShipyardConsoleUiKey uiKey)
    {
        VesselGuid = vesselGuid;
        UiKey = uiKey;
    }
}
