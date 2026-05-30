using Robust.Shared.GameStates;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class BoardingBluespaceScramblerComponent : Component
{
    [DataField]
    public bool BlockLocks = true;

    [DataField]
    public float ScatterBonus = BoardingTeleportConstants.ScramblerDefaultScatterBonus;

    [DataField]
    public float RiskBonus = BoardingTeleportConstants.ScramblerDefaultRiskBonus;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class BoardingTeleportRemoteComponent : Component;
