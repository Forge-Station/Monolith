using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportConsoleComponent : Component
{
    /// <summary>
    /// Bluespace engine on the same grid that supplies range and lock parameters.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkedEngine;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? TargetGrid;

    [ViewVariables, AutoNetworkedField]
    public EntityCoordinates? LandingCoordinates;

    [ViewVariables, AutoNetworkedField]
    public BoardingTeleportPage Page = BoardingTeleportPage.Sector;

    [ViewVariables, AutoNetworkedField]
    public BoardingTeleportStatus Status = BoardingTeleportStatus.None;

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public BoardingTeleportInsertionMode Mode = BoardingTeleportInsertionMode.Stealth;
}
