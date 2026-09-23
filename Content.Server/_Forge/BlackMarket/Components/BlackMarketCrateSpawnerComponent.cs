using Content.Shared._Forge.BlackMarket.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.BlackMarket.Components;

/// <summary>
/// Marks a crate machine as a black market cargo lift and holds the next pending delivery.
/// </summary>
[RegisterComponent]
public sealed partial class BlackMarketCrateSpawnerComponent : Component
{
    public BlackMarketPendingCrateDelivery? Pending;
}

public sealed class BlackMarketPendingCrateDelivery
{
    public EntProtoId Crate = default!;
    public LocId DisplayName = string.Empty;
    public List<BlackMarketContractContentEntry> Contents = new();
}
