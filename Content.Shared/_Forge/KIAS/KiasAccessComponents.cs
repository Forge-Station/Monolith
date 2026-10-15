using Robust.Shared.Network;

namespace Content.Shared._Forge.KIAS;

public enum KiasAccessPolicy : byte { OpenUnclaimed, Claimed, Poi, Company, ShipDeed }

[RegisterComponent]
public sealed partial class KiasClaimableComponent : Component;

[RegisterComponent]
public sealed partial class KiasClaimComponent : Component
{
    [DataField]
    public NetUserId? Owner;
}

[RegisterComponent]
public sealed partial class KiasPoiAccessComponent : Component;
