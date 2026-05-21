using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportConsoleComponent : Component
{
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

    [ViewVariables, AutoNetworkedField]
    public int SelectedPlatformSlot;

    [ViewVariables, AutoNetworkedField]
    public List<NetCoordinates?> PlatformLandings = new();

    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool UseSharedLandingZone = true;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan? LockEstablishedAt;
}
