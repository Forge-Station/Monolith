using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Shipyard.Components;

[RegisterComponent]
public sealed partial class ShipFabricatorComponent : Component
{
    public const string BlueprintContainerName = "ship_blueprint";
    public const string TargetIdCardSlotId = "ShipFabricator-targetId";

    [ViewVariables]
    public bool Fabricating;

    [ViewVariables]
    public TimeSpan FabricationEndTime;

    [ViewVariables]
    public ProtoId<VesselPrototype>? ActiveVessel;

    /// <summary>
    /// Vessel being built after the schematic is consumed. Cleared when fabrication ends.
    /// </summary>
    [ViewVariables]
    public ProtoId<VesselPrototype>? FabricatingVessel;

    [DataField]
    public EntityWhitelist? BlueprintWhitelist;

    [DataField]
    public float PowerLoadIdle = 2000f;

    [DataField]
    public float PowerLoadActive = 15000f;

    [ViewVariables]
    public Container BlueprintContainer = default!;

    [DataField("targetIdSlot")]
    public ItemSlot TargetIdSlot = new();

    [ViewVariables]
    public EntityUid? FabricatingActor;

    [ViewVariables]
    public EntityUid? FabricatingIdCard;
}
