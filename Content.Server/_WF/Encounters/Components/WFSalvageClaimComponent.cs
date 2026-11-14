using Robust.Shared.Prototypes;

namespace Content.Server._WF.Encounters.Components;

/// <summary>The papers to an unowned ship. Used aboard it, they register it to whoever holds them.</summary>
[RegisterComponent]
public sealed partial class WFSalvageClaimComponent : Component
{
    [ViewVariables]
    public EntityUid Ship;

    /// <summary>The design the ship was built from.</summary>
    [DataField]
    public string Vessel = string.Empty;

    /// <summary>The share of its worth the ship sells for once claimed.</summary>
    [DataField]
    public float Resale = 0.25f;
}

/// <summary>On a mob that carries a ship's claim: it is left where he dies.</summary>
[RegisterComponent]
public sealed partial class WFSalvageClaimDropComponent : Component
{
    [ViewVariables]
    public EntityUid Ship;

    [DataField]
    public EntProtoId Claim = "WFSalvageClaim";

    [DataField]
    public string Vessel = string.Empty;

    [DataField]
    public float Resale = 0.25f;
}

/// <summary>On a ship that was claimed, not bought: it is worth only a share of what its parts appraise at.</summary>
[RegisterComponent]
public sealed partial class WFSalvagedShipComponent : Component
{
    [DataField]
    public float Resale = 0.25f;
}
