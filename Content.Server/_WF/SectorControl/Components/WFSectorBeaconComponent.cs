using Content.Shared._Mono.Company;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl.Components;

/// <summary>
/// A placed beacon that claims the sector cell it stands in. A player beacon is destroyed by player fire and the
/// cell loses its colour with it. An admin or NPC beacon ignores damage, and a player beacon cannot paint its cell.
/// </summary>
[RegisterComponent]
public sealed partial class WFSectorBeaconComponent : Component
{
    /// <summary>Admin and NPC beacons. They take no damage, and only an admin can switch one on.</summary>
    [DataField]
    public bool Immortal;

    /// <summary>Claims its cell when the round is underway. Mapped NPC beacons set this and <see cref="Company"/>.</summary>
    [DataField]
    public bool AutoActivate;

    /// <summary>The company a mapped beacon claims for. A player beacon takes its activator's company.</summary>
    [DataField]
    public ProtoId<CompanyPrototype>? Company;

    /// <summary>Whether this beacon currently holds its cell.</summary>
    [ViewVariables]
    public bool Active;

    /// <summary>Set after an automatic activation was attempted this round, so a refused cell is not retried.</summary>
    [ViewVariables]
    public bool AutoTried;

    /// <summary>The cell it holds, while <see cref="Active"/>.</summary>
    [ViewVariables]
    public WFSectorCell? Cell;

    /// <summary>The faction it claimed for.</summary>
    [ViewVariables]
    public ProtoId<WFSectorFactionPrototype>? Faction;
}
