using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Shipyard.Components;

/// <summary>
/// Blueprint item that unlocks fabrication of a specific vessel at a ship fabricator.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VesselBlueprintComponent : Component
{
    [DataField(required: true)]
    public ProtoId<VesselPrototype> Vessel;
}
