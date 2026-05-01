using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Crypto.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class CryptoCoinComponent : Component
{
    [DataField]
    public string MarketId = string.Empty;

    [DataField]
    public double BasePrice;

    [DataField]
    public float GrowthMultiplier = 1f;
}
