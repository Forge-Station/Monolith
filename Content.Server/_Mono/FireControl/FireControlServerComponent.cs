// Copyright Rane (elijahrane@gmail.com) 2025
// All rights reserved. Relicensed under AGPL with permission

namespace Content.Server._Mono.FireControl;

[RegisterComponent]
public sealed partial class FireControlServerComponent : Component
{
    [ViewVariables]
    public EntityUid? ConnectedGrid = null;

    [ViewVariables]
    public HashSet<EntityUid> Controlled = [];

    [ViewVariables]
    public HashSet<EntityUid> Consoles = [];

    [ViewVariables]
    public Dictionary<EntityUid, EntityUid> Leases;

    [ViewVariables, DataField]
    public int ProcessingPower;

    [ViewVariables]
    public int UsedProcessingPower;

    // Forge-Change-Start: MaxWeapons limits active selection/firing, not registration count.
    [ViewVariables, DataField]
    /// <summary>
    /// Maximum weapons that can be selected for firing at once in gunnery consoles.
    /// Registration on the server is limited by <see cref="ProcessingPower"/> only.
    /// </summary>
    public int MaxWeapons = int.MaxValue;
    // Forge-Change-End

    [ViewVariables, DataField]
    public int MaxConsoles = 1;

    [ViewVariables, DataField]
    public bool EnforceMaxConsoles;
}
