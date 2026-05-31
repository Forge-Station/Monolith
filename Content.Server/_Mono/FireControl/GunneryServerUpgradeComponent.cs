namespace Content.Server._Mono.FireControl;

// Forge-Change-full
[RegisterComponent]
public sealed partial class GunneryServerUpgradeComponent : Component
{
    [DataField]
    public GunneryServerTier Tier = GunneryServerTier.Low;
}

public enum GunneryServerTier : byte
{
    Low,
    Medium,
    High,
    Ultra,
    Omega,
}
