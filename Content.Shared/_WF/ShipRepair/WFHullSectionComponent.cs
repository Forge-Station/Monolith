using Robust.Shared.GameStates;

namespace Content.Shared._WF.ShipRepair;

/// <summary>A grid split off a hull; the SRD reattaches it to the hull that holds the repair snapshot.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFHullSectionComponent : Component
{
    /// <summary>The hull holding the repair snapshot; a section keeps the tile indices it had there.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Hull;
}
