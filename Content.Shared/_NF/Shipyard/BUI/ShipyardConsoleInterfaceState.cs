using Robust.Shared.Serialization;
using Content.Shared._Forge.Persistence;

namespace Content.Shared._NF.Shipyard.BUI;

[NetSerializable, Serializable]
public sealed class ShipyardConsoleInterfaceState : BoundUserInterfaceState
{
    public int Balance;
    public readonly bool AccessGranted;
    public readonly string? ShipDeedTitle;
    public int ShipSellValue;
    public readonly bool IsTargetIdPresent;
    public readonly byte UiKey;

    public readonly (List<string> available, List<string> unavailable) ShipyardPrototypes;
    public readonly string ShipyardName;
    public readonly bool FreeListings;
    public readonly float SellRate;
    public readonly List<HangarVesselUiEntry> HangarVessels;
    public readonly int HangarMaxSlots;

    public ShipyardConsoleInterfaceState(
        int balance,
        bool accessGranted,
        string? shipDeedTitle,
        int shipSellValue,
        bool isTargetIdPresent,
        byte uiKey,
        (List<string> available, List<string> unavailable) shipyardPrototypes,
        string shipyardName,
        bool freeListings,
        float sellRate,
        List<HangarVesselUiEntry>? hangarVessels = null,
        int hangarMaxSlots = 2)
    {
        Balance = balance;
        AccessGranted = accessGranted;
        ShipDeedTitle = shipDeedTitle;
        ShipSellValue = shipSellValue;
        IsTargetIdPresent = isTargetIdPresent;
        UiKey = uiKey;
        ShipyardPrototypes = shipyardPrototypes;
        ShipyardName = shipyardName;
        FreeListings = freeListings;
        SellRate = sellRate;
        HangarVessels = hangarVessels ?? [];
        HangarMaxSlots = hangarMaxSlots;
    }
}

[NetSerializable, Serializable]
public sealed record HangarVesselUiEntry(
    Guid VesselId,
    string Name,
    string? VesselPrototypeId,
    HangarVesselState State);
