using Content.Shared._Forge.Shipyard;
using Robust.Shared.Serialization;

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

    // Forge-Change-Start
    public readonly bool HangarEnabled;
    public readonly List<HangarVesselEntry> HangarVessels;
    public readonly bool CanStoreInHangar;
    public readonly bool CanRetrieveFromHangar;
    public readonly Guid? HangarRetrieveVesselId;
    public readonly int HangarSlotsUsed;
    public readonly int HangarSlotsMax;
    // Forge-Change-End

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
        bool hangarEnabled = false,
        List<HangarVesselEntry>? hangarVessels = null,
        bool canStoreInHangar = false,
        bool canRetrieveFromHangar = false,
        Guid? hangarRetrieveVesselId = null,
        int hangarSlotsUsed = 0,
        int hangarSlotsMax = 3)
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
        HangarEnabled = hangarEnabled;
        HangarVessels = hangarVessels ?? new List<HangarVesselEntry>();
        CanStoreInHangar = canStoreInHangar;
        CanRetrieveFromHangar = canRetrieveFromHangar;
        HangarRetrieveVesselId = hangarRetrieveVesselId;
        HangarSlotsUsed = hangarSlotsUsed;
        HangarSlotsMax = hangarSlotsMax;
    }
}
